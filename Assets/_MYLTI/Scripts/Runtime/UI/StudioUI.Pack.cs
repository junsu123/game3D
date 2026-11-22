using System;
using System.IO;
using System.Linq;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    public sealed partial class StudioUI
    {
        // ================================================================ 7. YouTube Pack
        void PackTab()
        {
            var d = app.Director;
            var pack = d.Pack;
            GUILayout.Label("Results & YouTube Pack", h1);
            if (d.Run == null || pack == null)
            {
                GUILayout.Label(d.IsBusy ? "Run đang chạy — gói nội dung sẽ được tạo sau khi kết thúc." : "Chưa có run. Chạy 10-Minute Director trước.", wrap);
                return;
            }
            var cap = d.Run.Capture;
            if (cap.Verified) Status(CheckLevel.Pass, $"Video recorded — {DescriptionComposer.Timestamp(cap.Video.DurationSeconds ?? 0)} ({cap.Video.DurationSeconds:0.00}s) · {cap.Video.Path}");
            else Status(CheckLevel.Warn, $"Challenge complete — video chưa xác minh ({cap.Status}). Nội dung bên dưới là NHÁP.");
            GUILayout.Label($"Bộ tạo: {pack.Generator}. Chỉ dùng dữ kiện trong fact sheet; bạn duyệt trước khi đăng. Không tự đăng YouTube.", small);

            // results table
            GUILayout.Label("Kết quả từng vòng", h2);
            foreach (var r in d.Run.Rounds.OrderBy(x => x.RoundIndex))
                GUILayout.Label($"{(r.IsFinal ? "Final" : "Round " + r.RoundIndex)}: {r.Explanation}", small);

            // titles
            GUILayout.Space(6);
            GUILayout.Label("5 tiêu đề (TitleEditor)", h2);
            foreach (var t in pack.Titles)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label((t.Recommended ? "★ " : "") + t.Category, label, GUILayout.Width(150));
                string nt = GUILayout.TextField(t.Text, GUILayout.Width(560));
                if (nt != t.Text) { t.Text = nt; d.PackEdited = true; }
                GUILayout.Label($"{t.Length}c", small, GUILayout.Width(40));
                if (GUILayout.Button("Copy", GUILayout.Width(60))) Copy(t.Text);
                GUILayout.EndHorizontal();
                if (t.TruthCheckStatus != "Pass") GUILayout.Label("   " + t.TruthCheckStatus, bad);
            }

            // description
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mô tả SEO (DescriptionEditor)", h2);
            if (GUILayout.Button("Copy description", GUILayout.Width(140))) Copy(pack.Description);
            GUILayout.EndHorizontal();
            string nd = GUILayout.TextArea(pack.Description ?? "", GUILayout.MinHeight(220));
            if (nd != pack.Description) { pack.Description = nd; d.PackEdited = true; }
            if (!pack.Facts.ChaptersReliable) GUILayout.Label("Chapters bị bỏ qua vì timeline chưa đủ tin cậy (video chưa xác minh, hoặc OBS thủ công có độ lệch đầu video).", small);

            // pairs
            GUILayout.Space(6);
            GUILayout.Label("3 cặp Title + Thumbnail (A/B/C) — prompt chính + 2 biến thể (PromptEditor)", h2);
            GUILayout.Label("Chỉ là PROMPT — ứng dụng không tạo ảnh thumbnail. Ưu tiên dùng frame thật đính kèm làm tham chiếu.", small);
            foreach (var p in pack.Pairs)
            {
                GUILayout.BeginVertical(box);
                GUILayout.Label($"Cặp {p.PairId} — {p.Angle}", h2);
                GUILayout.Label($"Title: {p.Title}", wrap);
                GUILayout.Label($"Keyword: {p.PrimaryKeyword} · Chữ trên thumbnail: {(string.IsNullOrEmpty(p.ThumbnailText) ? "(không chữ)" : p.ThumbnailText)} · Frame: {p.ThumbnailFrameFile ?? "chưa có screenshot"}", small);
                GUILayout.Label("Lời hứa: " + p.OneSentencePromise, small);
                string np = GUILayout.TextArea(p.ThumbnailPrompt ?? "", GUILayout.MinHeight(90));
                if (np != p.ThumbnailPrompt) { p.ThumbnailPrompt = np; d.PackEdited = true; }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy title", GUILayout.Width(110))) Copy(p.Title);
                if (GUILayout.Button("Copy prompt", GUILayout.Width(110))) Copy(p.ThumbnailPrompt);
                if (!string.IsNullOrEmpty(p.ThumbnailFrameFile) && GUILayout.Button("Mở frame", GUILayout.Width(100)))
                    OpenFolder(Path.Combine(d.RunFolder, p.ThumbnailFrameFile));
                GUILayout.Label(p.TruthCheckStatus == "Pass" ? "truth ✓" : p.TruthCheckStatus, p.TruthCheckStatus == "Pass" ? good : bad);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            if (!string.IsNullOrEmpty(pack.WorkingTitleCheck) && pack.WorkingTitleCheck != "Pass")
                GUILayout.Label($"Working title trước khi quay \"{pack.WorkingTitle}\" không còn khớp kết quả → đề xuất: {pack.WorkingTitleSuggestion}", warn);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Kiểm tra lại tính trung thực", GUILayout.Height(30))) d.RecheckPack();
            if (GUILayout.Button("Tạo lại từ dữ kiện (mất chỉnh sửa)", GUILayout.Height(30))) d.RegenerateContent();
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUILayout.Button("Export All", GUILayout.Height(30)))
            {
                try
                {
                    var files = d.Export();
                    lastExportFolder = d.RunFolder;
                    app.Msg($"Exported {files.Count} files to {d.RunFolder}");
                }
                catch (Exception e) { app.Msg("Export failed: " + e.Message); }
            }
            GUI.backgroundColor = prev;
            if (lastExportFolder != null && GUILayout.Button("Mở thư mục", GUILayout.Height(30))) OpenFolder(lastExportFolder);
            GUILayout.EndHorizontal();
            if (pack.Issues.Any(i => i.Level == CheckLevel.Fail))
                foreach (var i in pack.Issues.Where(x => x.Level == CheckLevel.Fail).Take(8)) GUILayout.Label(i.ToString(), bad);
            GUILayout.Label("Thư mục run: " + d.RunFolder, small);
        }

        // ================================================================ 8. Quality
        void QualityTab()
        {
            var d = app.Director;
            GUILayout.Label("Quality Gate & Publish Readiness", h1);
            if (d.Quality == null) { GUILayout.Label("Chưa có báo cáo (chạy một run trước).", wrap); return; }
            var q = d.Quality;
            var lvl = q.Readiness == ReadinessLevel.PublishReady ? CheckLevel.Pass
                    : q.Readiness == ReadinessLevel.NeedsMoreFootage || q.Readiness == ReadinessLevel.NotRun ? CheckLevel.Fail : CheckLevel.Warn;
            Status(lvl, "Readiness: " + q.Readiness);
            GUILayout.Label("SimulationComplete → MediaReady → ReadyForReview → PublishReady (cần chủ kênh xem & duyệt). Các ngưỡng là heuristic cấu hình được, không phải luật YouTube và không dự báo lượt xem.", small);
            foreach (var c in q.Checks) Status(c.Level, $"{c.Label}: {c.Detail}");
            if (q.MostSimilarEpisode != null)
                GUILayout.Label($"Tập giống nhất: {q.MostSimilarEpisode} ({q.MostSimilarScore:P0}). Khác biệt: {string.Join(", ", q.NoveltyDifferences.Take(8))}", small);
            GUILayout.Space(8);
            bool approved = GUILayout.Toggle(d.HumanApproved, " Tôi đã xem toàn bộ video, kiểm tra tiêu đề/thumbnail khớp nội dung thật và duyệt đăng");
            if (approved != d.HumanApproved) d.SetHumanApproval(approved);
            if (GUILayout.Button("Đánh giá lại", GUILayout.Height(28))) d.RecheckQuality();
            GUILayout.Space(6);
            GUILayout.Label("Phân tích sau khi đăng (YouTube Analytics import) thuộc giai đoạn P1 — chưa có trong bản này.", small);
        }
    }
}
