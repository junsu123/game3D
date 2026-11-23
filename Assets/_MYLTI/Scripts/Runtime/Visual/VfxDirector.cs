using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Real-event effects: dust bursts on landings and rollovers, sparks + debris when obstacles are hit.
    /// Driven only by telemetry events (nothing is faked); purely visual, no physics influence.
    /// </summary>
    public sealed class VfxDirector : MonoBehaviour
    {
        public static VfxDirector Instance { get; private set; }
        public ChallengeSession Session;
        ParticleSystem dust, sparks, debris, kick, glassFx, smoke;

        void Awake() { Instance = this; }

        public void Attach(EventLogger log)
        {
            log.Logged += OnEvent;
        }

        void Start()
        {
            var sphere = BuiltinMesh(PrimitiveType.Sphere);
            var cube = BuiltinMesh(PrimitiveType.Cube);
            dust = Make("VFX_Dust", sphere, MaterialFactory.Get(new Color(0.62f, 0.53f, 0.40f), 0.05f), 2.2f, 4f, 0.9f, 2.6f, -0.03f, 900);
            sparks = Make("VFX_Sparks", cube, MaterialFactory.Get(new Color(1f, 0.55f, 0.15f), 0.9f, 0f, true), 0.45f, 11f, 0.06f, 0.14f, 1.2f, 400);
            debris = Make("VFX_Debris", cube, MaterialFactory.Get(new Color(0.45f, 0.32f, 0.2f), 0.2f), 1.6f, 7f, 0.15f, 0.4f, 1.6f, 300);
            kick = Make("VFX_WheelDust", sphere, MaterialFactory.Get(new Color(0.66f, 0.6f, 0.5f), 0.05f), 0.9f, 2.5f, 0.25f, 0.7f, -0.02f, 600);
            glassFx = Make("VFX_Glass", cube, MaterialFactory.Get(new Color(0.75f, 0.9f, 1f), 0.98f, 0.1f), 1.4f, 6f, 0.03f, 0.09f, 1.5f, 800);
            smoke = Make("VFX_Smoke", sphere, MaterialFactory.Get(new Color(0.28f, 0.28f, 0.3f), 0.02f), 3.5f, 1.6f, 1.2f, 3.2f, -0.06f, 400);
        }

        static Mesh BuiltinMesh(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(go);
            return m;
        }

        ParticleSystem Make(string name, Mesh mesh, Material mat, float life, float speed, float minSize, float maxSize, float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Hemisphere;
            sh.radius = 1.2f;
            sh.rotation = new Vector3(-90f, 0f, 0f); // emit upwards
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.drag = 1.8f;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Mesh;
            pr.mesh = mesh;
            pr.sharedMaterial = mat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        /// <summary>Glass shards bursting out of a window.</summary>
        public void GlassShatter(Vector3 pos, Vector3 carVelocity) => Emit(glassFx, pos, 90);

        /// <summary>Sparks, debris and a puff of smoke at a body impact, scaled by the damage it caused.</summary>
        public void ImpactBurst(Vector3 pos, float damage)
        {
            int n = Mathf.Clamp((int)(damage * 2.5f), 6, 60);
            Emit(sparks, pos, n);
            Emit(debris, pos, n / 2);
            if (damage > 12f) Emit(smoke, pos + Vector3.up * 0.5f, Mathf.Clamp((int)damage, 8, 40));
        }

        /// <summary>Small puff behind a fast rear wheel.</summary>
        public void WheelDust(Vector3 contact) => Emit(kick, contact + Vector3.up * 0.15f, 1);

        void Emit(ParticleSystem ps, Vector3 pos, int count)
        {
            if (ps == null) return;
            var p = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true };
            ps.Emit(p, count);
        }

        void OnEvent(RunEvent e)
        {
            if (Session == null || e.VehicleId == null || !Session.Vehicles.TryGetValue(e.VehicleId, out var vc) || vc == null) return;
            Vector3 pos = vc.transform.position;
            Vector3 ground = new Vector3(pos.x, 0.3f, pos.z);
            switch (e.Type)
            {
                case "Landed":
                {
                    float impact = e.Values.TryGetValue("impactSpeed", out var v) ? (float)v : 5f;
                    Emit(dust, ground, Mathf.Clamp((int)(impact * 6f), 20, 120));
                    if (impact > 12f) Emit(sparks, ground, 25);
                    break;
                }
                case "Rollover":
                    Emit(dust, ground, 70);
                    Emit(sparks, pos, 20);
                    break;
                case "ObstacleHit":
                    Emit(debris, pos, 30);
                    Emit(sparks, pos, 18);
                    break;
                case "Takeoff":
                    Emit(dust, new Vector3(pos.x, pos.y - 0.5f, pos.z - 2f), 12);
                    break;
            }
        }
    }
}
