using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace CouchPilot.Unlock
{
    public enum UnlockResult
    {
        Ok,          // the provider accepted the handoff; the PC is unlocking
        NotLocked,   // nothing to do, or the provider's pipe isn't up
        Unavailable, // not set up, or the provider didn't answer
        Forbidden,   // (decided at the API layer) this phone may not unlock
        Failed,      // the provider refused (bad secret, busy…)
    }

    // The Playnite-side half of unlocking: it keeps the Windows password (DPAPI-sealed), turns the
    // feature on and off through the elevated setup helper, and, when a paired phone asks and the
    // PC is locked, hands the credentials to the credential provider over the pipe. docs/unlock.md
    // is the whole picture; this file owns the password store and the pipe client.
    public class UnlockService
    {
        private const string PipeName = "CouchPilotUnlock";
        private const int ProtocolVersion = 1;
        // Must match CouchPilotUnlockSetup and the credential provider.
        private const string CredentialProviderKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{B6F4C3A2-7E51-4A90-9C3D-1F8E2A6B5D04}";

        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CouchPilot.Unlock.v1");

        private readonly string storePath;      // unlock.bin, DPAPI-sealed
        private readonly string helperPath;     // CouchPilotUnlockSetup.exe, beside the extension
        private readonly LockState lockState;
        private readonly object gate = new object();

        public UnlockService(string dataDir, string helperPath, LockState lockState)
        {
            storePath = Path.Combine(dataDir, "unlock.bin");
            this.helperPath = helperPath;
            this.lockState = lockState;
        }

        // Raised when the feature is turned on or off, so the settings and the phones refresh.
        public event Action Changed;

        public bool Locked => lockState.Locked;

        // The native setup helper shipped: without it (a plain checkout that skipped the C++
        // build) unlocking can't be offered at all.
        public bool Available => File.Exists(helperPath);

        // Set up and ready: there's a sealed password on file and the provider is registered. If
        // one is missing (an interrupted setup, a manual unregister) the feature is off.
        public bool Enabled => Available && File.Exists(storePath) && ProviderRegistered();

        // The enrolled account, for the settings to show. Never the password.
        public string Account
        {
            get
            {
                var store = TryLoad();
                return store?.Account;
            }
        }

        // The phone may see and press Unlock only when it's set up and the PC is actually locked.
        // (Per-device permission is checked at the API layer.)
        // Checked on every status poll: keep the locked test (a cheap field) first so the registry
        // and file look-ups only happen while the PC is actually locked.
        public bool OfferToPhones => lockState.Locked && Enabled;

        private static bool ProviderRegistered()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(CredentialProviderKey))
                {
                    return key != null;
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "Couldn't read the credential provider's registration");
                return false;
            }
        }

        // Turn it on: generate the shared secret, register the provider (one UAC prompt), and seal
        // the account, the secret and the password together. The password is only read here and
        // never leaves the PC. Returns false with a reason the settings can show.
        public bool Enable(string account, string password, out string error)
        {
            error = null;
            if (!Available)
            {
                error = "The unlock component isn't in this build.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(account) || password == null)
            {
                error = "An account and password are needed.";
                return false;
            }

            var secret = Convert.ToBase64String(RandomBytes(32));
            string sid;
            try
            {
                sid = WindowsIdentity.GetCurrent().User.Value;
            }
            catch (Exception e)
            {
                logger.Error(e, "Couldn't read the current user's SID");
                error = "Couldn't read the Windows account.";
                return false;
            }

            lock (gate)
            {
                // Hand the secret to the elevated helper in a short-lived, user-only file rather
                // than on its command line.
                var secretFile = Path.Combine(Path.GetTempPath(), "couchpilot-unlock-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllText(secretFile, secret, new UTF8Encoding(false));
                    if (!RunHelper("register \"" + sid + "\" \"" + secretFile + "\"", out var code) || code != 0)
                    {
                        error = code == 1223 ? "Setup was cancelled." : "Registering the unlock component failed.";
                        return false;
                    }
                }
                finally
                {
                    try { if (File.Exists(secretFile)) File.Delete(secretFile); } catch { }
                }

                var store = new Store { Account = account.Trim(), Secret = secret, Password = password };
                Save(store);
            }
            Changed?.Invoke();
            return true;
        }

        // Turn it off: unregister the provider (one UAC prompt) and delete the sealed password.
        public bool Disable()
        {
            lock (gate)
            {
                RunHelper("unregister", out _);
                try { if (File.Exists(storePath)) File.Delete(storePath); }
                catch (Exception e) { logger.Error(e, "Couldn't delete the unlock store"); }
            }
            Changed?.Invoke();
            return true;
        }

        // A paired, allowed phone asked to unlock. Decrypt, send to the provider, report the
        // handoff (not the unlock's own success, which shows on the PC).
        public UnlockResult TryUnlock()
        {
            if (!lockState.Locked) return UnlockResult.NotLocked;
            if (!Enabled) return UnlockResult.Unavailable;

            var store = TryLoad();
            if (store == null) return UnlockResult.Unavailable;

            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    try
                    {
                        pipe.Connect(1500);
                    }
                    catch (TimeoutException)
                    {
                        // No pipe: the lock screen (and so the provider) isn't up.
                        return UnlockResult.NotLocked;
                    }
                    pipe.ReadMode = PipeTransmissionMode.Message;

                    // Built by hand so the field names are exactly what the provider parses (v,
                    // secret, user, password), independent of any serializer's casing.
                    var request = "{\"v\":" + ProtocolVersion
                        + ",\"secret\":\"" + JsonEscape(store.Secret) + "\""
                        + ",\"user\":\"" + JsonEscape(store.Account) + "\""
                        + ",\"password\":\"" + JsonEscape(store.Password) + "\"}";
                    var bytes = Encoding.UTF8.GetBytes(request);
                    pipe.Write(bytes, 0, bytes.Length);
                    pipe.Flush();

                    var reply = ReadMessage(pipe) ?? "";
                    // { "ok": true } is the only success; everything else is a refusal.
                    return reply.Replace(" ", "").Contains("\"ok\":true") ? UnlockResult.Ok : UnlockResult.Failed;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Unlock over the pipe failed");
                return UnlockResult.Unavailable;
            }
        }

        private static string ReadMessage(NamedPipeClientStream pipe)
        {
            var buffer = new byte[1024];
            using (var ms = new MemoryStream())
            {
                do
                {
                    int read = pipe.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    ms.Write(buffer, 0, read);
                } while (!pipe.IsMessageComplete);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private bool RunHelper(string arguments, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = helperPath,
                    Arguments = arguments,
                    UseShellExecute = true,   // required for the UAC "runas" verb
                    Verb = "runas",
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                    return true;
                }
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
            {
                // The user dismissed the UAC prompt.
                exitCode = 1223;
                return false;
            }
            catch (Exception e)
            {
                logger.Error(e, "Couldn't run the unlock setup helper");
                return false;
            }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static byte[] RandomBytes(int n)
        {
            var bytes = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        private void Save(Store store)
        {
            var json = Encoding.UTF8.GetBytes(Serialization.ToJson(store));
            var sealed_ = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
            Array.Clear(json, 0, json.Length);
            File.WriteAllBytes(storePath, sealed_);
        }

        private Store TryLoad()
        {
            try
            {
                if (!File.Exists(storePath)) return null;
                var sealed_ = File.ReadAllBytes(storePath);
                var json = ProtectedData.Unprotect(sealed_, Entropy, DataProtectionScope.CurrentUser);
                var store = Serialization.FromJson<Store>(Encoding.UTF8.GetString(json));
                Array.Clear(json, 0, json.Length);
                return store;
            }
            catch (Exception e)
            {
                logger.Error(e, "Couldn't read the unlock store");
                return null;
            }
        }

        // Sealed with DPAPI (CurrentUser): only this Windows account on this PC can read it back.
        private class Store
        {
            public string Account { get; set; }
            public string Secret { get; set; }
            public string Password { get; set; }
        }
    }
}
