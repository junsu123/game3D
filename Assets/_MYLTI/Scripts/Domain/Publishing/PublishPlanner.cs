using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mylti.Domain
{
    public sealed class WeeklySlot
    {
        public DayOfWeek DayUtc;
        public int HourUtc, MinuteUtc;
        public double Score;
        public override string ToString() => $"{DayUtc} {HourUtc:00}:{MinuteUtc:00} UTC (score {Score:0.00})";
    }

    /// <summary>
    /// Chooses publish times that suit the whole audience: a video should go live 1–4 hours before the evening
    /// viewing peak in the markets that matter (US first, Europe second). Viewing intensity is a simple model
    /// (evening peak 19–22 h local on weekdays, broader daytime on weekends) built from the public studies cited in
    /// docs/MARKET_RESEARCH.md — your own YouTube Analytics “When your viewers are on YouTube” should replace it later.
    /// </summary>
    public static class PublishPlanner
    {
        // relative viewing intensity by local hour (0–23)
        static readonly double[] Weekday = { .30, .18, .10, .06, .05, .06, .12, .22, .25, .25, .27, .30, .38, .40, .42, .48, .55, .65, .78, .90, 1.0, .98, .80, .52 };
        static readonly double[] Weekend = { .40, .25, .14, .08, .06, .06, .10, .20, .35, .55, .68, .75, .78, .78, .76, .76, .78, .82, .88, .95, 1.0, .95, .75, .52 };
        // how much each hour after publishing counts (the first hours after release decide the initial push)
        static readonly double[] AfterPublish = { .5, .85, 1.0, 1.0, 1.0, .9, .8, .7, .6, .5 };

        public static double Intensity(MarketZone z, DateTime utc)
        {
            var local = MarketClock.ToLocal(z, utc);
            bool weekend = local.DayOfWeek == DayOfWeek.Saturday || local.DayOfWeek == DayOfWeek.Sunday;
            return (weekend ? Weekend : Weekday)[local.Hour];
        }

        /// <summary>Score of publishing at a UTC instant for the weighted markets.</summary>
        public static double Score(IList<Market> markets, DateTime utc)
        {
            double total = 0, wsum = 0;
            foreach (var m in markets)
            {
                if (m.Weight <= 0) continue;
                wsum += m.Weight;
                foreach (var z in m.Zones)
                    for (int k = 0; k < AfterPublish.Length; k++)
                        total += m.Weight * z.Value * AfterPublish[k] * Intensity(z.Key, utc.AddHours(k + 0.5));
            }
            return wsum > 0 ? total / wsum : 0;
        }

        /// <summary>
        /// Best weekly pattern: N days per week, spaced as evenly as possible, each at its best half-hour.
        /// Evaluated on the given reference week (DST matters: the result can shift by an hour between seasons).
        /// </summary>
        public static List<WeeklySlot> WeeklyPattern(IList<Market> markets, int perWeek, DateTime referenceUtc)
        {
            perWeek = Math.Max(1, Math.Min(7, perWeek));
            var monday = referenceUtc.Date.AddDays(-(((int)referenceUtc.DayOfWeek + 6) % 7));
            var best = new WeeklySlot[7];
            for (int d = 0; d < 7; d++)
            {
                var day = monday.AddDays(d);
                WeeklySlot top = null;
                for (int half = 0; half < 48; half++)
                {
                    var t = DateTime.SpecifyKind(day.AddMinutes(half * 30), DateTimeKind.Utc);
                    double s = Score(markets, t);
                    if (top == null || s > top.Score + 1e-9) top = new WeeklySlot { DayUtc = t.DayOfWeek, HourUtc = t.Hour, MinuteUtc = t.Minute, Score = s };
                }
                best[d] = top;
            }
            // choose the subset of days with the best total score whose circular gaps are as even as possible
            int minGap = 7 / perWeek;
            List<int> bestSet = null;
            double bestTotal = -1;
            foreach (var set in Combinations(7, perWeek))
            {
                if (!GapsOk(set, minGap)) continue;
                double tot = set.Sum(i => best[i].Score);
                if (tot > bestTotal + 1e-9) { bestTotal = tot; bestSet = set; }
            }
            if (bestSet == null) bestSet = Enumerable.Range(0, perWeek).ToList();
            return bestSet.Select(i => best[i]).ToList();
        }

        static bool GapsOk(IList<int> days, int minGap)
        {
            if (days.Count < 2) return true;
            for (int i = 0; i < days.Count; i++)
            {
                int a = days[i], b = days[(i + 1) % days.Count];
                int gap = (b - a + 7) % 7;
                if (gap == 0) gap = 7;
                if (gap < minGap) return false;
            }
            return true;
        }

        static IEnumerable<List<int>> Combinations(int n, int k, int start = 0)
        {
            if (k == 0) { yield return new List<int>(); yield break; }
            for (int i = start; i <= n - k; i++)
                foreach (var rest in Combinations(n, k - 1, i + 1))
                {
                    rest.Insert(0, i);
                    yield return rest;
                }
        }

        /// <summary>Concrete upcoming publish times (UTC) from the weekly pattern, after notBefore, skipping taken ones.</summary>
        public static List<DateTime> UpcomingSlots(IList<Market> markets, int perWeek, DateTime notBeforeUtc, int count, ICollection<DateTime> taken = null)
        {
            var result = new List<DateTime>();
            var day = notBeforeUtc.Date;
            List<WeeklySlot> pattern = null;
            DateTime patternWeek = DateTime.MinValue;
            for (int i = 0; i < 400 && result.Count < count; i++, day = day.AddDays(1))
            {
                var weekStart = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
                if (pattern == null || weekStart != patternWeek) { pattern = WeeklyPattern(markets, perWeek, weekStart); patternWeek = weekStart; }
                foreach (var s in pattern.Where(p => p.DayUtc == day.DayOfWeek))
                {
                    var t = DateTime.SpecifyKind(day.AddHours(s.HourUtc).AddMinutes(s.MinuteUtc), DateTimeKind.Utc);
                    if (t < notBeforeUtc) continue;
                    if (taken != null && taken.Any(x => Math.Abs((x - t).TotalHours) < 12)) continue;
                    result.Add(t);
                }
            }
            return result.Take(count).ToList();
        }

        /// <summary>“Tue 19:00 UTC = 14:00 EST · 20:00 CET · 02:00 (your PC)”.</summary>
        public static string Describe(DateTime utc, TimeSpan machineOffset)
        {
            var ci = CultureInfo.InvariantCulture;
            string L(MarketZone z) => MarketClock.ToLocal(z, utc).ToString("HH:mm", ci) + " " + MarketClock.ZoneLabel(z, utc);
            return $"{utc.ToString("ddd dd/MM HH:mm", ci)} UTC = {L(MarketZone.UsEastern)} · {L(MarketZone.UsPacific)} · {L(MarketZone.UkIreland)} · {L(MarketZone.CentralEurope)} · {utc.Add(machineOffset).ToString("ddd HH:mm", ci)} máy bạn";
        }
    }
}
