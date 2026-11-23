using System.Collections.Generic;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Creates coloured materials from the active render pipeline's default lit material.
    /// Copying the material of a primitive guarantees the shader is included in builds
    /// (Shader.Find can return null in a player if the shader is not referenced).
    /// </summary>
    public static class MaterialFactory
    {
        static Material baseMat;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        static Material Base
        {
            get
            {
                if (baseMat != null) return baseMat;
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseMat = new Material(probe.GetComponent<Renderer>().sharedMaterial) { name = "MYLTI_Base" };
                Object.DestroyImmediate(probe);
                return baseMat;
            }
        }

        public static Material Get(Color c, float smoothness = 0.35f, float metallic = 0f, bool emissive = false)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c) + smoothness.ToString("0.00") + metallic.ToString("0.00") + emissive;
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Base) { name = "MYLTI_" + key, color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (emissive && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 2f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            cache[key] = m;
            return m;
        }

        /// <summary>Textured lit material. tiling = repeats across a primitive face (UV 0..1) or per 6 m on generated meshes.</summary>
        public static Material Textured(Texture2D tex, Vector2 tiling, float smoothness = 0.2f, Color? tint = null, float metallic = 0f)
        {
            string key = "tex_" + tex.name + tiling.x.ToString("0.0") + "x" + tiling.y.ToString("0.0") + smoothness.ToString("0.00") + (tint.HasValue ? ColorUtility.ToHtmlStringRGBA(tint.Value) : "");
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Base) { name = "MYLTI_" + key, color = tint ?? Color.white, mainTexture = tex, mainTextureScale = tiling };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint ?? Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            m.enableInstancing = true;
            cache[key] = m;
            return m;
        }

        public static Material Hex(string hex, float smoothness = 0.5f, float metallic = 0.2f)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var c)) c = Color.magenta;
            return Get(c, smoothness, metallic);
        }

        /// <summary>Primitive without collider (visual only). Collider removed immediately so it never collides.</summary>
        public static GameObject Visual(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, string name = null)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.name = name ?? type.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Primitive with its collider kept (static scenery / physics props).</summary>
        public static GameObject Solid(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, string name = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name ?? type.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public static Font RuntimeFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            return f;
        }
    }
}
