using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Loads the owner's 3D car models once (GLB/glTF via Unity glTFast, or prefabs/FBX under Resources/Vehicles)
    /// and keeps an inactive template per vehicle id. VehicleFactory clones the template for each vehicle.
    /// </summary>
    public sealed class VehicleModelLibrary : MonoBehaviour
    {
        public static VehicleModelLibrary Instance { get; private set; }
        public readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, string> Errors = new Dictionary<string, string>();
        public bool Loading { get; private set; }
        public static bool GltfAvailable =>
#if MYLTI_GLTFAST
            true;
#else
            false;
#endif

        Transform store;

        void Awake()
        {
            Instance = this;
            store = new GameObject("VehicleModelTemplates").transform;
            store.gameObject.SetActive(false);
            DontDestroyOnLoad(store.gameObject);
        }

        /// <summary>Resources prefabs (FBX/prefab placed in Assets/_MYLTI/Resources/Vehicles) as "res:" references.</summary>
        public static List<string> ResourceRefs()
        {
            var list = new List<string>();
            foreach (var go in Resources.LoadAll<GameObject>("Vehicles")) list.Add("res:Vehicles/" + go.name);
            return list;
        }

        public IEnumerator LoadAll(IEnumerable<VehicleDefinition> defs, Action<string> log)
        {
            Loading = true;
            foreach (var d in defs.Where(x => x.IsUserModel).ToList())
            {
                if (Templates.ContainsKey(d.Id)) continue;
                GameObject model = null;
                string err = null;
                if (d.ModelFile.StartsWith("res:", StringComparison.Ordinal))
                {
                    var prefab = Resources.Load<GameObject>(d.ModelFile.Substring(4));
                    if (prefab == null) err = "prefab not found in Resources: " + d.ModelFile;
                    else model = Instantiate(prefab, store);
                }
                else
                {
#if MYLTI_GLTFAST
                    var holder = new GameObject("glb_" + d.Id);
                    holder.transform.SetParent(store, false);
                    var gltf = new GLTFast.GltfImport();
                    var load = gltf.LoadFile(d.ModelFile, new Uri(Path.GetFullPath(d.ModelFile)));
                    while (!load.IsCompleted) yield return null;
                    if (load.IsFaulted || !load.Result) err = "could not read " + Path.GetFileName(d.ModelFile) + (load.IsFaulted ? ": " + load.Exception.GetBaseException().Message : "");
                    else
                    {
                        var inst = gltf.InstantiateMainSceneAsync(holder.transform);
                        while (!inst.IsCompleted) yield return null;
                        if (inst.IsFaulted || !inst.Result) err = "could not build " + Path.GetFileName(d.ModelFile);
                        else model = holder;
                    }
                    if (model == null) Destroy(holder);
#else
                    err = "GLB needs the Unity glTFast package (menu MYLTI ▸ Install glTFast)";
#endif
                }
                if (model == null) { Errors[d.Id] = err; log?.Invoke($"Vehicle {d.DisplayNameEn}: {err} — using the built-in body."); continue; }
                model.name = "Model_" + d.Id;
                // strip anything that would interfere with our physics
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
                foreach (var cam in model.GetComponentsInChildren<Camera>(true)) Destroy(cam.gameObject);
                foreach (var light in model.GetComponentsInChildren<Light>(true)) light.enabled = false;
                foreach (var anim in model.GetComponentsInChildren<Animation>(true)) anim.enabled = false;
                Templates[d.Id] = model;
                Errors.Remove(d.Id);
                yield return null;
            }
            Loading = false;
        }

        public GameObject Clone(VehicleDefinition d, Transform parent)
        {
            if (d == null || !Templates.TryGetValue(d.Id, out var t) || t == null) return null;
            var go = Instantiate(t, parent);
            go.SetActive(true);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go;
        }

        public void Forget(string id)
        {
            if (Templates.TryGetValue(id, out var t) && t != null) Destroy(t);
            Templates.Remove(id);
        }
    }

    /// <summary>Result of fitting a 3D model onto our vehicle rig.</summary>
    public sealed class ModelRig
    {
        public Transform Model;
        public Transform[] WheelPivots = new Transform[4];   // FL, FR, RL, RR (null if not found)
        public float WheelRadius;
        public float Track, Wheelbase, WheelCenterY, FrontAxleZ, RearAxleZ;
        public Bounds Body;                                  // vehicle-local bounds without wheels
        public readonly List<MeshFilter> BodyMeshes = new List<MeshFilter>();
        public readonly List<Renderer> Windows = new List<Renderer>();
        public readonly List<Renderer> BodyRenderers = new List<Renderer>();
        public bool WheelsFound => WheelPivots.All(p => p != null);
        public string Notes = "";
    }

    /// <summary>
    /// Fits a 3D car model onto the rig: rotates it nose to +Z, scales it to real size, sits it on the ground,
    /// finds the four wheels (by name, else by shape and position), and sorts body / glass meshes.
    /// </summary>
    public static class ModelRigger
    {
        static readonly Regex WheelName = new Regex(@"(wheel|tire|tyre|rim|whl|reifen|roue|rueda|ruota|felge|hub|brake|caliper|disc)", RegexOptions.IgnoreCase);
        static readonly Regex NotWheel = new Regex(@"(steer|spare|interior|dash|arch|well|cover|fender|guard|emblem|logo)", RegexOptions.IgnoreCase);
        static readonly Regex GlassName = new Regex(@"(glass|window|windshield|windscreen|glas|vitre|scheibe|cristal|vetro|light.*lens|lens)", RegexOptions.IgnoreCase);

        public static ModelRig Fit(GameObject model, VehicleDefinition def, Transform vehicleRoot)
        {
            var rig = new ModelRig { Model = model.transform };
            var t = model.transform;
            t.SetParent(vehicleRoot, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            var renderers = model.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToList();
            if (renderers.Count == 0) { rig.Notes = "model has no meshes"; return rig; }

            // 1. orientation: longest horizontal axis becomes Z, then the user's yaw correction
            var b = LocalBounds(renderers, vehicleRoot);
            float yaw = b.size.x > b.size.z * 1.15f ? 90f : 0f;
            yaw += def.ModelYawDeg;
            t.localRotation = Quaternion.Euler(0, yaw, 0);

            // 2. scale: real length if given, else guess the unit (cm / mm models are common)
            b = LocalBounds(renderers, vehicleRoot);
            float len = Mathf.Max(b.size.z, 0.001f);
            float scale;
            if (def.ModelLengthM > 1f) scale = def.ModelLengthM / len;
            else if (len > 200f) scale = len > 2000f ? 0.001f : 0.01f;
            else if (len < 0.5f) scale = def.Length / len;
            else scale = 1f;
            if (scale * len < 2.2f || scale * len > 20f) scale = def.Length / len; // sanity: cars are 2–20 m
            t.localScale = Vector3.one * scale;

            // 3. centre over the origin and put the lowest point on y = 0
            b = LocalBounds(renderers, vehicleRoot);
            t.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = LocalBounds(renderers, vehicleRoot);

            // 4. wheels: by name, grouped by quadrant
            var groups = new List<Renderer>[4];
            for (int i = 0; i < 4; i++) groups[i] = new List<Renderer>();
            foreach (var r in renderers)
            {
                if (!WheelName.IsMatch(PathName(r.transform, t)) || NotWheel.IsMatch(r.name)) continue;
                var rb = LocalBounds(new[] { r }, vehicleRoot);
                if (rb.center.y > b.size.y * 0.55f || rb.size.y > b.size.y * 0.75f) continue; // too high / too big to be a wheel part
                groups[Quadrant(rb.center)].Add(r);
            }
            bool byName = groups.All(g => g.Count > 0);
            if (!byName)
            {
                // fallback: round-ish low parts in the four corners
                for (int i = 0; i < 4; i++) groups[i].Clear();
                foreach (var r in renderers)
                {
                    var rb = LocalBounds(new[] { r }, vehicleRoot);
                    float h = rb.size.y, dep = rb.size.z, w = rb.size.x;
                    bool round = h > 0.35f && h < 1.6f && Mathf.Abs(h - dep) < h * 0.25f && w < h * 0.9f;
                    bool low = rb.min.y < h * 0.25f && rb.center.y < b.size.y * 0.45f;
                    bool corner = Mathf.Abs(rb.center.x) > b.size.x * 0.22f && Mathf.Abs(rb.center.z) > b.size.z * 0.15f;
                    if (round && low && corner) groups[Quadrant(rb.center)].Add(r);
                }
            }
            if (groups.All(g => g.Count > 0))
            {
                for (int i = 0; i < 4; i++)
                {
                    var gb = LocalBounds(groups[i], vehicleRoot);
                    var pivot = new GameObject("WheelVisual_" + new[] { "FL", "FR", "RL", "RR" }[i]).transform;
                    pivot.SetParent(vehicleRoot, false);
                    pivot.localPosition = gb.center;
                    // keep the parts' world pose while moving them under the pivot (pivot spins with the wheel collider)
                    foreach (var r in groups[i]) r.transform.SetParent(pivot, true);
                    rig.WheelPivots[i] = pivot;
                }
                var c = rig.WheelPivots.Select(p => p.localPosition).ToArray();
                rig.WheelRadius = Mathf.Clamp(groups.Select(g => LocalBounds(g, vehicleRoot).size.y * 0.5f).Average(), 0.22f, 0.8f);
                rig.Track = (Mathf.Abs(c[0].x - c[1].x) + Mathf.Abs(c[2].x - c[3].x)) * 0.5f;
                rig.FrontAxleZ = (c[0].z + c[1].z) * 0.5f;
                rig.RearAxleZ = (c[2].z + c[3].z) * 0.5f;
                rig.Wheelbase = rig.FrontAxleZ - rig.RearAxleZ;
                rig.WheelCenterY = c.Average(p => p.y);
                rig.Notes = byName ? "wheels found by name" : "wheels found by shape";
                if (rig.Wheelbase < 0) rig.Notes += " — the model faces backwards: set yawDeg to 180 in its .vehicle.json";
            }
            else
            {
                rig.Notes = "wheels not found — wheels stay fixed on the body (name the wheel meshes 'wheel…' in the model)";
                rig.WheelRadius = def.WheelRadius;
                rig.Track = Mathf.Min(def.Track, b.size.x * 0.82f);
                rig.Wheelbase = Mathf.Min(def.Wheelbase, b.size.z * 0.62f);
                rig.FrontAxleZ = rig.Wheelbase * 0.5f;
                rig.RearAxleZ = -rig.Wheelbase * 0.5f;
                rig.WheelCenterY = rig.WheelRadius;
            }

            // 5. body vs glass
            var wheelSet = new HashSet<Renderer>(groups.SelectMany(g => g));
            foreach (var r in renderers)
            {
                if (wheelSet.Contains(r)) continue;
                bool isGlass = GlassName.IsMatch(PathName(r.transform, t)) || r.sharedMaterials.Any(m => m != null && GlassName.IsMatch(m.name));
                if (isGlass) rig.Windows.Add(r);
                rig.BodyRenderers.Add(r);
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && !isGlass) rig.BodyMeshes.Add(mf);
            }
            rig.Body = LocalBounds(rig.BodyRenderers.Count > 0 ? rig.BodyRenderers : renderers, vehicleRoot);
            return rig;
        }

        static int Quadrant(Vector3 p) => (p.z >= 0 ? 0 : 2) + (p.x >= 0 ? 1 : 0); // FL=0 FR=1 RL=2 RR=3

        static string PathName(Transform tr, Transform stop)
        {
            string s = tr.name;
            for (var p = tr.parent; p != null && p != stop; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        /// <summary>Bounds of renderers in the vehicle root's local space.</summary>
        public static Bounds LocalBounds(IEnumerable<Renderer> rs, Transform root)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in rs)
            {
                Bounds wb;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mb = mf.sharedMesh.bounds;
                    // transform the 8 corners of the mesh bounds (tighter than renderer.bounds for rotated parts)
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var lp = root.InverseTransformPoint(r.transform.TransformPoint(corner));
                        if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                    }
                    continue;
                }
                wb = r.bounds;
                var a = root.InverseTransformPoint(wb.min);
                var c = root.InverseTransformPoint(wb.max);
                if (!any) { b = new Bounds(a, Vector3.zero); any = true; } else b.Encapsulate(a);
                b.Encapsulate(c);
            }
            return b;
        }
    }
}
