using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CouchPilot.Games;
using CouchPilot.Stats;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace CouchPilot.Captures
{
    public class CaptureDto
    {
        public string Id { get; set; }
        // photo or video
        public string Kind { get; set; }
        // A clip of what just happened (Game Bar's «Record that», NVIDIA's Instant Replay).
        public bool Replay { get; set; }
        public string TakenAt { get; set; }
        public long Bytes { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public double? Seconds { get; set; }
        // The Playnite game it belongs to, when CouchPilot can tell.
        public string GameId { get; set; }
        // The game's name, or what the capture tool called the window ("Desktop"), or null.
        public string Game { get; set; }
        // gamebar, nvidia, or null for the extra folder from the settings.
        public string Tool { get; set; }
        public string FileName { get; set; }
        // Changes when the file does: the thumbnail's URL carries it.
        public string Version { get; set; }
        // Cut out of another clip on the phone: that clip's id, when it's still there.
        public bool Edited { get; set; }
        public string SourceId { get; set; }
    }

    public class ThumbnailSource
    {
        public string Id;
        public string Path;
        public string Version;
        public bool Video;
        public double? Seconds;
        public DateTime TakenUtc;
    }

    public class CapturesDto
    {
        public List<CaptureDto> Items { get; set; }
        public long Bytes { get; set; }
        public List<string> Folders { get; set; }
    }

    // Every screenshot and clip in the capture tools' folders, newest first. The folders are
    // watched: a new file shows up once its writer has let go of it, and phones are told.
    //
    // A capture belongs to the game whose session it falls in (GameActivity's sessions, and the
    // game running now); others, and all of them without GameActivity, go by the name the tool
    // gave them or their folder.
    public class CaptureLibrary : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        // GIFs are the clip editor's: a photo as far as the gallery is concerned, one that moves.
        private static readonly HashSet<string> PhotoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif" };
        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4" };
        // NVIDIA's names: "Elden Ring 2024.10.05 - 21.42.13.02.mp4", "… Screenshot 2024.10.05 - ….png".
        private static readonly Regex NvidiaName = new Regex(@"\d{4}\.\d{2}\.\d{2} - \d{2}\.\d{2}\.\d{2}", RegexOptions.Compiled);
        // From the first date-like run on: "Elden Ring 10_5_2024 9_42_13 PM" → "Elden Ring".
        private static readonly Regex DateTail = new Regex(@"\s*\d{1,4}[._-]\d{1,2}[._-]\d{1,4}.*$", RegexOptions.Compiled);
        private static readonly Regex EditSuffix = new Regex(@"(\s\(\d+(\.\d+)+-\d+(\.\d+)+\))?(\s\(\d+\))?$", RegexOptions.Compiled);
        private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(800);

        private readonly Func<IEnumerable<Game>> library;
        private readonly GameActivityReader activity;
        private readonly GameSessions sessions;
        private readonly string metaPath;
        private readonly object gate = new object();
        private readonly object scanGate = new object();
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly List<Waiter> waiters = new List<Waiter>();
        private readonly Timer timer;

        private List<Root> roots = new List<Root>();
        // Everything indexed, by full path and by id.
        private Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Entry> byId = new Dictionary<string, Entry>();
        // Size, length and replay flag of files already read, kept across restarts.
        private Dictionary<string, Meta> meta;
        private bool scanned;
        private bool metaDirty;
        private bool disposed;

        // Something was added or removed: phones should fetch the list again.
        public event Action Changed;

        private class Root
        {
            public string Path;
            public string Tool;
            // Only NVIDIA's own file names: its folder may be the whole of Videos.
            public bool NvidiaNamesOnly;
        }

        private class Entry
        {
            public string Id;
            public string Path;
            public Root Root;
            public bool Video;
            public long Bytes;
            public DateTime TakenUtc;
            public DateTime WrittenUtc;
            public Meta Meta;
        }

        public class Meta
        {
            public string Path { get; set; }
            public long Bytes { get; set; }
            public long Written { get; set; }
            public int? Width { get; set; }
            public int? Height { get; set; }
            public double? Seconds { get; set; }
            public bool Replay { get; set; }
            // Cut from another capture by the clip editor: that one's id.
            public string Source { get; set; }
        }

        private class Waiter
        {
            public HashSet<string> Known;
            public Func<CaptureDto, bool> Wanted;
            public TaskCompletionSource<CaptureDto> Done;
        }

        // `library`: Playnite's games, for naming captures by the session or the name they belong to.
        public CaptureLibrary(Func<IEnumerable<Game>> library, GameActivityReader activity, GameSessions sessions, string dataPath)
        {
            this.library = library;
            this.activity = activity;
            this.sessions = sessions;
            metaPath = Path.Combine(dataPath, "captures.json");
            timer = new Timer(_ => Rescan(), null, Timeout.Infinite, Timeout.Infinite);
        }

        // The folders to show: Game Bar's, NVIDIA's (each if it exists), and one of your own.
        public void Configure(string extraFolder)
        {
            var list = new List<Root>();
            void Add(string path, string tool, bool nvidiaNamesOnly = false)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                path = System.IO.Path.GetFullPath(path.Trim()).TrimEnd('\\', '/');
                if (list.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))) return;
                list.Add(new Root { Path = path, Tool = tool, NvidiaNamesOnly = nvidiaNamesOnly });
            }

            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            Add(Native.Shell.KnownFolder(Native.Shell.AppCaptures) ?? System.IO.Path.Combine(videos, "Captures"), "gamebar");
            foreach (var nvidia in NvidiaFolders(videos))
            {
                Add(nvidia, "nvidia", string.Equals(nvidia.TrimEnd('\\'), videos.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            }
            Add(extraFolder, null);

            lock (gate)
            {
                roots = list;
                // Indexed afresh: a file may now belong to another folder, and another tool.
                entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                byId = new Dictionary<string, Entry>();
                scanned = false;
            }
            Watch();
            Rescan();
        }

        // Where NVIDIA saves: the folder set in GeForce Experience or the NVIDIA app, else the
        // app's default, Videos\NVIDIA.
        private static IEnumerable<string> NvidiaFolders(string videos)
        {
            string configured = null;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS"))
                {
                    if (key?.GetValue("DefaultPathW") is byte[] bytes) configured = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                }
            }
            catch (Exception)
            {
                // No NVIDIA software, or a value in another shape.
            }
            if (!string.IsNullOrWhiteSpace(configured)) yield return configured;
            yield return System.IO.Path.Combine(videos, "NVIDIA");
        }

        public CapturesDto List()
        {
            EnsureScanned();
            List<Entry> all;
            List<string> folders;
            lock (gate)
            {
                all = entries.Values.ToList();
                folders = roots.Where(r => Directory.Exists(r.Path)).Select(r => r.Path).ToList();
            }
            var names = new Names(library(), activity, sessions);
            return new CapturesDto
            {
                Items = all.OrderByDescending(e => e.TakenUtc).Select(e => ToDto(e, names)).ToList(),
                Bytes = all.Sum(e => e.Bytes),
                Folders = folders,
            };
        }

        // The file behind an id, if it's one of ours: nothing else on the disk can be asked for.
        public string PathOf(string id) => Find(id)?.Path;

        // What a thumbnail needs: the file, its version, whether it's a video and how long.
        public ThumbnailSource ThumbnailOf(string id)
        {
            var entry = Find(id);
            return entry == null ? null : SourceOf(entry);
        }

        // The newest captures, for making their thumbnails before anyone asks.
        public List<ThumbnailSource> Newest(int count)
        {
            lock (gate)
            {
                return entries.Values.OrderByDescending(e => e.TakenUtc).Take(count).Select(SourceOf).ToList();
            }
        }

        private static ThumbnailSource SourceOf(Entry e) => new ThumbnailSource
        {
            Id = e.Id,
            Path = e.Path,
            Version = e.WrittenUtc.Ticks.ToString("x"),
            Video = e.Video,
            Seconds = e.Meta.Seconds,
            TakenUtc = e.TakenUtc,
        };

        // A file the clip editor just wrote into one of the folders: indexed now, remembered as
        // cut from `sourceId`, and returned as the gallery will list it. Null if it isn't in a
        // watched folder after all.
        public CaptureDto Adopt(string path, string sourceId)
        {
            Rescan();
            Entry entry;
            lock (gate)
            {
                if (!entries.TryGetValue(path, out entry)) return null;
                if (entry.Meta.Source != sourceId)
                {
                    entry.Meta.Source = sourceId;
                    metaDirty = true;
                }
            }
            SaveMeta();
            Changed?.Invoke();
            return ToDto(entry, new Names(library(), activity, sessions));
        }

        private Entry Find(string id)
        {
            EnsureScanned();
            lock (gate)
            {
                return id != null && byId.TryGetValue(id, out var entry) ? entry : null;
            }
        }

        public bool HasVideoSince(DateTime utc)
        {
            lock (gate)
            {
                return entries.Values.Any(e => e.Video && e.TakenUtc >= utc);
            }
        }

        public HashSet<string> KnownIds()
        {
            EnsureScanned();
            lock (gate)
            {
                return new HashSet<string>(entries.Values.Select(e => e.Id));
            }
        }

        // The first capture not in `known` that `wanted` accepts, or null after `timeout`.
        // Rescans every second meanwhile, in case the folder watcher misses it.
        public async Task<CaptureDto> WaitForNew(HashSet<string> known, Func<CaptureDto, bool> wanted, TimeSpan timeout)
        {
            var waiter = new Waiter { Known = known, Wanted = wanted, Done = new TaskCompletionSource<CaptureDto>() };
            lock (gate)
            {
                waiters.Add(waiter);
            }
            try
            {
                var deadline = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadline)
                {
                    var tick = Task.Delay(TimeSpan.FromSeconds(1));
                    if (await Task.WhenAny(waiter.Done.Task, tick) == waiter.Done.Task) return waiter.Done.Task.Result;
                    Rescan();
                    // It may have landed in a scan that ran before this waiter was listed.
                    Check(waiter);
                }
                return waiter.Done.Task.IsCompleted ? waiter.Done.Task.Result : null;
            }
            finally
            {
                lock (gate)
                {
                    waiters.Remove(waiter);
                }
            }
        }

        // Clips CouchPilot asked for as replays keep the flag; the tools don't mark them all.
        public void MarkReplay(string id)
        {
            lock (gate)
            {
                if (!byId.TryGetValue(id, out var entry) || entry.Meta.Replay) return;
                entry.Meta.Replay = true;
                metaDirty = true;
            }
            SaveMeta();
            Changed?.Invoke();
        }

        // To the Recycle Bin, so a wrong tap on the phone can still be undone at the PC.
        public bool Delete(string id)
        {
            var path = PathOf(id);
            if (path == null) return false;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            Rescan();
            return true;
        }

        private void EnsureScanned()
        {
            bool done;
            lock (gate)
            {
                done = scanned;
            }
            if (!done) Rescan();
        }

        private void Watch()
        {
            lock (gate)
            {
                foreach (var w in watchers) w.Dispose();
                watchers.Clear();
                if (disposed) return;
                foreach (var root in roots.Where(r => Directory.Exists(r.Path)))
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(root.Path)
                        {
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                        };
                        FileSystemEventHandler soon = (_, __) => RescanSoon();
                        watcher.Created += soon;
                        watcher.Changed += soon;
                        watcher.Deleted += soon;
                        watcher.Renamed += (_, __) => RescanSoon();
                        watcher.Error += (_, e) =>
                        {
                            logger.Warn(e.GetException(), "Capture folder watcher failed");
                            RescanSoon();
                        };
                        watcher.EnableRaisingEvents = true;
                        watchers.Add(watcher);
                    }
                    catch (Exception e)
                    {
                        logger.Warn(e, $"Can't watch {root.Path}");
                    }
                }
            }
        }

        // A burst of events (a clip being written) costs one scan, once things are quiet.
        private void RescanSoon()
        {
            try
            {
                timer.Change(Settle, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Rescan()
        {
            // One scan at a time: the watcher's timer, a phone's request and a waiting command
            // can all ask at once, and the second would only repeat the first.
            lock (scanGate)
            {
                ScanOnce();
            }
        }

        private void ScanOnce()
        {
            List<Root> current;
            lock (gate)
            {
                if (disposed) return;
                current = roots;
                LoadMeta();
            }

            var found = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            var busy = false;
            foreach (var root in current)
            {
                foreach (var file in Files(root))
                {
                    if (found.ContainsKey(file.FullName)) continue;
                    var video = VideoExtensions.Contains(file.Extension);
                    Entry known;
                    lock (gate)
                    {
                        entries.TryGetValue(file.FullName, out known);
                    }
                    if (known != null && known.Bytes == file.Length && known.WrittenUtc == file.LastWriteTimeUtc)
                    {
                        found[file.FullName] = known;
                        continue;
                    }
                    // Still being written (a clip being encoded, a recording under way): next time.
                    if (!Settled(file))
                    {
                        busy = true;
                        continue;
                    }
                    found[file.FullName] = Index(file, root, video);
                }
            }

            bool changed;
            List<Waiter> toCheck;
            lock (gate)
            {
                changed = found.Count != entries.Count || found.Keys.Any(k => !entries.TryGetValue(k, out var e) || !ReferenceEquals(e, found[k]));
                entries = found;
                byId = found.Values.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
                scanned = true;
                foreach (var gone in meta.Keys.Where(k => !found.ContainsKey(k)).ToList())
                {
                    meta.Remove(gone);
                    metaDirty = true;
                }
                toCheck = waiters.ToList();
            }
            if (busy) RescanSoon();
            if (!changed) return;

            SaveMeta();
            foreach (var waiter in toCheck) Check(waiter);
            Changed?.Invoke();
        }

        private void Check(Waiter waiter)
        {
            if (waiter.Done.Task.IsCompleted) return;
            List<Entry> fresh;
            lock (gate)
            {
                fresh = entries.Values.Where(e => !waiter.Known.Contains(e.Id)).OrderByDescending(e => e.TakenUtc).ToList();
            }
            if (fresh.Count == 0) return;
            var names = new Names(library(), activity, sessions);
            var match = fresh.Select(e => ToDto(e, names)).FirstOrDefault(waiter.Wanted);
            if (match != null) waiter.Done.TrySetResult(match);
        }

        private static IEnumerable<FileInfo> Files(Root root)
        {
            IEnumerable<FileInfo> files;
            try
            {
                if (!Directory.Exists(root.Path)) return Enumerable.Empty<FileInfo>();
                files = new DirectoryInfo(root.Path).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            }
            catch (Exception e)
            {
                logger.Warn(e, $"Can't read {root.Path}");
                return Enumerable.Empty<FileInfo>();
            }
            return files.Where(f =>
                (PhotoExtensions.Contains(f.Extension) || VideoExtensions.Contains(f.Extension))
                && (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
                && (!root.NvidiaNamesOnly || NvidiaName.IsMatch(f.Name)));
        }

        // Whoever wrote the file has closed it.
        private static bool Settled(FileInfo file)
        {
            try
            {
                using (new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private Entry Index(FileInfo file, Root root, bool video)
        {
            var written = file.LastWriteTimeUtc;
            Meta m;
            lock (gate)
            {
                meta.TryGetValue(file.FullName, out m);
            }
            if (m == null || m.Bytes != file.Length || m.Written != written.Ticks)
            {
                var info = MediaInfo.Read(file.FullName);
                m = new Meta
                {
                    Path = file.FullName,
                    Bytes = file.Length,
                    Written = written.Ticks,
                    Width = info.Width,
                    Height = info.Height,
                    Seconds = info.Seconds,
                    // GeForce Experience names its Instant Replays "….DVR.mp4".
                    Replay = m?.Replay == true || file.Name.EndsWith(".DVR.mp4", StringComparison.OrdinalIgnoreCase),
                    Source = m?.Source,
                };
                lock (gate)
                {
                    meta[file.FullName] = m;
                    metaDirty = true;
                }
            }
            // A copied file is newer than its last write: the older of the two is when it was taken.
            var created = file.CreationTimeUtc;
            return new Entry
            {
                Id = IdOf(file.FullName),
                Path = file.FullName,
                Root = root,
                Video = video,
                Bytes = file.Length,
                TakenUtc = created < written ? created : written,
                WrittenUtc = written,
                Meta = m,
            };
        }

        private CaptureDto ToDto(Entry e, Names names)
        {
            var (gameId, game) = names.For(e.TakenUtc, Hint(e));
            return new CaptureDto
            {
                Id = e.Id,
                Kind = e.Video ? "video" : "photo",
                Replay = e.Video && e.Meta.Replay,
                TakenAt = e.TakenUtc.ToString("o"),
                Bytes = e.Bytes,
                Width = e.Meta.Width,
                Height = e.Meta.Height,
                Seconds = e.Video ? e.Meta.Seconds : null,
                GameId = gameId,
                Game = game,
                Tool = e.Root.Tool,
                FileName = System.IO.Path.GetFileName(e.Path),
                Version = e.WrittenUtc.Ticks.ToString("x"),
                Edited = e.Meta.Source != null,
                SourceId = e.Meta.Source != null && byId.ContainsKey(e.Meta.Source) ? e.Meta.Source : null,
            };
        }

        // What the tool called the game: NVIDIA's folder per game, or the start of the file's name.
        private static string Hint(Entry e)
        {
            var relative = e.Path.Substring(e.Root.Path.Length).TrimStart('\\', '/');
            var slash = relative.IndexOfAny(new[] { '\\', '/' });
            if (e.Root.Tool == "nvidia" && slash > 0) return relative.Substring(0, slash);
            var name = System.IO.Path.GetFileNameWithoutExtension(e.Path);
            // The clip editor's « (0.12-0.41)» and « (2)», so the piece keeps its clip's name.
            name = EditSuffix.Replace(name, "");
            if (name.EndsWith(".DVR", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            name = DateTail.Replace(name, "");
            if (name.EndsWith(" Screenshot", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 11);
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        private static string IdOf(string path)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
                return BitConverter.ToString(hash, 0, 12).Replace("-", "").ToLowerInvariant();
            }
        }

        private void LoadMeta()
        {
            if (meta != null) return;
            meta = new Dictionary<string, Meta>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(metaPath)) return;
                foreach (var m in Serialization.FromJson<List<Meta>>(File.ReadAllText(metaPath)) ?? new List<Meta>())
                {
                    if (m?.Path != null) meta[m.Path] = m;
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "Couldn't read the capture index, rebuilding it");
            }
        }

        private void SaveMeta()
        {
            string json;
            lock (gate)
            {
                if (!metaDirty || meta == null) return;
                metaDirty = false;
                json = Serialization.ToJson(meta.Values.ToList());
            }
            try
            {
                File.WriteAllText(metaPath, json);
            }
            catch (Exception e)
            {
                logger.Warn(e, "Couldn't save the capture index");
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                foreach (var w in watchers) w.Dispose();
                watchers.Clear();
            }
            timer.Dispose();
        }

        // Which game a moment and a name belong to, prepared once per listing.
        private class Names
        {
            // A session's file can be saved a little after the game closes (a clip being encoded).
            private static readonly TimeSpan Before = TimeSpan.FromMinutes(1);
            private static readonly TimeSpan After = TimeSpan.FromMinutes(2);

            private readonly List<(Guid game, DateTime from, DateTime to)> spans = new List<(Guid, DateTime, DateTime)>();
            private readonly Dictionary<Guid, string> titles = new Dictionary<Guid, string>();
            private readonly Dictionary<string, Guid> byName = new Dictionary<string, Guid>();

            public Names(IEnumerable<Game> library, GameActivityReader activity, GameSessions running)
            {
                foreach (var s in activity.All() ?? new List<SessionRecord>()) spans.Add((s.Game, s.Start - Before, s.Start.AddSeconds(s.Seconds) + After));
                var now = DateTime.UtcNow;
                foreach (var s in running.All.Where(s => s.StartedAt != null)) spans.Add((s.GameId, s.StartedAt.Value - Before, now + After));
                foreach (var g in library)
                {
                    titles[g.Id] = g.Name;
                    var key = Normalize(g.Name);
                    if (key.Length > 0 && !byName.ContainsKey(key)) byName[key] = g.Id;
                }
            }

            public (string id, string name) For(DateTime taken, string hint)
            {
                // The latest session that covers it: one game closing as the next one starts.
                var span = spans.Where(s => taken >= s.from && taken <= s.to).OrderByDescending(s => s.from).FirstOrDefault();
                if (span.game != Guid.Empty && titles.TryGetValue(span.game, out var title)) return (span.game.ToString(), title);
                if (hint != null && byName.TryGetValue(Normalize(hint), out var id)) return (id.ToString(), titles[id]);
                return (null, hint);
            }

            // "ELDEN RING™" and "Elden Ring" are the same game.
            private static string Normalize(string name) =>
                new string((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }
    }
}
