using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mylti.Domain
{
    /// <summary>One verifiable fact with the telemetry event ids that support it.</summary>
    public sealed class Fact : IJsonWritable
    {
        public string Key;
        public double? Number;
        public string Text;
        public string Unit;
        public List<int> EventIds = new List<int>();

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("key", Key).Prop("number", Number).Prop("text", Text).Prop("unit", Unit);
            w.Key("evidenceEventIds"); w.Value(EventIds);
            w.EndObject();
        }
    }

    public sealed class VehicleFacts
    {
        public string VehicleId;
        public string Name;
        public string ClassNoun;
        public string ColorName;
        public int RoundWins;
        public int Points;
        public bool WonFinal;
        public int ValidLandings;
        public int CleanLandings;
        public int Rollovers;
        public int FellOff;
        public double? BestDistanceM;
        public double? MaxSpeedKmh;
        public double? PeakHeightM;
    }

    /// <summary>
    /// ContentFactSheet — the ONLY input of the metadata generator (spec §9, §18.3).
    /// null means "not verified": generators must lower specificity instead of guessing.
    /// </summary>
    public sealed class ContentFactSheet : IJsonWritable
    {
        public string RunId;
        public ContentLanguage Language;
        public string SeriesId;
        public string ConceptId;
        public string ViewerQuestion;
        public string WorkingTitle;
        public string TwistKind;
        public string MapName = "Mega Ramp";
        public List<VehicleFacts> Vehicles = new List<VehicleFacts>();
        public int PlannedRoundCount;
        public int CompletedRoundCount;
        public double? MinKickerHeightM, MaxKickerHeightM;
        public string OverallWinnerId;
        public string FinalWinnerId;
        public string ComebackVehicleId;     // trailing after round 3, overall winner at the end
        public bool LeadChanged;
        public int RolloverCount, FellOffCount, ObstacleHitCount, BodySlamCount, WallClearCount;
        public bool HadWallRound, HadTargetRound, HadBridgeRound, HadObstacleRound, HadLandingRampRound;
        public Fact LongestJump, FastestSpeed, HighestPeak, BiggestAirtime;
        public string LongestJumpVehicleId;
        public int LongestJumpRound = -1;
        public double? LongestJumpKickerM;
        public bool CaptureVerified;
        public CaptureBackend CaptureBackend;
        public double? MediaDurationSeconds;
        public bool ChaptersReliable;
        public List<ChapterMarker> Chapters = new List<ChapterMarker>();
        public List<Highlight> TopHighlights = new List<Highlight>();
        public List<RoundResult> Rounds = new List<RoundResult>();
        public List<Fact> Facts = new List<Fact>();
        public List<double> RoundKickerHeights = new List<double>();
        public HashSet<string> AllowedNumbers = new HashSet<string>();

        public VehicleFacts V(string id) => Vehicles.FirstOrDefault(v => v.VehicleId == id);
        public bool AnyRoundWinner => Rounds.Any(r => r.WinnerVehicleId != null);
        public int ValidLandingTotal => Vehicles.Sum(v => v.ValidLandings);

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("runId", RunId).Prop("language", Language.ToString()).Prop("seriesId", SeriesId)
                .Prop("conceptId", ConceptId).Prop("viewerQuestion", ViewerQuestion).Prop("workingTitle", WorkingTitle)
                .Prop("twistKind", TwistKind).Prop("mapName", MapName)
                .Prop("plannedRoundCount", PlannedRoundCount).Prop("completedRoundCount", CompletedRoundCount)
                .Prop("minKickerHeightMeters", MinKickerHeightM).Prop("maxKickerHeightMeters", MaxKickerHeightM)
                .Prop("overallWinnerVehicleId", OverallWinnerId).Prop("finalWinnerVehicleId", FinalWinnerId)
                .Prop("comebackVehicleId", ComebackVehicleId).Prop("leadChanged", LeadChanged)
                .Prop("rolloverCount", RolloverCount).Prop("fellOffCount", FellOffCount)
                .Prop("obstacleHitCount", ObstacleHitCount).Prop("bodySlamCount", BodySlamCount)
                .Prop("wallClearCount", WallClearCount)
                .Prop("captureVerified", CaptureVerified).Prop("captureBackend", CaptureBackend.ToString())
                .Prop("mediaDurationSeconds", MediaDurationSeconds).Prop("chaptersReliable", ChaptersReliable)
                .Prop("simulationNote", "Rigid-body game simulation; outcomes are simulated, not real-world measurements; no body deformation.");
            w.Key("vehicles").BeginArray();
            foreach (var v in Vehicles)
            {
                w.BeginObject().Prop("vehicleId", v.VehicleId).Prop("name", v.Name).Prop("classNoun", v.ClassNoun)
                    .Prop("color", v.ColorName).Prop("roundWins", v.RoundWins).Prop("points", v.Points).Prop("wonFinal", v.WonFinal)
                    .Prop("validLandings", v.ValidLandings).Prop("cleanLandings", v.CleanLandings).Prop("rollovers", v.Rollovers)
                    .Prop("fellOff", v.FellOff).Prop("bestDistanceMeters", v.BestDistanceM).Prop("maxSpeedKmh", v.MaxSpeedKmh)
                    .Prop("peakHeightMeters", v.PeakHeightM).EndObject();
            }
            w.EndArray();
            w.Key("facts"); w.Value(Facts);
            w.Key("rounds"); w.Value(Rounds);
            w.Key("topHighlights"); w.Value(TopHighlights);
            w.Key("chapters").BeginArray();
            foreach (var c in Chapters) w.BeginObject().Prop("time", c.Time).Prop("label", c.Label).EndObject();
            w.EndArray();
            w.EndObject();
        }
    }

    public static class FactSheetBuilder
    {
        static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        public static ContentFactSheet Build(RunRecord run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            var plan = run.Plan;
            var lang = plan?.Language ?? ContentLanguage.en;
            var fs = new ContentFactSheet
            {
                RunId = run.RunId,
                Language = lang,
                SeriesId = plan?.Concept?.SeriesId,
                ConceptId = plan?.Concept?.ConceptId,
                ViewerQuestion = plan?.Concept?.ViewerQuestion,
                WorkingTitle = plan?.Concept?.WorkingTitle,
                TwistKind = plan?.Concept?.TwistKind,
                CaptureVerified = run.Capture.Verified,
                CaptureBackend = run.Capture.Backend,
                MediaDurationSeconds = run.Capture.Verified ? run.Capture.Video?.DurationSeconds : null,
                Rounds = run.Rounds.ToList()
            };
            var entrants = plan?.Entrants ?? new List<Entrant>();
            var scored = run.Attempts.Where(a => a.Scored && !a.IsTeaser && a.Outcome != AttemptOutcome.None && a.Outcome != AttemptOutcome.Aborted).ToList();

            var standings = RoundScoring.ComputeStandings(run.Rounds, run.Attempts, entrants, out string overall);
            fs.OverallWinnerId = overall;
            fs.FinalWinnerId = run.Rounds.FirstOrDefault(r => r.IsFinal)?.WinnerVehicleId;
            fs.PlannedRoundCount = plan?.ScoredRounds.Count() ?? 0;
            fs.CompletedRoundCount = run.Rounds.Count(r => r.Completed);

            var completedSpecs = plan?.ScoredRounds.Where(r => run.Rounds.Any(x => x.RoundIndex == r.Index && x.Completed)).ToList() ?? new List<RoundSpec>();
            if (completedSpecs.Count > 0)
            {
                fs.MinKickerHeightM = Math.Round(completedSpecs.Min(r => r.KickerHeight), 0);
                fs.MaxKickerHeightM = Math.Round(completedSpecs.Max(r => r.KickerHeight), 0);
                fs.HadWallRound = completedSpecs.Any(r => r.Rule == ScoringRule.BeyondTheWall || r.Obstacles == ObstacleSet.CrateWall || r.Obstacles == ObstacleSet.BarrelsAndWall);
                fs.HadTargetRound = completedSpecs.Any(r => r.Rule == ScoringRule.TargetZone);
                fs.HadBridgeRound = completedSpecs.Any(r => r.BridgeWidth > 0);
                fs.HadObstacleRound = completedSpecs.Any(r => r.Obstacles != ObstacleSet.None);
                fs.HadLandingRampRound = completedSpecs.Any(r => r.LandingRamp);
                foreach (var r in completedSpecs)
                {
                    fs.RoundKickerHeights.Add(r.KickerHeight);
                    if (r.Rule == ScoringRule.TargetZone) fs.RoundKickerHeights.Add(r.TargetDistance);
                    if (r.Rule == ScoringRule.BeyondTheWall) fs.RoundKickerHeights.Add(r.WallDistance);
                }
            }

            foreach (var e in entrants)
            {
                var def = e.Def;
                if (def == null) continue;
                var mine = scored.Where(a => a.VehicleId == e.VehicleId).ToList();
                var st = standings.FirstOrDefault(s => s.VehicleId == e.VehicleId);
                var vf = new VehicleFacts
                {
                    VehicleId = e.VehicleId,
                    Name = def.DisplayName(lang),
                    ClassNoun = def.ClassNoun(lang),
                    ColorName = VehicleCatalog.ColorName(e.ColorHex),
                    RoundWins = st?.RoundWins ?? 0,
                    Points = st?.Points ?? 0,
                    WonFinal = st?.WonFinal ?? false,
                    ValidLandings = mine.Count(a => a.ValidLanding),
                    CleanLandings = mine.Count(a => a.Outcome == AttemptOutcome.CleanLanding),
                    Rollovers = mine.Count(a => a.Outcome == AttemptOutcome.Rollover),
                    FellOff = mine.Count(a => a.Outcome == AttemptOutcome.FellOff)
                };
                var valid = mine.Where(a => a.ValidLanding && a.LandingDistanceM.HasValue).ToList();
                if (valid.Count > 0) vf.BestDistanceM = Math.Round(valid.Max(a => a.LandingDistanceM.Value), 1);
                if (mine.Count > 0) vf.MaxSpeedKmh = Math.Round(mine.Max(a => a.MaxSpeedKmh), 0);
                var flown = mine.Where(a => a.TakeoffProgramTime.HasValue).ToList();
                if (flown.Count > 0) vf.PeakHeightM = Math.Round(flown.Max(a => a.PeakHeightM), 1);
                fs.Vehicles.Add(vf);
            }

            fs.RolloverCount = scored.Count(a => a.Outcome == AttemptOutcome.Rollover);
            fs.FellOffCount = scored.Count(a => a.Outcome == AttemptOutcome.FellOff);
            fs.ObstacleHitCount = scored.Sum(a => a.ObstacleHits);
            fs.BodySlamCount = scored.Count(a => a.BodySlam);
            fs.WallClearCount = scored.Count(a => a.ClearedWall > 0.5);

            // Comeback / lead change from the actual leader timeline.
            var timeline = RoundScoring.LeaderTimeline(run.Rounds.Where(r => r.Completed).ToList(), entrants);
            var distinctLeaders = timeline.Where(x => x != null).Distinct().ToList();
            fs.LeadChanged = distinctLeaders.Count > 1;
            if (overall != null && timeline.Count >= 4)
            {
                string leaderAfter3 = timeline[2];
                if (leaderAfter3 != null && leaderAfter3 != overall) fs.ComebackVehicleId = overall;
            }

            // Superlatives with evidence
            var longest = scored.Where(a => a.ValidLanding && a.LandingDistanceM.HasValue).OrderByDescending(a => a.LandingDistanceM.Value).FirstOrDefault();
            if (longest != null)
            {
                fs.LongestJump = MakeFact(run, "longestJump", longest, longest.LandingDistanceM.Value, "m", "Landed");
                fs.LongestJumpVehicleId = longest.VehicleId;
                fs.LongestJumpRound = longest.RoundIndex;
                var ls = plan?.ScoredRounds.FirstOrDefault(r => r.Index == longest.RoundIndex);
                if (ls != null) fs.LongestJumpKickerM = Math.Round(ls.KickerHeight, 0);
            }
            var fastest = scored.OrderByDescending(a => a.MaxSpeedKmh).FirstOrDefault();
            if (fastest != null && fastest.MaxSpeedKmh > 1) fs.FastestSpeed = MakeFact(run, "topSpeed", fastest, Math.Round(fastest.MaxSpeedKmh), "km/h", "Takeoff");
            var highest = scored.Where(a => a.TakeoffProgramTime.HasValue).OrderByDescending(a => a.PeakHeightM).FirstOrDefault();
            if (highest != null) fs.HighestPeak = MakeFact(run, "peakHeight", highest, Math.Round(highest.PeakHeightM, 1), "m", "Apex");
            var air = scored.OrderByDescending(a => a.AirTimeS).FirstOrDefault();
            if (air != null && air.AirTimeS > 0.3) fs.BiggestAirtime = MakeFact(run, "airtime", air, Math.Round(air.AirTimeS, 1), "s", "Landed");
            foreach (var f in new[] { fs.LongestJump, fs.FastestSpeed, fs.HighestPeak, fs.BiggestAirtime }) if (f != null) fs.Facts.Add(f);
            fs.Facts.Add(new Fact { Key = "completedRounds", Number = fs.CompletedRoundCount });
            fs.Facts.Add(new Fact { Key = "rollovers", Number = fs.RolloverCount });
            fs.Facts.Add(new Fact { Key = "overallWinner", Text = fs.OverallWinnerId });

            // Chapters: only when media time == program time is trustworthy.
            fs.ChaptersReliable = run.Capture.Verified && run.Capture.Backend != CaptureBackend.ObsManual && !run.Capture.Interrupted;
            fs.Chapters = BuildChapters(run);

            fs.TopHighlights = run.Highlights.OrderByDescending(h => h.Score).Take(5).ToList();
            BuildAllowedNumbers(fs);
            return fs;
        }

        static Fact MakeFact(RunRecord run, string key, AttemptRecord a, double value, string unit, string preferType)
        {
            var f = new Fact { Key = key, Number = value, Unit = unit, Text = a.VehicleId + "@round" + a.RoundIndex };
            foreach (int id in a.EventIds)
            {
                var e = run.FindEvent(id);
                if (e != null && e.Type == preferType) f.EventIds.Add(id);
            }
            if (f.EventIds.Count == 0) f.EventIds.AddRange(a.EventIds.Take(1));
            return f;
        }

        public static List<ChapterMarker> BuildChapters(RunRecord run)
        {
            var list = new List<ChapterMarker>();
            foreach (var e in run.Events.Where(x => x.Type == "PhaseStarted").OrderBy(x => x.ProgramTime))
            {
                if (list.Count > 0 && e.ProgramTime - list[list.Count - 1].Time < 10) continue; // YouTube: ≥10 s per chapter
                list.Add(new ChapterMarker { Time = list.Count == 0 ? 0 : Math.Floor(e.ProgramTime), Label = e.Note });
            }
            return list.Count >= 3 ? list : new List<ChapterMarker>();
        }

        static void BuildAllowedNumbers(ContentFactSheet fs)
        {
            var nums = new List<double>();
            for (int i = 0; i <= Math.Max(fs.PlannedRoundCount, 6) + 1; i++) nums.Add(i);
            nums.Add(fs.Vehicles.Count);
            if (fs.MinKickerHeightM.HasValue) nums.Add(fs.MinKickerHeightM.Value);
            if (fs.MaxKickerHeightM.HasValue) nums.Add(fs.MaxKickerHeightM.Value);
            foreach (var r in fs.Rounds) foreach (var m in r.BestMetric.Values) nums.Add(m);
            nums.AddRange(fs.RoundKickerHeights);
            foreach (var v in fs.Vehicles)
            {
                nums.Add(v.RoundWins); nums.Add(v.Points); nums.Add(v.ValidLandings); nums.Add(v.Rollovers);
                if (v.BestDistanceM.HasValue) nums.Add(v.BestDistanceM.Value);
                if (v.MaxSpeedKmh.HasValue) nums.Add(v.MaxSpeedKmh.Value);
                if (v.PeakHeightM.HasValue) nums.Add(v.PeakHeightM.Value);
            }
            foreach (var f in fs.Facts) if (f.Number.HasValue) nums.Add(f.Number.Value);
            nums.Add(fs.RolloverCount); nums.Add(fs.ObstacleHitCount); nums.Add(fs.WallClearCount);
            foreach (double n in nums)
            {
                fs.AllowedNumbers.Add(Math.Round(n).ToString(CultureInfo.InvariantCulture));
                fs.AllowedNumbers.Add(Math.Floor(n).ToString(CultureInfo.InvariantCulture));
                fs.AllowedNumbers.Add(F1(n));
            }
        }
    }
}
