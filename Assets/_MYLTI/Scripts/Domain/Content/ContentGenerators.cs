using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mylti.Domain
{
    public sealed class TitleOption : IJsonWritable
    {
        public string Category;     // Curiosity, Versus, ExtremeChallenge, SearchFriendly, StoryDriven
        public string Text;
        public bool Recommended;
        public string TruthCheckStatus;
        public int Length => Text?.Length ?? 0;

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("category", Category).Prop("text", Text).Prop("recommended", Recommended)
                .Prop("length", Length).Prop("truthCheckStatus", TruthCheckStatus).EndObject();
        }
    }

    public sealed class TitleThumbnailPair : IJsonWritable
    {
        public string PairId;            // A, B, C
        public string Angle;             // Versus/contrast, High stakes/result curiosity, Search intent + twist
        public string Title;
        public string PrimaryKeyword;
        public string OneSentencePromise;
        public string ThumbnailFrameId;  // highlight id
        public string ThumbnailFrameFile;
        public string ThumbnailPrompt;
        public string ThumbnailText;     // 0–3 words
        public string TruthCheckStatus;

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("pairId", PairId).Prop("angle", Angle).Prop("title", Title)
                .Prop("primaryKeyword", PrimaryKeyword).Prop("oneSentencePromise", OneSentencePromise)
                .Prop("thumbnailFrameId", ThumbnailFrameId).Prop("thumbnailFrameFile", ThumbnailFrameFile)
                .Prop("thumbnailPrompt", ThumbnailPrompt).Prop("thumbnailText", ThumbnailText)
                .Prop("truthCheckStatus", TruthCheckStatus).EndObject();
        }
    }

    /// <summary>Offline template engine (Tier 1, spec §18.4). Not an AI — deterministic templates over the fact sheet.</summary>
    public static class TitleGenerator
    {
        static string Num(double v) => Math.Floor(v).ToString(CultureInfo.InvariantCulture);

        static string Art(string noun) => !string.IsNullOrEmpty(noun) && ("aeiouAEIOU".IndexOf(noun[0]) >= 0 || noun.StartsWith("SUV")) ? "an" : "a";

        /// <summary>English article for a spoken number: "an 8m", "an 18m", "a 22m".</summary>
        static string A(string num) => num != null && (num.StartsWith("8") || num == "11" || num == "18" || (num.Length == 5 && (num.StartsWith("11") || num.StartsWith("18")))) ? "an" : "a";

        public static List<TitleOption> Generate(ContentFactSheet fs)
        {
            bool vi = fs.Language == ContentLanguage.vi;
            var v = fs.Vehicles;
            string versus = string.Join(" vs ", v.Select(x => x.Name));
            var heavy = v.OrderByDescending(x => VehicleCatalog.Find(x.VehicleId)?.MassKg ?? 0).FirstOrDefault();
            string H = fs.MaxKickerHeightM.HasValue ? Num(fs.MaxKickerHeightM.Value) : null;
            var winner = fs.OverallWinnerId != null ? fs.V(fs.OverallWinnerId) : null;
            var longestV = fs.LongestJump != null ? fs.V(fs.LongestJump.Text.Split('@')[0]) : null;
            int rounds = fs.CompletedRoundCount;

            var cands = new List<(string cat, List<string> options)>();

            // Curiosity
            var cur = new List<string>();
            if (H != null && heavy != null)
            {
                cur.Add(vi ? $"{heavy.Name} Có Sống Sót Qua Mega Ramp {H}m?" : $"Can {Art(heavy.ClassNoun)} {Cap(heavy.ClassNoun)} Survive {A(H)} {H}m Mega Ramp?");
                cur.Add(vi ? $"Điều Gì Xảy Ra Khi {heavy.Name} Lao Lên Mega Ramp {H}m?" : $"What Happens When {Art(heavy.ClassNoun)} {Cap(heavy.ClassNoun)} Hits {A(H)} {H}m Mega Ramp?");
            }
            cur.Add(vi ? "Xe Nào Bay Xa Nhất Trên Mega Ramp?" : "Which Vehicle Flies Furthest Off a Mega Ramp?");
            cands.Add(("Curiosity", cur));

            // Versus
            var vs = new List<string>();
            if (v.Count >= 2)
            {
                if (H != null) vs.Add(vi ? $"{versus} Trên Mega Ramp {H}m — Ai Thắng?" : $"{versus} on {A(H)} {H}m Mega Ramp — Who Wins?");
                vs.Add(vi ? $"{versus}: Đại Chiến Mega Ramp" : $"{versus}: Mega Ramp Showdown");
            }
            else vs.Add(vi ? $"{v[0].Name} Đối Đầu Mega Ramp" : $"{v[0].Name} vs the Mega Ramp");
            cands.Add(("Versus", vs));

            // Extreme challenge
            var ex = new List<string>();
            if (longestV != null && fs.LongestJump.Number.HasValue && fs.LongestJumpKickerM.HasValue)
            {
                string LH = Num(fs.LongestJumpKickerM.Value); // kicker of the round where that jump really happened
                ex.Add(vi ? $"{longestV.Name} Bay {Num(fs.LongestJump.Number.Value)} Mét Từ Mega Ramp {LH}m"
                          : $"{longestV.Name} Flies {Num(fs.LongestJump.Number.Value)} Meters Off {A(LH)} {LH}m Mega Ramp");
            }
            if (H != null && fs.RolloverCount > 0)
                ex.Add(vi ? $"Mega Ramp {H}m: Lật Xe, Bay Xa Và Một Trận Chung Kết" : $"{H}m Mega Ramp: Rollovers, Huge Jumps & One Final");
            if (H != null) ex.Add(vi ? $"Thử Thách Mega Ramp {H}m Cực Hạn: {versus}" : $"Extreme {H}m Mega Ramp Challenge: {versus}");
            ex.Add(vi ? $"Thử Thách Mega Ramp Cực Hạn: {versus}" : $"Extreme Mega Ramp Challenge: {versus}");
            cands.Add(("ExtremeChallenge", ex));

            // Search friendly
            var se = new List<string>
            {
                vi ? $"Thử Thách Xe Nhảy Mega Ramp: {versus} | Mô Phỏng 3D" : $"Mega Ramp Car Jump Challenge: {versus} | 3D Simulation",
                vi ? $"Thử Thách Xe Nhảy Mega Ramp | {versus}" : $"Mega Ramp Car Jump Challenge | {versus}",
                vi ? "Thử Thách Xe Nhảy Mega Ramp | Mô Phỏng 3D" : "Mega Ramp Car Jump Challenge | 3D Simulation"
            };
            cands.Add(("SearchFriendly", se));

            // Story driven
            var st = new List<string>();
            if (fs.ComebackVehicleId != null)
            {
                var cb = fs.V(fs.ComebackVehicleId);
                st.Add(vi ? $"{cb.Name} Đang Thua… Cho Đến Mega Ramp Chung Kết" : $"The {cb.Name} Was Losing… Until the Final Mega Ramp");
            }
            if (winner != null && rounds > 0)
                st.Add(vi ? $"{winner.Name} Thắng {winner.RoundWins}/{rounds} Vòng Mega Ramp" : $"{winner.Name} Wins {winner.RoundWins} of {rounds} Mega Ramp Rounds");
            if (rounds > 0) st.Add(vi ? $"{rounds} Vòng, Một Mega Ramp: {versus}" : $"{rounds} Rounds, One Mega Ramp: {versus}");
            st.Add(vi ? $"Một Mega Ramp, Nhiều Vòng: {versus}" : $"One Mega Ramp, Many Rounds: {versus}");
            cands.Add(("StoryDriven", st));

            var result = new List<TitleOption>();
            foreach (var (cat, options) in cands)
            {
                TitleOption chosen = null;
                foreach (var text in options)
                {
                    var issues = ClaimChecker.Check("title:" + cat, text, fs, isTitle: true);
                    if (issues.Any(i => i.Level == CheckLevel.Fail)) continue;
                    if (text.Length > 90) continue;
                    chosen = new TitleOption { Category = cat, Text = text, TruthCheckStatus = "Pass" };
                    break;
                }
                if (chosen == null)
                {
                    string safe = vi ? $"Thử Thách Mega Ramp | {versus}" : $"Mega Ramp Challenge | {versus}";
                    chosen = new TitleOption { Category = cat, Text = safe, TruthCheckStatus = ClaimChecker.Status(ClaimChecker.Check("title:fallback", safe, fs, true)) };
                }
                result.Add(chosen);
            }

            string rec = fs.ComebackVehicleId != null ? "StoryDriven" : v.Count >= 2 ? "Versus" : "Curiosity";
            foreach (var t in result) t.Recommended = t.Category == rec;
            return result;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : string.Join(" ", s.Split(' ').Select(w => w.Length > 1 && w.ToUpperInvariant() != w ? char.ToUpperInvariant(w[0]) + w.Substring(1) : w));
    }

    public static class DescriptionComposer
    {
        static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        static string F0(double v) => Math.Floor(v).ToString(CultureInfo.InvariantCulture);

        public static string Compose(ContentFactSheet fs, RunPlan plan)
        {
            bool vi = fs.Language == ContentLanguage.vi;
            var sb = new StringBuilder();
            string versus = string.Join(" vs ", fs.Vehicles.Select(x => x.Name));
            var winner = fs.OverallWinnerId != null ? fs.V(fs.OverallWinnerId) : null;
            int n = fs.CompletedRoundCount;

            // 1–2 opening lines: real challenge + real result + primary keywords.
            if (vi)
            {
                sb.Append($"{versus} đối đầu qua {n} vòng thử thách Mega Ramp tăng dần độ khó");
                sb.Append(winner != null ? $" — {winner.Name} giành chiến thắng chung cuộc với {winner.Points} điểm." : " — và kết thúc không có xe thắng chung cuộc.");
            }
            else
            {
                sb.Append($"{versus} take on {n} escalating Mega Ramp challenge rounds");
                sb.Append(winner != null ? $" — and the {winner.Name} wins overall with {winner.Points} points." : " — and it ends without a single overall winner.");
            }
            sb.Append('\n');
            if (fs.MinKickerHeightM.HasValue && fs.MaxKickerHeightM.HasValue)
            {
                sb.Append(vi ? $"Dốc nhảy tăng từ {F0(fs.MinKickerHeightM.Value)} m lên {F0(fs.MaxKickerHeightM.Value)} m"
                             : $"The kicker grows from {F0(fs.MinKickerHeightM.Value)} m to {F0(fs.MaxKickerHeightM.Value)} m");
                var extras = new List<string>();
                if (fs.HadObstacleRound) extras.Add(vi ? "chướng ngại ở bãi đáp" : "landing-zone obstacles");
                if (fs.HadWallRound) extras.Add(vi ? "tường thùng gỗ" : "a crate wall");
                if (fs.HadTargetRound) extras.Add(vi ? "vòng mục tiêu" : "a target-zone round");
                if (fs.HadBridgeRound) extras.Add(vi ? "cầu hẹp trước dốc" : "a narrow bridge before the ramp");
                if (extras.Count > 0) sb.Append(vi ? ", kèm " : ", with ").Append(ConceptGenerator.JoinNames(extras, fs.Language, vi ? "và" : "and"));
                sb.Append(".\n");
            }
            sb.Append('\n');

            sb.Append(vi ? "Diễn biến từng vòng:\n" : "Round by round:\n");
            var specs = plan?.ScoredRounds.ToDictionary(r => r.Index) ?? new Dictionary<int, RoundSpec>();
            foreach (var r in fs.Rounds.Where(x => x.Completed).OrderBy(x => x.RoundIndex))
            {
                specs.TryGetValue(r.RoundIndex, out var spec);
                string head = r.IsFinal ? (vi ? "Chung kết" : "Final") : (vi ? $"Vòng {r.RoundIndex}" : $"Round {r.RoundIndex}");
                string kick = spec != null ? (vi ? $" (dốc {F0(spec.KickerHeight)} m)" : $" ({F0(spec.KickerHeight)} m kicker)") : "";
                sb.Append("• ").Append(head).Append(kick).Append(": ").Append(r.Explanation).Append('\n');
            }
            sb.Append('\n');

            var moments = new List<string>();
            if (fs.LongestJump?.Number != null)
            {
                var lv = fs.V(fs.LongestJump.Text.Split('@')[0]);
                moments.Add(vi ? $"Cú nhảy xa nhất: {lv?.Name} — {F1(fs.LongestJump.Number.Value)} m." : $"Longest landing: {lv?.Name} — {F1(fs.LongestJump.Number.Value)} m.");
            }
            if (fs.FastestSpeed?.Number != null)
            {
                var sv = fs.V(fs.FastestSpeed.Text.Split('@')[0]);
                moments.Add(vi ? $"Tốc độ cao nhất ghi nhận: {sv?.Name} — {F0(fs.FastestSpeed.Number.Value)} km/h (trong game)." : $"Top recorded speed: {sv?.Name} — {F0(fs.FastestSpeed.Number.Value)} km/h (in-game).");
            }
            if (fs.RolloverCount > 0)
                moments.Add(vi ? $"Số lần lật xe: {fs.RolloverCount}." : $"Rollovers during the episode: {fs.RolloverCount}.");
            if (moments.Count > 0)
            {
                sb.Append(vi ? "Khoảnh khắc đáng chú ý:\n" : "Notable moments:\n");
                foreach (var m in moments) sb.Append("• ").Append(m).Append('\n');
                sb.Append('\n');
            }

            if (fs.ChaptersReliable && fs.Chapters.Count >= 3)
            {
                sb.Append(vi ? "Mốc thời gian:\n" : "Chapters:\n");
                foreach (var c in fs.Chapters) sb.Append(Timestamp(c.Time)).Append(' ').Append(c.Label).Append('\n');
                sb.Append('\n');
            }

            sb.Append(vi
                ? "Đây là mô phỏng trong game 3D (MYLTI Challenge Studio, vật lý thân cứng) — không phải thử nghiệm xe ngoài đời thực và không mô phỏng biến dạng thân xe.\n"
                : "This is a 3D game simulation made with MYLTI Challenge Studio (rigid-body vehicle physics) — not a real-world vehicle test, and body deformation is not simulated.\n");
            sb.Append(vi
                ? "Bạn muốn xe nào tham gia thử thách tiếp theo? Bình luận bên dưới và đăng ký kênh để xem tập sau!\n\n"
                : "Which vehicle should take on the next challenge? Tell us in the comments and subscribe for the next episode!\n\n");

            var tags = new List<string> { "#MegaRamp", "#CarJump", vi ? "#MoPhongXe" : "#VehicleSimulation" };
            if (fs.SeriesId == "ImpossibleBridge") tags.Add("#ImpossibleBridge");
            else if (fs.Vehicles.Count > 1) tags.Add("#VehicleVersus");
            sb.Append(string.Join(" ", tags.Take(4)));
            return sb.ToString();
        }

        public static string Timestamp(double seconds)
        {
            int s = (int)Math.Floor(seconds);
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }
    }

    public static class ThumbnailPromptBuilder
    {
        /// <summary>English prompt describing a REAL moment of the run (spec §18.2.3, §23.1).</summary>
        public static string Build(ContentFactSheet fs, RunRecord run, Highlight h, string angle, string thumbnailText)
        {
            var attempt = h != null ? run.Attempts.FirstOrDefault(a => a.AttemptId == h.AttemptId) : null;
            var spec = attempt != null ? run.Plan?.Phases.Select(p => p.Round).FirstOrDefault(r => r != null && r.Index == attempt.RoundIndex) : null;
            var sb = new StringBuilder();
            sb.Append("Create a cinematic 16:9 YouTube gaming thumbnail (1280x720) of a stylised 3D game scene. ");

            // Subjects: the vehicles really involved.
            var involved = new List<VehicleFacts>();
            if (spec != null && spec.HeadToHead) involved.AddRange(fs.Vehicles);
            else if (attempt != null && fs.V(attempt.VehicleId) != null) involved.Add(fs.V(attempt.VehicleId));
            if (involved.Count == 0) involved.AddRange(fs.Vehicles);
            string subjects = string.Join(" and ", involved.Select(v => $"a {v.ColorName} generic {v.ClassNoun.ToLowerInvariant()}".Replace("  ", " ")));
            if (fs.Language == ContentLanguage.vi)
            {
                // Prompt stays English (spec), but class nouns come from the English catalogue.
                subjects = string.Join(" and ", involved.Select(v => $"a {v.ColorName} generic {VehicleCatalog.Find(v.VehicleId)?.ClassNounEn}"));
            }

            string action;
            if (attempt == null) action = "launching off the lip of a giant mega ramp";
            else
            {
                switch (attempt.Outcome)
                {
                    case AttemptOutcome.Rollover: action = "airborne off a giant mega ramp and tilting sideways, about to roll over"; break;
                    case AttemptOutcome.FellOff: action = "slipping off the edge of a narrow bridge before the ramp"; break;
                    case AttemptOutcome.NoJump: action = "climbing a steep giant mega ramp"; break;
                    default: action = involved.Count > 1 ? "side by side in mid-air above a giant mega ramp" : "high in mid-air at the top of its arc above a giant mega ramp"; break;
                }
            }
            sb.Append($"Show {subjects} {action}. ");
            if (spec != null)
            {
                sb.Append($"The wooden-and-concrete kicker ramp is clearly visible (about {spec.KickerHeight:0} m tall in the game), with a long asphalt runway behind and a wide grass landing field with distance marker boards ahead. ");
                if (spec.Obstacles == ObstacleSet.BarrelLine || spec.Obstacles == ObstacleSet.BarrelsAndWall) sb.Append("A line of orange barrels sits in the landing zone. ");
                if (spec.Obstacles == ObstacleSet.CrateWall || spec.Obstacles == ObstacleSet.BarrelsAndWall) sb.Append("A stacked wall of wooden crates stands in the landing zone. ");
                if (spec.Rule == ScoringRule.TargetZone) sb.Append("A bright circular target ring is painted on the landing field. ");
                if (spec.BridgeWidth > 0) sb.Append("A narrow bridge over a water pit leads to the ramp. ");
                if (spec.LandingRamp) sb.Append("A downhill landing ramp waits beyond the gap. ");
            }
            sb.Append($"Angle: {angle}. Dramatic low camera angle, clear blue sky, strong rim light, high contrast, saturated colours, one clear focal point, vehicles large in frame so they read on a phone screen, simple uncluttered background. ");
            sb.Append(string.IsNullOrWhiteSpace(thumbnailText)
                ? "No text on the image. "
                : $"Optional bold short text (max 3 words): \"{thumbnailText}\". ");
            sb.Append("No logos, no real car brands, no license plates, no people, no copyrighted characters. ");
            if (!string.IsNullOrEmpty(h?.FrameFile))
                sb.Append($"Use the attached gameplay reference frame ({h.FrameFile}) and preserve its vehicle types, colours, ramp and obstacle layout.");
            else
                sb.Append("Preserve the actual vehicle types, colours and challenge geometry of the gameplay; do not invent other vehicles or events.");
            return sb.ToString();
        }
    }
}
