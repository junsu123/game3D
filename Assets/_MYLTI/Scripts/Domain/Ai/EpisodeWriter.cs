using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    /// <summary>One produced episode, kept so new scripts never repeat old ones.</summary>
    public sealed class EpisodeLogEntry : IJsonWritable
    {
        public string RunId, Date, SeriesId, TwistKind, ViewerQuestion, Title, RoundsSummary, Winner, ScriptSource;
        public List<string> Vehicles = new List<string>();

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("runId", RunId).Prop("date", Date).Prop("seriesId", SeriesId).Prop("twistKind", TwistKind)
                .Prop("viewerQuestion", ViewerQuestion).Prop("title", Title).Prop("roundsSummary", RoundsSummary)
                .Prop("winner", Winner).Prop("scriptSource", ScriptSource).PropStrings("vehicles", Vehicles).EndObject();
        }

        public static EpisodeLogEntry FromJson(Dictionary<string, object> o)
        {
            var e = new EpisodeLogEntry
            {
                RunId = MiniJson.GetString(o, "runId"), Date = MiniJson.GetString(o, "date"), SeriesId = MiniJson.GetString(o, "seriesId"),
                TwistKind = MiniJson.GetString(o, "twistKind"), ViewerQuestion = MiniJson.GetString(o, "viewerQuestion"),
                Title = MiniJson.GetString(o, "title"), RoundsSummary = MiniJson.GetString(o, "roundsSummary"),
                Winner = MiniJson.GetString(o, "winner"), ScriptSource = MiniJson.GetString(o, "scriptSource")
            };
            var v = MiniJson.Arr(MiniJson.Get(o, "vehicles"));
            if (v != null) foreach (var x in v) if (x is string s) e.Vehicles.Add(s);
            return e;
        }
    }

    public sealed class EpisodeLog
    {
        public readonly string FilePath;
        public readonly List<EpisodeLogEntry> Items = new List<EpisodeLogEntry>();

        public EpisodeLog(string path)
        {
            FilePath = path;
            if (!File.Exists(path)) return;
            try
            {
                var arr = MiniJson.Arr(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)));
                if (arr != null) foreach (var o in arr) { var d = MiniJson.Obj(o); if (d != null) Items.Add(EpisodeLogEntry.FromJson(d)); }
            }
            catch (Exception) { File.Copy(path, path + ".bak", true); }
        }

        public void Add(EpisodeLogEntry e)
        {
            Items.RemoveAll(x => x.RunId == e.RunId);
            Items.Add(e);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, MiniJson.Serialize(Items), new UTF8Encoding(false));
        }

        public static EpisodeLogEntry FromRun(RunRecord run, ContentPack pack, string scriptSource)
        {
            var plan = run.Plan;
            return new EpisodeLogEntry
            {
                RunId = run.RunId,
                Date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SeriesId = plan?.Concept?.SeriesId,
                TwistKind = plan?.Concept?.TwistKind,
                ViewerQuestion = plan?.Concept?.ViewerQuestion,
                Title = pack?.Titles.FirstOrDefault(t => t.Recommended)?.Text,
                RoundsSummary = plan == null ? "" : string.Join(" | ", plan.ScoredRounds.Select(r => $"{r.KickerHeight:0}m {r.Rule} {r.Obstacles}{(r.BridgeWidth > 0 ? " bridge" : "")}{(r.LandingRamp ? " landingRamp" : "")}")),
                Winner = pack?.Facts?.OverallWinnerId,
                ScriptSource = scriptSource,
                Vehicles = plan?.Entrants.Select(e => e.VehicleId).ToList() ?? new List<string>()
            };
        }
    }

    public sealed class EpisodeScript
    {
        public ConceptBrief Concept;
        public List<Entrant> Entrants;
        public List<RoundSpec> Rounds;
        public string Source;               // "claude:<model>" or "offline"
        public List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Writes a fresh 10-minute episode script (concept + 6 round designs).
    /// Claude is used when a key is available; otherwise an offline rotation picks the least-used series/twist/line-up.
    /// Every value Claude returns is validated and clamped; results are never scripted, only the challenge design.
    /// </summary>
    public static class EpisodeWriter
    {
        static readonly string[] Twists = { "obstacles", "target", "wall", "landing", "speed", "bridge", "crash", "crash_drop", "crash_wall" };
        static readonly string[] Palette = { "#D12B38", "#2C82D3", "#E8A33D", "#2DBE6C", "#8E44AD", "#F1C40F", "#ECF0F1", "#222831" };

        public const string SystemPrompt =
@"You are the showrunner of a YouTube channel that publishes 10-minute 3D vehicle challenge episodes made in a game simulation (rigid-body physics with simulated body damage, 3D vehicle models, a configurable Mega Ramp arena with a crash wall and a drop crane).
You design the CHALLENGE, never the result: outcomes come from the physics simulation and are reported honestly later.
Rules:
- Every episode must feel new compared with the episode history you are given: change the series, twist, vehicle line-up, rule sequence or geometry. Do not reuse a previous viewer question or title.
- Design exactly 6 rounds: 5 escalating rounds and a decisive final. Each round must change at least one meaningful variable (height, angle, speed, obstacles, rule, crash speed, drop height, bridge, landing ramp). Not just colours.
- Mix jumps with crash tests (CrashWall) and drop tests (DropTest) — viewers love seeing what survives; build up the speeds and heights.
- Round roles in order: Baseline, Complication, Reversal, Escalation, Extreme, Decisive. The final (Decisive) is always head-to-head.
- Use the vehicle names exactly as written in the catalogue and no other brand names. No real people, no claims of real-world physics, realistic damage, real crash-test results or scientific proof — it is a game simulation.
- English, short, punchy, curiosity-driven but truthful wording. Titles must be phrased as a question or a neutral challenge statement (no invented outcomes).
Reply with ONE JSON object only, no prose.";

        public static string BuildUserPrompt(IList<EpisodeLogEntry> history, IList<Entrant> currentEntrants, bool letClaudePickVehicles, string difficulty, int seed)
        {
            var w = new JsonWriter(true);
            w.BeginObject();
            w.Key("vehicleCatalogue").BeginArray();
            foreach (var d in VehicleCatalog.Selectable)
                w.BeginObject().Prop("id", d.Id).Prop("name", d.DisplayNameEn).Prop("massKg", d.MassKg).Prop("topSpeedKmhInGame", d.TopSpeedKmh)
                 .Prop("defaultTargetSpeedKmh", d.DefaultTargetSpeedKmh).EndObject();
            w.EndArray();
            w.Prop("vehicleChoice", letClaudePickVehicles ? "choose 2 or 3 vehicles from the catalogue (vary the line-up vs history)" : "use exactly: " + string.Join(", ", currentEntrants.Select(e => e.VehicleId)));
            w.PropStrings("seriesIds", ConceptGenerator.SeriesIds);
            w.PropStrings("twistKinds", Twists);
            w.Key("parameterRanges").BeginObject()
                .Prop("kickerHeight", "2..30 metres").Prop("kickerAngle", "8..40 degrees").Prop("speedFactor", "0.4..1.3 (multiplies each vehicle's target speed)")
                .Prop("obstacles", "None | BarrelLine | CrateWall | BarrelsAndWall")
                .Prop("rule", "FurthestValidLanding | CleanLandingOnly | TargetZone | BeyondTheWall | LeastDamage (jump, least body damage wins) | CrashWall (drive into a concrete wall at crashSpeedKmh 30-160, least damage wins) | DropTest (dropped from a crane at dropHeightM 5-60, least damage wins)")
                .Prop("bridgeWidth", "0 (none) or 3..8 metres; trucks are 2.4 m wide").Prop("landingRamp", "true/false").Prop("headToHead", "true = all vehicles at once").EndObject();
            w.Prop("difficulty", difficulty);
            w.Prop("seed", seed);
            w.Key("episodeHistory(newest last)").BeginArray();
            foreach (var h in history.Skip(Math.Max(0, history.Count - 25)))
                w.BeginObject().Prop("series", h.SeriesId).Prop("twist", h.TwistKind).Prop("vehicles", string.Join("+", h.Vehicles))
                 .Prop("question", h.ViewerQuestion).Prop("title", h.Title).Prop("rounds", h.RoundsSummary).EndObject();
            w.EndArray();
            w.Key("replyJsonShape").BeginObject()
                .Prop("seriesId", "one of seriesIds").Prop("twistKind", "one of twistKinds").Prop("vehicles", "[\"id\", ...]")
                .Prop("viewerQuestion", "string").Prop("viewerPromise", "string").Prop("stakes", "string").Prop("centralConflict", "string")
                .Prop("keyVisual", "string").Prop("workingTitle", "string (<= 70 chars)").Prop("unexpectedPossibleOutcome", "string")
                .Prop("rounds", "[6 x {role, kickerHeight, kickerAngle, speedFactor, obstacles, rule, crashSpeedKmh, dropHeightM, bridgeWidth, landingRamp, headToHead, subQuestion}]")
                .EndObject();
            w.EndObject();
            return "Design the next episode. Context:\n" + w;
        }

        public static EpisodeScript Parse(string reply, IList<Entrant> fallbackEntrants, int seed, out string error)
        {
            error = null;
            string json = ClaudeClient.ExtractJson(reply);
            if (json == null) { error = "No JSON in Claude reply."; return null; }
            Dictionary<string, object> o;
            try { o = MiniJson.Obj(MiniJson.Parse(json)); }
            catch (JsonException e) { error = "Bad JSON from Claude: " + e.Message; return null; }
            if (o == null) { error = "Claude reply is not an object."; return null; }

            var script = new EpisodeScript { Notes = new List<string>() };
            string series = MiniJson.GetString(o, "seriesId");
            if (!ConceptGenerator.SeriesIds.Contains(series)) { script.Notes.Add($"series '{series}' unknown → VehicleVersus"); series = "VehicleVersus"; }
            string twist = MiniJson.GetString(o, "twistKind");
            if (!Twists.Contains(twist)) twist = "obstacles";

            // vehicles
            var ids = (MiniJson.Arr(MiniJson.Get(o, "vehicles")) ?? new List<object>()).OfType<string>()
                .Select(x => VehicleCatalog.Find(x)?.Id).Where(x => x != null).Distinct().Take(3).ToList();
            List<Entrant> entrants;
            if (ids.Count >= 1 && (ids.Count >= 2 || series != "VehicleVersus"))
            {
                var rng = new Random(seed);
                var colors = Palette.OrderBy(_ => rng.Next()).ToList();
                entrants = ids.Select((id, i) => new Entrant
                {
                    VehicleId = id, Lane = i, Mode = DriveMode.AI,
                    ColorHex = fallbackEntrants.FirstOrDefault(e => e.VehicleId == id)?.ColorHex ?? colors[i],
                    TargetSpeedKmh = VehicleCatalog.Find(id).DefaultTargetSpeedKmh
                }).ToList();
            }
            else entrants = fallbackEntrants.Select(e => e.Clone()).ToList();
            if (entrants.Select(e => e.ColorHex).Distinct().Count() < entrants.Count)
                for (int i = 0; i < entrants.Count; i++) entrants[i].ColorHex = Palette[i];

            // rounds
            var arr = MiniJson.Arr(MiniJson.Get(o, "rounds"));
            if (arr == null || arr.Count < 6) { error = "Claude must return 6 rounds."; return null; }
            var roles = new[] { RoundRole.Baseline, RoundRole.Complication, RoundRole.Reversal, RoundRole.Escalation, RoundRole.Extreme, RoundRole.Decisive };
            var rounds = new List<RoundSpec>();
            var warn = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                var r = MiniJson.Obj(arr[i]) ?? new Dictionary<string, object>();
                var spec = new RoundSpec
                {
                    Index = i + 1,
                    Role = roles[i],
                    KickerHeight = (float)MiniJson.GetDouble(r, "kickerHeight", 6 + i * 3),
                    KickerAngleDeg = (float)MiniJson.GetDouble(r, "kickerAngle", 18 + i * 2),
                    SpeedFactor = (float)MiniJson.GetDouble(r, "speedFactor", 0.9 + i * 0.04),
                    Obstacles = ScenarioConfig.ParseEnum(MiniJson.GetString(r, "obstacles"), ObstacleSet.None),
                    Rule = ScenarioConfig.ParseEnum(MiniJson.GetString(r, "rule"), ScoringRule.FurthestValidLanding),
                    BridgeWidth = (float)MiniJson.GetDouble(r, "bridgeWidth", 0),
                    LandingRamp = MiniJson.GetBool(r, "landingRamp", false),
                    CrashSpeedKmh = (float)MiniJson.GetDouble(r, "crashSpeedKmh", 60 + i * 12),
                    DropHeightM = (float)MiniJson.GetDouble(r, "dropHeightM", 12 + i * 5),
                    HeadToHead = i == 5 || MiniJson.GetBool(r, "headToHead", false),
                    AttemptPasses = 1,
                    MaxExtraPasses = 2
                };
                ScenarioConfig.ClampRound(spec, warn, "round" + (i + 1));
                if (spec.BridgeWidth > 0 && spec.BridgeWidth < entrants.Max(e => e.Def.Width) + 0.8f) spec.BridgeWidth = entrants.Max(e => e.Def.Width) + 0.8f;
                rounds.Add(spec);
            }
            if (rounds[4].IsJump && rounds[0].IsJump && rounds[4].KickerHeight < rounds[0].KickerHeight) rounds[4].KickerHeight = Math.Min(30, rounds[0].KickerHeight + 8);
            RunPlanner.RefreshRoundTexts(rounds, entrants, ContentLanguage.en);
            for (int i = 0; i < 6; i++)
            {
                var r = MiniJson.Obj(arr[i]);
                string q = MiniJson.GetString(r, "subQuestion");
                if (!string.IsNullOrWhiteSpace(q) && q.Length < 140 && !q.Contains("\n")) rounds[i].SubQuestion = q.Trim();
                if (rounds[i].ChangeSummary.Contains("no meaningful change") && i > 0)
                {
                    rounds[i].KickerHeight = Math.Min(30, rounds[i - 1].KickerHeight + 2);
                    RunPlanner.RefreshRoundTexts(rounds, entrants, ContentLanguage.en);
                }
            }
            script.Notes.AddRange(warn);

            string S(string k, string d) { var v = MiniJson.GetString(o, k); return string.IsNullOrWhiteSpace(v) ? d : v.Trim(); }
            string versus = ConceptGenerator.VersusLine(entrants, ContentLanguage.en);
            var concept = new ConceptBrief
            {
                ConceptId = ScenarioConfig.SanitizeId($"claude_{series}_{twist}_{seed}"),
                SeriesId = series,
                TwistKind = twist,
                ViewerQuestion = S("viewerQuestion", $"Which vehicle handles the Mega Ramp best: {versus}?"),
                ViewerPromise = S("viewerPromise", "Five escalating rounds and a head-to-head final."),
                Stakes = S("stakes", "Round wins add up; the final counts double."),
                CentralConflict = S("centralConflict", "speed vs mass"),
                KeyVisual = S("keyVisual", "vehicles airborne over the Mega Ramp"),
                WorkingTitle = S("workingTitle", $"{versus} — Mega Ramp Challenge"),
                UnexpectedPossibleOutcome = S("unexpectedPossibleOutcome", ""),
                CandidateThumbnailShot = entrants.Count > 1 ? "side_by_side_airborne_over_ramp" : "airborne_at_apex_low_angle"
            };
            // reject branded or "realism" wording in Claude's creative text
            foreach (var field in new[] { concept.ViewerQuestion, concept.ViewerPromise, concept.WorkingTitle, concept.Stakes })
                if (ClaimChecker.FindBlockedBrand(field.ToLowerInvariant()) != null || field.ToLowerInvariant().Contains("real-world"))
                {
                    error = "Claude used a brand or real-world claim: " + field;
                    return null;
                }
            concept.Rules.AddRange(new[] { "same_initial_conditions", "ai_drivers_same_logic", "final_counts_double" });
            concept.NoveltyTags.AddRange(new[] { series, "twist_" + twist });
            concept.NoveltyTags.AddRange(entrants.Select(e => e.VehicleId));
            ConceptGenerator.Score(concept, entrants, rounds, null);
            script.Concept = concept;
            script.Entrants = entrants;
            script.Rounds = rounds;
            return script;
        }

        /// <summary>Ask Claude; retry once if the design is too close to a previous episode. Returns null on failure.</summary>
        public static async Task<EpisodeScript> WriteWithClaudeAsync(ClaudeClient claude, IList<EpisodeLogEntry> history, IList<NoveltyFingerprint> fingerprints,
            IList<Entrant> currentEntrants, bool letClaudePickVehicles, string difficulty, int seed, Action<string> log)
        {
            string user = BuildUserPrompt(history, currentEntrants, letClaudePickVehicles, difficulty, seed);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string reply = await claude.CompleteAsync(SystemPrompt, user, 4000).ConfigureAwait(false);
                if (reply == null) { log?.Invoke(claude.LastError); return null; }
                var script = Parse(reply, currentEntrants, seed, out string err);
                if (script == null) { log?.Invoke(err); user += "\n\nYour previous reply was rejected: " + err + " Reply again with valid JSON."; continue; }
                script.Source = "claude:" + claude.Model;
                double sim = MaxSimilarity(script, fingerprints, out string similarTo);
                if (sim > 0.85 && attempt == 0)
                {
                    log?.Invoke($"Claude script is {sim:P0} similar to {similarTo} — asking for a different one.");
                    user += $"\n\nYour design was {sim:P0} similar to a previous episode ({similarTo}). Change the series, twist, line-up and rule sequence.";
                    continue;
                }
                return script;
            }
            return null;
        }

        /// <summary>Offline writer: rotates through series × twist × line-ups, least recently used first.</summary>
        public static EpisodeScript WriteOffline(IList<EpisodeLogEntry> history, IList<NoveltyFingerprint> fingerprints, IList<Entrant> currentEntrants,
            bool pickVehicles, string difficulty, int seed)
        {
            var rng = new Random(seed);
            var lineups = VehicleCatalog.Lineups();
            var candidates = new List<(string series, string twist, string[] lineup, int lastUsed)>();
            foreach (var series in ConceptGenerator.SeriesIds)
                foreach (var twist in ConceptGenerator.TwistsFor(series))
                    foreach (var lu in pickVehicles ? lineups : new List<string[]> { currentEntrants.Select(e => e.VehicleId).ToArray() })
                    {
                        if (series == "VehicleVersus" && lu.Length < 2) continue;
                        int last = -1;
                        for (int i = 0; i < history.Count; i++)
                            if (history[i].SeriesId == series && history[i].TwistKind == twist && history[i].Vehicles.OrderBy(x => x).SequenceEqual(lu.OrderBy(x => x))) last = i;
                        candidates.Add((series, twist, lu, last));
                    }
            var pick = candidates.OrderBy(c => c.lastUsed).ThenBy(_ => rng.Next()).First();
            var entrants = pick.lineup.Select((id, i) => new Entrant
            {
                VehicleId = id, Lane = i, ColorHex = currentEntrants.FirstOrDefault(e => e.VehicleId == id)?.ColorHex ?? Palette[(i + seed) % Palette.Length],
                Mode = DriveMode.AI, TargetSpeedKmh = VehicleCatalog.Find(id).DefaultTargetSpeedKmh
            }).ToList();
            var concepts = ConceptGenerator.Generate(pick.series, entrants, seed, ContentLanguage.en, fingerprints, difficulty);
            var concept = concepts.FirstOrDefault(c => c.TwistKind == pick.twist) ?? concepts[0];
            var rounds = RunPlanner.BuildRounds(concept.SeriesId, concept.TwistKind, entrants, difficulty, 600, ContentLanguage.en);
            // small seeded geometry variation so even a repeated combination is not identical
            foreach (var r in rounds)
            {
                r.KickerHeight = (float)Math.Round(Math.Max(2, Math.Min(30, r.KickerHeight * (0.9 + rng.NextDouble() * 0.2))), 1);
                r.KickerAngleDeg = (float)Math.Round(Math.Max(8, Math.Min(40, r.KickerAngleDeg + rng.Next(-2, 3))));
            }
            RunPlanner.RefreshRoundTexts(rounds, entrants, ContentLanguage.en);
            return new EpisodeScript { Concept = concept, Entrants = entrants, Rounds = rounds, Source = "offline" };
        }

        static double MaxSimilarity(EpisodeScript s, IList<NoveltyFingerprint> fps, out string similarTo)
        {
            similarTo = null;
            if (fps == null || fps.Count == 0) return 0;
            var fp = NoveltyFingerprint.FromConcept(s.Concept, s.Entrants, s.Rounds);
            double best = 0;
            foreach (var h in fps) { double v = fp.Similarity(h); if (v > best) { best = v; similarTo = h.EpisodeId; } }
            return best;
        }
    }
}
