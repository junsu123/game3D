using System.Collections.Generic;
using System.Linq;
using Mylti.Domain;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Mylti
{
    /// <summary>Where one spectator stands and which way they face.</summary>
    public struct CrowdSpot
    {
        public Vector3 Pos;
        public float Yaw;
        public int Priority; // higher = closer to the action (real character models go here first)
    }

    /// <summary>
    /// The audience: spawns animated spectators around the arena and drives them from the CrowdMood
    /// (fed by real run events). People look at the vehicles, clap, raise their arms, jump and fist-pump;
    /// far spectators react a moment later. Optional real rigged characters (e.g. Mixamo) with their own
    /// idle / clapping / cheering clips are used for the front rows when present in Resources/Crowd.
    /// </summary>
    public sealed class CrowdDirector : MonoBehaviour
    {
        public static CrowdDirector Instance { get; private set; }
        public static bool Enabled = true;
        public static float Density = 1f;
        public static float HeroShare = 0.6f; // fraction of spectators dressed as the channel's own superheroes
        public const int MaxRealFans = 160;
        public const int MaxFans = 1100; // at density 1.0 (~1 M skinned vertices)

        public ChallengeSession Session;
        public readonly CrowdMood Mood = new CrowdMood();
        public int Count => fans.Count;
        public int RealCount { get; private set; }

        sealed class Fan
        {
            public Transform Root;
            public Transform[] Bones;
            public Vector3 HipBase;
            public CrowdPose.Style Style;
            public float Phase, Rate, Responsive, Excite;
            // real character
            public Animator Animator;
            public PlayableGraph Graph;
            public AnimationMixerPlayable Mixer;
            public int ClapInput = -1, CheerInput = -1;
        }

        readonly List<Fan> fans = new List<Fan>();
        Transform holder;
        readonly System.Numerics.Vector3[] euler = new System.Numerics.Vector3[HumanMesh.BoneCount];
        Vector3 focus;
        int frame;
        Transform lastParent;
        List<CrowdSpot> lastSpots;
        int lastSeed;

        /// <summary>Rebuild the current arena's crowd (after a settings change).</summary>
        public void Repopulate() { if (lastParent != null) Populate(lastParent, lastSpots, lastSeed); }

        // real characters (loaded once)
        List<GameObject> realModels;
        List<AnimationClip> idleClips, clapClips, cheerClips;

        void Awake() { Instance = this; }

        public void Attach(EventLogger log) => log.Logged += e => Mood.OnEvent(e.Type, e.Values);

        void LoadRealCharacters()
        {
            if (realModels != null) return;
            realModels = Resources.LoadAll<GameObject>("Crowd").Where(g => g.GetComponentInChildren<SkinnedMeshRenderer>(true) != null && g.GetComponentInChildren<Animator>(true) != null).ToList();
            var clips = Resources.LoadAll<AnimationClip>("Crowd").Where(c => !c.name.StartsWith("__preview__")).ToList();
            bool Has(AnimationClip c, params string[] k) => k.Any(x => c.name.ToLowerInvariant().Contains(x));
            clapClips = clips.Where(c => Has(c, "clap", "applau")).ToList();
            cheerClips = clips.Where(c => !clapClips.Contains(c) && Has(c, "cheer", "excit", "celebr", "victory", "jump", "fist", "yell", "wave", "hype", "happy", "dance")).ToList();
            idleClips = clips.Where(c => !clapClips.Contains(c) && !cheerClips.Contains(c)).ToList();
        }

        public string RealCharactersSummary()
        {
            LoadRealCharacters();
            return $"{realModels.Count} nhân vật · {idleClips.Count} đứng yên · {clapClips.Count} vỗ tay · {cheerClips.Count} reo hò";
        }

        /// <summary>Replace the crowd (called whenever the arena is rebuilt).</summary>
        public void Populate(Transform parent, List<CrowdSpot> spots, int seed)
        {
            lastParent = parent; lastSpots = spots; lastSeed = seed;
            Clear();
            if (!Enabled || spots == null || spots.Count == 0) return;
            LoadRealCharacters();
            var rng = new System.Random(seed * 7919 + 17);
            // thin out evenly (full stands everywhere, just less dense), then serve the front rows first
            float d = Mathf.Clamp(Density, 0.1f, 1.5f);
            int keep = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(spots.Count, MaxFans * d)), 0, spots.Count);
            var chosen = spots.OrderBy(_ => rng.Next()).Take(keep).OrderByDescending(s => s.Priority).ToList();
            bool canReal = realModels.Count > 0 && (idleClips.Count + clapClips.Count + cheerClips.Count) > 0;
            int real = 0;
            holder = new GameObject("Crowd").transform;
            holder.SetParent(parent, false);
            foreach (var s in chosen)
            {
                var fan = new Fan
                {
                    Style = (CrowdPose.Style)rng.Next(4),
                    Phase = (float)rng.NextDouble() * 10f,
                    Rate = 0.9f + (float)rng.NextDouble() * 0.25f,
                    Responsive = 0.6f + (float)rng.NextDouble() * 0.4f,
                    Excite = CrowdMood.Base,
                };
                var rot = Quaternion.Euler(0, s.Yaw + (float)(rng.NextDouble() - 0.5) * 24f, 0);
                if (canReal && real < MaxRealFans && s.Priority >= 2)
                {
                    if (SpawnReal(fan, holder, s.Pos, rot, rng)) { real++; fans.Add(fan); continue; }
                }
                var v = rng.NextDouble() < HeroShare ? CrowdPeople.GetHero(rng.Next(1000)) : CrowdPeople.Get(rng.Next(1000));
                fan.Bones = CrowdPeople.Spawn(v, holder, s.Pos, rot, "Fan");
                fan.Root = fan.Bones[HumanMesh.Hips].parent;
                fan.HipBase = fan.Bones[HumanMesh.Hips].localPosition;
                fans.Add(fan);
            }
            RealCount = real;
        }

        bool SpawnReal(Fan fan, Transform holder, Vector3 pos, Quaternion rot, System.Random rng)
        {
            var prefab = realModels[rng.Next(realModels.Count)];
            var go = Instantiate(prefab, holder);
            go.transform.SetPositionAndRotation(pos, rot);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            var anim = go.GetComponentInChildren<Animator>(true);
            if (anim == null) { Destroy(go); return false; }
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            // normalise height to 1.6–1.9 m
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.y > 0.1f) go.transform.localScale *= (1.6f + (float)rng.NextDouble() * 0.3f) / b.size.y;
            }
            var graph = PlayableGraph.Create("Fan");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Anim", anim);
            var mixer = AnimationMixerPlayable.Create(graph, 3);
            int input = 0;
            AnimationClip Pick(List<AnimationClip> l) => l.Count == 0 ? null : l[rng.Next(l.Count)];
            void Add(AnimationClip clip, ref int slot)
            {
                if (clip == null) return;
                var p = AnimationClipPlayable.Create(graph, clip);
                p.SetTime(rng.NextDouble() * clip.length);
                graph.Connect(p, 0, mixer, input);
                slot = input++;
            }
            int idleSlot = -1;
            Add(Pick(idleClips) ?? Pick(clapClips) ?? Pick(cheerClips), ref idleSlot);
            Add(Pick(clapClips), ref fan.ClapInput);
            Add(Pick(cheerClips), ref fan.CheerInput);
            output.SetSourcePlayable(mixer);
            graph.Play();
            fan.Root = go.transform;
            fan.Animator = anim;
            fan.Graph = graph;
            fan.Mixer = mixer;
            return true;
        }

        public void Clear()
        {
            foreach (var f in fans)
                if (f.Graph.IsValid()) f.Graph.Destroy();
            fans.Clear();
            RealCount = 0;
            if (holder != null) { Destroy(holder.gameObject); holder = null; }
        }

        void OnDestroy() => Clear();

        void Update()
        {
            float dt = Time.deltaTime;
            Mood.Tick(dt);
            if (fans.Count == 0) return;
            frame++;
            // what everybody is looking at
            Vector3 want = focus;
            if (Session != null)
            {
                var live = Session.ActiveVehicles.Where(v => v != null).ToList();
                if (live.Count > 0) { want = Vector3.zero; foreach (var v in live) want += v.transform.position; want /= live.Count; }
                else if (Session.Map != null) want = new Vector3(0, 2f, Session.Map.LipZ + 30f);
            }
            focus = Vector3.Lerp(focus, want, 1f - Mathf.Exp(-dt * 3f));
            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : focus;
            float now = Time.time;

            for (int i = 0; i < fans.Count; i++)
            {
                var f = fans[i];
                if (f.Root == null) continue;
                Vector3 p = f.Root.position;
                // far from the camera: animate at a lower rate (saves CPU, invisible difference)
                float camDist = (p - camPos).sqrMagnitude;
                if (camDist > 200f * 200f && (frame + i) % 3 != 0) continue;
                float dist = Vector3.Distance(p, focus);
                float target = Mathf.Lerp(CrowdMood.Base, Mood.LevelAgo(Mathf.Clamp(dist / 70f, 0f, 1.6f)), f.Responsive);
                f.Excite += (target - f.Excite) * (1f - Mathf.Exp(-dt * 6f * (camDist > 200f * 200f ? 3f : 1f)));
                float applause = Mood.Applause * f.Responsive;
                if (f.Bones != null)
                {
                    Vector3 local = Quaternion.Inverse(f.Root.rotation) * (focus - p);
                    float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -70f, 70f);
                    CrowdPose.Compute(f.Style, f.Excite, applause, (now + f.Phase) * f.Rate, yaw, euler, out float lift);
                    for (int b = 0; b < HumanMesh.BoneCount; b++)
                        f.Bones[b].localRotation = Quaternion.Euler(euler[b].X, euler[b].Y, euler[b].Z);
                    f.Bones[HumanMesh.Hips].localPosition = f.HipBase + new Vector3(0, lift, 0);
                }
                else if (f.Graph.IsValid())
                {
                    CrowdPose.Weights(f.Style, f.Excite, applause, out float idle, out float clap, out float cheer);
                    if (f.ClapInput < 0) { cheer += clap; clap = 0; }
                    if (f.CheerInput < 0) { clap += cheer; cheer = 0; if (f.ClapInput < 0) { idle = 1; clap = 0; } }
                    float sum = Mathf.Max(0.001f, idle + clap + cheer);
                    f.Mixer.SetInputWeight(0, idle / sum);
                    if (f.ClapInput >= 0) f.Mixer.SetInputWeight(f.ClapInput, clap / sum);
                    if (f.CheerInput >= 0) f.Mixer.SetInputWeight(f.CheerInput, cheer / sum);
                }
            }
        }
    }
}
