using System;
using System.Collections.Generic;
using System.Linq;

namespace Mylti.Domain
{
    public sealed class QualityCheck : IJsonWritable
    {
        public string Id;
        public string Label;
        public CheckLevel Level;
        public string Detail;

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("id", Id).Prop("label", Label).Prop("level", Level.ToString()).Prop("detail", Detail).EndObject();
        }
    }

    /// <summary>Configurable heuristics — NOT YouTube rules (spec §22.3).</summary>
    public sealed class QualityThresholds
    {
        public int MinMeaningfulBeats = 5;
        public double MaxFillerRatio = 0.45;     // non-live share: cards + replays + summary
        public double MaxDeadAirTotal = 20;      // seconds of stationary live footage
        public double MaxSingleDeadAir = 8;
        public double MaxNoveltySimilarity = 0.85;
        public double HookWindowSeconds = 20;
        public bool RequireAudio = true;
    }

    public sealed class QualityReport : IJsonWritable
    {
        public string RunId;
        public ReadinessLevel Readiness;
        public bool HumanApproved;
        public List<QualityCheck> Checks = new List<QualityCheck>();
        public string MostSimilarEpisode;
        public double MostSimilarScore;
        public List<string> NoveltyDifferences = new List<string>();

        public bool HasFail => Checks.Any(c => c.Level == CheckLevel.Fail);

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("runId", RunId).Prop("readiness", Readiness.ToString()).Prop("humanApproved", HumanApproved)
                .Prop("mostSimilarEpisode", MostSimilarEpisode).Prop("mostSimilarScore", Math.Round(MostSimilarScore, 3))
                .PropStrings("noveltyDifferences", NoveltyDifferences);
            w.Key("checks"); w.Value(Checks);
            w.Prop("note", "Heuristic production checks only. They do not predict views and are not YouTube policy decisions.");
            w.EndObject();
        }
    }

    /// <summary>ContentQualityGate + PublishReadinessReport (spec §20, §22, §26, §29).</summary>
    public static class QualityGate
    {
        public static QualityReport Evaluate(RunRecord run, ContentPack pack, IList<NoveltyFingerprint> history,
            bool humanApproved, QualityThresholds t = null)
        {
            t = t ?? new QualityThresholds();
            var rep = new QualityReport { RunId = run.RunId, HumanApproved = humanApproved };
            var plan = run.Plan;

            // 1. Simulation
            Add(rep, "sim", "Simulation finished", run.ChallengeFinished ? CheckLevel.Pass : CheckLevel.Fail,
                run.ChallengeFinished ? "All phases ran." : "Run stopped before the end.");

            // 2. Media
            var cap = run.Capture;
            CheckLevel mediaLevel;
            string mediaDetail;
            switch (cap.Status)
            {
                case CaptureStatus.CaptureVerified:
                    mediaLevel = CheckLevel.Pass;
                    mediaDetail = $"{cap.Video?.Path} — {cap.Video?.DurationSeconds:0.00}s (target {cap.TargetSeconds}±{cap.ToleranceSeconds}s)";
                    break;
                case CaptureStatus.CapturePending:
                case CaptureStatus.Stopped:
                case CaptureStatus.NotStarted:
                    mediaLevel = CheckLevel.Pending; mediaDetail = $"No verified video yet ({cap.Status})."; break;
                default:
                    mediaLevel = CheckLevel.Fail; mediaDetail = $"{cap.Status}: {cap.Message}"; break;
            }
            Add(rep, "media", "Video file verified (exists + duration)", mediaLevel, mediaDetail);
            if (cap.Interrupted) Add(rep, "interrupt", "Uninterrupted take", CheckLevel.Fail, cap.InterruptReason ?? "Take was interrupted — re-validate or re-record.");

            if (cap.Verified)
            {
                if (cap.Video?.HasAudioTrack == false)
                    Add(rep, "audio", "Audio track present", t.RequireAudio ? CheckLevel.Warn : CheckLevel.Pass, "The file has no audio track.");
                else if (cap.Video?.HasAudioTrack == true)
                    Add(rep, "audio", "Audio track present", CheckLevel.Pass, "Audio track found (listen to confirm level/clipping).");
                else
                    Add(rep, "audio", "Audio track present", CheckLevel.Pending, "Container did not expose track info.");
            }

            // 3. Meaningful beats: completed rounds with a real outcome and a declared change.
            int beats = 0;
            foreach (var r in run.Rounds.Where(x => x.Completed))
            {
                var spec = plan?.ScoredRounds.FirstOrDefault(s => s.Index == r.RoundIndex);
                bool hasOutcome = run.Attempts.Any(a => a.RoundIndex == r.RoundIndex && !a.IsTeaser && a.Outcome != AttemptOutcome.None && a.Outcome != AttemptOutcome.Aborted);
                bool changed = spec != null && !spec.ChangeSummary.Contains("no meaningful change") && !spec.ChangeSummary.Contains("không có thay đổi");
                if (hasOutcome && changed) beats++;
            }
            Add(rep, "beats", $"≥{t.MinMeaningfulBeats} meaningful beats", beats >= t.MinMeaningfulBeats ? CheckLevel.Pass : CheckLevel.Fail, $"{beats} rounds with a real outcome and a meaningful change.");

            // 4. Hook honesty: the cold open must contain a real takeoff from the teaser take.
            var hookEvent = run.Events.FirstOrDefault(e => e.Type == "Takeoff" && e.RoundIndex == 0 && e.ProgramTime <= t.HookWindowSeconds);
            Add(rep, "hook", "Hook uses real footage in the first seconds", hookEvent != null ? CheckLevel.Pass : CheckLevel.Fail,
                hookEvent != null ? $"Teaser takeoff event #{hookEvent.Id} ({hookEvent.VehicleId}) at {hookEvent.ProgramTime:0.0}s in the live take." : "No real teaser takeoff found in the hook window.");

            // 5. Payoff: the final must have happened and the viewer question must get an answer.
            var fin = run.Rounds.FirstOrDefault(r => r.IsFinal);
            CheckLevel payoff = fin == null || !fin.Completed ? CheckLevel.Fail : CheckLevel.Pass;
            string payoffDetail = fin == null ? "Final round missing." : fin.Explanation;
            if (payoff == CheckLevel.Pass && fin.WinnerVehicleId == null)
            {
                payoff = CheckLevel.Warn;
                payoffDetail += " (No winner in the final — make sure the title/description answer the viewer question honestly, or add another run.)";
            }
            Add(rep, "payoff", "Final answers the viewer question", payoff, payoffDetail);

            // 6. Filler & dead air
            double program = Math.Max(1, run.ProgramDurationSeconds);
            double filler = run.Segments.Where(s => s.Kind == SegmentKind.Card || s.Kind == SegmentKind.Replay || s.Kind == SegmentKind.Summary).Sum(s => s.Duration);
            double ratio = filler / program;
            Add(rep, "filler", "Non-live share (cards, replays, summary)", ratio <= t.MaxFillerRatio ? CheckLevel.Pass : CheckLevel.Fail,
                $"{ratio:P0} of the program (limit {t.MaxFillerRatio:P0}).");
            var dead = run.Segments.Where(s => s.Kind == SegmentKind.DeadAir).ToList();
            double deadTotal = dead.Sum(s => s.Duration);
            double deadMax = dead.Count == 0 ? 0 : dead.Max(s => s.Duration);
            var deadLevel = deadTotal > t.MaxDeadAirTotal * 3 ? CheckLevel.Fail : (deadTotal > t.MaxDeadAirTotal || deadMax > t.MaxSingleDeadAir) ? CheckLevel.Warn : CheckLevel.Pass;
            Add(rep, "deadair", "Dead air (stationary/stuck footage)", deadLevel, $"{deadTotal:0.0}s total, longest {deadMax:0.0}s. Suggest cutting segments listed in shot_list.csv (kind=DeadAir).");

            int stuck = run.Attempts.Count(a => a.Outcome == AttemptOutcome.Stuck || a.Outcome == AttemptOutcome.Timeout);
            int retries = run.Attempts.Sum(a => a.Retries);
            Add(rep, "stuck", "Stuck / timeout attempts", stuck == 0 ? CheckLevel.Pass : stuck <= 2 ? CheckLevel.Warn : CheckLevel.Fail, $"{stuck} stuck/timeout attempts, {retries} auto-retries.");

            // 7. Truthfulness of metadata
            if (pack != null)
            {
                int fails = pack.Issues.Count(i => i.Level == CheckLevel.Fail) + pack.Titles.Count(x => x.TruthCheckStatus != "Pass");
                Add(rep, "truth", "Titles/description/prompts backed by facts", fails == 0 ? CheckLevel.Pass : CheckLevel.Fail,
                    fails == 0 ? "All claims trace back to the fact sheet." : string.Join(" | ", pack.Issues.Where(i => i.Level == CheckLevel.Fail).Take(4).Select(i => i.ToString())));
                if (!string.IsNullOrEmpty(pack.WorkingTitleCheck) && pack.WorkingTitleCheck != "Pass")
                    Add(rep, "workingtitle", "Pre-run working title still honest", CheckLevel.Warn, $"Rewrite suggested: {pack.WorkingTitleSuggestion}");
            }

            // 8. Novelty / originality
            if (history != null && history.Count > 0)
            {
                var fp = NoveltyFingerprint.FromRun(run, pack?.Facts);
                var others = history.Where(h => h.EpisodeId != run.RunId).ToList();
                if (others.Count > 0)
                {
                    var best = others.OrderByDescending(h => fp.Similarity(h)).First();
                    rep.MostSimilarEpisode = best.EpisodeId;
                    rep.MostSimilarScore = fp.Similarity(best);
                    rep.NoveltyDifferences = fp.Differences(best);
                    Add(rep, "novelty", "Different enough from previous episodes",
                        rep.MostSimilarScore > t.MaxNoveltySimilarity ? CheckLevel.Warn : CheckLevel.Pass,
                        $"Most similar: {best.EpisodeId} ({rep.MostSimilarScore:P0}). Differences: {string.Join(", ", rep.NoveltyDifferences.Take(6))}");
                }
            }
            else Add(rep, "novelty", "Different enough from previous episodes", CheckLevel.Pass, "No previous episodes recorded.");

            // Readiness — never inferred upward without evidence.
            bool footageProblem = rep.Checks.Any(c => (c.Id == "beats" || c.Id == "filler" || c.Id == "hook" || c.Id == "payoff" || c.Id == "stuck" || c.Id == "deadair") && c.Level == CheckLevel.Fail);
            if (!run.ChallengeFinished) rep.Readiness = ReadinessLevel.NotRun;
            else if (footageProblem) rep.Readiness = ReadinessLevel.NeedsMoreFootage;
            else if (!cap.Verified || cap.Interrupted) rep.Readiness = ReadinessLevel.SimulationComplete;
            else if (rep.HasFail) rep.Readiness = ReadinessLevel.MediaReady;
            else if (!humanApproved) rep.Readiness = ReadinessLevel.ReadyForReview;
            else rep.Readiness = ReadinessLevel.PublishReady;
            return rep;
        }

        static void Add(QualityReport r, string id, string label, CheckLevel level, string detail)
            => r.Checks.Add(new QualityCheck { Id = id, Label = label, Level = level, Detail = detail });
    }
}
