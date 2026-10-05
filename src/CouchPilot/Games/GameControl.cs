using System;
using System.Threading.Tasks;
using Playnite.SDK;

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

        public bool Focus(Guid gameId)
        {
            var game = api.Database.Games.Get(gameId);
            if (game == null) return false;
            var session = sessions.Get(gameId);
            return GameProcesses.Focus(game.InstallDirectory, session?.ProcessId ?? 0);
        }
    }
}
