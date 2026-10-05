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
    // Screenshots are scaled down here; videos get the frame Explorer shows, through the shell,
    // which wants an STA thread: one worker makes them all, one at a time, so a gallery opening
    // on the phone can't load the PC with a hundred decoders at once.
    public class Thumbnails : IDisposable
    {
        private const int LongSide = 480;
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly string folder;
        private readonly BlockingCollection<Job> queue = new BlockingCollection<Job>();
        private readonly ConcurrentDictionary<string, Task<byte[]>> making = new ConcurrentDictionary<string, Task<byte[]>>();
        private readonly Thread worker;

        private class Job
        {
            public string Source;
            public string Target;
            public bool Video;
            public TaskCompletionSource<byte[]> Done;
        }

        public Thumbnails(string folder)
        {
            this.folder = folder;
            worker = new Thread(Work) { IsBackground = true, Name = "CouchPilot thumbnails" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        // The thumbnail's JPEG, or null when the file has none (a video Windows can't decode).
        public Task<byte[]> Get(string id, string version, bool video, string source)
        {
            var target = Path.Combine(folder, $"{id}-{version}.jpg");
            if (File.Exists(target)) return Task.FromResult(File.ReadAllBytes(target));
            return making.GetOrAdd(target, _ =>
            {
                var job = new Job { Source = source, Target = target, Video = video, Done = new TaskCompletionSource<byte[]>() };
                queue.Add(job);
                return job.Done.Task.ContinueWith(t =>
                {
                    making.TryRemove(target, out var __);
                    return t.Result;
                }, TaskScheduler.Default);
            });
        }

        // Thumbnails of files that are gone.
        public void Forget(string id)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (var file in Directory.EnumerateFiles(folder, id + "-*.jpg").ToList()) File.Delete(file);
            }
            catch (Exception e)
            {
                logger.Warn(e, "Couldn't remove a thumbnail");
            }
        }

        private void Work()
        {
            foreach (var job in queue.GetConsumingEnumerable())
            {
                byte[] jpeg = null;
                try
                {
                    jpeg = Make(job);
                }
                catch (Exception e)
                {
                    logger.Warn(e, $"No thumbnail for {job.Source}");
                }
                job.Done.TrySetResult(jpeg);
            }
        }

        private byte[] Make(Job job)
        {
            using (var source = job.Video ? Native.Shell.Thumbnail(job.Source, LongSide) : Load(job.Source))
            {
                if (source == null) return null;
                var scale = Math.Min(1.0, (double)LongSide / Math.Max(source.Width, source.Height));
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
                        quality.Param[0] = new EncoderParameter(Encoder.Quality, 82L);
                        small.Save(output, codec, quality);
                        var bytes = output.ToArray();
                        Directory.CreateDirectory(folder);
                        foreach (var old in Directory.EnumerateFiles(folder, Path.GetFileName(job.Target).Split('-')[0] + "-*.jpg").ToList()) File.Delete(old);
                        File.WriteAllBytes(job.Target, bytes);
                        return bytes;
                    }
                }
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
            queue.CompleteAdding();
        }
    }
}
