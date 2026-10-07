using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace CouchPilot.Captures
{
    // Small JPEGs for the gallery's grid, made once and kept in the extension's data folder.
    // Screenshots are scaled down here; videos go through the bundled ffmpeg (VideoFrame), or,
    // in a build without it, the frame Explorer shows, through the shell, which wants an STA
    // thread. One worker makes them all, one at a time, so a gallery opening on the phone can't
    // load the PC with a hundred decoders at once. Screenshots also get a preview, big enough for
    // the phone's screen and a fraction of the PNG's weight, for the viewer: the file itself only
    // travels when it's saved or shared. Previews jump the queue, since someone is waiting on one.
    public class Thumbnails : IDisposable
    {
        private const int LongSide = 480;
        private const int PreviewLongSide = 1920;
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly string folder;
        private readonly BlockingCollection<Job> queue = new BlockingCollection<Job>();
        private readonly BlockingCollection<Job> urgent = new BlockingCollection<Job>();
        private readonly ConcurrentDictionary<string, Task<byte[]>> making = new ConcurrentDictionary<string, Task<byte[]>>();
        private readonly Thread worker;

        private class Job
        {
            public string Source;
            public string Target;
            public bool Video;
            // A strip of frames for the trimmer rather than one thumbnail.
            public bool Strip;
            // The viewer's preview of a screenshot rather than its thumbnail.
            public bool Preview;
            public double? Seconds;
            public TaskCompletionSource<byte[]> Done;
        }

        public const int StripFrames = 12;
        public const int StripFrameWidth = 160;

        public Thumbnails(string folder)
        {
            this.folder = folder;
            worker = new Thread(Work) { IsBackground = true, Name = "CouchPilot thumbnails" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        // The thumbnail's JPEG, or null when the file has none (a clip that's damaged, say).
        public Task<byte[]> Get(string id, string version, bool video, double? seconds, string source)
        {
            var target = Path.Combine(folder, $"{id}-{version}.jpg");
            if (File.Exists(target)) return Task.FromResult(File.ReadAllBytes(target));
            // Already failed at this one: no point trying again on every request.
            if (File.Exists(Path.ChangeExtension(target, ".none"))) return Task.FromResult<byte[]>(null);
            return Enqueue(new Job { Source = source, Target = target, Video = video, Seconds = seconds }, queue);
        }

        // The viewer's JPEG of a screenshot, up to 1920 px; null when it can't be read.
        public Task<byte[]> Preview(string id, string version, string source)
        {
            var target = Path.Combine(folder, $"{id}-{version}-view.jpg");
            if (File.Exists(target)) return Task.FromResult(File.ReadAllBytes(target));
            return Enqueue(new Job { Source = source, Target = target, Preview = true }, urgent);
        }

        // The trimmer's strip of frames, made once like a thumbnail; null without ffmpeg or for a clip it can't read.
        public Task<byte[]> Strip(string id, string version, double? seconds, string source)
        {
            if (!VideoFrame.Available) return Task.FromResult<byte[]>(null);
            var target = Path.Combine(folder, $"{id}-{version}-strip.jpg");
            if (File.Exists(target)) return Task.FromResult(File.ReadAllBytes(target));
            if (File.Exists(Path.ChangeExtension(target, ".none"))) return Task.FromResult<byte[]>(null);
            return Enqueue(new Job { Source = source, Target = target, Video = true, Strip = true, Seconds = seconds }, queue);
        }

        // One job per file, however many requests ask for it while it's being made.
        private Task<byte[]> Enqueue(Job job, BlockingCollection<Job> into)
        {
            return making.GetOrAdd(job.Target, _ =>
            {
                job.Done = new TaskCompletionSource<byte[]>();
                into.Add(job);
                return job.Done.Task.ContinueWith(t =>
                {
                    making.TryRemove(job.Target, out var __);
                    return t.Result;
                }, TaskScheduler.Default);
            });
        }

        // Makes it in the background if it isn't made yet.
        public void Warm(ThumbnailSource c)
        {
            var target = Path.Combine(folder, $"{c.Id}-{c.Version}.jpg");
            if (File.Exists(target) || File.Exists(Path.ChangeExtension(target, ".none"))) return;
            Get(c.Id, c.Version, c.Video, c.Seconds, c.Path);
        }

        // A screenshot just taken is likely opened next: its preview is made now too.
        public void WarmPreview(ThumbnailSource c)
        {
            if (!HasPreview(c)) return;
            if (File.Exists(Path.Combine(folder, $"{c.Id}-{c.Version}-view.jpg"))) return;
            Preview(c.Id, c.Version, c.Path);
        }

        // Screenshots, but not the GIFs the clip editor made: a still of one wouldn't move.
        public static bool HasPreview(ThumbnailSource c) =>
            !c.Video && !string.Equals(Path.GetExtension(c.Path), ".gif", StringComparison.OrdinalIgnoreCase);

        // Thumbnails of files that are gone.
        public void Forget(string id)
        {
            try
            {
                ClearOld(id);
            }
            catch (Exception e)
            {
                logger.Warn(e, "Couldn't remove a thumbnail");
            }
        }

        private void Work()
        {
            var queues = new[] { urgent, queue };
            while (true)
            {
                Job job;
                try
                {
                    // The first queue with something in it: previews before thumbnails.
                    BlockingCollection<Job>.TakeFromAny(queues, out job);
                }
                catch (ArgumentException)
                {
                    return; // Both completed: the extension is closing.
                }
                byte[] jpeg = null;
                try
                {
                    jpeg = Make(job);
                }
                catch (Exception e)
                {
                    logger.Warn(e, $"No thumbnail for {job.Source}");
                }
                if (jpeg == null && job.Video) MarkFailed(job.Target);
                job.Done.TrySetResult(jpeg);
            }
        }

        private void MarkFailed(string target)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.ChangeExtension(target, ".none"), new byte[0]);
            }
            catch (Exception e)
            {
                logger.Warn(e, "Couldn't note a missing thumbnail");
            }
        }

        private byte[] Make(Job job)
        {
            if (job.Strip)
            {
                var strip = VideoFrame.Strip(job.Source, job.Seconds, StripFrames, StripFrameWidth);
                return strip == null ? null : Save(job.Target, strip);
            }
            if (job.Video && VideoFrame.Available)
            {
                var frame = VideoFrame.Grab(job.Source, job.Seconds, LongSide);
                return frame == null ? null : Save(job.Target, frame);
            }
            var longSide = job.Preview ? PreviewLongSide : LongSide;
            using (var source = job.Video ? Native.Shell.Thumbnail(job.Source, LongSide) : Load(job.Source))
            {
                if (source == null) return null;
                var scale = Math.Min(1.0, (double)longSide / Math.Max(source.Width, source.Height));
                var width = Math.Max(1, (int)Math.Round(source.Width * scale));
                var height = Math.Max(1, (int)Math.Round(source.Height * scale));
                using (var small = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(source, 0, 0, width, height);
                    }
                    using (var output = new MemoryStream())
                    {
                        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        var quality = new EncoderParameters(1);
                        quality.Param[0] = new EncoderParameter(Encoder.Quality, job.Preview ? 85L : 82L);
                        small.Save(output, codec, quality);
                        return Save(job.Target, output.ToArray());
                    }
                }
            }
        }

        private byte[] Save(string target, byte[] jpeg)
        {
            Directory.CreateDirectory(folder);
            // Older versions of this capture's files go; this version's thumbnail, strip and preview stay together.
            var parts = Path.GetFileName(target).Split('-');
            ClearOld(parts[0], parts[0] + "-" + Path.GetFileNameWithoutExtension(parts[1]));
            File.WriteAllBytes(target, jpeg);
            return jpeg;
        }

        // Every file of this capture (thumbnail, strip, marks of failed attempts), except those
        // whose name starts with `keep`.
        private void ClearOld(string id, string keep = null)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, id + "-*.*").ToList())
            {
                if (keep != null && Path.GetFileNameWithoutExtension(file).StartsWith(keep, StringComparison.Ordinal)) continue;
                File.Delete(file);
            }
        }

        // Read fully into memory, so the file isn't held open while the gallery browses.
        private static Bitmap Load(string path)
        {
            using (var stream = new MemoryStream(File.ReadAllBytes(path)))
            using (var image = Image.FromStream(stream))
            {
                return new Bitmap(image);
            }
        }

        public void Dispose()
        {
            urgent.CompleteAdding();
            queue.CompleteAdding();
        }
    }
}
