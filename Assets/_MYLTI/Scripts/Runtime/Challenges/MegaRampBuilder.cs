using System.Collections.Generic;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    public enum SurfaceKind { Ground, Runway, Ramp, LandingRamp, Bridge, Obstacle, Scenery, Wall }

    /// <summary>Marks what a collider is, so vehicles can tell ground contacts from obstacle hits.</summary>
    public sealed class SurfaceTag : MonoBehaviour
    {
        public SurfaceKind Kind;
    }

    /// <summary>Anything whose pose is recorded for replays (vehicles, dynamic obstacles).</summary>
    public sealed class Replayable : MonoBehaviour
    {
        public string Id;
        public Rigidbody Body;
    }

    /// <summary>Geometry facts of the currently built map (used by AI, trackers and cameras).</summary>
    public sealed class MapContext
    {
        public RoundSpec Spec;
        public float RampStartZ;
        public float LipZ;
        public float LipHeight;
        public float RampWidth;
        public float[] LaneX;
        public float BridgeStartZ = -1, BridgeEndZ = -1;
        public float LandingRampStartZ = -1, LandingRampEndZ = -1;
        public float WallZ = -1;
        public float TargetZ = -1;
        public float CrashWallZ = -1;   // CrashWall rounds: concrete barrier across the runway
        public float DropZ = -1;        // DropTest rounds: cranes hold the vehicles above this point
        public float DropHeight;
        public float KillY = -4f;
        public float FieldEndZ;
        public float HalfFieldWidth = 140f;
        public readonly List<Replayable> Obstacles = new List<Replayable>();

        public Vector3 SpawnPoint(int lane) => new Vector3(LaneX[Mathf.Clamp(lane, 0, LaneX.Length - 1)], 0.6f, 6f);
        public bool HasBridge => BridgeStartZ >= 0;
        public bool OutOfBounds(Vector3 p) => p.y < KillY || Mathf.Abs(p.x) > HalfFieldWidth || p.z < -70f || p.z > FieldEndZ;
    }

    /// <summary>Builds the Mega Ramp arena procedurally from a RoundSpec (FR-01).</summary>
    public sealed class MegaRampBuilder : MonoBehaviour
    {
        public const float LaneSpacing = 9f;
        public MapContext Current { get; private set; }

        GameObject root;
        Font font;

        public MapContext Build(RoundSpec spec, int laneCount, int seed)
        {
            if (root != null)
            {
                root.SetActive(false); // removes colliders from the physics scene immediately
                Destroy(root);
            }
            if (font == null) font = MaterialFactory.RuntimeFont();
            laneCount = Mathf.Clamp(laneCount, 1, 3);
            root = new GameObject("MegaRamp_Arena_R" + spec.Index);
            var ctx = new MapContext { Spec = spec, LaneX = new float[laneCount] };
            for (int i = 0; i < laneCount; i++) ctx.LaneX[i] = (i - (laneCount - 1) * 0.5f) * LaneSpacing;
            ctx.RampWidth = laneCount * LaneSpacing + 6f;
            ctx.RampStartZ = spec.RunwayLength;

            var asphalt = MaterialFactory.Get(new Color(0.18f, 0.19f, 0.21f), 0.25f); // tinted per strip in RunwayStrip
            var grass = MaterialFactory.Get(new Color(0.30f, 0.52f, 0.24f), 0.1f);   // replaced by textured grass in Ground
            var concrete = MaterialFactory.Textured(TextureFactory.Concrete(), new Vector2(2f, 2f), 0.25f);
            var rampMat = MaterialFactory.Textured(TextureFactory.RampDeck(), Vector2.one, 0.35f);
            var white = MaterialFactory.Get(Color.white, 0.4f);
            var water = MaterialFactory.Get(new Color(0.12f, 0.35f, 0.62f), 0.9f);

            // ------------------------------------------------ ground (split around the bridge pit)
            float fieldEnd = ctx.RampStartZ + 900f;
            ctx.FieldEndZ = fieldEnd - 5f;
            if (spec.BridgeWidth > 0.1f)
            {
                ctx.BridgeStartZ = 70f;
                ctx.BridgeEndZ = 150f;
                Ground(-80f, ctx.BridgeStartZ, grass);
                Ground(ctx.BridgeEndZ, fieldEnd, grass);
                var pitWater = MaterialFactory.Visual(PrimitiveType.Cube, root.transform,
                    new Vector3(0, -6f, (ctx.BridgeStartZ + ctx.BridgeEndZ) * 0.5f), new Vector3(300f, 0.2f, ctx.BridgeEndZ - ctx.BridgeStartZ), water, "Pit_Water");
                ctx.KillY = -3f;
                foreach (float x in ctx.LaneX)
                {
                    var deck = MaterialFactory.Solid(PrimitiveType.Cube, root.transform,
                        new Vector3(x, -0.5f, (ctx.BridgeStartZ + ctx.BridgeEndZ) * 0.5f),
                        new Vector3(spec.BridgeWidth, 1f, ctx.BridgeEndZ - ctx.BridgeStartZ + 0.2f), concrete, "Bridge_Deck");
                    deck.AddComponent<SurfaceTag>().Kind = SurfaceKind.Bridge;
                    // edge stripes (visual)
                    MaterialFactory.Visual(PrimitiveType.Cube, deck.transform, new Vector3(0.48f, 0.51f, 0), new Vector3(0.04f, 0.02f, 1f), MaterialFactory.Get(new Color(1f, 0.85f, 0.1f)), "Edge_R");
                    MaterialFactory.Visual(PrimitiveType.Cube, deck.transform, new Vector3(-0.48f, 0.51f, 0), new Vector3(0.04f, 0.02f, 1f), MaterialFactory.Get(new Color(1f, 0.85f, 0.1f)), "Edge_L");
                }
            }
            else Ground(-80f, fieldEnd, grass);

            // ------------------------------------------------ runway strips + lane lines
            if (ctx.HasBridge)
            {
                RunwayStrip(-40f, ctx.BridgeStartZ, ctx.RampWidth, asphalt);
                RunwayStrip(ctx.BridgeEndZ, ctx.RampStartZ, ctx.RampWidth, asphalt);
            }
            else RunwayStrip(-40f, ctx.RampStartZ, ctx.RampWidth, asphalt);
            for (int i = 0; i <= ctx.LaneX.Length; i++)
            {
                float x = -ctx.RampWidth * 0.5f + 3f + i * LaneSpacing;
                for (float z = 0; z < ctx.RampStartZ - 5; z += 12f)
                {
                    if (ctx.HasBridge && z > ctx.BridgeStartZ - 2 && z < ctx.BridgeEndZ + 2) continue;
                    MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(x, 0.02f, z), new Vector3(0.25f, 0.02f, 5f), white, "LaneLine");
                }
            }
            StartGate(ctx, concrete);

            // ------------------------------------------------ kicker
            var profile = MeshBuilder.KickerProfile(ctx.RampStartZ, spec.KickerHeight, spec.KickerAngleDeg, out float lipZ);
            ctx.LipZ = lipZ;
            ctx.LipHeight = spec.KickerHeight;
            var kicker = new GameObject("Kicker");
            kicker.transform.SetParent(root.transform, false);
            var mesh = MeshBuilder.ExtrudeProfile(profile, ctx.RampWidth, "KickerMesh");
            kicker.AddComponent<MeshFilter>().sharedMesh = mesh;
            kicker.AddComponent<MeshRenderer>().sharedMaterial = rampMat;
            var mc = kicker.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            kicker.AddComponent<SurfaceTag>().Kind = SurfaceKind.Ramp;
            // lip stripe + side rails (visual only, rails would trap vehicles)
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, spec.KickerHeight + 0.02f, lipZ - 0.6f), new Vector3(ctx.RampWidth, 0.05f, 1.2f),
                MaterialFactory.Textured(TextureFactory.Hazard(), new Vector2(ctx.RampWidth / 2f, 1f), 0.4f), "LipStripe");
            // landing zone: worn dirt strip so impacts read clearly on camera
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, 0.008f, lipZ + 135f), new Vector3(ctx.RampWidth + 26f, 0.01f, 270f),
                MaterialFactory.Textured(TextureFactory.Dirt(), new Vector2((ctx.RampWidth + 26f) / 10f, 27f), 0.12f), "LandingDirt");
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (ctx.RampWidth * 0.5f + 0.4f);
                for (int i = 2; i < profile.Count; i += 3)
                {
                    var p = profile[i];
                    MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(x, p.y * 0.5f, p.x), new Vector3(0.5f, Mathf.Max(0.2f, p.y), 0.5f), concrete, "Scaffold");
                }
            }

            // ------------------------------------------------ optional landing ramp
            if (spec.LandingRamp)
            {
                float lh = spec.KickerHeight * 0.55f;
                float start = lipZ + 10f + spec.KickerHeight;
                var lp = MeshBuilder.LandingProfile(start, lh, 22f, 6f);
                ctx.LandingRampStartZ = start;
                ctx.LandingRampEndZ = lp[lp.Count - 1].x;
                var land = new GameObject("LandingRamp");
                land.transform.SetParent(root.transform, false);
                var lm = MeshBuilder.ExtrudeProfile(lp, ctx.RampWidth + 8f, "LandingMesh");
                land.AddComponent<MeshFilter>().sharedMesh = lm;
                land.AddComponent<MeshRenderer>().sharedMaterial = rampMat;
                land.AddComponent<MeshCollider>().sharedMesh = lm;
                land.AddComponent<SurfaceTag>().Kind = SurfaceKind.LandingRamp;
            }

            // ------------------------------------------------ distance boards every 10 m from the lip
            for (int d = 10; d <= 250; d += 10)
            {
                float z = lipZ + d;
                MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, 0.015f, z), new Vector3(ctx.RampWidth + 30f, 0.02f, d % 50 == 0 ? 0.6f : 0.25f), white, "DistLine_" + d);
                if (d % 20 == 0 || d == 10) Board(new Vector3(ctx.RampWidth * 0.5f + 18f, 0, z), d + " m", d % 50 == 0);
                if (d % 20 == 0 || d == 10) Board(new Vector3(-ctx.RampWidth * 0.5f - 18f, 0, z), d + " m", d % 50 == 0);
            }

            // ------------------------------------------------ obstacles / target
            var rng = new System.Random(seed * 31 + spec.Index);
            if (spec.Obstacles == ObstacleSet.BarrelLine || spec.Obstacles == ObstacleSet.BarrelsAndWall)
                BarrelLine(ctx, lipZ + Mathf.Max(18f, spec.WallDistance * 0.65f), rng);
            if (spec.Obstacles == ObstacleSet.CrateWall || spec.Obstacles == ObstacleSet.BarrelsAndWall || spec.Rule == ScoringRule.BeyondTheWall)
            {
                ctx.WallZ = lipZ + spec.WallDistance;
                CrateWall(ctx, ctx.WallZ);
            }
            if (spec.Rule == ScoringRule.TargetZone)
            {
                ctx.TargetZ = lipZ + spec.TargetDistance;
                Target(ctx.TargetZ, ctx);
            }
            if (spec.Rule == ScoringRule.CrashWall)
            {
                ctx.CrashWallZ = Mathf.Min(ctx.RampStartZ - 25f, 230f);
                CrashBarrier(ctx, ctx.CrashWallZ, spec.CrashSpeedKmh);
            }
            if (spec.Rule == ScoringRule.DropTest)
            {
                ctx.DropZ = lipZ + 45f;
                ctx.DropHeight = spec.DropHeightM;
                Cranes(ctx, ctx.DropZ, spec.DropHeightM);
            }

            Scenery(ctx, rng, concrete);
            Dressing(ctx, rng);
            Current = ctx;
            return ctx;
        }

        /// <summary>Crowd, flags, tyre walls, light towers and distant hills — cheap shapes that make the arena feel alive.</summary>
        void Dressing(MapContext ctx, System.Random rng)
        {
            float side = ctx.RampWidth * 0.5f;
            Color[] shirt = { new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.45f, 0.9f), new Color(0.95f, 0.85f, 0.2f), new Color(0.95f, 0.95f, 0.95f), new Color(0.2f, 0.75f, 0.35f), new Color(0.15f, 0.15f, 0.18f) };
            var shirtMats = new Material[shirt.Length];
            for (int i = 0; i < shirt.Length; i++) shirtMats[i] = MaterialFactory.Get(shirt[i], 0.2f);
            // spectators on the grandstands (two tiers)
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                float x0 = sgn * (side + 42f);
                for (int g = 0; g < 4; g++)
                {
                    float zc = 40f + g * 50f;
                    if (ctx.HasBridge && zc > ctx.BridgeStartZ - 10 && zc < ctx.BridgeEndZ + 10) continue;
                    for (int n = 0; n < 60; n++)
                    {
                        float z = zc - 14f + (float)rng.NextDouble() * 28f;
                        int tier = rng.Next(2);
                        float x = x0 - sgn * (2.5f - tier * 2.5f) + (float)(rng.NextDouble() - 0.5) * 1.5f;
                        float y = 4.6f + tier * 1.6f;
                        MaterialFactory.Visual(PrimitiveType.Capsule, root.transform, new Vector3(x, y, z), new Vector3(0.55f, 0.8f, 0.55f), shirtMats[rng.Next(shirtMats.Length)], "Fan");
                    }
                }
            }
            // flags along the runway
            var pole = MaterialFactory.Get(new Color(0.8f, 0.8f, 0.82f), 0.8f, 0.9f);
            for (float z = 20f; z < ctx.RampStartZ - 10f; z += 30f)
            {
                if (ctx.HasBridge && z > ctx.BridgeStartZ - 4 && z < ctx.BridgeEndZ + 4) continue;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float x = sgn * (side + 4f);
                    MaterialFactory.Visual(PrimitiveType.Cylinder, root.transform, new Vector3(x, 3f, z), new Vector3(0.12f, 3f, 0.12f), pole, "FlagPole");
                    MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(x + sgn * 0.0f, 5.2f, z + 0.9f), new Vector3(0.04f, 1.2f, 1.8f), shirtMats[rng.Next(shirtMats.Length - 1)], "Flag");
                }
            }
            // tyre walls lining the landing zone
            var tyreMat = MaterialFactory.Get(new Color(0.07f, 0.07f, 0.07f), 0.2f);
            for (float z = ctx.LipZ + 20f; z < ctx.LipZ + 260f; z += 2.2f)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    MaterialFactory.Visual(PrimitiveType.Cylinder, root.transform, new Vector3(sgn * (side + 13.5f), 0.25f, z), new Vector3(1.2f, 0.25f, 1.2f), tyreMat, "Tyres");
                    MaterialFactory.Visual(PrimitiveType.Cylinder, root.transform, new Vector3(sgn * (side + 13.5f), 0.75f, z), new Vector3(1.2f, 0.25f, 1.2f), tyreMat, "Tyres");
                }
            // light towers
            var lamp = MaterialFactory.Get(new Color(1f, 0.97f, 0.88f), 0.9f, 0f, true);
            for (int i = 0; i < 4; i++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float z = ctx.RampStartZ - 120f + i * 110f;
                    float x = sgn * (side + 60f);
                    MaterialFactory.Visual(PrimitiveType.Cylinder, root.transform, new Vector3(x, 14f, z), new Vector3(0.6f, 14f, 0.6f), pole, "Tower");
                    MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(x, 28.5f, z), new Vector3(3.5f, 1.4f, 0.5f), lamp, "Floodlight");
                }
            // distant hills (beyond the field, softened by fog)
            var hill = MaterialFactory.Textured(TextureFactory.Rock(), new Vector2(3f, 3f), 0.05f, new Color(0.75f, 0.85f, 0.75f));
            for (int i = 0; i < 18; i++)
            {
                float a = (float)i / 18f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.2f;
                float rad = 650f + (float)rng.NextDouble() * 250f;
                float sz = 180f + (float)rng.NextDouble() * 220f;
                var c = new Vector3(Mathf.Cos(a) * rad, -sz * 0.35f, ctx.LipZ + Mathf.Sin(a) * rad);
                MaterialFactory.Visual(PrimitiveType.Sphere, root.transform, c, new Vector3(sz, sz * (0.5f + (float)rng.NextDouble() * 0.4f), sz), hill, "Hill");
            }
        }

        void Ground(float z0, float z1, Material mat)
        {
            mat = MaterialFactory.Textured(TextureFactory.Grass(), new Vector2(300f / 14f, (z1 - z0) / 14f), 0.08f);
            var g = MaterialFactory.Solid(PrimitiveType.Cube, root.transform, new Vector3(0, -0.5f, (z0 + z1) * 0.5f), new Vector3(300f, 1f, z1 - z0), mat, "Ground");
            g.AddComponent<SurfaceTag>().Kind = SurfaceKind.Ground;
        }

        void RunwayStrip(float z0, float z1, float width, Material mat)
        {
            mat = MaterialFactory.Textured(TextureFactory.Asphalt(), new Vector2(width / 6f, (z1 - z0) / 6f), 0.3f);
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, 0.005f, (z0 + z1) * 0.5f), new Vector3(width, 0.01f, z1 - z0), mat, "Runway");
        }

        void StartGate(MapContext ctx, Material mat)
        {
            float hw = ctx.RampWidth * 0.5f + 1.5f;
            var red = MaterialFactory.Get(new Color(0.85f, 0.12f, 0.15f), 0.4f);
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(-hw, 3.5f, 2f), new Vector3(0.8f, 7f, 0.8f), mat, "GatePillarL");
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(hw, 3.5f, 2f), new Vector3(0.8f, 7f, 0.8f), mat, "GatePillarR");
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, 7.2f, 2f), new Vector3(hw * 2f + 0.8f, 1.4f, 0.5f), red, "GateBanner");
            Label(new Vector3(0, 7.2f, 1.6f), "MYLTI CHALLENGE", 60, Color.white, Quaternion.identity);
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, 0.02f, 2f), new Vector3(ctx.RampWidth, 0.02f, 0.8f), MaterialFactory.Get(Color.white), "StartLine");
        }

        void Board(Vector3 pos, string text, bool big)
        {
            var post = MaterialFactory.Get(new Color(0.25f, 0.25f, 0.28f));
            var face = MaterialFactory.Get(big ? new Color(0.95f, 0.75f, 0.1f) : new Color(0.95f, 0.95f, 0.95f));
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, pos + new Vector3(0, 1.5f, 0), new Vector3(0.2f, 3f, 0.2f), post, "BoardPost");
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, pos + new Vector3(0, 3.6f, 0), new Vector3(0.2f, 1.6f, 3.6f), face, "Board");
            float side = Mathf.Sign(pos.x);
            Label(pos + new Vector3(-side * 0.12f, 3.6f, 0), text, big ? 70 : 55, Color.black, Quaternion.Euler(0, side > 0 ? 90f : -90f, 0));
        }

        void Label(Vector3 pos, string text, int size, Color color, Quaternion rot)
        {
            if (font == null) return;
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = text;
            tm.fontSize = size;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        void BarrelLine(MapContext ctx, float z, System.Random rng)
        {
            var orange = MaterialFactory.Get(new Color(0.95f, 0.45f, 0.08f), 0.5f);
            var stripe = MaterialFactory.Get(Color.white, 0.5f);
            float half = ctx.RampWidth * 0.5f + 4f;
            int k = 0;
            for (float x = -half; x <= half; x += 2.4f)
            {
                var b = MaterialFactory.Solid(PrimitiveType.Cylinder, root.transform, new Vector3(x + (float)(rng.NextDouble() - 0.5) * 0.4f, 0.65f, z), new Vector3(1f, 0.65f, 1f), orange, "Barrel_" + k);
                MaterialFactory.Visual(PrimitiveType.Cylinder, b.transform, new Vector3(0, 0.2f, 0), new Vector3(1.02f, 0.12f, 1.02f), stripe, "Stripe");
                AddProp(ctx, b, 60f, "barrel_" + (k++));
            }
        }

        void CrateWall(MapContext ctx, float z)
        {
            var wood = MaterialFactory.Textured(TextureFactory.RampDeck(), new Vector2(1f, 0.3f), 0.2f, new Color(0.75f, 0.55f, 0.35f));
            float half = ctx.RampWidth * 0.5f + 6f;
            int k = 0;
            for (int row = 0; row < 3; row++)
            {
                float offset = row % 2 == 0 ? 0f : 0.85f;
                for (float x = -half + offset; x <= half; x += 1.72f)
                {
                    var c = MaterialFactory.Solid(PrimitiveType.Cube, root.transform, new Vector3(x, 0.83f + row * 1.66f, z), new Vector3(1.65f, 1.65f, 1.65f), wood, "Crate_" + k);
                    AddProp(ctx, c, 70f, "crate_" + (k++));
                }
            }
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, 0.03f, z + 1.2f), new Vector3(ctx.RampWidth + 30f, 0.02f, 0.4f), MaterialFactory.Get(new Color(1f, 0.2f, 0.2f)), "WallLine");
        }

        /// <summary>Immovable reinforced-concrete barrier across all lanes, with a crash-test target face.</summary>
        void CrashBarrier(MapContext ctx, float z, float speedKmh)
        {
            var concrete = MaterialFactory.Textured(TextureFactory.Concrete(), new Vector2(4f, 1.5f), 0.2f, new Color(0.85f, 0.85f, 0.85f));
            float w = ctx.RampWidth + 14f;
            var wall = MaterialFactory.Solid(PrimitiveType.Cube, root.transform, new Vector3(0, 2.2f, z + 2f), new Vector3(w, 4.4f, 4f), concrete, "CrashWall");
            wall.AddComponent<SurfaceTag>().Kind = SurfaceKind.Wall;
            // yellow/black impact face and a dark backing block so it reads as solid on camera
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, 1.4f, z - 0.02f), new Vector3(w, 2.8f, 0.06f),
                MaterialFactory.Textured(TextureFactory.Hazard(), new Vector2(w / 2.4f, 1.2f), 0.3f), "CrashFace");
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, 3.2f, z + 7f), new Vector3(w + 4f, 6.4f, 6f), concrete, "CrashBacking");
            foreach (float x in ctx.LaneX)
                MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(x, 0.02f, z - 12f), new Vector3(3f, 0.02f, 0.5f), MaterialFactory.Get(new Color(1f, 0.15f, 0.1f)), "ImpactLine");
            Board(new Vector3(ctx.RampWidth * 0.5f + 10f, 0, z - 30f), Mathf.RoundToInt(speedKmh) + " km/h", true);
            Board(new Vector3(-ctx.RampWidth * 0.5f - 10f, 0, z - 30f), "CRASH TEST", true);
        }

        /// <summary>One crane per lane holding the vehicle above the drop point (visual only — no colliders).</summary>
        void Cranes(MapContext ctx, float z, float height)
        {
            var steel = MaterialFactory.Get(new Color(0.95f, 0.72f, 0.1f), 0.35f, 0.4f);
            var cable = MaterialFactory.Get(new Color(0.08f, 0.08f, 0.08f), 0.5f);
            float side = ctx.RampWidth * 0.5f + 7f;
            for (int i = 0; i < ctx.LaneX.Length; i++)
            {
                float x = ctx.LaneX[i];
                float towerX = side + i * 4f;
                float top = height + 8f;
                MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(towerX, top * 0.5f, z + i * 0.5f), new Vector3(1.6f, top, 1.6f), steel, "CraneTower");
                float boomLen = towerX - x + 4f;
                MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(towerX - boomLen * 0.5f + 2f, top, z), new Vector3(boomLen, 1f, 1f), steel, "CraneBoom");
                float hookY = height + 3.2f;
                MaterialFactory.Visual(PrimitiveType.Cylinder, root.transform, new Vector3(x, (top + hookY) * 0.5f, z), new Vector3(0.08f, (top - hookY) * 0.5f, 0.08f), cable, "CraneCable");
                MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(x, hookY, z), new Vector3(1.2f, 0.4f, 0.6f), steel, "CraneHook");
            }
            // impact pad
            MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(0, 0.012f, z), new Vector3(ctx.RampWidth + 6f, 0.01f, 14f),
                MaterialFactory.Textured(TextureFactory.Hazard(), new Vector2((ctx.RampWidth + 6f) / 3f, 4f), 0.3f, new Color(0.7f, 0.7f, 0.7f)), "DropPad");
            Board(new Vector3(-ctx.RampWidth * 0.5f - 10f, 0, z - 14f), Mathf.RoundToInt(height) + " m DROP", true);
        }

        void AddProp(MapContext ctx, GameObject go, float mass, string id)
        {
            go.AddComponent<SurfaceTag>().Kind = SurfaceKind.Obstacle;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.Sleep();
            var r = go.AddComponent<Replayable>();
            r.Id = id;
            r.Body = rb;
            ctx.Obstacles.Add(r);
        }

        void Target(float z, MapContext ctx)
        {
            Color[] cols = { new Color(0.95f, 0.15f, 0.15f), Color.white, new Color(0.95f, 0.15f, 0.15f), Color.white, new Color(1f, 0.85f, 0.1f) };
            float[] radii = { 12f, 9.5f, 7f, 4.5f, 2f };
            for (int i = 0; i < radii.Length; i++)
                MaterialFactory.Visual(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.02f + i * 0.004f, z), new Vector3(radii[i] * 2f, 0.01f, radii[i] * 2f), MaterialFactory.Get(cols[i], 0.3f, 0, i == 4), "TargetRing_" + i);
        }

        void Scenery(MapContext ctx, System.Random rng, Material concrete)
        {
            var seat = MaterialFactory.Get(new Color(0.2f, 0.35f, 0.75f));
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (ctx.RampWidth * 0.5f + 42f); // clear of the side-tracking camera lane
                for (int i = 0; i < 4; i++)
                {
                    float z = 40f + i * 50f;
                    if (ctx.HasBridge && z > ctx.BridgeStartZ - 10 && z < ctx.BridgeEndZ + 10) continue;
                    MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(x, 2f, z), new Vector3(8f, 4f, 30f), seat, "Grandstand");
                    MaterialFactory.Visual(PrimitiveType.Cube, root.transform, new Vector3(x + s * 2f, 5f, z), new Vector3(4f, 2f, 30f), concrete, "GrandstandTop");
                }
            }
            var trunk = MaterialFactory.Get(new Color(0.35f, 0.24f, 0.14f));
            var leaves = MaterialFactory.Get(new Color(0.16f, 0.40f, 0.18f), 0.05f);
            for (int i = 0; i < 70; i++)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * 130f;
                if (Mathf.Abs(x) < ctx.RampWidth * 0.5f + 50f) x = Mathf.Sign(x == 0 ? 1 : x) * (ctx.RampWidth * 0.5f + 50f + (float)rng.NextDouble() * 60f);
                float z = (float)rng.NextDouble() * (ctx.LipZ + 300f) - 40f;
                if (ctx.HasBridge && z > ctx.BridgeStartZ - 5 && z < ctx.BridgeEndZ + 5) continue;
                float h = 4f + (float)rng.NextDouble() * 5f;
                MaterialFactory.Visual(PrimitiveType.Cylinder, root.transform, new Vector3(x, h * 0.25f, z), new Vector3(0.6f, h * 0.25f, 0.6f), trunk, "Trunk");
                MaterialFactory.Visual(PrimitiveType.Sphere, root.transform, new Vector3(x, h * 0.6f, z), new Vector3(h * 0.6f, h * 0.7f, h * 0.6f), leaves, "Leaves");
            }
        }
    }
}
