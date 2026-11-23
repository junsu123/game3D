using System;
using System.Collections.Generic;
using System.Linq;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Owns the vehicles and the arena; starts attempts, ticks trackers, performs stuck recovery
    /// (max retries per attempt), and detects dead air. Used by both the Sandbox and the 10-Minute Director.
    /// Start/Reset never duplicates vehicles: one instance per vehicle id is reused (acceptance §12.5).
    /// </summary>
    public sealed class ChallengeSession : MonoBehaviour
    {
        public MegaRampBuilder Builder;
        public EventLogger Log;
        public float StuckSeconds = 8f;
        public int MaxRetries = 2;
        public bool AudioEnabled = true;

        public MapContext Map => Builder != null ? Builder.Current : null;
        public readonly Dictionary<string, VehicleController> Vehicles = new Dictionary<string, VehicleController>();
        public readonly List<AttemptTracker> Active = new List<AttemptTracker>();
        public bool Running => Active.Count > 0 && Active.Any(t => !t.Done);
        public event Action<AttemptRecord> AttemptFinished;

        Transform vehicleRoot;
        double deadStart = -1;
        int attemptCounter;

        void Awake()
        {
            vehicleRoot = new GameObject("Vehicles").transform;
        }

        public MapContext BuildMap(RoundSpec spec, int laneCount, int seed)
        {
            AbortAll("map rebuilt");
            var ctx = Builder.Build(spec, laneCount, seed);
            ParkAll();
            return ctx;
        }

        /// <summary>Create/destroy vehicle instances so exactly the given entrants exist.</summary>
        public void EnsureVehicles(IList<Entrant> entrants)
        {
            var wanted = new HashSet<string>(entrants.Select(e => e.VehicleId));
            foreach (var id in Vehicles.Keys.Where(k => !wanted.Contains(k)).ToList())
            {
                Destroy(Vehicles[id].gameObject);
                Vehicles.Remove(id);
            }
            foreach (var e in entrants)
            {
                if (Vehicles.TryGetValue(e.VehicleId, out var existing))
                {
                    // colour or settings changed → rebuild that one vehicle
                    if (existing.Entrant.ColorHex != e.ColorHex) { Destroy(existing.gameObject); Vehicles.Remove(e.VehicleId); }
                    else { existing.Entrant = e; continue; }
                }
                var vc = VehicleFactory.Create(e, vehicleRoot, AudioEnabled);
                // distinct temporary spot so new vehicles never spawn inside each other
                vc.transform.position = new Vector3(-60f - Vehicles.Count * 12f, 0.6f, -30f);
                ColorUtility.TryParseHtmlString(e.ColorHex ?? vc.Def.DefaultColorHex, out var team);
                vc.gameObject.AddComponent<Nameplate>().Init(vc, this, team);
                vc.InputLocked = true;
                Vehicles[e.VehicleId] = vc;
            }
            ParkAll();
        }

        /// <summary>Parks idle vehicles in the paddock beside the runway (visible, stationary, not in the way).</summary>
        public void ParkAll()
        {
            if (Map == null) return;
            int i = 0;
            foreach (var vc in Vehicles.Values)
            {
                if (Active.Any(t => t.Vehicle == vc && !t.Done)) continue;
                Park(vc, i++);
            }
        }

        public void Park(VehicleController vc, int slot)
        {
            if (Map == null) return;
            var pos = new Vector3(-(Map.RampWidth * 0.5f + 9f), 0.5f, 20f + slot * 10f);
            vc.Source = ControlSource.None;
            vc.GetComponent<AIDriver>()?.Stop();
            vc.Teleport(pos, Quaternion.Euler(0, 20f, 0), 0f);
            vc.InputLocked = true;
        }

        /// <summary>Line vehicles up at the start (stationary) — used for intros and manual driving.</summary>
        public void LineUp(IList<Entrant> who)
        {
            if (Map == null) return;
            foreach (var e in who)
            {
                if (!Vehicles.TryGetValue(e.VehicleId, out var vc)) continue;
                vc.Teleport(Map.SpawnPoint(e.Lane), Quaternion.identity, 0f);
                vc.Source = ControlSource.None;
                vc.InputLocked = true;
            }
        }

        public List<AttemptTracker> StartAttempts(IList<Entrant> who, RoundSpec spec, int roundIndex, int attemptIndex,
            bool teaser, bool bonus, bool rollingStart, bool forceAI = false, float spawnZ = -1f, float speedFrac = 0.6f)
        {
            if (Map == null) throw new InvalidOperationException("Build the map first");
            AbortAll("new attempt");
            int laneCount = Map.LaneX.Length;
            // vehicles not taking part are parked so they never block the landing zone
            int slot = 0;
            foreach (var kv in Vehicles)
                if (!who.Any(e => e.VehicleId == kv.Key)) Park(kv.Value, slot++);
            foreach (var e in who)
            {
                if (!Vehicles.TryGetValue(e.VehicleId, out var vc)) continue;
                var rec = new AttemptRecord
                {
                    AttemptId = $"r{roundIndex}_a{attemptIndex}_{e.VehicleId}_{++attemptCounter}",
                    RoundIndex = roundIndex,
                    AttemptIndex = attemptIndex,
                    VehicleId = e.VehicleId,
                    IsTeaser = teaser,
                    IsBonus = bonus,
                    Scored = !teaser && spec.Scored
                };
                int lane = spec.HeadToHead ? Mathf.Clamp(e.Lane, 0, laneCount - 1) : laneCount / 2;
                Launch(vc, e, spec, lane, rollingStart, forceAI, spawnZ, speedFrac);
                if (Log.Run != null) Log.Run.Attempts.Add(rec);
                var tracker = new AttemptTracker(vc, rec, spec, Map, Log, vc.Source == ControlSource.Manual ? 60f : StuckSeconds);
                if (vc.Source == ControlSource.Manual) tracker.AttemptTimeout = 180f;
                Active.Add(tracker);
            }
            deadStart = -1;
            return Active.ToList();
        }

        void Launch(VehicleController vc, Entrant e, RoundSpec spec, int lane, bool rolling, bool forceAI, float spawnZ = -1f, float speedFrac = 0.6f)
        {
            float targetKmh = Mathf.Min(vc.Def.TopSpeedKmh, (e.TargetSpeedKmh > 0 ? e.TargetSpeedKmh : vc.Def.DefaultTargetSpeedKmh) * spec.SpeedFactor);
            float startSpeed = rolling ? targetKmh / 3.6f * Mathf.Clamp01(speedFrac) : 0f;
            var spawn = Map.SpawnPoint(lane);
            if (spawnZ > 0f) spawn.z = Mathf.Min(spawnZ, Map.RampStartZ - 20f);
            vc.Teleport(spawn, Quaternion.identity, startSpeed);
            vc.InputLocked = false;
            var ai = vc.GetComponent<AIDriver>();
            if (e.Mode == DriveMode.Manual && !forceAI)
            {
                vc.Source = ControlSource.Manual;
                ai?.Stop();
            }
            else
            {
                vc.Source = ControlSource.AI;
                ai?.Begin(Map, Map.LaneX[lane], targetKmh / 3.6f);
            }
        }

        void FixedUpdate()
        {
            if (Active.Count == 0) return;
            float dt = Time.fixedDeltaTime;
            foreach (var t in Active.ToList())
            {
                if (t.Done) continue;
                t.Tick(dt);
                if (t.Done) { AttemptFinished?.Invoke(t.Rec); continue; }
                if (t.StuckFlag) HandleStuck(t);
            }
            TrackDeadAir();
        }

        void HandleStuck(AttemptTracker t)
        {
            if (t.Rec.Retries < MaxRetries)
            {
                t.Rec.Retries++;
                Log.Log("Retry", t.Rec.RoundIndex, t.Rec.AttemptIndex, t.Rec.VehicleId, $"auto-recovery #{t.Rec.Retries}: {t.StuckReason}");
                var e = t.Vehicle.Entrant;
                int lane = t.Spec.HeadToHead ? Mathf.Clamp(e.Lane, 0, Map.LaneX.Length - 1) : Map.LaneX.Length / 2;
                Launch(t.Vehicle, e, t.Spec, lane, true, t.Vehicle.Source == ControlSource.AI);
                t.RestartAfterRetry();
            }
            else
            {
                Log.Log("Stuck", t.Rec.RoundIndex, t.Rec.AttemptIndex, t.Rec.VehicleId, "max retries reached — attempt marked failed");
                t.Finish(AttemptOutcome.Stuck);
                AttemptFinished?.Invoke(t.Rec);
            }
        }

        void TrackDeadAir()
        {
            var live = Active.Where(x => !x.Done).ToList();
            bool allStill = live.Count > 0 && live.All(x => x.Vehicle.SpeedMs < 0.5f);
            if (allStill && deadStart < 0) deadStart = Log.Now;
            if (!allStill && deadStart >= 0)
            {
                if (Log.Now - deadStart > 2.0) Log.AddDeadAir(deadStart, Log.Now, "vehicles stationary");
                deadStart = -1;
            }
        }

        public void AbortAll(string reason)
        {
            foreach (var t in Active)
            {
                if (!t.Done)
                {
                    Log.Log("Abort", t.Rec.RoundIndex, t.Rec.AttemptIndex, t.Rec.VehicleId, reason);
                    t.Finish(AttemptOutcome.Aborted);
                }
            }
            if (deadStart >= 0 && Log.Now - deadStart > 2.0) Log.AddDeadAir(deadStart, Log.Now, "vehicles stationary");
            deadStart = -1;
            Active.Clear();
        }

        /// <summary>Vehicles of the current attempt (for cameras/HUD).</summary>
        public IEnumerable<VehicleController> ActiveVehicles => Active.Select(t => t.Vehicle).Distinct();
    }
}
