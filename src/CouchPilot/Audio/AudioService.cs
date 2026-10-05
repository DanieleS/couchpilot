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
using Playnite.SDK.Models;
using static CouchPilot.Native.CoreAudio;

namespace CouchPilot.Audio
{
    public class AudioDeviceDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        // The adapter it hangs off ("NVIDIA High Definition Audio"), to tell twin names apart.
        public string Detail { get; set; }
        // speakers | headphones | display | mic
        public string Kind { get; set; }
    }

    public class AudioEndpointDto : AudioDeviceDto
    {
        public int Volume { get; set; }
        public bool Muted { get; set; }
        public double Peak { get; set; }
    }

    public class AudioAppDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Volume { get; set; }
        public bool Muted { get; set; }
        public double Peak { get; set; }
        public bool System { get; set; }
        // Set when the app is a game Playnite launched.
        public string GameId { get; set; }
        public bool HasIcon { get; set; }
    }

    public class AudioStateDto
    {
        public AudioEndpointDto Output { get; set; }
        public AudioEndpointDto Input { get; set; }
        public List<AudioDeviceDto> Outputs { get; set; }
        public List<AudioDeviceDto> Inputs { get; set; }
        public List<AudioAppDto> Apps { get; set; }
    }

    // The PC's sound, the way Windows' volume mixer sees it: the default output and input,
    // their volume, and one entry per app playing on the default output. Apps are grouped
    // by executable, so a browser's many sessions are one slider, as in Windows.
    public class AudioService
    {
        public const string SystemSoundsId = "system";
        private static readonly TimeSpan GamePidsMaxAge = TimeSpan.FromSeconds(3);

        private readonly GameControl games;
        private readonly object gate = new object();
        private Dictionary<int, Game> gamePids = new Dictionary<int, Game>();
        private DateTime gamePidsAt = DateTime.MinValue;
        private readonly Dictionary<string, string> appPaths = new Dictionary<string, string>();
        private readonly Dictionary<string, byte[]> icons = new Dictionary<string, byte[]>();

        public AudioService(GameControl games)
        {
            this.games = games;
        }

        public AudioStateDto Snapshot()
        {
            lock (gate)
            {
                var enumerator = CreateEnumerator();
                var output = DefaultDevice(enumerator, EDataFlow.Render);
                var input = DefaultDevice(enumerator, EDataFlow.Capture);
                return new AudioStateDto
                {
                    Output = output == null ? null : Endpoint(output, EDataFlow.Render),
                    Input = input == null ? null : Endpoint(input, EDataFlow.Capture),
                    Outputs = Devices(enumerator, EDataFlow.Render).Select(d => Describe(d, EDataFlow.Render)).ToList(),
                    Inputs = Devices(enumerator, EDataFlow.Capture).Select(d => Describe(d, EDataFlow.Capture)).ToList(),
                    Apps = output == null ? new List<AudioAppDto>() : Apps(output),
                };
            }
        }

        // Makes an output or input the Windows default; false if no such active device.
        public bool SetDefault(string deviceId)
        {
            lock (gate)
            {
                if (FindDevice(deviceId) == null) return false;
                SetDefaultEndpoint(deviceId);
                return true;
            }
        }

        public bool SetDevice(string deviceId, double? volume, bool? muted)
        {
            lock (gate)
            {
                var device = FindDevice(deviceId);
                if (device == null) return false;
                var endpoint = Activate<IAudioEndpointVolume>(device, IID_IAudioEndpointVolume);
                var context = Guid.Empty;
                if (volume.HasValue) Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(Level(volume.Value), ref context));
                if (muted.HasValue) Marshal.ThrowExceptionForHR(endpoint.SetMute(muted.Value, ref context));
                return true;
            }
        }

        public bool SetApp(string appId, double? volume, bool? muted)
        {
            lock (gate)
            {
                var output = DefaultDevice(CreateEnumerator(), EDataFlow.Render);
                if (output == null) return false;
                var found = false;
                foreach (var session in Sessions(output).Where(s => s.AppId == appId))
                {
                    var simple = (ISimpleAudioVolume)session.Control;
                    var context = Guid.Empty;
                    if (volume.HasValue) Marshal.ThrowExceptionForHR(simple.SetMasterVolume(Level(volume.Value), ref context));
                    if (muted.HasValue) Marshal.ThrowExceptionForHR(simple.SetMute(muted.Value, ref context));
                    found = true;
                }
                return found;
            }
        }

        // The app's executable icon as PNG, or null when there is none to show.
        public byte[] AppIcon(string appId)
        {
            lock (gate)
            {
                if (icons.TryGetValue(appId, out var cached)) return cached;
                if (!appPaths.TryGetValue(appId, out var path)) return null;
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
                icons[appId] = png;
                return png;
            }
        }

        private List<AudioAppDto> Apps(IMMDevice output)
        {
            var running = GamePids();
            var apps = new Dictionary<string, AudioAppDto>();
            foreach (var session in Sessions(output))
            {
                var simple = (ISimpleAudioVolume)session.Control;
                simple.GetMasterVolume(out var level);
                simple.GetMute(out var muted);
                var peak = Peak((IAudioMeterInformation)session.Control);

                if (apps.TryGetValue(session.AppId, out var app))
                {
                    // Several sessions of one app: show the loudest, and muted only if all are.
                    app.Peak = Math.Max(app.Peak, peak);
                    app.Muted &= muted;
                    continue;
                }

                running.TryGetValue(session.Pid, out var game);
                if (session.Path != null) appPaths[session.AppId] = session.Path;
                apps[session.AppId] = new AudioAppDto
                {
                    Id = session.AppId,
                    Name = game?.Name ?? AppName(session),
                    Volume = Percent(level),
                    Muted = muted,
                    Peak = peak,
                    System = session.System,
                    GameId = game?.Id.ToString(),
                    HasIcon = session.Path != null,
                };
            }

            // The game first, since that's what you came to adjust; system sounds last.
            return apps.Values
                .OrderBy(a => a.GameId != null ? 0 : a.System ? 2 : 1)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private class SessionEntry
        {
            public IAudioSessionControl2 Control;
            public int Pid;
            public bool System;
            public string Path;
            public string DisplayName;
            public string AppId;
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
                    AppId = system ? SystemSoundsId : path != null ? "app-" + Hash(path.ToLowerInvariant()) : "pid-" + pid,
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
            // Finding a game's processes walks every process on the PC: not on every poll.
            if (DateTime.UtcNow - gamePidsAt > GamePidsMaxAge)
            {
                try { gamePids = games.RunningProcesses(); } catch { gamePids = new Dictionary<int, Game>(); }
                gamePidsAt = DateTime.UtcNow;
            }
            return gamePids;
        }

        private static AudioEndpointDto Endpoint(IMMDevice device, EDataFlow flow)
        {
            var info = Describe(device, flow);
            var endpoint = Activate<IAudioEndpointVolume>(device, IID_IAudioEndpointVolume);
            endpoint.GetMasterVolumeLevelScalar(out var level);
            endpoint.GetMute(out var muted);
            return new AudioEndpointDto
            {
                Id = info.Id,
                Name = info.Name,
                Detail = info.Detail,
                Kind = info.Kind,
                Volume = Percent(level),
                Muted = muted,
                Peak = Peak(Activate<IAudioMeterInformation>(device, IID_IAudioMeterInformation)),
            };
        }

        private static AudioDeviceDto Describe(IMMDevice device, EDataFlow flow)
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
            return new AudioDeviceDto
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? "Dispositivo audio" : name,
                Detail = detail,
                Kind = Kind(formFactor, flow),
            };
        }

        private static string Kind(uint? formFactor, EDataFlow flow)
        {
            switch (formFactor)
            {
                case FormFactorHeadphones:
                case FormFactorHeadset: return "headphones";
                case FormFactorDigitalDisplay: return "display";
                case FormFactorMicrophone: return "mic";
                default: return flow == EDataFlow.Capture ? "mic" : "speakers";
            }
        }

        private static IMMDevice DefaultDevice(IMMDeviceEnumerator enumerator, EDataFlow flow) =>
            enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var device) == 0 ? device : null;

        private static List<IMMDevice> Devices(IMMDeviceEnumerator enumerator, EDataFlow flow)
        {
            var result = new List<IMMDevice>();
            if (enumerator.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out var collection) != 0) return result;
            collection.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) == 0) result.Add(device);
            }
            return result;
        }

        // Only active devices: a stale id from the phone must not resurrect an unplugged one.
        private static IMMDevice FindDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;
            var enumerator = CreateEnumerator();
            foreach (var flow in new[] { EDataFlow.Render, EDataFlow.Capture })
            {
                foreach (var device in Devices(enumerator, flow))
                {
                    if (device.GetId(out var id) == 0 && id == deviceId) return device;
                }
            }
            return null;
        }

        private static double Peak(IAudioMeterInformation meter)
        {
            try { return meter.GetPeakValue(out var peak) == 0 ? Math.Round(peak, 3) : 0; } catch { return 0; }
        }

        private static bool IsAlive(int pid)
        {
            try { return !Process.GetProcessById(pid).HasExited; } catch { return false; }
        }

        private static int Percent(float level) => (int)Math.Round(level * 100);

        private static float Level(double percent) => (float)(Math.Max(0, Math.Min(100, percent)) / 100);

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
