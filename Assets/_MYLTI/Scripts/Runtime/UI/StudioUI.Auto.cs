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
        List<string> windowsVoices;
        bool listingVoices;

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
            if (GUILayout.Toggle(s.TtsProvider == TtsProviderKind.WindowsVoice, " Giọng có sẵn của Windows — miễn phí, offline, không cần key (giọng cơ bản)")) s.TtsProvider = TtsProviderKind.WindowsVoice;
            if (GUILayout.Toggle(s.TtsProvider == TtsProviderKind.GoogleCloud, " Google Cloud Text-to-Speech — có hạn mức miễn phí hằng tháng, cần API key")) s.TtsProvider = TtsProviderKind.GoogleCloud;
            if (GUILayout.Toggle(s.TtsProvider == TtsProviderKind.ElevenLabs, " ElevenLabs — giọng hay nhất; gói Free KHÔNG cho dùng thương mại (kênh bật kiếm tiền cần gói trả phí)")) s.TtsProvider = TtsProviderKind.ElevenLabs;
            switch (s.TtsProvider)
            {
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
                    break;
            }
            GUILayout.Label("Nếu dịch vụ trả lỗi hoặc hết hạn mức, ứng dụng tự chuyển sang giọng Windows cho các câu còn thiếu.", small);

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

        async void ListVoices()
        {
            listingVoices = true;
            try { windowsVoices = await WindowsVoiceTts.ListVoicesAsync(); }
            catch (Exception e) { app.Msg("Không liệt kê được giọng: " + e.Message); }
            listingVoices = false;
        }
    }
}
