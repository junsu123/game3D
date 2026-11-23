using System.Collections.Generic;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>Turns a ToonDesign into Unity meshes (one palette material per car) and attaches the animated face.</summary>
    public static class ToonCarRig
    {
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        static Material MaterialFor(ToonDesign d)
        {
            if (materials.TryGetValue(d.Id, out var m) && m != null) return m;
            var hex = ToonCars.Palette(d);
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "ToonPalette_" + d.Id };
            var px = new Color[16];
            for (int i = 0; i < 16; i++) { ColorUtility.TryParseHtmlString("#" + hex[i], out var c); px[i] = c; }
            tex.SetPixels(px);
            tex.Apply(false, false);
            m = MaterialFactory.Textured(tex, Vector2.one, 0.62f); // glossy toy plastic
            materials[d.Id] = m;
            return m;
        }

        static Vector3 U(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>Builds the visual under 'body' and returns the face controller.</summary>
        public static ToonFace Build(ToonDesign d, Transform body, VehicleController vc)
        {
            var visual = new GameObject("ToonVisual").transform;
            visual.SetParent(body, false);
            var mat = MaterialFor(d);
            var parts = new Dictionary<string, Transform>();
            foreach (var p in ToonCars.Build(d, false))
            {
                bool face = p.Name != "Body" && p.Name != "Glass";
                var go = new GameObject(p.Name == "Body" ? "ToonBody" : p.Name == "Glass" ? "Glass" : "Face_" + p.Name);
                go.transform.SetParent(visual, false);
                go.transform.localPosition = U(p.Pivot);
                var verts = new Vector3[p.Vertices.Length];
                var uv = new Vector2[p.Vertices.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    verts[i] = U(p.Vertices[i]);
                    int c = p.Cells[i];
                    uv[i] = new Vector2((c % 4 + 0.5f) / 4f, (c / 4 + 0.5f) / 4f);
                }
                var mesh = new Mesh { name = "Toon_" + d.Id + "_" + p.Name };
                mesh.vertices = verts;
                mesh.uv = uv;
                mesh.triangles = p.Triangles;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                if (face) parts[p.Name] = go.transform;
            }
            var faceCtl = body.gameObject.AddComponent<ToonFace>();
            faceCtl.Init(d, visual, parts, vc);
            return faceCtl;
        }
    }
}
