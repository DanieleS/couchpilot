using System;
using System.IO;
using System.Windows.Controls;
using CouchPilot.Displays;
using CouchPilot.Games;
using CouchPilot.HomeAssistant;
using CouchPilot.Library;
using CouchPilot.Server;
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
        private RemoteServer server;
        private HomeAssistantBridge homeAssistant;

        public CouchPilotSettings Settings { get; }

        public override Guid Id { get; } = Guid.Parse("6de22bd5-1691-40df-9a93-4a319d8f2d3a");

        public CouchPilotPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = true };
            Settings = new CouchPilotSettings(this);
            sessions = new GameSessions();
            library = new LibraryService(api, sessions);
            gameControl = new GameControl(api, sessions);
            displays = new DisplayService(Path.Combine(GetPluginUserDataPath(), "display-presets.json"));
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            sessions.SeedFromDatabase(PlayniteApi.Database.Games);
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
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            sessions.Started(args.Game, args.StartedProcessId);
            homeAssistant?.Nudge();
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            sessions.Stopped(args.Game);
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

        private void StartServer()
        {
            var webRoot = Path.Combine(Path.GetDirectoryName(typeof(CouchPilotPlugin).Assembly.Location), "wwwroot");
            try
            {
                server = new RemoteServer(Settings.Port, webRoot, library, gameControl, displays);
                server.Start();
                logger.Info($"CouchPilot listening on port {Settings.Port}");
            }
            catch (Exception e)
            {
                logger.Error(e, $"CouchPilot could not listen on port {Settings.Port}");
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "couchpilot-server",
                    $"CouchPilot: impossibile avviare il server sulla porta {Settings.Port}. {e.Message}",
                    NotificationType.Error));
            }
        }
    }
}
