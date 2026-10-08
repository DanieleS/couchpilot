using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Controls;
using CouchPilot.Achievements;
using CouchPilot.Audio;
using CouchPilot.Captures;
using CouchPilot.Displays;
using CouchPilot.Games;
using CouchPilot.HomeAssistant;
using CouchPilot.Input;
using CouchPilot.Library;
using CouchPilot.Media;
using CouchPilot.Server;
using CouchPilot.Stats;
using CouchPilot.Unlock;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using static CouchPilot.Lang;

namespace CouchPilot
{
    public class CouchPilotPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly GameSessions sessions;
        private readonly LibraryService library;
        private readonly GameControl gameControl;
        private readonly DisplayService displays;
        private readonly AudioService audio;
        private readonly MediaService media = new MediaService();
        private readonly SessionLog sessionLog;
        private readonly StatsService stats;
        private readonly RemoteInput input = new RemoteInput();
        private readonly CaptureLibrary captureLibrary;
        private readonly CaptureService captures;
        private readonly Thumbnails thumbnails;
        private readonly ClipEditor clips;
        private readonly AchievementsService achievements;
        private readonly LockState lockState = new LockState();
        private readonly UnlockService unlock;
        // The extra folder the gallery was last set up with; null until it first is.
        private string capturesConfiguredFor;
        private RemoteServer server;
        private HomeAssistantBridge homeAssistant;
        private PinWindow pinWindow;

        public CouchPilotSettings Settings { get; }

        internal PairingService Pairing { get; }

        internal UnlockService Unlock => unlock;

        // Where the running server answers, for the settings page.
        internal IReadOnlyList<string> Addresses { get; private set; } = new string[0];

        public override Guid Id { get; } = Guid.Parse("6de22bd5-1691-40df-9a93-4a319d8f2d3a");

        public CouchPilotPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = true };
            Lang.UsePlaynite(api.ApplicationSettings.Language);
            Settings = new CouchPilotSettings(this);
            sessions = new GameSessions();
            library = new LibraryService(api, sessions);
            gameControl = new GameControl(api, sessions);
            audio = new AudioService(gameControl);
            sessionLog = new SessionLog(Path.Combine(GetPluginUserDataPath(), "sessions.jsonl"));
            achievements = new AchievementsService(api);
            stats = new StatsService(api, sessionLog, sessions, achievements);
            displays = new DisplayService(Path.Combine(GetPluginUserDataPath(), "display-presets.json"));
            new AudioFollowsDisplays(displays, audio);
            captureLibrary = new CaptureLibrary(() => api.Database.Games, sessionLog, sessions, GetPluginUserDataPath());
            captures = new CaptureService(captureLibrary);
            thumbnails = new Thumbnails(Path.Combine(GetPluginUserDataPath(), "thumbnails"));
            clips = new ClipEditor(captureLibrary);
            achievements.Changed += () => server?.AchievementsChanged();
            Pairing = new PairingService(Path.Combine(GetPluginUserDataPath(), "devices.json")) { Required = Settings.RequirePairing };
            Pairing.Changed += () => server?.Nudge();
            Pairing.PinRequested += ShowPin;
            Pairing.PinDone += HidePin;
            // The setup helper rides in the .pext beside this assembly; absent from a checkout that
            // skipped the native build, and then unlocking just reports itself unavailable.
            unlock = new UnlockService(
                GetPluginUserDataPath(),
                Path.Combine(Path.GetDirectoryName(typeof(CouchPilotPlugin).Assembly.Location), "CouchPilotUnlockSetup.exe"),
                lockState,
                () => Pairing.Required);
            lockState.Changed += () => server?.Nudge();
            unlock.Changed += () => server?.Nudge();
            captureLibrary.Changed += () =>
            {
                server?.CapturesChanged();
                // The newest ones get their thumbnails now, so they're ready when the phone looks, and
                // the newest of all its preview, since it's the one the phone most likely opens next.
                if (Settings.Captures)
                {
                    var newest = captureLibrary.Newest(24);
                    if (newest.Count > 0) thumbnails.WarmPreview(newest[0]);
                    foreach (var c in newest) thumbnails.Warm(c);
                }
            };
            captures.StatusChanged += () => server?.Nudge();
            ApplyInputSetting();
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            sessions.SeedFromDatabase(PlayniteApi.Database.Games);
            // Installs, imports, playtime after a session: the phone refreshes its library.
            PlayniteApi.Database.Games.ItemUpdated += (_, __) => server?.LibraryChanged();
            PlayniteApi.Database.Games.ItemCollectionChanged += (_, __) => server?.LibraryChanged();
            media.Changed += () => server?.MediaChanged();
            media.Start();
            ApplyCaptureSettings();
            achievements.Start();
            StartServer();
            StartHomeAssistant();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            server?.Dispose();
            server = null;
            homeAssistant?.Dispose();
            homeAssistant = null;
            displays.Dispose();
            media.Dispose();
            captureLibrary.Dispose();
            thumbnails.Dispose();
            achievements.Dispose();
            lockState.Dispose();
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            sessions.Started(args.Game, args.StartedProcessId);
            server?.Nudge();
            homeAssistant?.Nudge();
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            sessions.Stopped(args.Game);
            sessionLog.Add(args.Game.Id, DateTime.UtcNow, (long)args.ElapsedSeconds);
            captures.GameStopped();
            server?.Nudge();
            homeAssistant?.Nudge();
        }

        public override ISettings GetSettings(bool firstRunSettings) => Settings;

        public override UserControl GetSettingsView(bool firstRunSettings) => new CouchPilotSettingsView(Settings);

        internal void RestartServer()
        {
            server?.Dispose();
            server = null;
            StartServer();
        }

        // Takes effect at once. Switched on, phones that haven't typed the PIN are let go and
        // see the pairing screen; switched off, every phone is back in.
        internal void ApplyPairingSetting()
        {
            var was = Pairing.Required;
            Pairing.Required = Settings.RequirePairing;
            if (!was && Pairing.Required) server?.DropUnpaired();
            server?.Nudge();
        }

        // The PIN goes on the PC's screen, on top of everything, and in Playnite's notifications
        // in case a game in full screen keeps the window out of sight.
        private void ShowPin(PinRequest request)
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    pinWindow?.Close();
                    pinWindow = new PinWindow(Pairing, request);
                    pinWindow.Show();
                }
                catch (Exception e)
                {
                    logger.Error(e, "Couldn't show the PIN window");
                }
            }));
            PlayniteApi.Notifications.Add(new NotificationMessage(
                "couchpilot-pin",
                Pc($"CouchPilot: “{request.DeviceName}” wants to connect. PIN: {request.Pin.Substring(0, 3)} {request.Pin.Substring(3)}", $"CouchPilot: «{request.DeviceName}» chiede di collegarsi. PIN: {request.Pin.Substring(0, 3)} {request.Pin.Substring(3)}"),
                NotificationType.Info));
        }

        private void HidePin()
        {
            PlayniteApi.Notifications.Remove("couchpilot-pin");
            var app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                pinWindow?.Close();
                pinWindow = null;
            }));
        }

        // Takes effect at once: turning it off lets go of anything held, and phones hear about it.
        internal void ApplyInputSetting()
        {
            input.Enabled = Settings.RemoteInput;
            server?.Nudge();
        }

        // The folders are scanned in the background, and only once the gallery is on: with
        // GeForce Experience's default that's the whole Videos folder.
        internal void ApplyCaptureSettings()
        {
            captures.Enabled = Settings.Captures;
            captures.Tool = Settings.CaptureTool;
            var folder = Settings.CaptureFolder?.Trim() ?? "";
            if (Settings.Captures && capturesConfiguredFor != folder)
            {
                capturesConfiguredFor = folder;
                Task.Run(() =>
                {
                    try
                    {
                        captureLibrary.Configure(folder);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Reading the capture folders failed");
                    }
                });
            }
            server?.Nudge();
            server?.CapturesChanged();
        }

        internal void PickCaptureFolder()
        {
            var path = PlayniteApi.Dialogs.SelectFolder();
            if (!string.IsNullOrEmpty(path)) Settings.CaptureFolder = path;
        }

        internal void RestartHomeAssistant()
        {
            homeAssistant?.Dispose();
            homeAssistant = null;
            StartHomeAssistant();
        }

        private void StartHomeAssistant()
        {
            if (!Settings.MqttEnabled || string.IsNullOrWhiteSpace(Settings.MqttHost)) return;
            var version = typeof(CouchPilotPlugin).Assembly.GetName().Version.ToString(3);
            homeAssistant = new HomeAssistantBridge(Settings.Mqtt, library, displays, gameControl, version);
            homeAssistant.Start();
        }

        internal void PickCertificate()
        {
            var path = PlayniteApi.Dialogs.SelectFile(Pc("PKCS#12 certificate|*.pfx;*.p12", "Certificato PKCS#12|*.pfx;*.p12"));
            if (!string.IsNullOrEmpty(path)) Settings.CertificatePath = path;
        }

        private void StartServer()
        {
            var webRoot = Path.Combine(Path.GetDirectoryName(typeof(CouchPilotPlugin).Assembly.Location), "wwwroot");
            Addresses = new string[0];
            X509Certificate2 certificate = null;
            try
            {
                // A certificate that doesn't load stops the server rather than falling back to
                // HTTP: a quiet downgrade would also move the app to an address it isn't installed at.
                PlayniteApi.Notifications.Remove("couchpilot-certificate");
                if (!string.IsNullOrWhiteSpace(Settings.CertificatePath))
                {
                    certificate = ServerCertificate.Load(Settings.CertificatePath.Trim(), Settings.CertificatePassword);
                    if (ServerCertificate.IsExpired(certificate))
                    {
                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            "couchpilot-certificate",
                            Pc($"CouchPilot: the HTTPS certificate expired on {certificate.NotAfter:d}, the phone will refuse it. Make a new one.", $"CouchPilot: il certificato HTTPS è scaduto il {certificate.NotAfter:d}, il telefono lo rifiuterà. Generane uno nuovo."),
                            NotificationType.Error));
                    }
                }
                server = new RemoteServer(Settings.Port, certificate, webRoot, library, gameControl, displays, input, audio, media, stats, captures, captureLibrary, thumbnails, clips, Pairing, achievements, unlock);
                server.Start();
                PlayniteApi.Notifications.Remove("couchpilot-server");
                Addresses = NetworkInfo.LanUrls(Settings.Port, certificate != null, certificate == null ? null : ServerCertificate.HostName(certificate));
                logger.Info($"CouchPilot listening on port {Settings.Port} ({(certificate != null ? "HTTPS" : "HTTP")})");
            }
            catch (Exception e)
            {
                if (server == null) certificate?.Dispose();
                logger.Error(e, $"CouchPilot could not listen on port {Settings.Port}");
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "couchpilot-server",
                    Pc($"CouchPilot: couldn't start the server on port {Settings.Port}. {e.Message}", $"CouchPilot: impossibile avviare il server sulla porta {Settings.Port}. {e.Message}"),
                    NotificationType.Error));
            }
        }
    }
}
