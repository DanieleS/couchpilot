using System;
using System.Linq;
using System.Threading;

namespace CouchPilot
{
    // CouchPilot speaks English and Italian. What shows on the PC (settings, notifications, the
    // PIN, Home Assistant's entity names) follows Playnite's language; what goes back to a phone
    // follows the phone's, from the Accept-Language its browser sends with every request.
    // Every string sits where it's used, both languages side by side: Tr("Game not found.", "Gioco non trovato.")
    // for what a phone reads, Pc("Port", "Porta") for what the PC shows, whoever set it off.
    public static class Lang
    {
        private static readonly AsyncLocal<bool?> requestItalian = new AsyncLocal<bool?>();

        // Set once at startup from Playnite's settings ("it_IT", "en_US"…).
        public static bool PlayniteItalian { get; set; }

        public static bool Italian => requestItalian.Value ?? PlayniteItalian;

        public static string Tr(string en, string it) => Italian ? it : en;

        // Playnite's language even inside a phone's request: the PIN a phone asks for goes up on
        // the PC's screen, in the PC's words.
        public static string Pc(string en, string it) => PlayniteItalian ? it : en;

        public static void UsePlaynite(string language) =>
            PlayniteItalian = language != null && language.StartsWith("it", StringComparison.OrdinalIgnoreCase);

        // For the rest of this request: the first of Italian and English the phone asks for,
        // as the web app picks its own. Neither: Playnite's.
        public static void UseRequest(string acceptLanguage)
        {
            var first = (acceptLanguage ?? "")
                .Split(',')
                .Select(part => part.Split(';'))
                .Select((part, i) => new { Tag = part[0].Trim().ToLowerInvariant(), Q = Quality(part), Order = i })
                .Where(l => l.Q > 0 && (l.Tag.StartsWith("it") || l.Tag.StartsWith("en")))
                .OrderByDescending(l => l.Q)
                .ThenBy(l => l.Order)
                .FirstOrDefault();
            requestItalian.Value = first == null ? (bool?)null : first.Tag.StartsWith("it");
        }

        private static double Quality(string[] part)
        {
            foreach (var p in part.Skip(1))
            {
                var kv = p.Trim();
                if (kv.StartsWith("q=") && double.TryParse(kv.Substring(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var q)) return q;
            }
            return 1;
        }
    }
}
