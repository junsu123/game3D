using System;
using System.Collections.Generic;
using System.Linq;

namespace Mylti.Domain
{
    /// <summary>
    /// Builds the 10-minute (or draft) run plan: cold open → intro → 5 escalating rounds → final → summary.
    /// Each round has a narrative role and changes at least one meaningful variable (spec §17.4, §21.1).
    /// Timings are an editorial chapter budget, not a hard per-attempt duration (§17.2).
    /// </summary>
    public static class RunPlanner
    {
        // Reference budget from spec §17.2 (sums to 600 s).
        public static readonly double[] ReferenceBudget = { 20, 40, 75, 75, 90, 90, 105, 70, 35 };
        public const double SoloAttemptSeconds = 24;   // estimate incl. countdown + flight + settle
        public const double CardSeconds = 4;

        static string L(ContentLanguage lang, string en, string vi) => lang == ContentLanguage.vi ? vi : en;

        public static RunPlan CreatePlan(ConceptBrief concept, IList<Entrant> entrants, int seed,
            double targetSeconds, ContentLanguage lang, string difficulty, out List<string> warnings)
        {
            warnings = new List<string>();
            if (entrants == null || entrants.Count == 0) throw new ArgumentException("No vehicles");
            if (concept == null) throw new ArgumentException("A concept must be selected before planning (Concept-first, §21.1)");
            if (targetSeconds < 240) { warnings.Add("Target below 4:00 is not supported → 4:00 draft."); targetSeconds = 240; }

            var plan = new RunPlan
            {
                PlanId = ScenarioConfig.SanitizeId($"plan_{concept.ConceptId}_{seed}"),
                Seed = seed,
                TargetDurationSeconds = targetSeconds,
                Language = lang,
                Difficulty = difficulty,
                Concept = concept,
                Entrants = entrants.Select(e => e.Clone()).ToList()
            };
            for (int i = 0; i < plan.Entrants.Count; i++) plan.Entrants[i].Lane = i;

            var rounds = BuildRounds(concept.SeriesId, concept.TwistKind, plan.Entrants, difficulty, targetSeconds, lang);
            double scale = targetSeconds / 600.0;
            double[] budget = ReferenceBudget.Select(b => b * scale).ToArray();
            int n = plan.Entrants.Count;

            double t = 0;
            var teaserSpec = rounds[4].Clone();
            teaserSpec.Index = 0; teaserSpec.Scored = false; teaserSpec.HeadToHead = true;
            teaserSpec.Title = L(lang, "Teaser", "Teaser");
            teaserSpec.BudgetSeconds = (float)budget[0];
            plan.Phases.Add(new PhaseSpec { Kind = PhaseKind.ColdOpen, Label = "Cold Open", PlannedStart = t, PlannedDuration = budget[0], Round = teaserSpec });
            t += budget[0];
            plan.Phases.Add(new PhaseSpec { Kind = PhaseKind.Intro, Label = L(lang, "Vehicles & Rules", "Giới thiệu xe & luật"), PlannedStart = t, PlannedDuration = budget[1], Round = rounds[0].Clone() });
            t += budget[1];

            for (int i = 0; i < rounds.Count; i++)
            {
                var r = rounds[i];
                r.BudgetSeconds = (float)budget[2 + i];
                // Solo attempts read better on video; switch to head-to-head when the budget is too tight.
                double soloNeed = n * SoloAttemptSeconds + 2 * CardSeconds;
                if (!r.HeadToHead && n > 1 && soloNeed > r.BudgetSeconds)
                {
                    r.HeadToHead = true;
                    warnings.Add($"Round {r.Index}: budget {r.BudgetSeconds:0}s too short for {n} solo runs → head-to-head.");
                }
                bool isFinal = i == rounds.Count - 1;
                plan.Phases.Add(new PhaseSpec
                {
                    Kind = isFinal ? PhaseKind.Final : PhaseKind.Round,
                    Label = r.Title,
                    PlannedStart = t,
                    PlannedDuration = r.BudgetSeconds,
                    Round = r
                });
                t += r.BudgetSeconds;
            }
            plan.Phases.Add(new PhaseSpec { Kind = PhaseKind.Summary, Label = L(lang, "Results", "Kết quả"), PlannedStart = t, PlannedDuration = targetSeconds - t, Round = null });

            if (Math.Abs(plan.Phases.Sum(p => p.PlannedDuration) - targetSeconds) > 0.01)
                warnings.Add("Internal: phase budgets do not sum to target.");
            return plan;
        }

        /// <summary>Five escalating rounds + a decisive final, specialised by series and twist.</summary>
        public static List<RoundSpec> BuildRounds(string seriesId, string twist, IList<Entrant> entrants, string difficulty,
            double targetSeconds, ContentLanguage lang)
        {
            float hMul = difficulty == "Easy" ? 0.75f : difficulty == "Hard" ? 1.2f : 1f;
            var roles = new[] { RoundRole.Baseline, RoundRole.Complication, RoundRole.Reversal, RoundRole.Escalation, RoundRole.Extreme, RoundRole.Decisive };
            float[] heights = { 6, 10, 12, 16, 22, 18 };
            float[] angles = { 18, 22, 24, 27, 30, 28 };
            float[] speed = { 0.85f, 0.95f, 1.0f, 1.05f, 1.12f, 1.1f };
            var list = new List<RoundSpec>();
            for (int i = 0; i < 6; i++)
            {
                list.Add(new RoundSpec
                {
                    Index = i + 1,
                    Role = roles[i],
                    KickerHeight = (float)Math.Round(Math.Min(30, heights[i] * hMul), 1),
                    KickerAngleDeg = angles[i],
                    SpeedFactor = speed[i],
                    Rule = seriesId == "MegaRampSurvival" ? ScoringRule.CleanLandingOnly : ScoringRule.FurthestValidLanding,
                    HeadToHead = i == 5,
                    AttemptPasses = 1,
                    MaxExtraPasses = 2 // only used when time allows; always given to every vehicle, best attempt counts
                });
            }

            // Series-wide modifiers
            if (seriesId == "ImpossibleBridge")
            {
                list[0].BridgeWidth = 0;    // baseline without bridge
                list[1].BridgeWidth = 7f;
                list[2].BridgeWidth = 6f;
                list[3].BridgeWidth = 5f;
                list[4].BridgeWidth = 4.2f;
                list[5].BridgeWidth = 5f;
            }
            if (seriesId == "SpeedExperiment")
            {
                list[0].SpeedFactor = 0.7f; list[1].SpeedFactor = 0.85f;
            }
            if (seriesId == "ObstacleSurvival")
            {
                list[1].Obstacles = ObstacleSet.BarrelLine;
            }

            // Twist modifiers (rounds 3–5 carry the twist; the final stays decisive)
            string tw = twist ?? "obstacles";
            if (tw.StartsWith("bridge_")) tw = tw.Substring(7);
            switch (tw)
            {
                case "obstacles":
                    list[2].Obstacles = ObstacleSet.BarrelLine;
                    list[3].Obstacles = ObstacleSet.CrateWall;
                    list[4].Obstacles = ObstacleSet.BarrelsAndWall;
                    break;
                case "wall":
                    list[2].Rule = ScoringRule.BeyondTheWall; list[2].Obstacles = ObstacleSet.CrateWall;
                    list[3].Rule = ScoringRule.BeyondTheWall; list[3].Obstacles = ObstacleSet.CrateWall;
                    list[4].Obstacles = ObstacleSet.BarrelsAndWall;
                    break;
                case "target":
                    list[2].Rule = ScoringRule.TargetZone;
                    list[3].Rule = ScoringRule.TargetZone;
                    list[4].Obstacles = ObstacleSet.BarrelLine;
                    break;
                case "landing":
                    list[2].LandingRamp = true;
                    list[3].LandingRamp = false; list[3].Obstacles = ObstacleSet.BarrelLine;
                    list[4].Obstacles = ObstacleSet.CrateWall;
                    break;
                case "speed":
                    list[2].SpeedFactor = 1.2f;
                    list[3].SpeedFactor = 0.75f;
                    list[4].Obstacles = ObstacleSet.BarrelLine;
                    break;
                case "bridge":
                    list[4].Obstacles = ObstacleSet.BarrelLine;
                    break;
            }

            RefreshRoundTexts(list, entrants, lang);
            return list;
        }

        /// <summary>Recompute wall/target placement, titles, change summaries and sub-questions (after edits too).</summary>
        public static void RefreshRoundTexts(IList<RoundSpec> list, IList<Entrant> entrants, ContentLanguage lang)
        {
            // Place walls/targets relative to the predicted jump so they are challenging but possible.
            foreach (var r in list)
            {
                double median = Median(entrants.Select(e => PredictDistance(e, r)));
                r.WallDistance = (float)Math.Round(Math.Max(25, median * (r.Rule == ScoringRule.BeyondTheWall ? 0.8 : 0.9)));
                r.TargetDistance = (float)Math.Round(Math.Max(20, median * 0.85));
            }

            // Titles, change summaries and sub-questions
            RoundSpec prev = null;
            foreach (var r in list)
            {
                bool fin = r.Role == RoundRole.Decisive;
                string roleName = RoleName(r.Role, lang);
                r.Title = fin ? L(lang, $"FINAL — {r.KickerHeight:0} m kicker", $"CHUNG KẾT — dốc {r.KickerHeight:0} m")
                              : L(lang, $"Round {r.Index} — {roleName}: {r.KickerHeight:0} m kicker", $"Vòng {r.Index} — {roleName}: dốc {r.KickerHeight:0} m");
                r.ChangeSummary = DescribeChange(prev, r, lang);
                r.SubQuestion = SubQuestion(r, entrants, lang);
                prev = r;
            }
        }

        public static string RoleName(RoundRole role, ContentLanguage lang)
        {
            switch (role)
            {
                case RoundRole.Baseline: return L(lang, "Baseline", "Khởi động");
                case RoundRole.Complication: return L(lang, "Higher & Faster", "Cao hơn, nhanh hơn");
                case RoundRole.Reversal: return L(lang, "The Twist", "Bước ngoặt");
                case RoundRole.Escalation: return L(lang, "Escalation", "Leo thang");
                case RoundRole.Extreme: return L(lang, "Extreme", "Cực hạn");
                default: return L(lang, "Final", "Chung kết");
            }
        }

        public static string RuleText(ScoringRule rule, RoundSpec r, ContentLanguage lang)
        {
            switch (rule)
            {
                case ScoringRule.CleanLandingOnly: return L(lang, "Only clean, upright landings count", "Chỉ tính cú tiếp đất sạch, xe còn đứng");
                case ScoringRule.TargetZone: return L(lang, $"Land closest to the {r.TargetDistance:0} m target", $"Tiếp đất gần mục tiêu {r.TargetDistance:0} m nhất");
                case ScoringRule.BeyondTheWall: return L(lang, $"Land beyond the crate wall at {r.WallDistance:0} m", $"Tiếp đất vượt tường thùng ở {r.WallDistance:0} m");
                default: return L(lang, "Longest upright landing wins", "Cú tiếp đất xa nhất (xe còn đứng) thắng");
            }
        }

        static string DescribeChange(RoundSpec prev, RoundSpec r, ContentLanguage lang)
        {
            if (prev == null) return L(lang, "Baseline: everyone sets a first distance", "Mốc chuẩn: mỗi xe lập khoảng cách đầu tiên");
            var parts = new List<string>();
            if (Math.Abs(r.KickerHeight - prev.KickerHeight) > 0.1f) parts.Add(L(lang, $"kicker {prev.KickerHeight:0}→{r.KickerHeight:0} m", $"dốc {prev.KickerHeight:0}→{r.KickerHeight:0} m"));
            if (Math.Abs(r.SpeedFactor - prev.SpeedFactor) > 0.01f) parts.Add(L(lang, $"speed ×{r.SpeedFactor:0.00}", $"tốc độ ×{r.SpeedFactor:0.00}"));
            if (r.Obstacles != prev.Obstacles) parts.Add(L(lang, $"obstacles: {r.Obstacles}", $"chướng ngại: {r.Obstacles}"));
            if (r.Rule != prev.Rule) parts.Add(L(lang, $"rule: {RuleText(r.Rule, r, lang)}", $"luật: {RuleText(r.Rule, r, lang)}"));
            if (Math.Abs(r.BridgeWidth - prev.BridgeWidth) > 0.05f) parts.Add(r.BridgeWidth > 0 ? L(lang, $"bridge {r.BridgeWidth:0.0} m wide", $"cầu rộng {r.BridgeWidth:0.0} m") : L(lang, "no bridge", "bỏ cầu"));
            if (r.LandingRamp != prev.LandingRamp) parts.Add(r.LandingRamp ? L(lang, "landing ramp added", "thêm dốc tiếp đất") : L(lang, "landing ramp removed", "bỏ dốc tiếp đất"));
            if (r.HeadToHead != prev.HeadToHead) parts.Add(r.HeadToHead ? L(lang, "all vehicles at once", "tất cả xe chạy cùng lúc") : L(lang, "solo runs", "chạy từng xe"));
            return parts.Count == 0 ? L(lang, "same setup (warning: no meaningful change)", "giữ nguyên (cảnh báo: không có thay đổi)") : string.Join("; ", parts);
        }

        static string SubQuestion(RoundSpec r, IList<Entrant> entrants, ContentLanguage lang)
        {
            var heavy = entrants.Select(e => e.Def).OrderByDescending(d => d.MassKg).First();
            switch (r.Role)
            {
                case RoundRole.Baseline: return L(lang, "Who sets the first benchmark?", "Ai lập mốc đầu tiên?");
                case RoundRole.Complication: return L(lang, $"Does extra height help or hurt the {heavy.ClassNounEn}?", $"Dốc cao hơn giúp hay hại {heavy.ClassNounVi}?");
                case RoundRole.Reversal:
                    if (r.Rule == ScoringRule.TargetZone) return L(lang, "Can anyone hit the target instead of just going far?", "Có xe nào trúng mục tiêu thay vì chỉ bay xa?");
                    if (r.Rule == ScoringRule.BeyondTheWall) return L(lang, "Who clears the crate wall?", "Xe nào vượt được tường thùng?");
                    if (r.BridgeWidth > 0) return L(lang, "Can everyone stay on the bridge?", "Tất cả có trụ được trên cầu?");
                    return L(lang, "Does the new obstacle change the order?", "Chướng ngại mới có đảo ngược thứ hạng?");
                case RoundRole.Escalation: return L(lang, "Who adapts when it gets harder?", "Xe nào thích nghi khi khó hơn?");
                case RoundRole.Extreme: return L(lang, $"Can anyone stay upright after a {r.KickerHeight:0} m launch?", $"Có xe nào còn đứng vững sau cú phóng {r.KickerHeight:0} m?");
                default: return L(lang, "All vehicles together — the best final landing takes it", "Tất cả cùng chạy — cú tiếp đất tốt nhất ở chung kết sẽ quyết định");
            }
        }

        /// <summary>Rough ballistic estimate (planning only — never used as a reported fact).</summary>
        public static double PredictDistance(Entrant e, RoundSpec r)
        {
            var d = e.Def;
            if (d == null) return 40;
            double v = Math.Min(d.TopSpeedKmh, (e.TargetSpeedKmh > 0 ? e.TargetSpeedKmh : d.DefaultTargetSpeedKmh) * r.SpeedFactor) / 3.6;
            double h = r.KickerHeight;
            double v0sq = v * v - 2 * 9.81 * h * 0.45; // AI keeps throttle on the ramp
            if (v0sq < 4) return 5;
            double v0 = Math.Sqrt(v0sq);
            double th = r.KickerAngleDeg * Math.PI / 180.0;
            double vx = v0 * Math.Cos(th), vy = v0 * Math.Sin(th);
            double t = (vy + Math.Sqrt(vy * vy + 2 * 9.81 * h)) / 9.81;
            return vx * t;
        }

        static double Median(IEnumerable<double> values)
        {
            var arr = values.OrderBy(x => x).ToArray();
            if (arr.Length == 0) return 50;
            return arr.Length % 2 == 1 ? arr[arr.Length / 2] : (arr[arr.Length / 2 - 1] + arr[arr.Length / 2]) / 2;
        }
    }
}
