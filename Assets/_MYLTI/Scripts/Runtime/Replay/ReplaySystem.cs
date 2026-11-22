using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Pose-based replay (spec §5.1, lite): records position/rotation of every Replayable at 30 Hz per take,
    /// and plays a time range back with physics disabled. Replays show real recorded motion only —
    /// they never re-simulate (no seed-based "reproduction" promise).
    /// </summary>
    public sealed class ReplaySystem : MonoBehaviour
    {
        public struct Sample { public double T; public Vector3 P; public Quaternion R; }

        public sealed class Take
        {
            public string TakeId;
            public int RoundIndex;
            public double Start, End;
            public readonly Dictionary<string, List<Sample>> Tracks = new Dictionary<string, List<Sample>>();
            public readonly List<string> AttemptIds = new List<string>();
        }

        public ProgramClock Clock;
        public bool IsPlaying { get; private set; }
        public float SampleRate = 30f;

        readonly Dictionary<string, Take> takes = new Dictionary<string, Take>();
        readonly Dictionary<string, string> attemptToTake = new Dictionary<string, string>();
        Take recording;
        List<Replayable> recordingItems;
        double nextSample;

        public void Clear()
        {
            takes.Clear();
            attemptToTake.Clear();
            recording = null;
        }

        public Take BeginTake(string takeId, int roundIndex, IEnumerable<Replayable> items, IEnumerable<string> attemptIds)
        {
            EndTake();
            recording = new Take { TakeId = takeId, RoundIndex = roundIndex, Start = Clock.Time };
            recording.AttemptIds.AddRange(attemptIds);
            foreach (var a in attemptIds) attemptToTake[a] = takeId;
            recordingItems = items.Where(i => i != null).ToList();
            takes[takeId] = recording;
            nextSample = Clock.Time;
            SampleNow(force: true);
            return recording;
        }

        public void EndTake()
        {
            if (recording == null) return;
            SampleNow(force: true);
            recording.End = Clock.Time;
            recording = null;
            recordingItems = null;
        }

        public Take FindTakeForAttempt(string attemptId)
            => attemptId != null && attemptToTake.TryGetValue(attemptId, out var id) && takes.TryGetValue(id, out var t) ? t : null;

        void LateUpdate()
        {
            if (recording == null || IsPlaying) return;
            if (Clock.Time >= nextSample) SampleNow(false);
        }

        void SampleNow(bool force)
        {
            if (recording == null || recordingItems == null) return;
            nextSample = Clock.Time + 1.0 / SampleRate;
            foreach (var item in recordingItems)
            {
                if (item == null) continue;
                if (!recording.Tracks.TryGetValue(item.Id, out var list)) recording.Tracks[item.Id] = list = new List<Sample>();
                var t = item.transform;
                if (!force && list.Count > 0)
                {
                    var last = list[list.Count - 1];
                    // sleeping props: skip identical samples (hold-last on playback)
                    if ((last.P - t.position).sqrMagnitude < 1e-6f && Quaternion.Angle(last.R, t.rotation) < 0.05f) continue;
                }
                list.Add(new Sample { T = Clock.Time, P = t.position, R = t.rotation });
            }
        }

        /// <summary>Plays [from,to] (program time of the original take) at 'speed'. Items are matched by Replayable.Id.</summary>
        public IEnumerator Play(Take take, double from, double to, float speed, IEnumerable<Replayable> sceneItems)
        {
            if (take == null) yield break;
            EndTake();
            IsPlaying = true;
            var items = sceneItems.Where(i => i != null && take.Tracks.ContainsKey(i.Id)).ToList();
            foreach (var i in items)
            {
                var vc = i.GetComponent<VehicleController>();
                if (vc != null) vc.SetReplayMode(true);
                else if (i.Body != null) { i.Body.isKinematic = true; }
            }
            double clipT = from;
            double startProgram = Clock.Time;
            while (clipT < to)
            {
                foreach (var i in items) Apply(i, take.Tracks[i.Id], clipT);
                yield return null;
                clipT = from + (Clock.Time - startProgram) * speed;
            }
            foreach (var i in items) Apply(i, take.Tracks[i.Id], to);
            IsPlaying = false;
        }

        /// <summary>Restore physics after a replay (vehicles are re-placed by the session afterwards).</summary>
        public void ReleaseItems(IEnumerable<Replayable> sceneItems)
        {
            foreach (var i in sceneItems)
            {
                if (i == null) continue;
                var vc = i.GetComponent<VehicleController>();
                if (vc != null) vc.SetReplayMode(false);
                else if (i.Body != null) i.Body.isKinematic = false;
            }
        }

        static void Apply(Replayable item, List<Sample> track, double t)
        {
            if (track.Count == 0) return;
            int hi = track.FindIndex(s => s.T >= t);
            Vector3 p; Quaternion r;
            if (hi <= 0) { p = track[hi < 0 ? track.Count - 1 : 0].P; r = track[hi < 0 ? track.Count - 1 : 0].R; }
            else
            {
                var a = track[hi - 1]; var b = track[hi];
                float k = (float)((t - a.T) / System.Math.Max(1e-6, b.T - a.T));
                p = Vector3.Lerp(a.P, b.P, k);
                r = Quaternion.Slerp(a.R, b.R, k);
            }
            if (item.Body != null && item.Body.isKinematic) { item.Body.position = p; item.Body.rotation = r; }
            item.transform.SetPositionAndRotation(p, r);
        }
    }
}
