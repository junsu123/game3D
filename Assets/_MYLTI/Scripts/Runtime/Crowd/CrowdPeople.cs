using System.Collections.Generic;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Unity side of the procedural crowd: turns HumanMesh data into shared skinned meshes (one palette material
    /// for everybody) and spawns a spectator = bone hierarchy + SkinnedMeshRenderer.
    /// </summary>
    public static class CrowdPeople
    {
        public sealed class Variant
        {
            public Mesh Mesh;
            public Vector3[] BonePos = new Vector3[HumanMesh.BoneCount];
            public float Height;
        }

        static readonly List<Variant> variants = new List<Variant>(); // everyday fans (1 in 5 is a child)
        static readonly List<Variant> heroes = new List<Variant>();   // the channel's own superhero squad, adults and kids
        static Material material;

        public static Material Material
        {
            get
            {
                if (material != null) return material;
                var palette = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "CrowdPalette" };
                var px = new Color[256];
                for (int i = 0; i < 256; i++) { ColorUtility.TryParseHtmlString("#" + HumanMesh.PaletteHex(i), out var c); px[i] = c; }
                palette.SetPixels(px);
                palette.Apply(false, false);
                material = MaterialFactory.Textured(palette, Vector2.one, 0.18f);
                return material;
            }
        }

        static Vector3 U(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        public static Variant Get(int i)
        {
            while (variants.Count < 40) variants.Add(Convert(HumanMesh.Build(variants.Count, -1, variants.Count % 5 == 4)));
            return variants[Mathf.Abs(i) % variants.Count];
        }

        /// <summary>A spectator in one of the MYLTI Squad costumes (original designs, see HumanMesh.Heroes).</summary>
        public static Variant GetHero(int i)
        {
            int perHero = 4; // 2 grown-ups + 2 kids per costume
            while (heroes.Count < HumanMesh.Heroes.Length * perHero)
            {
                int n = heroes.Count;
                heroes.Add(Convert(HumanMesh.Build(500 + n, n / perHero, n % perHero >= 2)));
            }
            return heroes[Mathf.Abs(i) % heroes.Count];
        }

        static Variant Convert(HumanMeshData d)
        {
            var v = new Variant { Height = d.Height };
            for (int i = 0; i < HumanMesh.BoneCount; i++) v.BonePos[i] = U(d.BonePos[i]);
            var verts = new Vector3[d.Vertices.Length];
            var uv = new Vector2[d.Vertices.Length];
            var bw = new BoneWeight[d.Vertices.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = U(d.Vertices[i]);
                uv[i] = new Vector2((d.Cells[i] % 16 + 0.5f) / 16f, (d.Cells[i] / 16 + 0.5f) / 16f);
                bw[i] = new BoneWeight { boneIndex0 = d.Bones[i], weight0 = 1f };
            }
            var mesh = new Mesh { name = "Spectator" };
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = d.Triangles;
            mesh.boneWeights = bw;
            var bind = new Matrix4x4[HumanMesh.BoneCount];
            for (int i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.Translate(-v.BonePos[i]); // bones have identity rotation in bind pose
            mesh.bindposes = bind;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            v.Mesh = mesh;
            return v;
        }

        /// <summary>Instantiate one spectator: bone hierarchy + skinned renderer. Returns the bones (HumanMesh indices).</summary>
        public static Transform[] Spawn(Variant v, Transform parent, Vector3 pos, Quaternion rot, string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(pos, rot);
            var bones = new Transform[HumanMesh.BoneCount];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = new GameObject("b" + i).transform;
                int p = HumanMesh.Parent[i];
                bones[i].SetParent(p < 0 ? root : bones[p], false);
                bones[i].localPosition = v.BonePos[i] - (p < 0 ? Vector3.zero : v.BonePos[p]);
                bones[i].localRotation = Quaternion.identity;
            }
            var smr = root.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = v.Mesh;
            smr.bones = bones;
            smr.rootBone = bones[HumanMesh.Hips];
            smr.sharedMaterial = Material;
            smr.updateWhenOffscreen = false;
            smr.localBounds = new Bounds(Vector3.zero, new Vector3(1.8f, 2.6f, 1.4f));
            smr.quality = SkinQuality.Bone1;
            return bones;
        }
    }
}
