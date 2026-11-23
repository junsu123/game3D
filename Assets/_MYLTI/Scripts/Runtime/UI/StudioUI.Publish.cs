using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    public sealed partial class StudioUI
    {
        bool signingIn, showMarkets, showSetupHelp;
        string publishMsg;

        void PublishTab()
        {
            var ap = app.Autopilot;
            var ps = ap.Settings;
            GUI.changed = false;
            GUILayout.Label("📅 Lịch sản xuất & tự đăng YouTube", h1);
            GUILayout.Label("Thị trường chính: Mỹ, kèm châu Âu. App chọn giờ lên sóng theo múi giờ khán giả, tự làm video trước, tải lên ở chế độ Riêng tư với giờ công khai hẹn sẵn — YouTube tự công khai đúng giờ.", wrap);

            // ---------------------------------------------------- YouTube connection
            GUILayout.Space(8);
            GUILayout.Label("1. Kết nối kênh YouTube (đăng nhập Google — app không bao giờ cần mật khẩu của bạn)", h2);
            if (ap.YouTube.SignedIn)
            {
                GUILayout.Label("✓ Đã kết nối: " + (ap.YouTube.Token.ChannelTitle ?? "(kênh)") + "  ·  " + (ap.YouTube.Token.ChannelId ?? ""), good);
                if (GUILayout.Button("Ngắt kết nối", GUILayout.Width(160))) { ap.YouTube.SignOut(); publishMsg = "Đã xoá đăng nhập trên máy này."; }
            }
            else
            {
                ps.GoogleClientId = Field("OAuth Client ID", ps.GoogleClientId, 520);
                GUILayout.BeginHorizontal();
                GUILayout.Label("OAuth Client secret", label, GUILayout.Width(160));
                ps.GoogleClientSecret = GUILayout.PasswordField(ps.GoogleClientSecret ?? "", '*', GUILayout.Width(360));
                GUILayout.EndHorizontal();
                GUI.enabled = !signingIn && !string.IsNullOrWhiteSpace(ps.GoogleClientId);
                if (GUILayout.Button(signingIn ? "Đang chờ đăng nhập trong trình duyệt…" : "Đăng nhập Google & chọn kênh", GUILayout.Width(320), GUILayout.Height(30))) SignIn();
                GUI.enabled = true;
                showSetupHelp = GUILayout.Toggle(showSetupHelp, " Hướng dẫn lấy Client ID (làm 1 lần, ~10 phút)");
                if (showSetupHelp)
                    GUILayout.Label(
                        "1) console.cloud.google.com → tạo Project mới.\n" +
                        "2) APIs & Services → Library → bật “YouTube Data API v3”.\n" +
                        "3) Google Auth Platform (OAuth consent screen) → External → điền tên app + email của bạn.\n" +
                        "4) Audience → bấm “Publish app” (In production). Nếu để Testing, Google bắt đăng nhập lại mỗi 7 ngày.\n" +
                        "5) Clients → Create client → loại “Desktop app” → copy Client ID và Client secret dán vào đây.\n" +
                        "6) Bấm Đăng nhập: Google sẽ báo “app chưa được xác minh” vì đây là app riêng của bạn → Advanced → Go to … (unsafe) → cho phép.\n" +
                        "Mỗi ngày YouTube cho tối đa 100 lượt tải lên qua API — dư cho lịch này.", small);
            }
            if (!string.IsNullOrEmpty(ap.YouTube.LastError)) GUILayout.Label(ap.YouTube.LastError, bad);

            // ---------------------------------------------------- schedule
            GUILayout.Space(8);
            GUILayout.Label("2. Lịch đăng", h2);
            ps.VideosPerWeek = Mathf.RoundToInt(Slider("Số video / tuần", ps.VideosPerWeek, 1, 7, "0"));
            ps.ReviewWindowHours = Mathf.RoundToInt(Slider("Tải lên trước giờ công khai", ps.ReviewWindowHours, 2, 72, "0", " giờ"));
            GUILayout.Label("Khoảng này để bạn kịp xem lại trong YouTube Studio nếu muốn (video ở chế độ Riêng tư cho tới giờ hẹn).", small);
            ps.AutoPublish = GUILayout.Toggle(ps.AutoPublish, " Tự công khai đúng giờ hẹn (tắt = để Riêng tư, bạn tự bấm công khai)");
            ps.NotifySubscribers = GUILayout.Toggle(ps.NotifySubscribers, " Thông báo cho người đăng ký");
            var pattern = PublishPlanner.WeeklyPattern(ps.Markets, ps.VideosPerWeek, DateTime.UtcNow);
            GUILayout.Label("Khung giờ đẹp nhất mỗi tuần (tính theo tỉ trọng thị trường bên dưới, tự đổi khi Mỹ/châu Âu đổi giờ mùa hè):", label);
            var monday = DateTime.UtcNow.Date.AddDays(-(((int)DateTime.UtcNow.DayOfWeek + 6) % 7));
            foreach (var s in pattern)
            {
                var t = DateTime.SpecifyKind(monday.AddDays(((int)s.DayUtc + 6) % 7).AddHours(s.HourUtc).AddMinutes(s.MinuteUtc), DateTimeKind.Utc);
                GUILayout.Label("  • " + PublishPlanner.Describe(t, Autopilot.LocalOffset()), mono);
            }
            GUILayout.Label("Lịch 14 ngày tới (giờ trống):", label);
            foreach (var t in ap.NextSlots(Math.Max(2, ps.VideosPerWeek * 2)))
                GUILayout.Label("  ▸ " + PublishPlanner.Describe(t, Autopilot.LocalOffset()), mono);

            showMarkets = GUILayout.Toggle(showMarkets, " Tỉ trọng thị trường (Mỹ + châu Âu) — chỉnh theo YouTube Analytics khi kênh có số liệu");
            if (showMarkets)
            {
                double sum = ps.Markets.Sum(m => m.Weight);
                foreach (var m in ps.Markets)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{m.Name}", label, GUILayout.Width(130));
                    m.Weight = Math.Round(GUILayout.HorizontalSlider((float)m.Weight, 0f, 0.8f, GUILayout.Width(150)), 3);
                    GUILayout.Label($"{(sum > 0 ? m.Weight / sum : 0):P0}", label, GUILayout.Width(48));
                    GUILayout.Label($"RPM ~${(m.RpmUsd ?? 0).ToString("0.00", CultureInfo.InvariantCulture)} · tiếng Anh: {m.English}" + (m.MetadataLanguage != null ? $" · dịch tiêu đề: {m.MetadataLanguage}" : "") + (m.AutoDub ? " · có lồng tiếng tự động" : ""), small);
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Khôi phục tỉ trọng mặc định", GUILayout.Width(240))) ps.Markets = Market.Defaults();
                GUILayout.Label("RPM: dữ liệu Dynamoi (11/2025–11/2026, mọi thể loại, không phải RPM riêng của kênh bạn). Trình độ tiếng Anh: EF EPI 2026. Chi tiết & nguồn: docs/MARKET_RESEARCH.md.", small);
            }

            // ---------------------------------------------------- Europe
            GUILayout.Space(8);
            GUILayout.Label("3. Châu Âu", h2);
            ps.TranslateMetadata = GUILayout.Toggle(ps.TranslateMetadata, " Dịch tiêu đề + mô tả sang " + string.Join(", ", Market.LanguagesFor(ps.Markets)) + " (Claude; kiểm tra giữ nguyên số liệu & mốc chương)");
            if (ps.TranslateMetadata && (!app.Settings.UseClaude || string.IsNullOrWhiteSpace(app.Settings.ClaudeApiKey)))
                GUILayout.Label("Cần Claude API key (tab ▶ Auto Episode) để dịch; không có key thì chỉ đăng tiếng Anh.", warn);
            GUILayout.Label("Nên bật thêm trong YouTube Studio ▸ Settings ▸ Upload defaults ▸ Advanced: “Automatic dubbing” — YouTube tự lồng tiếng Đức/Pháp/Tây Ban Nha/Ý/Ba Lan/Hà Lan… (API không bật hộ được).", small);

            // ---------------------------------------------------- upload options
            GUILayout.Space(8);
            GUILayout.Label("4. Tuỳ chọn tải lên", h2);
            ps.UploadThumbnail = GUILayout.Toggle(ps.UploadThumbnail, " Thumbnail (khung hình thật) — kênh phải xác minh số điện thoại");
            ps.UploadCaptions = GUILayout.Toggle(ps.UploadCaptions, " Phụ đề tiếng Anh (captions_en.srt)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Danh mục", label, GUILayout.Width(160));
            if (GUILayout.Toggle(ps.CategoryId == "20", "Gaming", GUI.skin.button, GUILayout.Width(120))) ps.CategoryId = "20";
            if (GUILayout.Toggle(ps.CategoryId == "2", "Autos & Vehicles", GUI.skin.button, GUILayout.Width(160))) ps.CategoryId = "2";
            GUILayout.EndHorizontal();
            ps.DeclareSyntheticMedia = GUILayout.Toggle(ps.DeclareSyntheticMedia, " Gắn nhãn “nội dung thay đổi/tổng hợp” (YouTube không bắt buộc cho cảnh game + giọng đọc AI chung chung)");
            ps.MadeForKids = GUILayout.Toggle(ps.MadeForKids, " Video dành cho trẻ em (Made for Kids)");
            GUILayout.Label("Bạn phải tự khai báo đúng theo luật COPPA. Nếu khán giả chính là trẻ em (ví dụ vì nội dung siêu anh hùng nhắm tới các bạn nhỏ) thì bật lên — khi đó YouTube tắt bình luận và quảng cáo cá nhân hoá. Nếu nhắm tới người lớn/mọi lứa tuổi thì để tắt.", small);

            // ---------------------------------------------------- autopilot
            GUILayout.Space(8);
            GUILayout.Label("5. Tự động hoàn toàn", h2);
            bool before = ps.AutopilotEnabled;
            ps.AutopilotEnabled = GUILayout.Toggle(ps.AutopilotEnabled, " ▶ BẬT AUTOPILOT: tự làm video, tự tải lên, tự hẹn giờ");
            if (ps.AutopilotEnabled && !before) ap.RunNow();
            ps.BufferEpisodes = Mathf.RoundToInt(Slider("Làm sẵn trước", ps.BufferEpisodes, 1, 6, "0", " tập"));
            ps.MaxProductionsPerDay = Mathf.RoundToInt(Slider("Tối đa / ngày", ps.MaxProductionsPerDay, 1, 4, "0", " tập"));
            GUILayout.Label("Trạng thái: " + ap.Status + (ap.Working && ap.UploadProgress > 0 && ap.UploadProgress < 1 ? $" ({ap.UploadProgress:P0})" : ""), ap.YouTube.SignedIn ? good : warn);
            GUILayout.BeginHorizontal();
            GUI.enabled = !ap.Working && !app.Producer.Busy;
            if (GUILayout.Button("Chạy kiểm tra ngay", GUILayout.Width(180))) ap.RunNow();
            if (GUILayout.Button("Thêm video vừa làm vào hàng đợi", GUILayout.Width(260))) publishMsg = ap.EnqueueLastEpisode() ?? "Đã thêm vào hàng đợi.";
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label("Lịch Windows (bản .exe): mở app mỗi ngày, làm/tải video còn thiếu rồi tự đóng.", label);
            ps.ProductionHourLocal = Mathf.RoundToInt(Slider("Giờ chạy hằng ngày (giờ máy)", ps.ProductionHourLocal, 0, 23, "0", ":00"));
            ps.WakeComputer = GUILayout.Toggle(ps.WakeComputer, " Đánh thức máy nếu đang ngủ (cần bật “Allow wake timers” trong Power Options)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Tạo / cập nhật lịch Windows", GUILayout.Width(240))) { ap.Save(); publishMsg = ap.InstallWindowsTask() ?? "✓ Đã tạo lịch “" + Autopilot.TaskName + "” trong Task Scheduler."; }
            if (GUILayout.Button("Xoá lịch Windows", GUILayout.Width(160))) publishMsg = ap.RemoveWindowsTask() ?? "Đã xoá lịch.";
            GUILayout.EndHorizontal();
            GUILayout.Label("Máy phải bật và đã đăng nhập Windows lúc chạy (cần GPU để quay). Mỗi tập mất khoảng thời gian quay 10 phút + hậu kỳ.", small);
            if (!string.IsNullOrEmpty(publishMsg)) GUILayout.Label(publishMsg, publishMsg.StartsWith("✓") || publishMsg.StartsWith("Đã") ? good : warn);

            // ---------------------------------------------------- queue
            GUILayout.Space(8);
            GUILayout.Label($"Hàng đợi ({ap.Queue.Items.Count})", h2);
            foreach (var it in ap.Queue.Items.OrderByDescending(i => i.ProducedAtUtc).Take(12))
            {
                GUILayout.BeginHorizontal(box);
                string when = it.PublishAtUtc.HasValue ? PublishPlanner.Describe(it.PublishAtUtc.Value, Autopilot.LocalOffset()) : "";
                var style = it.State == PublishState.Failed || it.State == PublishState.Skipped ? bad : it.State == PublishState.Scheduled || it.State == PublishState.Published ? good : label;
                GUILayout.Label($"[{it.State}{(it.Manual ? " · riêng tư" : "")}] {it.Title}\n{when}\n{it.Error ?? it.Notes}", style, GUILayout.Width(760));
                if (!string.IsNullOrEmpty(it.VideoId) && GUILayout.Button("Mở", GUILayout.Width(60))) Application.OpenURL("https://studio.youtube.com/video/" + it.VideoId + "/edit");
                GUILayout.EndHorizontal();
            }
            if (GUI.changed) ap.Save();
        }

        async void SignIn()
        {
            signingIn = true;
            var ap = app.Autopilot;
            ap.Save();
            ap.RebuildClient();
            try
            {
                bool ok = await ap.YouTube.SignInAsync(url => Application.OpenURL(url), m => app.Msg(m));
                publishMsg = ok ? "✓ Đã kết nối kênh " + ap.YouTube.Token.ChannelTitle : ap.YouTube.LastError;
            }
            catch (Exception e) { publishMsg = e.Message; }
            signingIn = false;
        }
    }
}
