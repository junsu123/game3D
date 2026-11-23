using System;
using System.Collections.Generic;
using System.Linq;

namespace Mylti.Domain
{
    /// <summary>
    /// Concept Lab (spec §21): produces 3 genuinely different concept briefs for a series,
    /// each with a viewer question, stakes, twist and a working title/thumbnail idea.
    /// Concept Score is a rule-based internal heuristic — NOT a view/CTR forecast.
    /// </summary>
    public static class ConceptGenerator
    {
        public static readonly string[] SeriesIds =
        {
            "VehicleVersus", "MegaRampSurvival", "SpeedExperiment", "ImpossibleBridge", "ObstacleSurvival", "CrashLab"
        };

        /// <summary>Series shown in the UI but not implemented in this MVP (honest labelling).</summary>
        public static readonly string[] PlannedSeriesIds = { "UnderdogComeback" };

        public static string SeriesLabel(string id)
        {
            switch (id)
            {
                case "VehicleVersus": return "Vehicle Versus";
                case "MegaRampSurvival": return "Mega Ramp Survival";
                case "SpeedExperiment": return "Speed Experiment";
                case "ImpossibleBridge": return "Impossible Bridge";
                case "ObstacleSurvival": return "Obstacle Survival";
                case "CrashLab": return "Crash Lab";
                case "UnderdogComeback": return "Underdog Comeback (P1)";
                default: return id;
            }
        }

        public static string[] TwistsFor(string seriesId)
        {
            switch (seriesId)
            {
                case "MegaRampSurvival": return new[] { "landing", "wall", "obstacles", "crash" };
                case "CrashLab": return new[] { "crash", "crash_drop", "crash_wall" };
                case "SpeedExperiment": return new[] { "speed", "target", "wall" };
                case "ImpossibleBridge": return new[] { "bridge", "bridge_target", "bridge_wall" };
                case "ObstacleSurvival": return new[] { "obstacles", "wall", "landing" };
                default: return new[] { "obstacles", "target", "wall", "crash" };
            }
        }

        static string L(ContentLanguage lang, string en, string vi) => lang == ContentLanguage.vi ? vi : en;

        public static string JoinNames(IList<string> names, ContentLanguage lang, string conj = null)
        {
            conj = conj ?? L(lang, "or", "hay");
            if (names.Count == 0) return "";
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return names[0] + " " + conj + " " + names[1];
            return string.Join(", ", names.Take(names.Count - 1)) + " " + conj + " " + names[names.Count - 1];
        }

        public static string VersusLine(IList<Entrant> entrants, ContentLanguage lang)
            => string.Join(" vs ", entrants.Select(e => e.Def.DisplayName(lang)));

        public static List<ConceptBrief> Generate(string seriesId, IList<Entrant> entrants, int seed,
            ContentLanguage lang, IList<NoveltyFingerprint> history, string difficulty = "Progressive")
        {
            if (!SeriesIds.Contains(seriesId)) seriesId = "VehicleVersus";
            if (entrants == null || entrants.Count == 0) throw new ArgumentException("At least one vehicle is required");
            if (seriesId == "VehicleVersus" && entrants.Count < 2) seriesId = "MegaRampSurvival";

            var defs = entrants.Select(e => e.Def).ToList();
            var names = defs.Select(d => d.DisplayName(lang)).ToList();
            var heaviest = defs.OrderByDescending(d => d.MassKg).First();
            var lightest = defs.OrderBy(d => d.MassKg).First();
            var widest = defs.OrderByDescending(d => d.Width).First();
            string versus = VersusLine(entrants, lang);
            var rng = new Random(seed);
            var result = new List<ConceptBrief>();
            string[] twists = TwistsFor(seriesId);

            for (int i = 0; i < 3; i++)
            {
                string twist = twists[i % twists.Length];
                var c = new ConceptBrief
                {
                    ConceptId = ScenarioConfig.SanitizeId($"{seriesId}_{twist}_{seed}_{i + 1}"),
                    SeriesId = seriesId,
                    TwistKind = twist
                };
                // A dry-run plan gives the concept its real final kicker height.
                var probe = RunPlanner.BuildRounds(seriesId, twist, entrants, difficulty, 600, lang);
                float maxH = probe.Where(r => r.IsJump).Select(r => r.KickerHeight).DefaultIfEmpty(10).Max();
                float maxCrash = probe.Where(r => r.Rule == ScoringRule.CrashWall).Select(r => r.CrashSpeedKmh).DefaultIfEmpty(0).Max();
                float maxDrop = probe.Where(r => r.Rule == ScoringRule.DropTest).Select(r => r.DropHeightM).DefaultIfEmpty(0).Max();
                string hTxt = maxH.ToString("0");

                switch (seriesId)
                {
                    case "CrashLab":
                        c.ViewerQuestion = L(lang, $"Which one survives the most: {JoinNames(names, lang)}?",
                                                   $"Xe nào chịu đựng tốt nhất: {JoinNames(names, lang)}?");
                        c.ViewerPromise = L(lang, $"Wall crashes up to {maxCrash:0} km/h, drops from up to {maxDrop:0} m and a {hTxt} m mega jump — the least damage wins each round.",
                                                  $"Đâm tường tới {maxCrash:0} km/h, thả rơi tới {maxDrop:0} m và cú nhảy {hTxt} m — xe ít hư hại nhất thắng từng vòng.");
                        c.Stakes = L(lang, "Damage is measured after every test.", "Hư hại được đo sau mỗi thử nghiệm.");
                        c.CentralConflict = L(lang, "mass vs impact", "khối lượng đối đầu lực va chạm");
                        c.WorkingTitle = L(lang, $"{versus}: Crash Test, Drop Test, Mega Ramp", $"{versus}: Đâm Tường, Thả Rơi, Mega Ramp");
                        break;
                    case "MegaRampSurvival":
                        c.ViewerQuestion = L(lang, $"Can the {heaviest.DisplayNameEn} survive a {hTxt} m Mega Ramp?",
                                                   $"{heaviest.DisplayNameVi} có sống sót qua Mega Ramp {hTxt} m?");
                        c.ViewerPromise = L(lang, $"Watch {JoinNames(names, lang, "and")} try to land upright on ramps that keep getting taller.",
                                                  $"Xem {JoinNames(names, lang, "và")} cố tiếp đất an toàn trên các dốc ngày càng cao.");
                        c.Stakes = L(lang, "Only landings that end upright score.", "Chỉ cú tiếp đất còn đứng vững mới được tính điểm.");
                        c.CentralConflict = L(lang, "height vs stability", "độ cao đối đầu độ ổn định");
                        c.WorkingTitle = L(lang, $"Can a {heaviest.ClassNounEn} Survive a {hTxt}m Mega Ramp?", $"{heaviest.DisplayNameVi} Có Sống Sót Qua Mega Ramp {hTxt}m?");
                        break;
                    case "SpeedExperiment":
                        c.ViewerQuestion = L(lang, "How much does speed really change a Mega Ramp jump?",
                                                   "Tốc độ thay đổi cú nhảy Mega Ramp nhiều đến mức nào?");
                        c.ViewerPromise = L(lang, $"Same ramp, different speeds — {JoinNames(names, lang, "and")} test what happens when you go faster.",
                                                  $"Cùng một dốc, tốc độ khác nhau — {JoinNames(names, lang, "và")} thử điều gì xảy ra khi chạy nhanh hơn.");
                        c.Stakes = L(lang, "Too slow falls short, too fast can overshoot.", "Quá chậm thì hụt, quá nhanh thì vượt quá mục tiêu.");
                        c.CentralConflict = L(lang, "speed vs control", "tốc độ đối đầu kiểm soát");
                        c.WorkingTitle = L(lang, $"Mega Ramp Speed Test: {versus}", $"Thử Nghiệm Tốc Độ Mega Ramp: {versus}");
                        break;
                    case "ImpossibleBridge":
                        c.ViewerQuestion = L(lang, $"Can the {widest.DisplayNameEn} cross the narrow bridge AND make the jump?",
                                                   $"{widest.DisplayNameVi} có qua được cây cầu hẹp và hoàn thành cú nhảy?");
                        c.ViewerPromise = L(lang, $"The bridge gets narrower every round. {JoinNames(names, lang, "and")} must cross it before the Mega Ramp.",
                                                  $"Cây cầu hẹp dần qua từng vòng. {JoinNames(names, lang, "và")} phải vượt cầu trước khi lên Mega Ramp.");
                        c.Stakes = L(lang, "Fall off the bridge and the attempt is over.", "Rơi khỏi cầu là lượt đó thất bại.");
                        c.CentralConflict = L(lang, "width vs precision", "kích thước đối đầu độ chính xác");
                        c.WorkingTitle = L(lang, $"{versus}: Who Survives the Impossible Bridge?", $"{versus}: Ai Vượt Qua Cây Cầu Bất Khả?");
                        break;
                    case "ObstacleSurvival":
                        c.ViewerQuestion = L(lang, "Which vehicle gets through the landing zone obstacles?",
                                                   "Xe nào vượt qua được chướng ngại ở bãi đáp?");
                        c.ViewerPromise = L(lang, "Every round adds something new to land in: barrels, crate walls and more height.",
                                                  "Mỗi vòng thêm thử thách mới ở bãi đáp: thùng phuy, tường thùng gỗ và dốc cao hơn.");
                        c.Stakes = L(lang, "Valid landings only — rolling over scores nothing.", "Chỉ tính cú tiếp đất hợp lệ — lật xe không có điểm.");
                        c.CentralConflict = L(lang, "momentum vs obstacles", "quán tính đối đầu chướng ngại");
                        c.WorkingTitle = L(lang, $"Mega Ramp Obstacle Challenge: {versus}", $"Thử Thách Chướng Ngại Mega Ramp: {versus}");
                        break;
                    default:
                        c.ViewerQuestion = L(lang, $"Which vehicle lands furthest: {JoinNames(names, lang)}?",
                                                   $"Xe nào nhảy xa nhất: {JoinNames(names, lang)}?");
                        c.ViewerPromise = L(lang, "Same Mega Ramp, same rules — escalating rounds and one final decide it.",
                                                  "Cùng Mega Ramp, cùng luật — các vòng tăng độ khó và một trận chung kết sẽ phân định.");
                        c.Stakes = L(lang, "Round wins add up; the final is worth double.", "Thắng vòng được cộng điểm; chung kết tính gấp đôi.");
                        c.CentralConflict = L(lang, $"{lightest.ClassNounEn} speed vs {heaviest.ClassNounEn} mass",
                                                    $"tốc độ {lightest.ClassNounVi} đối đầu khối lượng {heaviest.ClassNounVi}");
                        c.WorkingTitle = L(lang, $"{versus} on a Mega Ramp — Who Lands Furthest?", $"{versus} Trên Mega Ramp — Xe Nào Nhảy Xa Nhất?");
                        break;
                }

                c.KeyVisual = TwistVisual(twist, names, lang, hTxt);
                c.CandidateThumbnailShot = entrants.Count > 1 ? "side_by_side_airborne_over_ramp" : "airborne_at_apex_low_angle";
                c.UnexpectedPossibleOutcome = L(lang,
                    $"The {heaviest.DisplayNameEn} may out-jump lighter vehicles on the taller kickers — or roll over trying.",
                    $"{heaviest.DisplayNameVi} có thể nhảy xa hơn xe nhẹ ở dốc cao — hoặc lật xe khi cố gắng.");
                c.Rules.Add("same_initial_conditions");
                c.Rules.Add("ai_drivers_same_logic");
                c.Rules.Add(seriesId == "MegaRampSurvival" ? "upright_landings_only" : "best_valid_landing_per_round");
                c.Rules.Add("final_counts_double");
                c.NoveltyTags.Add(seriesId);
                c.NoveltyTags.Add("twist_" + twist);
                foreach (var d in defs) c.NoveltyTags.Add(d.Id);
                c.NoveltyTags.Add("max_kicker_" + ((int)Math.Round(maxH / 5.0) * 5));

                Score(c, entrants, probe, history);
                // Small seeded variation in ordering text so repeated seeds stay deterministic.
                if (rng.NextDouble() < 0.5 && entrants.Count > 1 && seriesId == "VehicleVersus" && i == 2)
                    c.WorkingTitle = L(lang, $"{names[names.Count - 1]} vs {names[0]}: Mega Ramp Showdown", $"{names[names.Count - 1]} vs {names[0]}: Đại Chiến Mega Ramp");
                result.Add(c);
            }
            return result;
        }

        static string TwistVisual(string twist, IList<string> names, ContentLanguage lang, string h)
        {
            string a = names[0];
            string pair = names.Count > 1 ? $"{names[0]} and {names[1]}" : names[0];
            switch (twist)
            {
                case "obstacles": return L(lang, $"{pair} flying over a barrel-filled landing zone", $"{pair} bay qua bãi đáp đầy thùng phuy");
                case "wall": return L(lang, $"{a} airborne above a crate wall in the landing zone", $"{a} bay phía trên bức tường thùng gỗ");
                case "target": return L(lang, $"{pair} aiming for a bright target ring past the {h} m ramp", $"{pair} nhắm vòng mục tiêu sau dốc {h} m");
                case "landing": return L(lang, $"{a} dropping onto a steep landing ramp", $"{a} lao xuống dốc tiếp đất");
                case "speed": return L(lang, $"{a} launching off the lip at full speed with a long shadow below", $"{a} rời mép dốc ở tốc độ tối đa");
                case "crash": case "crash_wall": return L(lang, $"{a} hitting a concrete wall, front end crumpling, glass flying", $"{a} đâm tường bê tông, đầu xe móp, kính văng");
                case "crash_drop": return L(lang, $"{a} falling nose-first from a crane", $"{a} rơi chúi đầu từ cần cẩu");
                default: return L(lang, $"{pair} side-by-side on a narrow bridge before the Mega Ramp", $"{pair} song song trên cầu hẹp trước Mega Ramp");
            }
        }

        /// <summary>Rule-based 0–5 scores. Deliberately simple and explainable.</summary>
        public static void Score(ConceptBrief c, IList<Entrant> entrants, IList<RoundSpec> rounds, IList<NoveltyFingerprint> history)
        {
            var defs = entrants.Select(e => e.Def).ToList();
            int n = defs.Count;
            double clarity = 5 - Math.Max(0, n - 2) - (c.SeriesId == "SpeedExperiment" ? 1 : 0) - (c.TwistKind.StartsWith("bridge_") ? 0.5 : 0);
            double ratio = n > 1 ? defs.Max(d => d.MassKg) / defs.Min(d => d.MassKg) : 1;
            double contrast = n == 1 ? 2 : ratio >= 2.5 ? 5 : ratio >= 1.6 ? 4 : ratio > 1.1 ? 3 : 2;

            // Curiosity: how close are the predicted jumps on the decisive round? Closer = less predictable.
            var fin = rounds.Last();
            var dists = entrants.Select(e => RunPlanner.PredictDistance(e, fin)).ToList();
            double closeness = n > 1 ? dists.Min() / Math.Max(1, dists.Max()) : 0.5;
            double curiosity = Math.Min(5, 2 + 3 * closeness + (c.ViewerQuestion.EndsWith("?") ? 0.3 : 0));

            double payoff = fin.Rule == ScoringRule.FurthestValidLanding || fin.Rule == ScoringRule.BeyondTheWall ? 5
                          : fin.Rule == ScoringRule.TargetZone ? 4 : 3;
            double unique = 4;
            if (history != null && history.Count > 0)
            {
                var fp = NoveltyFingerprint.FromConcept(c, entrants, rounds);
                double maxSim = history.Max(h => fp.Similarity(h));
                unique = Math.Max(0, 5 - 5 * maxSim);
            }
            double feasibility = 5;
            if (c.TwistKind.StartsWith("bridge") && defs.Any(d => d.Width > 2.2f)) feasibility = 4;

            c.Scores["Clarity"] = Round1(clarity);
            c.Scores["VisualContrast"] = Round1(contrast);
            c.Scores["Curiosity"] = Round1(curiosity);
            c.Scores["PlausiblePayoff"] = Round1(payoff);
            c.Scores["UniqueTwist"] = Round1(unique);
            c.Scores["ProductionFeasibility"] = Round1(feasibility);
        }

        static double Round1(double v) => Math.Round(Math.Max(0, Math.Min(5, v)), 1);
    }
}
