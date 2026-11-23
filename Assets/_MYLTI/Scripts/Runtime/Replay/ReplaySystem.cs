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

        /// <summary>Vehicle speed over program time for every live take (drives the offline engine sound).</summary>
        public List<Mylti.Domain.SpeedTrack> ExportSpeedTracks()
        {
            var list = new List<Mylti.Domain.SpeedTrack>();
            foreach (var take in takes.Values)
                foreach (var kv in take.Tracks)
                {
                    var def = Mylti.Domain.VehicleCatalog.Find(kv.Key);
                    if (def == null || kv.Value.Count < 2) continue;
                    var st = new Mylti.Domain.SpeedTrack
                    {
                        VehicleId = def.Id,
                        TopSpeedMs = def.TopSpeedKmh / 3.6f,
                        BasePitch = def.BodyStyle == "truck" ? 0.55f : def.BodyStyle == "suv" ? 0.75f : 0.95f
                    };
                    var smp = kv.Value;
                    for (int i = 1; i < smp.Count; i++)
                    {
                        double dt = smp[i].T - smp[i - 1].T;
                        if (dt <= 1e-4) continue;
                        st.Times.Add(smp[i].T);
                        st.Speeds.Add((float)((smp[i].P - smp[i - 1].P).magnitude / dt));
                    }
                    if (st.Times.Count >= 2) list.Add(st);
                }
            return list;
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
        /// <summary>Playback speed at clip time t: 'speed', dropping to a dramatic slow motion around 'slowAt' (if given).</summary>
        public static float SpeedAt(double t, float speed, double slowAt)
        {
            if (slowAt < 0) return speed;
            double d = t - slowAt;
            if (d < -0.9 || d > 1.8) return speed;
            if (d < -0.35) return Mathf.Lerp(speed, 0.2f, (float)((d + 0.9) / 0.55)); // ease into slow motion
            if (d < 1.2) return 0.2f;
            return Mathf.Lerp(0.2f, speed, (float)((d - 1.2) / 0.6));
        }

        /// <summary>Real-time seconds needed to play [from,to] with SpeedAt.</summary>
        public static double Duration(double from, double to, float speed, double slowAt)
        {
            double t = from, real = 0;
            while (t < to) { float s = SpeedAt(t, speed, slowAt); t += 0.02 * s; real += 0.02; }
            return real;
        }

        public IEnumerator Play(Take take, double from, double to, float speed, IEnumerable<Replayable> sceneItems, double slowAt = -1)
        {
            if (take == null) yield break;
            EndTake();
            IsPlaying = true;
            var items = sceneItems.Where(i => i != null && take.Tracks.ContainsKey(i.Id)).ToList();
            var damages = new List<VehicleDamage>();
            foreach (var i in items)
            {
                var vc = i.GetComponent<VehicleController>();
                if (vc != null) vc.SetReplayMode(true);
                else if (i.Body != null) { i.Body.isKinematic = true; }
                var dmg = i.GetComponent<VehicleDamage>();
                if (dmg != null) { dmg.BeginReplay(from); damages.Add(dmg); }
            }
            double clipT = from;
            double lastProgram = Clock.Time;
            while (clipT < to)
            {
                foreach (var i in items) Apply(i, take.Tracks[i.Id], clipT);
                foreach (var d in damages) d.ReplayTo(clipT);
                yield return null;
                double dt = Clock.Time - lastProgram;
                lastProgram = Clock.Time;
                clipT += dt * SpeedAt(clipT, speed, slowAt);
            }
            foreach (var i in items) Apply(i, take.Tracks[i.Id], to);
            foreach (var d in damages) d.ReplayTo(to);
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
                var dmg = i.GetComponent<VehicleDamage>();
                if (dmg != null) dmg.EndReplay();
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
