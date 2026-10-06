using System;
using System.IO;
using System.Text;

namespace CouchPilot.Captures
{
    // Size and length of a capture, read from the file's own headers: a few small reads, no codec.
    public class MediaInfo
    {
        public int? Width { get; set; }
        public int? Height { get; set; }
        public double? Seconds { get; set; }

        public static MediaInfo Read(string path)
        {
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new BinaryReader(file))
                {
                    switch (Path.GetExtension(path).ToLowerInvariant())
                    {
                        case ".mp4": return Mp4(reader);
                        case ".png": return Png(reader);
                        case ".jpg":
                        case ".jpeg": return Jpeg(reader);
                    }
                }
            }
            catch (Exception)
            {
                // A file cut short or in an unexpected shape: shown without size and length.
            }
            return new MediaInfo();
        }

        // IHDR is always the first chunk: width and height at bytes 16 and 20, big-endian.
        private static MediaInfo Png(BinaryReader r)
        {
            r.BaseStream.Position = 16;
            return new MediaInfo { Width = BigInt32(r), Height = BigInt32(r) };
        }

        // Walk the markers to the first start-of-frame, which carries height then width.
        private static MediaInfo Jpeg(BinaryReader r)
        {
            if (r.ReadByte() != 0xFF || r.ReadByte() != 0xD8) return new MediaInfo();
            var stream = r.BaseStream;
            while (stream.Position < stream.Length)
            {
                if (r.ReadByte() != 0xFF) continue;
                var marker = r.ReadByte();
                while (marker == 0xFF) marker = r.ReadByte();
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;
                var length = BigUInt16(r);
                var isFrame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isFrame)
                {
                    r.ReadByte();
                    var height = BigUInt16(r);
                    return new MediaInfo { Width = BigUInt16(r), Height = height };
                }
                stream.Position += length - 2;
            }
            return new MediaInfo();
        }

        // moov/mvhd has the length; the first tkhd with a size is the video track's.
        private static MediaInfo Mp4(BinaryReader r)
        {
            var info = new MediaInfo();
            var moov = Find(r, 0, r.BaseStream.Length, "moov");
            if (moov == null) return info;
            var (moovStart, moovEnd) = moov.Value;

            var mvhd = Find(r, moovStart, moovEnd, "mvhd");
            if (mvhd != null)
            {
                r.BaseStream.Position = mvhd.Value.start;
                var version = r.ReadByte();
                r.BaseStream.Position += 3 + (version == 1 ? 16 : 8);
                var timescale = BigUInt32(r);
                var duration = version == 1 ? BigUInt64(r) : BigUInt32(r);
                // Fragmented files leave it at 0 and spread the length over their fragments.
                if (timescale > 0 && duration > 0) info.Seconds = Math.Round((double)duration / timescale, 2);
            }

            var at = moovStart;
            while (at < moovEnd)
            {
                var trak = Find(r, at, moovEnd, "trak");
                if (trak == null) break;
                at = trak.Value.end;
                var tkhd = Find(r, trak.Value.start, trak.Value.end, "tkhd");
                if (tkhd == null) continue;
                // Width and height are the header's last 8 bytes, 16.16 fixed point.
                r.BaseStream.Position = tkhd.Value.end - 8;
                var width = BigUInt32(r) >> 16;
                var height = BigUInt32(r) >> 16;
                if (width == 0 || height == 0) continue;
                info.Width = (int)width;
                info.Height = (int)height;
                break;
            }
            return info;
        }

        // The first box of this type between start and end: where its content starts and ends.
        private static (long start, long end)? Find(BinaryReader r, long start, long end, string type)
        {
            var at = start;
            while (at + 8 <= end)
            {
                r.BaseStream.Position = at;
                long size = BigUInt32(r);
                var name = Encoding.ASCII.GetString(r.ReadBytes(4));
                var header = 8L;
                if (size == 1)
                {
                    size = (long)BigUInt64(r);
                    header = 16;
                }
                else if (size == 0)
                {
                    size = end - at;
                }
                if (size < header) return null;
                if (name == type) return (at + header, Math.Min(end, at + size));
                at += size;
            }
            return null;
        }

        private static int BigInt32(BinaryReader r) => (int)BigUInt32(r);

        private static int BigUInt16(BinaryReader r) => (r.ReadByte() << 8) | r.ReadByte();

        private static uint BigUInt32(BinaryReader r)
        {
            var b = r.ReadBytes(4);
            if (b.Length < 4) throw new EndOfStreamException();
            return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        }

        private static ulong BigUInt64(BinaryReader r) => ((ulong)BigUInt32(r) << 32) | BigUInt32(r);
    }
}
