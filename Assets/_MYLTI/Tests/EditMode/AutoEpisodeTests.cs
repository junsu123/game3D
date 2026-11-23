using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mylti.Domain;
using NUnit.Framework;

namespace Mylti.Tests
{
    /// <summary>A run shaped exactly like the TenMinuteDirector writes it (segment labels + events).</summary>
    public static class DirectorLikeRun
    {
        public static RunRecord Build(RunPlan plan, Func<int, string, (AttemptOutcome, double)> outcome)
        {
            var run = new RunRecord { RunId = "auto_test", Plan = plan, ChallengeFinished = true, ProgramDurationSeconds = 600 };
            int id = 1;
            void Ev(string type, double t, int round, string v, string note = null, Dictionary<string, double> values = null, List<int> into = null)
            {
                var e = new RunEvent { Id = id++, Type = type, ProgramTime = t, RoundIndex = round, VehicleId = v, Note = note, Values = values ?? new Dictionary<string, double>() };
                run.Events.Add(e);
                into?.Add(e.Id);
            }
            void Seg(SegmentKind k, double a, double b, string label) => run.Segments.Add(new SegmentMarker { Kind = k, Start = a, End = b, Label = label });

            // cold open
            Ev("PhaseStarted", 0, 0, null, "Teaser");
            Seg(SegmentKind.Live, 0, 8, "Teaser (real attempt, result hidden)");
            Ev("Takeoff", 3, 0, "sport_01");
            Seg(SegmentKind.Card, 8, 20, "Title card");
            // intro
            Ev("PhaseStarted", 20, 0, null, "Vehicles & rules");
            double t = 20;
            foreach (var e in plan.Entrants) { Ev("IntroVehicle", t, 0, e.VehicleId); t += 8; }
            Ev("RulesShown", t, 0, null);
            Seg(SegmentKind.Intro, 20, 60, "Vehicle introductions");

            foreach (var ph in plan.Phases.Where(p => p.Kind == PhaseKind.Round || p.Kind == PhaseKind.Final))
            {
                var r = ph.Round;
                double s = ph.PlannedStart;
                Ev("PhaseStarted", s, r.Index, null, r.Title);
                Seg(SegmentKind.Card, s, s + 4, "Round " + r.Index + " intro");
                double cur = s + 4;
                var groups = r.HeadToHead ? new List<List<Entrant>> { plan.Entrants } : plan.Entrants.Select(e => new List<Entrant> { e }).ToList();
                foreach (var g in groups)
                {
                    Seg(SegmentKind.Live, cur, cur + 16, "attempt");
                    foreach (var e in g)
                    {
                        var (oc, dist) = outcome(r.Index, e.VehicleId);
                        var a = new AttemptRecord
                        {
                            AttemptId = $"r{r.Index}_a1_{e.VehicleId}", RoundIndex = r.Index, AttemptIndex = 1, VehicleId = e.VehicleId, Outcome = oc,
                            StartProgramTime = cur, TakeoffProgramTime = cur + 9, LandingProgramTime = cur + 12, EndProgramTime = cur + 16,
                            MaxSpeedKmh = 120, PeakHeightM = r.KickerHeight + 5, AirTimeS = 3, LandingDistanceM = dist
                        };
                        if (r.Rule == ScoringRule.TargetZone) a.TargetError = Math.Abs(dist - r.TargetDistance);
                        if (r.Rule == ScoringRule.BeyondTheWall) a.ClearedWall = dist > r.WallDistance ? 1 : 0;
                        Ev("AttemptStart", cur, r.Index, e.VehicleId, null, null, a.EventIds);
                        Ev("Takeoff", cur + 9, r.Index, e.VehicleId, null, null, a.EventIds);
                        Ev("Landed", cur + 12, r.Index, e.VehicleId, null, new Dictionary<string, double> { ["impactSpeed"] = 8 }, a.EventIds);
                        if (oc == AttemptOutcome.Rollover) Ev("Rollover", cur + 14, r.Index, e.VehicleId, null, null, a.EventIds);
                        run.Attempts.Add(a);
                    }
                    Seg(SegmentKind.Card, cur + 16, cur + 18.6, "Attempt result");
                    cur += 18.6;
                }
                var best = run.Attempts.Where(a => a.RoundIndex == r.Index).First();
                Seg(SegmentKind.Replay, cur, cur + 10, "Replay " + best.AttemptId);
                Ev("ReplayShown", cur, r.Index, best.VehicleId, "take");
                cur += 10;
                var rr = RoundScoring.Evaluate(r, run.Attempts.Where(a => a.RoundIndex == r.Index).ToList(), plan.Entrants, plan.Language, ph.Kind == PhaseKind.Final);
                run.Rounds.Add(rr);
                Seg(SegmentKind.Card, cur, cur + 6, ph.Kind == PhaseKind.Final ? "FINAL RESULT" : $"ROUND {r.Index} RESULT");
            }
            var sum = plan.Phases.Last();
            Ev("PhaseStarted", sum.PlannedStart, 0, null, "Results");
            Seg(SegmentKind.Summary, sum.PlannedStart, sum.PlannedStart + 8, "Final standings");
            Seg(SegmentKind.Summary, sum.PlannedStart + 8, 600, "Outro");
            run.Capture.Backend = CaptureBackend.BuiltInFfmpeg;
            run.Capture.Status = CaptureStatus.CaptureVerified;
            run.Capture.Video = new VideoInfo { Exists = true, DurationSeconds = 600, HasAudioTrack = true, HasVideoTrack = true };
            run.Highlights = HighlightScorer.Extract(run);
            return run;
        }

        public static RunRecord SportWins() => Build(TestRuns.Plan(), (r, v) => v == "sport_01" ? (AttemptOutcome.CleanLanding, 40 + r * 4.3) : (r == 4 ? AttemptOutcome.Rollover : AttemptOutcome.RoughLanding, 30 + r * 3.1));
    }

    [TestFixture]
    public class NarrationTests
    {
        [Test]
        public void Slots_CoverTheEpisode_WithoutOverlaps()
        {
            var run = DirectorLikeRun.SportWins();
            var slots = NarrationSlotBuilder.Build(run);
            Assert.IsTrue(slots.Count >= 25, "slots: " + slots.Count);
            Assert.AreEqual("hook", slots[0].Kind);
            Assert.IsTrue(slots[0].PreOutcome);
            foreach (var k in new[] { "title", "vehicle", "rules", "round_intro", "result", "replay", "round_result", "standings", "outro" })
                Assert.IsTrue(slots.Any(s => s.Kind == k), "missing slot kind " + k);
            Assert.AreEqual(6, slots.Count(s => s.Kind == "round_result"));
            for (int i = 1; i < slots.Count; i++) Assert.IsTrue(slots[i].Start >= slots[i - 1].End, $"overlap {slots[i - 1].Id}/{slots[i].Id}");
            Assert.IsTrue(slots.All(s => s.Seconds >= 1.8));
        }

        [Test]
        public void OfflineTemplates_PassTheGuard_ForEverySlot()
        {
            var run = DirectorLikeRun.SportWins();
            var fs = FactSheetBuilder.Build(run);
            var slots = NarrationSlotBuilder.Build(run);
            var script = NarrationWriter.WriteAsync(run, slots, fs, null, 7, null).GetAwaiter().GetResult();
            Assert.AreEqual(slots.Count, script.Lines.Count);
            Assert.IsFalse(script.Notes.Any(n => n.Contains("template trimmed")), string.Join("\n", script.Notes));
            var rolloverLine = script.Lines.First(l => slots.First(s => s.Id == l.SlotId).Attempts.Any(a => a.Outcome == AttemptOutcome.Rollover) && slots.First(s => s.Id == l.SlotId).Kind == "result");
            StringAssert.Contains("Heavy Truck", rolloverLine.Text);
            StringAssert.Contains("-->", script.ToSrt());
        }

        [Test]
        public void Guard_BlocksSpoilersAndInventedNumbers()
        {
            var run = DirectorLikeRun.SportWins();
            var fs = FactSheetBuilder.Build(run);
            var slots = NarrationSlotBuilder.Build(run);
            var hook = slots.First(s => s.Kind == "hook");
            Assert.IsNotNull(SlotGuard.Check("The Sports Car wins this one easily.", hook, fs));
            Assert.IsNull(SlotGuard.Check("Who wins this?", hook, fs));
            var result = slots.First(s => s.Kind == "result" && s.Attempts.All(a => a.Outcome == AttemptOutcome.CleanLanding));
            Assert.IsNotNull(SlotGuard.Check("It flips and crashes!", result, fs));
            Assert.IsNotNull(SlotGuard.Check("A huge 999 meter jump!", result, fs));
            var rr = slots.First(s => s.Kind == "round_result" && s.Round.WinnerVehicleId == "sport_01");
            Assert.IsNull(SlotGuard.Check("The Sports Car wins the round!", rr, fs));
            Assert.IsNotNull(SlotGuard.Check("The Heavy Truck wins the round!", rr, fs));
        }

        [Test]
        public void ClaudeCommentaryReply_IsParsedAndBadLinesReplaced()
        {
            var run = DirectorLikeRun.SportWins();
            var slots = NarrationSlotBuilder.Build(run);
            string json = "Here you go:\n```json\n{\"lines\":[" + string.Join(",", slots.Select((s, i) =>
                "{\"id\":\"" + s.Id + "\",\"text\":\"" + (i == 0 ? "The Sports Car wins everything!" : "Here we go.") + "\"}")) + "]}\n```";
            var extracted = ClaudeClient.ExtractJson(json);
            Assert.IsNotNull(extracted);
            Assert.IsTrue(extracted.StartsWith("{") && extracted.EndsWith("}"));
            var lines = MiniJson.Arr(MiniJson.Get(MiniJson.Obj(MiniJson.Parse(extracted)), "lines"));
            Assert.AreEqual(slots.Count, lines.Count);
        }
    }

    [TestFixture]
    public class EpisodeWriterTests
    {
        const string Reply = @"{""seriesId"":""ImpossibleBridge"",""twistKind"":""bridge"",""vehicles"":[""suv_01"",""truck_01"",""ghost""],
""viewerQuestion"":""Can a heavy truck cross a 4 meter bridge and still fly?"",""viewerPromise"":""Narrower bridges, taller ramps."",""stakes"":""Fall and you score nothing."",
""centralConflict"":""width vs precision"",""keyVisual"":""truck on a narrow bridge"",""workingTitle"":""SUV vs Heavy Truck: The Impossible Bridge"",""unexpectedPossibleOutcome"":""the truck makes it"",
""rounds"":[{""kickerHeight"":5,""kickerAngle"":18,""speedFactor"":0.8,""obstacles"":""None"",""rule"":""FurthestValidLanding"",""bridgeWidth"":0,""subQuestion"":""Who sets the benchmark?""},
{""kickerHeight"":9,""kickerAngle"":22,""speedFactor"":0.9,""obstacles"":""None"",""rule"":""FurthestValidLanding"",""bridgeWidth"":7},
{""kickerHeight"":11,""kickerAngle"":24,""speedFactor"":1.0,""obstacles"":""BarrelLine"",""rule"":""TargetZone"",""bridgeWidth"":6},
{""kickerHeight"":15,""kickerAngle"":26,""speedFactor"":1.05,""obstacles"":""CrateWall"",""rule"":""BeyondTheWall"",""bridgeWidth"":2},
{""kickerHeight"":99,""kickerAngle"":30,""speedFactor"":5,""obstacles"":""BarrelsAndWall"",""rule"":""CleanLandingOnly"",""bridgeWidth"":4.2},
{""kickerHeight"":18,""kickerAngle"":28,""speedFactor"":1.1,""obstacles"":""None"",""rule"":""FurthestValidLanding"",""bridgeWidth"":5,""headToHead"":false}]}";

        [Test]
        public void ClaudeScript_IsValidatedAndClamped()
        {
            var s = EpisodeWriter.Parse("```json\n" + Reply + "\n```", TestRuns.TwoVehicles(), 3, out string err);
            Assert.IsNull(err, err);
            Assert.AreEqual("ImpossibleBridge", s.Concept.SeriesId);
            Assert.AreEqual(2, s.Entrants.Count);                     // unknown "ghost" dropped
            Assert.AreEqual(6, s.Rounds.Count);
            Assert.AreEqual(30f, s.Rounds[4].KickerHeight, 0.01);      // clamped
            Assert.AreEqual(1.3f, s.Rounds[4].SpeedFactor, 0.01);
            Assert.IsTrue(s.Rounds[3].BridgeWidth >= 3.2f);            // never narrower than the widest vehicle + margin
            Assert.IsTrue(s.Rounds[5].HeadToHead);                     // final always head-to-head
            Assert.AreEqual("Who sets the benchmark?", s.Rounds[0].SubQuestion);
            var plan = RunPlanner.CreatePlan(s.Concept, s.Entrants, 3, 600, ContentLanguage.en, "Progressive", out _, s.Rounds);
            Assert.AreEqual(600.0, plan.Phases.Sum(p => p.PlannedDuration), 0.01);
            Assert.AreEqual(9f, plan.ScoredRounds.ElementAt(1).KickerHeight, 0.01);
        }

        [Test]
        public void ClaudeScript_WithBrandIsRejected()
        {
            var bad = Reply.Replace("SUV vs Heavy Truck: The Impossible Bridge", "Ferrari vs Truck");
            Assert.IsNull(EpisodeWriter.Parse(bad, TestRuns.TwoVehicles(), 3, out string err));
            Assert.IsNotNull(err);
        }

        [Test]
        public void OfflineWriter_RotatesAndNeverRepeatsTheLastEpisode()
        {
            var history = new List<EpisodeLogEntry>();
            var seen = new HashSet<string>();
            for (int i = 0; i < 6; i++)
            {
                var s = EpisodeWriter.WriteOffline(history, null, TestRuns.TwoVehicles(), true, "Progressive", 100 + i);
                string key = s.Concept.SeriesId + "/" + s.Concept.TwistKind + "/" + string.Join("+", s.Entrants.Select(e => e.VehicleId).OrderBy(x => x));
                Assert.IsTrue(seen.Add(key), "repeated " + key);
                history.Add(new EpisodeLogEntry { SeriesId = s.Concept.SeriesId, TwistKind = s.Concept.TwistKind, Vehicles = s.Entrants.Select(e => e.VehicleId).ToList() });
            }
        }
    }

    [TestFixture]
    public class SoundtrackTests
    {
        [Test]
        public void Mix_HasExactLength_AndVoiceIsAudible()
        {
            var st = new Soundtrack(10, 8000);
            var tr = new SpeedTrack { TopSpeedMs = 40 };
            for (int i = 0; i <= 30; i++) { tr.Times.Add(1 + i * 0.1); tr.Speeds.Add(i); }
            st.AddEngine(tr);
            st.AddWind(4, 6);
            st.AddImpact(6, 0.8f);
            st.AddBed();
            var voice = new float[8000];
            for (int i = 0; i < voice.Length; i++) voice[i] = (float)Math.Sin(i * 0.1) * 0.5f;
            st.AddVoice(voice, 2.0);
            Assert.IsTrue(st.VoiceActiveAround(2.5, 0.1));
            string p = Path.Combine(Path.GetTempPath(), "mylti_mix_" + Guid.NewGuid().ToString("N") + ".wav");
            st.Render(p, 10);
            var back = Wav.ReadMono(p, out int rate);
            Assert.AreEqual(8000, rate);
            Assert.AreEqual(80000, back.Length);
            Assert.IsTrue(back.Skip(16000).Take(8000).Max() > 0.3f);
            File.Delete(p);
        }
    }
}
