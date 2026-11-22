using System.Collections.Generic;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    public enum TrackerState { Approach, OnRamp, Airborne, Landed, Done }

    /// <summary>
    /// Observes ONE vehicle during ONE attempt and turns physics into facts (FR-08, FR-12):
    /// ramp entry, takeoff, apex, landing distance, rollover, fall-out, obstacle hits, stuck, timeout.
    /// </summary>
    public sealed class AttemptTracker
    {
        public readonly VehicleController Vehicle;
        public readonly AttemptRecord Rec;
        public readonly RoundSpec Spec;
        public TrackerState State { get; private set; } = TrackerState.Approach;
        public bool Done => State == TrackerState.Done;
        public bool StuckFlag { get; private set; }
        public string StuckReason { get; private set; }
        public float StateTime { get; private set; }
        public float PredictedLandingZ { get; private set; } = -1;
        public double LastMovingProgramTime;

        readonly MapContext ctx;
        readonly EventLogger log;
        readonly AIDriver ai;
        readonly float stuckSeconds;
        float totalTime, stationary, airTimer, upsideDown, settleStill;
        float prevVy;
        bool apexLogged;
        readonly HashSet<string> hitObstacles = new HashSet<string>();
        public float AttemptTimeout = 32f;

        public AttemptTracker(VehicleController v, AttemptRecord rec, RoundSpec spec, MapContext ctx, EventLogger log, float stuckSeconds)
        {
            Vehicle = v; Rec = rec; Spec = spec; this.ctx = ctx; this.log = log; this.stuckSeconds = stuckSeconds;
            ai = v.GetComponent<AIDriver>();
            v.Impact += OnImpact;
            v.VehicleContact += OnVehicleContact;
            Rec.StartProgramTime = log.Now;
            LastMovingProgramTime = log.Now;
            Ev("AttemptStart", null, new Dictionary<string, double> { ["kickerHeight"] = spec.KickerHeight, ["speedFactor"] = spec.SpeedFactor });
        }

        public void Detach()
        {
            Vehicle.Impact -= OnImpact;
            Vehicle.VehicleContact -= OnVehicleContact;
        }

        /// <summary>Restart after an automatic stuck recovery (same attempt, Retries++).</summary>
        public void RestartAfterRetry()
        {
            State = TrackerState.Approach;
            StuckFlag = false;
            totalTime = stationary = airTimer = upsideDown = settleStill = 0;
            apexLogged = false;
            groundBodyHit = false;
            StateTime = 0;
            Rec.MaxSpeedKmh = 0; Rec.PeakHeightM = 0; Rec.TakeoffProgramTime = null; Rec.LandingProgramTime = null;
            Rec.LandingDistanceM = null; Rec.BodySlam = false; Rec.AirTimeS = 0;
            PredictedLandingZ = -1;
        }

        int Ev(string type, string note = null, Dictionary<string, double> values = null)
        {
            int id = log.Log(type, Rec.RoundIndex, Rec.AttemptIndex, Rec.VehicleId, note, values);
            if (id > 0) Rec.EventIds.Add(id);
            return id;
        }

        public void Tick(float dt)
        {
            if (Done) return;
            totalTime += dt;
            StateTime += dt;
            var t = Vehicle.transform;
            Vector3 pos = t.position;
            Vector3 vel = Vehicle.Body.linearVelocity;
            float speed = vel.magnitude;
            Rec.MaxSpeedKmh = Mathf.Max((float)Rec.MaxSpeedKmh, speed * 3.6f);
            if (speed > 1f) LastMovingProgramTime = log.Now;

            if (ctx.OutOfBounds(pos))
            {
                Ev("FellOff", ctx.HasBridge && pos.z < ctx.RampStartZ ? "fell off the bridge" : "left the arena");
                Finish(AttemptOutcome.FellOff);
                return;
            }
            if (totalTime > AttemptTimeout)
            {
                Ev("Timeout");
                Finish(State == TrackerState.Landed ? LandedOutcome() : AttemptOutcome.Timeout);
                return;
            }

            switch (State)
            {
                case TrackerState.Approach:
                    if (pos.z >= ctx.RampStartZ - 1f)
                    {
                        Ev("RampEntered", null, new Dictionary<string, double> { ["speedKmh"] = Round1(speed * 3.6f) });
                        SetState(TrackerState.OnRamp);
                    }
                    CheckStuck(speed, dt, "stopped on the runway");
                    break;

                case TrackerState.OnRamp:
                    airTimer = Vehicle.Airborne ? airTimer + dt : 0f;
                    if (airTimer > 0.1f && pos.z > ctx.LipZ - 3f) // bumps earlier on the ramp are not a takeoff
                    {
                        Rec.TakeoffProgramTime = log.Now - airTimer;
                        Rec.TakeoffSpeedKmh = Round1(speed * 3.6f);
                        Ev("Takeoff", null, new Dictionary<string, double> { ["speedKmh"] = Rec.TakeoffSpeedKmh, ["heightM"] = Round1(pos.y) });
                        PredictedLandingZ = PredictLanding(pos, vel);
                        prevVy = vel.y;
                        SetState(TrackerState.Airborne);
                        break;
                    }
                    if (pos.z < ctx.RampStartZ - 3f || (StateTime > 4f && speed < 1.5f))
                    {
                        Ev("NoJump", "did not reach the lip");
                        Finish(AttemptOutcome.NoJump);
                        return;
                    }
                    break;

                case TrackerState.Airborne:
                    Rec.PeakHeightM = Mathf.Max((float)Rec.PeakHeightM, pos.y);
                    if (!apexLogged && prevVy > 0f && vel.y <= 0f)
                    {
                        apexLogged = true;
                        Ev("Apex", null, new Dictionary<string, double> { ["heightM"] = Round1(pos.y), ["distanceFromLipM"] = Round1(pos.z - ctx.LipZ) });
                    }
                    float impactVy = -prevVy;
                    prevVy = vel.y;
                    if (TouchingGround())
                    {
                        Rec.LandingProgramTime = log.Now;
                        Rec.AirTimeS = Round2((float)(log.Now - Rec.TakeoffProgramTime.GetValueOrDefault(log.Now)));
                        Rec.LandingDistanceM = Round1(pos.z - ctx.LipZ);
                        Rec.LandingImpactSpeed = Round1(Mathf.Max(0f, impactVy));
                        if (Vehicle.GroundedWheels == 0 && Rec.LandingImpactSpeed > 5) Rec.BodySlam = true;
                        if (ctx.WallZ > 0) Rec.ClearedWall = pos.z > ctx.WallZ + 1f ? 1 : 0;
                        if (ctx.TargetZ > 0) Rec.TargetError = Round1(Mathf.Abs(pos.z - ctx.TargetZ));
                        var vals = new Dictionary<string, double>
                        {
                            ["distanceM"] = Rec.LandingDistanceM.Value, ["airTimeS"] = Rec.AirTimeS,
                            ["impactSpeed"] = Rec.LandingImpactSpeed, ["wheelsDown"] = Vehicle.GroundedWheels
                        };
                        if (Rec.TargetError.HasValue) vals["targetErrorM"] = Rec.TargetError.Value;
                        if (Rec.ClearedWall >= 0) vals["clearedWall"] = Rec.ClearedWall;
                        Ev("Landed", null, vals);
                        ai?.BrakeToStop();
                        SetState(TrackerState.Landed);
                    }
                    else if (StateTime > 14f) // absurd airtime safeguard
                    {
                        Ev("Timeout", "airborne too long");
                        Finish(AttemptOutcome.Timeout);
                    }
                    break;

                case TrackerState.Landed:
                    upsideDown = Vehicle.Upright ? 0f : upsideDown + dt;
                    if (upsideDown > 1.2f)
                    {
                        Ev("Rollover");
                        Finish(AttemptOutcome.Rollover);
                        return;
                    }
                    settleStill = speed < 1.2f ? settleStill + dt : 0f;
                    if (settleStill > 0.8f || StateTime > 8f)
                        Finish(LandedOutcome());
                    break;
            }
        }

        AttemptOutcome LandedOutcome()
        {
            if (!Vehicle.Upright) { Ev("Rollover"); return AttemptOutcome.Rollover; }
            return Rec.BodySlam ? AttemptOutcome.RoughLanding : AttemptOutcome.CleanLanding;
        }

        void CheckStuck(float speed, float dt, string reason)
        {
            stationary = speed < 1f ? stationary + dt : 0f;
            if (stationary > stuckSeconds && !StuckFlag)
            {
                StuckFlag = true;
                StuckReason = reason;
            }
        }

        bool TouchingGround()
        {
            foreach (var w in Vehicle.Wheels)
            {
                if (w == null || !w.GetGroundHit(out WheelHit hit) || hit.collider == null) continue;
                var tag = hit.collider.GetComponentInParent<SurfaceTag>();
                if (tag == null || tag.Kind != SurfaceKind.Obstacle) return true;
            }
            return groundBodyHit;
        }

        bool groundBodyHit;

        void OnImpact(SurfaceKind kind, float impulse, Vector3 point, string other)
        {
            if (Done) return;
            float dv = impulse / Mathf.Max(1f, Vehicle.Def.MassKg);
            if (kind == SurfaceKind.Obstacle)
            {
                if (hitObstacles.Add(other))
                {
                    Rec.ObstacleHits++;
                    Ev("ObstacleHit", other, new Dictionary<string, double> { ["deltaV"] = Round1(dv) });
                }
                return;
            }
            if (State == TrackerState.Airborne && kind != SurfaceKind.Scenery)
            {
                groundBodyHit = true;
                if (dv > 4f) Rec.BodySlam = true;
            }
            else if (State == TrackerState.Landed && dv > 6f && kind != SurfaceKind.Scenery)
            {
                Rec.BodySlam = true;
            }
        }

        void OnVehicleContact(VehicleController other, float impulse, Vector3 point)
        {
            if (Done) return;
            if (hitObstacles.Add("veh:" + other.name)) Ev("VehicleContact", other.Def?.Id);
        }

        void SetState(TrackerState s) { State = s; StateTime = 0f; }

        public void Finish(AttemptOutcome outcome)
        {
            if (Done) return;
            Rec.Outcome = outcome;
            Rec.EndProgramTime = log.Now;
            var vals = new Dictionary<string, double> { ["maxSpeedKmh"] = Round1((float)Rec.MaxSpeedKmh) };
            if (Rec.LandingDistanceM.HasValue) vals["distanceM"] = Rec.LandingDistanceM.Value;
            Ev("AttemptEnd", outcome.ToString(), vals);
            State = TrackerState.Done;
            ai?.BrakeToStop();
            Detach();
        }

        float PredictLanding(Vector3 p, Vector3 v)
        {
            float g = Mathf.Abs(Physics.gravity.y);
            float disc = v.y * v.y + 2f * g * Mathf.Max(0f, p.y);
            float tFlight = (v.y + Mathf.Sqrt(disc)) / g;
            return p.z + v.z * tFlight;
        }

        static float Round1(float v) => Mathf.Round(v * 10f) / 10f;
        static float Round2(float v) => Mathf.Round(v * 100f) / 100f;
    }
}
