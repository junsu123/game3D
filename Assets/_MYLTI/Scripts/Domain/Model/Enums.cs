namespace Mylti.Domain
{
    /// <summary>Project-level state (spec §3).</summary>
    public enum ProjectState
    {
        Setup, PlanPreview, CaptureReady, Recording, Finalizing, Analyzing, ContentReady, Exported,
        RecordingFailed, Incomplete
    }

    /// <summary>State of the actual video file — never inferred from the simulation alone (FR-14).</summary>
    public enum CaptureStatus
    {
        NotStarted,
        CapturePending,   // simulation ran, no file confirmed yet
        Recording,
        Stopped,          // recorder stopped, file not yet inspected
        VideoMissing,     // expected file does not exist
        InvalidDuration,  // file exists, duration outside tolerance
        DurationUnknown,  // file exists, container could not be parsed
        Interrupted,      // pause / focus loss / recorder error during the take
        CaptureVerified,  // file exists and duration within tolerance
        Failed
    }

    /// <summary>Readiness levels (spec §20). PublishReady always needs explicit human approval.</summary>
    public enum ReadinessLevel
    {
        NotRun, SimulationComplete, MediaReady, NeedsMoreFootage, ReadyForReview, PublishReady
    }

    public enum CaptureBackend
    {
        None,            // simulation only — content pack will be a draft
        UnityRecorder,   // Editor only (com.unity.recorder installed)
        ObsWebSocket,    // OBS 28+ built-in WebSocket server (opt-in)
        ObsManual        // user starts/stops OBS by hand, then points to the file
    }

    public enum DriveMode { AI, Manual }

    public enum AttemptOutcome
    {
        None, CleanLanding, RoughLanding, Rollover, NoJump, FellOff, Stuck, Timeout, Aborted
    }

    public enum ScoringRule
    {
        FurthestValidLanding,  // longest landing that ends upright
        CleanLandingOnly,      // only landings with no heavy body slam count; then distance
        TargetZone,            // valid landing closest to a target distance
        BeyondTheWall          // must land beyond a crate wall; then distance
    }

    public enum ObstacleSet
    {
        None, BarrelLine, CrateWall, BarrelsAndWall
    }

    public enum PhaseKind
    {
        ColdOpen, Intro, Round, Final, Summary
    }

    public enum RoundRole
    {
        Baseline, Complication, Reversal, Escalation, Extreme, Decisive
    }

    public enum SegmentKind
    {
        Live, Replay, Card, Intro, Summary, DeadAir
    }

    public enum CheckLevel { Pass, Warn, Fail, Pending }

    public enum ContentLanguage { en, vi }
}
