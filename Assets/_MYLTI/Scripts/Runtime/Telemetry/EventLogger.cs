using System.Collections.Generic;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Program clock = position in the final single-take video.
    /// With Unity Recorder (constant frame rate) it advances by Time.captureDeltaTime per frame,
    /// otherwise by unscaled real time (OBS records wall-clock). Pauses are tracked as interruptions.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class ProgramClock : MonoBehaviour
    {
        public double Time { get; private set; }
        public bool Running { get; private set; }
        public bool Paused { get; set; }
        public bool ConstantFrameRate => UnityEngine.Time.captureDeltaTime > 0f;

        public void StartClock() { Time = 0; Running = true; Paused = false; }
        public void StopClock() { Running = false; }

        void Update()
        {
            if (!Running || Paused) return;
            float dt = UnityEngine.Time.captureDeltaTime > 0f ? UnityEngine.Time.captureDeltaTime : UnityEngine.Time.unscaledDeltaTime;
            Time += dt;
        }
    }

    /// <summary>Writes timestamped events and video segments into the RunRecord (FR-12).</summary>
    public sealed class EventLogger
    {
        public RunRecord Run { get; private set; }
        readonly ProgramClock clock;
        int nextId = 1;
        SegmentMarker open;
        public readonly List<string> RecentLines = new List<string>();
        public event System.Action<RunEvent> Logged;

        public EventLogger(ProgramClock clock) { this.clock = clock; }

        public void Attach(RunRecord run)
        {
            Run = run;
            nextId = 1;
            open = null;
            RecentLines.Clear();
        }

        public double Now => clock != null ? clock.Time : 0;

        public int Log(string type, int round, int attempt, string vehicleId, string note = null, Dictionary<string, double> values = null)
        {
            if (Run == null) return -1;
            var e = new RunEvent
            {
                Id = nextId++,
                Type = type,
                ProgramTime = Now,
                SimTime = UnityEngine.Time.timeSinceLevelLoad,
                RoundIndex = round,
                AttemptIndex = attempt,
                VehicleId = vehicleId,
                Note = note,
                Values = values ?? new Dictionary<string, double>()
            };
            Run.Events.Add(e);
            string line = $"{DescriptionComposer.Timestamp(e.ProgramTime)} #{e.Id} {type}" + (vehicleId != null ? " " + vehicleId : "") + (note != null ? " — " + note : "");
            RecentLines.Add(line);
            if (RecentLines.Count > 60) RecentLines.RemoveAt(0);
            Logged?.Invoke(e);
            return e.Id;
        }

        public void BeginSegment(SegmentKind kind, string label, string camera = null, string sourceClip = null)
        {
            EndSegment();
            open = new SegmentMarker { Kind = kind, Start = Now, Label = label, Camera = camera, SourceClip = sourceClip };
        }

        public void SetSegmentCamera(string camera)
        {
            if (open != null && open.Camera == null) open.Camera = camera;
        }

        public void EndSegment()
        {
            if (open == null || Run == null) { open = null; return; }
            open.End = Now;
            if (open.Duration > 0.01) Run.Segments.Add(open);
            open = null;
        }

        /// <summary>Dead air is recorded as its own marker (overlapping the live segment) for the quality gate / editor.</summary>
        public void AddDeadAir(double start, double end, string label)
        {
            if (Run == null || end - start < 0.5) return;
            Run.Segments.Add(new SegmentMarker { Kind = SegmentKind.DeadAir, Start = start, End = end, Label = label });
        }
    }
}
