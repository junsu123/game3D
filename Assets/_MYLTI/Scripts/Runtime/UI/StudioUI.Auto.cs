using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    public sealed partial class StudioUI
    {
        List<string> windowsVoices, elevenVoices;
        bool listingVoices, installingKokoro;
        string kokoroStatus;

        // ================================================================ 0. Auto Episode (one button)
        void AutoTab()
        {
            var p = app.Producer;
            var s = app.Settings;
            GUILayout.Label("Auto Episode — một nút ra video 10:00 hoàn chỉnh", h1);
            GUILayout.Label("Claude viết kịch bản mới (không trùng các tập trước) → game tự chạy và tự quay 1920×1080 (chỉ hình 3D, không chữ/thông số) → Claude viết lời bình tiếng Anh theo diễn biến THẬT → đọc thành giọng nói → xuất MP4 cuối + tiêu đề, mô tả, phụ đề, thumbnail.", wrap);

            GUILayout.Space(6);
            bool busy = p.Busy || app.Director.IsBusy;
            GUI.enabled = !busy;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUILayout.Button(busy ? "Đang sản xuất…" : "▶  AUTO EPISODE", GUILayout.Height(54))) p.StartAutoEpisode();
            GUI.backgroundColor = prevBg;
            GUI.enabled = true;

            foreach (var st in p.Steps)
            {
                var lvl = st.State == StepState.Done ? CheckLevel.Pass : st.State == StepState.Failed ? CheckLevel.Fail
                        : st.State == StepState.Running ? CheckLevel.Warn : CheckLevel.Pending;
                string icon = st.State == StepState.Skipped ? "(bỏ qua) " : "";
                Status(lvl, $"{st.Name}  {icon}{st.Detail}");
            }
            if (p.FinalVideoPath != null)
            {
                GUILayout.Space(4);
                GUILayout.Label("Video cuối: " + p.FinalVideoPath, good);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Xem video", GUILayout.Height(30))) Application.OpenURL("file:///" + p.FinalVideoPath.Replace('\\', '/'));
                if (GUILayout.Button("Mở thư mục", GUILayout.Height(30))) OpenFolder(Path.GetDirectoryName(p.FinalVideoPath));
                if (GUILayout.Button("Tiêu đề & mô tả (tab YouTube Pack)", GUILayout.Height(30))) tab = 8;
                GUILayout.EndHorizontal();
                GUILayout.Label("Upload: tệp FINAL_*.mp4 + captions_en.srt (phụ đề) + thumbnail_1280x720.jpg. Xem lại video trước khi đăng.", small);
            }
            GUI.enabled = !busy && app.Director.Run != null;
            if (GUILayout.Button("Làm lại hậu kỳ cho run gần nhất (lời bình + giọng + MP4)", GUILayout.Height(26))) p.StartPostProduction();
            GUI.enabled = true;

            // ---------------------------------------------------- Claude
            GUILayout.Space(10);
            GUILayout.Label("Claude (kịch bản + lời bình)", h2);
            s.UseClaude = GUILayout.Toggle(s.UseClaude, " Dùng Claude API");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Claude API key", label, GUILayout.Width(160));
            s.ClaudeApiKey = GUILayout.PasswordField(s.ClaudeApiKey ?? "", '*', GUILayout.Width(420));
            GUILayout.EndHorizontal();
            s.ClaudeModel = Field("Model", s.ClaudeModel, 240);
            s.LetClaudePickVehicles = GUILayout.Toggle(s.LetClaudePickVehicles, " Cho Claude chọn xe (đổi đội hình giữa các tập)");
            GUILayout.Label("Lấy key ở console.anthropic.com (tính phí theo token, tách biệt gói Claude.ai). Không có key → dùng bộ viết offline (vẫn xoay vòng không trùng, lời bình theo mẫu).", small);
            GUILayout.Label($"Đã sản xuất {app.Episodes.Items.Count} tập — lịch sử này được gửi cho Claude để tránh trùng.", small);

            // ---------------------------------------------------- Voice
            GUILayout.Space(8);
            GUILayout.Label("Giọng đọc tiếng Anh (TTS)", h2);
            s.NarrationEnabled = GUILayout.Toggle(s.NarrationEnabled, " Có lời bình");
            s.SoundDesignEnabled = GUILayout.Toggle(s.SoundDesignEnabled, " Âm thanh động cơ / gió / va chạm");
            if (GUILayout.Toggle(s.TtsProvider == TtsProviderKind.Kokoro, " ★ Kokoro (khuyên dùng) — giọng AI tự nhiên, MIỄN PHÍ, chạy trên máy, được dùng thương mại (Apache-2.0)")) s.TtsProvider = TtsProviderKind.Kokoro;
            if (GUILayout.Toggle(s.TtsProvider == TtsProviderKind.WindowsVoice, " Giọng có sẵn của Windows — miễn phí, offline, không cần key (giọng cơ bản)")) s.TtsProvider = TtsProviderKind.WindowsVoice;
            if (GUILayout.Toggle(s.TtsProvider == TtsProviderKind.GoogleCloud, " Google Cloud Text-to-Speech — có hạn mức miễn phí hằng tháng, cần API key")) s.TtsProvider = TtsProviderKind.GoogleCloud;
            if (GUILayout.Toggle(s.TtsProvider == TtsProviderKind.ElevenLabs, " ElevenLabs — giọng hay nhất; gói Free KHÔNG cho dùng thương mại (kênh bật kiếm tiền cần gói trả phí)")) s.TtsProvider = TtsProviderKind.ElevenLabs;
            switch (s.TtsProvider)
            {
                case TtsProviderKind.Kokoro:
                    GUILayout.Label("Lần đầu tự tải ~150 MB (sherpa-onnx + model Kokoro-82M) từ GitHub. Sau đó chạy offline, không giới hạn.", small);
                    GUILayout.Label("Giọng đang chọn: " + KokoroTts.VoiceName(s.KokoroVoice), good);
                    GUILayout.BeginHorizontal();
                    int col = 0;
                    foreach (var v in KokoroTts.Voices)
                    {
                        if (GUILayout.Toggle(s.KokoroVoice == v.Key, v.Value.Split(' ')[0], GUI.skin.button, GUILayout.Width(118))) s.KokoroVoice = v.Key;
                        if (++col % 5 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Label("af = nữ Mỹ, am = nam Mỹ, bf/bm = nữ/nam Anh. Gợi ý: af_heart (chất lượng cao nhất), am_michael / am_fenrir (nam, hợp bình luận).", small);
                    GUILayout.BeginHorizontal();
                    GUI.enabled = !installingKokoro && !busy;
                    if (GUILayout.Button("Tải / kiểm tra Kokoro", GUILayout.Width(200))) InstallKokoro(false);
                    if (GUILayout.Button("▶ Nghe thử giọng", GUILayout.Width(160))) InstallKokoro(true);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(kokoroStatus)) GUILayout.Label(kokoroStatus, small);
                    break;
                case TtsProviderKind.WindowsVoice:
                    s.TtsVoice = Field("Tên giọng (trống = tự chọn)", s.TtsVoice, 300);
                    GUI.enabled = !listingVoices;
                    if (GUILayout.Button("Liệt kê giọng trên máy", GUILayout.Width(220))) ListVoices();
                    GUI.enabled = true;
                    if (windowsVoices != null)
                        foreach (var v in windowsVoices)
                        {
                            var parts = v.Split('|');
                            if (GUILayout.Button(v, GUILayout.Width(420))) s.TtsVoice = parts[0];
                        }
                    break;
                case TtsProviderKind.GoogleCloud:
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Google API key", label, GUILayout.Width(160));
                    s.GoogleTtsKey = GUILayout.PasswordField(s.GoogleTtsKey ?? "", '*', GUILayout.Width(420));
                    GUILayout.EndHorizontal();
                    s.TtsVoice = Field("Voice (vd en-US-Standard-J)", s.TtsVoice, 300);
                    break;
                case TtsProviderKind.ElevenLabs:
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("ElevenLabs key", label, GUILayout.Width(160));
                    s.ElevenLabsKey = GUILayout.PasswordField(s.ElevenLabsKey ?? "", '*', GUILayout.Width(420));
                    GUILayout.EndHorizontal();
                    s.ElevenLabsVoiceId = Field("Voice ID", s.ElevenLabsVoiceId, 300);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Model", label, GUILayout.Width(160));
                    foreach (var m in ElevenLabsTts.Models)
                        if (GUILayout.Toggle(s.ElevenLabsModel == m, m, GUI.skin.button, GUILayout.Width(190))) s.ElevenLabsModel = m;
                    GUILayout.EndHorizontal();
                    GUILayout.Label("multilingual_v2 = tự nhiên nhất; turbo/flash = rẻ hơn (tốn ít credit hơn mỗi ký tự).", small);
                    GUI.enabled = !listingVoices;
                    if (GUILayout.Button("Liệt kê giọng trong tài khoản", GUILayout.Width(240))) ListElevenVoices();
                    GUI.enabled = true;
                    if (elevenVoices != null)
                        foreach (var v in elevenVoices.Take(30))
                        {
                            var parts = v.Split('|');
                            if (parts[0] == "ERROR") { GUILayout.Label(v, warn); continue; }
                            if (GUILayout.Button(parts[1] + "  —  " + (parts.Length > 2 ? parts[2] : ""), GUILayout.Width(520))) s.ElevenLabsVoiceId = parts[0];
                        }
                    break;
            }
            GUILayout.Label("Nếu dịch vụ trả lỗi hoặc hết hạn mức, ứng dụng tự chuyển sang giọng Windows cho các câu còn thiếu.", small);

            // ---------------------------------------------------- On-screen text
            GUILayout.Space(8);
            GUILayout.Label("Chữ trong video", h2);
            s.ShowNameplates = GUILayout.Toggle(s.ShowNameplates, " Tên xe + tốc độ (km/h) trên đầu xe");
            Nameplate.Enabled = s.ShowNameplates;
            if (s.ShowNameplates) { s.NameplateSize = Slider("Cỡ chữ tên xe / tốc độ", s.NameplateSize, 0.6f, 1.6f, "0.0"); Nameplate.Size = s.NameplateSize; }
            s.HandheldCamera = Slider("Rung tay máy quay (0 = chân máy cố định)", s.HandheldCamera, 0f, 1.5f, "0.0");
            if (app.Cam != null) app.Cam.HandheldAmount = s.HandheldCamera;
            s.BurnTextCards = GUILayout.Toggle(s.BurnTextCards, " Tiêu đề hook đầu video, giới thiệu từng thử thách, câu gây tò mò, kết quả vòng");
            GUILayout.Label("Câu hook chỉ “nhá” những gì THẬT SỰ xảy ra ở cảnh sau (lấy từ kết quả đã quay), không bao giờ lộ người thắng trước. Có Claude key thì Claude viết lại cho cuốn hơn (vẫn bị kiểm tra).", small);

            // ---------------------------------------------------- Music & sky
            GUILayout.Space(8);
            GUILayout.Label("Nhạc nền & bầu trời", h2);
            s.MusicEnabled = GUILayout.Toggle(s.MusicEnabled, " Nhạc nền từ thư mục Music (mỗi tập lấy một bài, tự nhỏ đi khi có lời bình)");
            if (s.MusicEnabled)
            {
                s.MusicVolume = Slider("Âm lượng nhạc", s.MusicVolume, 0f, 1f, "0.00");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Mở thư mục Music", GUILayout.Width(180))) OpenFolder(System.IO.Path.Combine(app.DataRoot, "Music"));
                GUILayout.Label("Chỉ dùng nhạc bạn có quyền dùng trên YouTube — ví dụ tải từ YouTube Studio ▸ Audio Library (miễn phí, không bị Content ID).", small);
                GUILayout.EndHorizontal();
            }
            // ---------------------------------------------------- Crowd
            bool crowdOn = GUILayout.Toggle(s.CrowdEnabled, " Khán giả hoạt hình: người thật đứng trên khán đài & sau hàng rào, nhìn theo xe, vỗ tay, giơ tay reo hò, nhảy lên");
            if (crowdOn != s.CrowdEnabled) { s.CrowdEnabled = crowdOn; CrowdDirector.Enabled = crowdOn; app.Crowd?.Repopulate(); }
            if (s.CrowdEnabled)
            {
                float dens = Slider("Số khán giả (máy yếu → kéo nhỏ)", s.CrowdDensity, 0.3f, 1.5f, "0.0");
                if (Mathf.Abs(dens - s.CrowdDensity) > 0.001f) { s.CrowdDensity = dens; CrowdDirector.Density = dens; }
                float hs = Slider("Tỉ lệ khán giả hoá trang (MYLTI Squad + MYLTI Legends)", s.CrowdHeroShare, 0f, 1f, "0.00");
                if (Mathf.Abs(hs - s.CrowdHeroShare) > 0.001f) { s.CrowdHeroShare = hs; CrowdDirector.HeroShare = hs; }
                GUILayout.Label("MYLTI Squad = 7 siêu anh hùng (Turbo Ranger, Nova Spark, Gear Guardian, Comet Kid, Rocket Rex, Pixel Knight, Aurora Dash). MYLTI Legends = 6 chiến binh phong cách game (Ember Ronin, Volt Striker, Desert Drifter, Steel Hornet, Jade Warden, Night Courier). Tất cả do kênh tự sáng tạo — không dựa trên nhân vật có bản quyền nào.", small);
                s.CrowdVolume = Slider("Tiếng reo hò / vỗ tay trong video", s.CrowdVolume, 0f, 1f, "0.00");
                GUILayout.BeginHorizontal();
                if (app.Crowd != null) GUILayout.Label($"Đang có {app.Crowd.Count} khán giả ({app.Crowd.RealCount} nhân vật 3D riêng). Nhân vật riêng: {app.Crowd.RealCharactersSummary()}", small);
                if (GUILayout.Button("Dựng lại khán đài", GUILayout.Width(160))) app.Crowd.Repopulate();
                GUILayout.EndHorizontal();
                GUILayout.Label("Muốn người giống thật hơn: tải nhân vật + động tác (Idle, Clapping, Cheering…) miễn phí từ Mixamo dạng FBX, bỏ vào Assets/_MYLTI/Resources/Crowd — hàng ghế đầu sẽ tự dùng chúng.", small);
            }
            bool sky = GUILayout.Toggle(s.UseHdriSky, " Bầu trời chụp thật (HDRI miễn phí CC0 từ Poly Haven) + phản chiếu trên thân xe");
            if (sky != s.UseHdriSky) { s.UseHdriSky = sky; if (sky) app.StartCoroutine(app.Sky.Load(app.DataRoot, s.HdriId, app.Msg)); }
            if (s.UseHdriSky)
            {
                GUILayout.BeginHorizontal();
                foreach (var id in HdriSky.Suggested)
                    if (GUILayout.Toggle(s.HdriId == id, id.Replace("_puresky", "").Replace('_', ' '), GUI.skin.button, GUILayout.Width(200)) && s.HdriId != id)
                    {
                        s.HdriId = id;
                        app.StartCoroutine(app.Sky.Load(app.DataRoot, id, app.Msg));
                    }
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(app.Sky.Status)) GUILayout.Label(app.Sky.Status + " (có thể thả file .hdr của bạn vào thư mục Sky)", small);
            }

            // ---------------------------------------------------- Recording
            GUILayout.Space(8);
            GUILayout.Label("Tự quay màn hình (FFmpeg)", h2);
            GUILayout.Label(app.Ffmpeg.Available ? "✓ FFmpeg: " + app.Ffmpeg.ExePath : "FFmpeg chưa có — sẽ tự tải ở lần chạy đầu (~150 MB, cần Internet một lần).", app.Ffmpeg.Available ? good : warn);
            GUILayout.BeginHorizontal();
            GUI.enabled = !busy && !app.Ffmpeg.Available;
            if (GUILayout.Button("Tải FFmpeg ngay", GUILayout.Width(180))) p.DownloadFfmpeg();
            GUI.enabled = !busy;
            if (GUILayout.Button("Quay thử 3 giây", GUILayout.Width(180))) p.TestRecording();
            GUI.enabled = true;
            bool flip = GUILayout.Toggle(s.FlipVertical, " Lật dọc (bật nếu video quay thử bị ngược)");
            if (flip != s.FlipVertical) { s.FlipVertical = flip; app.Recorder.FlipVertical = flip; }
            GUILayout.EndHorizontal();
            bool draft = GUILayout.Toggle(s.DraftDuration, " Bản thử 5:00 (để kiểm tra nhanh — không dùng để đăng)");
            if (draft != s.DraftDuration) s.DraftDuration = draft;
            s.RememberApiKeys = GUILayout.Toggle(s.RememberApiKeys, " Nhớ các API key trên máy này (lưu dạng văn bản trong settings.json)");
            if (GUI.changed) app.SaveSettings();
        }

        async void ListElevenVoices()
        {
            listingVoices = true;
            try { elevenVoices = await ElevenLabsTts.ListVoicesAsync(app.Settings.ElevenLabsKey); }
            catch (Exception e) { app.Msg("ElevenLabs: " + e.Message); }
            listingVoices = false;
        }

        async void InstallKokoro(bool sample)
        {
            installingKokoro = true;
            kokoroStatus = "Đang chuẩn bị…";
            try
            {
                var k = new KokoroTts(TtsFactory.KokoroDir, app.Settings.KokoroVoice) { Progress = (p, m) => kokoroStatus = $"{m} ({p:P0})" };
                if (!await k.EnsureInstalledAsync(m => kokoroStatus = m)) { kokoroStatus = "✗ " + k.LastError; return; }
                kokoroStatus = "✓ Kokoro sẵn sàng: " + k.ExePath;
                if (sample)
                {
                    string dir = System.IO.Path.Combine(TtsFactory.KokoroDir, "samples");
                    var line = new KeyValuePair<string, string>("sample_" + app.Settings.KokoroVoice,
                        "Three vehicles. One mega ramp. Only one of them lands it cleanly, and nobody saw round three coming.");
                    var r = await k.SynthesizeAsync(new List<KeyValuePair<string, string>> { line }, dir, m => kokoroStatus = m);
                    if (r.TryGetValue(line.Key, out string wav)) { kokoroStatus = "✓ Đã tạo: " + wav; Application.OpenURL("file:///" + wav.Replace('\\', '/')); }
                    else kokoroStatus = "✗ " + k.LastError;
                }
            }
            catch (Exception e) { kokoroStatus = "✗ " + e.GetBaseException().Message; }
            finally { installingKokoro = false; }
        }

        async void ListVoices()
        {
            listingVoices = true;
            try { windowsVoices = await WindowsVoiceTts.ListVoicesAsync(); }
            catch (Exception e) { app.Msg("Không liệt kê được giọng: " + e.Message); }
            listingVoices = false;
        }
    }
}
