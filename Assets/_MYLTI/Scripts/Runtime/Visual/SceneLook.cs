using UnityEngine;
using UnityEngine.Rendering;
#if MYLTI_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Mylti
{
    /// <summary>
    /// "Broadcast look": warm sun, soft ambient, distance fog and — with URP — post-processing
    /// (ACES tonemapping, bloom on lights, colour grading, light vignette, motion blur), SMAA and long-range shadows.
    /// Original URP asset values are restored when the studio closes.
    /// </summary>
    public sealed class SceneLook : MonoBehaviour
    {
        public Camera Cam;
#if MYLTI_URP
        UniversalRenderPipelineAsset urp;
        float oldShadowDistance;
        int oldCascades;
        bool changedAsset;
#endif

        public void Apply(string quality)
        {
            bool high = quality == "High", low = quality == "Low";

            // ---- sun & ambient
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
                sun.color = new Color(1f, 0.95f, 0.86f);
                sun.intensity = 1.35f;
                sun.shadows = low ? LightShadows.Hard : LightShadows.Soft;
                sun.shadowStrength = 0.85f;
                RenderSettings.sun = sun;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.27f, 0.22f);

            // ---- atmosphere: soft haze makes the huge arena read as deep
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.90f);
            RenderSettings.fogDensity = 0.0011f;

            if (Cam != null)
            {
                Cam.farClipPlane = 2500f;
                Cam.allowHDR = true;
            }

#if MYLTI_URP
            if (Cam != null)
            {
                var data = Cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = low ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                if (!changedAsset) { oldShadowDistance = urp.shadowDistance; oldCascades = urp.shadowCascadeCount; changedAsset = true; }
                urp.shadowDistance = low ? 150f : high ? 450f : 300f;
                urp.shadowCascadeCount = low ? 2 : 4;
            }

            var volGo = GameObject.Find("MYLTI_PostFX");
            if (volGo == null) volGo = new GameObject("MYLTI_PostFX");
            var vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(low ? 0.3f : 0.65f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.65f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.25f);
            ca.contrast.Override(14f);
            ca.saturation.Override(14f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.2f);
            vig.smoothness.Override(0.45f);
            if (!low)
            {
                var mb = profile.Add<MotionBlur>(true);
                mb.intensity.Override(high ? 0.35f : 0.25f);
            }
            vol.profile = profile;
#endif
        }

        void OnDestroy()
        {
#if MYLTI_URP
            // runtime edits to the URP asset would otherwise persist in the Editor
            if (urp != null && changedAsset)
            {
                urp.shadowDistance = oldShadowDistance;
                urp.shadowCascadeCount = oldCascades;
            }
#endif
        }
    }
}
