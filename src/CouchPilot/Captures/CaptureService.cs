using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Playnite.SDK;
using Key = CouchPilot.Native.Input;

namespace CouchPilot.Captures
{
    public enum CaptureTool { GameBar, Nvidia }

    // A command the PC couldn't carry out, with a code the phone can act on.
    public class CaptureException : Exception
    {
        // capture-off: Game Bar captures are turned off in Windows.
        // replay-off: Game Bar isn't recording in the background, so there's no replay to save.
        // nothing-saved: the shortcut went out but no file came.
        // busy: another command is still waiting for its file.
        public string Code { get; }

        public CaptureException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    public class CaptureStatusDto
    {
        // gamebar or nvidia
        public string Tool { get; set; }
        // When the recording CouchPilot started began, or null.
        public string RecordingSince { get; set; }
        // How much a replay keeps, when the tool says (Game Bar does); null otherwise.
        public int? ReplaySeconds { get; set; }
        // Clips can be trimmed on the PC (the bundled ffmpeg is there).
        public bool Editing { get; set; }
    }

    // Screenshots, recordings and replays, made by the capture tool already on the PC: CouchPilot
    // presses its keyboard shortcuts, as you would, and waits for the file to appear. The game
    // has to be in the foreground, which it is while you play.
    //
    //                 Game Bar           NVIDIA (GeForce Experience, NVIDIA app)
    //   screenshot    Win+Alt+PrtScn     Alt+F1
    //   record        Win+Alt+R          Alt+F9
    //   replay        Win+Alt+G          Alt+F10
    //
    // Recording is a toggle in both: CouchPilot can't ask the tool whether it's recording, so it
    // keeps track of what it started, and forgets it once a recording turns up in the folder
    // (Game Bar stops by itself at its time limit, or when the game closes).
    public class CaptureService
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan ScreenshotWait = TimeSpan.FromSeconds(6);
        // Encoding the clip takes a few seconds, more for minutes of replay.
        private static readonly TimeSpan VideoWait = TimeSpan.FromSeconds(30);

        private readonly CaptureLibrary library;
        private readonly SemaphoreSlim busy = new SemaphoreSlim(1, 1);
        private DateTime? recordingSince;

        public CaptureTool Tool { get; set; }
        public bool Enabled { get; set; }

        // The recording state changed: phones should fetch the status again.
        public event Action StatusChanged;

        public CaptureService(CaptureLibrary library)
        {
            this.library = library;
            // A recording turning up means the one CouchPilot started has ended, however it ended.
            library.Changed += () =>
            {
                var since = recordingSince;
                if (since != null && busy.CurrentCount == 1 && library.HasVideoSince(since.Value - TimeSpan.FromSeconds(3))) SetRecording(null);
            };
        }

        public CaptureStatusDto Status() => !Enabled ? null : new CaptureStatusDto
        {
            Tool = ToolName,
            RecordingSince = recordingSince?.ToString("o"),
            ReplaySeconds = Tool == CaptureTool.GameBar ? GameBar.ReplaySeconds() : null,
        };

        public string ToolName => Tool == CaptureTool.Nvidia ? "nvidia" : "gamebar";

        public Task<CaptureDto> Screenshot() => Run(async () =>
        {
            if (Tool == CaptureTool.GameBar) GameBar.EnsureCapturesOn();
            var known = library.KnownIds();
            Press(Shortcut.Screenshot);
            var shot = await library.WaitForNew(known, c => c.Kind == "photo", ScreenshotWait);
            return shot ?? throw NothingSaved("Lo screenshot non è arrivato.");
        });

        public Task<CaptureDto> Replay() => Run(async () =>
        {
            if (Tool == CaptureTool.GameBar)
            {
                GameBar.EnsureCapturesOn();
                if (GameBar.ReplaySeconds() == null)
                {
                    throw new CaptureException("replay-off", "Game Bar non sta registrando in sottofondo: non c'è niente da salvare.");
                }
            }
            if (recordingSince != null) throw new CaptureException("busy", "Ferma prima la registrazione.");
            var known = library.KnownIds();
            Press(Shortcut.Replay);
            var clip = await library.WaitForNew(known, c => c.Kind == "video", VideoWait);
            if (clip == null)
            {
                throw Tool == CaptureTool.Nvidia
                    ? new CaptureException("replay-off", "Il PC non ha salvato nessuna clip: il Replay istantaneo di NVIDIA è acceso?")
                    : NothingSaved("Il PC non ha salvato nessuna clip.");
            }
            library.MarkReplay(clip.Id);
            clip.Replay = true;
            return clip;
        });

        public Task StartRecording() => Run<object>(() =>
        {
            if (recordingSince != null) return Task.FromResult<object>(null);
            if (Tool == CaptureTool.GameBar) GameBar.EnsureCapturesOn();
            Press(Shortcut.Record);
            SetRecording(DateTime.UtcNow);
            return Task.FromResult<object>(null);
        });

        public Task<CaptureDto> StopRecording() => Run(async () =>
        {
            var known = library.KnownIds();
            Press(Shortcut.Record);
            SetRecording(null);
            var clip = await library.WaitForNew(known, c => c.Kind == "video", VideoWait);
            return clip ?? throw NothingSaved("La registrazione non è arrivata: forse era già ferma.");
        });

        // Game Bar stops recording when the game closes.
        public void GameStopped()
        {
            if (Tool == CaptureTool.GameBar) SetRecording(null);
        }

        private void SetRecording(DateTime? since)
        {
            if (recordingSince == since) return;
            recordingSince = since;
            StatusChanged?.Invoke();
        }

        private enum Shortcut { Screenshot, Record, Replay }

        // Game Bar's shortcuts are Win+Alt and a key, NVIDIA's Alt and a function key.
        private void Press(Shortcut shortcut)
        {
            if (Tool == CaptureTool.Nvidia)
            {
                var key = shortcut == Shortcut.Screenshot ? VK_F1 : shortcut == Shortcut.Record ? VK_F9 : VK_F10;
                Key.Chord(key, new[] { Key.VK_LMENU });
            }
            else
            {
                var key = shortcut == Shortcut.Screenshot ? Key.VK_SNAPSHOT : shortcut == Shortcut.Record ? VK_R : VK_G;
                Key.Chord(key, new[] { Key.VK_LWIN, Key.VK_LMENU });
            }
        }

        private const ushort VK_F1 = Key.VK_F1;
        private const ushort VK_F9 = 0x78;
        private const ushort VK_F10 = 0x79;
        private const ushort VK_G = 0x47;
        private const ushort VK_R = 0x52;

        private async Task<T> Run<T>(Func<Task<T>> command)
        {
            if (!Enabled) throw new CaptureException("off", "Le catture sono spente nelle impostazioni di CouchPilot.");
            if (!await busy.WaitAsync(0)) throw new CaptureException("busy", "Sto ancora aspettando la cattura di prima.");
            try
            {
                return await command();
            }
            finally
            {
                busy.Release();
            }
        }

        private CaptureException NothingSaved(string what) => new CaptureException(
            "nothing-saved",
            Tool == CaptureTool.Nvidia
                ? $"{what} L'overlay di NVIDIA è acceso e il gioco è in primo piano?"
                : $"{what} Il gioco è in primo piano sul PC?");

        // Game Bar's own settings, in the registry: Settings → Gaming → Captures.
        private static class GameBar
        {
            private const string Key = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

            public static void EnsureCapturesOn()
            {
                if (Read("AppCaptureEnabled") == 0)
                {
                    throw new CaptureException("capture-off", "Le acquisizioni di Game Bar sono spente in Windows: Impostazioni → Giochi → Acquisizioni.");
                }
            }

            // How long «Record what happened» keeps, or null when it's off.
            public static int? ReplaySeconds()
            {
                if (Read("AppCaptureEnabled") == 0 || Read("HistoricalCaptureEnabled") != 1) return null;
                var length = Read("HistoricalBufferLength") ?? 30;
                // 1 seconds, 2 minutes; 30 seconds when Windows hasn't written it.
                return Read("HistoricalBufferLengthUnit") == 2 ? length * 60 : length;
            }

            private static int? Read(string name)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(Key))
                    {
                        return key?.GetValue(name) is int value ? value : (int?)null;
                    }
                }
                catch (Exception e)
                {
                    logger.Warn(e, $"Couldn't read {name}");
                    return null;
                }
            }
        }
    }
}
