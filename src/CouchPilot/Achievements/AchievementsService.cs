using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace CouchPilot.Achievements
{
    public class AchievementDto
    {
        // The source's own name for it (Steam's API name, say), or its position when there is none.
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Unlocked { get; set; }
        // When it was unlocked, or null: still locked, or the source doesn't say when.
        public string UnlockedAt { get; set; }
        // The game hides it until it's unlocked.
        public bool Hidden { get; set; }
        // How many players have it, 0–100; null when the source doesn't say.
        public float? Percent { get; set; }
        public float? GamerScore { get; set; }
        // The icon's URL: on the web, or on this PC through /api when the source keeps it locally.
        public string Icon { get; set; }
        // The greyed-out icon the game shows while it's locked, or null to grey out the other.
        public string LockedIcon { get; set; }
    }

    public class GameAchievementsDto
    {
        public string GameId { get; set; }
        public string Game { get; set; }
        public int Total { get; set; }
        public int Unlocked { get; set; }
        public string LastRefresh { get; set; }
        // Unlocked ones first, newest first; then the locked ones, the most common first.
        public List<AchievementDto> Items { get; set; }
    }

    public class RecentAchievementDto : AchievementDto
    {
        public string GameId { get; set; }
        public string Game { get; set; }
        public bool HasCover { get; set; }
    }

    // Achievements, read from SuccessStory's data. SuccessStory (Lacro59's Playnite extension)
    // fetches them from Steam, Epic, GOG, Xbox, RetroAchievements and more and keeps one JSON
    // file per game in its data folder; CouchPilot only reads those, so it has nothing to log
    // into and nothing to fetch. Without SuccessStory there are no achievements to show, and the
    // phone doesn't mention them.
    //
    // The folder is watched: when SuccessStory refreshes a game (at the end of a session, or
    // while you play if it's set to), the phone is told and shows what was just unlocked.
    public class AchievementsService : IDisposable
    {
        public static readonly Guid SuccessStoryId = Guid.Parse("cebe6d32-8c46-4459-b993-5a5189d60788");
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1200);
        // The real unlock dates can only be trusted as far as the source gives them; SuccessStory
        // itself uses 1982 for "unlocked, no idea when".
        private const int UnknownYear = 1982;

        private readonly IPlayniteAPI api;
        private readonly string dataPath;
        private readonly string folder;
        private readonly object gate = new object();
        private readonly Dictionary<Guid, Cached> cache = new Dictionary<Guid, Cached>();
        private readonly HashSet<string> dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer timer;
        private FileSystemWatcher watcher;
        private bool loaded;
        private bool disposed;
        // The status asks twice a second; the folder is looked at every half minute.
        private bool available;
        private DateTime availableCheckedAt = DateTime.MinValue;

        // A game's achievements changed on disk.
        public event Action Changed;

        private class Cached
        {
            public string Path;
            public DateTime Written;
            public SsGame Data;
        }

        // What SuccessStory writes, the parts CouchPilot reads. Everything else in the file is skipped.
        public class SsGame
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public List<SsAchievement> Items { get; set; }
            public DateTime DateLastRefresh { get; set; }
            public bool IsIgnored { get; set; }
        }

        public class SsAchievement
        {
            public string Name { get; set; }
            public string ApiName { get; set; }
            public string Description { get; set; }
            public string UrlUnlocked { get; set; }
            public string UrlLocked { get; set; }
            public DateTime? DateUnlocked { get; set; }
            public bool IsHidden { get; set; }
            public float Percent { get; set; } = 100;
            public float GamerScore { get; set; }
            public bool NoRarety { get; set; }

            [DontSerialize]
            public bool IsUnlocked => DateUnlocked != null && DateUnlocked.Value.Year > 1;

            [DontSerialize]
            public DateTime? When => IsUnlocked && DateUnlocked.Value.Year != UnknownYear ? DateUnlocked : null;
        }

        public AchievementsService(IPlayniteAPI api)
        {
            this.api = api;
            dataPath = Path.Combine(api.Paths.ExtensionsDataPath, SuccessStoryId.ToString());
            folder = Path.Combine(dataPath, "SuccessStory");
            timer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        }

        // SuccessStory has written something: there are achievements to show.
        public bool Available
        {
            get
            {
                var now = DateTime.UtcNow;
                if (now - availableCheckedAt < TimeSpan.FromSeconds(30)) return available;
                availableCheckedAt = now;
                try
                {
                    available = Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.json").Any();
                }
                catch (Exception)
                {
                    available = false;
                }
                return available;
            }
        }

        // Reads everything once, in the background, and starts watching. Called after Playnite is up.
        public void Start()
        {
            Task.Run(() =>
            {
                try
                {
                    EnsureLoaded();
                    Watch();
                }
                catch (Exception e)
                {
                    logger.Error(e, "Reading SuccessStory's data failed");
                }
            });
        }

        public GameAchievementsDto Game(Guid id)
        {
            var data = Load(id);
            if (data?.Items == null || data.Items.Count == 0 || data.IsIgnored) return null;
            var game = api.Database.Games.Get(id);
            var items = data.Items.Select((a, i) => ToDto(a, i, id)).ToList();
            return new GameAchievementsDto
            {
                GameId = id.ToString(),
                Game = game?.Name ?? data.Name,
                Total = items.Count,
                Unlocked = items.Count(a => a.Unlocked),
                LastRefresh = data.DateLastRefresh.Year > 1 ? data.DateLastRefresh.ToUniversalTime().ToString("o") : null,
                Items = items.OrderByDescending(a => a.Unlocked)
                    .ThenByDescending(a => a.UnlockedAt ?? "")
                    .ThenByDescending(a => a.Unlocked ? 0 : a.Percent ?? 0)
                    .ToList(),
            };
        }

        // The latest unlocked, across every game, newest first. Ones without a date don't count.
        public List<RecentAchievementDto> Recent(int count) => Unlocked(DateTime.MinValue, DateTime.MaxValue).Take(count).ToList();

        // Unlocked between two moments (UTC, `to` exclusive), newest first.
        public List<RecentAchievementDto> Unlocked(DateTime fromUtc, DateTime toUtc)
        {
            EnsureLoaded();
            List<Cached> all;
            lock (gate)
            {
                all = cache.Values.ToList();
            }
            var games = api.Database.Games;
            var result = new List<(DateTime when, RecentAchievementDto dto)>();
            foreach (var c in all)
            {
                if (c.Data?.Items == null || c.Data.IsIgnored) continue;
                Playnite.SDK.Models.Game game = null;
                for (var i = 0; i < c.Data.Items.Count; i++)
                {
                    var a = c.Data.Items[i];
                    var when = a.When?.ToUniversalTime();
                    if (when == null || when < fromUtc || when >= toUtc) continue;
                    game = game ?? games.Get(c.Data.Id);
                    var dto = ToDto(a, i, c.Data.Id, new RecentAchievementDto());
                    dto.GameId = c.Data.Id.ToString();
                    dto.Game = game?.Name ?? c.Data.Name;
                    dto.HasCover = !string.IsNullOrEmpty(game?.CoverImage);
                    result.Add((when.Value, dto));
                }
            }
            return result.OrderByDescending(r => r.when).Select(r => r.dto).ToList();
        }

        // Where an icon SuccessStory keeps on disk is, or null when the achievement's icon is on the web.
        public string IconPath(Guid game, int index, bool locked)
        {
            var data = Load(game);
            if (data?.Items == null || index < 0 || index >= data.Items.Count) return null;
            var url = locked ? data.Items[index].UrlLocked : data.Items[index].UrlUnlocked;
            if (string.IsNullOrWhiteSpace(url) || IsWeb(url)) return null;
            // RPCS3's and ShadPS4's icons live under the data folder, a few games' under the
            // extension's own Resources. Either way nothing outside those two.
            foreach (var root in new[] { dataPath, ResourcesPath() })
            {
                if (root == null) continue;
                var path = Path.GetFullPath(Path.Combine(root, url.TrimStart('\\', '/')));
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) return path;
            }
            return null;
        }

        private string ResourcesPath()
        {
            try
            {
                var plugin = api.Addons.Plugins.FirstOrDefault(p => p.Id == SuccessStoryId);
                var location = plugin?.GetType().Assembly.Location;
                return location == null ? null : Path.Combine(Path.GetDirectoryName(location), "Resources");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private AchievementDto ToDto(SsAchievement a, int index, Guid game) => ToDto(a, index, game, new AchievementDto());

        private T ToDto<T>(SsAchievement a, int index, Guid game, T dto) where T : AchievementDto
        {
            dto.Id = string.IsNullOrWhiteSpace(a.ApiName) ? index.ToString() : a.ApiName;
            dto.Name = a.Name;
            dto.Description = a.Description;
            dto.Unlocked = a.IsUnlocked;
            dto.UnlockedAt = a.When?.ToUniversalTime().ToString("o");
            dto.Hidden = a.IsHidden;
            dto.Percent = a.NoRarety || a.Percent >= 100 ? (float?)null : (float)Math.Round(a.Percent, 1);
            dto.GamerScore = a.GamerScore > 0 ? a.GamerScore : (float?)null;
            dto.Icon = IconUrl(a.UrlUnlocked, game, index, false);
            // Steam's locked icon is often its generic placeholder (a short CDN URL): then the
            // phone greys out the real one instead.
            var locked = a.UrlLocked;
            if (string.IsNullOrWhiteSpace(locked) || locked == a.UrlUnlocked || (locked.Contains("steamcdn-a.akamaihd.net") && locked.Length < 75)) locked = null;
            dto.LockedIcon = IconUrl(locked, game, index, true);
            return dto;
        }

        private static string IconUrl(string url, Guid game, int index, bool locked)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (IsWeb(url)) return url;
            return $"/api/games/{game}/achievements/{index}/icon{(locked ? "?locked=1" : "")}";
        }

        private static bool IsWeb(string url) => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        // One game's file, from the cache or the disk.
        private SsGame Load(Guid id)
        {
            var path = Path.Combine(folder, id + ".json");
            lock (gate)
            {
                if (cache.TryGetValue(id, out var cached))
                {
                    try
                    {
                        if (!File.Exists(path)) return null;
                        if (File.GetLastWriteTimeUtc(path) == cached.Written) return cached.Data;
                    }
                    catch (Exception)
                    {
                        return cached.Data;
                    }
                }
            }
            return Read(path)?.Data;
        }

        private Cached Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var written = File.GetLastWriteTimeUtc(path);
                var data = Serialization.FromJson<SsGame>(File.ReadAllText(path));
                if (data == null) return null;
                if (data.Id == Guid.Empty && Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var fromName)) data.Id = fromName;
                if (data.Id == Guid.Empty) return null;
                var cached = new Cached { Path = path, Written = written, Data = data };
                lock (gate)
                {
                    cache[data.Id] = cached;
                }
                return cached;
            }
            catch (Exception e)
            {
                // Mid-write, or not one of SuccessStory's files: next time.
                logger.Debug($"Skipping {path}: {e.Message}");
                return null;
            }
        }

        private void EnsureLoaded()
        {
            lock (gate)
            {
                if (loaded) return;
                loaded = true;
            }
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").ToList()) Read(file);
        }

        private void Watch()
        {
            lock (gate)
            {
                if (disposed || watcher != null || !Directory.Exists(folder)) return;
                try
                {
                    watcher = new FileSystemWatcher(folder, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    FileSystemEventHandler soon = (_, e) => Touch(e.FullPath);
                    watcher.Created += soon;
                    watcher.Changed += soon;
                    watcher.Deleted += soon;
                    watcher.Renamed += (_, e) =>
                    {
                        Touch(e.OldFullPath);
                        Touch(e.FullPath);
                    };
                    watcher.EnableRaisingEvents = true;
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Can't watch SuccessStory's folder");
                }
            }
        }

        private void Touch(string path)
        {
            lock (gate)
            {
                dirty.Add(path);
            }
            try
            {
                timer.Change(Settle, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        // The files that changed, re-read once things are quiet; gone ones dropped.
        private void Reload()
        {
            List<string> paths;
            lock (gate)
            {
                paths = dirty.ToList();
                dirty.Clear();
            }
            var changed = false;
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    changed |= Read(path) != null;
                }
                else
                {
                    lock (gate)
                    {
                        var gone = cache.Where(c => string.Equals(c.Value.Path, path, StringComparison.OrdinalIgnoreCase)).Select(c => c.Key).ToList();
                        foreach (var id in gone) cache.Remove(id);
                        changed |= gone.Count > 0;
                    }
                }
            }
            if (changed)
            {
                availableCheckedAt = DateTime.MinValue;
                Changed?.Invoke();
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
