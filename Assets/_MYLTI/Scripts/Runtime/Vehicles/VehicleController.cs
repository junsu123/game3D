using System;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    public enum ControlSource { None, Manual, AI }

    /// <summary>
    /// Rigidbody + 4 WheelColliders, all-wheel drive. Drivers (keyboard or AI) only send
    /// throttle/steer/brake commands — they never touch physics directly (spec §9).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour
    {
        public VehicleDefinition Def;
        public Entrant Entrant;
        public ControlSource Source = ControlSource.None;
        public WheelCollider[] Wheels = new WheelCollider[4];   // FL, FR, RL, RR
        public Transform[] WheelVisuals = new Transform[4];
        public Rigidbody Body { get; private set; }

        public float ThrottleInput { get; private set; }
        public float SteerInput { get; private set; }
        public float BrakeInput { get; private set; }
        public bool InputLocked;          // countdown / after finish

        public float SpeedMs => Body != null ? Body.linearVelocity.magnitude : 0f;
        public float SpeedKmh => SpeedMs * 3.6f;
        public float ForwardSpeed => Body != null ? Vector3.Dot(Body.linearVelocity, transform.forward) : 0f;
        public int GroundedWheels { get; private set; }
        public bool BodyTouching => Time.fixedTime - lastBodyContact < 0.06f;
        public bool Airborne => GroundedWheels == 0 && !BodyTouching;
        public bool Upright => transform.up.y > 0.35f;
        public bool ReplayMode { get; private set; }

        /// <summary>(kind, impulse N·s, contact point, other object name). Raised for body collisions.</summary>
        public event Action<SurfaceKind, float, Vector3, string> Impact;
        public event Action<VehicleController, float, Vector3> VehicleContact;

        float lastBodyContact = -10f;
        Vector3[] wheelRestPos;
        Quaternion[] wheelRestRot;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
        }

        /// <summary>Called by the factory once wheels exist (visuals must be children of this transform).</summary>
        public void CaptureWheelRestPose()
        {
            wheelRestPos = new Vector3[4];
            wheelRestRot = new Quaternion[4];
            for (int i = 0; i < 4; i++)
            {
                if (WheelVisuals[i] == null) continue;
                wheelRestPos[i] = WheelVisuals[i].localPosition;
                wheelRestRot[i] = WheelVisuals[i].localRotation;
            }
        }

        public void SetInput(float throttle, float steer, float brake)
        {
            ThrottleInput = Mathf.Clamp(throttle, -1f, 1f);
            SteerInput = Mathf.Clamp(steer, -1f, 1f);
            BrakeInput = Mathf.Clamp01(brake);
        }

        void FixedUpdate()
        {
            if (ReplayMode || Def == null || Body == null) return;

            if (Source == ControlSource.Manual && !InputLocked)
                SetInput(InputReader.Throttle(), InputReader.Steer(), InputReader.BrakeHeld() ? 1f : 0f);

            float throttle = InputLocked ? 0f : ThrottleInput;
            float steer = InputLocked ? 0f : SteerInput;
            float brake = InputLocked ? 1f : BrakeInput;
            float fwd = ForwardSpeed;
            float topMs = Def.TopSpeedKmh / 3.6f;

            float motor = 0f;
            if (throttle > 0.01f) motor = fwd < topMs ? throttle * Def.MotorTorquePerWheel : 0f;
            else if (throttle < -0.01f)
            {
                if (fwd > 1.5f) brake = Mathf.Max(brake, -throttle);           // pressing reverse while moving = brake
                else motor = fwd > -8f ? throttle * Def.MotorTorquePerWheel * 0.5f : 0f;
            }
            if (Mathf.Abs(throttle) < 0.01f && brake < 0.01f) brake = 0.02f;   // light engine braking

            float steerScale = Mathf.Lerp(1f, 0.22f, Mathf.InverseLerp(5f, 45f, Mathf.Abs(fwd)));
            float steerAngle = steer * Def.MaxSteerDeg * steerScale;

            GroundedWheels = 0;
            for (int i = 0; i < 4; i++)
            {
                var w = Wheels[i];
                if (w == null) continue;
                w.motorTorque = motor;
                w.brakeTorque = brake * Def.BrakeTorque;
                if (i < 2) w.steerAngle = steerAngle;
                if (w.isGrounded) GroundedWheels++;
            }

            ApplyAntiRoll(Wheels[0], Wheels[1]);
            ApplyAntiRoll(Wheels[2], Wheels[3]);

            if (GroundedWheels > 0)
            {
                Body.AddForce(-transform.up * Def.Downforce * Body.linearVelocity.sqrMagnitude);
                Body.angularDamping = 0.05f;
            }
            else
            {
                // Mild in-air stabilisation (heavier vehicles get less) — a game choice, not real physics:
                // damp spinning and gently weather-vane the nose toward the flight direction.
                Body.angularDamping = BodyTouching ? 0.05f : Def.AirStability;
                Vector3 v = Body.linearVelocity;
                if (!BodyTouching && v.sqrMagnitude > 25f)
                    Body.AddTorque(Vector3.Cross(transform.forward, v.normalized) * Def.AirStability * 1.5f, ForceMode.Acceleration);
            }
        }

        void ApplyAntiRoll(WheelCollider left, WheelCollider right)
        {
            if (left == null || right == null) return;
            float travelL = 1f, travelR = 1f;
            bool gl = left.GetGroundHit(out WheelHit hitL);
            if (gl) travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance;
            bool gr = right.GetGroundHit(out WheelHit hitR);
            if (gr) travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance;
            float force = (travelL - travelR) * Def.AntiRoll;
            if (gl) Body.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (gr) Body.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        void LateUpdate()
        {
            if (ReplayMode) return;
            for (int i = 0; i < 4; i++)
            {
                if (Wheels[i] == null || WheelVisuals[i] == null) continue;
                Wheels[i].GetWorldPose(out Vector3 p, out Quaternion q);
                WheelVisuals[i].SetPositionAndRotation(p, q);
            }
        }

        void OnCollisionEnter(Collision c)
        {
            lastBodyContact = Time.fixedTime;
            var otherVehicle = c.rigidbody != null ? c.rigidbody.GetComponent<VehicleController>() : null;
            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            if (otherVehicle != null)
            {
                VehicleContact?.Invoke(otherVehicle, c.impulse.magnitude, point);
                return;
            }
            var tag = c.collider.GetComponentInParent<SurfaceTag>();
            Impact?.Invoke(tag != null ? tag.Kind : SurfaceKind.Scenery, c.impulse.magnitude, point, c.collider.name);
        }

        void OnCollisionStay(Collision c)
        {
            lastBodyContact = Time.fixedTime;
        }

        /// <summary>Place the vehicle (e.g. start line) with an optional rolling-start speed.</summary>
        public void Teleport(Vector3 pos, Quaternion rot, float forwardSpeed)
        {
            SetReplayMode(false);
            Body.position = pos;
            Body.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            Body.linearVelocity = rot * Vector3.forward * forwardSpeed;
            Body.angularVelocity = Vector3.zero;
            foreach (var w in Wheels) if (w != null) { w.motorTorque = 0; w.brakeTorque = 0; w.steerAngle = 0; }
            SetInput(0, 0, 0);
            lastBodyContact = -10f;
            Body.WakeUp();
        }

        /// <summary>Replay mode: physics disabled, pose driven by recorded data.</summary>
        public void SetReplayMode(bool on)
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            if (ReplayMode == on) return;
            ReplayMode = on;
            if (on)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                Body.isKinematic = true;
                Body.interpolation = RigidbodyInterpolation.None;
                // wheels follow the body at their design position while replaying
                for (int i = 0; i < 4; i++)
                    if (WheelVisuals[i] != null && wheelRestPos != null)
                        WheelVisuals[i].SetLocalPositionAndRotation(wheelRestPos[i], wheelRestRot[i]);
            }
            else
            {
                Body.isKinematic = false;
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                Body.interpolation = RigidbodyInterpolation.Interpolate;
            }
            foreach (var w in Wheels) if (w != null) w.enabled = !on;
        }
    }
}
