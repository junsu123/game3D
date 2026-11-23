using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mylti
{
    public enum Shot { Auto, Chase, SideTracking, LowAngleLip, ImpactCloseup, FixedWide, TopDown, Orbit, Free, Lineup, Flyover, ReplaySide, ReplayLow, WallCam, DropCam }

    /// <summary>
    /// Story-aware camera (FR-06, §23.1). Hard cuts between shot types, smooth tracking inside a shot,
    /// minimum shot length, occlusion check, limited shake. Never touches vehicle physics.
    /// Keys: 1 Chase · 2 Side · 3 Lip/Front · 4 Impact · 5 Wide · 6 Top · 7 Orbit · 8 Free · 0 Auto.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraDirector : MonoBehaviour
    {
        public ChallengeSession Session;
        public Shot Requested = Shot.Auto;      // operator / director choice
        public Shot Current { get; private set; } = Shot.FixedWide;
        public bool AllowShake = true;
        public float MinShotSeconds = 1.6f;
        public event Action<Shot> ManualOverride;
        public event Action<Shot> ShotChanged;

        // set by the 10-minute director for scripted shots
        public Transform LineupTarget;
        public float FlyoverT;
        public Shot? Forced;
        public float ImpactLandingZOverride = -1;
        public List<Transform> FocusOverride;   // replays / summary: frame these instead of live attempts

        Camera cam;
        Vector3 vel;
        float shotTimer, shake, orbitAngle;
        Vector3 freeEuler;
        bool snapNext = true;
        Shot autoLast = Shot.FixedWide;

        public string CurrentShotName => Current.ToString();

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 2500f;
        }

        public void Snap() { snapNext = true; }

        public void AddShake(float amount)
        {
            if (AllowShake) shake = Mathf.Min(0.35f, shake + amount);
        }

        void Update()
        {
            HandleKeys();
            if (Current == Shot.Free) UpdateFree();
        }

        void HandleKeys()
        {
            Shot? pick = null;
            if (InputReader.Pressed(GameKey.Cam1)) pick = Shot.Chase;
            else if (InputReader.Pressed(GameKey.Cam2)) pick = Shot.SideTracking;
            else if (InputReader.Pressed(GameKey.Cam3)) pick = Shot.LowAngleLip;
            else if (InputReader.Pressed(GameKey.Cam4)) pick = Shot.ImpactCloseup;
            else if (InputReader.Pressed(GameKey.Cam5)) pick = Shot.FixedWide;
            else if (InputReader.Pressed(GameKey.Cam6)) pick = Shot.TopDown;
            else if (InputReader.Pressed(GameKey.Cam7)) pick = Shot.Orbit;
            else if (InputReader.Pressed(GameKey.Cam8)) pick = Shot.Free;
            else if (InputReader.Pressed(GameKey.CamAuto)) pick = Shot.Auto;
            if (pick.HasValue)
            {
                Requested = pick.Value;
                if (pick.Value == Shot.Free) freeEuler = transform.eulerAngles;
                ManualOverride?.Invoke(pick.Value);
            }
        }

        void LateUpdate()
        {
            var map = Session != null ? Session.Map : null;
            Shot want = Forced ?? (Requested == Shot.Auto ? ChooseAuto() : Requested);
            shotTimer += Time.deltaTime;
            if (want != Current)
            {
                bool scripted = Forced.HasValue || Requested != Shot.Auto;
                if (scripted || shotTimer >= MinShotSeconds)
                {
                    Current = want;
                    shotTimer = 0f;
                    snapNext = true;
                    ShotChanged?.Invoke(Current);
                }
            }
            if (Current == Shot.Free || map == null) return;

            Vector3 focus = Focus(out float spread, out Vector3 focusVel);
            ComputeShot(Current, map, focus, spread, focusVel, out Vector3 pos, out Vector3 look, out float fov, out float smooth);
            pos = AvoidOcclusion(look, pos);

            if (snapNext) { transform.position = pos; vel = Vector3.zero; snapNext = false; }
            else transform.position = smooth <= 0f ? pos : Vector3.SmoothDamp(transform.position, pos, ref vel, smooth, Mathf.Infinity, Time.deltaTime);
            var rot = Quaternion.LookRotation((look - transform.position).sqrMagnitude > 0.01f ? look - transform.position : Vector3.forward, Vector3.up);
            transform.rotation = rot;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, Time.deltaTime * 4f);

            if (shake > 0.001f)
            {
                transform.position += UnityEngine.Random.insideUnitSphere * shake;
                shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 0.8f);
            }
        }

        Shot ChooseAuto()
        {
            if (Session == null || Session.Active.Count == 0) return autoLast == Shot.Orbit ? Shot.Orbit : Shot.FixedWide;
            var live = Session.Active.Where(t => !t.Done).ToList();
            if (live.Count == 0) return Shot.Orbit;
            var lead = live.OrderByDescending(t => (int)t.State).ThenByDescending(t => t.Vehicle.transform.position.z).First();
            Shot s;
            var m = Session.Map;
            if (m.CrashWallZ > 0)
            {
                if (lead.State == TrackerState.Approach) s = lead.Vehicle.transform.position.z < m.CrashWallZ - 75f ? Shot.Chase : Shot.WallCam;
                else s = lead.StateTime < 3.2f ? Shot.WallCam : Shot.Orbit;
                autoLast = s;
                return s;
            }
            if (m.DropZ > 0)
            {
                if (lead.State == TrackerState.Hanging) s = Shot.SideTracking;
                else if (lead.State == TrackerState.Airborne) s = Shot.DropCam;
                else s = lead.StateTime < 3f ? Shot.DropCam : Shot.Orbit;
                autoLast = s;
                return s;
            }
            switch (lead.State)
            {
                case TrackerState.Approach:
                    if (lead.Vehicle.transform.position.z < Session.Map.RampStartZ - 90f) s = live.Count > 1 ? Shot.SideTracking : Shot.Chase;
                    else s = Shot.SideTracking;
                    break;
                case TrackerState.OnRamp: s = Shot.LowAngleLip; break;
                case TrackerState.Airborne:
                    s = lead.StateTime < 0.9f ? Shot.LowAngleLip : (lead.PredictedLandingZ > 0 ? Shot.ImpactCloseup : Shot.SideTracking);
                    break;
                case TrackerState.Landed: s = lead.StateTime < 2.5f ? Shot.ImpactCloseup : Shot.Orbit; break;
                default: s = Shot.Orbit; break;
            }
            autoLast = s;
            return s;
        }

        Vector3 Focus(out float spread, out Vector3 velocity)
        {
            spread = 0f;
            velocity = Vector3.zero;
            List<Transform> ts;
            if (LineupTarget != null && (Current == Shot.Lineup)) ts = new List<Transform> { LineupTarget };
            else if (FocusOverride != null && FocusOverride.Count > 0) ts = FocusOverride.Where(t => t != null).ToList();
            else if (Session != null && Session.Active.Count > 0) ts = Session.Active.Where(t => !t.Done || t.State == TrackerState.Done).Select(t => t.Vehicle.transform).ToList();
            else if (Session != null && Session.Vehicles.Count > 0) ts = Session.Vehicles.Values.Select(v => v.transform).ToList();
            else return Vector3.zero;
            if (ts.Count == 0) return Vector3.zero;
            Vector3 c = Vector3.zero;
            foreach (var t in ts) c += t.position;
            c /= ts.Count;
            foreach (var t in ts) spread = Mathf.Max(spread, Vector3.Distance(c, t.position));
            var rb = ts[0].GetComponent<Rigidbody>();
            if (rb != null) velocity = rb.linearVelocity;
            return c;
        }

        void ComputeShot(Shot s, MapContext map, Vector3 f, float spread, Vector3 fv, out Vector3 pos, out Vector3 look, out float fov, out float smooth)
        {
            float side = map.RampWidth * 0.5f;
            look = f + Vector3.up * 0.8f;
            fov = 55f;
            smooth = 0.2f;
            switch (s)
            {
                case Shot.Chase:
                {
                    Vector3 dir = new Vector3(fv.x, 0, fv.z);
                    dir = dir.sqrMagnitude > 4f ? dir.normalized : Vector3.forward;
                    pos = f - dir * (11f + spread) + Vector3.up * (4f + spread * 0.3f);
                    look = f + dir * 6f + Vector3.up;
                    smooth = 0.15f;
                    fov = 55f + Mathf.Clamp01(fv.magnitude / 50f) * 9f; // speed FOV kick
                    break;
                }
                case Shot.SideTracking:
                case Shot.ReplaySide:
                    pos = new Vector3(side + 22f + spread * 1.2f, Mathf.Max(3f, f.y * 0.7f + 3f), f.z - 6f);
                    fov = 50f + Mathf.Clamp01(fv.magnitude / 50f) * 6f;
                    smooth = 0.25f;
                    break;
                case Shot.LowAngleLip:
                case Shot.ReplayLow:
                    pos = new Vector3(side + 7f, 1.2f, map.LipZ + 16f);
                    fov = 58f;
                    smooth = 0f;
                    break;
                case Shot.ImpactCloseup:
                {
                    float lz = ImpactLandingZOverride > 0 ? ImpactLandingZOverride : PredictedLanding(map, f);
                    pos = new Vector3(side + 12f, 2.2f, lz + 18f);
                    fov = 42f;
                    smooth = 0f;
                    break;
                }
                case Shot.WallCam:
                {
                    // low, beside the barrier, looking back at the incoming car — the classic crash-test angle
                    float wz = map.CrashWallZ > 0 ? map.CrashWallZ : f.z + 10f;
                    pos = new Vector3(side + 4.5f, 1.0f, wz - 2.5f);
                    look = Vector3.Lerp(new Vector3(0, 1f, wz), f + Vector3.up * 0.6f, 0.75f);
                    fov = Mathf.Lerp(38f, 60f, Mathf.InverseLerp(60f, 8f, wz - f.z));
                    smooth = 0f;
                    break;
                }
                case Shot.DropCam:
                {
                    float dz = map.DropZ > 0 ? map.DropZ : f.z;
                    pos = new Vector3(side + 13f, 0.9f, dz - 13f);
                    look = f + Vector3.up * 0.5f;
                    fov = 52f;
                    smooth = 0f;
                    break;
                }
                case Shot.FixedWide:
                    pos = new Vector3(side + 75f, 28f, map.LipZ + 15f);
                    look = new Vector3(0, map.LipHeight * 0.5f, Mathf.Lerp(map.LipZ + 30f, f.z, 0.5f));
                    fov = 52f;
                    smooth = 0.6f;
                    break;
                case Shot.TopDown:
                    pos = f + new Vector3(0, 45f + spread, -12f);
                    fov = 50f;
                    smooth = 0.3f;
                    break;
                case Shot.Orbit:
                    orbitAngle += Time.deltaTime * 18f;
                    float rad = orbitAngle * Mathf.Deg2Rad;
                    float r = 13f + spread;
                    pos = f + new Vector3(Mathf.Cos(rad) * r, 4.5f, Mathf.Sin(rad) * r);
                    smooth = 0.1f;
                    break;
                case Shot.Lineup:
                    pos = f + new Vector3(5.5f, 1.6f, 6.5f) + Vector3.forward * Mathf.Sin(Time.time * 0.3f);
                    look = f + Vector3.up * 0.9f;
                    fov = 45f;
                    smooth = 0.4f;
                    break;
                case Shot.Flyover:
                {
                    float t = Mathf.Clamp01(FlyoverT);
                    float z = Mathf.Lerp(-20f, map.LipZ + 140f, t);
                    pos = new Vector3(Mathf.Lerp(30f, -25f, t), Mathf.Lerp(18f, 38f, Mathf.Sin(t * Mathf.PI)), z);
                    look = new Vector3(0, map.LipHeight * 0.4f, z + 60f);
                    fov = 60f;
                    smooth = 0.05f;
                    break;
                }
                default:
                    pos = transform.position;
                    break;
            }
        }

        float PredictedLanding(MapContext map, Vector3 focus)
        {
            if (Session != null)
            {
                var t = Session.Active.FirstOrDefault(x => x.PredictedLandingZ > 0 && (x.State == TrackerState.Airborne || x.State == TrackerState.Landed));
                if (t != null) return t.PredictedLandingZ;
            }
            return Mathf.Max(map.LipZ + 30f, focus.z);
        }

        Vector3 AvoidOcclusion(Vector3 target, Vector3 desired)
        {
            RaycastHit hit = default;
            for (int k = 0; k < 6; k++)
            {
                Vector3 cand = desired + Vector3.up * (k * 4f);
                if (!Physics.Linecast(target, cand, out hit, ~0, QueryTriggerInteraction.Ignore)
                    || hit.collider.GetComponentInParent<VehicleController>() != null)
                    return cand; // clear line of sight (raising the camera if needed)
            }
            return hit.point + hit.normal * 0.6f + Vector3.up * 0.5f;
        }

        void UpdateFree()
        {
            if (InputReader.RightMouseHeld())
            {
                var d = InputReader.MouseDelta();
                freeEuler.x = Mathf.Clamp(freeEuler.x - d.y * 2f, -85f, 85f);
                freeEuler.y += d.x * 2f;
            }
            transform.rotation = Quaternion.Euler(freeEuler.x, freeEuler.y, 0);
            float spd = InputReader.Held(GameKey.FreeFast) ? 60f : 18f;
            var mv = InputReader.FreeMove();
            float up = (InputReader.Held(GameKey.FreeUp) ? 1f : 0f) - (InputReader.Held(GameKey.FreeDown) ? 1f : 0f);
            transform.position += (transform.forward * mv.y + transform.right * mv.x + Vector3.up * up) * spd * Time.unscaledDeltaTime;
            cam.fieldOfView = Mathf.Clamp(cam.fieldOfView - InputReader.Scroll() * 3f, 20f, 90f);
        }
    }
}
