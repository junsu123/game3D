using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mylti
{
    public enum Shot { Auto, Chase, SideTracking, LowAngleLip, ImpactCloseup, FixedWide, TopDown, Orbit, Free, Lineup, Flyover, ReplaySide, ReplayLow, WallCam, DropCam, CrowdReaction }

    /// <summary>
    /// Broadcast-style camera director (FR-06, §23.1).
    ///
    /// Framing: every shot is a "lens" — the zoom is computed so the subject fills a set share of the frame
    /// (trackside cameras use long telephoto lenses like motorsport TV, which also makes the crowd behind look big).
    /// Motion: each shot is an operated rig with damped position, damped aim with look-ahead (lead room) and a damped
    /// zoom — no per-frame snapping. Shake and the subtle hand-held drift are smooth Perlin noise, never random jitter.
    /// Editing: cuts respect the 180° line (side cameras stay on +X) and the 30° rule — two shots that would look alike
    /// are BLENDED instead of cut; phase changes are cut on action; minimum shot length avoids flicker.
    /// Keys: 1 Chase · 2 Side · 3 Lip/Front · 4 Impact · 5 Wide · 6 Top · 7 Orbit · 8 Free · 9 Crowd · 0 Auto.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraDirector : MonoBehaviour
    {
        public ChallengeSession Session;
        public Shot Requested = Shot.Auto;      // operator / director choice
        public Shot Current { get; private set; } = Shot.FixedWide;
        public bool AllowShake = true;
        public float MinShotSeconds = 2.2f;
        public float HandheldAmount = 1f;       // 0 = locked-off tripod look
        public event Action<Shot> ManualOverride;
        public event Action<Shot> ShotChanged;

        // set by the 10-minute director for scripted shots
        public Transform LineupTarget;
        public float FlyoverT;
        public Shot? Forced;
        public float ImpactLandingZOverride = -1;
        public List<Transform> FocusOverride;   // replays / summary: frame these instead of live attempts
        public CrowdDirector Crowd;


        /// <summary>One operated camera: damped position, damped aim point and damped zoom.</summary>
        sealed class Rig
        {
            public bool Live;
            public Vector3 Pos, PosVel, Aim, AimVel;
            public float Fov, FovVel, Age;
            public float Orbit;                 // orbit angle (deg)
            public Vector3 Anchor, AnchorLook;  // fixed positions chosen when the shot starts (crowd shot)
            public bool HasAnchor;
        }

        struct Want
        {
            public Vector3 Pos, Aim;
            public float Fov, PosSmooth, AimSmooth, FovSmooth, Handheld;
        }

        Camera cam;
        readonly Dictionary<Shot, Rig> rigs = new Dictionary<Shot, Rig>();
        Shot blendFrom;
        float blendT, blendDur;
        float shotTimer, shake, noiseT;
        Vector3 freeEuler;
        bool snapNext = true;
        Shot autoLast = Shot.FixedWide;
        string crowdShotFor;          // attempt id that already had its crowd cut-away

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
            if (AllowShake) shake = Mathf.Min(0.6f, shake + amount);
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
            else if (InputReader.Pressed(GameKey.Cam9)) pick = Shot.CrowdReaction;
            else if (InputReader.Pressed(GameKey.CamAuto)) pick = Shot.Auto;
            if (pick.HasValue)
            {
                Requested = pick.Value;
                if (pick.Value == Shot.Free) freeEuler = transform.eulerAngles;
                ManualOverride?.Invoke(pick.Value);
            }
        }

        Rig RigFor(Shot s)
        {
            if (!rigs.TryGetValue(s, out var r)) rigs[s] = r = new Rig();
            return r;
        }

        void LateUpdate()
        {
            var map = Session != null ? Session.Map : null;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            noiseT += dt;
            Shot want = Forced ?? (Requested == Shot.Auto ? ChooseAuto() : Requested);
            if (want == Shot.CrowdReaction && (Crowd == null || Crowd.Count == 0)) want = Shot.Orbit;
            shotTimer += dt;
            if (want != Current)
            {
                bool scripted = Forced.HasValue || Requested != Shot.Auto;
                if (scripted || snapNext || shotTimer >= MinShotSeconds)
                {
                    var from = Current;
                    Current = want;
                    shotTimer = 0f;
                    RigFor(Current).Live = false; // fresh operator position
                    float blend = snapNext ? 0f : BlendSeconds(from, Current);
                    if (blend > 0f && rigs.TryGetValue(from, out var fr) && fr.Live) { blendFrom = from; blendT = 0f; blendDur = blend; }
                    else blendDur = 0f;
                    ShotChanged?.Invoke(Current);
                }
            }
            if (Current == Shot.Free || map == null) return;
            if (snapNext) { foreach (var r in rigs.Values) r.Live = false; blendDur = 0f; snapNext = false; }

            Vector3 focus = Focus(out float spread, out Vector3 focusVel, out float size);
            Evaluate(Current, map, focus, spread, focusVel, size, dt, out var pos, out var rot, out var fov, out float hh);
            if (blendDur > 0f)
            {
                Evaluate(blendFrom, map, focus, spread, focusVel, size, dt, out var p0, out var r0, out var f0, out float hh0);
                blendT += dt;
                float t = Mathf.Clamp01(blendT / blendDur);
                t = Mylti.Domain.Lens.Smoother(t); // no velocity jump at either end
                pos = Vector3.Lerp(p0, pos, t);
                rot = Quaternion.Slerp(r0, rot, t);
                fov = Mathf.Lerp(f0, fov, t);
                hh = Mathf.Lerp(hh0, hh, t);
                if (blendT >= blendDur) blendDur = 0f;
            }

            // smooth operator drift + impact shake (Perlin noise: continuous, never per-frame jitter)
            // drift is scaled by the lens: on a long lens the operator is on a tripod, so the frame barely breathes
            float lensK = Mathf.Clamp(fov / 45f, 0.15f, 1.3f);
            float amp = hh * HandheldAmount * 0.35f * lensK + (AllowShake ? shake * 6f * Mathf.Sqrt(lensK) : 0f);
            if (amp > 0.001f)
            {
                float f = 0.35f + shake * 6f; // shake is faster than hand drift
                float n1 = Mathf.PerlinNoise(noiseT * f, 1.7f) - 0.5f, n2 = Mathf.PerlinNoise(4.2f, noiseT * f) - 0.5f, n3 = Mathf.PerlinNoise(noiseT * f * 0.8f, 9.1f) - 0.5f;
                rot = rot * Quaternion.Euler(n1 * amp * 2f, n2 * amp * 2f, n3 * amp * 0.8f);
            }
            shake = Mathf.MoveTowards(shake, 0f, dt * 0.9f);

            transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = Mathf.Clamp(fov, 6f, 90f);
        }

        /// <summary>Editing rules: when to dissolve the camera move instead of cutting.</summary>
        static float BlendSeconds(Shot from, Shot to)
        {
            bool Fixed(Shot s) => s == Shot.LowAngleLip || s == Shot.ImpactCloseup || s == Shot.WallCam || s == Shot.DropCam || s == Shot.CrowdReaction || s == Shot.ReplayLow;
            if (from == Shot.CrowdReaction || to == Shot.CrowdReaction) return 0f; // cut-aways are cuts
            if ((from == Shot.Chase && to == Shot.SideTracking) || (from == Shot.SideTracking && to == Shot.Chase)) return 1.4f; // same subject, similar size → move, don't jump-cut
            if (to == Shot.Orbit && from != Shot.FixedWide) return 1.2f;   // settle into the hero orbit
            if (from == Shot.Orbit && to == Shot.Chase) return 1.0f;
            if (Fixed(to)) return 0f;                                       // cut on action into the big moment
            return 0f;
        }

        Shot ChooseAuto()
        {
            if (Session == null || Session.Active.Count == 0) return autoLast == Shot.Orbit ? Shot.Orbit : Shot.FixedWide;
            var live = Session.Active.Where(t => !t.Done).ToList();
            if (live.Count == 0) return Shot.Orbit;
            var lead = live.OrderByDescending(t => (int)t.State).ThenByDescending(t => t.Vehicle.transform.position.z).First();
            var m = Session.Map;
            Shot s;
            bool crowdOk = Crowd != null && Crowd.Count > 0 && crowdShotFor != lead.Rec?.AttemptId;
            if (m.CrashWallZ > 0)
            {
                if (lead.State == TrackerState.Approach) s = lead.Vehicle.transform.position.z < m.CrashWallZ - 70f ? Shot.Chase : Shot.WallCam;
                else s = lead.StateTime < 3.0f ? Shot.WallCam : Reaction(lead, crowdOk, 3.0f, 2.2f);
            }
            else if (m.DropZ > 0)
            {
                if (lead.State == TrackerState.Hanging) s = Shot.SideTracking;
                else if (lead.State == TrackerState.Airborne) s = Shot.DropCam;
                else s = lead.StateTime < 2.8f ? Shot.DropCam : Reaction(lead, crowdOk, 2.8f, 2.2f);
            }
            else
            {
                switch (lead.State)
                {
                    case TrackerState.Approach:
                        s = lead.Vehicle.transform.position.z < m.RampStartZ - 85f ? (live.Count > 1 ? Shot.SideTracking : Shot.Chase) : Shot.SideTracking;
                        break;
                    case TrackerState.OnRamp: s = Shot.LowAngleLip; break;
                    case TrackerState.Airborne:
                        s = lead.StateTime < 0.9f ? Shot.LowAngleLip : (lead.PredictedLandingZ > 0 ? Shot.ImpactCloseup : Shot.SideTracking);
                        break;
                    case TrackerState.Landed: s = lead.StateTime < 2.4f ? Shot.ImpactCloseup : Reaction(lead, crowdOk, 2.4f, 2.0f); break;
                    default: s = Shot.Orbit; break;
                }
            }
            autoLast = s;
            return s;
        }

        Shot Reaction(AttemptTracker lead, bool crowdOk, float start, float length)
        {
            float t = lead.StateTime - start;
            if (crowdOk && t < length) return Shot.CrowdReaction;
            if (t >= length && Current == Shot.CrowdReaction) crowdShotFor = lead.Rec?.AttemptId;
            return Shot.Orbit;
        }

        Vector3 Focus(out float spread, out Vector3 velocity, out float size)
        {
            spread = 0f;
            size = 4.6f;
            velocity = Vector3.zero;
            List<Transform> ts;
            if (LineupTarget != null && (Current == Shot.Lineup)) ts = new List<Transform> { LineupTarget };
            else if (FocusOverride != null && FocusOverride.Count > 0) ts = FocusOverride.Where(t => t != null).ToList();
            else if (Session != null && Session.Active.Count > 0) ts = Session.Active.Where(t => !t.Done || t.State == TrackerState.Done).Select(t => t.Vehicle.transform).ToList();
            else if (Session != null && Session.Vehicles.Count > 0) ts = Session.Vehicles.Values.Select(v => v.transform).ToList();
            else return Vector3.zero;
            if (ts.Count == 0) return Vector3.zero;
            Vector3 c = Vector3.zero;
            float len = 0f;
            foreach (var t in ts)
            {
                c += t.position;
                var vc = t.GetComponent<VehicleController>();
                len = Mathf.Max(len, vc != null && vc.Def != null && vc.Def.Length > 0.5f ? vc.Def.Length : 4.6f);
            }
            c /= ts.Count;
            foreach (var t in ts) spread = Mathf.Max(spread, Vector3.Distance(c, t.position));
            size = len + spread * 2f;
            var rb = ts[0].GetComponent<Rigidbody>();
            if (rb != null) velocity = rb.linearVelocity;
            return c;
        }

        /// <summary>Vertical FOV that makes a subject of 'size' metres fill 'fill' of the frame WIDTH at 'dist'.</summary>
        static float Lens(float size, float dist, float fill, float min, float max) => Mylti.Domain.Lens.FovFor(size, dist, fill, min, max);

        void Evaluate(Shot s, MapContext map, Vector3 f, float spread, Vector3 fv, float size, float dt, out Vector3 pos, out Quaternion rot, out float fov, out float handheld)
        {
            var rig = RigFor(s);
            if (!rig.Live)
            {
                rig.HasAnchor = false;
                rig.Age = 0f;
                // the orbit starts where the camera already is, so the blend into it is seamless
                if (s == Shot.Orbit) rig.Orbit = Mathf.Atan2(transform.position.z - f.z, transform.position.x - f.x) * Mathf.Rad2Deg;
            }
            var w = Compose(s, rig, map, f, spread, fv, size);
            w.Pos = AvoidOcclusion(w.Aim, w.Pos);
            if (!rig.Live)
            {
                rig.Live = true;
                rig.Pos = w.Pos; rig.Aim = w.Aim; rig.Fov = w.Fov;
                rig.PosVel = rig.AimVel = Vector3.zero; rig.FovVel = 0f;
            }
            else
            {
                rig.Age += dt;
                rig.Pos = w.PosSmooth <= 0f ? w.Pos : Vector3.SmoothDamp(rig.Pos, w.Pos, ref rig.PosVel, w.PosSmooth, Mathf.Infinity, dt);
                rig.Aim = w.AimSmooth <= 0f ? w.Aim : Vector3.SmoothDamp(rig.Aim, w.Aim, ref rig.AimVel, w.AimSmooth, Mathf.Infinity, dt);
                rig.Fov = Mathf.SmoothDamp(rig.Fov, w.Fov, ref rig.FovVel, Mathf.Max(0.01f, w.FovSmooth), Mathf.Infinity, dt);
            }
            pos = rig.Pos;
            Vector3 d = rig.Aim - rig.Pos;
            rot = Quaternion.LookRotation(d.sqrMagnitude > 0.01f ? d : Vector3.forward, Vector3.up);
            fov = rig.Fov;
            handheld = w.Handheld;
        }

        Want Compose(Shot s, Rig rig, MapContext map, Vector3 f, float spread, Vector3 fv, float size)
        {
            float side = map.RampWidth * 0.5f;
            Vector3 lead = new Vector3(fv.x, fv.y * 0.5f, fv.z) * 0.18f;   // aim a little ahead of the car: lead room
            var w = new Want { Aim = f + Vector3.up * 0.7f + lead, Fov = 45f, PosSmooth = 0.25f, AimSmooth = 0.12f, FovSmooth = 0.35f, Handheld = 1f };
            float speed = new Vector3(fv.x, 0, fv.z).magnitude;
            switch (s)
            {
                case Shot.Chase:
                {
                    // low, close follow-car: the vehicle is big in frame and the ramp grows ahead of it
                    Vector3 dir = new Vector3(fv.x, 0, fv.z);
                    dir = dir.sqrMagnitude > 4f ? dir.normalized : Vector3.forward;
                    float back = size * 0.9f + 2f;
                    w.Pos = f - dir * back + Vector3.up * (1.5f + size * 0.12f);
                    w.Aim = f + dir * 7f + Vector3.up * 0.9f;
                    w.Fov = 40f + Mathf.Clamp01(speed / 45f) * 10f;   // speed "breathing" of the lens
                    w.PosSmooth = 0.12f; w.AimSmooth = 0.08f; w.FovSmooth = 0.6f; w.Handheld = 0.5f;
                    break;
                }
                case Shot.SideTracking:
                case Shot.ReplaySide:
                {
                    // tracking vehicle beside the runway with a long lens: car fills ~45 % of the width, crowd compressed behind it
                    float x = side + 16f + spread * 0.6f;
                    w.Pos = new Vector3(x, 2.2f + Mathf.Max(0f, f.y) * 0.45f, f.z - 5f);
                    float dist = Vector3.Distance(w.Pos, f);
                    w.Fov = Lens(size, dist, 0.45f, 12f, 40f);
                    w.PosSmooth = 0.35f; w.AimSmooth = 0.1f; w.FovSmooth = 0.5f; w.Handheld = 0.8f;
                    break;
                }
                case Shot.LowAngleLip:
                case Shot.ReplayLow:
                {
                    // locked-off low camera beside the lip; the operator zooms out as the car arrives and flies over
                    w.Pos = new Vector3(side + 7f, 1.2f, map.LipZ + 16f);
                    float dist = Vector3.Distance(w.Pos, f);
                    w.Fov = Lens(size, dist, 0.5f, 18f, 62f);
                    w.PosSmooth = 0f; w.AimSmooth = 0.07f; w.FovSmooth = 0.25f; w.Handheld = 0.6f;
                    break;
                }
                case Shot.ImpactCloseup:
                {
                    // long-lens landing camera, far to the side and ahead: compresses the landing, fills the frame
                    float lz = ImpactLandingZOverride > 0 ? ImpactLandingZOverride : PredictedLanding(map, f);
                    w.Pos = new Vector3(side + 30f, 3.2f, lz + 24f);
                    float dist = Vector3.Distance(w.Pos, f);
                    w.Fov = Lens(size, dist, 0.5f, 9f, 40f);
                    w.PosSmooth = 0f; w.AimSmooth = 0.1f; w.FovSmooth = 0.4f; w.Handheld = 1f;
                    break;
                }
                case Shot.WallCam:
                {
                    // low, beside the barrier, looking back at the incoming car — the classic crash-test angle
                    float wz = map.CrashWallZ > 0 ? map.CrashWallZ : f.z + 10f;
                    w.Pos = new Vector3(side + 4.5f, 1.0f, wz - 2.5f);
                    w.Aim = Vector3.Lerp(new Vector3(0, 1f, wz), f + Vector3.up * 0.6f, 0.75f);
                    float dist = Vector3.Distance(w.Pos, f);
                    w.Fov = Lens(size + 3f, dist, 0.55f, 16f, 62f);
                    w.PosSmooth = 0f; w.AimSmooth = 0.12f; w.FovSmooth = 0.3f; w.Handheld = 0.4f;
                    break;
                }
                case Shot.DropCam:
                {
                    float dz = map.DropZ > 0 ? map.DropZ : f.z;
                    w.Pos = new Vector3(side + 13f, 0.9f, dz - 13f);
                    w.Aim = f + Vector3.up * 0.5f;
                    float dist = Vector3.Distance(w.Pos, f);
                    w.Fov = Lens(size, dist, 0.45f, 18f, 60f);
                    w.PosSmooth = 0f; w.AimSmooth = 0.1f; w.FovSmooth = 0.3f; w.Handheld = 0.5f;
                    break;
                }
                case Shot.FixedWide:
                    // establishing shot: arena, ramp and the packed stands
                    w.Pos = new Vector3(side + 75f, 28f, map.LipZ + 15f);
                    w.Aim = new Vector3(0, map.LipHeight * 0.5f, Mathf.Lerp(map.LipZ + 30f, f.z, 0.5f));
                    w.Fov = 50f;
                    w.PosSmooth = 0.8f; w.AimSmooth = 0.6f; w.Handheld = 0.3f;
                    break;
                case Shot.TopDown:
                    w.Pos = f + new Vector3(0, 32f + spread, -10f);
                    w.Aim = f;
                    w.Fov = Lens(size, 34f, 0.4f, 15f, 50f);
                    w.PosSmooth = 0.3f; w.AimSmooth = 0.15f; w.Handheld = 0.2f;
                    break;
                case Shot.Orbit:
                {
                    // slow hero orbit, low and close
                    rig.Orbit += Time.deltaTime * 11f;
                    float rad = rig.Orbit * Mathf.Deg2Rad;
                    float r = Mathf.Max(7f, size * 1.25f);
                    w.Pos = f + new Vector3(Mathf.Cos(rad) * r, 1.6f + size * 0.12f, Mathf.Sin(rad) * r);
                    w.Aim = f + Vector3.up * 0.6f;
                    w.Fov = Lens(size, r, 0.55f, 24f, 55f);
                    w.PosSmooth = 0.3f; w.AimSmooth = 0.2f; w.FovSmooth = 0.6f; w.Handheld = 0.4f;
                    break;
                }
                case Shot.CrowdReaction:
                {
                    // cut-away to the nearest front-row fans: tight on a handful of people, slow push-in
                    if (!rig.HasAnchor)
                    {
                        if (Crowd != null && Crowd.ReactionShot(f, out var cp, out var cl)) { rig.Anchor = cp; rig.AnchorLook = cl; rig.HasAnchor = true; }
                        else { rig.Anchor = f + new Vector3(10, 3, 10); rig.AnchorLook = f; rig.HasAnchor = true; }
                    }
                    Vector3 toward = (rig.AnchorLook - rig.Anchor).normalized;
                    w.Pos = rig.Anchor + toward * Mathf.Min(1.2f, rig.Age * 0.35f);
                    w.Aim = rig.AnchorLook;
                    w.Fov = 24f - Mathf.Min(3f, rig.Age * 1.2f);
                    w.PosSmooth = 0.2f; w.AimSmooth = 0.3f; w.FovSmooth = 0.8f; w.Handheld = 1.3f;
                    break;
                }
                case Shot.Lineup:
                    w.Pos = f + new Vector3(4.5f, 1.4f, 5.5f) + Vector3.forward * Mathf.Sin(Time.time * 0.3f);
                    w.Aim = f + Vector3.up * 0.9f;
                    w.Fov = 40f;
                    w.PosSmooth = 0.5f; w.AimSmooth = 0.3f; w.Handheld = 0.5f;
                    break;
                case Shot.Flyover:
                {
                    float t = Mathf.Clamp01(FlyoverT);
                    float z = Mathf.Lerp(-20f, map.LipZ + 140f, t);
                    w.Pos = new Vector3(Mathf.Lerp(30f, -25f, t), Mathf.Lerp(18f, 38f, Mathf.Sin(t * Mathf.PI)), z);
                    w.Aim = new Vector3(0, map.LipHeight * 0.4f, z + 60f);
                    w.Fov = 55f;
                    w.PosSmooth = 0.35f; w.AimSmooth = 0.35f; w.Handheld = 0.2f;
                    break;
                }
                default:
                    w.Pos = transform.position;
                    break;
            }
            return w;
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
                Vector3 cand = desired + Vector3.up * (k * 3f);
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
