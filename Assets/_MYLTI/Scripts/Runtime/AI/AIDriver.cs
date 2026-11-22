using UnityEngine;

namespace Mylti
{
    public enum AIPhase { Idle, Drive, Air, Brake }

    /// <summary>
    /// Basic AI (FR-04): follows its lane centre line (pure pursuit), holds a target speed on the runway,
    /// keeps full throttle up the kicker, coasts while airborne and brakes after landing.
    /// It only sends throttle/steer/brake to VehicleController.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class AIDriver : MonoBehaviour
    {
        public AIPhase Phase = AIPhase.Idle;
        public float TargetSpeedMs;
        public float LaneX;

        VehicleController vc;
        MapContext ctx;

        void Awake() { vc = GetComponent<VehicleController>(); }

        public void Begin(MapContext map, float laneX, float targetSpeedMs)
        {
            ctx = map;
            LaneX = laneX;
            TargetSpeedMs = targetSpeedMs;
            Phase = AIPhase.Drive;
        }

        public void Stop() { Phase = AIPhase.Idle; if (vc != null && vc.Source == ControlSource.AI) vc.SetInput(0, 0, 0); }
        public void BrakeToStop() { if (Phase != AIPhase.Idle) Phase = AIPhase.Brake; }

        void FixedUpdate()
        {
            if (vc == null || vc.Source != ControlSource.AI || ctx == null || vc.ReplayMode) return;
            Vector3 pos = transform.position;
            float fwd = vc.ForwardSpeed;

            switch (Phase)
            {
                case AIPhase.Idle:
                    return;
                case AIPhase.Drive:
                {
                    if (pos.z > ctx.LipZ - 1f && vc.Airborne) { Phase = AIPhase.Air; vc.SetInput(0, 0, 0); return; }
                    float steer = SteerToward(new Vector3(LaneX, pos.y, pos.z + 6f + Mathf.Abs(fwd) * 0.45f));
                    bool onRamp = pos.z >= ctx.RampStartZ - 4f;
                    float throttle, brake = 0f;
                    if (onRamp) throttle = 1f;
                    else
                    {
                        float err = TargetSpeedMs - fwd;
                        throttle = Mathf.Clamp(err * 0.5f, -1f, 1f);
                        if (throttle < 0f) { brake = Mathf.Clamp01(-throttle * 0.4f); throttle = 0f; }
                    }
                    vc.SetInput(throttle, steer, brake);
                    break;
                }
                case AIPhase.Air:
                    vc.SetInput(0f, 0f, 0f);
                    break;
                case AIPhase.Brake:
                    vc.SetInput(0f, vc.Upright ? SteerToward(pos + Vector3.forward * 20f) * 0.5f : 0f, 1f);
                    break;
            }
        }

        float SteerToward(Vector3 worldTarget)
        {
            Vector3 local = transform.InverseTransformPoint(worldTarget);
            float angle = Mathf.Atan2(local.x, Mathf.Max(0.1f, local.z)) * Mathf.Rad2Deg;
            float maxSteer = vc.Def != null ? vc.Def.MaxSteerDeg : 30f;
            float yawRate = vc.Body != null ? vc.Body.angularVelocity.y : 0f;
            return Mathf.Clamp(angle / maxSteer * 1.6f - yawRate * 0.25f, -1f, 1f);
        }
    }
}
