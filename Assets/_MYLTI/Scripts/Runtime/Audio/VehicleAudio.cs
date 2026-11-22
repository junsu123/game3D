using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Procedurally synthesised engine + impact sounds (original, no licensed samples needed).
    /// Gives the recorded video a real audio track; replace with licensed SFX later via AudioDirector (P1).
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleAudio : MonoBehaviour
    {
        static AudioClip engineClip, impactClip, windClip;
        public static float MasterVolume = 0.8f;

        VehicleController vc;
        AudioSource engine, wind, oneShots;
        float lastImpactTime;

        void Start()
        {
            vc = GetComponent<VehicleController>();
            EnsureClips();
            engine = MakeSource(engineClip, true, 0.35f);
            wind = MakeSource(windClip, true, 0f);
            oneShots = MakeSource(null, false, 1f);
            engine.Play();
            wind.Play();
            vc.Impact += OnImpact;
            vc.VehicleContact += (other, impulse, p) => OnImpact(SurfaceKind.Obstacle, impulse, p, other.name);
        }

        void OnDestroy()
        {
            if (vc != null) vc.Impact -= OnImpact;
        }

        AudioSource MakeSource(AudioClip clip, bool loop, float vol)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.volume = vol * MasterVolume;
            s.spatialBlend = 0.55f;
            s.minDistance = 18f;
            s.maxDistance = 400f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.dopplerLevel = 0.2f;
            s.playOnAwake = false;
            return s;
        }

        void Update()
        {
            if (vc == null || vc.Def == null) return;
            if (vc.ReplayMode) { engine.volume = 0.12f * MasterVolume; wind.volume = 0f; return; }
            float speed01 = Mathf.Clamp01(vc.SpeedKmh / vc.Def.TopSpeedKmh);
            float load = Mathf.Abs(vc.ThrottleInput);
            float basePitch = vc.Def.BodyStyle == "truck" ? 0.55f : vc.Def.BodyStyle == "suv" ? 0.75f : 0.95f;
            float target = basePitch + speed01 * 1.3f + (vc.Airborne ? load * 0.35f : 0f);
            engine.pitch = Mathf.Lerp(engine.pitch, target, Time.deltaTime * 6f);
            engine.volume = (0.18f + 0.3f * load + 0.12f * speed01) * MasterVolume;
            wind.volume = (vc.Airborne ? 0.45f : 0.08f) * speed01 * MasterVolume;
            wind.pitch = 0.8f + speed01 * 0.6f;
        }

        void OnImpact(SurfaceKind kind, float impulse, Vector3 point, string other)
        {
            if (Time.time - lastImpactTime < 0.12f) return;
            float strength = impulse / Mathf.Max(1f, vc.Def.MassKg); // ≈ Δv in m/s
            if (strength < 0.8f) return;
            lastImpactTime = Time.time;
            oneShots.pitch = kind == SurfaceKind.Obstacle ? 1.3f : 0.8f + Random.value * 0.2f;
            oneShots.PlayOneShot(impactClip, Mathf.Clamp01(strength / 8f) * MasterVolume);
        }

        static void EnsureClips()
        {
            const int rate = 44100;
            if (engineClip == null)
            {
                int n = rate; // 1 s loop; 55 Hz fundamental fits exactly 55 cycles
                var d = new float[n];
                var rng = new System.Random(7);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / rate;
                    float saw = 0f;
                    for (int h = 1; h <= 6; h++) saw += Mathf.Sin(2f * Mathf.PI * 55f * h * t) / h;
                    float pulse = Mathf.Sin(2f * Mathf.PI * 27.5f * t) > 0.6f ? 0.25f : 0f;
                    d[i] = (saw * 0.35f + pulse + ((float)rng.NextDouble() - 0.5f) * 0.05f) * 0.6f;
                }
                engineClip = AudioClip.Create("MYLTI_Engine", n, 1, rate, false);
                engineClip.SetData(d, 0);
            }
            if (impactClip == null)
            {
                int n = (int)(rate * 0.5f);
                var d = new float[n];
                var rng = new System.Random(11);
                float prev = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / rate;
                    float noise = (float)rng.NextDouble() * 2f - 1f;
                    prev = Mathf.Lerp(prev, noise, 0.25f); // crude low-pass
                    float thump = Mathf.Sin(2f * Mathf.PI * 60f * t) * Mathf.Exp(-t * 12f);
                    d[i] = (prev * Mathf.Exp(-t * 9f) * 0.8f + thump * 0.9f);
                }
                impactClip = AudioClip.Create("MYLTI_Impact", n, 1, rate, false);
                impactClip.SetData(d, 0);
            }
            if (windClip == null)
            {
                int n = rate * 2;
                var d = new float[n];
                var rng = new System.Random(3);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    lp = Mathf.Lerp(lp, (float)rng.NextDouble() * 2f - 1f, 0.05f);
                    float fade = Mathf.Min(1f, Mathf.Min(i, n - i) / 2000f); // avoid loop click
                    d[i] = lp * fade * 1.5f;
                }
                windClip = AudioClip.Create("MYLTI_Wind", n, 1, rate, false);
                windClip.SetData(d, 0);
            }
        }
    }
}
