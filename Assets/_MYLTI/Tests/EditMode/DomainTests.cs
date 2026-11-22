using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mylti.Domain;
using NUnit.Framework;

namespace Mylti.Tests
{
    /// <summary>Builds synthetic but internally consistent runs for testing the content pipeline.</summary>
    public static class TestRuns
    {
        public static List<Entrant> TwoVehicles() => new List<Entrant>
        {
            new Entrant { VehicleId = "sport_01", ColorHex = "#D12B38", Lane = 0, TargetSpeedKmh = 150 },
            new Entrant { VehicleId = "truck_01", ColorHex = "#2C82D3", Lane = 1, TargetSpeedKmh = 105 }
        };

        public static RunPlan Plan(string series = "VehicleVersus", int conceptIndex = 0, List<Entrant> entrants = null, int seed = 42)
        {
            entrants = entrants ?? TwoVehicles();
            var concepts = ConceptGenerator.Generate(series, entrants, seed, ContentLanguage.en, null);
            return RunPlanner.CreatePlan(concepts[conceptIndex], entrants, seed, 600, ContentLanguage.en, "Progressive", out _);
        }

        /// <param name="outcome">(roundIndex, vehicleId) → (outcome, distance)</param>
        public static RunRecord Run(RunPlan plan, Func<int, string, (AttemptOutcome, double)> outcome, bool verified = true, double deadAir = 0)
        {
            var run = new RunRecord { RunId = "test_run", StartedAtIso = "2026-11-23T10:00:00", Plan = plan, ChallengeFinished = true, ProgramDurationSeconds = 600 };
            int id = 1;
            RunEvent Ev(string type, double t, int round, string v, string note = null, Dictionary<string, double> values = null)
            {
                var e = new RunEvent { Id = id++, Type = type, ProgramTime = t, RoundIndex = round, VehicleId = v, Note = note, Values = values ?? new Dictionary<string, double>() };
                run.Events.Add(e);
                return e;
            }
            foreach (var ph in plan.Phases)
            {
                Ev("PhaseStarted", ph.PlannedStart, ph.Round?.Index ?? 0, null, ph.Kind == PhaseKind.Round || ph.Kind == PhaseKind.Final ? ph.Round.Title : ph.Label);
                run.Segments.Add(new SegmentMarker { Kind = ph.Kind == PhaseKind.Summary ? SegmentKind.Summary : SegmentKind.Live, Start = ph.PlannedStart, End = ph.PlannedEnd, Camera = "SideTracking" });
            }
            // teaser takeoff (hook)
            Ev("Takeoff", 3.0, 0, "sport_01");
            foreach (var ph in plan.Phases.Where(p => p.Kind == PhaseKind.Round || p.Kind == PhaseKind.Final))
            {
                double t = ph.PlannedStart + 6;
                foreach (var e in plan.Entrants)
                {
                    var (oc, dist) = outcome(ph.Round.Index, e.VehicleId);
                    var a = new AttemptRecord
                    {
                        AttemptId = $"r{ph.Round.Index}_{e.VehicleId}", RoundIndex = ph.Round.Index, AttemptIndex = 1, VehicleId = e.VehicleId,
                        Outcome = oc, StartProgramTime = t, TakeoffProgramTime = t + 8, LandingProgramTime = t + 11, EndProgramTime = t + 14,
                        MaxSpeedKmh = e.VehicleId == "sport_01" ? 141.6 : 98.2, TakeoffSpeedKmh = 120, PeakHeightM = ph.Round.KickerHeight + 6,
                        AirTimeS = 3.1, LandingDistanceM = oc == AttemptOutcome.NoJump || oc == AttemptOutcome.FellOff ? (double?)null : dist
                    };
                    if (ph.Round.Rule == ScoringRule.TargetZone && a.LandingDistanceM.HasValue) a.TargetError = Math.Abs(a.LandingDistanceM.Value - ph.Round.TargetDistance);
                    if (ph.Round.Rule == ScoringRule.BeyondTheWall && a.LandingDistanceM.HasValue) a.ClearedWall = a.LandingDistanceM.Value > ph.Round.WallDistance ? 1 : 0;
                    a.EventIds.Add(Ev("Takeoff", t + 8, ph.Round.Index, e.VehicleId).Id);
                    a.EventIds.Add(Ev("Apex", t + 9.5, ph.Round.Index, e.VehicleId).Id);
                    if (a.LandingDistanceM.HasValue) a.EventIds.Add(Ev("Landed", t + 11, ph.Round.Index, e.VehicleId).Id);
                    if (oc == AttemptOutcome.Rollover) a.EventIds.Add(Ev("Rollover", t + 12, ph.Round.Index, e.VehicleId).Id);
                    run.Attempts.Add(a);
                    t += 20;
                }
                var attempts = run.Attempts.Where(x => x.RoundIndex == ph.Round.Index).ToList();
                run.Rounds.Add(RoundScoring.Evaluate(ph.Round, attempts, plan.Entrants, plan.Language, ph.Kind == PhaseKind.Final));
            }
            if (deadAir > 0) run.Segments.Add(new SegmentMarker { Kind = SegmentKind.DeadAir, Start = 100, End = 100 + deadAir });
            run.Capture.Backend = CaptureBackend.UnityRecorder;
            if (verified)
            {
                run.Capture.Status = CaptureStatus.CaptureVerified;
                run.Capture.Video = new VideoInfo { Path = "C:/x.mp4", Exists = true, DurationSeconds = 600.0, HasVideoTrack = true, HasAudioTrack = true };
            }
            else run.Capture.Status = CaptureStatus.CapturePending;
            run.Highlights = HighlightScorer.Extract(run);
            return run;
        }

        public static RunRecord SportWins(RunPlan plan, bool verified = true) =>
            Run(plan, (r, v) => v == "sport_01" ? (AttemptOutcome.CleanLanding, 40 + r * 4.3) : (r == 4 ? AttemptOutcome.Rollover : AttemptOutcome.RoughLanding, 30 + r * 3.1), verified);

        /// <summary>Everyone lands the same distance (tie every round) and nobody crashes.</summary>
        public static RunRecord AllTies(RunPlan plan) => Run(plan, (r, v) => (AttemptOutcome.CleanLanding, 50.0));

        public static byte[] Box(string type, params byte[][] children)
        {
            int len = 8 + children.Sum(c => c.Length);
            var b = new List<byte> { (byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len };
            b.AddRange(Encoding.ASCII.GetBytes(type));
            foreach (var c in children) b.AddRange(c);
            return b.ToArray();
        }

        static byte[] U32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

        public static byte[] Mp4(uint timescale, uint duration, bool audio)
        {
            var mvhd = Box("mvhd", new byte[] { 0, 0, 0, 0 }, U32(0), U32(0), U32(timescale), U32(duration), new byte[80]);
            byte[] Trak(string handler, uint samples) => Box("trak",
                Box("mdia",
                    Box("mdhd", new byte[] { 0, 0, 0, 0 }, U32(0), U32(0), U32(timescale), U32(duration), new byte[4]),
                    Box("hdlr", new byte[] { 0, 0, 0, 0 }, U32(0), Encoding.ASCII.GetBytes(handler), new byte[13]),
                    Box("minf", Box("stbl", Box("stts", new byte[] { 0, 0, 0, 0 }, U32(1), U32(samples), U32(timescale / 30))))));
            var parts = new List<byte[]> { mvhd, Trak("vide", duration / (timescale / 30)) };
            if (audio) parts.Add(Trak("soun", 100));
            var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom"), U32(512), Encoding.ASCII.GetBytes("isomiso2avc1mp41"));
            var mdat = Box("mdat", new byte[64]);
            var moov = Box("moov", parts.ToArray());
            return ftyp.Concat(mdat).Concat(moov).ToArray();
        }

        public static byte[] Mkv(double durationMs)
        {
            var d = BitConverter.GetBytes(durationMs);
            if (BitConverter.IsLittleEndian) Array.Reverse(d);
            byte[] El(byte[] id, byte[] data) => id.Concat(new[] { (byte)(0x80 | data.Length) }).Concat(data).ToArray();
            var info = El(new byte[] { 0x15, 0x49, 0xA9, 0x66 },
                El(new byte[] { 0x2A, 0xD7, 0xB1 }, new byte[] { 0x0F, 0x42, 0x40 })       // TimecodeScale 1,000,000
                .Concat(El(new byte[] { 0x44, 0x89 }, d)).ToArray());                        // Duration (float64)
            var tracks = El(new byte[] { 0x16, 0x54, 0xAE, 0x6B },
                El(new byte[] { 0xAE }, El(new byte[] { 0x83 }, new byte[] { 1 }))
                .Concat(El(new byte[] { 0xAE }, El(new byte[] { 0x83 }, new byte[] { 2 }))).ToArray());
            var segBody = info.Concat(tracks).ToArray();
            var header = El(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, El(new byte[] { 0x42, 0x82 }, Encoding.ASCII.GetBytes("matroska")));
            // Segment id + 8-byte size vint (0x01 marker followed by 7 big-endian bytes)
            var seg = new byte[] { 0x18, 0x53, 0x80, 0x67, 0x01, 0, 0, 0, 0, 0, (byte)(segBody.Length >> 8), (byte)segBody.Length };
            return header.Concat(seg).Concat(segBody).ToArray();
        }
    }

    [TestFixture]
    public class PlannerTests
    {
        [Test]
        public void TenMinutePlan_SumsTo600_WithFiveRoundsFinalAndSummary()
        {
            var plan = TestRuns.Plan();
            Assert.AreEqual(600.0, plan.Phases.Sum(p => p.PlannedDuration), 0.01);
            Assert.AreEqual(PhaseKind.ColdOpen, plan.Phases[0].Kind);
            Assert.AreEqual(5, plan.Phases.Count(p => p.Kind == PhaseKind.Round));
            Assert.AreEqual(1, plan.Phases.Count(p => p.Kind == PhaseKind.Final));
            Assert.AreEqual(PhaseKind.Summary, plan.Phases.Last().Kind);
        }

        [Test]
        public void EveryRoundChangesAMeaningfulVariable_AndHeightEscalates()
        {
            foreach (var series in ConceptGenerator.SeriesIds)
            {
                var plan = TestRuns.Plan(series);
                var rounds = plan.ScoredRounds.ToList();
                foreach (var r in rounds.Skip(1)) Assert.IsFalse(r.ChangeSummary.Contains("no meaningful change"), series + " round " + r.Index);
                Assert.IsTrue(rounds[4].KickerHeight > rounds[0].KickerHeight, series);
            }
        }

        [Test]
        public void ThreeConceptsAreDifferent()
        {
            var c = ConceptGenerator.Generate("VehicleVersus", TestRuns.TwoVehicles(), 1, ContentLanguage.en, null);
            Assert.AreEqual(3, c.Count);
            Assert.AreEqual(3, c.Select(x => x.TwistKind).Distinct().Count());
            foreach (var k in c) Assert.AreEqual(6, k.Scores.Count);
        }
    }

    [TestFixture]
    public class ScoringTests
    {
        [Test]
        public void CleanLandingOnly_IgnoresRoughLandings_AndTieGivesNoWinner()
        {
            var spec = new RoundSpec { Index = 1, Rule = ScoringRule.CleanLandingOnly };
            var ents = TestRuns.TwoVehicles();
            var atts = new List<AttemptRecord>
            {
                new AttemptRecord { AttemptId = "a", RoundIndex = 1, VehicleId = "sport_01", Outcome = AttemptOutcome.RoughLanding, LandingDistanceM = 90 },
                new AttemptRecord { AttemptId = "b", RoundIndex = 1, VehicleId = "truck_01", Outcome = AttemptOutcome.CleanLanding, LandingDistanceM = 40 }
            };
            Assert.AreEqual("truck_01", RoundScoring.Evaluate(spec, atts, ents, ContentLanguage.en, false).WinnerVehicleId);
            atts[0].Outcome = AttemptOutcome.CleanLanding; atts[0].LandingDistanceM = 40;
            Assert.IsNull(RoundScoring.Evaluate(spec, atts, ents, ContentLanguage.en, false).WinnerVehicleId);
        }

        [Test]
        public void TargetZone_LowerErrorWins()
        {
            var spec = new RoundSpec { Index = 2, Rule = ScoringRule.TargetZone, TargetDistance = 50 };
            var atts = new List<AttemptRecord>
            {
                new AttemptRecord { AttemptId = "a", RoundIndex = 2, VehicleId = "sport_01", Outcome = AttemptOutcome.CleanLanding, LandingDistanceM = 70, TargetError = 20 },
                new AttemptRecord { AttemptId = "b", RoundIndex = 2, VehicleId = "truck_01", Outcome = AttemptOutcome.CleanLanding, LandingDistanceM = 48, TargetError = 2 }
            };
            Assert.AreEqual("truck_01", RoundScoring.Evaluate(spec, atts, TestRuns.TwoVehicles(), ContentLanguage.en, false).WinnerVehicleId);
        }
    }

    [TestFixture]
    public class ContentTests
    {
        [Test]
        public void ContentPack_HasFiveTitlesOneDescriptionThreePrompts_AllTruthChecked()
        {
            var run = TestRuns.SportWins(TestRuns.Plan());
            var pack = ContentPackService.Generate(run);
            Assert.AreEqual(5, pack.Titles.Count);
            Assert.AreEqual(1, pack.Titles.Count(t => t.Recommended));
            Assert.AreEqual(3, pack.Pairs.Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(pack.Description));
            foreach (var t in pack.Titles) Assert.AreEqual("Pass", t.TruthCheckStatus, t.Text);
            foreach (var p in pack.Pairs) Assert.AreEqual("Pass", p.TruthCheckStatus, p.PairId + ": " + p.TruthCheckStatus);
            Assert.IsFalse(pack.Issues.Any(i => i.Level == CheckLevel.Fail), string.Join("\n", pack.Issues));
            Assert.IsFalse(pack.IsDraft);
            StringAssert.Contains("Sports Car", pack.Description);
            StringAssert.Contains("00:00", pack.Description); // chapters only because capture is verified
        }

        [Test]
        public void NoCrashOrWinnerClaims_WhenNothingLikeThatHappened()
        {
            var run = TestRuns.AllTies(TestRuns.Plan());
            var pack = ContentPackService.Generate(run);
            Assert.IsNull(pack.Facts.OverallWinnerId);
            string all = string.Join("\n", pack.Titles.Select(t => t.Text)) + "\n" + pack.Description;
            StringAssert.DoesNotContain("crash", all.ToLowerInvariant());
            StringAssert.DoesNotContain("rollover", all.ToLowerInvariant());
            foreach (var t in pack.Titles)
            {
                bool claimsWin = t.Text.Contains(" Wins ") || t.Text.EndsWith(" Wins");
                Assert.IsFalse(claimsWin, t.Text);
            }
        }

        [Test]
        public void DraftWhenVideoNotVerified_AndNoChapters()
        {
            var run = TestRuns.SportWins(TestRuns.Plan(), verified: false);
            var pack = ContentPackService.Generate(run);
            Assert.IsTrue(pack.IsDraft);
            StringAssert.DoesNotContain("Chapters:", pack.Description);
        }

        [Test]
        public void ClaimChecker_RejectsInventedNumbersBrandsAndRealismClaims()
        {
            var fs = FactSheetBuilder.Build(TestRuns.AllTies(TestRuns.Plan()));
            Assert.IsTrue(ClaimChecker.Check("t", "Sports Car hits 300 km/h", fs, true).Any(i => i.Level == CheckLevel.Fail));
            Assert.IsTrue(ClaimChecker.Check("t", "Ferrari vs Truck", fs, true).Any(i => i.Level == CheckLevel.Fail));
            Assert.IsTrue(ClaimChecker.Check("t", "Realistic damage test", fs, true).Any(i => i.Level == CheckLevel.Fail));
            Assert.IsTrue(ClaimChecker.Check("t", "Massive crashes everywhere", fs, true).Any(i => i.Level == CheckLevel.Fail));
            Assert.IsFalse(ClaimChecker.Check("t", "Sports Car vs Heavy Truck — Who Wins?", fs, true).Any(i => i.Level == CheckLevel.Fail));
        }

        [Test]
        public void VietnameseContent_GeneratesAndExportsUnicode()
        {
            var ents = TestRuns.TwoVehicles();
            var concepts = ConceptGenerator.Generate("VehicleVersus", ents, 5, ContentLanguage.vi, null);
            var plan = RunPlanner.CreatePlan(concepts[0], ents, 5, 600, ContentLanguage.vi, "Progressive", out _);
            var run = TestRuns.SportWins(plan);
            var pack = ContentPackService.Generate(run);
            StringAssert.Contains("Xe thể thao", pack.Description);
            string dir = Path.Combine(Path.GetTempPath(), "mylti_test_" + Guid.NewGuid().ToString("N"));
            var files = ContentPackService.Export(dir, run, pack, QualityGate.Evaluate(run, pack, null, false));
            Assert.IsTrue(files.Count >= 10);
            StringAssert.Contains("Xe thể thao", File.ReadAllText(Path.Combine(dir, "youtube_description.txt"), Encoding.UTF8));
            Directory.Delete(dir, true);
        }

        [Test]
        public void UniqueRunFolders_NeverOverwrite()
        {
            string root = Path.Combine(Path.GetTempPath(), "mylti_runs_" + Guid.NewGuid().ToString("N"));
            string a = ContentPackService.UniqueRunFolder(root, "20261123_100000");
            File.WriteAllText(Path.Combine(a, "events.json"), "[]");
            string b = ContentPackService.UniqueRunFolder(root, "20261123_100000");
            Assert.AreNotEqual(a, b);
            Directory.Delete(root, true);
        }
    }

    [TestFixture]
    public class QualityTests
    {
        [Test]
        public void GoodVerifiedRun_NeedsHumanApprovalBeforePublishReady()
        {
            var run = TestRuns.SportWins(TestRuns.Plan());
            var pack = ContentPackService.Generate(run);
            Assert.AreEqual(ReadinessLevel.ReadyForReview, QualityGate.Evaluate(run, pack, null, false).Readiness);
            Assert.AreEqual(ReadinessLevel.PublishReady, QualityGate.Evaluate(run, pack, null, true).Readiness);
        }

        [Test]
        public void StuckFootage_IsNeedsMoreFootage_EvenWith600sFile()
        {
            var run = TestRuns.Run(TestRuns.Plan(), (r, v) => (AttemptOutcome.CleanLanding, 40 + r), verified: true, deadAir: 180);
            var q = QualityGate.Evaluate(run, ContentPackService.Generate(run), null, true);
            Assert.AreEqual(ReadinessLevel.NeedsMoreFootage, q.Readiness);
        }

        [Test]
        public void MissingVideo_IsNeverMediaReady()
        {
            var run = TestRuns.SportWins(TestRuns.Plan(), verified: false);
            var q = QualityGate.Evaluate(run, ContentPackService.Generate(run), null, true);
            Assert.AreEqual(ReadinessLevel.SimulationComplete, q.Readiness);
        }

        [Test]
        public void ColourOnlyChanges_AreFlaggedAsDuplicates()
        {
            var runA = TestRuns.SportWins(TestRuns.Plan());
            var runB = TestRuns.SportWins(TestRuns.Plan());
            runB.RunId = "other";
            runB.Plan.Entrants[0].ColorHex = "#00FF00";
            var fa = NoveltyFingerprint.FromRun(runA, FactSheetBuilder.Build(runA));
            var fb = NoveltyFingerprint.FromRun(runB, FactSheetBuilder.Build(runB));
            Assert.IsTrue(fa.Similarity(fb) > 0.85);
            var runC = TestRuns.SportWins(TestRuns.Plan("ImpossibleBridge"));
            var fc = NoveltyFingerprint.FromRun(runC, FactSheetBuilder.Build(runC));
            Assert.IsTrue(fa.Similarity(fc) < 0.85);
        }
    }

    [TestFixture]
    public class MediaAndPresetTests
    {
        static string Temp(byte[] data, string ext)
        {
            string p = Path.Combine(Path.GetTempPath(), "mylti_" + Guid.NewGuid().ToString("N") + ext);
            File.WriteAllBytes(p, data);
            return p;
        }

        [Test]
        public void Mp4_DurationAndTracksAreRead()
        {
            string p = Temp(TestRuns.Mp4(1000, 600000, true), ".mp4");
            var v = VideoFileInspector.Inspect(p);
            Assert.AreEqual(600.0, v.DurationSeconds.Value, 0.001);
            Assert.IsTrue(v.HasVideoTrack.Value);
            Assert.IsTrue(v.HasAudioTrack.Value);
            Assert.AreEqual(CaptureStatus.CaptureVerified, VideoFileInspector.Classify(v, 600, 2, out _));
            File.Delete(p);
        }

        [Test]
        public void Mp4_WrongDuration_And_MissingFile_And_Unfinalised()
        {
            string p = Temp(TestRuns.Mp4(1000, 590000, false), ".mp4");
            Assert.AreEqual(CaptureStatus.InvalidDuration, VideoFileInspector.Classify(VideoFileInspector.Inspect(p), 600, 2, out _));
            File.Delete(p);
            Assert.AreEqual(CaptureStatus.VideoMissing, VideoFileInspector.Classify(VideoFileInspector.Inspect(p), 600, 2, out _));
            var noMoov = TestRuns.Box("ftyp", Encoding.ASCII.GetBytes("isom0000")).Concat(TestRuns.Box("mdat", new byte[100])).ToArray();
            string q = Temp(noMoov, ".mp4");
            Assert.AreEqual(CaptureStatus.DurationUnknown, VideoFileInspector.Classify(VideoFileInspector.Inspect(q), 600, 2, out _));
            File.Delete(q);
        }

        [Test]
        public void Mkv_DurationIsRead()
        {
            string p = Temp(TestRuns.Mkv(600000.0), ".mkv");
            var v = VideoFileInspector.Inspect(p);
            Assert.IsNull(v.Error, v.Error);
            Assert.AreEqual(600.0, v.DurationSeconds.Value, 0.001);
            Assert.IsTrue(v.HasVideoTrack.Value);
            Assert.IsTrue(v.HasAudioTrack.Value);
            File.Delete(p);
        }

        [Test]
        public void Preset_RoundTrip_Clamp_UnknownVehicle_AndCorruptFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mylti_presets_" + Guid.NewGuid().ToString("N"));
            var repo = new PresetRepository(dir);
            var c = ScenarioConfig.CreateDefault();
            c.ScenarioId = "My Test";
            c.Sandbox.KickerHeight = 99;
            c.Entrants.Add(new Entrant { VehicleId = "ghost_99" });
            repo.Save(c);
            var loaded = repo.Load("my_test", out var warnings, out var error);
            Assert.IsNull(error);
            Assert.AreEqual(30f, loaded.Sandbox.KickerHeight, 0.001);
            Assert.AreEqual(2, loaded.Entrants.Count);
            File.WriteAllText(repo.PathFor("broken"), "{ \"schemaVersion\": 1, \"vehicles\": [ ");
            Assert.IsNull(repo.Load("broken", out _, out var err2));
            Assert.IsNotNull(err2);
            Assert.IsTrue(Directory.GetFiles(dir, "*.corrupt").Length == 1);
            Directory.Delete(dir, true);
        }
    }
}
