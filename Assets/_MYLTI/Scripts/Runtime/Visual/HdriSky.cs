using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Mylti.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mylti
{
    /// <summary>
    /// Photographed sky + image-based lighting: a CC0 HDRI from Poly Haven (downloaded once) or any .hdr the owner
    /// drops into &lt;data&gt;/Sky. Sets the skybox, ambient light and a reflection probe so car paint and glass
    /// reflect a real sky. Falls back silently to the procedural sky if nothing can be loaded.
    /// </summary>
    public sealed class HdriSky : MonoBehaviour
    {
        public static readonly string[] Suggested =
        {
            "kloofendal_48d_partly_cloudy_puresky", "kloppenheim_06_puresky", "qwantani_noon_puresky", "rogland_clear_night"
        };

        public string Status { get; private set; } = "";
        ReflectionProbe probe;
        Material skyMat;

        public IEnumerator Load(string dataRoot, string hdriId, Action<string> log)
        {
            string dir = Path.Combine(dataRoot, "Sky");
            Directory.CreateDirectory(dir);
            // 1. owner's own file wins
            string file = Directory.GetFiles(dir, "*.hdr").Where(f => !Path.GetFileName(f).StartsWith("ph_", StringComparison.Ordinal)).OrderBy(f => f).FirstOrDefault();
            // 2. Poly Haven (CC0) download, cached
            if (file == null && !string.IsNullOrWhiteSpace(hdriId))
            {
                string cached = Path.Combine(dir, "ph_" + hdriId + "_2k.hdr");
                if (!File.Exists(cached))
                {
                    Status = "Đang tải bầu trời HDRI (Poly Haven, CC0)…";
                    var t = Download(hdriId, cached);
                    while (!t.IsCompleted) yield return null;
                    if (t.IsFaulted || t.Result != null) { Status = "Không tải được HDRI: " + (t.IsFaulted ? t.Exception.GetBaseException().Message : t.Result); log?.Invoke(Status); yield break; }
                }
                file = cached;
            }
            if (file == null) yield break;

            Status = "Đang đọc " + Path.GetFileName(file) + "…";
            var parse = Task.Run(() => RadianceHdr.Read(file));
            while (!parse.IsCompleted) yield return null;
            if (parse.IsFaulted) { Status = "HDRI lỗi: " + parse.Exception.GetBaseException().Message; log?.Invoke(Status); yield break; }
            var img = parse.Result;
            Apply(img);
            Status = "✓ Bầu trời: " + Path.GetFileName(file);
        }

        static async Task<string> Download(string id, string path)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MYLTI-Challenge-Studio/1.0");
                string url = null;
                try
                {
                    // the files endpoint lists every resolution/format with its download URL
                    string json = await http.GetStringAsync("https://api.polyhaven.com/files/" + Uri.EscapeDataString(id)).ConfigureAwait(false);
                    var hdri = MiniJson.Obj(MiniJson.Get(MiniJson.Obj(MiniJson.Parse(json)), "hdri"));
                    var k2 = MiniJson.Obj(MiniJson.Get(hdri, "2k")) ?? MiniJson.Obj(MiniJson.Get(hdri, "1k"));
                    url = MiniJson.GetString(MiniJson.Obj(MiniJson.Get(k2, "hdr")), "url");
                }
                catch (Exception) { /* fall back to the usual file layout */ }
                if (string.IsNullOrEmpty(url)) url = $"https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/2k/{id}_2k.hdr";
                using (var resp = await http.GetAsync(url).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return $"HTTP {(int)resp.StatusCode} {url}";
                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    File.WriteAllBytes(path, bytes);
                }
            }
            return null;
        }

        void Apply(RadianceHdr.Image img)
        {
            var tex = new Texture2D(img.Width, img.Height, TextureFormat.RGBAHalf, false, true)
            {
                wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HDRI"
            };
            var px = new Color[img.Width * img.Height];
            for (int y = 0; y < img.Height; y++)
            {
                int src = (img.Height - 1 - y) * img.Width; // file is top-down, Unity textures bottom-up
                for (int x = 0; x < img.Width; x++)
                {
                    int i = (src + x) * 3;
                    px[y * img.Width + x] = new Color(img.Rgb[i], img.Rgb[i + 1], img.Rgb[i + 2], 1f);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);

            var shader = Shader.Find("Skybox/Panoramic");
            if (shader == null) { Status = "Skybox/Panoramic shader missing (MYLTI ▸ Setup adds it to the build)"; return; }
            skyMat = new Material(shader) { name = "HDRI_Sky" };
            skyMat.SetTexture("_MainTex", tex);
            skyMat.SetFloat("_Mapping", 1f);      // latitude-longitude layout
            skyMat.SetFloat("_ImageType", 0f);    // 360°
            skyMat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            skyMat.SetFloat("_Exposure", 1.0f);
            RenderSettings.skybox = skyMat;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();
            SceneLook.HdriActive = true;

            if (probe == null)
            {
                var go = new GameObject("SkyReflectionProbe");
                go.transform.position = new Vector3(0, 12f, 150f);
                probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                probe.size = new Vector3(2000f, 400f, 2000f);
                probe.resolution = 256;
                probe.hdr = true;
                probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                probe.cullingMask = 0; // sky only: cheap and never shows stale cars
            }
            probe.RenderProbe();
        }
    }
}
