using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Visual soft-body damage on rigid vehicles: impacts dent the body mesh around the contact point
    /// (vertex displacement with falloff), shatter windows and can tear wheels off. Also produces a
    /// "damage %" estimate for scoring (a game number — not a real safety measure).
    /// Every impact is stored with its program time so replays can show the car intact before the hit.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleDamage : MonoBehaviour
    {
        public static Func<double> Now = () => Time.time;
        public static event Action<VehicleDamage, float, Vector3> BigImpact; // (vehicle, deltaV m/s, point)
        public static float Strength = 1f; // global multiplier (settings)

        sealed class Part
        {
            public MeshFilter Filter;
            public Mesh Mesh;
            public Vector3[] Original, Current;
            public Vector3[] OriginalNormals;
            public float[] Moved;
            public bool Dirty;
        }

        struct Impact
        {
            public double T;
            public Vector3 LocalPoint, LocalDir;
            public float Depth, Radius;
            public int Wheel;       // -1 none
            public bool Glass;      // shatter windows near the point
            public float Energy;    // damage energy added (DamagePercent = 100·(1 − e^(−E/600)))
        }

        readonly List<Part> parts = new List<Part>();
        readonly List<Renderer> glass = new List<Renderer>();
        readonly List<Impact> history = new List<Impact>();
        readonly List<double> repairs = new List<double> { double.MinValue };
        VehicleController vc;
        float lastImpactTime = -10f;
        float maxDepth = 0.45f;
        float toughness = 1f;
        float energy;
        bool replaying;

        public float DamagePercent { get; private set; }
        public int WheelsLost { get; private set; }
        public bool GlassBroken { get; private set; }
        public bool Deformable => parts.Count > 0;

        readonly bool[] wheelGone = new bool[4];
        readonly GameObject[] looseWheels = new GameObject[4];

        public void Init(VehicleController v, IEnumerable<MeshFilter> bodyMeshes, IEnumerable<Renderer> windows, float vehicleLength)
        {
            vc = v;
            maxDepth = Mathf.Clamp(vehicleLength * 0.09f, 0.25f, 0.9f);
            // heavier vehicles have stronger frames (game rule): 1250 kg → 0.91, 3600 kg → 1.55
            toughness = Mathf.Sqrt(Mathf.Clamp(v.Def.MassKg / 1500f, 0.6f, 4f));
            foreach (var mf in bodyMeshes)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (!mf.sharedMesh.isReadable) continue; // GLTFAST_KEEP_MESH_DATA / Read-Write enabled needed
                if (mf.sharedMesh.vertexCount > 250000) continue;
                var m = Instantiate(mf.sharedMesh);
                m.name = mf.sharedMesh.name + "_dent";
                m.MarkDynamic();
                mf.sharedMesh = m;
                var verts = m.vertices;
                parts.Add(new Part
                {
                    Filter = mf, Mesh = m, Original = verts, Current = (Vector3[])verts.Clone(),
                    OriginalNormals = m.normals, Moved = new float[verts.Length]
                });
            }
            foreach (var r in windows) if (r != null) glass.Add(r);
            vc.Impact += OnImpact;
            vc.VehicleContact += (other, impulse, point) => OnHit(impulse, point, SurfaceKind.Scenery, true);
        }

        void OnDestroy()
        {
            if (vc != null) vc.Impact -= OnImpact;
        }

        void OnImpact(SurfaceKind kind, float impulse, Vector3 point, string other) => OnHit(impulse, point, kind, false);

        void OnHit(float impulse, Vector3 point, SurfaceKind kind, bool vehicle)
        {
            if (replaying || vc == null || vc.ReplayMode) return;
            float dv = impulse / Mathf.Max(1f, vc.Def.MassKg); // m/s change from this contact
            if (dv < 2.2f) return;
            if (Time.time - lastImpactTime < 0.08f && dv < 6f) return;
            lastImpactTime = Time.time;

            float k = Strength * (kind == SurfaceKind.Wall ? 1.25f : 1f);
            var imp = new Impact
            {
                T = Now(),
                LocalPoint = transform.InverseTransformPoint(point),
                Depth = maxDepth * (1f - Mathf.Exp(-(dv - 2.2f) * k / 14f)) / Mathf.Sqrt(toughness),
                Radius = Mathf.Clamp(0.55f + dv * 0.055f, 0.55f, 1.9f) * Mathf.Clamp(vc.Def.Length / 4.5f, 0.8f, 1.8f),
                Wheel = -1,
                Energy = Mathf.Pow(Mathf.Max(0f, dv - 3f), 2f) * k / toughness,
            };
            Vector3 centre = transform.TransformPoint(Vector3.up * vc.Def.Height * 0.4f);
            Vector3 dir = (centre - point).normalized;
            imp.LocalDir = transform.InverseTransformDirection(dir);
            imp.Glass = dv > 9f;
            // a hard hit next to a wheel can tear it off
            if (dv > 18f && WheelsLost < 2)
            {
                int best = -1; float bestD = 1.4f;
                for (int i = 0; i < 4; i++)
                {
                    if (wheelGone[i] || vc.Wheels[i] == null) continue;
                    float d = Vector3.Distance(vc.Wheels[i].transform.position, point);
                    if (d < bestD) { bestD = d; best = i; }
                }
                imp.Wheel = best;
            }
            history.Add(imp);
            Apply(imp, true);
            if (dv > 7f) BigImpact?.Invoke(this, dv, point);
        }

        void Apply(Impact imp, bool live)
        {
            energy += imp.Energy;
            DamagePercent = Percent(energy);
            foreach (var p in parts) Dent(p, imp);
            if (imp.Glass || DamagePercent > 70f) ShatterNear(transform.TransformPoint(imp.LocalPoint), DamagePercent > 70f ? 99f : 1.8f, live);
            if (imp.Wheel >= 0 && !wheelGone[imp.Wheel]) DetachWheel(imp.Wheel, live);
            FlushMeshes();
        }

        void Dent(Part p, Impact imp)
        {
            var t = p.Filter.transform;
            // impact point/direction in this mesh's local space (meshes can be scaled/rotated inside the model)
            Vector3 wp = transform.TransformPoint(imp.LocalPoint);
            Vector3 lp = t.InverseTransformPoint(wp);
            Vector3 ld = t.InverseTransformDirection(transform.TransformDirection(imp.LocalDir)).normalized;
            float scale = Mathf.Max(1e-4f, t.lossyScale.magnitude / 1.732f);
            float r = imp.Radius / scale, depth = imp.Depth / scale, maxD = maxDepth / scale;
            var b = p.Mesh.bounds;
            b.Expand(r * 2f);
            if (!b.Contains(lp)) return;
            float r2 = r * r;
            int seed = (int)(imp.T * 1000.0);
            for (int i = 0; i < p.Current.Length; i++)
            {
                Vector3 d = p.Current[i] - lp;
                float dist2 = d.sqrMagnitude;
                if (dist2 > r2) continue;
                float f = 1f - Mathf.Sqrt(dist2) / r;
                f *= f;
                if (p.Moved[i] >= maxD) continue;
                // small deterministic wrinkle so panels crumple instead of moving as one sheet
                float wrinkle = 0.75f + 0.5f * Mathf.Abs(Mathf.Sin((i * 12.9898f + seed) * 0.37f));
                float amount = Mathf.Min(depth * f * wrinkle, maxD - p.Moved[i]);
                p.Current[i] += ld * amount;
                p.Moved[i] += amount;
                p.Dirty = true;
            }
        }

        void FlushMeshes()
        {
            foreach (var p in parts)
            {
                if (!p.Dirty) continue;
                p.Dirty = false;
                p.Mesh.vertices = p.Current;
                // recompute shading only where the metal moved; keep the artist's normals elsewhere
                p.Mesh.RecalculateNormals();
                if (p.OriginalNormals != null && p.OriginalNormals.Length == p.Current.Length)
                {
                    var n = p.Mesh.normals;
                    for (int i = 0; i < n.Length; i++) if (p.Moved[i] <= 1e-5f) n[i] = p.OriginalNormals[i];
                    p.Mesh.normals = n;
                }
                p.Mesh.RecalculateBounds();
            }
        }

        void ShatterNear(Vector3 point, float radius, bool live)
        {
            foreach (var g in glass)
            {
                if (g == null || !g.enabled) continue;
                if (radius < 50f && g.bounds.SqrDistance(point) > radius * radius) continue;
                g.enabled = false;
                GlassBroken = true;
                if (live && VfxDirector.Instance != null) VfxDirector.Instance.GlassShatter(g.bounds.center, vc.Body != null ? vc.Body.linearVelocity : Vector3.zero);
            }
        }

        void DetachWheel(int i, bool live)
        {
            wheelGone[i] = true;
            WheelsLost++;
            var wc = vc.Wheels[i];
            var vis = vc.WheelVisuals[i];
            if (wc != null) wc.enabled = false;
            if (vis == null) return;
            if (!live) { vis.gameObject.SetActive(false); return; }
            // spawn a loose physical copy of the wheel
            var loose = Instantiate(vis.gameObject, vis.position, vis.rotation);
            loose.name = "LooseWheel_" + i;
            foreach (var c in loose.GetComponentsInChildren<Collider>()) Destroy(c);
            var rb = loose.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(vc.Def.MassKg / 50f, 15f, 120f);
            var sc = loose.AddComponent<SphereCollider>();
            sc.radius = Mathf.Max(0.2f, vc.Def.WheelRadius / Mathf.Max(0.01f, loose.transform.lossyScale.x));
            Vector3 v = vc.Body != null ? vc.Body.linearVelocity : Vector3.zero;
            rb.linearVelocity = v + (vis.position - transform.position).normalized * 4f + Vector3.up * 3f;
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 12f;
            looseWheels[i] = loose;
            vis.gameObject.SetActive(false);
            Destroy(loose, 25f);
        }

        /// <summary>Fresh car: original meshes, windows and wheels (called when the vehicle is placed for a new attempt).</summary>
        public void Repair()
        {
            if (replaying) return;
            repairs.Add(Now());
            ResetVisual();
        }

        void ResetVisual()
        {
            DamagePercent = 0;
            energy = 0;
            WheelsLost = 0;
            GlassBroken = false;
            foreach (var p in parts)
            {
                Array.Copy(p.Original, p.Current, p.Original.Length);
                Array.Clear(p.Moved, 0, p.Moved.Length);
                p.Mesh.vertices = p.Current;
                if (p.OriginalNormals != null && p.OriginalNormals.Length == p.Current.Length) p.Mesh.normals = p.OriginalNormals;
                p.Mesh.RecalculateBounds();
            }
            foreach (var g in glass) if (g != null) g.enabled = true;
            for (int i = 0; i < 4; i++)
            {
                if (looseWheels[i] != null) Destroy(looseWheels[i]);
                looseWheels[i] = null;
                if (!wheelGone[i]) continue;
                wheelGone[i] = false;
                if (vc.Wheels[i] != null) vc.Wheels[i].enabled = !vc.ReplayMode;
                if (vc.WheelVisuals[i] != null) vc.WheelVisuals[i].gameObject.SetActive(true);
            }
        }

        /// <summary>Game damage estimate from accumulated impact energy (saturates towards 100 %).</summary>
        public static float Percent(float e) => 100f * (1f - Mathf.Exp(-e / 600f));

        // ------------------------------------------------------------------ replay support
        int replayIndex;
        double replayLife;

        /// <summary>Show the car as it was at program time t (intact since the last repair before t).</summary>
        public void BeginReplay(double t)
        {
            replaying = true;
            replayLife = double.MinValue;
            foreach (var r in repairs) if (r <= t) replayLife = r;
            ResetVisual();
            replayIndex = 0;
            ReplayTo(t);
        }

        public void ReplayTo(double t)
        {
            if (!replaying) return;
            bool any = false;
            while (replayIndex < history.Count && history[replayIndex].T <= t)
            {
                var imp = history[replayIndex++];
                if (imp.T < replayLife) continue;
                // impacts after the next repair belong to a later attempt
                bool laterLife = false;
                foreach (var r in repairs) if (r > replayLife && r <= imp.T) { laterLife = true; break; }
                if (laterLife) continue;
                energy += imp.Energy;
                DamagePercent = Percent(energy);
                foreach (var p in parts) Dent(p, imp);
                if (imp.Glass || DamagePercent > 70f) ShatterNear(transform.TransformPoint(imp.LocalPoint), DamagePercent > 70f ? 99f : 1.8f, true);
                if (imp.Wheel >= 0 && !wheelGone[imp.Wheel]) { wheelGone[imp.Wheel] = true; WheelsLost++; if (vc.WheelVisuals[imp.Wheel] != null) vc.WheelVisuals[imp.Wheel].gameObject.SetActive(false); }
                any = true;
                if (VfxDirector.Instance != null) VfxDirector.Instance.ImpactBurst(transform.TransformPoint(imp.LocalPoint), Mathf.Sqrt(imp.Energy) * 1.5f);
            }
            if (any) FlushMeshes();
        }

        /// <summary>Back to the live state (re-applies everything since the latest repair).</summary>
        public void EndReplay()
        {
            if (!replaying) return;
            replaying = false;
            double life = repairs[repairs.Count - 1];
            ResetVisual();
            foreach (var imp in history) if (imp.T >= life) Apply(imp, false);
        }
    }
}
