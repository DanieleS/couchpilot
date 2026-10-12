using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace CouchPilot.Captures
{
    public class ClipPreviewDto
    {
        // ready: the light version is there, at /light/file.
        // queued, making: on its way; `progress` from 0 to 1 while making.
        // none: not made, and nobody asked yet.
        // original: the clip is light enough already, or the PC can't make one (no ffmpeg with
        //   x264), or it failed at this one: the phone plays the file itself.
        public string State { get; set; }
        public double? Progress { get; set; }
        public long? Bytes { get; set; }
    }

    // A light copy of each clip for watching on the phone: 720p, 30 frames a second, H.264 at a
    // few Mbit/s with its index up front, where NVIDIA and Game Bar write tens of Mbit/s at 1080p
    // or more, often HEVC or HDR. Over the house's Wi-Fi the original stalls every few seconds;
    // this plays at once and seeks anywhere. Saving and sharing still take the original.
    //
    // Made by the bundled ffmpeg, one at a time, at below-normal priority. A clip the phone opens
    // goes first; new clips are made ahead, but only while no game is running, so the encoder
    // never takes the CPU from a game. Kept in the extension's data folder (`previews`), the ones
    // watched least recently going first past MaxBytes.
    public class ClipPreviews : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);
        private const int LongSide = 1280;
        // Below this the original streams fine: no copy for it.
        private const double LightBitsPerSecond = 6_000_000;
        private const long MaxBytes = 4L * 1024 * 1024 * 1024;

        private readonly string folder;
        private readonly Func<bool> idle;
        private readonly Lazy<bool> available;
        private readonly object gate = new object();
        // Asked for by a phone, and made ahead: the first are made first.
        private readonly LinkedList<Job> wanted = new LinkedList<Job>();
        private readonly LinkedList<Job> ahead = new LinkedList<Job>();
        private readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, bool> failed = new ConcurrentDictionary<string, bool>();
        private Job current;
        private Process encoder;

        private class Job
        {
            public ThumbnailSource Source;
            public string Target;
            public double Progress;
        }

        // `idle`: no game is running, so clips may be made ahead. `x264`: the bundled ffmpeg can
        // encode H.264 (the clip editor knows).
        public ClipPreviews(string folder, Func<bool> idle, Func<bool> x264)
        {
            this.folder = folder;
            this.idle = idle;
            available = new Lazy<bool>(() => VideoFrame.Available && x264());
            Task.Run(Work);
        }

        // Where the clip stands; with `want`, it's made next if it isn't made yet.
        public ClipPreviewDto State(ThumbnailSource source, bool want)
        {
            if (!source.Video || !available.Value || Light(source)) return new ClipPreviewDto { State = "original" };
            var target = TargetOf(source);
            if (File.Exists(target)) return new ClipPreviewDto { State = "ready", Bytes = new FileInfo(target).Length };
            if (failed.ContainsKey(target) || File.Exists(Path.ChangeExtension(target, ".none"))) return new ClipPreviewDto { State = "original" };
            lock (gate)
            {
                if (!jobs.TryGetValue(target, out var job))
                {
                    if (!want) return new ClipPreviewDto { State = "none" };
                    job = Add(source, target, wanted);
                }
                else if (want && job != current && ahead.Remove(job))
                {
                    wanted.AddLast(job);
                    signal.Release();
                }
                return job == current
                    ? new ClipPreviewDto { State = "making", Progress = Math.Round(job.Progress, 3) }
                    : new ClipPreviewDto { State = "queued" };
            }
        }

        // The light copy, if it's made; marked as just watched, so it's the last to be cleared.
        public string PathOf(ThumbnailSource source)
        {
            var target = TargetOf(source);
            if (!File.Exists(target)) return null;
            try
            {
                File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
            }
            catch (Exception)
            {
            }
            return target;
        }

        // Clips that just landed: made in the background while no game is running.
        public void Ahead(IEnumerable<ThumbnailSource> sources)
        {
            if (!available.Value) return;
            foreach (var source in sources.Where(s => s.Video && !Light(s)))
            {
                var target = TargetOf(source);
                if (File.Exists(target) || failed.ContainsKey(target) || File.Exists(Path.ChangeExtension(target, ".none"))) continue;
                lock (gate)
                {
                    if (!jobs.ContainsKey(target)) Add(source, target, ahead);
                }
            }
        }

        // The game stopped: what's waiting to be made ahead can go now.
        public void Wake() => signal.Release();

        // The clip went to the Recycle Bin: its copy goes too.
        public void Forget(string id)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, id + "-*.*").ToList()) TryDelete(file);
        }

        private Job Add(ThumbnailSource source, string target, LinkedList<Job> into)
        {
            var job = new Job { Source = source, Target = target };
            jobs[target] = job;
            into.AddLast(job);
            signal.Release();
            return job;
        }

        private string TargetOf(ThumbnailSource source) => Path.Combine(folder, $"{source.Id}-{source.Version}.mp4");

        // Already a few Mbit/s at most: it streams as it is.
        private static bool Light(ThumbnailSource source)
        {
            if (source.Seconds == null || source.Seconds <= 0) return false;
            try
            {
                return new FileInfo(source.Path).Length * 8 / source.Seconds.Value <= LightBitsPerSecond;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task Work()
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    // Woken by a new job, or every half minute to see whether the game has closed.
                    await signal.WaitAsync(TimeSpan.FromSeconds(30), stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                while (!stop.IsCancellationRequested)
                {
                    Job job;
                    lock (gate)
                    {
                        job = wanted.First?.Value ?? (idle() ? ahead.First?.Value : null);
                        if (job == null) break;
                        if (wanted.Count > 0) wanted.RemoveFirst();
                        else ahead.RemoveFirst();
                        current = job;
                    }
                    try
                    {
                        Make(job);
                    }
                    catch (Exception e)
                    {
                        logger.Warn(e, $"No light copy of {job.Source.Path}");
                        failed[job.Target] = true;
                    }
                    lock (gate)
                    {
                        jobs.Remove(job.Target);
                        current = null;
                    }
                }
            }
        }

        private void Make(Job job)
        {
            Directory.CreateDirectory(folder);
            var part = Path.ChangeExtension(job.Target, ".part.mp4");
            var hdr = VideoFrame.IsHdr(job.Source.Path);
            // Long side at most 1280, even sizes for x264; HDR tone-mapped like the thumbnails.
            var filter = $"scale=w='if(gte(iw,ih),min({LongSide},iw),-2)':h='if(gte(iw,ih),-2,min({LongSide},ih))'" +
                (hdr ? "," + VideoFrame.TonemapFilter : "") + ",format=yuv420p";
            // CRF for quality, capped at 4 Mbit/s so a busy scene can't outrun the Wi-Fi; a
            // keyframe every two seconds, so seeking lands at once. Only the first sound track:
            // NVIDIA can keep the microphone on a second one.
            var arguments =
                $"-hide_banner -loglevel error -nostdin -nostats -y -i \"{job.Source.Path}\" -map 0:v:0 -map 0:a:0? " +
                $"-vf \"{filter}\" -fpsmax 30 -c:v libx264 -preset veryfast -crf 24 -maxrate 4M -bufsize 8M " +
                "-profile:v high -level 4.1 -g 60 -c:a aac -b:a 128k -ac 2 -movflags +faststart " +
                $"-progress pipe:1 \"{part}\"";
            var (code, errors) = Run(arguments, job);
            if (code != 0 || !File.Exists(part))
            {
                TryDelete(part);
                logger.Warn($"ffmpeg failed making a light copy of {job.Source.Path}: {Tail(errors)}");
                File.WriteAllBytes(Path.ChangeExtension(job.Target, ".none"), new byte[0]);
                return;
            }
            // Older versions of this clip's copy go; this one takes its place whole.
            foreach (var old in Directory.EnumerateFiles(folder, job.Source.Id + "-*.*").Where(f => !string.Equals(f, part, StringComparison.OrdinalIgnoreCase)).ToList()) TryDelete(old);
            File.Move(part, job.Target);
            Trim();
        }

        private (int code, string errors) Run(string arguments, Job job)
        {
            var start = new ProcessStartInfo(VideoFrame.Executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using (var process = Process.Start(start))
            {
                lock (gate)
                {
                    encoder = process;
                }
                try
                {
                    process.PriorityClass = ProcessPriorityClass.BelowNormal;
                    var errors = process.StandardError.ReadToEndAsync();
                    // -progress writes key=value lines: out_time_us is how far it got.
                    var seconds = job.Source.Seconds ?? 0;
                    var progress = Task.Run(() =>
                    {
                        string line;
                        while ((line = process.StandardOutput.ReadLine()) != null)
                        {
                            if (seconds <= 0 || !line.StartsWith("out_time_us=")) continue;
                            if (long.TryParse(line.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us > 0)
                            {
                                job.Progress = Math.Min(1, us / 1e6 / seconds);
                            }
                        }
                    });
                    if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
                    {
                        Kill(process);
                        return (-1, "timed out");
                    }
                    Task.WaitAll(errors, progress);
                    return (process.ExitCode, errors.Result);
                }
                finally
                {
                    lock (gate)
                    {
                        encoder = null;
                    }
                }
            }
        }

        // Past MaxBytes, the copies watched least recently go.
        private void Trim()
        {
            var files = new DirectoryInfo(folder).EnumerateFiles("*.mp4")
                .Where(f => !f.Name.EndsWith(".part.mp4", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            long total = 0;
            foreach (var file in files)
            {
                total += file.Length;
                if (total > MaxBytes) TryDelete(file.FullName);
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        private static string Tail(string text)
        {
            text = (text ?? "").Trim();
            return text.Length > 600 ? text.Substring(text.Length - 600) : text;
        }

        public void Dispose()
        {
            stop.Cancel();
            lock (gate)
            {
                if (encoder != null) Kill(encoder);
            }
        }
    }
}
