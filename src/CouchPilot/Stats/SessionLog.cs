using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace CouchPilot.Stats
{
    public class SessionRecord
    {
        public Guid Game { get; set; }
        // When it started, UTC, and how long Playnite says it lasted.
        public DateTime Start { get; set; }
        public long Seconds { get; set; }
    }

    // Every game session Playnite reports, one JSON object per line, appended as each game
    // closes. Playnite itself keeps only totals, so the history starts the day this is installed.
    // A few dozen bytes a session: years of play stay well under a megabyte.
    public class SessionLog
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly string path;
        private readonly object gate = new object();
        private List<SessionRecord> records;

        public SessionLog(string path)
        {
            this.path = path;
        }

        // Playnite's own count of the session's length, so our days add up to its totals.
        public void Add(Guid game, DateTime endUtc, long seconds)
        {
            if (seconds <= 0) return;
            var record = new SessionRecord { Game = game, Start = endUtc.AddSeconds(-seconds), Seconds = seconds };
            lock (gate)
            {
                Load();
                records.Add(record);
                try
                {
                    File.AppendAllText(path, Serialization.ToJson(record) + Environment.NewLine);
                }
                catch (Exception e)
                {
                    logger.Error(e, "Saving a game session failed");
                }
            }
        }

        public List<SessionRecord> All()
        {
            lock (gate)
            {
                Load();
                return records.ToList();
            }
        }

        private void Load()
        {
            if (records != null) return;
            records = new List<SessionRecord>();
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = Serialization.FromJson<SessionRecord>(line);
                    if (record != null && record.Seconds > 0) records.Add(record);
                }
                catch
                {
                    // A line cut short by a crash mid-write: skip it, keep the rest.
                }
            }
        }
    }
}
