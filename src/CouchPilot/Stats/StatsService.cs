using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CouchPilot.Games;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace CouchPilot.Stats
{
    // How much you play: by day, week, month and year from our own session log, and the library's
    // totals from Playnite. All in the PC's local time.
    //
    // A day of play runs from 5:00 to 5:00: a session from 23:00 to 1:00 is all the evening's.
    public class StatsService
    {
        public static readonly TimeSpan DayStartsAt = TimeSpan.FromHours(5);
        private static readonly TimeSpan ResumeAfter = TimeSpan.FromDays(90);
        private const int ResumeMinSeconds = 3600;

        private readonly IPlayniteAPI api;
        private readonly SessionLog log;
        private readonly GameSessions running;

        public StatsService(IPlayniteAPI api, SessionLog log, GameSessions running)
        {
            this.api = api;
            this.log = log;
            this.running = running;
        }

        // range: week, month or year; offset: 0 the current one, -1 the one before, and so on.
        public object Range(string range, int offset)
        {
            var now = DateTime.Now;
            var today = DayOf(now);
            DateTime from, to;
            Func<DateTime, DateTime> bucketOf;
            switch (range)
            {
                case "month":
                    from = new DateTime(today.Year, today.Month, 1).AddMonths(offset);
                    to = from.AddMonths(1);
                    bucketOf = d => d;
                    break;
                case "year":
                    from = new DateTime(today.Year + offset, 1, 1);
                    to = from.AddYears(1);
                    bucketOf = d => new DateTime(d.Year, d.Month, 1);
                    break;
                default:
                    range = "week";
                    from = MondayOf(today).AddDays(7 * offset);
                    to = from.AddDays(7);
                    bucketOf = d => d;
                    break;
            }
            var previousFrom = range == "month" ? from.AddMonths(-1) : range == "year" ? from.AddYears(-1) : from.AddDays(-7);
            // The period under way is compared with the previous one up to the same point (the
            // first 5 days of last month for the 5th of this one), not with all of it.
            var previousTo = from;
            if (today >= from && today < to)
            {
                var sameDay = previousFrom + (today - from) + TimeSpan.FromDays(1);
                if (sameDay < previousTo) previousTo = sameDay;
            }

            var sessions = Sessions(now);
            var days = Split(sessions);

            var buckets = new SortedDictionary<DateTime, long>();
            for (var d = from; d < to; d = range == "year" ? d.AddMonths(1) : d.AddDays(1)) buckets[d] = 0;
            var perGame = new Dictionary<Guid, long>();
            long total = 0, previous = 0;
            foreach (var part in days)
            {
                if (part.Day >= from && part.Day < to)
                {
                    total += part.Seconds;
                    buckets[bucketOf(part.Day)] += part.Seconds;
                    perGame[part.Game] = (perGame.TryGetValue(part.Game, out var s) ? s : 0) + part.Seconds;
                }
                else if (part.Day >= previousFrom && part.Day < previousTo)
                {
                    previous += part.Seconds;
                }
            }

            var games = api.Database.Games;
            return new
            {
                range,
                offset,
                from = Date(from),
                to = Date(to),
                today = Date(today),
                trackingSince = sessions.Count == 0 ? null : Date(DayOf(sessions.Min(x => x.Start).ToLocalTime())),
                totalSeconds = total,
                previousTotalSeconds = previous,
                previousFrom = Date(previousFrom),
                sessions = sessions.Count(x => DayOf(x.Start.ToLocalTime()) >= from && DayOf(x.Start.ToLocalTime()) < to),
                buckets = buckets.Select(b => new { date = Date(b.Key), seconds = b.Value }).ToList(),
                top = perGame.OrderByDescending(p => p.Value).Take(5).Select(p =>
                {
                    var g = games.Get(p.Key);
                    return new { id = p.Key.ToString(), name = g?.Name ?? "Gioco rimosso", seconds = p.Value, hasCover = !string.IsNullOrEmpty(g?.CoverImage) };
                }).ToList(),
                library = Library(),
                resume = Resume(now),
            };
        }

        // One game: Playnite's totals, and what the log knows about its sessions.
        public object Game(Guid id)
        {
            var game = api.Database.Games.Get(id);
            if (game == null) return null;
            var now = DateTime.Now;
            var sessions = Sessions(now);
            var mine = sessions.Where(s => s.Game == id).ToList();
            var thisWeek = MondayOf(DayOf(now));
            var weeks = new long[12];
            foreach (var part in Split(mine))
            {
                var index = 11 - (int)((thisWeek - MondayOf(part.Day)).TotalDays / 7);
                if (index >= 0 && index < 12) weeks[index] += part.Seconds;
            }
            var last = mine.OrderByDescending(s => s.Start).FirstOrDefault();
            return new
            {
                playtimeSeconds = (long)game.Playtime,
                playCount = (long)game.PlayCount,
                lastActivity = game.LastActivity?.ToUniversalTime().ToString("o"),
                completionStatus = game.CompletionStatus?.Name,
                sessions = mine.Count,
                averageSeconds = mine.Count == 0 ? 0 : (long)mine.Average(s => s.Seconds),
                weeks,
                lastSession = last == null ? null : new { start = last.Start.ToString("o"), seconds = last.Seconds },
                trackingSince = sessions.Count == 0 ? null : Date(DayOf(sessions.Min(x => x.Start).ToLocalTime())),
            };
        }

        private object Library()
        {
            var visible = api.Database.Games.Where(g => !g.Hidden).ToList();
            var thisYear = new DateTime(DateTime.Now.Year, 1, 1);
            return new
            {
                playtimeSeconds = visible.Sum(g => (long)g.Playtime),
                games = visible.Count,
                installed = visible.Count(g => g.IsInstalled),
                installedNeverPlayed = visible.Count(g => g.IsInstalled && g.Playtime == 0),
                playedThisYear = visible.Count(g => g.LastActivity >= thisYear),
            };
        }

        // Installed games you put real time into and then left: an hour or more, nothing for
        // three months. The longest-played first.
        private object Resume(DateTime now) => api.Database.Games
            .Where(g => !g.Hidden && g.IsInstalled && g.Playtime >= ResumeMinSeconds && g.LastActivity != null && now - g.LastActivity.Value > ResumeAfter)
            .OrderByDescending(g => g.Playtime)
            .Take(6)
            .Select(g => new
            {
                id = g.Id.ToString(),
                name = g.Name,
                playtimeSeconds = (long)g.Playtime,
                lastActivity = g.LastActivity?.ToUniversalTime().ToString("o"),
                hasCover = !string.IsNullOrEmpty(g.CoverImage),
            })
            .ToList();

        // The log, plus whatever is being played right now, up to now.
        private List<SessionRecord> Sessions(DateTime now)
        {
            var all = log.All();
            foreach (var s in running.All)
            {
                if (s.StartedAt == null) continue;
                var seconds = (long)(now.ToUniversalTime() - s.StartedAt.Value).TotalSeconds;
                if (seconds > 0) all.Add(new SessionRecord { Game = s.GameId, Start = s.StartedAt.Value, Seconds = seconds });
            }
            return all;
        }

        private struct DayPart
        {
            public Guid Game;
            public DateTime Day;
            public long Seconds;
        }

        // Each session cut at the 5:00 boundaries it crosses: a night-long session counts for
        // both days it spans.
        private static IEnumerable<DayPart> Split(IEnumerable<SessionRecord> sessions)
        {
            foreach (var s in sessions)
            {
                var start = s.Start.ToLocalTime();
                var end = start.AddSeconds(s.Seconds);
                while (start < end)
                {
                    var day = DayOf(start);
                    var boundary = day.AddDays(1) + DayStartsAt;
                    var stop = end < boundary ? end : boundary;
                    yield return new DayPart { Game = s.Game, Day = day, Seconds = (long)(stop - start).TotalSeconds };
                    start = stop;
                }
            }
        }

        public static DateTime DayOf(DateTime local) => (local - DayStartsAt).Date;

        private static DateTime MondayOf(DateTime day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

        private static string Date(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
