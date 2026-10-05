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
    }

    // Which games are running right now, fed by Playnite's start/stop events.
    public class GameSessions
    {
        private readonly ConcurrentDictionary<Guid, GameSession> running = new ConcurrentDictionary<Guid, GameSession>();

        public void SeedFromDatabase(IEnumerable<Game> games)
        {
            foreach (var game in games.Where(g => g.IsRunning))
            {
                running.TryAdd(game.Id, new GameSession { GameId = game.Id });
            }
        }

        public void Started(Game game, int processId)
        {
            running[game.Id] = new GameSession { GameId = game.Id, ProcessId = processId, StartedAt = DateTime.UtcNow };
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
