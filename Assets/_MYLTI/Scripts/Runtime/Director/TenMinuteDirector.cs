using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// 10-Minute Challenge Director (spec §17, §22): runs the planned episode as ONE continuous take —
    /// cold open (real teaser) → intro → 5 escalating rounds → final → results — on the program clock.
    /// The PacingEngine adds only meaningful extra passes or real replays when a round ends early,
    /// skips optional parts when late, and the summary closes the program at the target time.
    /// After the take: capture verification → highlights → fact sheet → content pack → quality gate.
    /// </summary>
    public sealed class TenMinuteDirector : MonoBehaviour
    {
        public ChallengeSession Session;
        public CameraDirector Cam;
        public ProgramClock Clock;
        public EventLogger Log;
        public ReplaySystem Replay;
        public CaptureCoordinator Capture;
        public HighlightFrameCapture Frames;
        public ShowOverlay Overlay;
        public NoveltyHistory History;
        public AppSettings Settings;
        public string RunsRoot;

        public ProjectState State { get; private set; } = ProjectState.Setup;
        public RunPlan Plan { get; private set; }
        public RunRecord Run { get; private set; }
        public string RunFolder { get; private set; }
        public ContentPack Pack { get; private set; }
        public QualityReport Quality { get; private set; }
        public string CurrentLabel { get; private set; } = "—";
        public string NextLabel { get; private set; } = "—";
        public int AttemptsRun { get; private set; }
        public bool Paused { get; private set; }
        public string LastError { get; private set; }
        public bool HumanApproved { get; private set; }
        public bool PackEdited;
        public event Action StateChanged;

        public bool IsBusy => State == ProjectState.CaptureReady || State == ProjectState.Recording || State == ProjectState.Finalizing || State == ProjectState.Analyzing;

        const double CardSeconds = 4.0;
        const double MiniResultSeconds = 2.6;
        bool abortRequested;
        double lastApexTime = -1;
        readonly HashSet<string> replayed = new HashSet<string>();
        readonly List<double> passDurations = new List<double>();
        ContentLanguage Lang => Plan?.Language ?? ContentLanguage.en;
        int Lanes => Plan.Entrants.Count;
        string L(string en, string vi) => Lang == ContentLanguage.vi ? vi : en;

        // ================================================================ control
        public bool Begin(RunPlan plan, out string error)
        {
            error = null;
            if (IsBusy) { error = "A run is already in progress."; return false; }
            if (plan == null || plan.Phases.Count == 0) { error = "Generate a plan first."; return false; }
            if (plan.Entrants.Any(e => VehicleCatalog.Find(e.VehicleId) == null)) { error = "Plan references an unknown vehicle."; return false; }
            Plan = plan;
            StartCoroutine(Routine());
            return true;
        }

        public void RequestAbort()
        {
            if (!IsBusy) return;
            abortRequested = true;
            if (Paused) TogglePause();
            Log.Log("ManualOverride", 0, 0, null, "operator aborted the run");
        }

        public void TogglePause()
        {
            if (State != ProjectState.Recording) return;
            Paused = !Paused;
            Time.timeScale = Paused ? 0f : 1f;
            Clock.Paused = Paused;
            if (Paused)
            {
                Capture.MarkInterrupted("Paused by operator at " + DescriptionComposer.Timestamp(Clock.Time));
                Log.Log("Paused", 0, 0, null, "take interrupted — video must be re-validated");
            }
            else Log.Log("Resumed", 0, 0, null);
            StateChanged?.Invoke();
        }

        public void SkipCurrentAttempt()
        {
            if (State != ProjectState.Recording || !Session.Running) return;
            Run.ManualOverrides.Add(DescriptionComposer.Timestamp(Clock.Time) + " skip attempt");
            Session.AbortAll("operator skipped the attempt (manual override)");
        }

        void SetState(ProjectState s)
        {
            State = s;
            StateChanged?.Invoke();
        }

        // ================================================================ main routine
        IEnumerator Routine()
        {
            abortRequested = false;
            AttemptsRun = 0;
            Pack = null; Quality = null; LastError = null; HumanApproved = false; PackEdited = false;
            replayed.Clear(); passDurations.Clear();
            string runId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            RunFolder = ContentPackService.UniqueRunFolder(RunsRoot, runId);
            Run = new RunRecord
            {
                RunId = Path.GetFileName(RunFolder),
                StartedAtIso = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                Plan = Plan,
                AudioEnabled = Settings.EnableAudio
            };
            Log.Attach(Run);
            Log.Logged += OnLogged;
            Cam.ManualOverride += OnCamOverride;
            Replay.Clear();
            Frames.Clear();
            Overlay.ClearAll();
            Overlay.CardsEnabled = Settings.ShowCardsInVideo;
            Overlay.ScoreboardEnabled = Settings.ShowScoreboardInVideo;
            Overlay.Now = () => Clock.Running ? Clock.Time : Time.time;
            SetState(ProjectState.CaptureReady);

            Session.EnsureVehicles(Plan.Entrants);
            Session.BuildMap(Plan.Phases[0].Round ?? Plan.ScoredRounds.First(), Lanes, Plan.Seed);
            Cam.FocusOverride = null;
            Cam.Forced = Shot.FixedWide;
            yield return null;
            yield return null;

            Capture.Prepare(Run, RunFolder, Plan.TargetDurationSeconds);
            var begin = Capture.BeginAsync();
            while (!begin.IsCompleted) yield return null;
            if (begin.IsFaulted || !begin.Result)
            {
                LastError = Capture.LastMessage ?? begin.Exception?.Message;
                Cleanup();
                SetState(ProjectState.RecordingFailed);
                yield break;
            }

            Clock.StartClock();
            SetState(ProjectState.Recording);
            for (int i = 0; i < Plan.Phases.Count && !abortRequested; i++)
            {
                var ph = Plan.Phases[i];
                CurrentLabel = ph.Label;
                NextLabel = i + 1 < Plan.Phases.Count ? Plan.Phases[i + 1].Label : "—";
                Log.Log("PhaseStarted", ph.Round?.Index ?? 0, 0, null, ChapterLabel(ph));
                StateChanged?.Invoke();
                switch (ph.Kind)
                {
                    case PhaseKind.ColdOpen: yield return StartCoroutine(ColdOpen(ph)); break;
                    case PhaseKind.Intro: yield return StartCoroutine(Intro(ph)); break;
                    case PhaseKind.Round:
                    case PhaseKind.Final: yield return StartCoroutine(RoundPhase(ph)); break;
                    case PhaseKind.Summary: yield return StartCoroutine(Summary(ph)); break;
                }
            }

            Log.EndSegment();
            Session.AbortAll("program end");
            Log.Log("ProgramEnd", 0, 0, null, abortRequested ? "aborted" : "complete");
            Clock.StopClock();
            Run.ProgramDurationSeconds = Clock.Time;
            Run.ChallengeFinished = !abortRequested;
            Cam.Forced = Shot.FixedWide;
            Overlay.ClearAll();

            SetState(ProjectState.Finalizing);
            var end = Capture.EndAsync();
            while (!end.IsCompleted) yield return null;
            if (end.IsFaulted) Run.Capture.Message = "Recorder stop error: " + end.Exception?.GetBaseException().Message;

            SetState(ProjectState.Analyzing);
            yield return null;
            try { Analyze(); }
            catch (Exception e)
            {
                LastError = "Analysis failed: " + e.Message;
                Debug.LogException(e);
            }
            Cleanup();
            SetState(abortRequested ? ProjectState.Incomplete : ProjectState.ContentReady);
        }

        void Cleanup()
        {
            Log.Logged -= OnLogged;
            Cam.ManualOverride -= OnCamOverride;
            Cam.Forced = null;
            Cam.FocusOverride = null;
            Time.timeScale = 1f;
            Paused = false;
            Clock.Paused = false;
            CurrentLabel = NextLabel = "—";
        }

        string ChapterLabel(PhaseSpec ph)
        {
            switch (ph.Kind)
            {
                case PhaseKind.ColdOpen: return L("Teaser", "Mở đầu");
                case PhaseKind.Intro: return L("Vehicles & rules", "Giới thiệu xe & luật chơi");
                case PhaseKind.Summary: return L("Results & best moments", "Kết quả & khoảnh khắc đáng nhớ");
                default: return ph.Round.Title;
            }
        }

        // ================================================================ phases
        IEnumerator ColdOpen(PhaseSpec ph)
        {
            var spec = ph.Round;
            Session.BuildMap(spec, Lanes, Plan.Seed);
            lastApexTime = -1;
            StartLive(Plan.Entrants, spec, 0, 1, true, "Teaser (real attempt, result hidden)", Session.Map.RampStartZ - 75f, 0.92f);
            double cutAt = ph.PlannedEnd - 3.5;
            while (!abortRequested && Clock.Time < cutAt && Session.Running)
            {
                if (lastApexTime > 0 && Clock.Time - lastApexTime > 0.7) break;
                yield return null;
            }
            EndLive();
            Session.AbortAll("teaser cut before the landing — result kept for the final");
            Session.ParkAll(); // hard cut: the landing is never shown, so the result stays hidden
            Cam.Forced = Shot.FixedWide;
            Cam.Snap();
            Overlay.ShowCard(Plan.Concept.ViewerQuestion.ToUpperInvariant(), "MYLTI CHALLENGE", Plan.Concept.ViewerPromise);
            Log.BeginSegment(SegmentKind.Card, "Title card", "FixedWide");
            yield return WaitUntilProgram(ph.PlannedEnd);
            Overlay.HideCard();
            Log.EndSegment();
        }

        IEnumerator Intro(PhaseSpec ph)
        {
            Session.BuildMap(ph.Round, Lanes, Plan.Seed);
            Session.LineUp(Plan.Entrants);
            int n = Plan.Entrants.Count;
            double per = Math.Max(5.0, Math.Min(8.0, (ph.PlannedDuration - 12.0) / n));
            Log.BeginSegment(SegmentKind.Intro, "Vehicle introductions", "Lineup");
            foreach (var e in Plan.Entrants)
            {
                if (abortRequested) yield break;
                if (!Session.Vehicles.TryGetValue(e.VehicleId, out var vc)) continue;
                var d = vc.Def;
                Log.Log("IntroVehicle", 0, 0, e.VehicleId, d.DisplayName(Lang));
                Cam.LineupTarget = vc.transform;
                Cam.Forced = Shot.Lineup;
                Cam.Snap();
                string stats = L($"{d.MassKg:0} kg · AWD · top speed {d.TopSpeedKmh:0} km/h (in-game)",
                                 $"{d.MassKg:0} kg · 4 bánh chủ động · tốc độ tối đa {d.TopSpeedKmh:0} km/h (trong game)");
                Overlay.LowerThird(d.DisplayName(Lang).ToUpperInvariant(), stats, per - 0.3);
                yield return WaitProgram(per);
            }
            Cam.LineupTarget = null;
            Cam.Forced = Shot.Flyover;
            Cam.Snap();
            var r1 = Plan.ScoredRounds.First();
            int rounds = Plan.ScoredRounds.Count();
            string rules = L($"{rounds - 1} rounds + a final (worth double) · {RunPlanner.RuleText(r1.Rule, r1, Lang)} · {Plan.Concept.Stakes}",
                             $"{rounds - 1} vòng + chung kết (điểm gấp đôi) · {RunPlanner.RuleText(r1.Rule, r1, Lang)} · {Plan.Concept.Stakes}");
            Overlay.ShowCard(L("THE CHALLENGE", "THỬ THÁCH"), Plan.Concept.ViewerPromise, rules);
            Log.Log("RulesShown", 0, 0, null, rules);
            double start = Clock.Time, endT = ph.PlannedEnd;
            while (!abortRequested && Clock.Time < endT)
            {
                Cam.FlyoverT = (float)((Clock.Time - start) / Math.Max(1, endT - start));
                yield return null;
            }
            Overlay.HideCard();
            Log.EndSegment();
        }

        IEnumerator RoundPhase(PhaseSpec ph)
        {
            var spec = ph.Round;
            bool isFinal = ph.Kind == PhaseKind.Final;
            double deadline = ph.PlannedEnd;
            Session.BuildMap(spec, Lanes, Plan.Seed);
            UpdateScoreboard(spec.Index);

            // round intro card over a flyover of the new setup
            Cam.Forced = Shot.Flyover;
            Cam.Snap();
            Overlay.ShowCard(spec.Title.ToUpperInvariant(), spec.ChangeSummary, RunPlanner.RuleText(spec.Rule, spec, Lang) + "\n" + spec.SubQuestion,
                isFinal ? new Color(1f, 0.3f, 0.2f) : (Color?)null);
            Log.BeginSegment(SegmentKind.Card, "Round " + spec.Index + " intro", "Flyover");
            double s0 = Clock.Time;
            while (!abortRequested && Clock.Time < s0 + CardSeconds)
            {
                Cam.FlyoverT = 0.3f + 0.65f * (float)((Clock.Time - s0) / CardSeconds);
                yield return null;
            }
            Overlay.HideCard();
            Cam.Forced = null;

            int pass = 0;
            for (; pass < spec.AttemptPasses && !abortRequested; pass++)
                yield return StartCoroutine(RunPass(spec, pass));

            int extras = 0;
            while (!abortRequested && extras < spec.MaxExtraPasses && Remaining(deadline) > EstimatePass(spec) + 6.0 + 3.0)
            {
                Log.Log("PacingDecision", spec.Index, 0, null, $"time left {Remaining(deadline):0}s → extra attempt for every vehicle (best attempt counts)");
                yield return StartCoroutine(RunPass(spec, pass++));
                extras++;
            }

            if (!abortRequested && Remaining(deadline) > 6.0 + 9.0)
            {
                var best = Run.Attempts.Where(a => a.RoundIndex == spec.Index && !a.IsTeaser && (a.TakeoffProgramTime.HasValue || a.ImpactSpeedKmh.HasValue) && !replayed.Contains(a.AttemptId))
                                       .OrderByDescending(a => HighlightScorer.Score(a, isFinal)).FirstOrDefault();
                if (best != null)
                {
                    Log.Log("PacingDecision", spec.Index, 0, null, "time left → real replay of the round's best moment");
                    yield return StartCoroutine(ReplayAttempt(best, Math.Min(11.0, Remaining(deadline) - 7.0), false));
                }
            }

            // result
            var attempts = Run.Attempts.Where(a => a.RoundIndex == spec.Index && !a.IsTeaser).ToList();
            var rr = RoundScoring.Evaluate(spec, attempts, Plan.Entrants, Lang, isFinal);
            Run.Rounds.RemoveAll(x => x.RoundIndex == spec.Index);
            Run.Rounds.Add(rr);
            Log.Log("RoundResult", spec.Index, 0, rr.WinnerVehicleId, rr.Explanation);
            UpdateScoreboard(spec.Index);

            var winnerVc = rr.WinnerVehicleId != null && Session.Vehicles.TryGetValue(rr.WinnerVehicleId, out var w) ? w : null;
            Cam.FocusOverride = winnerVc != null ? new List<Transform> { winnerVc.transform } : Session.Vehicles.Values.Select(v => v.transform).ToList();
            bool crowdCut = Cam.Crowd != null && Cam.Crowd.Count > 0;
            Cam.Forced = crowdCut ? Shot.CrowdReaction : Shot.Orbit; // applause cut-away first, then the winner
            Cam.Snap();
            string head = isFinal ? L("FINAL RESULT", "KẾT QUẢ CHUNG KẾT") : L($"ROUND {spec.Index} RESULT", $"KẾT QUẢ VÒNG {spec.Index}");
            string sub = winnerVc != null ? L($"{winnerVc.Def.DisplayName(Lang)} wins the round", $"{winnerVc.Def.DisplayName(Lang)} thắng vòng này")
                                          : L("No winner this round", "Không có xe thắng vòng này");
            Overlay.ShowCard(head, sub, rr.Explanation, isFinal ? new Color(1f, 0.3f, 0.2f) : (Color?)null);
            Log.BeginSegment(SegmentKind.Card, head, "Orbit");
            double hold = Math.Max(4.0, Math.Min(8.0, Remaining(deadline)));
            if (crowdCut)
            {
                yield return WaitProgram(2.2);
                Cam.Forced = Shot.Orbit;
                hold -= 2.2;
            }
            yield return WaitProgram(hold);
            Overlay.HideCard();
            Log.EndSegment();
            Cam.Forced = null;
            Cam.FocusOverride = null;
        }

        IEnumerator RunPass(RoundSpec spec, int passIndex)
        {
            var groups = spec.HeadToHead ? new List<List<Entrant>> { Plan.Entrants.ToList() }
                                         : Plan.Entrants.Select(e => new List<Entrant> { e }).ToList();
            foreach (var group in groups)
            {
                if (abortRequested) yield break;
                double t0 = Clock.Time;
                string who = spec.HeadToHead ? L("ALL VEHICLES", "TẤT CẢ CÁC XE") : group[0].Def.DisplayName(Lang).ToUpperInvariant();
                string detail = L($"Attempt {passIndex + 1} · {RunPlanner.RuleText(spec.Rule, spec, Lang)}", $"Lượt {passIndex + 1} · {RunPlanner.RuleText(spec.Rule, spec, Lang)}");
                Overlay.LowerThird(who, detail, 3.5);
                var trackers = StartLive(group, spec, spec.Index, passIndex + 1, false, $"Round {spec.Index} attempt {passIndex + 1} {who}");
                if (trackers.Count == 0) { Log.EndSegment(); continue; }
                while (!abortRequested && Session.Running) yield return null;
                EndLive();

                // mini result: after a dramatic attempt a short crowd cut-away, then a slow orbit of the vehicle(s)
                Cam.FocusOverride = trackers.Select(t => t.Vehicle.transform).ToList();
                bool drama = trackers.Any(t => t.Rec.Outcome == AttemptOutcome.Rollover || t.Rec.Outcome == AttemptOutcome.Crashed || t.Rec.Outcome == AttemptOutcome.FellOff || (t.Rec.DamagePercent ?? 0) >= 40);
                bool cutAway = drama && Cam.Crowd != null && Cam.Crowd.Count > 0;
                Cam.Forced = cutAway ? Shot.CrowdReaction : Shot.Orbit;
                Cam.Snap();
                var lines = trackers.Select(t => OutcomeText(t.Rec)).ToList();
                Overlay.LowerThird(lines[0], lines.Count > 1 ? string.Join("   ·   ", lines.Skip(1)) : "", MiniResultSeconds);
                Log.BeginSegment(SegmentKind.Card, "Attempt result", "Orbit");
                if (cutAway)
                {
                    yield return WaitProgram(1.4);
                    Cam.Forced = Shot.Orbit;
                }
                yield return WaitProgram(cutAway ? MiniResultSeconds - 0.2 : MiniResultSeconds);
                Log.EndSegment();
                Cam.Forced = null;
                Cam.FocusOverride = null;
                passDurations.Add(Clock.Time - t0);
                UpdateScoreboard(spec.Index);
            }
        }

        IEnumerator Summary(PhaseSpec ph)
        {
            double target = Plan.TargetDurationSeconds;
            var standings = RoundScoring.ComputeStandings(Run.Rounds, Run.Attempts, Plan.Entrants, out string overall);
            var finalSpec = Plan.ScoredRounds.Last();
            Session.BuildMap(finalSpec, Lanes, Plan.Seed);
            Session.LineUp(Plan.Entrants);
            Cam.FocusOverride = Session.Vehicles.Values.Select(v => v.transform).ToList();
            Cam.Forced = Shot.Orbit;
            Cam.Snap();
            Overlay.SetScoreboard(null, null);
            var rows = standings.Select(s =>
            {
                var e = Plan.Entrants.First(x => x.VehicleId == s.VehicleId);
                ColorUtility.TryParseHtmlString(e.ColorHex, out var col);
                var best = Run.Attempts.Where(a => a.VehicleId == s.VehicleId && a.Scored && a.ValidLanding && a.LandingDistanceM.HasValue)
                                       .Select(a => a.LandingDistanceM.Value).DefaultIfEmpty(-1).Max();
                return new ShowOverlay.ScoreRow
                {
                    Name = e.Def.DisplayName(Lang), Color = col, Points = s.Points,
                    Extra = best > 0 ? L($"best {best:0.0} m", $"xa nhất {best:0.0} m") : L("no valid landing", "chưa tiếp đất hợp lệ")
                };
            }).ToList();
            string title = overall != null ? L($"WINNER: {VehicleCatalog.Find(overall).DisplayName(Lang).ToUpperInvariant()}", $"CHIẾN THẮNG: {VehicleCatalog.Find(overall).DisplayName(Lang).ToUpperInvariant()}")
                                           : L("FINAL STANDINGS — NO OVERALL WINNER", "BẢNG XẾP HẠNG — KHÔNG CÓ XE THẮNG CHUNG CUỘC");
            Overlay.ShowStandings(title, rows);
            Log.BeginSegment(SegmentKind.Summary, "Final standings", "Orbit");
            Log.Log("FinalStandings", 0, 0, overall, null);
            yield return WaitProgram(Math.Max(5.0, Math.Min(8.0, target - Clock.Time - 6.0)));
            Overlay.HideStandings();
            Log.EndSegment();

            // best moments not shown yet (real recorded replays, one per round, best first)
            var candidates = Run.Attempts.Where(a => !a.IsTeaser && (a.TakeoffProgramTime.HasValue || a.ImpactSpeedKmh.HasValue) && !replayed.Contains(a.AttemptId))
                                         .GroupBy(a => a.RoundIndex)
                                         .Select(g => g.OrderByDescending(a => HighlightScorer.Score(a, a.RoundIndex == finalSpec.Index)).First())
                                         .OrderByDescending(a => HighlightScorer.Score(a, a.RoundIndex == finalSpec.Index))
                                         .Take(4).ToList();
            foreach (var a in candidates)
            {
                if (abortRequested || target - Clock.Time < 15.0) break;
                yield return StartCoroutine(ReplayAttempt(a, Math.Min(10.0, target - Clock.Time - 7.0), true));
            }

            Session.BuildMap(finalSpec, Lanes, Plan.Seed);
            Session.LineUp(Plan.Entrants);
            Cam.FocusOverride = Session.Vehicles.Values.Select(v => v.transform).ToList();
            Cam.Forced = Shot.Orbit;
            Cam.Snap();
            string answer = overall != null
                ? L($"{VehicleCatalog.Find(overall).DisplayName(Lang)} takes the overall win.", $"{VehicleCatalog.Find(overall).DisplayName(Lang)} giành chiến thắng chung cuộc.")
                : L("Nobody took the overall win this time.", "Lần này không có xe nào thắng chung cuộc.");
            Overlay.ShowCard(L("THANKS FOR WATCHING", "CẢM ƠN ĐÃ XEM"), answer, L("Which vehicle should take on the next challenge? Tell us in the comments!", "Bạn muốn xe nào tham gia thử thách tiếp theo? Bình luận nhé!"));
            Log.BeginSegment(SegmentKind.Summary, "Outro", "Orbit");
            yield return WaitUntilProgram(Math.Max(target, Clock.Time + 4.0));
            Overlay.HideCard();
            Log.EndSegment();
        }

        // ================================================================ live attempts & replays
        List<AttemptTracker> StartLive(IList<Entrant> who, RoundSpec spec, int round, int attemptIdx, bool teaser, string label,
            float spawnZ = -1f, float speedFrac = 0.6f)
        {
            Cam.Forced = null;
            Cam.FocusOverride = null;
            Cam.ImpactLandingZOverride = -1;
            var trackers = Session.StartAttempts(who, spec, round, attemptIdx, teaser, false, true, true, spawnZ, speedFrac);
            if (trackers.Count == 0) return trackers;
            string takeId = "take_" + trackers[0].Rec.AttemptId;
            var items = trackers.Select(t => t.Vehicle.GetComponent<Replayable>()).Concat(Session.Map.Obstacles);
            Replay.BeginTake(takeId, round, items, trackers.Select(t => t.Rec.AttemptId));
            Log.BeginSegment(SegmentKind.Live, label, Cam.CurrentShotName, takeId);
            AttemptsRun += trackers.Count;
            return trackers;
        }

        void EndLive()
        {
            Replay.EndTake();
            Log.EndSegment();
        }

        IEnumerator ReplayAttempt(AttemptRecord a, double maxSeconds, bool rebuildMap)
        {
            var take = Replay.FindTakeForAttempt(a.AttemptId);
            if (take == null || maxSeconds < 4) yield break;
            var spec = Plan.Phases.Select(p => p.Round).FirstOrDefault(r => r != null && r.Index == a.RoundIndex);
            if (rebuildMap && spec != null) Session.BuildMap(spec, Lanes, Plan.Seed);
            else Session.AbortAll("replay");
            Session.ParkAll(); // nothing may stand in the replayed vehicle's path

            bool crash = a.ImpactSpeedKmh.HasValue;
            double impact = a.LandingProgramTime ?? -1;
            double from = crash ? Math.Max(take.Start, impact - 2.2) : Math.Max(take.Start, (a.TakeoffProgramTime ?? a.StartProgramTime) - 1.5);
            double to = Math.Min(take.End, (a.LandingProgramTime ?? a.EndProgramTime) + (crash ? 2.6 : 2.2));
            float speed = crash ? 0.8f : 0.6f;
            double slowAt = impact > 0 && (crash || a.DamagePercent >= 15 || a.Outcome == AttemptOutcome.Rollover || a.BodySlam) ? impact : -1;
            while (to - from > 1.0 && ReplaySystem.Duration(from, to, speed, slowAt) > maxSeconds) to -= 0.25;
            if (to - from < 1.0) yield break;

            var items = Session.Vehicles.Values.Select(v => v.GetComponent<Replayable>()).Concat(Session.Map.Obstacles).ToList();
            var subjects = Session.Vehicles.Values.Where(v => take.Tracks.ContainsKey(v.Def.Id)).Select(v => v.transform).ToList();
            Cam.FocusOverride = subjects;
            Cam.ImpactLandingZOverride = Session.Map.LipZ + (float)(a.LandingDistanceM ?? 40.0);
            Cam.Forced = crash ? Shot.WallCam : Session.Map.DropZ > 0 ? Shot.DropCam : a.Outcome == AttemptOutcome.Rollover || a.BodySlam ? Shot.ReplayLow : Shot.ReplaySide;
            Cam.Snap();
            var def = VehicleCatalog.Find(a.VehicleId);
            Overlay.SetReplay(L($"Round {a.RoundIndex} · {def.DisplayName(Lang)}", $"Vòng {a.RoundIndex} · {def.DisplayName(Lang)}"));
            Log.BeginSegment(SegmentKind.Replay, "Replay " + a.AttemptId, Cam.Forced.ToString(), take.TakeId);
            Log.Log("ReplayShown", a.RoundIndex, a.AttemptIndex, a.VehicleId,
                string.Format(CultureInfo.InvariantCulture, "{0} [{1:0.0}–{2:0.0}s] ×{3}", take.TakeId, from, to, speed));
            replayed.Add(a.AttemptId);
            yield return StartCoroutine(Replay.Play(take, from, to, speed, items, slowAt));
            Replay.ReleaseItems(items);
            Overlay.SetReplay(null);
            Log.EndSegment();
            Cam.Forced = null;
            Cam.FocusOverride = null;
            Cam.ImpactLandingZOverride = -1;
            Session.ParkAll();
        }

        // ================================================================ helpers
        double Remaining(double deadline) => deadline - Clock.Time;

        double EstimatePass(RoundSpec spec)
        {
            int groups = spec.HeadToHead ? 1 : Plan.Entrants.Count;
            double perGroup = passDurations.Count > 0 ? passDurations.Average() : 22.0;
            return groups * perGroup;
        }

        IEnumerator WaitProgram(double seconds)
        {
            double end = Clock.Time + seconds;
            while (!abortRequested && Clock.Time < end) yield return null;
        }

        IEnumerator WaitUntilProgram(double t)
        {
            while (!abortRequested && Clock.Time < t) yield return null;
        }

        string OutcomeText(AttemptRecord a)
        {
            string name = VehicleCatalog.Find(a.VehicleId)?.DisplayName(Lang) ?? a.VehicleId;
            string d = a.LandingDistanceM.HasValue ? a.LandingDistanceM.Value.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "";
            switch (a.Outcome)
            {
                case AttemptOutcome.CleanLanding: return L($"{name}: clean landing {d}", $"{name}: tiếp đất sạch {d}");
                case AttemptOutcome.RoughLanding: return L($"{name}: rough landing {d}", $"{name}: tiếp đất mạnh {d}");
                case AttemptOutcome.Rollover: return L($"{name}: rolled over" + (d != "" ? $" after {d}" : ""), $"{name}: lật xe" + (d != "" ? $" sau {d}" : ""));
                case AttemptOutcome.FellOff: return L($"{name}: fell off", $"{name}: rơi khỏi đường");
                case AttemptOutcome.NoJump: return L($"{name}: didn't reach the lip", $"{name}: không lên tới mép dốc");
                case AttemptOutcome.Stuck: return L($"{name}: stuck — attempt failed", $"{name}: bị kẹt — lượt thất bại");
                case AttemptOutcome.Timeout: return L($"{name}: timed out", $"{name}: hết giờ");
                default: return L($"{name}: attempt stopped", $"{name}: lượt bị dừng");
            }
        }

        void UpdateScoreboard(int currentRound)
        {
            var standings = RoundScoring.ComputeStandings(Run.Rounds, Run.Attempts, Plan.Entrants, out _);
            var rows = standings.Select(s =>
            {
                var e = Plan.Entrants.First(x => x.VehicleId == s.VehicleId);
                ColorUtility.TryParseHtmlString(e.ColorHex, out var col);
                return new ShowOverlay.ScoreRow { Name = e.Def.DisplayName(Lang), Color = col, Points = s.Points };
            }).ToList();
            int total = Plan.ScoredRounds.Count();
            Overlay.SetScoreboard(currentRound >= total ? L("FINAL", "CHUNG KẾT") : L($"ROUND {currentRound}/{total - 1}", $"VÒNG {currentRound}/{total - 1}"), rows);
        }

        void OnLogged(RunEvent e)
        {
            if (Replay.IsPlaying) return;
            var tracker = Session.Active.FirstOrDefault(t => t.Rec.VehicleId == e.VehicleId);
            switch (e.Type)
            {
                case "Apex":
                    lastApexTime = Clock.Time;
                    if (tracker != null && !tracker.Rec.IsTeaser)
                    {
                        float score = (float)(e.Values.TryGetValue("heightM", out var h) ? h * 0.2 : 1) + (tracker.Spec.HeadToHead ? 1.5f : 0f)
                                      + (tracker.Spec.Role == RoundRole.Decisive ? 2f : 0f);
                        Frames.Request(tracker.Rec.AttemptId, e.Id, score);
                    }
                    break;
                case "Rollover":
                    if (tracker != null && !tracker.Rec.IsTeaser) Frames.Request(tracker.Rec.AttemptId, e.Id, 6f);
                    Cam.AddShake(0.15f);
                    break;
                case "Landed":
                    if (e.Values.TryGetValue("impactSpeed", out var v) && v > 9) Cam.AddShake(0.2f);
                    break;
                case "NearImpact":
                    lastApexTime = Clock.Time - 0.5; // teaser cliffhanger: cut just before the hit
                    break;
                case "WallCrash":
                    Cam.AddShake(0.35f);
                    if (tracker != null && !tracker.Rec.IsTeaser) Frames.Request(tracker.Rec.AttemptId, e.Id, 7f);
                    break;
            }
        }

        void OnCamOverride(Shot s)
        {
            if (State != ProjectState.Recording) return;
            string line = DescriptionComposer.Timestamp(Clock.Time) + " camera → " + s;
            Run.ManualOverrides.Add(line);
            Log.Log("ManualOverride", 0, 0, null, "camera " + s);
        }

        // ================================================================ post-production
        void Analyze()
        {
            Run.Highlights = HighlightScorer.Extract(Run, 12);
            int saved = Frames.SaveAll(RunFolder);
            foreach (var h in Run.Highlights)
            {
                var take = Replay.FindTakeForAttempt(h.AttemptId);
                h.SourceClip = take != null ? take.TakeId : "live";
                if (Frames.FileByAttempt.TryGetValue(h.AttemptId, out var f)) h.FrameFile = f;
            }
            Log.Log("FramesSaved", 0, 0, null, saved + " reference frames");
            RegenerateContent();
            // keep the raw record immediately so nothing is lost even before Export
            File.WriteAllText(Path.Combine(RunFolder, "run_record.json"), MiniJson.Serialize(Run), new System.Text.UTF8Encoding(false));
        }

        /// <summary>(Re)build the content pack from facts only. Overwrites manual edits.</summary>
        public void RegenerateContent()
        {
            if (Run == null) return;
            Pack = ContentPackService.Generate(Run);
            PackEdited = false;
            RecheckQuality();
        }

        /// <summary>Re-run truth checks after the user edited titles/description/prompts.</summary>
        public void RecheckPack()
        {
            if (Pack == null) return;
            var fs = Pack.Facts;
            Pack.Issues.Clear();
            foreach (var t in Pack.Titles)
                t.TruthCheckStatus = ClaimChecker.Status(ClaimChecker.Check("title:" + t.Category, t.Text, fs, true));
            Pack.Issues.AddRange(ClaimChecker.Check("description", Pack.Description, fs, false));
            foreach (var p in Pack.Pairs)
            {
                var issues = ClaimChecker.Check("pair" + p.PairId, p.Title + "\n" + p.ThumbnailText, fs, true);
                issues.AddRange(ClaimChecker.Check("prompt" + p.PairId, p.ThumbnailPrompt, fs, false));
                p.TruthCheckStatus = ClaimChecker.Status(issues);
                Pack.Issues.AddRange(issues);
            }
            RecheckQuality();
        }

        public void SetHumanApproval(bool approved)
        {
            HumanApproved = approved;
            if (Run != null) Log.Log("HumanReview", 0, 0, null, approved ? "owner approved after watching" : "approval withdrawn");
            RecheckQuality();
        }

        public void RecheckQuality()
        {
            if (Run == null) return;
            Quality = QualityGate.Evaluate(Run, Pack, History?.Items, HumanApproved);
            StateChanged?.Invoke();
        }

        /// <summary>Manually verify a recording (OBS manual mode, remuxed file, re-check).</summary>
        public void VerifyVideo(string path)
        {
            if (Run == null) return;
            Capture.Verify(path);
            Log.Log("CaptureVerify", 0, 0, null, $"{Run.Capture.Status}: {Run.Capture.Message}");
            if (!PackEdited) RegenerateContent(); else { Pack.IsDraft = !Run.Capture.Verified; RecheckPack(); }
        }

        public List<string> Export()
        {
            if (Run == null || Pack == null) throw new InvalidOperationException("Nothing to export yet.");
            RecheckPack();
            var files = ContentPackService.Export(RunFolder, Run, Pack, Quality);
            History?.AddAndSave(NoveltyFingerprint.FromRun(Run, Pack.Facts));
            Log.Log("Exported", 0, 0, null, files.Count + " files");
            File.WriteAllText(Path.Combine(RunFolder, "run_record.json"), MiniJson.Serialize(Run), new System.Text.UTF8Encoding(false));
            SetState(ProjectState.Exported);
            return files;
        }
    }
}
