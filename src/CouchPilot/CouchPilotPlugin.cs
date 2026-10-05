using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Controls;
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
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

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
        // The extra folder the gallery was last set up with; null until it first is.
        private string capturesConfiguredFor;
        private RemoteServer server;
        private HomeAssistantBridge homeAssistant;

        public CouchPilotSettings Settings { get; }

        // Where the running server answers, for the settings page.
        internal IReadOnlyList<string> Addresses { get; private set; } = new string[0];

        public override Guid Id { get; } = Guid.Parse("6de22bd5-1691-40df-9a93-4a319d8f2d3a");

        public CouchPilotPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = true };
            Settings = new CouchPilotSettings(this);
            sessions = new GameSessions();
            library = new LibraryService(api, sessions);
            gameControl = new GameControl(api, sessions);
            audio = new AudioService(gameControl);
            sessionLog = new SessionLog(Path.Combine(GetPluginUserDataPath(), "sessions.jsonl"));
            stats = new StatsService(api, sessionLog, sessions);
            displays = new DisplayService(Path.Combine(GetPluginUserDataPath(), "display-presets.json"));
            new AudioFollowsDisplays(displays, audio);
            captureLibrary = new CaptureLibrary(() => api.Database.Games, sessionLog, sessions, GetPluginUserDataPath());
            captures = new CaptureService(captureLibrary);
            thumbnails = new Thumbnails(Path.Combine(GetPluginUserDataPath(), "thumbnails"));
            captureLibrary.Changed += () => server?.CapturesChanged();
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
            var path = PlayniteApi.Dialogs.SelectFile("Certificato PKCS#12|*.pfx;*.p12");
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
                            $"CouchPilot: il certificato HTTPS è scaduto il {certificate.NotAfter:d}, il telefono lo rifiuterà. Generane uno nuovo.",
                            NotificationType.Error));
                    }
                }
                server = new RemoteServer(Settings.Port, certificate, webRoot, library, gameControl, displays, input, audio, media, stats, captures, captureLibrary, thumbnails);
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
                    $"CouchPilot: impossibile avviare il server sulla porta {Settings.Port}. {e.Message}",
                    NotificationType.Error));
            }
        }
    }
}
