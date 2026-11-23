using System;

namespace Mylti.Domain
{
    /// <summary>
    /// Camera-grammar maths shared by the camera director (pure, testable outside Unity).
    /// </summary>
    public static class Lens
    {
        public const float Aspect = 16f / 9f;

        /// <summary>Vertical field of view (deg) that makes a subject 'size' metres wide fill 'fill' of the frame WIDTH at 'dist' metres.</summary>
        public static float FovFor(float size, float dist, float fill, float minDeg, float maxDeg, float aspect = Aspect)
        {
            double v = 2.0 * Math.Atan(size * 0.5 / (Math.Max(0.5, dist) * Math.Max(0.05, fill) * aspect)) * 180.0 / Math.PI;
            return (float)Math.Max(minDeg, Math.Min(maxDeg, v));
        }

        /// <summary>Share of the frame width a subject covers with the given vertical FOV.</summary>
        public static float Fill(float size, float dist, float fovDeg, float aspect = Aspect)
            => (float)(size / (2.0 * Math.Max(0.5, dist) * Math.Tan(fovDeg * 0.5 * Math.PI / 180.0) * aspect));

        /// <summary>
        /// 30° / 20 mm rule: a straight cut between two shots of the same subject looks like a jump cut unless the
        /// viewing angle changes by ≥ 30° or the shot size clearly changes (here: subject size on screen × 1.5).
        /// </summary>
        public static bool WouldJumpCut(float angleBetweenDeg, float fillA, float fillB)
        {
            float ratio = Math.Max(fillA, fillB) / Math.Max(1e-4f, Math.Min(fillA, fillB));
            return angleBetweenDeg < 30f && ratio < 1.5f;
        }

        /// <summary>Smootherstep 0..1 (zero velocity and acceleration at both ends) used for camera blends.</summary>
        public static float Smoother(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
    }
}
