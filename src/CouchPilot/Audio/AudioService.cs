using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CouchPilot.Games;
using CouchPilot.Native;
using Playnite.SDK;
using Playnite.SDK.Models;
using static CouchPilot.Audio.AudioProtocol;
using static CouchPilot.Native.CoreAudio;

namespace CouchPilot.Audio
{
    // An output device, for picking one outside the live mixer (a display preset's audio).
    public class AudioOutputDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Detail { get; set; }
        // speakers, headphones, display
        public string Kind { get; set; }
        public bool Default { get; set; }
    }

    // The PC's sound, the way Windows' volume mixer sees it: the default output and input, and
    // one channel per app playing on the default output. Apps are grouped by executable, so a
    // browser's many sessions are one slider, as in Windows.
    //
    // Two speeds: Rescan enumerates devices and sessions (once a second is plenty), Refresh
    // re-reads volume, mute and level of the channels already found (cheap, many times a second).
    public class AudioService
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan GamePidsMaxAge = TimeSpan.FromSeconds(3);
        private const string SystemSoundsKey = "system";

        private readonly GameControl games;
        private readonly object gate = new object();
        private AudioModel model = new AudioModel();
        private string shape = "";
        private Dictionary<int, Game> gamePids = new Dictionary<int, Game>();
        private DateTime gamePidsAt = DateTime.MinValue;
        private readonly Dictionary<string, string> appPaths = new Dictionary<string, string>();
        private readonly Dictionary<string, byte[]> icons = new Dictionary<string, byte[]>();

        public AudioService(GameControl games)
        {
            this.games = games;
        }

        // Set when a read failed (a device unplugged, a session gone): the next tick should rescan.
        public bool Stale { get; private set; }

        // Reads everything again. True when the channel list changed shape: then the generation
        // moved on, and the phone needs a new STATE.
        public bool Rescan()
        {
            lock (gate)
            {
                AudioModel next;
                try
                {
                    next = Build();
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Reading the PC's audio failed");
                    Stale = true;
                    return false;
                }
                Stale = false;
                var nextShape = Shape(next);
                var changed = nextShape != shape;
                next.Generation = changed ? unchecked((byte)(model.Generation + 1)) : model.Generation;
                model = next;
                shape = nextShape;
                return changed;
            }
        }

        public void Refresh()
        {
            lock (gate)
            {
                foreach (var c in model.Channels)
                {
                    try { Read(c); } catch { Stale = true; }
                }
            }
        }

        public byte[] State() { lock (gate) return AudioProtocol.State(model); }

        public byte[] Volumes() { lock (gate) return AudioProtocol.Volumes(model); }

        public byte[] Levels() { lock (gate) return AudioProtocol.Levels(model); }

        // A command from the phone (VOLUME, MUTE, DEFAULT). Commands for an older generation, or
        // naming a channel or device that isn't there, are dropped: the phone will get the
        // current STATE soon enough. True when the devices changed and a rescan is due.
        public bool Handle(byte[] m)
        {
            lock (gate)
            {
                if (m.Length < 4 || m[1] != model.Generation) return false;
                switch (m[0])
                {
                    case OpSetVolume:
                    {
                        if (m[2] >= model.Channels.Count) return false;
                        var c = model.Channels[m[2]];
                        var level = Math.Min(m[3], (byte)100) / 100f;
                        var context = Guid.Empty;
                        if (c.Endpoint != null) c.Endpoint.SetMasterVolumeLevelScalar(level, ref context);
                        foreach (var s in c.Sessions) s.SetMasterVolume(level, ref context);
                        return false;
                    }
                    case OpSetMute:
                    {
                        if (m[2] >= model.Channels.Count) return false;
                        var c = model.Channels[m[2]];
                        var muted = m[3] != 0;
                        var context = Guid.Empty;
                        if (c.Endpoint != null) c.Endpoint.SetMute(muted, ref context);
                        foreach (var s in c.Sessions) s.SetMute(muted, ref context);
                        return false;
                    }
                    case OpSetDefault:
                    {
                        var list = m[2] == FlowOutput ? model.Outputs : m[2] == FlowInput ? model.Inputs : null;
                        if (list == null || m[3] >= list.Count) return false;
                        SetDefaultEndpoint(list[m[3]].Id);
                        return true;
                    }
                    default:
                        return false;
                }
            }
        }

        public List<AudioOutputDto> Outputs()
        {
            lock (gate)
            {
                var enumerator = CreateEnumerator();
                return Devices(enumerator, EDataFlow.Render, DefaultDevice(enumerator, EDataFlow.Render))
                    .Select(d => new AudioOutputDto
                    {
                        Id = d.Id,
                        Name = d.Name,
                        Detail = d.Detail,
                        Kind = d.Kind == KindHeadphones ? "headphones" : d.Kind == KindDisplay ? "display" : "speakers",
                        Default = d.Default,
                    })
                    .ToList();
            }
        }

        public string DefaultOutputId()
        {
            lock (gate)
            {
                string id = null;
                DefaultDevice(CreateEnumerator(), EDataFlow.Render)?.GetId(out id);
                return id;
            }
        }

        // Makes an active output the default; false if it isn't plugged in, or already is.
        public bool SetDefaultOutput(string deviceId)
        {
            lock (gate)
            {
                var enumerator = CreateEnumerator();
                var outputs = Devices(enumerator, EDataFlow.Render, DefaultDevice(enumerator, EDataFlow.Render));
                var target = outputs.FirstOrDefault(d => d.Id == deviceId);
                if (target == null || target.Default) return false;
                SetDefaultEndpoint(deviceId);
                return true;
            }
        }

        // The app's executable icon as PNG, or null when there is none to show.
        public byte[] AppIcon(string key)
        {
            lock (gate)
            {
                if (icons.TryGetValue(key, out var cached)) return cached;
                if (!appPaths.TryGetValue(key, out var path)) return null;
                byte[] png = null;
                try
                {
                    using (var icon = Icon.ExtractAssociatedIcon(path))
                    using (var bitmap = icon?.ToBitmap())
                    using (var stream = new MemoryStream())
                    {
                        if (bitmap != null)
                        {
                            bitmap.Save(stream, ImageFormat.Png);
                            png = stream.ToArray();
                        }
                    }
                }
                catch { }
                icons[key] = png;
                return png;
            }
        }

        private AudioModel Build()
        {
            var enumerator = CreateEnumerator();
            var output = DefaultDevice(enumerator, EDataFlow.Render);
            var input = DefaultDevice(enumerator, EDataFlow.Capture);
            var next = new AudioModel
            {
                Outputs = Devices(enumerator, EDataFlow.Render, output),
                Inputs = Devices(enumerator, EDataFlow.Capture, input),
            };
            if (output != null) next.Channels.Add(EndpointChannel(output, EDataFlow.Render));
            if (input != null) next.Channels.Add(EndpointChannel(input, EDataFlow.Capture));
            if (output != null) next.Channels.AddRange(AppChannels(output).Take(MaxItems - next.Channels.Count));
            foreach (var c in next.Channels)
            {
                try { Read(c); } catch { }
            }
            return next;
        }

        private static AudioModel.Channel EndpointChannel(IMMDevice device, EDataFlow flow) => new AudioModel.Channel
        {
            Role = flow == EDataFlow.Render ? RoleOutput : RoleInput,
            Name = Describe(device, flow).Name,
            Endpoint = Activate<IAudioEndpointVolume>(device, IID_IAudioEndpointVolume),
            EndpointMeter = Activate<IAudioMeterInformation>(device, IID_IAudioMeterInformation),
        };

        private IEnumerable<AudioModel.Channel> AppChannels(IMMDevice output)
        {
            var running = GamePids();
            var apps = new Dictionary<string, AudioModel.Channel>();
            foreach (var session in Sessions(output))
            {
                if (!apps.TryGetValue(session.Key, out var app))
                {
                    running.TryGetValue(session.Pid, out var game);
                    if (session.Path != null) appPaths[session.Key] = session.Path;
                    app = new AudioModel.Channel
                    {
                        Role = game != null ? RoleGame : session.System ? RoleSystem : RoleApp,
                        Name = game?.Name ?? AppName(session),
                        Key = session.Key,
                        GameId = game?.Id ?? Guid.Empty,
                        HasIcon = session.Path != null,
                    };
                    apps[session.Key] = app;
                }
                app.Sessions.Add((ISimpleAudioVolume)session.Control);
                app.SessionMeters.Add((IAudioMeterInformation)session.Control);
            }

            // The game first, since that's what you came to adjust; system sounds last.
            return apps.Values
                .OrderBy(a => a.Role == RoleGame ? 0 : a.Role == RoleSystem ? 2 : 1)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        private static void Read(AudioModel.Channel c)
        {
            if (c.Endpoint != null)
            {
                c.Endpoint.GetMasterVolumeLevelScalar(out var level);
                c.Endpoint.GetMute(out var muted);
                c.Volume = Percent(level);
                c.Muted = muted;
                c.Peak = Peak(c.EndpointMeter);
                return;
            }
            // Several sessions of one app: the first one's volume, muted only if all are, the loudest level.
            var first = true;
            var allMuted = true;
            double peak = 0;
            foreach (var s in c.Sessions)
            {
                s.GetMute(out var muted);
                allMuted &= muted;
                if (first)
                {
                    s.GetMasterVolume(out var level);
                    c.Volume = Percent(level);
                    first = false;
                }
            }
            foreach (var meter in c.SessionMeters) peak = Math.Max(peak, Peak(meter));
            c.Muted = allMuted;
            c.Peak = peak;
        }

        // What decides whether the phone needs a new STATE: anything in it but volumes and levels.
        private static string Shape(AudioModel m)
        {
            var text = new StringBuilder();
            foreach (var d in m.Outputs.Concat(m.Inputs)) text.Append($"{d.Id}|{d.Name}|{d.Detail}|{d.Kind}|{d.Default}\n");
            text.Append("--\n");
            foreach (var c in m.Channels) text.Append($"{c.Role}|{c.Key}|{c.Name}|{c.HasIcon}|{c.GameId}\n");
            return text.ToString();
        }

        private class SessionEntry
        {
            public IAudioSessionControl2 Control;
            public int Pid;
            public bool System;
            public string Path;
            public string DisplayName;
            public string Key;
        }

        private static IEnumerable<SessionEntry> Sessions(IMMDevice device)
        {
            var manager = Activate<IAudioSessionManager2>(device, IID_IAudioSessionManager2);
            if (manager.GetSessionEnumerator(out var sessions) != 0) yield break;
            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var control) != 0) continue;
                if (control.GetState(out var state) != 0 || state == SessionState.Expired) continue;
                control.GetProcessId(out var pid);
                var system = control.IsSystemSoundsSession() == 0;
                string path = null;
                if (!system)
                {
                    // A session can outlive its process for a while; there's nothing to adjust then.
                    path = Win32.ImagePath(pid);
                    if (path == null && !IsAlive(pid)) continue;
                }
                control.GetDisplayName(out var displayName);
                yield return new SessionEntry
                {
                    Control = control,
                    Pid = pid,
                    System = system,
                    Path = path,
                    DisplayName = displayName,
                    Key = system ? SystemSoundsKey : path != null ? "app-" + Hash(path.ToLowerInvariant()) : "pid-" + pid,
                };
            }
        }

        private static string AppName(SessionEntry session)
        {
            if (session.System) return "Suoni di sistema";
            // Apps can name their session; "@dll,-id" resource references are Windows' own.
            if (!string.IsNullOrWhiteSpace(session.DisplayName) && !session.DisplayName.StartsWith("@")) return session.DisplayName;
            if (session.Path == null) return $"Processo {session.Pid}";
            try
            {
                var description = FileVersionInfo.GetVersionInfo(session.Path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(session.Path);
        }

        private Dictionary<int, Game> GamePids()
        {
            // Finding a game's processes walks every process on the PC: not on every rescan.
            if (DateTime.UtcNow - gamePidsAt > GamePidsMaxAge)
            {
                try { gamePids = games.RunningProcesses(); } catch { gamePids = new Dictionary<int, Game>(); }
                gamePidsAt = DateTime.UtcNow;
            }
            return gamePids;
        }

        private static AudioModel.Device Describe(IMMDevice device, EDataFlow flow)
        {
            device.GetId(out var id);
            string name = null, detail = null;
            uint? formFactor = null;
            if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) == 0)
            {
                name = GetString(store, PKEY_Device_DeviceDesc);
                detail = GetString(store, PKEY_DeviceInterface_FriendlyName);
                formFactor = GetUInt(store, PKEY_AudioEndpoint_FormFactor);
                if (string.IsNullOrWhiteSpace(name)) name = GetString(store, PKEY_Device_FriendlyName);
            }
            return new AudioModel.Device
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? "Dispositivo audio" : name,
                Detail = detail,
                Kind = Kind(formFactor, flow),
            };
        }

        private static byte Kind(uint? formFactor, EDataFlow flow)
        {
            switch (formFactor)
            {
                case FormFactorHeadphones:
                case FormFactorHeadset: return KindHeadphones;
                case FormFactorDigitalDisplay: return KindDisplay;
                case FormFactorMicrophone: return KindMic;
                default: return flow == EDataFlow.Capture ? KindMic : KindSpeakers;
            }
        }

        private static IMMDevice DefaultDevice(IMMDeviceEnumerator enumerator, EDataFlow flow) =>
            enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var device) == 0 ? device : null;

        private static List<AudioModel.Device> Devices(IMMDeviceEnumerator enumerator, EDataFlow flow, IMMDevice current)
        {
            var result = new List<AudioModel.Device>();
            string currentId = null;
            current?.GetId(out currentId);
            if (enumerator.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out var collection) != 0) return result;
            collection.GetCount(out var count);
            for (var i = 0; i < count && result.Count < MaxItems; i++)
            {
                if (collection.Item(i, out var device) != 0) continue;
                var d = Describe(device, flow);
                d.Default = d.Id == currentId;
                result.Add(d);
            }
            return result;
        }

        private static double Peak(IAudioMeterInformation meter)
        {
            try { return meter != null && meter.GetPeakValue(out var peak) == 0 ? peak : 0; } catch { return 0; }
        }

        private static bool IsAlive(int pid)
        {
            try { return !Process.GetProcessById(pid).HasExited; } catch { return false; }
        }

        private static byte Percent(float level) => (byte)Math.Max(0, Math.Min(100, Math.Round(level * 100)));

        private static string Hash(string text)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return BitConverter.ToString(bytes, 0, 6).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
