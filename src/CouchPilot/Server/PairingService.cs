using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Playnite.SDK;
using Playnite.SDK.Data;
using static CouchPilot.Lang;

namespace CouchPilot.Server
{
    // A phone CouchPilot knows: it typed the PIN once, and carries a token in a cookie since.
    public class PairedDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        // SHA-256 of the token, base64. The token itself lives only on the phone.
        public string TokenHash { get; set; }
        public DateTime PairedAt { get; set; }
        public DateTime LastSeenAt { get; set; }
        // What this phone may do, on top of the switches in the settings.
        public bool AllowInput { get; set; } = true;
        public bool AllowCaptures { get; set; } = true;
        // Unlocking the PC from the lock screen: off by default, since it's the most powerful thing
        // a phone can do. Granted per phone in the settings.
        public bool AllowUnlock { get; set; } = false;
    }

    public class PairedDeviceDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Input { get; set; }
        public bool Captures { get; set; }
        public bool Unlock { get; set; }
    }

    // The PIN on the PC's screen while a phone asks to be paired.
    public class PinRequest
    {
        public string Pin { get; set; }
        public string DeviceName { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    // A pairing attempt the PC refused, with a code the phone can act on.
    public class PairingException : Exception
    {
        // wrong-pin: not the PIN on the screen; AttemptsLeft says how many more tries.
        // expired: no PIN is pending (it expired, was refused, or too many wrong tries).
        // too-soon: a PIN was asked for a moment ago; wait before asking again.
        // forgotten: the token a phone kept is no device's any more (removed in the settings).
        public string Code { get; }
        public int AttemptsLeft { get; }

        public PairingException(string code, string message, int attemptsLeft = 0) : base(message)
        {
            Code = code;
            AttemptsLeft = attemptsLeft;
        }
    }

    // Pairing phones with a PIN. Off, every phone on the network is let in, as before. On, a
    // phone has to show a token it got by typing a six-digit PIN that CouchPilot puts on the
    // PC's screen: whoever can read the TV is allowed in, nobody else. Each paired phone can
    // then be allowed, or not, to drive mouse and keyboard and to see the captures.
    //
    // One PIN at a time, good for two minutes and five tries, and a new one at most every few
    // seconds: a million PINs can't be guessed at that pace. The devices are kept in
    // devices.json in the extension's data folder; the token is stored hashed.
    public class PairingService
    {
        public const string CookieName = "couchpilot_device";
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan PinLifetime = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan RequestEvery = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SeenEvery = TimeSpan.FromMinutes(1);
        private const int Attempts = 5;

        private readonly string path;
        private readonly object gate = new object();
        private readonly RandomNumberGenerator random = RandomNumberGenerator.Create();
        private List<PairedDevice> devices;
        private PinRequest pending;
        private int attemptsLeft;
        private DateTime lastRequest = DateTime.MinValue;
        private volatile bool required;
        private int version;

        // The settings' switch. While off, nothing is checked and nothing is asked.
        public bool Required
        {
            get => required;
            set
            {
                if (required == value) return;
                required = value;
                if (!value) CancelPin();
                Bump();
            }
        }

        // Bumped whenever a device, a permission or the switch changes: phones are told the status changed.
        public int Version => version;

        // A device was added, removed or changed, or the switch flipped.
        public event Action Changed;

        // A phone asked for a PIN: show it on the PC. And the PIN is no longer needed: take it down.
        public event Action<PinRequest> PinRequested;
        public event Action PinDone;

        public PairingService(string path)
        {
            this.path = path;
        }

        public IReadOnlyList<PairedDevice> Devices
        {
            get
            {
                lock (gate)
                {
                    Load();
                    return devices.ToList();
                }
            }
        }

        // The device behind a token, or null. Marks it seen, at most once a minute.
        public PairedDevice Authenticate(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var hash = Hash(token);
            lock (gate)
            {
                Load();
                var device = devices.FirstOrDefault(d => d.TokenHash == hash);
                if (device == null) return null;
                var now = DateTime.UtcNow;
                if (now - device.LastSeenAt > SeenEvery)
                {
                    device.LastSeenAt = now;
                    Save();
                }
                return device;
            }
        }

        // A phone wants in: a new PIN goes on the screen (replacing any pending one), for two minutes.
        public PinRequest RequestPin(string deviceName)
        {
            PinRequest request;
            lock (gate)
            {
                var now = DateTime.UtcNow;
                if (now - lastRequest < RequestEvery)
                {
                    throw new PairingException("too-soon", Tr("A PIN just went up on the PC: wait a few seconds.", "Ho appena mostrato un PIN sul PC: aspetta qualche secondo."));
                }
                lastRequest = now;
                request = pending = new PinRequest
                {
                    Pin = NewPin(),
                    DeviceName = Clean(deviceName) ?? Pc("Phone", "Telefono"),
                    ExpiresAt = now + PinLifetime,
                };
                attemptsLeft = Attempts;
            }
            PinRequested?.Invoke(request);
            return request;
        }

        // The phone typed the PIN: right, it's a device from now on, and gets its token.
        public PairedDevice Complete(string pin, string deviceName, out string token)
        {
            PairedDevice device;
            lock (gate)
            {
                if (pending == null || DateTime.UtcNow > pending.ExpiresAt)
                {
                    pending = null;
                    throw new PairingException("expired", Tr("The PIN has expired: ask for another one.", "Il PIN è scaduto: chiedine un altro."));
                }
                if ((pin ?? "").Trim() != pending.Pin)
                {
                    attemptsLeft--;
                    if (attemptsLeft <= 0)
                    {
                        pending = null;
                        PinDone?.Invoke();
                        throw new PairingException("expired", Tr("Too many tries: ask for another PIN.", "Troppi tentativi: chiedi un altro PIN."));
                    }
                    throw new PairingException("wrong-pin", attemptsLeft == 1 ? Tr("Wrong PIN, last try.", "PIN sbagliato, ultimo tentativo.") : Tr($"Wrong PIN, {attemptsLeft} tries left.", $"PIN sbagliato, {attemptsLeft} tentativi rimasti."), attemptsLeft);
                }
                token = NewToken();
                Load();
                var now = DateTime.UtcNow;
                device = new PairedDevice
                {
                    Id = NewId(),
                    Name = Clean(deviceName) ?? pending.DeviceName,
                    TokenHash = Hash(token),
                    PairedAt = now,
                    LastSeenAt = now,
                };
                devices.Add(device);
                pending = null;
                Save();
            }
            PinDone?.Invoke();
            Bump();
            logger.Info($"CouchPilot paired a phone: {device.Name}");
            return device;
        }

        // The PIN on the screen was refused at the PC, or isn't needed any more.
        public void CancelPin()
        {
            bool had;
            lock (gate)
            {
                had = pending != null;
                pending = null;
            }
            if (had) PinDone?.Invoke();
        }

        public PairedDevice Get(string id)
        {
            lock (gate)
            {
                Load();
                return devices.FirstOrDefault(d => d.Id == id);
            }
        }

        public bool Forget(string id)
        {
            lock (gate)
            {
                Load();
                if (devices.RemoveAll(d => d.Id == id) == 0) return false;
                Save();
            }
            Bump();
            return true;
        }

        public bool SetPermissions(string id, bool? input, bool? captures, bool? unlock = null)
        {
            lock (gate)
            {
                var device = Get(id);
                if (device == null) return false;
                if (input != null) device.AllowInput = input.Value;
                if (captures != null) device.AllowCaptures = captures.Value;
                if (unlock != null) device.AllowUnlock = unlock.Value;
                Save();
            }
            Bump();
            return true;
        }

        public bool Rename(string id, string name)
        {
            name = Clean(name);
            if (name == null) return false;
            lock (gate)
            {
                var device = Get(id);
                if (device == null) return false;
                device.Name = name;
                Save();
            }
            Bump();
            return true;
        }

        public static PairedDeviceDto ToDto(PairedDevice d) => d == null ? null : new PairedDeviceDto
        {
            Id = d.Id,
            Name = d.Name,
            Input = d.AllowInput,
            Captures = d.AllowCaptures,
            Unlock = d.AllowUnlock,
        };

        private void Bump()
        {
            version++;
            Changed?.Invoke();
        }

        // Six digits, from the system's random source; never from the clock.
        private string NewPin()
        {
            var bytes = new byte[4];
            random.GetBytes(bytes);
            return (BitConverter.ToUInt32(bytes, 0) % 1000000).ToString("000000");
        }

        private string NewToken()
        {
            var bytes = new byte[32];
            random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private string NewId()
        {
            var bytes = new byte[6];
            random.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private static string Hash(string token)
        {
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
            }
        }

        private static string Clean(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }

        // devices.json, or the copy of it the last save left behind: a file cut short (Playnite
        // killed, the PC switched off mid-write) must not cost every phone its pairing. Neither
        // readable, the file is set aside rather than overwritten by the next save.
        private void Load()
        {
            if (devices != null) return;
            devices = Read(path) ?? Read(path + ".bak");
            if (devices != null) return;
            devices = new List<PairedDevice>();
            if (!File.Exists(path)) return;
            try
            {
                File.Copy(path, path + ".broken", true);
            }
            catch (Exception e)
            {
                logger.Error(e, "Couldn't set the unreadable paired devices aside");
            }
        }

        private static List<PairedDevice> Read(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                return Serialization.FromJson<List<PairedDevice>>(File.ReadAllText(file))?.Where(d => d?.Id != null && d.TokenHash != null).ToList();
            }
            catch (Exception e)
            {
                logger.Error(e, $"Couldn't read the paired devices from {file}");
                return null;
            }
        }

        // Written beside the file and swapped in whole, keeping the previous one as .bak: the file
        // is rewritten every minute a phone is in use, so a write cut short is bound to happen.
        private void Save()
        {
            var temp = path + ".tmp";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(Serialization.ToJson(devices, true));
                using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    // On the disk before the swap, or a power cut could leave the new name on an empty file.
                    file.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
                else File.Move(temp, path);
            }
            catch (Exception e)
            {
                logger.Error(e, "Couldn't save the paired devices");
            }
        }
    }
}
