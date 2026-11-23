using System.Collections.Generic;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Original, smooth vehicle bodies built by lofting rounded cross-sections (superellipses) along the car.
    /// Generic designs only — no real manufacturer's shape, badge or name. Visual only: physics colliders stay boxes.
    /// </summary>
    public static class CarBodyBuilder
    {
        public struct Station
        {
            public float Z, Bottom, Top, HalfWidth;
            public Station(float z, float bottom, float top, float halfWidth) { Z = z; Bottom = bottom; Top = top; HalfWidth = halfWidth; }
        }

        /// <summary>Loft a closed, smooth-shaded body through the stations (rear → front). exponent: 2 = round, 8+ = boxy.</summary>
        public static Mesh Loft(IList<Station> st, float exponent, int ring = 24)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            float e = 2f / exponent;
            for (int i = 0; i < st.Count; i++)
            {
                var s = st[i];
                float mid = (s.Top + s.Bottom) * 0.5f, hh = Mathf.Max(0.01f, (s.Top - s.Bottom) * 0.5f);
                for (int k = 0; k < ring; k++)
                {
                    float a = (float)k / ring * Mathf.PI * 2f;
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    float x = s.HalfWidth * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e);
                    float y = mid + hh * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), e);
                    v.Add(new Vector3(x, y, s.Z));
                    uv.Add(new Vector2((float)k / ring, (float)i / (st.Count - 1)));
                }
            }
            for (int i = 0; i < st.Count - 1; i++)
                for (int k = 0; k < ring; k++)
                {
                    int a = i * ring + k, b = i * ring + (k + 1) % ring, c = (i + 1) * ring + k, d = (i + 1) * ring + (k + 1) % ring;
                    // Unity: normal = cross(v1-v0, v2-v0); this order points outward
                    t.Add(a); t.Add(b); t.Add(c);
                    t.Add(b); t.Add(d); t.Add(c);
                }
            // flat caps (separate vertices so the ends shade flat)
            AddCap(v, uv, t, 0, ring, false);
            AddCap(v, uv, t, (st.Count - 1) * ring, ring, true);
            var m = new Mesh { name = "MYLTI_Loft" };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void AddCap(List<Vector3> v, List<Vector2> uv, List<int> t, int start, int ring, bool front)
        {
            Vector3 center = Vector3.zero;
            for (int k = 0; k < ring; k++) center += v[start + k];
            center /= ring;
            int c = v.Count;
            v.Add(center); uv.Add(new Vector2(0.5f, 0.5f));
            int first = v.Count;
            for (int k = 0; k < ring; k++) { v.Add(v[start + k]); uv.Add(Vector2.zero); }
            for (int k = 0; k < ring; k++)
            {
                int a = first + k, b = first + (k + 1) % ring;
                if (front) { t.Add(c); t.Add(a); t.Add(b); }   // faces +Z
                else { t.Add(c); t.Add(b); t.Add(a); }         // faces -Z
            }
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void Light(Transform parent, Vector3 pos, Vector3 size, Material mat, string name)
            => MaterialFactory.Visual(PrimitiveType.Cube, parent, pos, size, mat, name);

        /// <summary>Build the visual body for a vehicle. 'bottom' = chassis underside height used by the colliders.</summary>
        public static void Build(VehicleDefinition d, Transform body, float bottom, Material paint)
        {
            float L = d.Length, W = d.Width, h = L * 0.5f, b = bottom;
            var glass = MaterialFactory.Get(new Color(0.05f, 0.07f, 0.10f), 0.95f, 0.2f);
            var dark = MaterialFactory.Get(new Color(0.07f, 0.07f, 0.08f), 0.35f);
            var chrome = MaterialFactory.Get(new Color(0.85f, 0.86f, 0.88f), 0.92f, 0.95f);
            var head = MaterialFactory.Get(new Color(1f, 0.96f, 0.85f), 0.9f, 0f, true);
            var tail = MaterialFactory.Get(new Color(1f, 0.06f, 0.04f), 0.9f, 0f, true);
            var white = MaterialFactory.Get(new Color(0.95f, 0.95f, 0.95f), 0.7f);

            switch (d.BodyStyle)
            {
                case "truck":
                {
                    // chassis rails
                    Light(body, new Vector3(0, b + 0.25f, 0), new Vector3(W * 0.7f, 0.35f, L * 0.98f), dark, "Frame");
                    float cabL = 2.3f, cz0 = h - cabL, cb = b + 0.45f;
                    var cab = new List<Station>
                    {
                        new Station(cz0,              cb,        cb + 1.95f, W * 0.49f),
                        new Station(cz0 + 0.2f,       cb - 0.05f, cb + 2.05f, W * 0.5f),
                        new Station(h - 0.45f,        cb - 0.05f, cb + 2.0f,  W * 0.5f),
                        new Station(h - 0.12f,        cb,         cb + 1.6f,  W * 0.49f),
                        new Station(h,                cb + 0.1f,  cb + 1.1f,  W * 0.46f),
                    };
                    Part(body, "Cab", Loft(cab, 6f), paint);
                    var wind = new List<Station>
                    {
                        new Station(h - 0.55f, cb + 1.15f, cb + 1.85f, W * 0.44f),
                        new Station(h - 0.30f, cb + 1.1f,  cb + 1.75f, W * 0.45f),
                        new Station(h - 0.12f, cb + 1.05f, cb + 1.55f, W * 0.46f),
                    };
                    Part(body, "Windshield", Loft(wind, 6f), glass);
                    Light(body, new Vector3(0, cb + 0.55f, h + 0.01f), new Vector3(W * 0.62f, 0.55f, 0.06f), chrome, "Grille");
                    Light(body, new Vector3(-W * 0.36f, cb + 0.55f, h + 0.02f), new Vector3(0.32f, 0.2f, 0.05f), head, "HeadL");
                    Light(body, new Vector3(W * 0.36f, cb + 0.55f, h + 0.02f), new Vector3(0.32f, 0.2f, 0.05f), head, "HeadR");
                    Light(body, new Vector3(0, cb + 2.12f, h - 0.6f), new Vector3(W * 0.6f, 0.08f, 0.25f), head, "RoofLights");
                    Light(body, new Vector3(0, cb - 0.05f, h + 0.08f), new Vector3(W * 0.98f, 0.28f, 0.18f), chrome, "Bumper");
                    // cargo box: near-boxy loft so the edges are softly rounded
                    float boxZ0 = -h + 0.1f, boxZ1 = cz0 - 0.15f;
                    var box = new List<Station>
                    {
                        new Station(boxZ0, cb, cb + 2.35f, W * 0.5f),
                        new Station(boxZ0 + 0.05f, cb - 0.02f, cb + 2.4f, W * 0.51f),
                        new Station(boxZ1 - 0.05f, cb - 0.02f, cb + 2.4f, W * 0.51f),
                        new Station(boxZ1, cb, cb + 2.35f, W * 0.5f),
                    };
                    Part(body, "Cargo", Loft(box, 14f), white);
                    Light(body, new Vector3(0, cb + 1.0f, (boxZ0 + boxZ1) * 0.5f), new Vector3(W * 1.025f, 0.35f, (boxZ1 - boxZ0) * 0.92f), paint, "CargoStripe");
                    Light(body, new Vector3(-W * 0.4f, cb + 0.2f, -h - 0.01f), new Vector3(0.25f, 0.18f, 0.05f), tail, "TailL");
                    Light(body, new Vector3(W * 0.4f, cb + 0.2f, -h - 0.01f), new Vector3(0.25f, 0.18f, 0.05f), tail, "TailR");
                    Light(body, new Vector3(-W * 0.52f, cb + 1.6f, h - 0.5f), new Vector3(0.08f, 0.35f, 0.2f), dark, "MirrorL");
                    Light(body, new Vector3(W * 0.52f, cb + 1.6f, h - 0.5f), new Vector3(0.08f, 0.35f, 0.2f), dark, "MirrorR");
                    Light(body, new Vector3(W * 0.42f, cb + 2.3f, cz0 + 0.1f), new Vector3(0.12f, 0.6f, 0.12f), chrome, "Exhaust");
                    break;
                }
                case "suv":
                {
                    var lower = new List<Station>
                    {
                        new Station(-h,         b + 0.12f, b + 0.80f, W * 0.47f),
                        new Station(-h + 0.12f, b,         b + 0.90f, W * 0.50f),
                        new Station(h - 0.55f,  b,         b + 0.90f, W * 0.50f),
                        new Station(h - 0.15f,  b + 0.03f, b + 0.82f, W * 0.49f),
                        new Station(h,          b + 0.12f, b + 0.66f, W * 0.46f),
                    };
                    Part(body, "Body", Loft(lower, 6f), paint);
                    var cabin = new List<Station>
                    {
                        new Station(-h + 0.15f, b + 0.86f, b + 1.52f, W * 0.45f),
                        new Station(-h + 0.35f, b + 0.86f, b + 1.58f, W * 0.46f),
                        new Station(h - 1.45f,  b + 0.86f, b + 1.58f, W * 0.46f),
                        new Station(h - 0.95f,  b + 0.86f, b + 1.05f, W * 0.47f),
                    };
                    Part(body, "Glass", Loft(cabin, 6f), glass);
                    var roof = new List<Station>
                    {
                        new Station(-h + 0.13f, b + 1.50f, b + 1.62f, W * 0.455f),
                        new Station(h - 1.40f,  b + 1.50f, b + 1.62f, W * 0.465f),
                    };
                    Part(body, "Roof", Loft(roof, 8f), paint);
                    Light(body, new Vector3(-W * 0.3f, b + 1.66f, -0.2f), new Vector3(0.06f, 0.06f, L * 0.45f), chrome, "RailL");
                    Light(body, new Vector3(W * 0.3f, b + 1.66f, -0.2f), new Vector3(0.06f, 0.06f, L * 0.45f), chrome, "RailR");
                    Light(body, new Vector3(0, b + 0.2f, h + 0.02f), new Vector3(W * 0.92f, 0.3f, 0.14f), dark, "Bumper");
                    Light(body, new Vector3(0, b + 0.5f, h + 0.005f), new Vector3(W * 0.42f, 0.22f, 0.04f), dark, "Grille");
                    Light(body, new Vector3(-W * 0.34f, b + 0.6f, h - 0.01f), new Vector3(0.38f, 0.12f, 0.05f), head, "HeadL");
                    Light(body, new Vector3(W * 0.34f, b + 0.6f, h - 0.01f), new Vector3(0.38f, 0.12f, 0.05f), head, "HeadR");
                    Light(body, new Vector3(-W * 0.38f, b + 0.62f, -h + 0.01f), new Vector3(0.3f, 0.14f, 0.05f), tail, "TailL");
                    Light(body, new Vector3(W * 0.38f, b + 0.62f, -h + 0.01f), new Vector3(0.3f, 0.14f, 0.05f), tail, "TailR");
                    Light(body, new Vector3(0, b + 0.7f, -h - 0.15f), new Vector3(0.12f, 0.5f, 0.5f), dark, "SpareWheel");
                    break;
                }
                default: // sport
                {
                    var lower = new List<Station>
                    {
                        new Station(-h,          b + 0.14f, b + 0.52f, W * 0.44f),
                        new Station(-h + 0.18f,  b + 0.02f, b + 0.62f, W * 0.50f),
                        new Station(-h * 0.35f,  b,         b + 0.64f, W * 0.50f),
                        new Station(h * 0.25f,   b,         b + 0.54f, W * 0.49f),
                        new Station(h * 0.75f,   b + 0.02f, b + 0.40f, W * 0.47f),
                        new Station(h,           b + 0.06f, b + 0.24f, W * 0.40f),
                    };
                    Part(body, "Body", Loft(lower, 3.2f), paint);
                    var cabin = new List<Station>
                    {
                        new Station(-h * 0.62f, b + 0.58f, b + 0.64f, W * 0.36f),
                        new Station(-h * 0.40f, b + 0.58f, b + 1.02f, W * 0.39f),
                        new Station(-h * 0.02f, b + 0.56f, b + 1.10f, W * 0.40f),
                        new Station(h * 0.20f,  b + 0.52f, b + 0.86f, W * 0.42f),
                        new Station(h * 0.38f,  b + 0.50f, b + 0.56f, W * 0.43f),
                    };
                    Part(body, "Canopy", Loft(cabin, 2.6f), glass);
                    var roof = new List<Station>
                    {
                        new Station(-h * 0.40f, b + 0.98f, b + 1.06f, W * 0.30f),
                        new Station(-h * 0.02f, b + 1.06f, b + 1.13f, W * 0.31f),
                        new Station(h * 0.12f,  b + 0.98f, b + 1.04f, W * 0.30f),
                    };
                    Part(body, "Roof", Loft(roof, 3f), paint);
                    // twin racing stripes + wing + diffuser
                    Light(body, new Vector3(-0.16f, b + 0.545f, h * 0.45f), new Vector3(0.16f, 0.02f, L * 0.34f), white, "StripeL");
                    Light(body, new Vector3(0.16f, b + 0.545f, h * 0.45f), new Vector3(0.16f, 0.02f, L * 0.34f), white, "StripeR");
                    Light(body, new Vector3(0, b + 0.92f, -h + 0.2f), new Vector3(W * 0.88f, 0.05f, 0.32f), dark, "Wing");
                    Light(body, new Vector3(-W * 0.3f, b + 0.76f, -h + 0.22f), new Vector3(0.05f, 0.3f, 0.12f), dark, "WingPostL");
                    Light(body, new Vector3(W * 0.3f, b + 0.76f, -h + 0.22f), new Vector3(0.05f, 0.3f, 0.12f), dark, "WingPostR");
                    Light(body, new Vector3(0, b + 0.06f, -h + 0.1f), new Vector3(W * 0.8f, 0.1f, 0.25f), dark, "Diffuser");
                    Light(body, new Vector3(0, b + 0.04f, h - 0.05f), new Vector3(W * 0.8f, 0.04f, 0.3f), dark, "Splitter");
                    Light(body, new Vector3(-W * 0.31f, b + 0.25f, h - 0.03f), new Vector3(0.34f, 0.07f, 0.12f), head, "HeadL");
                    Light(body, new Vector3(W * 0.31f, b + 0.25f, h - 0.03f), new Vector3(0.34f, 0.07f, 0.12f), head, "HeadR");
                    Light(body, new Vector3(0, b + 0.46f, -h + 0.01f), new Vector3(W * 0.78f, 0.06f, 0.05f), tail, "TailBar");
                    Light(body, new Vector3(-0.25f, b + 0.12f, -h - 0.02f), new Vector3(0.1f, 0.1f, 0.1f), chrome, "ExhaustL");
                    Light(body, new Vector3(0.25f, b + 0.12f, -h - 0.02f), new Vector3(0.1f, 0.1f, 0.1f), chrome, "ExhaustR");
                    break;
                }
            }
        }

        /// <summary>Wheel with tyre, rim, five spokes and a brake disc. Axis = local X.</summary>
        public static void Wheel(Transform pivot, float r, bool leftSide)
        {
            var rubber = MaterialFactory.Get(new Color(0.06f, 0.06f, 0.06f), 0.25f);
            var rim = MaterialFactory.Get(new Color(0.78f, 0.79f, 0.82f), 0.9f, 0.95f);
            var disc = MaterialFactory.Get(new Color(0.35f, 0.35f, 0.37f), 0.6f, 0.8f);
            float side = leftSide ? -1f : 1f;
            var tyre = MaterialFactory.Visual(PrimitiveType.Cylinder, pivot, Vector3.zero, new Vector3(r * 2f, 0.16f, r * 2f), rubber, "Tyre");
            tyre.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            var hub = MaterialFactory.Visual(PrimitiveType.Cylinder, pivot, new Vector3(side * 0.07f, 0, 0), new Vector3(r * 1.25f, 0.035f, r * 1.25f), rim, "Rim");
            hub.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            var dsk = MaterialFactory.Visual(PrimitiveType.Cylinder, pivot, new Vector3(side * 0.02f, 0, 0), new Vector3(r * 1.05f, 0.03f, r * 1.05f), disc, "Disc");
            dsk.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            for (int i = 0; i < 5; i++)
            {
                var spoke = MaterialFactory.Visual(PrimitiveType.Cube, pivot, new Vector3(side * 0.1f, 0, 0), new Vector3(0.04f, r * 1.15f, r * 0.16f), rim, "Spoke" + i);
                spoke.transform.localRotation = Quaternion.Euler(i * 72f, 0, 0);
            }
            var cap = MaterialFactory.Visual(PrimitiveType.Cylinder, pivot, new Vector3(side * 0.11f, 0, 0), new Vector3(r * 0.35f, 0.03f, r * 0.35f), rim, "Cap");
            cap.transform.localRotation = Quaternion.Euler(0, 0, 90f);
        }
    }
}
