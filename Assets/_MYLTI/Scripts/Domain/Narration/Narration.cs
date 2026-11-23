using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    /// <summary>A window of the final video where one commentary line can be spoken.</summary>
    public sealed class NarrationSlot
    {
        public string Id;
        public string Kind;          // hook, title, vehicle, rules, round_intro, approach, result, replay, round_result, standings, outro
        public double Start, End;
        public bool PreOutcome;      // must not reveal any result
        public int RoundIndex;
        public readonly Dictionary<string, string> Facts = new Dictionary<string, string>();
        public readonly List<AttemptRecord> Attempts = new List<AttemptRecord>();
        public readonly HashSet<string> AllowedNumbers = new HashSet<string>();
        public RoundResult Round;

        public double Seconds => Math.Max(0, End - Start);
        public int MaxWords => Math.Max(3, (int)Math.Floor(Seconds * 2.4));
    }

    public sealed class NarrationLine
    {
        public string SlotId;
        public double Start, End;
        public string Text;
        public string Source;        // claude / template
        public string RejectedReason;
        public string AudioFile;
        public double AudioSeconds;
    }

    public sealed class NarrationScript : IJsonWritable
    {
        public List<NarrationLine> Lines = new List<NarrationLine>();
        public string Writer;
        public List<string> Notes = new List<string>();

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("writer", Writer).PropStrings("notes", Notes);
            w.Key("lines").BeginArray();
            foreach (var l in Lines)
                w.BeginObject().Prop("slot", l.SlotId).Prop("start", Math.Round(l.Start, 2)).Prop("end", Math.Round(l.End, 2))
                 .Prop("text", l.Text).Prop("source", l.Source).Prop("rejectedClaudeLine", l.RejectedReason)
                 .Prop("audioFile", l.AudioFile).Prop("audioSeconds", Math.Round(l.AudioSeconds, 2)).EndObject();
            w.EndArray().EndObject();
        }

        /// <summary>SubRip captions for YouTube (uploaded as a subtitle track, not burned into the picture).</summary>
        public string ToSrt()
        {
            var sb = new StringBuilder();
            int k = 1;
            foreach (var l in Lines.Where(x => !string.IsNullOrWhiteSpace(x.Text)).OrderBy(x => x.Start))
            {
                double end = Math.Min(l.End, l.Start + Math.Max(1.2, l.AudioSeconds > 0 ? l.AudioSeconds + 0.3 : l.End - l.Start));
                sb.Append(k++).Append('\n').Append(Tc(l.Start)).Append(" --> ").Append(Tc(end)).Append('\n').Append(l.Text).Append("\n\n");
            }
            return sb.ToString();
        }

        static string Tc(double t)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, t));
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
        }
    }

    /// <summary>Turns the recorded run (segments + events + attempts) into commentary windows on the program timeline.</summary>
    public static class NarrationSlotBuilder
    {
        static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        static string F0(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

        public static List<NarrationSlot> Build(RunRecord run)
        {
            var plan = run.Plan;
            var slots = new List<NarrationSlot>();
            var lang = ContentLanguage.en;
            var specs = plan.Phases.Where(p => p.Round != null && p.Round.Index > 0).Select(p => p.Round).GroupBy(r => r.Index).ToDictionary(g => g.Key, g => g.First());
            var ev = run.Events.OrderBy(e => e.ProgramTime).ToList();
            var names = plan.Entrants.Select(e => e.Def.DisplayNameEn).ToList();
            int totalRounds = plan.ScoredRounds.Count();

            NarrationSlot Add(string kind, double start, double end, bool pre, int round = 0)
            {
                var s = new NarrationSlot { Id = "s" + (slots.Count + 1).ToString("00"), Kind = kind, Start = start, End = end, PreOutcome = pre, RoundIndex = round };
                s.Facts["vehicles"] = string.Join(", ", names);
                s.Facts["map"] = "Mega Ramp";
                slots.Add(s);
                return s;
            }

            // 1. Hook (teaser live take) and title card
            var teaser = run.Segments.FirstOrDefault(x => x.Kind == SegmentKind.Live && x.Label != null && x.Label.StartsWith("Teaser"));
            var teaserSpec = plan.Phases.FirstOrDefault(p => p.Kind == PhaseKind.ColdOpen)?.Round;
            if (teaser != null)
            {
                var s = Add("hook", teaser.Start + 0.4, teaser.End, true);
                if (teaserSpec != null) { s.Facts["kickerHeightM"] = F0(teaserSpec.KickerHeight); }
                s.Facts["note"] = "real teaser attempt on the hardest ramp; the landing is NOT shown — do not reveal anything";
            }
            var title = run.Segments.FirstOrDefault(x => x.Kind == SegmentKind.Card && x.Label == "Title card");
            if (title != null)
            {
                var s = Add("title", title.Start + 0.3, title.End, true);
                s.Facts["viewerQuestion"] = plan.Concept.ViewerQuestion;
                s.Facts["viewerPromise"] = plan.Concept.ViewerPromise;
            }

            // 2. Intro: one slot per vehicle + rules
            var introEvents = ev.Where(e => e.Type == "IntroVehicle" || e.Type == "RulesShown").ToList();
            var introSeg = run.Segments.FirstOrDefault(x => x.Kind == SegmentKind.Intro);
            for (int i = 0; i < introEvents.Count; i++)
            {
                var e = introEvents[i];
                double end = i + 1 < introEvents.Count ? introEvents[i + 1].ProgramTime : (introSeg?.End ?? e.ProgramTime + 8);
                if (e.Type == "IntroVehicle")
                {
                    var d = VehicleCatalog.Find(e.VehicleId);
                    var s = Add("vehicle", e.ProgramTime + 0.3, end, true);
                    s.Facts["vehicle"] = d.DisplayNameEn;
                    s.Facts["massKg"] = F0(d.MassKg);
                    s.Facts["topSpeedKmhInGame"] = F0(d.TopSpeedKmh);
                    s.Facts["drive"] = "all-wheel drive";
                }
                else
                {
                    var s = Add("rules", e.ProgramTime + 0.3, end, true);
                    s.Facts["viewerPromise"] = plan.Concept.ViewerPromise;
                    s.Facts["stakes"] = plan.Concept.Stakes;
                    s.Facts["rounds"] = (totalRounds - 1) + " rounds plus a final worth double points";
                }
            }

            // 3. Rounds
            foreach (var spec in plan.ScoredRounds)
            {
                var roundEvents = ev.Where(e => e.RoundIndex == spec.Index).ToList();
                var startEv = ev.FirstOrDefault(e => e.Type == "PhaseStarted" && e.RoundIndex == spec.Index);
                if (startEv == null) continue;
                double roundEnd = ev.FirstOrDefault(e => e.Type == "PhaseStarted" && e.ProgramTime > startEv.ProgramTime + 0.01)?.ProgramTime ?? double.MaxValue;
                var attempts = run.Attempts.Where(a => a.RoundIndex == spec.Index && !a.IsTeaser && a.Outcome != AttemptOutcome.None).OrderBy(a => a.StartProgramTime).ToList();
                // groups = attempts that started together (head-to-head)
                var groups = attempts.GroupBy(a => Math.Round(a.StartProgramTime, 1)).Select(g => g.ToList()).ToList();
                bool isFinal = spec.Role == RoundRole.Decisive;

                double firstTakeoff = groups.Count > 0 ? (groups[0].Min(a => a.TakeoffProgramTime ?? a.LandingProgramTime ?? a.EndProgramTime)) : startEv.ProgramTime + 8;
                var ri = Add("round_intro", startEv.ProgramTime + 0.3, firstTakeoff - 0.8, true, spec.Index);
                FillRoundFacts(ri, spec, isFinal, totalRounds, lang);
                if (groups.Count > 0) FillApproach(ri, groups[0], spec);

                for (int g = 0; g < groups.Count; g++)
                {
                    var grp = groups[g];
                    double start = grp.Min(a => a.StartProgramTime);
                    double takeoff = grp.Min(a => a.TakeoffProgramTime ?? a.LandingProgramTime ?? a.EndProgramTime);
                    if (g > 0)
                    {
                        var ap = Add("approach", start + 0.3, takeoff - 0.8, true, spec.Index);
                        FillApproach(ap, grp, spec);
                        ap.Facts["attemptNumber"] = (grp[0].AttemptIndex).ToString(CultureInfo.InvariantCulture);
                        ap.AllowedNumbers.Add(grp[0].AttemptIndex.ToString(CultureInfo.InvariantCulture));
                    }
                    double landed = grp.Max(a => a.LandingProgramTime ?? a.EndProgramTime);
                    double resEnd = grp.Max(a => a.EndProgramTime) + 2.6; // mini result card
                    var rs = Add("result", Math.Min(landed + 0.6, resEnd - 1.5), resEnd, false, spec.Index);
                    rs.Attempts.AddRange(grp);
                    foreach (var a in grp)
                    {
                        string n = VehicleCatalog.Find(a.VehicleId).DisplayNameEn;
                        rs.Facts["outcome_" + n] = OutcomeWords(a);
                        if (a.LandingDistanceM.HasValue && a.ValidLanding) { rs.Facts["distanceM_" + n] = F1(a.LandingDistanceM.Value); AddNum(rs, a.LandingDistanceM.Value); }
                        if (a.AirTimeS > 0.3) { rs.Facts["airtimeS_" + n] = F1(a.AirTimeS); AddNum(rs, a.AirTimeS); }
                        if (a.ObstacleHits > 0) rs.Facts["obstaclesHit_" + n] = a.ObstacleHits.ToString(CultureInfo.InvariantCulture);
                        if (a.ClearedWall >= 0) rs.Facts["clearedWall_" + n] = a.ClearedWall > 0.5 ? "yes" : "no";
                        if (a.TargetError.HasValue) { rs.Facts["metersFromTarget_" + n] = F1(a.TargetError.Value); AddNum(rs, a.TargetError.Value); }
                        if (a.DamagePercent.HasValue) { rs.Facts["damagePercent_" + n] = F0(a.DamagePercent.Value); AddNum(rs, a.DamagePercent.Value); }
                        if (a.ImpactSpeedKmh.HasValue) { rs.Facts["wallImpactKmh_" + n] = F0(a.ImpactSpeedKmh.Value); AddNum(rs, a.ImpactSpeedKmh.Value); }
                        if (a.WheelsLost > 0) { rs.Facts["wheelsLost_" + n] = a.WheelsLost.ToString(CultureInfo.InvariantCulture); AddNum(rs, a.WheelsLost); }
                        if (a.GlassBroken) rs.Facts["glass_" + n] = "windows shattered";
                    }
                    if (spec.Rule == ScoringRule.DropTest) { rs.Facts["dropHeightM"] = F0(spec.DropHeightM); AddNum(rs, spec.DropHeightM); }
                    rs.Facts["rule"] = RunPlanner.RuleText(spec.Rule, spec, lang);
                }

                foreach (var rep in run.Segments.Where(x => x.Kind == SegmentKind.Replay && x.Start >= startEv.ProgramTime && x.Start < roundEnd))
                {
                    var shown = ev.FirstOrDefault(e => e.Type == "ReplayShown" && Math.Abs(e.ProgramTime - rep.Start) < 0.5 && e.RoundIndex == spec.Index);
                    if (shown == null) continue;
                    var a = attempts.FirstOrDefault(x => rep.Label != null && rep.Label.EndsWith(x.AttemptId));
                    var s = Add("replay", rep.Start + 0.4, rep.End, false, spec.Index);
                    if (a != null)
                    {
                        s.Attempts.Add(a);
                        string n = VehicleCatalog.Find(a.VehicleId).DisplayNameEn;
                        s.Facts["replayOf"] = n;
                        s.Facts["outcome"] = OutcomeWords(a);
                        if (a.LandingDistanceM.HasValue && a.ValidLanding) { s.Facts["distanceM"] = F1(a.LandingDistanceM.Value); AddNum(s, a.LandingDistanceM.Value); }
                        if (a.PeakHeightM > 0 && a.LandingDistanceM.HasValue) { s.Facts["peakHeightM"] = F1(a.PeakHeightM); AddNum(s, a.PeakHeightM); }
                        if (a.DamagePercent.HasValue) { s.Facts["damagePercent"] = F0(a.DamagePercent.Value); AddNum(s, a.DamagePercent.Value); }
                        if (a.ImpactSpeedKmh.HasValue) { s.Facts["wallImpactKmh"] = F0(a.ImpactSpeedKmh.Value); AddNum(s, a.ImpactSpeedKmh.Value); }
                    }
                    s.Facts["speed"] = "slow motion replay";
                }

                var rr = run.Rounds.FirstOrDefault(r => r.RoundIndex == spec.Index);
                var card = run.Segments.Where(x => x.Kind == SegmentKind.Card && x.Label != null && x.Label.Contains("RESULT") && x.Start >= startEv.ProgramTime && x.Start < roundEnd)
                                       .OrderBy(x => x.Start).FirstOrDefault();
                if (card != null && rr != null)
                {
                    var s = Add("round_result", card.Start + 0.3, card.End, false, spec.Index);
                    s.Round = rr;
                    s.Facts["roundResult"] = Speakable(rr.Explanation);
                    s.Facts["winner"] = rr.WinnerVehicleId != null ? VehicleCatalog.Find(rr.WinnerVehicleId).DisplayNameEn : "none";
                    foreach (var m in rr.BestMetric.Values) AddNum(s, m);
                    var standings = RoundScoring.ComputeStandings(run.Rounds.Where(r => r.RoundIndex <= spec.Index).ToList(), run.Attempts, plan.Entrants, out _);
                    s.Facts["pointsSoFar"] = string.Join(", ", standings.Select(x => $"{VehicleCatalog.Find(x.VehicleId).DisplayNameEn} {x.Points}"));
                    foreach (var x in standings) AddNum(s, x.Points);
                    s.Facts["isFinal"] = isFinal ? "yes" : "no";
                }
            }

            // 4. Summary
            var standingsSeg = run.Segments.FirstOrDefault(x => x.Kind == SegmentKind.Summary && x.Label == "Final standings");
            var all = RoundScoring.ComputeStandings(run.Rounds, run.Attempts, plan.Entrants, out string overall);
            if (standingsSeg != null)
            {
                var s = Add("standings", standingsSeg.Start + 0.3, standingsSeg.End, false);
                s.Facts["overallWinner"] = overall != null ? VehicleCatalog.Find(overall).DisplayNameEn : "none (tie)";
                s.Facts["standings"] = string.Join(", ", all.Select(x => $"{VehicleCatalog.Find(x.VehicleId).DisplayNameEn} {x.Points} points"));
                foreach (var x in all) AddNum(s, x.Points);
            }
            foreach (var rep in run.Segments.Where(x => x.Kind == SegmentKind.Replay && standingsSeg != null && x.Start > standingsSeg.Start))
            {
                var a = run.Attempts.FirstOrDefault(x => rep.Label != null && rep.Label.EndsWith(x.AttemptId));
                var s = Add("replay", rep.Start + 0.4, rep.End, false, a?.RoundIndex ?? 0);
                if (a == null) continue;
                s.Attempts.Add(a);
                s.Facts["replayOf"] = VehicleCatalog.Find(a.VehicleId).DisplayNameEn;
                s.Facts["round"] = a.RoundIndex.ToString(CultureInfo.InvariantCulture);
                s.Facts["outcome"] = OutcomeWords(a);
                if (a.LandingDistanceM.HasValue && a.ValidLanding) { s.Facts["distanceM"] = F1(a.LandingDistanceM.Value); AddNum(s, a.LandingDistanceM.Value); }
                s.Facts["context"] = "best moments of the episode";
            }
            var outro = run.Segments.FirstOrDefault(x => x.Kind == SegmentKind.Summary && x.Label == "Outro");
            if (outro != null)
            {
                var s = Add("outro", outro.Start + 0.3, outro.End - 0.5, false);
                s.Facts["overallWinner"] = overall != null ? VehicleCatalog.Find(overall).DisplayNameEn : "none (tie)";
                s.Facts["viewerQuestion"] = plan.Concept.ViewerQuestion;
                s.Facts["callToAction"] = "ask viewers which vehicle or challenge to test next; subscribe";
            }

            // order, remove overlaps, drop tiny windows, set number whitelists
            slots = slots.OrderBy(s => s.Start).ToList();
            for (int i = 0; i < slots.Count - 1; i++)
                if (slots[i].End > slots[i + 1].Start - 0.25) slots[i].End = slots[i + 1].Start - 0.25;
            slots.RemoveAll(s => s.Seconds < 1.8);
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                s.Id = "s" + (i + 1).ToString("00");
                for (int k = 0; k <= Math.Max(10, totalRounds + 1); k++) s.AllowedNumbers.Add(k.ToString(CultureInfo.InvariantCulture));
                if (s.RoundIndex > 0) s.AllowedNumbers.Add(s.RoundIndex.ToString(CultureInfo.InvariantCulture));
                foreach (var v in s.Facts.Values)
                    foreach (Match m in Regex.Matches(v, @"\d+(\.\d+)?")) AddNumStr(s, m.Value);
            }
            return slots;
        }

        static void FillRoundFacts(NarrationSlot s, RoundSpec spec, bool isFinal, int totalRounds, ContentLanguage lang)
        {
            s.Facts["round"] = isFinal ? "Final" : $"Round {spec.Index} of {totalRounds - 1}";
            s.Facts["kickerHeightM"] = F0(spec.KickerHeight);
            s.Facts["kickerAngleDeg"] = F0(spec.KickerAngleDeg);
            s.Facts["rule"] = RunPlanner.RuleText(spec.Rule, spec, lang);
            s.Facts["whatChanged"] = SpeakableChange(spec);
            s.Facts["subQuestion"] = Speakable(spec.SubQuestion);
            s.Facts["format"] = spec.HeadToHead ? "all vehicles at the same time" : "one vehicle at a time";
            if (spec.Rule == ScoringRule.CrashWall) { s.Facts["crashSpeedKmh"] = F0(spec.CrashSpeedKmh); s.Facts["test"] = "drive straight into a concrete wall"; }
            if (spec.Rule == ScoringRule.DropTest) { s.Facts["dropHeightM"] = F0(spec.DropHeightM); s.Facts["test"] = "dropped from a crane"; }
            if (isFinal) s.Facts["stakes"] = "the final counts double";
        }

        static void FillApproach(NarrationSlot s, List<AttemptRecord> grp, RoundSpec spec)
        {
            s.Facts["upNow"] = string.Join(" and ", grp.Select(a => VehicleCatalog.Find(a.VehicleId).DisplayNameEn));
            foreach (var a in grp)
            {
                var d = VehicleCatalog.Find(a.VehicleId);
                s.Facts["targetSpeedKmh_" + d.DisplayNameEn] = F0(Math.Min(d.TopSpeedKmh, d.DefaultTargetSpeedKmh * spec.SpeedFactor));
            }
        }

        public static string SpeakableChange(RoundSpec r)
        {
            if (r.Rule == ScoringRule.CrashWall) return $"crash test: straight into a concrete wall at {F0(r.CrashSpeedKmh)} kilometers per hour, least damage wins";
            if (r.Rule == ScoringRule.DropTest) return $"drop test: every vehicle falls from a crane {F0(r.DropHeightM)} meters up, least damage wins";
            var parts = new List<string> { $"the kicker is {F0(r.KickerHeight)} meters high at {F0(r.KickerAngleDeg)} degrees" };
            if (r.Rule == ScoringRule.LeastDamage) parts.Add("this time the landing with the least damage wins");
            if (r.Obstacles == ObstacleSet.BarrelLine) parts.Add("a line of barrels waits in the landing zone");
            if (r.Obstacles == ObstacleSet.CrateWall) parts.Add("a wall of crates stands in the landing zone");
            if (r.Obstacles == ObstacleSet.BarrelsAndWall) parts.Add("barrels and a crate wall fill the landing zone");
            if (r.BridgeWidth > 0) parts.Add($"a {F1(r.BridgeWidth)} meter wide bridge comes before the ramp");
            if (r.LandingRamp) parts.Add("there is a landing ramp");
            if (Math.Abs(r.SpeedFactor - 1) > 0.04) parts.Add(r.SpeedFactor > 1 ? "speeds are turned up" : "speeds are turned down");
            return string.Join("; ", parts);
        }

        public static string OutcomeWords(AttemptRecord a)
        {
            switch (a.Outcome)
            {
                case AttemptOutcome.CleanLanding: return "clean landing, stayed upright";
                case AttemptOutcome.RoughLanding: return "hard landing but stayed upright";
                case AttemptOutcome.Rollover: return "rolled over after landing";
                case AttemptOutcome.FellOff: return "fell off the course";
                case AttemptOutcome.Crashed: return a.WheelsLost > 0 ? "hit the concrete wall and lost a wheel" : "hit the concrete wall and stayed on its wheels";
                case AttemptOutcome.NoJump: return "did not reach the lip";
                case AttemptOutcome.Stuck: return "got stuck, attempt failed";
                case AttemptOutcome.Timeout: return "timed out";
                default: return "attempt stopped";
            }
        }

        public static string Speakable(string s) => s == null ? "" :
            Regex.Replace(Regex.Replace(s, @"(\d+(\.\d+)?) m landing", "$1-meter landing"), @"(\d+(\.\d+)?) m\b", "$1 meters");

        static void AddNum(NarrationSlot s, double v)
        {
            s.AllowedNumbers.Add(Math.Round(v).ToString(CultureInfo.InvariantCulture));
            s.AllowedNumbers.Add(Math.Floor(v).ToString(CultureInfo.InvariantCulture));
            s.AllowedNumbers.Add(F1(v));
        }

        static void AddNumStr(NarrationSlot s, string v)
        {
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) AddNum(s, d);
        }
    }

    /// <summary>Checks one spoken line against its slot: no spoilers before outcomes, no invented numbers or events.</summary>
    public static class SlotGuard
    {
        // past tense always reports a result; present tense only when tied to a named vehicle ("the Truck wins")
        static readonly string[] PastOutcomeWords = { "won", "lost", "landed", "crashed", "rolled", "flipped", "fell", "survived", "failed", "beat" };
        static readonly string[] PresentOutcomeWords = { "wins", "winner", "beats", "champion", "victory", "loses", "lands", "crash", "crashes", "rolls", "rollover", "flips", "falls", "survives", "fails" };
        static readonly string[] RollWords = { "rolled", "rolls", "rollover", "flipped", "flips", "crash", "crashed", "crashes", "wreck" };
        static readonly string[] FallWords = { "fell", "falls", "plunge", "plunges" };
        static readonly string[] RollOnlyWords = { "rolled", "rolls", "rollover", "flipped", "flips" };
        static readonly string[] WinWords = { "wins", "won", "winner", "champion", "victory", "takes the round", "takes it" };

        public static string Check(string text, NarrationSlot slot, ContentFactSheet fs)
        {
            if (string.IsNullOrWhiteSpace(text)) return "empty";
            string t = text.ToLowerInvariant();
            int words = text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (words > slot.MaxWords * 1.2 + 1) return $"too long ({words} words > {slot.MaxWords})";
            string blocked = ClaimChecker.FindBlockedBrand(t);
            if (blocked != null) return "brand: " + blocked;
            if (t.Contains("real-world") || t.Contains("realistic damage") || t.Contains("scientific")) return "realism claim";

            var sentences = Regex.Split(text, @"(?<=[.!?])\s+").Where(x => x.Trim().Length > 0).ToList();
            if (slot.PreOutcome)
            {
                foreach (var sen in sentences)
                {
                    bool q = sen.TrimEnd().EndsWith("?");
                    string sl = sen.ToLowerInvariant();
                    if (q) continue;
                    bool namesVehicle = VehicleCatalog.All.Any(v => sl.Contains(v.DisplayNameEn.ToLowerInvariant()));
                    foreach (var w in PastOutcomeWords)
                        if (HasWord(sl, w)) return "spoiler word '" + w + "' before the outcome";
                    if (namesVehicle)
                        foreach (var w in PresentOutcomeWords)
                            if (HasWord(sl, w)) return "spoiler word '" + w + "' before the outcome";
                }
            }
            else
            {
                bool question = sentences.Count > 0 && sentences.All(x => x.TrimEnd().EndsWith("?"));
                bool rolled = slot.Attempts.Any(a => a.Outcome == AttemptOutcome.Rollover);
                bool fell = slot.Attempts.Any(a => a.Outcome == AttemptOutcome.FellOff);
                bool crashTest = slot.Attempts.Any(a => a.ImpactSpeedKmh.HasValue);
                bool summary = slot.Kind == "standings" || slot.Kind == "outro";
                if (!rolled && !summary)
                {
                    if (RollOnlyWords.Any(w => HasWord(t, w))) return "rollover not in this slot";
                    if (!crashTest && RollWords.Any(w => HasWord(t, w))) return "rollover/crash not in this slot";
                }
                if (!fell && FallWords.Any(w => HasWord(t, w))) return "fall not in this slot";
                bool winAllowed = (slot.Kind == "round_result" && slot.Round?.WinnerVehicleId != null)
                                  || ((slot.Kind == "standings" || slot.Kind == "outro") && fs?.OverallWinnerId != null);
                if (!winAllowed && !question && WinWords.Any(w => HasWord(t, w))) return "win claim not supported in this slot";
                if (winAllowed)
                {
                    string winnerId = slot.Kind == "round_result" ? slot.Round.WinnerVehicleId : fs.OverallWinnerId;
                    foreach (var v in VehicleCatalog.All)
                    {
                        if (v.Id == winnerId) continue;
                        if (Regex.IsMatch(t, Regex.Escape(v.DisplayNameEn.ToLowerInvariant()) + @"\s+(wins|won|takes)")) return v.DisplayNameEn + " did not win";
                    }
                }
            }
            foreach (var num in ClaimChecker.ExtractNumbers(text))
                if (!slot.AllowedNumbers.Contains(num)) return "number '" + num + "' not in this slot's facts";
            return null;
        }

        static bool HasWord(string lower, string w)
        {
            int i = 0;
            while ((i = lower.IndexOf(w, i, StringComparison.Ordinal)) >= 0)
            {
                bool a = i == 0 || !char.IsLetter(lower[i - 1]);
                int e = i + w.Length;
                bool b = e >= lower.Length || !char.IsLetter(lower[e]);
                if (a && b) return true;
                i = e;
            }
            return false;
        }
    }

    /// <summary>Writes the commentary: Claude (if available) with per-line validation, offline templates otherwise.</summary>
    public static class NarrationWriter
    {
        public const string SystemPrompt =
@"You are the voice-over commentator for a YouTube 3D vehicle challenge simulation. Energetic, warm, concise American English, like a sports host.
Hard rules:
- Use ONLY the facts given for each slot. Never invent numbers, events, speeds, damage or winners.
- Slots with preOutcome=true happen BEFORE the result: build suspense, never reveal or hint who wins, lands, crashes or fails.
- Stay within maxWords for every slot (spoken at about 2.4 words per second). Shorter is fine.
- It is a game simulation with generic vehicles: no brands, no 'real-world physics', no 'realistic damage'.
- Vary your phrasing across the episode; do not repeat the same exclamation or sentence opener.
- Say numbers as digits (e.g. 41.2 meters). Use 'meters' and 'kilometers per hour'.
Reply with JSON only: {""lines"":[{""id"":""s01"",""text"":""...""}, ...]} with exactly one line per slot id.";

        public static string BuildUserPrompt(RunRecord run, IList<NarrationSlot> slots)
        {
            var w = new JsonWriter(true);
            w.BeginObject();
            w.Prop("episodeQuestion", run.Plan.Concept.ViewerQuestion).Prop("episodePromise", run.Plan.Concept.ViewerPromise)
             .Prop("vehicles", string.Join(", ", run.Plan.Entrants.Select(e => e.Def.DisplayNameEn)));
            w.Key("slots").BeginArray();
            foreach (var s in slots)
            {
                w.BeginObject().Prop("id", s.Id).Prop("kind", s.Kind).Prop("seconds", Math.Round(s.Seconds, 1)).Prop("maxWords", s.MaxWords).Prop("preOutcome", s.PreOutcome);
                w.Key("facts"); w.Value(s.Facts.ToDictionary(k => k.Key, k => (object)k.Value));
                w.EndObject();
            }
            w.EndArray().EndObject();
            return "Write one commentary line per slot for this episode:\n" + w;
        }

        public static async Task<NarrationScript> WriteAsync(RunRecord run, IList<NarrationSlot> slots, ContentFactSheet fs, ClaudeClient claude, int seed, Action<string> log)
        {
            var script = new NarrationScript { Writer = "template" };
            Dictionary<string, string> claudeLines = null;
            if (claude != null && claude.HasKey)
            {
                string reply = await claude.CompleteAsync(SystemPrompt, BuildUserPrompt(run, slots), 8000).ConfigureAwait(false);
                if (reply == null) log?.Invoke("Claude commentary failed: " + claude.LastError + " → offline templates.");
                else
                {
                    try
                    {
                        var o = MiniJson.Obj(MiniJson.Parse(ClaudeClient.ExtractJson(reply) ?? "{}"));
                        claudeLines = new Dictionary<string, string>();
                        foreach (var item in MiniJson.Arr(MiniJson.Get(o, "lines")) ?? new List<object>())
                        {
                            var d = MiniJson.Obj(item);
                            string id = MiniJson.GetString(d, "id"), text = MiniJson.GetString(d, "text");
                            if (id != null && text != null) claudeLines[id] = text.Trim();
                        }
                        script.Writer = "claude:" + claude.Model;
                    }
                    catch (Exception e) { log?.Invoke("Claude commentary JSON invalid: " + e.Message + " → offline templates."); }
                }
            }
            var rng = new Random(seed);
            foreach (var s in slots)
            {
                var line = new NarrationLine { SlotId = s.Id, Start = s.Start, End = s.End };
                if (claudeLines != null && claudeLines.TryGetValue(s.Id, out string c))
                {
                    string why = SlotGuard.Check(c, s, fs);
                    if (why == null) { line.Text = c; line.Source = "claude"; }
                    else { line.RejectedReason = why + " :: " + c; script.Notes.Add($"{s.Id} Claude line rejected ({why})"); }
                }
                if (line.Text == null)
                {
                    line.Text = Template(s, fs, rng);
                    line.Source = "template";
                    string why = SlotGuard.Check(line.Text, s, fs);
                    if (why != null) { script.Notes.Add($"{s.Id} template trimmed ({why})"); line.Text = SafeMinimal(s); }
                }
                script.Lines.Add(line);
            }
            return script;
        }

        static string Pick(Random r, params string[] o) => o[r.Next(o.Length)];

        /// <summary>Offline commentary templates (English). Every value comes from slot facts.</summary>
        public static string Template(NarrationSlot s, ContentFactSheet fs, Random r)
        {
            string F(string k) => s.Facts.TryGetValue(k, out var v) ? v : "";
            switch (s.Kind)
            {
                case "hook":
                    return Pick(r, $"A {F("kickerHeightM")} meter ramp. Full speed. Watch this.",
                                   "This is the biggest jump of the day. Keep watching.",
                                   $"{F("vehicles")}. One giant ramp. Here we go.");
                case "title":
                    return F("viewerQuestion") + " " + Pick(r, "Let's find out.", "Let's see.", "Stick around to find out.");
                case "vehicle":
                    return Pick(r, $"The {F("vehicle")}: {F("massKg")} kilograms, top speed {F("topSpeedKmhInGame")} kilometers per hour in the game.",
                                   $"Next, the {F("vehicle")}. {F("massKg")} kilograms with all-wheel drive.",
                                   $"Meet the {F("vehicle")}, weighing {F("massKg")} kilograms.");
                case "rules":
                    return $"{F("viewerPromise")} {F("stakes")}";
                case "round_intro":
                    return Fit(s,
                        $"{F("round")}. {Cap(F("whatChanged"))}. {F("subQuestion")}",
                        s.Facts.ContainsKey("test") ? $"{F("round")}. {F("subQuestion")}" : $"{F("round")}. {F("kickerHeightM")} meters at {F("kickerAngleDeg")} degrees. {F("subQuestion")}",
                        s.Facts.ContainsKey("test") ? $"{F("round")}." : $"{F("round")}. {F("kickerHeightM")} meters at {F("kickerAngleDeg")} degrees.",
                        $"{F("round")}.");
                case "approach":
                    return Pick(r, $"Up next, the {F("upNow")}.", $"Here comes the {F("upNow")}.", $"The {F("upNow")} is on the runway.");
                case "result":
                {
                    if (s.Attempts.Count > 1)
                    {
                        var shortParts = s.Attempts.Select(a =>
                        {
                            string n = VehicleCatalog.Find(a.VehicleId).DisplayNameEn;
                            string d = s.Facts.TryGetValue("distanceM_" + n, out var dv) ? dv : null;
                            string dmg = s.Facts.TryGetValue("damagePercent_" + n, out var dd) ? dd : null;
                            if (dmg != null && (a.ImpactSpeedKmh.HasValue || d == null)) return $"{n}, {dmg} percent damage.";
                            switch (a.Outcome)
                            {
                                case AttemptOutcome.CleanLanding:
                                case AttemptOutcome.RoughLanding: return $"{n}, {d} meters.";
                                case AttemptOutcome.Rollover: return $"{n} rolls!";
                                case AttemptOutcome.FellOff: return $"{n} falls off!";
                                default: return $"{n}, no jump.";
                            }
                        }).ToList();
                        return Fit(s, string.Join(" ", shortParts), string.Join(" ", shortParts.Take(2)), shortParts[0]);
                    }
                    var parts = new List<string>();
                    foreach (var a in s.Attempts)
                    {
                        string n = VehicleCatalog.Find(a.VehicleId).DisplayNameEn;
                        string d = s.Facts.TryGetValue("distanceM_" + n, out var dv) ? dv : null;
                        string dmg = s.Facts.TryGetValue("damagePercent_" + n, out var dd) ? dd : null;
                        string extra = (a.WheelsLost > 0 ? Pick(r, " A wheel comes off!", " There goes a wheel!") : "") + (a.GlassBroken ? Pick(r, " Glass everywhere!", " The windows are gone!") : "");
                        if (a.ImpactSpeedKmh.HasValue && dmg != null)
                        {
                            parts.Add(Pick(r, $"Huge hit for the {n}! {dmg} percent damage.", $"The {n} slams into the concrete. {dmg} percent damage.", $"What an impact from the {n}: {dmg} percent damage.") + extra);
                            continue;
                        }
                        if (dmg != null && d == null)
                        {
                            parts.Add((a.Outcome == AttemptOutcome.Rollover ? Pick(r, $"The {n} lands on its roof!", $"The {n} comes down upside down!") : Pick(r, $"The {n} hits the ground hard!", $"Down comes the {n}!"))
                                      + $" {dmg} percent damage." + extra);
                            continue;
                        }
                        if (dmg != null && a.ValidLanding)
                        {
                            parts.Add(Pick(r, $"The {n} lands at {d} meters with {dmg} percent damage.", $"{d} meters for the {n}, and {dmg} percent damage.") + extra);
                            continue;
                        }
                        switch (a.Outcome)
                        {
                            case AttemptOutcome.CleanLanding: parts.Add(Pick(r, $"Clean landing for the {n}, {d} meters!", $"The {n} sticks it at {d} meters!", $"Beautiful from the {n}: {d} meters.")); break;
                            case AttemptOutcome.RoughLanding: parts.Add(Pick(r, $"A hard landing, but the {n} stays upright at {d} meters.", $"Rough one for the {n}, still {d} meters.")); break;
                            case AttemptOutcome.Rollover: parts.Add(Pick(r, $"Oh no, the {n} rolls over!", $"The {n} can't hold it and rolls!")); break;
                            case AttemptOutcome.FellOff: parts.Add($"The {n} falls off the course!"); break;
                            case AttemptOutcome.NoJump: parts.Add($"The {n} never makes it over the lip."); break;
                            default: parts.Add($"That attempt doesn't count for the {n}."); break;
                        }
                    }
                    return string.Join(" ", parts);
                }
                case "replay":
                    return s.Facts.ContainsKey("distanceM")
                        ? Pick(r, $"Let's see that again. The {F("replayOf")}, {F("distanceM")} meters.", $"One more look at the {F("replayOf")} in slow motion.")
                        : Pick(r, $"Watch the {F("replayOf")} again in slow motion.", "Here it is again, slowed down.");
                case "round_result":
                {
                    string first = Regex.Split(F("roundResult"), @"(?<=\.)\s+")[0];
                    return Fit(s, F("roundResult") + (F("isFinal") == "yes" ? "" : " Points: " + F("pointsSoFar") + "."),
                               F("roundResult"), first,
                               F("winner") == "none" ? "No winner this round." : $"{F("winner")} takes the round.");
                }
                case "standings":
                    return F("overallWinner").StartsWith("none") ? "It ends level. No overall winner today." : $"And the overall winner is the {F("overallWinner")}! {F("standings")}.";
                case "outro":
                    return Pick(r, "Which vehicle should we test next? Tell us in the comments, and subscribe for the next challenge.",
                                   "That's the episode. Drop your next challenge idea in the comments and subscribe!");
                default:
                    return "";
            }
        }

        /// <summary>First candidate that fits the slot's word budget (last one as a fallback).</summary>
        static string Fit(NarrationSlot s, params string[] candidates)
        {
            foreach (var c in candidates)
                if (c.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length <= s.MaxWords) return c;
            return candidates[candidates.Length - 1];
        }

        static string SafeMinimal(NarrationSlot s)
        {
            switch (s.Kind)
            {
                case "outro": return "Thanks for watching. See you in the next challenge!";
                case "replay": return "Let's see that again.";
                case "result": return "Let's see how that went.";
                default: return "Here we go.";
            }
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
