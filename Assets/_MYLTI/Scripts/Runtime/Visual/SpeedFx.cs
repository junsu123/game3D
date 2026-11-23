using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Speed effects on a vehicle: red tail-light streaks above ~70 km/h and dust kicked up by the
    /// rear wheels when driving fast on the ground. Purely visual.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class SpeedFx : MonoBehaviour
    {
        VehicleController vc;
        TrailRenderer[] trails;
        float dustAcc;

        public void Init(VehicleController v, float bottom)
        {
            vc = v;
            var d = v.Def;
            var mat = MaterialFactory.Get(new Color(1f, 0.12f, 0.08f), 0.5f, 0f, true);
            trails = new TrailRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("TailTrail" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3((i == 0 ? -1 : 1) * d.Width * 0.36f, bottom + 0.4f, -d.Length * 0.5f - 0.05f);
                var tr = go.AddComponent<TrailRenderer>();
                tr.time = 0.35f;
                tr.startWidth = 0.14f;
                tr.endWidth = 0f;
                tr.minVertexDistance = 0.25f;
                tr.sharedMaterial = mat;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.emitting = false;
                trails[i] = tr;
            }
        }

        void LateUpdate()
        {
            if (vc == null || trails == null) return;
            float kmh = vc.ReplayMode ? 0f : vc.SpeedKmh;
            bool fast = kmh > 70f;
            foreach (var t in trails) if (t != null && t.emitting != fast) t.emitting = fast;

            if (!vc.ReplayMode && vc.GroundedWheels >= 2 && kmh > 45f && VfxDirector.Instance != null)
            {
                dustAcc += Time.deltaTime * Mathf.Lerp(8f, 30f, Mathf.InverseLerp(45f, 160f, kmh));
                while (dustAcc >= 1f)
                {
                    dustAcc -= 1f;
                    var w = vc.Wheels[Random.value < 0.5f ? 2 : 3];
                    if (w != null && w.GetGroundHit(out WheelHit hit)) VfxDirector.Instance.WheelDust(hit.point);
                }
            }
        }

        /// <summary>Clear trails after a teleport so no streak is drawn across the map.</summary>
        public void ClearTrails()
        {
            if (trails == null) return;
            foreach (var t in trails) if (t != null) t.Clear();
        }
    }
}
