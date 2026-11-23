using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mylti.Domain
{
    /// <summary>Generated spectator mesh (bind pose, metres, feet at y = 0, facing +Z). Each vertex belongs to one bone.</summary>
    public sealed class HumanMeshData
    {
        public float Height;
        public Vector3[] Vertices;
        public int[] Cells;       // palette cell (0..255) per vertex
        public int[] Triangles;
        public int[] Bones;       // bone index per vertex
        public Vector3[] BonePos = new Vector3[HumanMesh.BoneCount];
    }

    /// <summary>
    /// Procedural low-poly person for the crowd: 12 bones, varied height/build/skin/hair/cap/clothes.
    /// Pure math (no engine types) so it can be tested and previewed outside Unity.
    /// </summary>
    public static class HumanMesh
    {
        public const int Hips = 0, Spine = 1, Chest = 2, Head = 3, LUpper = 4, LFore = 5, RUpper = 6, RFore = 7, LThigh = 8, LShin = 9, RThigh = 10, RShin = 11, BoneCount = 12;
        public static readonly int[] Parent = { -1, Hips, Spine, Chest, Chest, LUpper, Chest, RUpper, Hips, LThigh, Hips, RThigh };

        // palette rows (16 cells each): skin, shirts, pants, hair, shoes
        public static readonly string[] Skin = { "F1C27D", "E0AC69", "C68642", "8D5524", "FFDBAC", "6B4226" };
        public static readonly string[] Shirts = { "D7263D", "1B98E0", "F4D35E", "FFFFFF", "2E933C", "232323", "F46036", "7B2CBF", "00A6A6", "EE6C4D", "3D5A80", "FF006E" };
        public static readonly string[] Pants = { "2B3A55", "1C1C1C", "5C5C5C", "3E5C76", "7A6C5D", "C9C5BA" };
        public static readonly string[] Hair = { "1A1110", "4E3524", "8B5A2B", "D6B370", "B7B7B7", "A0522D" };
        public static readonly string[] Shoes = { "FFFFFF", "111111", "6D4C41" };
        // row 5: costume colours of the channel's own superhero squad
        public static readonly string[] HeroCols = { "F77F00", "2B2D42", "00A6A6", "7FFFD4", "FFFFFF", "FF4FA3", "8E9AAF", "A7E22E", "B39DDB", "FFC300", "FFE66D", "6A0DAD", "0FA3B1", "FF8FAB", "87CEEB" };

        public enum Emblem { Star, Diamond, Triangle, Ring, Plus, Chevron, Disc }

        /// <summary>One of the channel's ORIGINAL superheroes (invented for MYLTI — not based on any existing character).</summary>
        public sealed class HeroDesign
        {
            public string Name; public int Suit, Accent, Cape, EmblemColor; public Emblem Emblem;
            public HeroDesign(string name, int suit, int accent, int cape, Emblem emblem, int emblemColor)
            { Name = name; Suit = suit; Accent = accent; Cape = cape; Emblem = emblem; EmblemColor = emblemColor; }
        }

        /// <summary>The MYLTI Squad. Indices refer to HeroCols; Cape -1 = no cape.</summary>
        public static readonly HeroDesign[] Heroes =
        {
            new HeroDesign("Turbo Ranger", 0, 1, 2, Emblem.Chevron, 4),
            new HeroDesign("Nova Spark", 3, 4, 5, Emblem.Star, 5),
            new HeroDesign("Gear Guardian", 6, 7, -1, Emblem.Ring, 7),
            new HeroDesign("Comet Kid", 8, 9, 4, Emblem.Diamond, 9),
            new HeroDesign("Rocket Rex", 10, 11, 11, Emblem.Triangle, 11),
            new HeroDesign("Pixel Knight", 12, 0, -1, Emblem.Plus, 0),
            new HeroDesign("Aurora Dash", 13, 14, 14, Emblem.Disc, 4),
        };

        public static string PaletteHex(int cell)
        {
            int row = cell / 16, i = cell % 16;
            string[] r = row == 0 ? Skin : row == 1 ? Shirts : row == 2 ? Pants : row == 3 ? Hair : row == 4 ? Shoes : row == 5 ? HeroCols : null;
            return r != null && i < r.Length ? r[i] : "FF00FF";
        }

        static readonly Vector3 Forward = Vector3.UnitZ;
        static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<int> Cells = new List<int>();
            public readonly List<int> T = new List<int>();
            public readonly List<int> Bone = new List<int>();

            int Vert(Vector3 p, int uv, int bone)
            {
                V.Add(p); Cells.Add(uv); Bone.Add(bone);
                return V.Count - 1;
            }

            /// <summary>Elliptic tube from a to b (radii ra→rb, depth ratio dz) with end caps.</summary>
            public void Tube(Vector3 a, Vector3 b, float ra, float rb, float dz, int uv, int bone, int seg = 10)
            {
                Vector3 axis = Vector3.Normalize(b - a);
                Vector3 side = Vector3.Cross(axis, Forward).LengthSquared() > 1e-4f ? Vector3.Normalize(Vector3.Cross(axis, Forward)) : Vector3.UnitX;
                Vector3 fwd = Vector3.Normalize(Vector3.Cross(side, axis));
                int baseA = V.Count;
                for (int ring = 0; ring < 2; ring++)
                {
                    Vector3 c = ring == 0 ? a : b;
                    float r = ring == 0 ? ra : rb;
                    for (int s = 0; s < seg; s++)
                    {
                        float ang = s * MathF.PI * 2f / seg;
                        Vert(c + side * MathF.Cos(ang) * r + fwd * MathF.Sin(ang) * r * dz, uv, bone);
                    }
                }
                for (int s = 0; s < seg; s++)
                {
                    int s1 = (s + 1) % seg;
                    int i0 = baseA + s, i1 = baseA + s1, j0 = baseA + seg + s, j1 = baseA + seg + s1;
                    T.Add(i0); T.Add(j0); T.Add(i1);
                    T.Add(i1); T.Add(j0); T.Add(j1);
                }
                int ca = Vert(a, uv, bone), cb = Vert(b, uv, bone);
                for (int s = 0; s < seg; s++)
                {
                    int s1 = (s + 1) % seg;
                    T.Add(ca); T.Add(baseA + s); T.Add(baseA + s1);
                    T.Add(cb); T.Add(baseA + seg + s1); T.Add(baseA + seg + s);
                }
            }

            /// <summary>Flat double-sided shape facing ±Z (fan from 'centre'), for chest emblems.</summary>
            public void Flat(Vector3 origin, float size, (float x, float y)[] pts, (float x, float y) centre, int uv, int bone)
            {
                int c = Vert(origin + new Vector3(centre.x * size, centre.y * size, 0), uv, bone);
                int start = V.Count;
                foreach (var q in pts) Vert(origin + new Vector3(q.x * size, q.y * size, 0), uv, bone);
                for (int i = 0; i < pts.Length; i++)
                {
                    int a = start + i, b = start + (i + 1) % pts.Length;
                    T.Add(c); T.Add(a); T.Add(b);
                    T.Add(c); T.Add(b); T.Add(a);
                }
            }

            /// <summary>Ellipsoid (optionally only the part above 'cutY' in local units, for hair and caps).</summary>
            public void Ellipsoid(Vector3 c, Vector3 r, int uv, int bone, float minLat = -1f, int lat = 8, int lon = 12)
            {
                int start = V.Count;
                for (int i = 0; i <= lat; i++)
                {
                    float v = Lerp(MathF.Asin(Math.Clamp(minLat, -1f, 1f)), MathF.PI * 0.5f, i / (float)lat);
                    float y = MathF.Sin(v), rr = MathF.Cos(v);
                    for (int j = 0; j <= lon; j++)
                    {
                        float u = j * MathF.PI * 2f / lon;
                        Vert(c + new Vector3(MathF.Cos(u) * rr * r.X, y * r.Y, MathF.Sin(u) * rr * r.Z), uv, bone);
                    }
                }
                for (int i = 0; i < lat; i++)
                    for (int j = 0; j < lon; j++)
                    {
                        int a = start + i * (lon + 1) + j, b = a + lon + 1;
                        T.Add(a); T.Add(b); T.Add(a + 1);
                        T.Add(a + 1); T.Add(b); T.Add(b + 1);
                    }
            }
        }

        static (float x, float y)[] Poly(int n, float r0, float r1, float rot = 0f)
        {
            var a = new (float x, float y)[n];
            for (int i = 0; i < n; i++)
            {
                float ang = rot + i * MathF.PI * 2f / n, r = i % 2 == 0 ? r0 : r1;
                a[i] = (MathF.Sin(ang) * r, MathF.Cos(ang) * r);
            }
            return a;
        }

        static void AddEmblem(Builder b, Emblem e, Vector3 at, float size, int col, int back)
        {
            var c0 = (0f, 0f);
            switch (e)
            {
                case Emblem.Star: b.Flat(at, size, Poly(10, 1f, 0.45f), c0, col, Chest); break;
                case Emblem.Diamond: b.Flat(at, size, new[] { (0f, 1f), (0.68f, 0f), (0f, -1f), (-0.68f, 0f) }, c0, col, Chest); break;
                case Emblem.Triangle: b.Flat(at, size, new[] { (0f, 1f), (0.85f, -0.7f), (-0.85f, -0.7f) }, c0, col, Chest); break;
                case Emblem.Disc: b.Flat(at, size, Poly(16, 0.9f, 0.9f), c0, col, Chest); break;
                case Emblem.Ring:
                    b.Flat(at, size, Poly(16, 0.95f, 0.95f), c0, col, Chest);
                    b.Flat(at + new Vector3(0, 0, 0.002f), size, Poly(16, 0.55f, 0.55f), c0, back, Chest); break;
                case Emblem.Plus:
                    b.Flat(at, size, new[] { (-0.3f, 0.95f), (0.3f, 0.95f), (0.3f, -0.95f), (-0.3f, -0.95f) }, c0, col, Chest);
                    b.Flat(at + new Vector3(0, 0, 0.001f), size, new[] { (-0.95f, 0.3f), (0.95f, 0.3f), (0.95f, -0.3f), (-0.95f, -0.3f) }, c0, col, Chest); break;
                case Emblem.Chevron:
                    b.Flat(at, size, new[] { (0f, 0.8f), (1f, -0.2f), (0.7f, -0.5f), (0f, 0.2f), (-0.7f, -0.5f), (-1f, -0.2f) }, (0f, 0.5f), col, Chest); break;
            }
        }

        public static HumanMeshData Build(int seed) => Build(seed, -1, false);

        /// <summary>A spectator. hero ≥ 0 dresses them as one of the channel's own superheroes; kid = child proportions.</summary>
        public static HumanMeshData Build(int seed, int hero, bool kid)
        {
            var rng = new System.Random(9173 + seed * 31);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var hd = hero >= 0 ? Heroes[hero % Heroes.Length] : null;
            var vr = new HumanMeshData { Height = kid ? R(1.08f, 1.38f) : R(1.58f, 1.92f) };
            float H = vr.Height;
            float bulk = R(0.9f, 1.25f) * (hd != null && !kid ? 1.08f : 1f);
            float headK = kid ? 1.28f : 1f;
            float sw = 0.115f * H * Lerp(1f, bulk, 0.5f);
            int skin = rng.Next(Skin.Length), shirt = 16 + rng.Next(Shirts.Length), pants = 32 + rng.Next(Pants.Length);
            int hair = 48 + rng.Next(Hair.Length), shoes = 64 + rng.Next(Shoes.Length);
            bool longSleeves = rng.NextDouble() < 0.35, cap = rng.NextDouble() < 0.3, longHair = !cap && rng.NextDouble() < 0.3;
            int capCol = 16 + rng.Next(Shirts.Length);
            int gloves = skin, boots = shoes, belt = pants;
            if (hd != null)
            {
                shirt = pants = 80 + hd.Suit;
                gloves = boots = belt = 80 + hd.Accent;
                longSleeves = true; cap = false; longHair = false;
            }

            var p = vr.BonePos;
            p[Hips] = new Vector3(0, 0.53f * H, 0);
            p[Spine] = new Vector3(0, 0.60f * H, 0);
            p[Chest] = new Vector3(0, 0.70f * H, 0);
            p[Head] = new Vector3(0, 0.84f * H, 0);
            p[LUpper] = new Vector3(-sw, 0.805f * H, 0); p[RUpper] = new Vector3(sw, 0.805f * H, 0);
            p[LFore] = new Vector3(-sw * 1.04f, 0.64f * H, 0); p[RFore] = new Vector3(sw * 1.04f, 0.64f * H, 0);
            p[LThigh] = new Vector3(-0.055f * H, 0.52f * H, 0); p[RThigh] = new Vector3(0.055f * H, 0.52f * H, 0);
            p[LShin] = new Vector3(-0.057f * H, 0.285f * H, 0.005f); p[RShin] = new Vector3(0.057f * H, 0.285f * H, 0.005f);

            var b = new Builder();
            float k = H;
            // legs + shoes
            for (int s = -1; s <= 1; s += 2)
            {
                int thigh = s < 0 ? LThigh : RThigh, shin = s < 0 ? LShin : RShin;
                b.Tube(p[thigh], p[shin], 0.042f * k * bulk, 0.033f * k, 1f, pants, thigh);
                var ankle = new Vector3(p[shin].X, 0.05f * k, 0);
                if (hd != null)
                {
                    var knee = Vector3.Lerp(p[shin], ankle, 0.35f);
                    b.Tube(p[shin], knee, 0.031f * k, 0.029f * k, 1f, pants, shin);
                    b.Tube(knee, ankle, 0.031f * k, 0.025f * k, 1f, boots, shin); // boots
                }
                else b.Tube(p[shin], ankle, 0.031f * k, 0.023f * k, 1f, pants, shin);
                b.Ellipsoid(new Vector3(p[shin].X, 0.026f * k, 0.03f * k), new Vector3(0.026f * k, 0.026f * k, 0.062f * k), hd != null ? boots : shoes, shin, -1f, 4, 8); // sole touches y = 0
            }
            // pelvis + torso
            b.Tube(new Vector3(0, 0.50f * k, 0), new Vector3(0, 0.585f * k, 0), 0.088f * k * bulk, 0.085f * k * bulk, 0.68f, pants, Hips);
            b.Tube(new Vector3(0, 0.58f * k, 0), new Vector3(0, 0.69f * k, 0), 0.083f * k * bulk, 0.088f * k * bulk, 0.66f, shirt, Spine);
            b.Tube(new Vector3(0, 0.685f * k, 0), new Vector3(0, 0.80f * k, 0), 0.09f * k * bulk, sw * 0.95f, 0.6f, shirt, Chest);
            b.Ellipsoid(new Vector3(0, 0.80f * k, 0), new Vector3(sw * 1.02f, 0.03f * k, 0.06f * k), shirt, Chest, -0.2f, 4, 12); // shoulders
            if (hd != null)
            {
                b.Tube(new Vector3(0, 0.555f * k, 0), new Vector3(0, 0.575f * k, 0), 0.091f * k * bulk, 0.091f * k * bulk, 0.7f, belt, Hips, 12); // belt
                float rc = 0.09f * k * bulk + (sw * 0.95f - 0.09f * k * bulk) * 0.55f;
                AddEmblem(b, hd.Emblem, new Vector3(0, 0.748f * k, rc * 0.6f + 0.006f * k), 0.042f * k * (kid ? 1.15f : 1f), 80 + hd.EmblemColor, shirt);
                if (hd.Cape >= 0) // cape hangs from the shoulders down the back
                    b.Tube(new Vector3(0, 0.795f * k, -(sw * 0.62f + 0.012f * k)), new Vector3(0, 0.30f * k, -0.15f * k), sw * 0.9f, sw * 1.35f, 0.1f, 80 + hd.Cape, Chest, 12);
            }
            // neck, head, hair / cap
            b.Tube(new Vector3(0, 0.81f * k, 0), new Vector3(0, 0.85f * k, 0.004f * k), 0.026f * k, 0.024f * k, 1f, skin, Head, 8);
            var hr = new Vector3(0.055f * k, 0.066f * k, 0.061f * k) * headK;
            var hc = new Vector3(0, 0.905f * k + (headK - 1f) * 0.05f * k, 0.006f * k);
            b.Ellipsoid(hc, hr, skin, Head, -1f, 8, 12);
            if (hd != null)
            {
                // domino mask with white eye holes
                b.Ellipsoid(hc + new Vector3(0, 0.009f * k * headK, 0.002f * k), new Vector3(hr.X * 1.08f, 0.021f * k * headK, hr.Z * 1.08f), 80 + hd.Accent, Head, -1f, 4, 14);
                for (int s = -1; s <= 1; s += 2)
                    b.Ellipsoid(hc + new Vector3(s * 0.021f * k * headK, 0.009f * k * headK, hr.Z * 1.07f), new Vector3(0.011f * k * headK, 0.008f * k * headK, 0.005f * k), 64, Head, -1f, 3, 6);
            }
            else
                for (int s = -1; s <= 1; s += 2) // eyes
                    b.Ellipsoid(hc + new Vector3(s * 0.021f * k * headK, 0.008f * k * headK, hr.Z * 0.93f), new Vector3(0.009f * k * headK, 0.011f * k * headK, 0.006f * k), 48, Head, -1f, 3, 6);
            if (cap)
            {
                b.Ellipsoid(hc + new Vector3(0, 0.012f * k, 0), hr * 1.07f, capCol, Head, 0.15f, 5, 12);
                b.Ellipsoid(hc + new Vector3(0, 0.022f * k, 0.05f * k), new Vector3(0.05f * k, 0.006f * k, 0.045f * k), capCol, Head, -1f, 3, 10); // brim
            }
            else
            {
                b.Ellipsoid(hc + new Vector3(0, 0.006f * k, -0.004f * k), hr * 1.06f, hair, Head, hd != null ? 0.42f : 0.05f, 6, 12);
                if (longHair) b.Ellipsoid(hc + new Vector3(0, -0.025f * k, -0.03f * k), new Vector3(0.058f * k, 0.085f * k, 0.045f * k), hair, Head, -1f, 6, 10);
            }
            // arms (sleeve + forearm + hand)
            for (int s = -1; s <= 1; s += 2)
            {
                int up = s < 0 ? LUpper : RUpper, fo = s < 0 ? LFore : RFore;
                b.Tube(p[up], p[fo], 0.032f * k * bulk, 0.028f * k, 1f, shirt, up);
                var wrist = new Vector3(p[fo].X * 1.02f, 0.495f * k, 0.01f * k);
                b.Tube(p[fo], wrist, 0.026f * k, 0.02f * k, 1f, longSleeves ? shirt : skin, fo);
                b.Ellipsoid(wrist + new Vector3(0, -0.035f * k, 0.004f * k), new Vector3(0.019f * k, 0.04f * k, 0.026f * k), gloves, fo, -1f, 4, 8);
            }

            vr.Vertices = b.V.ToArray(); vr.Cells = b.Cells.ToArray(); vr.Triangles = b.T.ToArray(); vr.Bones = b.Bone.ToArray();
            return vr;
        }

    }
}
