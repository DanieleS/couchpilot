using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace CouchPilot.Games
{
    public class GameControl
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan CloseGracePeriod = TimeSpan.FromSeconds(10);

        private readonly IPlayniteAPI api;
        private readonly GameSessions sessions;

        public GameControl(IPlayniteAPI api, GameSessions sessions)
        {
            this.api = api;
            this.sessions = sessions;
        }

        public void Start(Guid gameId)
        {
            // StartGame has to run on Playnite's UI thread; from the web server's thread
            // it fails or leaves the game half-started.
            api.MainView.UIDispatcher.Invoke(() => api.StartGame(gameId));
        }

        // Both hand over to the game's library plugin, which may open its own client (Steam,
        // Epic…) or a confirmation on the PC to finish the job. They need Playnite's UI thread,
        // but unlike StartGame they're not waited for: a dialog on the PC would hold the phone's
        // request until someone answered it.
        public void Install(Guid gameId) => api.MainView.UIDispatcher.BeginInvoke(new Action(() => api.InstallGame(gameId)));

        public void Uninstall(Guid gameId) => api.MainView.UIDispatcher.BeginInvoke(new Action(() => api.UninstallGame(gameId)));

        public bool Stop(Guid gameId)
        {
            var game = api.Database.Games.Get(gameId);
            if (game == null) return false;
            var session = sessions.Get(gameId);
            var pids = GameProcesses.Find(game.InstallDirectory, session?.ProcessId ?? 0);
            if (pids.Count == 0) return false;

            Task.Run(async () =>
            {
                try
                {
                    await GameProcesses.CloseGracefullyThenForce(game.InstallDirectory, session?.ProcessId ?? 0, CloseGracePeriod);
                }
                catch (Exception e)
                {
                    logger.Error(e, $"Closing {game.Name} failed");
                }
            });
            return true;
        }

        // Every process of every running game, so other features can tell the game apart.
        public Dictionary<int, Game> RunningProcesses()
        {
            var result = new Dictionary<int, Game>();
            foreach (var session in sessions.All)
            {
                var game = api.Database.Games.Get(session.GameId);
                if (game == null) continue;
                foreach (var pid in GameProcesses.Find(game.InstallDirectory, session.ProcessId))
                {
                    result[pid] = game;
                }
            }
            return result;
        }

        public bool Focus(Guid gameId)
        {
            var game = api.Database.Games.Get(gameId);
            if (game == null) return false;
            var session = sessions.Get(gameId);
            return GameProcesses.Focus(game.InstallDirectory, session?.ProcessId ?? 0);
        }
    }
}
