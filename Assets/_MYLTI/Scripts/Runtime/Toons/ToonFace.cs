using System.Collections.Generic;
using System.Linq;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Brings a MYLTI Toon to life from what the car is really doing: blinks, looks at the camera when idle, gets a
    /// determined frown at speed, eyes wide + "O" mouth in the air, winces on impacts, big grin after a landing,
    /// panics when upside down — plus cartoon squash & stretch of the body. Works in replays too (motion-based).
    /// </summary>
    public sealed class ToonFace : MonoBehaviour
    {
        static readonly string[] Animated = { "LidL", "LidR", "PupilL", "PupilR", "BrowL", "BrowR", "Mouth" };

        ToonDesign d;
        Transform visual;
        Dictionary<string, Transform> parts;
        VehicleController vc;

        ToonExpression current = ToonExpression.Neutral;
        Vector3 lastPos, lastVel;
        bool hasLast;
        float airTime, happyT, winceT, blinkIn = 2f, blinkT = -1f, winkIn = 9f, winkT = -1f;
        float squash, squashVel;
        public string Mood { get; private set; } = "Neutral";

        public void Init(ToonDesign design, Transform visualRoot, Dictionary<string, Transform> faceParts, VehicleController controller)
        {
            d = design; visual = visualRoot; parts = faceParts; vc = controller;
            blinkIn = Random.Range(1.5f, 4f);
            winkIn = Random.Range(8f, 16f);
            Apply(current, 0f);
        }

        void LateUpdate()
        {
            if (d == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var tr = vc != null ? vc.transform : transform;
            Vector3 pos = tr.position;
            Vector3 vel = hasLast ? (pos - lastPos) / dt : Vector3.zero;
            float accel = hasLast ? (vel - lastVel).magnitude / dt : 0f;
            lastPos = pos; lastVel = vel; hasLast = true;
            if (accel > 400f) accel = 0f; // teleport / replay jump

            bool grounded = vc != null && vc.Wheels != null && vc.Wheels.Any(w => w != null && w.isGrounded);
            bool upside = tr.up.y < 0.2f;
            float speed = new Vector3(vel.x, 0, vel.z).magnitude;

            if (accel > 55f && winceT <= 0f) { winceT = 0.5f; squashVel -= Mathf.Min(3f, accel / 60f); }
            if (!grounded) airTime += dt;
            else
            {
                if (airTime > 0.45f) { happyT = 1.6f; squashVel -= Mathf.Clamp(airTime, 0.5f, 2f) * 1.4f; }
                airTime = 0f;
            }
            winceT -= dt; happyT -= dt;

            // pick the target expression
            ToonExpression target;
            if (upside) { target = ToonExpression.Scared; target.LookX = Mathf.Cos(Time.time * 7f); target.LookY = Mathf.Sin(Time.time * 7f); Mood = "Dizzy"; }
            else if (winceT > 0f) { target = ToonExpression.Wince; Mood = "Wince"; }
            else if (airTime > 0.25f) { target = vel.y < -6f ? ToonExpression.Scared : ToonExpression.Surprised; target.LookY = vel.y < 0 ? -0.7f : 0.3f; Mood = "Air"; }
            else if (happyT > 0f) { target = ToonExpression.Happy; Mood = "Happy"; }
            else if (speed > 12f) { target = ToonExpression.Determined; target.LookY = -0.1f; Mood = "Determined"; }
            else
            {
                target = ToonExpression.Neutral;
                Mood = "Neutral";
                var cam = Camera.main;
                if (cam != null) // look at the camera
                {
                    Vector3 local = tr.InverseTransformPoint(cam.transform.position) - new Vector3(0, d.Top, d.Length * 0.5f);
                    if (local.z > 0f)
                    {
                        target.LookX = Mathf.Clamp(local.x / local.z, -1f, 1f);
                        target.LookY = Mathf.Clamp(local.y / local.z, -1f, 1f);
                    }
                }
                if (speed < 2f)
                {
                    winkIn -= dt;
                    if (winkIn <= 0f) { winkT = 0.7f; winkIn = Random.Range(10f, 20f); }
                }
            }
            if (winkT > 0f) { winkT -= dt; var w = ToonExpression.Cheeky; w.LookX = target.LookX; w.LookY = target.LookY; target = w; }

            float k = 1f - Mathf.Exp(-dt * (Mood == "Wince" || Mood == "Air" ? 22f : 10f));
            current = ToonExpression.Lerp(current, target, k);

            // blink (not while the eyes are squeezed shut or wide in the air)
            float blink = 0f;
            blinkIn -= dt;
            if (blinkIn <= 0f && Mood != "Air" && Mood != "Wince") { blinkT = 0f; blinkIn = Random.Range(2f, 5f); }
            if (blinkT >= 0f)
            {
                blinkT += dt;
                blink = Mathf.Sin(Mathf.Clamp01(blinkT / 0.16f) * Mathf.PI);
                if (blinkT > 0.16f) blinkT = -1f;
            }
            Apply(current, blink);

            // squash & stretch (critically-damped-ish spring), stretch while rising fast
            float want = Mathf.Clamp(vel.y * 0.006f, -0.03f, 0.06f) * (grounded ? 0f : 1f);
            squashVel += ((want - squash) * 180f - squashVel * 16f) * dt;
            squash = Mathf.Clamp(squash + squashVel * dt, -0.22f, 0.12f);
            visual.localScale = new Vector3(1f - squash * 0.5f, 1f + squash, 1f - squash * 0.3f);
        }

        void Apply(ToonExpression x, float blink)
        {
            foreach (var name in Animated)
            {
                if (!parts.TryGetValue(name, out var t) || t == null) continue;
                var e = x;
                if (name.StartsWith("Lid")) { e.LidL = Mathf.Max(e.LidL, blink); e.LidR = Mathf.Max(e.LidR, blink); }
                ToonCars.PartPose(d, name, e, out var p, out var eu, out var s);
                t.localPosition = new Vector3(p.X, p.Y, p.Z);
                t.localRotation = Quaternion.Euler(eu.X, eu.Y, eu.Z);
                t.localScale = new Vector3(s.X, s.Y, s.Z);
            }
        }
    }
}
