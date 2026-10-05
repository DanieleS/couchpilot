using System;

namespace CouchPilot.Server
{
    // The Range header of a request for part of a file. One range, the only kind video players
    // ask for: bytes=a-b, bytes=a- (to the end), bytes=-n (the last n).
    public static class ByteRange
    {
        // False when the header asks for something this file doesn't have, or in another form.
        public static bool TryParse(string header, long length, out long from, out long to)
        {
            from = 0;
            to = length - 1;
            if (header == null || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || header.Contains(",")) return false;
            var parts = header.Substring(6).Split('-');
            if (parts.Length != 2) return false;
            if (parts[0].Trim().Length == 0)
            {
                if (!long.TryParse(parts[1], out var suffix) || suffix <= 0 || length == 0) return false;
                from = Math.Max(0, length - suffix);
                return true;
            }
            if (!long.TryParse(parts[0], out from) || from < 0 || from >= length) return false;
            if (parts[1].Trim().Length > 0)
            {
                if (!long.TryParse(parts[1], out to) || to < from) return false;
                to = Math.Min(to, length - 1);
            }
            return true;
        }
    }
}
