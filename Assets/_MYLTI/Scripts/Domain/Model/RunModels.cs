using System;
using System.Collections.Generic;
using System.Linq;

namespace Mylti.Domain
{
    /// <summary>A vehicle taking part in a run.</summary>
    public sealed class Entrant : IJsonWritable
    {
        public string VehicleId;
        public string ColorHex;
        public int Lane;
        public DriveMode Mode = DriveMode.AI;
        public float TargetSpeedKmh;

        public VehicleDefinition Def => VehicleCatalog.Find(VehicleId);

        public Entrant Clone() => (Entrant)MemberwiseClone();

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject()
                .Prop("vehicleId", VehicleId).Prop("colorHex", ColorHex).Prop("lane", Lane)
                .Prop("mode", Mode.ToString()).Prop("targetSpeedKmh", TargetSpeedKmh)
                .EndObject();
        }
    }

    /// <summary>Geometry + rules of one challenge configuration (a round, the teaser, or the sandbox).</summary>
    public sealed class RoundSpec : IJsonWritable
    {
        public int Index;                 // 1-based round number; 0 = teaser/sandbox
        public RoundRole Role;
        public string Title = "";
        public string ChangeSummary = ""; // the meaningful variable that changed vs the previous round
        public string SubQuestion = "";   // small open loop for this round
        public ScoringRule Rule = ScoringRule.FurthestValidLanding;

        public float KickerHeight = 8f;   // m
        public float KickerAngleDeg = 20f;
        public float SpeedFactor = 1f;    // multiplies each vehicle's target speed
        public float RunwayLength = 260f; // m from start line to ramp foot
        public float BridgeWidth = 0f;    // 0 = no narrow bridge section
        public bool LandingRamp = false;
        public ObstacleSet Obstacles = ObstacleSet.None;
        public float WallDistance = 70f;  // m from lip (crate wall)
        public float TargetDistance = 60f; // m from lip (target zone)
        public float CrashSpeedKmh = 80f;  // CrashWall: AI speed into the barrier
        public float DropHeightM = 20f;    // DropTest: crane height

        /// <summary>Jump rounds use the ramp; crash-wall and drop-test rounds do not.</summary>
        public bool IsJump => Rule != ScoringRule.CrashWall && Rule != ScoringRule.DropTest;
        public bool DamageScored => Rule == ScoringRule.LeastDamage || Rule == ScoringRule.CrashWall || Rule == ScoringRule.DropTest;

        public bool HeadToHead;           // all vehicles start together
        public int AttemptPasses = 1;     // mandatory passes (each pass = one attempt per vehicle)
        public int MaxExtraPasses = 1;    // optional passes the pacing engine may add (applied to everyone)
        public float BudgetSeconds = 75f;
        public bool Scored = true;

        public RoundSpec Clone() => (RoundSpec)MemberwiseClone();

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject()
                .Prop("index", Index).Prop("role", Role.ToString()).Prop("title", Title)
                .Prop("changeSummary", ChangeSummary).Prop("subQuestion", SubQuestion)
                .Prop("rule", Rule.ToString())
                .Prop("kickerHeightMeters", KickerHeight).Prop("kickerAngleDegrees", KickerAngleDeg)
                .Prop("speedFactor", SpeedFactor).Prop("runwayLengthMeters", RunwayLength)
                .Prop("bridgeWidthMeters", BridgeWidth).Prop("landingRamp", LandingRamp)
                .Prop("obstacles", Obstacles.ToString()).Prop("wallDistanceMeters", WallDistance)
                .Prop("targetDistanceMeters", TargetDistance).Prop("crashSpeedKmh", CrashSpeedKmh).Prop("dropHeightMeters", DropHeightM).Prop("headToHead", HeadToHead)
                .Prop("attemptPasses", AttemptPasses).Prop("maxExtraPasses", MaxExtraPasses)
                .Prop("budgetSeconds", BudgetSeconds).Prop("scored", Scored)
                .EndObject();
        }
    }

    public sealed class PhaseSpec : IJsonWritable
    {
        public PhaseKind Kind;
        public string Label;
        public double PlannedStart;
        public double PlannedDuration;
        public RoundSpec Round; // for Round/Final (and the map used by ColdOpen)

        public double PlannedEnd => PlannedStart + PlannedDuration;

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("kind", Kind.ToString()).Prop("label", Label)
                .Prop("plannedStart", PlannedStart).Prop("plannedDuration", PlannedDuration);
            w.Key("round"); w.Value(Round);
            w.EndObject();
        }
    }

    /// <summary>Concept brief (spec §21.2) — the viewer promise that drives a run.</summary>
    public sealed class ConceptBrief : IJsonWritable
    {
        public string ConceptId;
        public string SeriesId;
        public string ViewerQuestion;
        public string ViewerPromise;
        public string CentralConflict;
        public string Stakes;
        public string KeyVisual;
        public string WorkingTitle;
        public string CandidateThumbnailShot;
        public string TwistKind;          // "obstacles" | "bridge" | "target" | "wall" | "speed"
        public string UnexpectedPossibleOutcome;
        public List<string> Rules = new List<string>();
        public List<string> NoveltyTags = new List<string>();
        public Dictionary<string, double> Scores = new Dictionary<string, double>();

        public double TotalScore => Scores.Count == 0 ? 0 : Scores.Values.Average();

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject()
                .Prop("conceptId", ConceptId).Prop("seriesId", SeriesId)
                .Prop("viewerQuestion", ViewerQuestion).Prop("viewerPromise", ViewerPromise)
                .Prop("centralConflict", CentralConflict).Prop("stakes", Stakes)
                .Prop("keyVisual", KeyVisual).Prop("workingTitle", WorkingTitle)
                .Prop("candidateThumbnailShot", CandidateThumbnailShot).Prop("twistKind", TwistKind)
                .Prop("unexpectedPossibleOutcome", UnexpectedPossibleOutcome)
                .PropStrings("rules", Rules).PropStrings("noveltyTags", NoveltyTags);
            w.Key("conceptScore(heuristic,not_a_view_forecast)"); w.Value(Scores);
            w.EndObject();
        }
    }

    public sealed class RunPlan : IJsonWritable
    {
        public string PlanId;
        public int Seed;
        public double TargetDurationSeconds = 600;
        public ContentLanguage Language = ContentLanguage.en;
        public string Difficulty = "Progressive";
        public ConceptBrief Concept;
        public List<Entrant> Entrants = new List<Entrant>();
        public List<PhaseSpec> Phases = new List<PhaseSpec>();

        public IEnumerable<RoundSpec> ScoredRounds =>
            Phases.Where(p => (p.Kind == PhaseKind.Round || p.Kind == PhaseKind.Final) && p.Round != null && p.Round.Scored)
                  .Select(p => p.Round);

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("planId", PlanId).Prop("seed", Seed)
                .Prop("targetDurationSeconds", TargetDurationSeconds)
                .Prop("language", Language.ToString()).Prop("difficulty", Difficulty);
            w.Key("concept"); w.Value(Concept);
            w.Key("entrants"); w.Value(Entrants);
            w.Key("phases"); w.Value(Phases);
            w.EndObject();
        }
    }

    /// <summary>One timestamped telemetry event. programTime == position in the final single-take video.</summary>
    public sealed class RunEvent : IJsonWritable
    {
        public int Id;
        public string Type;
        public double ProgramTime;
        public double SimTime;
        public int RoundIndex;
        public int AttemptIndex;
        public string VehicleId;
        public string Note;
        public Dictionary<string, double> Values = new Dictionary<string, double>();

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("eventId", Id).Prop("type", Type)
                .Prop("programTime", Math.Round(ProgramTime, 3)).Prop("simTime", Math.Round(SimTime, 3))
                .Prop("round", RoundIndex).Prop("attempt", AttemptIndex)
                .Prop("vehicleId", VehicleId).Prop("note", Note);
            w.Key("values"); w.Value(Values);
            w.EndObject();
        }
    }

    public sealed class AttemptRecord : IJsonWritable
    {
        public string AttemptId;
        public int RoundIndex;
        public int AttemptIndex;
        public string VehicleId;
        public bool IsTeaser;
        public bool IsBonus;
        public bool Scored = true;
        public AttemptOutcome Outcome = AttemptOutcome.None;
        public double StartProgramTime, EndProgramTime;
        public double? TakeoffProgramTime, LandingProgramTime;
        public double MaxSpeedKmh;
        public double TakeoffSpeedKmh;
        public double PeakHeightM;
        public double AirTimeS;
        public double? LandingDistanceM;
        public double LandingImpactSpeed;   // vertical m/s at touchdown
        public bool BodySlam;                // chassis hit the ground hard on landing
        public int ObstacleHits;
        public int Retries;
        public double ClearedWall = -1;      // -1 n/a, 0 no, 1 yes
        public double? TargetError;          // |landing - target| for TargetZone rule
        public double? DamagePercent;        // body damage 0–100 at the end of the attempt (game estimate from impacts)
        public double? ImpactSpeedKmh;       // CrashWall: speed when hitting the barrier
        public int WheelsLost;
        public bool GlassBroken;
        public List<int> EventIds = new List<int>();

        public bool ValidLanding => Outcome == AttemptOutcome.CleanLanding || Outcome == AttemptOutcome.RoughLanding;
        public bool Crashed => Outcome == AttemptOutcome.Rollover || Outcome == AttemptOutcome.FellOff;

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("attemptId", AttemptId).Prop("round", RoundIndex).Prop("attempt", AttemptIndex)
                .Prop("vehicleId", VehicleId).Prop("isTeaser", IsTeaser).Prop("isBonus", IsBonus).Prop("scored", Scored)
                .Prop("outcome", Outcome.ToString())
                .Prop("startProgramTime", Math.Round(StartProgramTime, 2)).Prop("endProgramTime", Math.Round(EndProgramTime, 2))
                .Prop("takeoffProgramTime", TakeoffProgramTime).Prop("landingProgramTime", LandingProgramTime)
                .Prop("maxSpeedKmh", Math.Round(MaxSpeedKmh, 1)).Prop("takeoffSpeedKmh", Math.Round(TakeoffSpeedKmh, 1))
                .Prop("peakHeightMeters", Math.Round(PeakHeightM, 1)).Prop("airTimeSeconds", Math.Round(AirTimeS, 2))
                .Prop("landingDistanceMeters", LandingDistanceM.HasValue ? Math.Round(LandingDistanceM.Value, 1) : (double?)null)
                .Prop("landingImpactSpeed", Math.Round(LandingImpactSpeed, 1)).Prop("bodySlam", BodySlam)
                .Prop("obstacleHits", ObstacleHits).Prop("retries", Retries).Prop("clearedWall", ClearedWall)
                .Prop("targetError", TargetError).Prop("damagePercent", DamagePercent.HasValue ? Math.Round(DamagePercent.Value, 1) : (double?)null)
                .Prop("impactSpeedKmh", ImpactSpeedKmh.HasValue ? Math.Round(ImpactSpeedKmh.Value, 0) : (double?)null)
                .Prop("wheelsLost", WheelsLost).Prop("glassBroken", GlassBroken);
            w.Key("eventIds"); w.Value(EventIds);
            w.EndObject();
        }
    }

    public sealed class RoundResult : IJsonWritable
    {
        public int RoundIndex;
        public string Title;
        public ScoringRule Rule;
        public bool IsFinal;
        public string WinnerVehicleId;          // null = no valid winner / tie
        public string Explanation;              // human-readable, generated from data
        public Dictionary<string, double> BestMetric = new Dictionary<string, double>(); // per vehicle; NaN-free
        public List<string> AttemptIds = new List<string>();
        public bool Completed;

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("index", RoundIndex).Prop("title", Title).Prop("rule", Rule.ToString())
                .Prop("isFinal", IsFinal).Prop("winnerVehicleId", WinnerVehicleId)
                .Prop("explanation", Explanation).Prop("completed", Completed);
            w.Key("bestMetricByVehicle"); w.Value(BestMetric);
            w.PropStrings("attemptIds", AttemptIds);
            w.EndObject();
        }
    }

    public sealed class Highlight : IJsonWritable
    {
        public string HighlightId;
        public int EventId;
        public string AttemptId;
        public string VehicleId;
        public int RoundIndex;
        public double Start, End;        // program time (== media time for a verified single take)
        public double Score;
        public string Camera;
        public string Reason;
        public string SourceClip;        // take id, used for replays/EDL
        public string FrameFile;         // reference PNG (if captured)

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("highlightId", HighlightId).Prop("eventId", EventId).Prop("attemptId", AttemptId)
                .Prop("vehicleId", VehicleId).Prop("round", RoundIndex)
                .Prop("start", Math.Round(Start, 2)).Prop("end", Math.Round(End, 2)).Prop("score", Math.Round(Score, 2))
                .Prop("camera", Camera).Prop("reason", Reason).Prop("sourceClip", SourceClip).Prop("frameFile", FrameFile)
                .EndObject();
        }
    }

    public sealed class SegmentMarker : IJsonWritable
    {
        public SegmentKind Kind;
        public double Start, End;
        public string Label;
        public string Camera;
        public string SourceClip;

        public double Duration => Math.Max(0, End - Start);

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("kind", Kind.ToString()).Prop("start", Math.Round(Start, 2)).Prop("end", Math.Round(End, 2))
                .Prop("label", Label).Prop("camera", Camera).Prop("sourceClip", SourceClip).EndObject();
        }
    }

    public sealed class ChapterMarker
    {
        public double Time;
        public string Label;
    }

    /// <summary>Facts about the media file — filled only by inspecting the real file.</summary>
    public sealed class VideoInfo : IJsonWritable
    {
        public string Path;
        public bool Exists;
        public long SizeBytes;
        public string Container;
        public double? DurationSeconds;
        public bool? HasVideoTrack;
        public bool? HasAudioTrack;
        public long? VideoFrameCount;
        public double? VideoFps;
        public string Error;

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("path", Path).Prop("exists", Exists).Prop("sizeBytes", SizeBytes)
                .Prop("container", Container).Prop("durationSeconds", DurationSeconds);
            w.Key("hasVideoTrack"); if (HasVideoTrack.HasValue) w.Bool(HasVideoTrack.Value); else w.Null();
            w.Key("hasAudioTrack"); if (HasAudioTrack.HasValue) w.Bool(HasAudioTrack.Value); else w.Null();
            w.Key("videoFrameCount"); if (VideoFrameCount.HasValue) w.Num(VideoFrameCount.Value); else w.Null();
            w.Prop("videoFps", VideoFps).Prop("decoderCheck", "not performed (no decoder in app) — open the file to confirm playback")
                .Prop("error", Error).EndObject();
        }
    }

    public sealed class CaptureInfo : IJsonWritable
    {
        public CaptureBackend Backend = CaptureBackend.None;
        public CaptureStatus Status = CaptureStatus.NotStarted;
        public string ExpectedPath;
        public double TargetSeconds = 600;
        public double ToleranceSeconds = 2;
        public int Fps = 30;
        public bool Interrupted;
        public string InterruptReason;
        public string Message;
        public VideoInfo Video;

        public bool Verified => Status == CaptureStatus.CaptureVerified;

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("backend", Backend.ToString()).Prop("status", Status.ToString())
                .Prop("expectedPath", ExpectedPath).Prop("targetSeconds", TargetSeconds)
                .Prop("toleranceSeconds", ToleranceSeconds).Prop("fps", Fps)
                .Prop("interrupted", Interrupted).Prop("interruptReason", InterruptReason).Prop("message", Message);
            w.Key("video"); w.Value(Video);
            w.EndObject();
        }
    }

    /// <summary>Everything that happened in one run. Content generation reads ONLY from here.</summary>
    public sealed class RunRecord : IJsonWritable
    {
        public int SchemaVersion = 1;
        public string RunId;
        public string StartedAtIso;
        public RunPlan Plan;
        public bool ChallengeFinished;
        public double ProgramDurationSeconds;
        public string MapType = "MegaRamp";
        public List<RunEvent> Events = new List<RunEvent>();
        public List<AttemptRecord> Attempts = new List<AttemptRecord>();
        public List<RoundResult> Rounds = new List<RoundResult>();
        public List<Highlight> Highlights = new List<Highlight>();
        public List<SegmentMarker> Segments = new List<SegmentMarker>();
        public List<string> ManualOverrides = new List<string>();
        public CaptureInfo Capture = new CaptureInfo();
        public bool AudioEnabled = true;

        public RunEvent FindEvent(int id) => Events.FirstOrDefault(e => e.Id == id);

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("schemaVersion", SchemaVersion).Prop("runId", RunId).Prop("startedAt", StartedAtIso)
                .Prop("challengeFinished", ChallengeFinished).Prop("programDurationSeconds", Math.Round(ProgramDurationSeconds, 3))
                .Prop("mapType", MapType).Prop("audioEnabled", AudioEnabled);
            w.Key("capture"); w.Value(Capture);
            w.Key("plan"); w.Value(Plan);
            w.Key("rounds"); w.Value(Rounds);
            w.Key("attempts"); w.Value(Attempts);
            w.Key("highlights"); w.Value(Highlights);
            w.Key("segments"); w.Value(Segments);
            w.PropStrings("manualOverrides", ManualOverrides);
            w.EndObject();
        }
    }
}
