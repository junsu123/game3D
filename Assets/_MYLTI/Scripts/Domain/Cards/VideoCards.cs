using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    public enum CardStyle { Hook, BigCenter, LowerThird, Badge }

    /// <summary>On-screen text burned into the final video (title cards, round intros, curiosity hooks, results).</summary>
    public sealed class VideoCard : IJsonWritable
    {
        public string Id;
        public CardStyle Style;
        public double Start, End;
        public string Title;
        public string Subtitle;
        public string Hook;               // curiosity line about what is coming (always true, never names the winner early)
        public List<string> TeaserFacts = new List<string>(); // true facts the hook may tease
        public string HookSource = "template";

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("id", Id).Prop("style", Style.ToString()).Prop("start", Math.Round(Start, 2)).Prop("end", Math.Round(End, 2))
             .Prop("title", Title).Prop("subtitle", Subtitle).Prop("hook", Hook).Prop("hookSource", HookSource).PropStrings("teaserFacts", TeaserFacts).EndObject();
        }
    }

    /// <summary>
    /// Builds the text cards from the RECORDED run, so hooks can tease what really happens next
    /// ("Not everyone lands on their wheels") without inventing anything or revealing the winner early.
    /// </summary>
    public static class VideoCardBuilder
    {
        static string N0(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
        static string Name(string id) => VehicleCatalog.Find(id)?.DisplayNameEn.ToUpperInvariant() ?? id;

        public static List<VideoCard> Build(RunRecord run)
        {
            var plan = run.Plan;
            var cards = new List<VideoCard>();
            var specs = plan.ScoredRounds.ToList();
            var attempts = run.Attempts.Where(a => !a.IsTeaser && a.Outcome != AttemptOutcome.None).ToList();
            double maxH = specs.Where(r => r.IsJump).Select(r => (double)r.KickerHeight).DefaultIfEmpty(0).Max();
            double maxCrash = specs.Where(r => r.Rule == ScoringRule.CrashWall).Select(r => (double)r.CrashSpeedKmh).DefaultIfEmpty(0).Max();
            double maxDrop = specs.Where(r => r.Rule == ScoringRule.DropTest).Select(r => (double)r.DropHeightM).DefaultIfEmpty(0).Max();
            var rounds = run.Rounds.OrderBy(r => r.RoundIndex).ToList();

            // 1. Cold-open hook over the teaser (first seconds — the most important text of the video)
            var teaser = run.Segments.FirstOrDefault(s => s.Kind == SegmentKind.Live && s.Label != null && s.Label.StartsWith("Teaser"));
            if (teaser != null)
                cards.Add(new VideoCard
                {
                    Id = "hook", Style = CardStyle.Hook, Start = teaser.Start + 0.3, End = Math.Min(teaser.End, teaser.Start + 4.5),
                    Title = maxCrash > 0 ? $"{N0(maxCrash)} KM/H INTO CONCRETE" : maxDrop > 0 ? $"DROPPED FROM {N0(maxDrop)} METERS" : $"{N0(maxH)} METER MEGA RAMP",
                    Subtitle = "WATCH THIS"
                });

            // 2. Title card: the viewer question + an honest "coming up" teaser of real moments
            var title = run.Segments.FirstOrDefault(s => s.Kind == SegmentKind.Card && s.Label == "Title card");
            if (title != null)
            {
                var coming = new List<string>();
                if (maxCrash > 0) coming.Add($"A {N0(maxCrash)} KM/H WALL CRASH");
                if (maxDrop > 0) coming.Add($"A {N0(maxDrop)} M DROP");
                if (maxH > 0) coming.Add($"A {N0(maxH)} M RAMP");
                if (attempts.Any(a => a.WheelsLost > 0)) coming.Add("FLYING WHEELS");
                if (specs.Any(r => r.Obstacles == ObstacleSet.CrateWall || r.Obstacles == ObstacleSet.BarrelsAndWall || r.Rule == ScoringRule.BeyondTheWall)) coming.Add("A CRATE WALL");
                else if (specs.Any(r => r.Obstacles != ObstacleSet.None)) coming.Add("A BARREL LINE");
                if (specs.Any(r => r.BridgeWidth > 0)) coming.Add("A NARROW BRIDGE");
                if (specs.Any(r => r.Rule == ScoringRule.TargetZone)) coming.Add("A TARGET ZONE");
                if (attempts.Any(a => a.Outcome == AttemptOutcome.Rollover)) coming.Add("A ROLLOVER");
                if (attempts.Any(a => a.Outcome == AttemptOutcome.FellOff)) coming.Add("A FALL");
                var c = new VideoCard
                {
                    Id = "title", Style = CardStyle.BigCenter, Start = title.Start + 0.2, End = title.End - 0.2,
                    Title = plan.Concept.ViewerQuestion.ToUpperInvariant(),
                    Subtitle = "COMING UP: " + string.Join(" · ", coming.Take(4))
                };
                c.TeaserFacts.AddRange(coming);
                cards.Add(c);
            }

            // 3. Round intros with a curiosity hook based on what really happens in that round
            string leader = null;
            var usedHooks = new HashSet<string>();
            int recordCount = 0;
            string[] recordLines = { "A NEW LONGEST JUMP IS COMING", "SOMEONE FLIES EVEN FURTHER", "THE RECORD FALLS AGAIN", "THE BAR GOES EVEN HIGHER" };
            var points = plan.Entrants.ToDictionary(e => e.VehicleId, e => 0);
            double bestSoFar = 0;
            foreach (var spec in specs)
            {
                var seg = run.Segments.FirstOrDefault(s => s.Kind == SegmentKind.Card && s.Label == "Round " + spec.Index + " intro");
                var rr = rounds.FirstOrDefault(r => r.RoundIndex == spec.Index);
                var ra = attempts.Where(a => a.RoundIndex == spec.Index).ToList();
                bool isFinal = spec.Role == RoundRole.Decisive;
                if (seg != null)
                {
                    var card = new VideoCard
                    {
                        Id = "round" + spec.Index, Style = CardStyle.BigCenter, Start = seg.Start + 0.1, End = seg.End + 1.2,
                        Title = isFinal ? "THE FINAL" : $"ROUND {spec.Index}",
                        Subtitle = Headline(spec)
                    };
                    var facts = new List<string>();
                    if (isFinal)
                    {
                        var ordered = points.Values.OrderByDescending(x => x).ToList();
                        bool open = ordered.Count < 2 || ordered[0] - ordered[1] <= 2; // final is worth 2 points
                        facts.Add(open ? "EVERYTHING COMES DOWN TO THIS" : "ONE LAST SHOT AT GLORY");
                    }
                    if (spec.Rule == ScoringRule.CrashWall) facts.Add("THE WALL DOES NOT MOVE");
                    if (ra.Any(a => a.WheelsLost > 0)) facts.Add("SOMETHING IS ABOUT TO COME OFF");
                    if (ra.Any(a => a.GlassBroken)) facts.Add("GLASS EVERYWHERE");
                    if (ra.Any(a => a.Outcome == AttemptOutcome.Rollover)) facts.Add("NOT EVERYONE LANDS ON THEIR WHEELS");
                    if (ra.Any(a => a.Outcome == AttemptOutcome.FellOff)) facts.Add("THE BRIDGE CLAIMS A VEHICLE");
                    double roundBest = ra.Where(a => a.ValidLanding && a.LandingDistanceM.HasValue).Select(a => a.LandingDistanceM.Value).DefaultIfEmpty(0).Max();
                    if (roundBest > bestSoFar + 0.5 && bestSoFar > 0) facts.Add(recordLines[recordCount++ % recordLines.Length]);
                    if (rr?.WinnerVehicleId != null && leader != null && rr.WinnerVehicleId != leader && !isFinal) facts.Add("THE LEADERBOARD IS ABOUT TO SHAKE");
                    if (spec.Rule == ScoringRule.BeyondTheWall)
                    {
                        int cleared = ra.Count(a => a.ClearedWall > 0.5);
                        facts.Add(cleared == 0 ? "THE WALL WINS THIS ONE" : "WHO MAKES IT OVER THE WALL?");
                    }
                    if (rr != null && rr.WinnerVehicleId == null) facts.Add("THIS ROUND ENDS WITHOUT A WINNER");
                    card.TeaserFacts.AddRange(facts);
                    string fresh = facts.FirstOrDefault(f => !usedHooks.Contains(f));
                    card.Hook = fresh ?? (spec.SubQuestion ?? "").ToUpperInvariant();
                    if (fresh != null) usedHooks.Add(fresh);
                    cards.Add(card);
                }
                bestSoFar = Math.Max(bestSoFar, ra.Where(a => a.ValidLanding && a.LandingDistanceM.HasValue).Select(a => a.LandingDistanceM.Value).DefaultIfEmpty(0).Max());
                if (rr?.WinnerVehicleId != null && points.ContainsKey(rr.WinnerVehicleId)) points[rr.WinnerVehicleId] += rr.IsFinal ? 2 : 1;
                var top = points.OrderByDescending(kv => kv.Value).ToList();
                leader = top.Count > 1 && top[0].Value == top[1].Value ? null : top[0].Key;

                // round result lower third
                var resSeg = run.Segments.FirstOrDefault(s => s.Kind == SegmentKind.Card && s.Label != null && s.Label.Contains("RESULT")
                                                                && seg != null && s.Start > seg.Start && s.Start < seg.Start + 400);
                if (resSeg != null && rr != null)
                {
                    string best = rr.WinnerVehicleId != null && rr.BestMetric.TryGetValue(rr.WinnerVehicleId, out var m)
                        ? (rr.Rule == ScoringRule.TargetZone ? $"{m.ToString("0.0", CultureInfo.InvariantCulture)} M FROM TARGET"
                           : RoundScoring.IsDamageRule(rr.Rule) ? $"LEAST DAMAGE: {m.ToString("0", CultureInfo.InvariantCulture)}%"
                           : $"{m.ToString("0.0", CultureInfo.InvariantCulture)} M")
                        : "";
                    cards.Add(new VideoCard
                    {
                        Id = "result" + spec.Index, Style = CardStyle.LowerThird, Start = resSeg.Start + 0.2, End = resSeg.End - 0.1,
                        Title = rr.WinnerVehicleId != null ? $"{(isFinal ? "FINAL" : "ROUND " + spec.Index)}: {Name(rr.WinnerVehicleId)}" : $"{(isFinal ? "FINAL" : "ROUND " + spec.Index)}: NO WINNER",
                        Subtitle = best
                    });
                }
            }

            // 4. Replay badges
            int rk = 0;
            foreach (var s in run.Segments.Where(x => x.Kind == SegmentKind.Replay))
                cards.Add(new VideoCard { Id = "replay" + (++rk), Style = CardStyle.Badge, Start = s.Start + 0.1, End = s.End - 0.1, Title = "REPLAY" });

            // 5. Final standings + outro
            var stand = run.Segments.FirstOrDefault(s => s.Kind == SegmentKind.Summary && s.Label == "Final standings");
            var all = RoundScoring.ComputeStandings(run.Rounds, run.Attempts, plan.Entrants, out string overall);
            if (stand != null)
                cards.Add(new VideoCard
                {
                    Id = "standings", Style = CardStyle.BigCenter, Start = stand.Start + 0.2, End = stand.End - 0.1,
                    Title = overall != null ? "WINNER: " + Name(overall) : "NO OVERALL WINNER",
                    Subtitle = string.Join("   ·   ", all.Select(x => $"{Name(x.VehicleId)} {x.Points} PTS"))
                });
            var outro = run.Segments.FirstOrDefault(s => s.Kind == SegmentKind.Summary && s.Label == "Outro");
            if (outro != null)
                cards.Add(new VideoCard
                {
                    Id = "outro", Style = CardStyle.BigCenter, Start = outro.Start + 0.5, End = outro.End - 0.3,
                    Title = "WHICH VEHICLE NEXT?", Subtitle = "TELL US IN THE COMMENTS"
                });
            return cards.Where(c => c.End - c.Start > 0.8).OrderBy(c => c.Start).ToList();
        }

        static string Headline(RoundSpec r)
        {
            if (r.Rule == ScoringRule.CrashWall) return $"CRASH TEST · {N0(r.CrashSpeedKmh)} KM/H INTO CONCRETE" + (r.HeadToHead ? " · ALL AT ONCE" : "");
            if (r.Rule == ScoringRule.DropTest) return $"DROP TEST · {N0(r.DropHeightM)} M CRANE" + (r.HeadToHead ? " · ALL AT ONCE" : "");
            var parts = new List<string> { $"{N0(r.KickerHeight)} M KICKER" };
            if (r.Rule == ScoringRule.LeastDamage) parts.Add("LEAST DAMAGE WINS");
            if (r.BridgeWidth > 0) parts.Add($"{r.BridgeWidth.ToString("0.#", CultureInfo.InvariantCulture)} M BRIDGE");
            switch (r.Obstacles)
            {
                case ObstacleSet.BarrelLine: parts.Add("BARRELS"); break;
                case ObstacleSet.CrateWall: parts.Add("CRATE WALL"); break;
                case ObstacleSet.BarrelsAndWall: parts.Add("BARRELS + WALL"); break;
            }
            if (r.Rule == ScoringRule.TargetZone) parts.Add("HIT THE TARGET");
            else if (r.Rule == ScoringRule.CleanLandingOnly) parts.Add("CLEAN LANDINGS ONLY");
            else if (r.Rule == ScoringRule.BeyondTheWall) parts.Add("CLEAR THE WALL");
            if (r.LandingRamp) parts.Add("LANDING RAMP");
            if (r.HeadToHead) parts.Add("ALL AT ONCE");
            return string.Join(" · ", parts);
        }

        // ------------------------------------------------------------------ Claude hooks (optional)
        public const string HookSystemPrompt =
@"You write the short on-screen curiosity hooks for a YouTube 3D vehicle challenge video (game simulation, generic vehicles).
For each card you get the card's title and a list of TRUE teaser facts about what happens next.
Write ONE punchy hook per card in English CAPITAL LETTERS, at most 7 words, that makes viewers want to keep watching.
Rules: only tease the given facts; never name who wins; never invent numbers, crashes or results; no brands; no emojis.
Reply JSON only: {""hooks"":[{""id"":""round1"",""hook"":""...""}]}";

        public static async Task ImproveHooksWithClaudeAsync(List<VideoCard> cards, ClaudeClient claude, Action<string> log)
        {
            var targets = cards.Where(c => c.TeaserFacts.Count > 0).ToList();
            if (claude == null || !claude.HasKey || targets.Count == 0) return;
            var w = new JsonWriter(true);
            w.BeginObject().Key("cards").BeginArray();
            foreach (var c in targets) w.BeginObject().Prop("id", c.Id).Prop("title", c.Title).PropStrings("trueTeaserFacts", c.TeaserFacts).EndObject();
            w.EndArray().EndObject();
            string reply = await claude.CompleteAsync(HookSystemPrompt, w.ToString(), 1500).ConfigureAwait(false);
            if (reply == null) { log?.Invoke("Claude hooks failed: " + claude.LastError); return; }
            try
            {
                var o = MiniJson.Obj(MiniJson.Parse(ClaudeClient.ExtractJson(reply) ?? "{}"));
                foreach (var item in MiniJson.Arr(MiniJson.Get(o, "hooks")) ?? new List<object>())
                {
                    var d = MiniJson.Obj(item);
                    var card = targets.FirstOrDefault(c => c.Id == MiniJson.GetString(d, "id"));
                    string hook = MiniJson.GetString(d, "hook")?.Trim();
                    if (card == null || string.IsNullOrEmpty(hook)) continue;
                    string why = CheckHook(hook, card);
                    if (why == null) { card.Hook = hook.ToUpperInvariant(); card.HookSource = "claude"; }
                    else log?.Invoke($"Hook for {card.Id} rejected ({why}): {hook}");
                }
            }
            catch (Exception e) { log?.Invoke("Claude hooks JSON invalid: " + e.Message); }
        }

        /// <summary>A hook may not name a winner, invent numbers or use brands; max ~8 words.</summary>
        public static string CheckHook(string hook, VideoCard card)
        {
            string h = hook.ToLowerInvariant();
            if (hook.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 9) return "too long";
            if (ClaimChecker.FindBlockedBrand(h) != null) return "brand";
            if (VehicleCatalog.All.Any(v => h.Contains(v.DisplayNameEn.ToLowerInvariant())) && (h.Contains("win") || h.Contains("champion") || h.Contains("beat"))) return "names a winner";
            var allowed = new HashSet<string>();
            foreach (var f in card.TeaserFacts.Concat(new[] { card.Title ?? "", card.Subtitle ?? "" }))
                foreach (var n in ClaimChecker.ExtractNumbers(f)) allowed.Add(n);
            foreach (var n in ClaimChecker.ExtractNumbers(hook)) if (!allowed.Contains(n)) return "number " + n;
            bool rolloverFact = card.TeaserFacts.Any(f => f.Contains("LANDS ON THEIR WHEELS") || f.Contains("ROLLOVER"));
            bool crashFact = card.TeaserFacts.Any(f => f.Contains("WALL DOES NOT MOVE") || f.Contains("CRASH")) || (card.Subtitle ?? "").Contains("CRASH TEST");
            if (!crashFact && !rolloverFact && h.Contains("crash")) return "crash not in facts";
            bool fallFact = card.TeaserFacts.Any(f => f.Contains("BRIDGE CLAIMS") || f.Contains("FALL"));
            if (!rolloverFact && (h.Contains("flip") || h.Contains("roll"))) return "rollover not in facts";
            if (!fallFact && (h.Contains("fall") || h.Contains("plunge"))) return "fall not in facts";
            return null;
        }

        // ------------------------------------------------------------------ FFmpeg drawtext
        /// <summary>Wrap text to lines of at most maxChars characters.</summary>
        public static string Wrap(string text, int maxChars)
        {
            var words = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            int line = 0;
            foreach (var w in words)
            {
                if (line > 0 && line + 1 + w.Length > maxChars) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(w);
                line += w.Length;
            }
            return sb.ToString();
        }

        static string Esc(string path) => "'" + path.Replace('\\', '/').Replace(":", "\\:").Replace("'", "") + "'";
        static string T(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Writes one text file per line and returns the drawtext filter chain for all cards.</summary>
        public static string BuildFilter(IList<VideoCard> cards, string textDir, string boldFont, string titleFont)
        {
            Directory.CreateDirectory(textDir);
            var parts = new List<string>();
            int k = 0;
            string Txt(string content)
            {
                string p = Path.Combine(textDir, "t" + (k++).ToString("000") + ".txt");
                File.WriteAllText(p, content, new UTF8Encoding(false));
                return p;
            }
            string Alpha(double a, double b) => $"'if(lt(t,{T(a)}+0.25),(t-{T(a)})/0.25,if(gt(t,{T(b)}-0.25),({T(b)}-t)/0.25,1))'";
            string Draw(string file, string font, int size, string color, string x, string y, double a, double b, bool box, int border)
                => $"drawtext=fontfile={Esc(font)}:textfile={Esc(file)}:expansion=none:fontsize={size}:fontcolor={color}:line_spacing=12"
                 + $":borderw={border}:bordercolor=black@0.85:x={x}:y={y}:enable='between(t,{T(a)},{T(b)})':alpha={Alpha(a, b)}"
                 + (box ? ":box=1:boxcolor=black@0.55:boxborderw=26" : "");

            foreach (var c in cards)
            {
                double a = c.Start, b = c.End;
                switch (c.Style)
                {
                    case CardStyle.Hook:
                        parts.Add(Draw(Txt(Wrap(c.Title, 22)), titleFont, 118, "white", "(w-text_w)/2", "h*0.10", a, b, false, 8));
                        if (!string.IsNullOrEmpty(c.Subtitle)) parts.Add(Draw(Txt(c.Subtitle), boldFont, 54, "0xFFD21F", "(w-text_w)/2", "h*0.10+text_h*0+150", a + 0.4, b, false, 5));
                        break;
                    case CardStyle.BigCenter:
                        int size = (c.Title ?? "").Length > 34 ? 84 : 104;
                        parts.Add(Draw(Txt(Wrap(c.Title, size == 84 ? 32 : 26)), titleFont, size, "white", "(w-text_w)/2", "h*0.47-text_h", a, b, false, 8));
                        if (!string.IsNullOrEmpty(c.Subtitle)) parts.Add(Draw(Txt(Wrap(c.Subtitle, 62)), boldFont, 46, "0xFFD21F", "(w-text_w)/2", "h*0.53", a + 0.2, b, true, 0));
                        if (!string.IsNullOrEmpty(c.Hook)) parts.Add(Draw(Txt(Wrap(c.Hook, 40)), titleFont, 64, "0xFF4436", "(w-text_w)/2", "h*0.68", a + 0.6, b, false, 6));
                        break;
                    case CardStyle.LowerThird:
                        parts.Add(Draw(Txt(c.Title), titleFont, 64, "white", "90", "h-260", a, b, true, 0));
                        if (!string.IsNullOrEmpty(c.Subtitle)) parts.Add(Draw(Txt(c.Subtitle), boldFont, 46, "0xFFD21F", "96", "h-160", a + 0.2, b, false, 4));
                        break;
                    case CardStyle.Badge:
                        parts.Add(Draw(Txt(c.Title), titleFont, 54, "white", "60", "50", a, b, true, 0));
                        break;
                }
            }
            return parts.Count == 0 ? "null" : string.Join(",\n", parts);
        }

        public static string FindFont(params string[] names)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            foreach (var n in names)
            {
                string p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}
