using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    public enum UiMode { Creator, Capture }

    /// <summary>
    /// Composition root. Put ONE GameObject with this component in the scene (MYLTI ▸ Setup Demo Scene does it);
    /// everything else (arena, vehicles, camera rig, director, UI) is created at runtime.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class StudioApp : MonoBehaviour
    {
        public static StudioApp Instance { get; private set; }

        // systems
        public ProgramClock Clock { get; private set; }
        public EventLogger Log { get; private set; }
        public MegaRampBuilder Builder { get; private set; }
        public ChallengeSession Session { get; private set; }
        public CameraDirector Cam { get; private set; }
        public ReplaySystem Replay { get; private set; }
        public CaptureCoordinator Capture { get; private set; }
        public HighlightFrameCapture Frames { get; private set; }
        public ShowOverlay Overlay { get; private set; }
        public TenMinuteDirector Director { get; private set; }
        public FfmpegTool Ffmpeg { get; private set; }
        public BuiltInRecorder Recorder { get; private set; }
        public EpisodeProducer Producer { get; private set; }
        public EpisodeLog Episodes { get; private set; }
        public SceneLook Look { get; private set; }
        public VfxDirector Vfx { get; private set; }

        // data
        public string DataRoot { get; private set; }
        public string RunsRoot { get; private set; }
        public string SettingsPath { get; private set; }
        public AppSettings Settings { get; private set; }
        public PresetRepository Presets { get; private set; }
        public NoveltyHistory History { get; private set; }
        public ScenarioConfig Config { get; set; }
        public List<string> Messages { get; } = new List<string>();

        // concept lab / plan
        public List<ConceptBrief> Concepts = new List<ConceptBrief>();
        public int SelectedConcept = -1;
        public RunPlan Plan;
        public List<string> PlanWarnings = new List<string>();

        // ui
        public UiMode Mode = UiMode.Creator;
        public bool HudVisible = true;
        public RunRecord SandboxRun { get; private set; }
        public List<AttemptRecord> LastSandboxResults = new List<AttemptRecord>();

        /// <summary>
        /// Zero-setup start: pressing Play in ANY scene (e.g. the template's SampleScene) launches the studio.
        /// If the scene already contains a StudioApp, nothing extra is created.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBootstrap()
        {
            if (Instance != null || FindAnyObjectByType<StudioApp>() != null) return;
            new GameObject("MYLTI_Studio").AddComponent<StudioApp>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            Application.runInBackground = true;
            Time.fixedDeltaTime = 0.02f; // spec §10 baseline; profile before changing

            DataRoot = Application.persistentDataPath;
            RunsRoot = Path.Combine(DataRoot, "Runs");
            SettingsPath = Path.Combine(DataRoot, "settings.json");
            Directory.CreateDirectory(RunsRoot);
            Settings = AppSettings.Load(SettingsPath, out string settingsError);
            if (settingsError != null) Msg(settingsError);
            Presets = new PresetRepository(Path.Combine(DataRoot, "Presets"));
            History = new NoveltyHistory(Path.Combine(DataRoot, "episode_history.json"));
            if (History.LoadError != null) Msg(History.LoadError);
            Config = ScenarioConfig.CreateDefault();
            Config.Language = Settings.Language;

            EnsureLighting();
            Clock = gameObject.AddComponent<ProgramClock>();
            Log = new EventLogger(Clock);
            Builder = new GameObject("Arena").AddComponent<MegaRampBuilder>();
            Session = new GameObject("ChallengeSession").AddComponent<ChallengeSession>();
            Session.Builder = Builder;
            Session.Log = Log;
            Session.AudioEnabled = Settings.EnableAudio;
            Session.StuckSeconds = Config.AutoRecoverStuckSeconds;

            var camGo = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            if (camGo.GetComponent<AudioListener>() == null) camGo.AddComponent<AudioListener>();
            Cam = camGo.GetComponent<CameraDirector>();
            if (Cam == null) Cam = camGo.AddComponent<CameraDirector>(); // explicit check: Unity's fake-null breaks '??'
            Cam.Session = Session;
            Cam.Requested = Shot.Auto;

            Replay = gameObject.AddComponent<ReplaySystem>();
            Replay.Clock = Clock;
            Ffmpeg = new FfmpegTool(Path.Combine(DataRoot, "ffmpeg"), Settings.FfmpegPath);
            TtsFactory.KokoroDir = Path.Combine(DataRoot, "tts");
            Nameplate.Enabled = Settings.ShowNameplates;
            Recorder = gameObject.AddComponent<BuiltInRecorder>();
            Recorder.Cam = Cam.GetComponent<Camera>();
            Recorder.Ffmpeg = Ffmpeg;
            Recorder.FlipVertical = Settings.FlipVertical;
            Episodes = new EpisodeLog(Path.Combine(DataRoot, "episodes_log.json"));
            Capture = gameObject.AddComponent<CaptureCoordinator>();
            Capture.Settings = Settings;
            Capture.Recorder = Recorder;
            Frames = gameObject.AddComponent<HighlightFrameCapture>();
            Overlay = gameObject.AddComponent<ShowOverlay>();
            Director = gameObject.AddComponent<TenMinuteDirector>();
            Director.Session = Session; Director.Cam = Cam; Director.Clock = Clock; Director.Log = Log;
            Director.Replay = Replay; Director.Capture = Capture; Director.Frames = Frames; Director.Overlay = Overlay;
            Director.History = History; Director.Settings = Settings; Director.RunsRoot = RunsRoot;
            Director.StateChanged += OnDirectorState;
            Producer = gameObject.AddComponent<EpisodeProducer>();
            Producer.App = this;
            Producer.Ffmpeg = Ffmpeg;
            Producer.Episodes = Episodes;
            gameObject.AddComponent<StudioUI>();

            Look = gameObject.AddComponent<SceneLook>();
            Look.Cam = Cam.GetComponent<Camera>();
            Vfx = new GameObject("VFX").AddComponent<VfxDirector>();
            Vfx.Session = Session;
            Vfx.Attach(Log);
            ApplyQuality(Settings.Quality);
            VehicleAudio.MasterVolume = Settings.EnableAudio ? 0.8f : 0f;
        }

        void Start()
        {
            NewSandboxRecord();
            Session.BuildMap(Config.Sandbox, Config.Entrants.Count, Config.Seed);
            Session.EnsureVehicles(Config.Entrants);
            Session.LineUp(Config.Entrants);
            Cam.Requested = Shot.Auto;
            Overlay.CardsEnabled = Settings.ShowCardsInVideo;
            Overlay.ScoreboardEnabled = Settings.ShowScoreboardInVideo;
            Msg("Sẵn sàng. Bấm ▶ AUTO EPISODE (tab đầu tiên) để Claude viết kịch bản, tự quay và xuất video 10:00 có lời bình.");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        void EnsureLighting()
        {
            if (FindAnyObjectByType<Light>() == null) // SceneLook then tunes sun, ambient, fog and post-processing
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.25f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            }
        }

        public void ApplyQuality(string level)
        {
            string[] names = QualitySettings.names;
            int idx = Array.FindIndex(names, n => n.Equals(level, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = level == "Low" ? 0 : level == "Medium" ? Math.Min(1, names.Length - 1) : names.Length - 1;
            if (idx >= 0 && idx < names.Length) QualitySettings.SetQualityLevel(idx, true);
            if (Look != null) Look.Apply(level);
        }

        readonly System.Collections.Concurrent.ConcurrentQueue<string> pendingMessages = new System.Collections.Concurrent.ConcurrentQueue<string>();
        int mainThreadId;

        /// <summary>Thread-safe: background tasks (Claude, TTS, downloads) may call this.</summary>
        public void Msg(string m)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + m;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThreadId) { pendingMessages.Enqueue(line); return; }
            Messages.Add(line);
            if (Messages.Count > 40) Messages.RemoveAt(0);
            Debug.Log("[MYLTI] " + m);
        }

        public void SaveSettings()
        {
            try { Settings.Save(SettingsPath); }
            catch (Exception e) { Msg("Could not save settings: " + e.Message); }
        }

        void Update()
        {
            while (pendingMessages.TryDequeue(out var queued))
            {
                Messages.Add(queued);
                if (Messages.Count > 40) Messages.RemoveAt(0);
                Debug.Log("[MYLTI] " + queued);
            }
            if (InputReader.Pressed(GameKey.ToggleCreator)) Mode = Mode == UiMode.Creator ? UiMode.Capture : UiMode.Creator;
            if (InputReader.Pressed(GameKey.ToggleHud)) HudVisible = !HudVisible;
            if (InputReader.Pressed(GameKey.Pause) && Director.State == ProjectState.Recording) Director.TogglePause();
            if (InputReader.Pressed(GameKey.Reset) && !Director.IsBusy) StartSandbox();
            Cursor.visible = Mode == UiMode.Creator || !Director.IsBusy;
        }

        void OnDirectorState()
        {
            if (Director.State == ProjectState.Recording && Config.HideUi) Mode = UiMode.Capture;
            if (Director.State == ProjectState.ContentReady || Director.State == ProjectState.Incomplete || Director.State == ProjectState.RecordingFailed)
            {
                Mode = UiMode.Creator;
                if (Director.State == ProjectState.RecordingFailed) Msg("Recording failed: " + Director.LastError);
                else Msg($"Run {Director.Run?.RunId} finished — capture: {Director.Run?.Capture.Status}. Open the YouTube Pack tab.");
            }
        }

        // ================================================================ sandbox (Mốc A/B)
        void NewSandboxRecord()
        {
            SandboxRun = new RunRecord { RunId = "sandbox", StartedAtIso = DateTime.Now.ToString("s") };
            Log.Attach(SandboxRun);
        }

        public void StartSandbox()
        {
            if (Director.IsBusy) { Msg("A 10-minute run is in progress."); return; }
            var warnings = Config.Validate();
            foreach (var w in warnings) Msg("Preset: " + w);
            if (Log.Run != SandboxRun) NewSandboxRecord();
            Session.StuckSeconds = Config.AutoRecoverStuckSeconds;
            var spec = Config.Sandbox.Clone();
            spec.Index = 0;
            spec.Scored = false;
            spec.HeadToHead = true;
            Session.BuildMap(spec, Config.Entrants.Count, Config.Seed);
            Session.EnsureVehicles(Config.Entrants);
            bool anyManual = Config.Entrants.Any(e => e.Mode == DriveMode.Manual);
            LastSandboxResults.Clear();
            Session.AttemptFinished -= OnSandboxAttempt;
            Session.AttemptFinished += OnSandboxAttempt;
            Session.StartAttempts(Config.Entrants, spec, 0, SandboxRun.Attempts.Count + 1, false, false, rollingStart: !anyManual);
            Cam.FocusOverride = null;
            Cam.Forced = null;
            Msg(anyManual ? "Manual: W/S throttle, A/D steer, Space brake, R reset." : "AI run started.");
        }

        void OnSandboxAttempt(AttemptRecord r)
        {
            if (Director.IsBusy) return;
            LastSandboxResults.RemoveAll(x => x.VehicleId == r.VehicleId);
            LastSandboxResults.Add(r);
        }

        public void ResetSandbox()
        {
            if (Director.IsBusy) return;
            Session.AbortAll("reset");
            Session.LineUp(Config.Entrants);
        }

        public void ApplyConfigToScene()
        {
            if (Director.IsBusy) return;
            Config.Validate();
            Session.BuildMap(Config.Sandbox, Config.Entrants.Count, Config.Seed);
            Session.EnsureVehicles(Config.Entrants);
            Session.LineUp(Config.Entrants);
        }

        // ================================================================ concept lab & plan
        public void GenerateConcepts()
        {
            Config.Validate();
            Concepts = ConceptGenerator.Generate(Config.SeriesId, Config.Entrants, Config.Seed, Config.Language, History.Items, Config.Difficulty);
            SelectedConcept = Concepts.Count > 0 ? 0 : -1;
            Plan = null;
        }

        public void GeneratePlan()
        {
            if (SelectedConcept < 0 || SelectedConcept >= Concepts.Count) { Msg("Choose a concept first (Concept Lab)."); return; }
            double target = Settings.DraftDuration ? 300 : 600;
            Plan = RunPlanner.CreatePlan(Concepts[SelectedConcept], Config.Entrants, Config.Seed, target, Config.Language, Config.Difficulty, out PlanWarnings);
            foreach (var w in PlanWarnings) Msg("Plan: " + w);
            Msg($"Plan ready: {Plan.Phases.Count} phases, target {DescriptionComposer.Timestamp(target)}.");
        }

        public void StartAndCapture()
        {
            if (Plan == null) { Msg("Generate the plan first."); return; }
            Session.AttemptFinished -= OnSandboxAttempt;
            Session.StuckSeconds = Config.AutoRecoverStuckSeconds;
            if (!Director.Begin(Plan, out string err)) Msg(err);
            else Msg("Program started. Capture backend: " + Settings.Backend);
        }

        public void RandomizeConfig()
        {
            var rng = new System.Random(Environment.TickCount);
            Config.Seed = rng.Next(1, 999999);
            Config.Sandbox.KickerHeight = (float)Math.Round(5 + rng.NextDouble() * 15, 1);
            Config.Sandbox.KickerAngleDeg = (float)Math.Round(15 + rng.NextDouble() * 15, 1);
            Config.Sandbox.SpeedFactor = (float)Math.Round(0.8 + rng.NextDouble() * 0.4, 2);
            Config.Sandbox.Obstacles = (ObstacleSet)rng.Next(0, 4);
            Config.Sandbox.LandingRamp = rng.NextDouble() < 0.3;
            Config.Sandbox.BridgeWidth = rng.NextDouble() < 0.25 ? (float)Math.Round(4 + rng.NextDouble() * 3, 1) : 0f;
            foreach (var e in Config.Entrants)
            {
                var d = e.Def;
                e.TargetSpeedKmh = (float)Math.Round(d.DefaultTargetSpeedKmh * (0.85 + rng.NextDouble() * 0.3));
            }
            Config.ScenarioId = Config.ScenarioId.Split(new[] { "_rnd" }, StringSplitOptions.None)[0] + "_rnd" + Config.Seed;
            Config.Validate();
            Msg($"Randomized with seed {Config.Seed} (seed logs parameters; it does not promise identical physics).");
        }
    }
}
