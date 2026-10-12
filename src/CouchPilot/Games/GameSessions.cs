using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace CouchPilot.Games
{
    public class GameSession
    {
        public Guid GameId { get; set; }
        public int ProcessId { get; set; }
        // Null when the game was already running before Playnite (re)loaded the plugin.
        public DateTime? StartedAt { get; set; }
        // Started from CouchPilot (the phone, or Home Assistant's start_game), not from Playnite
        // itself or a Vibepollo app.
        public bool LaunchedHere { get; set; }
    }

    // Which games are running right now, fed by Playnite's start/stop events.
    public class GameSessions
    {
        // How long a launch from CouchPilot waits for Playnite to report the game started: Steam
        // may update the game first.
        private static readonly TimeSpan LaunchWindow = TimeSpan.FromMinutes(10);

        private readonly ConcurrentDictionary<Guid, GameSession> running = new ConcurrentDictionary<Guid, GameSession>();
        private readonly ConcurrentDictionary<Guid, DateTime> launching = new ConcurrentDictionary<Guid, DateTime>();

        public void SeedFromDatabase(IEnumerable<Game> games)
        {
            foreach (var game in games.Where(g => g.IsRunning))
            {
                running.TryAdd(game.Id, new GameSession { GameId = game.Id });
            }
        }

        // CouchPilot is about to start this game: the session that follows counts as launched here.
        public void Launching(Guid gameId)
        {
            launching[gameId] = DateTime.UtcNow;
        }

        public void Started(Game game, int processId)
        {
            var now = DateTime.UtcNow;
            var launchedHere = launching.TryRemove(game.Id, out var asked) && now - asked < LaunchWindow;
            running[game.Id] = new GameSession { GameId = game.Id, ProcessId = processId, StartedAt = now, LaunchedHere = launchedHere };
        }

        public void Stopped(Game game)
        {
            running.TryRemove(game.Id, out _);
        }

        public bool IsRunning(Guid gameId) => running.ContainsKey(gameId);

        public GameSession Get(Guid gameId) => running.TryGetValue(gameId, out var s) ? s : null;

        public IReadOnlyList<GameSession> All => running.Values.OrderBy(s => s.StartedAt ?? DateTime.MinValue).ToList();
    }
}
