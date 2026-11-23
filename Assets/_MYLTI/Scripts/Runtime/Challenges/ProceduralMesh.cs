using System.Collections.Generic;
using UnityEngine;

namespace Mylti
{
    /// <summary>Generates simple solid meshes (ramps) by extruding a (z,y) profile along X.</summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        /// <summary>Adds a quad and flips winding so the face points along 'outward'. UVs default to world-planar mapping.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward,
            Vector2? ua = null, Vector2? ub = null, Vector2? uc = null, Vector2? ud = null)
        {
            Vector2 Planar(Vector3 p)
            {
                Vector3 n = outward;
                if (Mathf.Abs(n.x) > 0.7f) return new Vector2(p.z, p.y) / 6f;
                if (Mathf.Abs(n.z) > 0.7f) return new Vector2(p.x, p.y) / 6f;
                return new Vector2(p.x, p.z) / 6f;
            }
            Vector2 A = ua ?? Planar(a), B = ub ?? Planar(b), C = uc ?? Planar(c), D = ud ?? Planar(d);
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(nrm, outward) < 0) { var t = b; b = d; d = t; var tu = B; B = D; D = tu; }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            uvs.Add(A); uvs.Add(B); uvs.Add(C); uvs.Add(D);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Solid under a top profile. profile = list of (z, y) points with strictly increasing z.
        /// Width is centred on x = 0. Bottom is y = 0. Vertical end caps are added where y &gt; 0.
        /// </summary>
        public static Mesh ExtrudeProfile(IList<Vector2> profile, float width, string name)
        {
            var mb = new MeshBuilder();
            float hx = width * 0.5f;
            float along = 0f; // distance along the deck → plank texture runs across the driving direction
            for (int i = 0; i < profile.Count - 1; i++)
            {
                Vector2 p = profile[i], q = profile[i + 1];
                float seg = Vector2.Distance(p, q);
                float v0 = along / 6f, v1 = (along + seg) / 6f;
                along += seg;
                var a = new Vector3(-hx, p.y, p.x); var b = new Vector3(hx, p.y, p.x);
                var c = new Vector3(hx, q.y, q.x); var d = new Vector3(-hx, q.y, q.x);
                Vector3 slopeUp = Vector3.Cross(Vector3.right, new Vector3(0, q.y - p.y, q.x - p.x)).normalized;
                if (slopeUp.y < 0) slopeUp = -slopeUp;
                mb.Quad(a, b, c, d, slopeUp, new Vector2(0, v0), new Vector2(width / 6f, v0), new Vector2(width / 6f, v1), new Vector2(0, v1));
                // side walls
                mb.Quad(new Vector3(-hx, 0, p.x), new Vector3(-hx, p.y, p.x), new Vector3(-hx, q.y, q.x), new Vector3(-hx, 0, q.x), Vector3.left);
                mb.Quad(new Vector3(hx, 0, p.x), new Vector3(hx, p.y, p.x), new Vector3(hx, q.y, q.x), new Vector3(hx, 0, q.x), Vector3.right);
            }
            Vector2 first = profile[0], last = profile[profile.Count - 1];
            if (first.y > 0.01f)
                mb.Quad(new Vector3(-hx, 0, first.x), new Vector3(hx, 0, first.x), new Vector3(hx, first.y, first.x), new Vector3(-hx, first.y, first.x), Vector3.back);
            if (last.y > 0.01f)
                mb.Quad(new Vector3(-hx, 0, last.x), new Vector3(hx, 0, last.x), new Vector3(hx, last.y, last.x), new Vector3(-hx, last.y, last.x), Vector3.forward);
            return mb.Build(name);
        }

        /// <summary>Kicker profile: flat entry → circular transition → straight incline up to the lip.</summary>
        public static List<Vector2> KickerProfile(float startZ, float height, float angleDeg, out float lipZ)
        {
            float th = angleDeg * Mathf.Deg2Rad;
            float maxR = 120f; // large transition radius keeps g-load manageable at 150+ km/h
            float rFit = 0.6f * height / Mathf.Max(0.01f, 1f - Mathf.Cos(th));
            float R = Mathf.Min(maxR, rFit);
            var pts = new List<Vector2> { new Vector2(startZ, 0f) };
            int seg = 14;
            for (int i = 1; i <= seg; i++)
            {
                float a = th * i / seg;
                pts.Add(new Vector2(startZ + R * Mathf.Sin(a), R * (1f - Mathf.Cos(a))));
            }
            Vector2 arcEnd = pts[pts.Count - 1];
            float remaining = height - arcEnd.y;
            if (remaining > 0.05f)
                pts.Add(new Vector2(arcEnd.x + remaining / Mathf.Tan(th), height));
            lipZ = pts[pts.Count - 1].x;
            return pts;
        }

        /// <summary>Landing ramp: flat deck then a straight descent to the ground.</summary>
        public static List<Vector2> LandingProfile(float startZ, float height, float angleDeg, float deck)
        {
            float run = height / Mathf.Tan(angleDeg * Mathf.Deg2Rad);
            return new List<Vector2>
            {
                new Vector2(startZ, height),
                new Vector2(startZ + deck, height),
                new Vector2(startZ + deck + run * 0.5f, height * 0.5f),
                new Vector2(startZ + deck + run, 0f)
            };
        }
    }
}
