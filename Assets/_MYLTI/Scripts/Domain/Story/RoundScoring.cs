using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mylti.Domain
{
    /// <summary>Evaluates rounds strictly from recorded attempts. No result is ever edited after the fact.</summary>
    public static class RoundScoring
    {
        static string L(ContentLanguage lang, string en, string vi) => lang == ContentLanguage.vi ? vi : en;
        static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>Does this attempt qualify under the rule, and what is its metric? (higher is better, except TargetZone)</summary>
        public static bool Qualifies(AttemptRecord a, ScoringRule rule, out double metric)
        {
            metric = 0;
            if (!a.Scored || a.IsTeaser || !a.LandingDistanceM.HasValue) return false;
            switch (rule)
            {
                case ScoringRule.CleanLandingOnly:
                    if (a.Outcome != AttemptOutcome.CleanLanding) return false;
                    metric = a.LandingDistanceM.Value; return true;
                case ScoringRule.TargetZone:
                    if (!a.ValidLanding || !a.TargetError.HasValue) return false;
                    metric = a.TargetError.Value; return true;
                case ScoringRule.BeyondTheWall:
                    if (!a.ValidLanding || a.ClearedWall < 0.5) return false;
                    metric = a.LandingDistanceM.Value; return true;
                default:
                    if (!a.ValidLanding) return false;
                    metric = a.LandingDistanceM.Value; return true;
            }
        }

        public static RoundResult Evaluate(RoundSpec spec, IList<AttemptRecord> attempts, IList<Entrant> entrants,
            ContentLanguage lang, bool isFinal)
        {
            var res = new RoundResult
            {
                RoundIndex = spec.Index,
                Title = spec.Title,
                Rule = spec.Rule,
                IsFinal = isFinal,
                Completed = attempts.Count > 0
            };
            var roundAttempts = attempts.Where(a => a.RoundIndex == spec.Index && !a.IsTeaser).ToList();
            res.AttemptIds = roundAttempts.Select(a => a.AttemptId).ToList();
            bool lowerBetter = spec.Rule == ScoringRule.TargetZone;

            foreach (var e in entrants)
            {
                double? best = null;
                foreach (var a in roundAttempts.Where(x => x.VehicleId == e.VehicleId))
                {
                    if (!Qualifies(a, spec.Rule, out double m)) continue;
                    if (!best.HasValue || (lowerBetter ? m < best.Value : m > best.Value)) best = m;
                }
                if (best.HasValue) res.BestMetric[e.VehicleId] = Math.Round(best.Value, 1);
            }

            if (res.BestMetric.Count == 0)
            {
                res.WinnerVehicleId = null;
                res.Explanation = L(lang, "No vehicle met the rule this round — no winner.", "Không xe nào đạt luật ở vòng này — không có người thắng.");
                return res;
            }
            var ordered = lowerBetter ? res.BestMetric.OrderBy(kv => kv.Value).ToList() : res.BestMetric.OrderByDescending(kv => kv.Value).ToList();
            if (ordered.Count > 1 && Math.Abs(ordered[0].Value - ordered[1].Value) < 0.1)
            {
                res.WinnerVehicleId = null;
                res.Explanation = L(lang, $"Tie at {F1(ordered[0].Value)} m — no single winner.", $"Hòa ở {F1(ordered[0].Value)} m — không có xe thắng duy nhất.");
                return res;
            }
            res.WinnerVehicleId = ordered[0].Key;
            var wDef = VehicleCatalog.Find(res.WinnerVehicleId);
            string name = wDef?.DisplayName(lang) ?? res.WinnerVehicleId;
            if (lowerBetter)
                res.Explanation = L(lang, $"{name} landed {F1(ordered[0].Value)} m from the target.", $"{name} tiếp đất cách mục tiêu {F1(ordered[0].Value)} m.");
            else
                res.Explanation = L(lang, $"{name} wins with a {F1(ordered[0].Value)} m landing.", $"{name} thắng với cú tiếp đất {F1(ordered[0].Value)} m.");
            if (ordered.Count > 1)
            {
                var second = VehicleCatalog.Find(ordered[1].Key)?.DisplayName(lang) ?? ordered[1].Key;
                res.Explanation += lowerBetter
                    ? L(lang, $" {second}: {F1(ordered[1].Value)} m off.", $" {second}: lệch {F1(ordered[1].Value)} m.")
                    : L(lang, $" {second}: {F1(ordered[1].Value)} m.", $" {second}: {F1(ordered[1].Value)} m.");
            }
            return res;
        }

        public sealed class Standing
        {
            public string VehicleId;
            public int RoundWins;
            public bool WonFinal;
            public int Points;
            public int ValidLandings;
            public int Rollovers;
        }

        /// <summary>1 point per round win, 2 for the final. Tie → final winner; still tied → null.</summary>
        public static List<Standing> ComputeStandings(IList<RoundResult> rounds, IList<AttemptRecord> attempts, IList<Entrant> entrants, out string overallWinner)
        {
            var list = entrants.Select(e => new Standing { VehicleId = e.VehicleId }).ToList();
            foreach (var r in rounds)
            {
                if (r.WinnerVehicleId == null) continue;
                var s = list.FirstOrDefault(x => x.VehicleId == r.WinnerVehicleId);
                if (s == null) continue;
                s.RoundWins++;
                s.Points += r.IsFinal ? 2 : 1;
                if (r.IsFinal) s.WonFinal = true;
            }
            foreach (var s in list)
            {
                var mine = attempts.Where(a => a.VehicleId == s.VehicleId && a.Scored && !a.IsTeaser).ToList();
                s.ValidLandings = mine.Count(a => a.ValidLanding);
                s.Rollovers = mine.Count(a => a.Outcome == AttemptOutcome.Rollover);
            }
            list = list.OrderByDescending(s => s.Points).ThenByDescending(s => s.WonFinal).ThenByDescending(s => s.ValidLandings).ToList();
            overallWinner = null;
            if (list.Count == 1) overallWinner = list[0].Points > 0 ? list[0].VehicleId : null;
            else if (list.Count > 1 && list[0].Points > 0)
            {
                if (list[0].Points > list[1].Points) overallWinner = list[0].VehicleId;
                else if (list[0].WonFinal && !list[1].WonFinal) overallWinner = list[0].VehicleId;
            }
            return list;
        }

        /// <summary>Leader after each completed round (for comeback / lead-change facts).</summary>
        public static List<string> LeaderTimeline(IList<RoundResult> rounds, IList<Entrant> entrants)
        {
            var points = entrants.ToDictionary(e => e.VehicleId, e => 0);
            var timeline = new List<string>();
            foreach (var r in rounds.OrderBy(x => x.RoundIndex))
            {
                if (r.WinnerVehicleId != null && points.ContainsKey(r.WinnerVehicleId)) points[r.WinnerVehicleId] += r.IsFinal ? 2 : 1;
                var top = points.OrderByDescending(kv => kv.Value).ToList();
                timeline.Add(top.Count > 1 && top[0].Value == top[1].Value ? null : top[0].Value > 0 ? top[0].Key : null);
            }
            return timeline;
        }
    }

    /// <summary>InterestingnessScore over REAL attempts only (spec §17.4). Never invents events.</summary>
    public static class HighlightScorer
    {
        public static double Score(AttemptRecord a, bool isFinal)
        {
            double s = 0;
            s += a.AirTimeS * 1.2;
            s += a.PeakHeightM * 0.12;
            if (a.LandingDistanceM.HasValue) s += a.LandingDistanceM.Value / 25.0;
            if (a.Outcome == AttemptOutcome.Rollover) s += 3;
            if (a.Outcome == AttemptOutcome.FellOff) s += 2.5;
            if (a.BodySlam) s += 1.2;
            s += Math.Min(4, a.ObstacleHits) * 0.7;
            if (a.ClearedWall > 0.5) s += 1;
            if (isFinal) s += 1.5;
            if (a.IsTeaser) s -= 1; // teaser footage is already used as the hook
            return Math.Round(s, 2);
        }

        public static string Reason(AttemptRecord a, ContentLanguage lang)
        {
            var parts = new List<string>();
            bool vi = lang == ContentLanguage.vi;
            if (a.AirTimeS > 0.5) parts.Add(vi ? $"bay {a.AirTimeS:0.0}s" : $"{a.AirTimeS:0.0}s airtime");
            if (a.LandingDistanceM.HasValue) parts.Add(vi ? $"tiếp đất {a.LandingDistanceM.Value:0.0} m" : $"{a.LandingDistanceM.Value:0.0} m landing");
            if (a.Outcome == AttemptOutcome.Rollover) parts.Add(vi ? "lật xe" : "rollover");
            if (a.Outcome == AttemptOutcome.FellOff) parts.Add(vi ? "rơi khỏi đường" : "fell off");
            if (a.ObstacleHits > 0) parts.Add(vi ? $"va {a.ObstacleHits} chướng ngại" : $"hit {a.ObstacleHits} obstacle(s)");
            if (a.ClearedWall > 0.5) parts.Add(vi ? "vượt tường" : "cleared the wall");
            return parts.Count == 0 ? (vi ? "lượt chạy" : "run") : string.Join(", ", parts);
        }

        public static List<Highlight> Extract(RunRecord run, int maxCount = 12)
        {
            var finals = new HashSet<int>(run.Rounds.Where(r => r.IsFinal).Select(r => r.RoundIndex));
            var lang = run.Plan?.Language ?? ContentLanguage.en;
            var list = new List<Highlight>();
            int k = 0;
            foreach (var a in run.Attempts)
            {
                if (a.Outcome == AttemptOutcome.None || a.Outcome == AttemptOutcome.Aborted) continue;
                double score = Score(a, finals.Contains(a.RoundIndex));
                if (score <= 0.5) continue;
                double start = a.TakeoffProgramTime.HasValue ? Math.Max(a.StartProgramTime, a.TakeoffProgramTime.Value - 3) : a.StartProgramTime;
                double end = a.LandingProgramTime.HasValue ? Math.Min(a.EndProgramTime, a.LandingProgramTime.Value + 4) : a.EndProgramTime;
                // Anchor to the most telling real event of the attempt.
                int evId = a.EventIds.Count > 0 ? a.EventIds[0] : -1;
                foreach (int id in a.EventIds)
                {
                    var e = run.FindEvent(id);
                    if (e != null && (e.Type == "Rollover" || e.Type == "FellOff" || e.Type == "Landed" || e.Type == "Apex")) { evId = id; if (e.Type != "Apex") break; }
                }
                var seg = run.Segments.FirstOrDefault(s => s.Kind == SegmentKind.Live && s.Start <= start + 0.01 && s.End >= start);
                list.Add(new Highlight
                {
                    HighlightId = "hl_" + (++k).ToString("00"),
                    EventId = evId,
                    AttemptId = a.AttemptId,
                    VehicleId = a.VehicleId,
                    RoundIndex = a.RoundIndex,
                    Start = start,
                    End = Math.Max(start + 1, end),
                    Score = score,
                    Camera = seg?.Camera ?? "auto",
                    Reason = Reason(a, lang),
                    SourceClip = "take_" + a.AttemptId
                });
            }
            return list.OrderByDescending(h => h.Score).Take(maxCount).ToList();
        }
    }
}
