using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace CouchPilot.Captures
{
    public class TrimBody
    {
        public double From { get; set; }
        public double To { get; set; }
        // mp4 (the default) or gif.
        public string Format { get; set; }
        // mp4 only: keep the sound. GIFs have none.
        public bool Audio { get; set; } = true;
    }

    // Cuts a piece out of a clip, on the PC, through the bundled ffmpeg: a shorter MP4 to share,
    // or a GIF for the chats that want one. The piece lands next to the clip it came from, dated
    // like it, so the gallery shows the two together under the same game.
    //
    // MP4s are re-encoded (H.264 with x264, AAC), which makes the cut land on the frame you
    // chose rather than on the nearest keyframe seconds away; HDR clips are tone-mapped like the
    // thumbnails, or they'd come out grey. Resolution is capped at 1080p: a phone is where this
    // is going. One cut at a time: encoding is what the CPU can do, and two at once would only
    // make both slower.
    public class ClipEditor
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);
        public const double MinSeconds = 0.5;
        public const double MaxGifSeconds = 20;

        private readonly CaptureLibrary library;
        private readonly SemaphoreSlim busy = new SemaphoreSlim(1, 1);
        private readonly Lazy<string> encoders;

        public ClipEditor(CaptureLibrary library)
        {
            this.library = library;
            encoders = new Lazy<string>(ListEncoders);
        }

        // The bundled ffmpeg is there: clips can be cut at all.
        public bool Available => VideoFrame.Available;

        // x264 is in: cuts land on the frame. Without it the piece is copied from the nearest
        // keyframe before the start, and may begin a few seconds early.
        public bool Precise => encoders.Value.Contains(" libx264 ");

        public bool Gif => encoders.Value.Contains(" gif ");

        public async Task<CaptureDto> Trim(string id, TrimBody body)
        {
            if (!Available) throw new CaptureException("no-ffmpeg", "Questa versione di CouchPilot non include ffmpeg: non posso tagliare le clip.");
            var source = library.ThumbnailOf(id) ?? throw new CaptureException("not-found", "Clip non trovata.");
            if (!source.Video) throw new CaptureException("not-video", "Si tagliano solo le clip.");
            var gif = string.Equals(body?.Format, "gif", StringComparison.OrdinalIgnoreCase);
            if (gif && !Gif) throw new CaptureException("no-gif", "Questa versione di ffmpeg non sa fare GIF.");
            var from = Math.Max(0, body?.From ?? 0);
            var to = body?.To ?? source.Seconds ?? 0;
            if (source.Seconds != null) to = Math.Min(to, source.Seconds.Value);
            if (to - from < MinSeconds) throw new CaptureException("too-short", "Scegli almeno mezzo secondo.");
            if (gif && to - from > MaxGifSeconds) throw new CaptureException("too-long", $"Una GIF può durare al massimo {MaxGifSeconds:0} secondi.");

            if (!await busy.WaitAsync(0)) throw new CaptureException("busy", "Sto ancora tagliando la clip di prima.");
            try
            {
                var target = TargetPath(source.Path, from, to, gif ? ".gif" : ".mp4");
                var hdr = VideoFrame.IsHdr(source.Path);
                var arguments = gif ? GifArguments(source.Path, from, to, hdr, target) : Mp4Arguments(source.Path, from, to, hdr, body?.Audio ?? true, target);
                var (code, errors) = await Task.Run(() => Run(arguments));
                if (code != 0 || !File.Exists(target))
                {
                    TryDelete(target);
                    logger.Warn($"ffmpeg failed trimming {source.Path}: {Tail(errors)}");
                    throw new CaptureException("trim-failed", "Il PC non è riuscito a tagliare la clip.");
                }
                // Dated like the moment it shows, so it sits with its clip in the gallery and under
                // the same game; half a second later than the clip itself, to come after it.
                var when = source.TakenUtc.AddSeconds(from + 0.5);
                try
                {
                    File.SetCreationTimeUtc(target, when);
                    File.SetLastWriteTimeUtc(target, when);
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Couldn't date the trimmed clip");
                }
                return library.Adopt(target, id) ?? throw new CaptureException("trim-failed", "La clip tagliata non compare nella cartella.");
            }
            finally
            {
                busy.Release();
            }
        }

        private string Mp4Arguments(string source, double from, double to, bool hdr, bool audio, string target)
        {
            var length = (to - from).ToString("0.###", CultureInfo.InvariantCulture);
            var start = from.ToString("0.###", CultureInfo.InvariantCulture);
            if (!Precise)
            {
                // No encoder: copy the packets from the keyframe before the start.
                return $"-hide_banner -loglevel error -nostdin -y -ss {start} -i \"{source}\" -t {length} -map 0:v:0 {(audio ? "-map 0:a? -c:a copy" : "-an")} -c:v copy -avoid_negative_ts make_zero -movflags +faststart \"{target}\"";
            }
            var filter = "scale=w='min(1920,iw)':h=-2" + (hdr ? "," + VideoFrame.TonemapFilter : "") + ",format=yuv420p";
            var sound = audio ? "-map 0:a? -c:a aac -b:a 160k -ac 2" : "-an";
            // -ss before -i seeks to the keyframe before, decodes from there and starts the output
            // on the frame asked for: exact, and no slower than it has to be.
            return $"-hide_banner -loglevel error -nostdin -y -ss {start} -i \"{source}\" -t {length} -map 0:v:0 {sound} " +
                $"-vf \"{filter}\" -c:v libx264 -preset veryfast -crf 20 -profile:v high -level 4.2 -movflags +faststart \"{target}\"";
        }

        private static string GifArguments(string source, double from, double to, bool hdr, string target)
        {
            var length = (to - from).ToString("0.###", CultureInfo.InvariantCulture);
            var start = from.ToString("0.###", CultureInfo.InvariantCulture);
            // 15 frames a second at 480 px wide, with a palette made for this very clip: far
            // better colours than the generic 256, and a smaller file.
            var filter = "fps=15,scale=w=480:h=-2:flags=lanczos" + (hdr ? "," + VideoFrame.TonemapFilter : "") +
                ",split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle";
            return $"-hide_banner -loglevel error -nostdin -y -ss {start} -i \"{source}\" -t {length} -an -filter_complex \"{filter}\" -loop 0 \"{target}\"";
        }

        // «Elden Ring 2026-10-05 21-42-13 (0.12-0.41).mp4», next to the clip; (2), (3)… if taken.
        private static string TargetPath(string source, double from, double to, string extension)
        {
            var folder = Path.GetDirectoryName(source);
            var name = Path.GetFileNameWithoutExtension(source);
            var range = $"({Clock(from)}-{Clock(to)})";
            var candidate = Path.Combine(folder, $"{name} {range}{extension}");
            for (var n = 2; File.Exists(candidate); n++)
            {
                candidate = Path.Combine(folder, $"{name} {range} ({n}){extension}");
            }
            return candidate;
        }

        // 0.12, 1.05, 1.02.07: a clock with dots, since colons can't be in a file name.
        private static string Clock(double seconds)
        {
            var s = (int)Math.Floor(seconds);
            var h = s / 3600;
            var m = s % 3600 / 60;
            var rest = (s % 60).ToString("00");
            return h > 0 ? $"{h}.{m:00}.{rest}" : $"{m}.{rest}";
        }

        private static (int code, string errors) Run(string arguments)
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
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
                var errors = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    return (-1, "timed out");
                }
                Task.WaitAll(errors, output);
                return (process.ExitCode, errors.Result);
            }
        }

        private static string ListEncoders()
        {
            if (!VideoFrame.Available) return "";
            try
            {
                var start = new ProcessStartInfo(VideoFrame.Executable, "-hide_banner -loglevel error -encoders")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using (var process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    // One encoder a line: " V..... libx264   libx264 H.264 …". Padded so a
                    // name can be looked for with spaces around it.
                    return string.Join("\n", output.Split('\n').Select(l => " " + string.Join(" ", l.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(2)) + " "));
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "Couldn't list ffmpeg's encoders");
                return "";
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
    }
}
