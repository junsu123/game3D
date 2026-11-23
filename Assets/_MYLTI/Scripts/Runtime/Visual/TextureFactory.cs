using System.Collections.Generic;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Procedural, tileable textures (grass, asphalt, concrete, ramp planks, dirt) generated at startup —
    /// original artwork, no third-party image files. Generated once and cached.
    /// </summary>
    public static class TextureFactory
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        static Texture2D Make(string key, int size, System.Func<float, float, Color> pixel)
        {
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "MYLTI_" + key, wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, filterMode = FilterMode.Trilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = pixel((float)x / size, (float)y / size);
            t.SetPixels(px);
            t.Apply(true, false);
            cache[key] = t;
            return t;
        }

        /// <summary>Tileable fractal noise (wraps at u,v = 0/1).</summary>
        static float Noise(float u, float v, float freq, int octaves, float seed)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                float f = freq * (1 << o);
                // sample on a torus so the texture tiles seamlessly
                float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
                float r = f / (Mathf.PI * 2f);
                float n = Mathf.PerlinNoise(seed + Mathf.Cos(a) * r + Mathf.Sin(b) * r * 0.5f, seed * 0.7f + Mathf.Sin(a) * r + Mathf.Cos(b) * r * 0.5f);
                n += Mathf.PerlinNoise(seed * 1.3f + Mathf.Cos(b) * r, seed * 0.3f + Mathf.Sin(b) * r);
                sum += amp * (n * 0.5f);
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        public static Texture2D Grass() => Make("grass", 512, (u, v) =>
        {
            float n = Noise(u, v, 6f, 4, 11.3f);
            float fine = Noise(u, v, 48f, 2, 3.1f);
            float stripe = Mathf.Repeat(u * 4f, 1f) < 0.5f ? 0.035f : -0.035f; // mowing stripes
            var a = new Color(0.20f, 0.42f, 0.15f);
            var b = new Color(0.36f, 0.58f, 0.22f);
            var c = Color.Lerp(a, b, n) * (0.88f + fine * 0.24f);
            return new Color(c.r + stripe * 0.5f, c.g + stripe, c.b + stripe * 0.3f, 1f);
        });

        public static Texture2D Asphalt() => Make("asphalt", 512, (u, v) =>
        {
            float n = Noise(u, v, 10f, 3, 5.7f);
            float speck = Mathf.PerlinNoise(u * 180f, v * 180f);
            float g = 0.16f + n * 0.07f + (speck > 0.78f ? 0.08f : 0f);
            return new Color(g, g, g * 1.04f, 1f);
        });

        public static Texture2D Concrete() => Make("concrete", 256, (u, v) =>
        {
            float n = Noise(u, v, 8f, 3, 2.2f);
            float seam = (Mathf.Repeat(u * 2f, 1f) < 0.01f || Mathf.Repeat(v * 2f, 1f) < 0.01f) ? -0.12f : 0f;
            float g = 0.62f + n * 0.12f + seam;
            return new Color(g, g * 0.98f, g * 0.94f, 1f);
        });

        /// <summary>Ramp deck: painted planks across the width with darker gaps and wear.</summary>
        public static Texture2D RampDeck() => Make("rampdeck", 512, (u, v) =>
        {
            float plank = Mathf.Repeat(v * 8f, 1f);
            float gap = plank < 0.04f ? 0.55f : 1f;
            float grain = Noise(u, v, 24f, 2, 7.9f);
            float wear = Noise(u, v, 4f, 3, 1.5f);
            var paint = new Color(0.93f, 0.50f, 0.12f);
            var wood = new Color(0.55f, 0.38f, 0.22f);
            var c = Color.Lerp(paint, wood, Mathf.Clamp01((wear - 0.55f) * 2.5f)) * (0.85f + grain * 0.25f) * gap;
            c.a = 1f;
            return c;
        });

        public static Texture2D Dirt() => Make("dirt", 512, (u, v) =>
        {
            float n = Noise(u, v, 7f, 4, 9.1f);
            float pebble = Mathf.PerlinNoise(u * 120f, v * 120f) > 0.8f ? -0.06f : 0f;
            var c = Color.Lerp(new Color(0.45f, 0.36f, 0.25f), new Color(0.62f, 0.52f, 0.37f), n);
            return new Color(c.r + pebble, c.g + pebble, c.b + pebble, 1f);
        });

        /// <summary>Black/yellow hazard stripes for the ramp lip and barriers.</summary>
        public static Texture2D Hazard() => Make("hazard", 128, (u, v) =>
            Mathf.Repeat((u + v) * 4f, 1f) < 0.5f ? new Color(0.95f, 0.78f, 0.08f) : new Color(0.08f, 0.08f, 0.09f));

        /// <summary>Simple vertical sky-to-horizon gradient used for distant hills tint.</summary>
        public static Texture2D Rock() => Make("rock", 256, (u, v) =>
        {
            float n = Noise(u, v, 5f, 4, 4.4f);
            var c = Color.Lerp(new Color(0.36f, 0.40f, 0.34f), new Color(0.52f, 0.55f, 0.48f), n);
            c.a = 1f;
            return c;
        });
    }
}
