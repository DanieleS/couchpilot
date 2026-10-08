using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace CouchPilot.Stats
{
    public class SessionRecord
    {
        public Guid Game { get; set; }
        // When it started, UTC, and how long it lasted.
        public DateTime Start { get; set; }
        public long Seconds { get; set; }
    }

    // Play sessions, read from GameActivity's data. GameActivity (Lacro59's Playnite extension,
    // like SuccessStory) records every session, when it started and how long it lasted, and keeps
    // one JSON file per game in its data folder:
    // <ExtensionsDataPath>\afbb1a0d-04a1-4d0c-9afa-c6e42ca855b4\GameActivity\<game id>.json.
    // CouchPilot only reads those. Without GameActivity there is no history by day: Playnite
    // itself keeps only totals.
    //
    // GameActivity adds a session to its file when the game closes, with its length; the run
    // under way is in memory until then (its crash backups, SaveSession_<id>.json, sit one folder
    // up and aren't sessions). Each file is read again only when its write time changes. The
    // folder is watched too, so phones are told when a session lands.
    //
    // Turned off in the settings (Enabled false), it reads and watches nothing, as if
    // GameActivity weren't installed.
    public class GameActivityReader : IDisposable
    {
        public static readonly Guid GameActivityId = Guid.Parse("afbb1a0d-04a1-4d0c-9afa-c6e42ca855b4");
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1200);

        private readonly IPlayniteAPI api;
        private readonly string folder;
        private readonly object gate = new object();
        private readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer timer;
        private FileSystemWatcher watcher;
        private bool disposed;
        private volatile bool enabled = true;

        // GameActivity wrote, renamed or deleted a game's file.
        public event Action Changed;

        private class Cached
        {
            public DateTime Written;
            public List<SessionRecord> Sessions;
        }

        // What GameActivity writes, the parts CouchPilot reads: the session list. Each session's
        // hardware readings (Details), the legacy ItemsDetails block and the rest are skipped.
        public class GaGame
        {
            public Guid Id { get; set; }
            public List<GaActivity> Items { get; set; }
        }

        public class GaActivity
        {
            // DateTime.Now.ToUniversalTime() when the game started, so UTC, written with a Z.
            public DateTime DateSession { get; set; }
            public ulong ElapsedSeconds { get; set; }
        }

        public GameActivityReader(IPlayniteAPI api)
        {
            this.api = api;
            folder = Path.Combine(api.Paths.ExtensionsDataPath, GameActivityId.ToString(), "GameActivity");
            timer = new Timer(_ =>
            {
                if (enabled) Changed?.Invoke();
            }, null, Timeout.Infinite, Timeout.Infinite);
        }

        // Starts watching, if GameActivity's folder is there. Called after Playnite is up; also
        // tried again on each read, for GameActivity installed later.
        public void Start() => Watch();

        // Whether CouchPilot reads GameActivity's data at all (the settings). Takes effect at
        // once: off, the cache and the watcher go; on again, the files are read afresh.
        public bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value) return;
                enabled = value;
                if (value)
                {
                    Watch();
                    return;
                }
                lock (gate)
                {
                    watcher?.Dispose();
                    watcher = null;
                    cache.Clear();
                }
            }
        }

        // GameActivity is among Playnite's extensions, for the settings.
        public bool Installed => api.Addons.Plugins.Any(p => p.Id == GameActivityId);

        // GameActivity has written something, whether CouchPilot reads it or not, for the settings.
        public bool HasData
        {
            get
            {
                try
                {
                    return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.json").Any();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        // Every session GameActivity has, of every game, in no particular order; null when
        // GameActivity isn't there (no folder, or no file in it) or is turned off. Sessions of 0
        // seconds are left out, whatever GameActivity's own "ignore short sessions" setting says:
        // that one only filters what GameActivity shows.
        public List<SessionRecord> All()
        {
            if (!enabled) return null;
            List<string> files;
            try
            {
                files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json").ToList() : new List<string>();
            }
            catch (Exception e)
            {
                logger.Warn(e, "Can't list GameActivity's folder");
                files = new List<string>();
            }
            if (files.Count == 0)
            {
                lock (gate)
                {
                    cache.Clear();
                }
                return null;
            }
            Watch();
            lock (gate)
            {
                foreach (var path in files)
                {
                    DateTime written;
                    try
                    {
                        written = File.GetLastWriteTimeUtc(path);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (cache.TryGetValue(path, out var cached) && cached.Written == written) continue;
                    var sessions = Read(path);
                    // Mid-write: keep what it said before, and try again next time.
                    if (sessions != null) cache[path] = new Cached { Written = written, Sessions = sessions };
                }
                var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
                foreach (var gone in cache.Keys.Where(k => !present.Contains(k)).ToList()) cache.Remove(gone);
                return cache.Values.SelectMany(c => c.Sessions).ToList();
            }
        }

        private static List<SessionRecord> Read(string path)
        {
            try
            {
                var data = Serialization.FromJson<GaGame>(File.ReadAllText(path));
                if (data == null) return null;
                var game = data.Id;
                if (game == Guid.Empty && !Guid.TryParse(Path.GetFileNameWithoutExtension(path), out game)) return new List<SessionRecord>();
                return (data.Items ?? new List<GaActivity>())
                    .Where(a => a != null && a.ElapsedSeconds > 0 && a.DateSession.Year > 1)
                    .Select(a => new SessionRecord
                    {
                        Game = game,
                        Start = Utc(a.DateSession),
                        Seconds = a.ElapsedSeconds > long.MaxValue ? long.MaxValue : (long)a.ElapsedSeconds,
                    })
                    .ToList();
            }
            catch (Exception e)
            {
                // Mid-write, or not one of GameActivity's files: next time.
                logger.Debug($"Skipping {path}: {e.Message}");
                return null;
            }
        }

        // GameActivity writes UTC with a Z, which reads back as UTC. A date without a zone is
        // taken as UTC too; one with an offset is converted.
        private static DateTime Utc(DateTime d) => d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime();

        private void Watch()
        {
            lock (gate)
            {
                if (disposed || !enabled || watcher != null || !Directory.Exists(folder)) return;
                try
                {
                    watcher = new FileSystemWatcher(folder, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    FileSystemEventHandler soon = (_, __) => Touch();
                    watcher.Created += soon;
                    watcher.Changed += soon;
                    watcher.Deleted += soon;
                    watcher.Renamed += (_, __) => Touch();
                    watcher.EnableRaisingEvents = true;
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Can't watch GameActivity's folder");
                    watcher?.Dispose();
                    watcher = null;
                }
            }
        }

        // A write comes as several events: phones hear about it once things are quiet. The files
        // themselves are read again on the next request, by their write time.
        private void Touch()
        {
            try
            {
                timer.Change(Settle, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                watcher?.Dispose();
                watcher = null;
            }
            timer.Dispose();
        }
    }
}
