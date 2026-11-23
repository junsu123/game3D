using System;
using System.Numerics;

namespace Mylti.Domain
{
    /// <summary>
    /// Procedural spectator animation: idle sway, clapping, cheering with arms up, jumping, fist pumps.
    /// Output = local Euler angles per bone (degrees, Unity order Z→X→Y) + a vertical hip offset.
    /// Shared by the runtime crowd and the tests/preview so they stay identical.
    /// </summary>
    public static class CrowdPose
    {
        public enum Style { Clapper, Waver, Jumper, FistPumper }

        /// <param name="excite">0..1 cheering energy of this person</param>
        /// <param name="applause">0..1 clapping demand (after results)</param>
        /// <param name="t">seconds (person's own clock, already phase-shifted)</param>
        /// <param name="lookYaw">degrees to turn the head toward the action (−70..70)</param>
        /// <summary>Blend weights: calm → light clapping → arms up. Clappers keep clapping unless the moment is huge; applause overrides.</summary>
        public static void Weights(Style style, float excite, float applause, out float idle, out float clap, out float cheer)
        {
            float e = Math.Clamp(excite, 0f, 1f);
            float ap = Math.Clamp(applause, 0f, 1f);
            float c0 = Math.Clamp((e - 0.22f) / 0.25f, 0f, 1f);
            cheer = Smooth(style == Style.Clapper ? (e - 0.85f) / 0.15f : (e - 0.55f) / 0.25f) * (1f - ap);
            clap = Math.Max(ap, c0 * (1f - cheer));
            idle = 1f - Math.Max(clap, cheer);
        }

        public static void Compute(Style style, float excite, float applause, float t, float lookYaw, Vector3[] euler, out float hipLift)
        {
            for (int i = 0; i < euler.Length; i++) euler[i] = Vector3.Zero;
            Weights(style, excite, applause, out float idle, out float clap, out float cheer);
            const float Tau = MathF.PI * 2f;

            // body: sway when calm, bounce when excited
            float sway = MathF.Sin(t * 0.9f) * 3f * idle;
            euler[HumanMesh.Hips] = new Vector3(0, 0, sway);
            euler[HumanMesh.Chest] = new Vector3(-4f * cheer, lookYaw * 0.3f, -sway * 0.6f);
            euler[HumanMesh.Head] = new Vector3(-6f * cheer + MathF.Sin(t * 1.3f) * 2f, lookYaw * 0.6f, 0);

            // arms — idle: hanging with a little swing
            Vector3 lu = new Vector3(MathF.Sin(t * 1.1f) * 4f, 0, -4f), ru = new Vector3(MathF.Sin(t * 1.1f + 1f) * 4f, 0, 4f);
            Vector3 lf = new Vector3(-8f, 0, 0), rf = new Vector3(-8f, 0, 0);

            // clapping: arms forward, forearms up, hands meeting ~3.3 times a second
            if (clap > 0)
            {
                float c = (MathF.Sin(t * Tau * 3.3f) + 1f) * 0.5f; // 0 apart → 1 together
                var cu = new Vector3(-36f, 0, 24f + 11f * c);
                var cf = new Vector3(-64f, 0, 0);
                lu = Vector3.Lerp(lu, cu, clap);
                ru = Vector3.Lerp(ru, new Vector3(cu.X, 0, -cu.Z), clap);
                lf = Vector3.Lerp(lf, cf, clap);
                rf = Vector3.Lerp(rf, cf, clap);
            }
            // cheering: arms overhead and waving, or fist pumps, or jumping
            if (cheer > 0)
            {
                float w = MathF.Sin(t * Tau * (style == Style.Waver ? 1.6f : 2.2f));
                Vector3 cl, cr, fl, fr;
                switch (style)
                {
                    case Style.FistPumper:
                        cl = new Vector3(-10f, 0, -20f);
                        cr = new Vector3(-20f, 0, 140f + 25f * w);
                        fl = new Vector3(-90f, 0, 0);
                        fr = new Vector3(-35f - 25f * w, 0, 0);
                        break;
                    default:
                        cl = new Vector3(-15f, 0, -150f + 15f * w);
                        cr = new Vector3(-15f, 0, 150f + 15f * MathF.Sin(t * Tau * 1.6f + 0.8f));
                        fl = new Vector3(-12f, 0, 0);
                        fr = new Vector3(-12f, 0, 0);
                        break;
                }
                lu = Vector3.Lerp(lu, cl, cheer);
                ru = Vector3.Lerp(ru, cr, cheer);
                lf = Vector3.Lerp(lf, fl, cheer);
                rf = Vector3.Lerp(rf, fr, cheer);
            }
            euler[HumanMesh.LUpper] = lu; euler[HumanMesh.RUpper] = ru;
            euler[HumanMesh.LFore] = lf; euler[HumanMesh.RFore] = rf;

            // legs: knees flex on jumps
            float jump = style == Style.Jumper || style == Style.Waver ? cheer : cheer * 0.35f;
            float hop = MathF.Max(0f, MathF.Sin(t * Tau * 2.0f));
            hipLift = hop * 0.11f * jump;
            float knee = (1f - hop) * 18f * jump;
            euler[HumanMesh.LThigh] = new Vector3(-knee, 0, 0);
            euler[HumanMesh.RThigh] = new Vector3(-knee, 0, 0);
            euler[HumanMesh.LShin] = new Vector3(knee * 1.8f, 0, 0);
            euler[HumanMesh.RShin] = new Vector3(knee * 1.8f, 0, 0);
            hipLift -= knee * 0.0012f;
        }

        static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }

        /// <summary>Unity's Quaternion.Euler(x, y, z): rotate about Z, then X, then Y.</summary>
        public static Quaternion Euler(Vector3 deg)
        {
            const float D = MathF.PI / 180f;
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, deg.Y * D)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitX, deg.X * D)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, deg.Z * D);
        }

        /// <summary>Pose the mesh (forward kinematics, rigid skinning) — for tests and previews.</summary>
        public static Vector3[] Apply(HumanMeshData m, Vector3[] euler, float hipLift)
        {
            int n = HumanMesh.BoneCount;
            var worldRot = new Quaternion[n];
            var worldPos = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                int p = HumanMesh.Parent[i];
                var local = Euler(euler[i]);
                if (p < 0)
                {
                    worldRot[i] = local;
                    worldPos[i] = m.BonePos[i] + new Vector3(0, hipLift, 0);
                }
                else
                {
                    worldRot[i] = worldRot[p] * local;
                    worldPos[i] = worldPos[p] + Vector3.Transform(m.BonePos[i] - m.BonePos[p], worldRot[p]);
                }
            }
            var outV = new Vector3[m.Vertices.Length];
            for (int v = 0; v < outV.Length; v++)
            {
                int b = m.Bones[v];
                outV[v] = worldPos[b] + Vector3.Transform(m.Vertices[v] - m.BonePos[b], worldRot[b]);
            }
            return outV;
        }
    }
}
