using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace CouchPilot.Captures
{
    // One frame of a clip as a small JPEG, through the ffmpeg.exe shipped with the extension
    // (built by scripts/build-ffmpeg.sh). Unlike Windows' own thumbnails it decodes HEVC and AV1
    // whatever codecs the PC has, and tone-maps HDR clips, which would otherwise come out grey.
    public static class VideoFrame
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        public static string Executable { get; } =
            Path.Combine(Path.GetDirectoryName(typeof(VideoFrame).Assembly.Location) ?? "", "ffmpeg.exe");

        public static bool Available => File.Exists(Executable);

        // HDR (PQ or HLG) to SDR: to linear light, then Möbius' curve, which keeps the midtones
        // where they were (Hable's darkens them), back to BT.709. Shared with the clip editor.
        public const string TonemapFilter = "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=mobius:desat=0,zscale=t=bt709:m=bt709:r=tv";

        // The frame a moment in (the very first is often black), at most `longSide` pixels on its
        // long side; null if ffmpeg can't make one.
        public static byte[] Grab(string path, double? seconds, int longSide)
        {
            var hdr = IsHdr(path);
            var at = Math.Min(2, (seconds ?? 4) * 0.25);
            return Frame(path, at, hdr, longSide) ?? (at > 0 ? Frame(path, 0, hdr, longSide) : null);
        }

        // A strip of `frames` frames spread over the clip, side by side in one JPEG, each `width`
        // pixels wide: what the trimmer shows under its handles. Only keyframes are decoded, so a
        // long recording costs seconds, not minutes; each frame is the keyframe before its moment.
        public static byte[] Strip(string path, double? seconds, int frames, int width)
        {
            var length = Math.Max(1, seconds ?? 30);
            var hdr = IsHdr(path);
            var rate = (frames / length).ToString("0.######", CultureInfo.InvariantCulture);
            var filter = $"fps={rate}:start_time=0,scale=w={width}:h=-2" + (hdr ? "," + TonemapFilter : "") + $",tile={frames}x1,format=yuvj420p";
            var (code, output, _) = Run(
                $"-hide_banner -loglevel error -nostdin -skip_frame nokey -i \"{path}\" -frames:v 1 -an -vf \"{filter}\" -c:v mjpeg -q:v 5 -f image2pipe pipe:1");
            return code == 0 && output.Length > 3 && output[0] == 0xFF && output[1] == 0xD8 ? output : null;
        }

        private static byte[] Frame(string path, double at, bool hdr, int longSide)
        {
            var scale = $"scale=w={longSide}:h={longSide}:force_original_aspect_ratio=decrease";
            var filter = hdr ? scale + "," + TonemapFilter + ",format=yuvj420p" : scale + ",format=yuvj420p";
            var (code, output, _) = Run(
                $"-hide_banner -loglevel error -nostdin -ss {at.ToString(CultureInfo.InvariantCulture)} -i \"{path}\" " +
                $"-frames:v 1 -an -vf \"{filter}\" -c:v mjpeg -q:v 4 -f image2pipe pipe:1");
            return code == 0 && output.Length > 3 && output[0] == 0xFF && output[1] == 0xD8 ? output : null;
        }

        // ffmpeg describes the input on stderr; HDR shows as its transfer function.
        public static bool IsHdr(string path)
        {
            var (_, _, info) = Run($"-hide_banner -nostdin -i \"{path}\"");
            return info.Contains("smpte2084") || info.Contains("arib-std-b67");
        }

        private static (int code, byte[] output, string info) Run(string arguments)
        {
            var start = new ProcessStartInfo(Executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var process = Process.Start(start))
            using (var output = new MemoryStream())
            {
                // Both streams drained at once: either filling up would stall ffmpeg.
                var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
                var errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    return (-1, new byte[0], "");
                }
                Task.WaitAll(copy, errors);
                return (process.ExitCode, output.ToArray(), errors.Result);
            }
        }
    }
}
