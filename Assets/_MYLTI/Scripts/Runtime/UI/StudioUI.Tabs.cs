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
        // ================================================================ 0. Challenge (sandbox)
        void ChallengeTab()
        {
            var c = app.Config;
            var s = c.Sandbox;
            GUILayout.Label("Thử thách Mega Ramp (sandbox)", h1);
            GUILayout.Label("Chỉnh thông số rồi bấm Áp dụng / Start. Mọi giá trị được kiểm tra giới hạn.", small);
            s.KickerHeight = Slider("Độ cao dốc", s.KickerHeight, 2, 30, "0.0", " m");
            s.KickerAngleDeg = Slider("Góc dốc", s.KickerAngleDeg, 8, 40, "0", "°");
            s.RunwayLength = Slider("Đường tăng tốc", s.RunwayLength, 160, 400, "0", " m");
            s.SpeedFactor = Slider("Hệ số tốc độ", s.SpeedFactor, 0.4f, 1.3f, "0.00", "×");
            bool bridge = GUILayout.Toggle(s.BridgeWidth > 0, " Cầu hẹp trước dốc");
            if (bridge && s.BridgeWidth <= 0) s.BridgeWidth = 6f;
            if (!bridge) s.BridgeWidth = 0f;
            if (bridge) s.BridgeWidth = Slider("Bề rộng cầu", s.BridgeWidth, 3, 8, "0.0", " m");
            s.LandingRamp = GUILayout.Toggle(s.LandingRamp, " Dốc tiếp đất");
            s.Obstacles = Cycle("Chướng ngại", s.Obstacles);
            s.Rule = Cycle("Điều kiện thắng", s.Rule);
            if (s.Rule == ScoringRule.BeyondTheWall || s.Obstacles == ObstacleSet.CrateWall || s.Obstacles == ObstacleSet.BarrelsAndWall)
                s.WallDistance = Slider("Tường thùng ở", s.WallDistance, 25, 200, "0", " m");
            if (s.Rule == ScoringRule.TargetZone) s.TargetDistance = Slider("Mục tiêu ở", s.TargetDistance, 20, 200, "0", " m");
            if (s.Rule == ScoringRule.CrashWall) s.CrashSpeedKmh = Slider("Tốc độ đâm tường", s.CrashSpeedKmh, 30, 160, "0", " km/h");
            if (s.Rule == ScoringRule.DropTest) s.DropHeightM = Slider("Độ cao thả", s.DropHeightM, 5, 60, "0", " m");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", label, GUILayout.Width(160));
            if (int.TryParse(GUILayout.TextField(c.Seed.ToString(), GUILayout.Width(120)), out int seed)) c.Seed = seed;
            GUILayout.EndHorizontal();
            c.AutoRecoverStuckSeconds = Slider("Tự hồi sinh khi kẹt sau", c.AutoRecoverStuckSeconds, 3, 30, "0", " s");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUI.enabled = !app.Director.IsBusy;
            if (GUILayout.Button("Áp dụng bản đồ", GUILayout.Height(30))) app.ApplyConfigToScene();
            if (GUILayout.Button("Start", GUILayout.Height(30))) app.StartSandbox();
            if (GUILayout.Button("Reset", GUILayout.Height(30))) app.ResetSandbox();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Kết quả lượt gần nhất", h2);
            if (app.LastSandboxResults.Count == 0) GUILayout.Label("Chưa có.", small);
            foreach (var r in app.LastSandboxResults)
            {
                var d = VehicleCatalog.Find(r.VehicleId);
                GUILayout.Label($"{d?.DisplayNameVi}: {r.Outcome} · xa {(r.LandingDistanceM.HasValue ? r.LandingDistanceM.Value.ToString("0.0") + " m" : "—")} · max {r.MaxSpeedKmh:0} km/h · cao {r.PeakHeightM:0.0} m · bay {r.AirTimeS:0.0}s · va chạm {r.ObstacleHits} · hồi sinh {r.Retries}", wrap);
            }
            GUILayout.Space(8);
            GUILayout.Label("Điều khiển thủ công: W/S hoặc ↑/↓ ga-lùi, A/D đánh lái, Space phanh, R reset. Đổi chế độ lái ở tab Xe.", small);
        }

        // ================================================================ 1. Vehicles
        static readonly string[] Palette = { "#D12B38", "#2C82D3", "#E8A33D", "#2DBE6C", "#8E44AD", "#F1C40F", "#ECF0F1", "#222831" };

        void VehicleTab()
        {
            var c = app.Config;
            GUILayout.Label("Xe tham gia (1–3)", h1);
            UserVehiclesPanel();
            GUILayout.Label("Danh sách xe (xe 3D thật của bạn + 3 xe mẫu có sẵn):", small);
            foreach (var def in VehicleCatalog.All)
            {
                var e = c.Entrants.FirstOrDefault(x => x.VehicleId == def.Id);
                GUILayout.BeginVertical(box);
                bool on = GUILayout.Toggle(e != null, $" {def.DisplayNameVi} — {def.MassKg:0} kg, tối đa {def.TopSpeedKmh:0} km/h");
                if (on && e == null && c.Entrants.Count < 3)
                {
                    e = new Entrant { VehicleId = def.Id, ColorHex = def.DefaultColorHex, TargetSpeedKmh = def.DefaultTargetSpeedKmh };
                    c.Entrants.Add(e);
                }
                else if (!on && e != null && c.Entrants.Count > 1) { c.Entrants.Remove(e); e = null; }
                if (e != null)
                {
                    e.Mode = Cycle("Chế độ lái", e.Mode);
                    e.TargetSpeedKmh = Slider("Tốc độ mục tiêu", e.TargetSpeedKmh, 30, def.TopSpeedKmh, "0", " km/h");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Màu", label, GUILayout.Width(40));
                    foreach (var hex in Palette)
                    {
                        ColorUtility.TryParseHtmlString(hex, out var col);
                        var prev = GUI.backgroundColor;
                        GUI.backgroundColor = col;
                        if (GUILayout.Button(e.ColorHex == hex ? "●" : " ", GUILayout.Width(26))) e.ColorHex = hex;
                        GUI.backgroundColor = prev;
                    }
                    e.ColorHex = GUILayout.TextField(e.ColorHex, GUILayout.Width(80));
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
            for (int i = 0; i < c.Entrants.Count; i++) c.Entrants[i].Lane = i;
            GUI.enabled = !app.Director.IsBusy;
            if (GUILayout.Button("Áp dụng xe vào cảnh", GUILayout.Height(30))) app.ApplyConfigToScene();
            GUI.enabled = true;
            GUILayout.Space(6);
            GUILayout.Label("Vật lý: Rigidbody + WheelCollider, dẫn động 4 bánh, Continuous Collision Detection, fixed timestep 0.02 s. Thân xe cứng — không mô phỏng biến dạng.", small);
        }

        // ================================================================ 2. Camera / display
        void CameraTab()
        {
            GUILayout.Label("Camera", h1);
            GUILayout.Label("Phím: 1 Chase · 2 Side · 3 Front/Lip · 4 Impact · 5 Wide · 6 Top · 7 Orbit · 8 Free (chuột phải + WASD, Q/E) · 0 Auto", small);
            Shot[] shots = { Shot.Auto, Shot.Chase, Shot.SideTracking, Shot.LowAngleLip, Shot.ImpactCloseup, Shot.FixedWide, Shot.TopDown, Shot.Orbit, Shot.Free };
            int cols = 3;
            for (int i = 0; i < shots.Length; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Math.Min(i + cols, shots.Length); j++)
                {
                    var prev = GUI.backgroundColor;
                    if (app.Cam.Requested == shots[j]) GUI.backgroundColor = new Color(1f, 0.75f, 0.2f);
                    if (GUILayout.Button(shots[j].ToString(), GUILayout.Height(28))) app.Cam.Requested = shots[j];
                    GUI.backgroundColor = prev;
                }
                GUILayout.EndHorizontal();
            }
            app.Cam.AllowShake = GUILayout.Toggle(app.Cam.AllowShake, " Rung camera nhẹ khi va chạm (giới hạn)");
            GUILayout.Space(10);
            GUILayout.Label("Hiển thị trong video", h2);
            var s = app.Settings;
            bool cards = GUILayout.Toggle(s.ShowCardsInVideo, " Thẻ tiêu đề / kết quả vòng");
            bool board = GUILayout.Toggle(s.ShowScoreboardInVideo, " Bảng điểm (ScoreboardOverlay — chỉ dữ kiện thật)");
            bool audio = GUILayout.Toggle(s.EnableAudio, " Âm thanh động cơ/va chạm tổng hợp");
            if (cards != s.ShowCardsInVideo || board != s.ShowScoreboardInVideo || audio != s.EnableAudio)
            {
                s.ShowCardsInVideo = cards; s.ShowScoreboardInVideo = board; s.EnableAudio = audio;
                app.Overlay.CardsEnabled = cards; app.Overlay.ScoreboardEnabled = board;
                VehicleAudio.MasterVolume = audio ? 0.8f : 0f;
                app.SaveSettings();
            }
            GUILayout.Label("Chất lượng đồ họa", h2);
            GUILayout.BeginHorizontal();
            foreach (var q in new[] { "Low", "Medium", "High" })
            {
                if (GUILayout.Button((s.Quality == q ? "● " : "") + q)) { s.Quality = q; app.ApplyQuality(q); app.SaveSettings(); }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"FPS hiện tại ≈ {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0}. Mục tiêu 60 fps chỉ là mục tiêu — hãy đo bằng Profiler trên máy của bạn.", small);
        }

        // ================================================================ 3. Presets
        void PresetTab()
        {
            var c = app.Config;
            GUILayout.Label("Preset (JSON)", h1);
            GUILayout.Label("Lưu tại: " + app.Presets.Folder, small);
            presetName = Field("scenarioId", presetName);
            c.Name = Field("Tên hiển thị", c.Name);
            GUILayout.BeginHorizontal();
            GUI.enabled = !app.Director.IsBusy;
            if (GUILayout.Button("Save Preset", GUILayout.Height(28))) SavePreset();
            if (GUILayout.Button("Duplicate", GUILayout.Height(28)))
            {
                app.Config = c.Clone();
                app.Config.ScenarioId = ScenarioConfig.SanitizeId(c.ScenarioId + "_copy");
                presetName = app.Config.ScenarioId;
                SavePreset();
            }
            if (GUILayout.Button("Randomize", GUILayout.Height(28))) { app.RandomizeConfig(); presetName = app.Config.ScenarioId; }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Preset đã lưu", h2);
            foreach (var id in app.Presets.List())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(id, label, GUILayout.Width(250));
                GUI.enabled = !app.Director.IsBusy;
                if (GUILayout.Button("Load", GUILayout.Width(60)))
                {
                    var loaded = app.Presets.Load(id, out var warnings, out string error);
                    if (loaded == null) app.Msg(error);
                    else
                    {
                        app.Config = loaded;
                        presetName = loaded.ScenarioId;
                        foreach (var w in warnings) app.Msg("Preset: " + w);
                        app.ApplyConfigToScene();
                        app.Msg("Loaded preset " + id);
                    }
                }
                if (GUILayout.Button("Xoá", GUILayout.Width(50))) { app.Presets.Delete(id); app.Msg("Deleted " + id); }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        // ================================================================ 4. Concept Lab
        void ConceptTab()
        {
            var c = app.Config;
            GUILayout.Label("Concept Lab — ý tưởng trước khi quay", h1);
            GUILayout.Label("Bắt đầu từ lời hứa với người xem. Concept Score là heuristic nội bộ, KHÔNG phải dự báo lượt xem/CTR.", small);
            GUILayout.Label("Series", h2);
            foreach (var id in ConceptGenerator.SeriesIds)
            {
                if (GUILayout.Toggle(c.SeriesId == id, " " + ConceptGenerator.SeriesLabel(id)) && c.SeriesId != id) { c.SeriesId = id; app.Concepts.Clear(); app.Plan = null; }
            }
            foreach (var id in ConceptGenerator.PlannedSeriesIds) GUILayout.Label("  ○ " + ConceptGenerator.SeriesLabel(id) + " — chưa có trong MVP", small);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ngôn ngữ nội dung", label, GUILayout.Width(160));
            if (GUILayout.Toggle(c.Language == ContentLanguage.en, " EN", GUILayout.Width(60))) c.Language = ContentLanguage.en;
            if (GUILayout.Toggle(c.Language == ContentLanguage.vi, " VI", GUILayout.Width(60))) c.Language = ContentLanguage.vi;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Độ khó", label, GUILayout.Width(160));
            foreach (var d in new[] { "Easy", "Progressive", "Hard" })
                if (GUILayout.Toggle(c.Difficulty == d, " " + d, GUILayout.Width(110))) c.Difficulty = d;
            GUILayout.EndHorizontal();
            if (app.Settings.Language != c.Language) { app.Settings.Language = c.Language; app.SaveSettings(); }

            if (GUILayout.Button("Sinh 3 concept", GUILayout.Height(30))) app.GenerateConcepts();
            for (int i = 0; i < app.Concepts.Count; i++)
            {
                var k = app.Concepts[i];
                GUILayout.BeginVertical(box);
                if (GUILayout.Toggle(app.SelectedConcept == i, $" Concept {i + 1} · twist: {k.TwistKind} · score {k.TotalScore:0.0}/5", h2) && app.SelectedConcept != i)
                {
                    app.SelectedConcept = i;
                    app.Plan = null;
                }
                GUILayout.Label("Câu hỏi: " + k.ViewerQuestion, wrap);
                GUILayout.Label("Lời hứa: " + k.ViewerPromise, small);
                GUILayout.Label($"Xung đột: {k.CentralConflict} · Mức cược: {k.Stakes}", small);
                GUILayout.Label($"Hình ảnh chủ đạo: {k.KeyVisual} · Shot thumbnail dự kiến: {k.CandidateThumbnailShot}", small);
                GUILayout.Label("Bất ngờ có thể: " + k.UnexpectedPossibleOutcome, small);
                GUILayout.Label("Điểm: " + string.Join(" · ", k.Scores.Select(kv => $"{kv.Key} {kv.Value:0.0}")), small);
                if (app.SelectedConcept == i)
                {
                    k.ViewerQuestion = Field("viewerQuestion", k.ViewerQuestion, 380);
                    k.WorkingTitle = Field("Working title", k.WorkingTitle, 380);
                    GUILayout.Label("Working title sẽ được đối chiếu lại với kết quả thật sau khi quay.", small);
                }
                GUILayout.EndVertical();
            }
            if (app.SelectedConcept >= 0)
            {
                if (GUILayout.Button("Dùng concept này → Generate Run Plan", GUILayout.Height(30))) { app.GeneratePlan(); tab = 6; }
            }
        }

        // ================================================================ 5. 10-minute director
        void DirectorTab()
        {
            var d = app.Director;
            var s = app.Settings;
            GUILayout.Label("10-Minute Director", h1);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Thời lượng", label, GUILayout.Width(120));
            bool draft = GUILayout.Toggle(s.DraftDuration, " Draft 5:00 (chỉ để thử nghiệm)");
            if (draft != s.DraftDuration) { s.DraftDuration = draft; app.Plan = null; app.SaveSettings(); }
            GUILayout.EndHorizontal();
            GUILayout.Label(s.DraftDuration ? "Mục tiêu: 05:00 (draft — không dùng giao thành phẩm YouTube 10:00)" : "Mục tiêu: 10:00 (600 s, chấp nhận 598–602 s)", s.DraftDuration ? warn : good);
            GUILayout.Label($"Concept: {(app.SelectedConcept >= 0 && app.SelectedConcept < app.Concepts.Count ? app.Concepts[app.SelectedConcept].ViewerQuestion : "chưa chọn (tab Concept Lab)")}", wrap);
            GUILayout.Label($"Capture: {s.Backend} · {s.CaptureFps} fps · 1920×1080 · Clean Capture khi quay: {(app.Config.HideUi ? "bật" : "tắt")}", small);
            app.Config.HideUi = GUILayout.Toggle(app.Config.HideUi, " Tự chuyển Capture Mode khi bắt đầu quay");

            GUI.enabled = !d.IsBusy && app.SelectedConcept >= 0;
            if (GUILayout.Button("Generate Plan", GUILayout.Height(28))) app.GeneratePlan();
            GUI.enabled = true;

            var plan = app.Plan;
            if (plan != null)
            {
                GUILayout.Space(6);
                GUILayout.Label("Kế hoạch (có thể sửa vòng trước khi chạy)", h2);
                // difficulty chart
                var rounds = plan.ScoredRounds.ToList();
                var chart = GUILayoutUtility.GetRect(400, 70);
                Fill(chart, new Color(0, 0, 0, 0.3f));
                float bw = chart.width / Math.Max(1, rounds.Count);
                for (int i = 0; i < rounds.Count; i++)
                {
                    float h = rounds[i].KickerHeight / 30f * (chart.height - 16);
                    Fill(new Rect(chart.x + i * bw + 6, chart.yMax - h - 14, bw - 12, h), rounds[i].Role == RoundRole.Decisive ? new Color(1f, 0.35f, 0.2f) : new Color(1f, 0.75f, 0.2f));
                    GUI.Label(new Rect(chart.x + i * bw + 6, chart.yMax - 14, bw, 14), rounds[i].KickerHeight.ToString("0") + "m", small);
                }
                bool changed = false;
                foreach (var ph in plan.Phases)
                {
                    GUILayout.BeginVertical(box);
                    GUILayout.Label($"{DescriptionComposer.Timestamp(ph.PlannedStart)}–{DescriptionComposer.Timestamp(ph.PlannedEnd)}  {ph.Kind}: {ph.Label}", h2);
                    if ((ph.Kind == PhaseKind.Round || ph.Kind == PhaseKind.Final) && ph.Round != null)
                    {
                        var r = ph.Round;
                        GUILayout.Label("Thay đổi: " + r.ChangeSummary, small);
                        GUILayout.Label("Câu hỏi nhỏ: " + r.SubQuestion, small);
                        if (!d.IsBusy)
                        {
                            float kh = Slider("Độ cao dốc", r.KickerHeight, 2, 30, "0", " m");
                            float ka = Slider("Góc", r.KickerAngleDeg, 8, 40, "0", "°");
                            float sf = Slider("Tốc độ", r.SpeedFactor, 0.4f, 1.3f, "0.00", "×");
                            var ob = Cycle("Chướng ngại", r.Obstacles);
                            var ru = Cycle("Luật", r.Rule);
                            bool h2h = GUILayout.Toggle(r.HeadToHead, " Tất cả xe chạy cùng lúc");
                            if (Math.Abs(kh - r.KickerHeight) > 0.01f || Math.Abs(ka - r.KickerAngleDeg) > 0.01f || Math.Abs(sf - r.SpeedFactor) > 0.001f || ob != r.Obstacles || ru != r.Rule || h2h != r.HeadToHead)
                            {
                                r.KickerHeight = Mathf.Round(kh * 2) / 2; r.KickerAngleDeg = Mathf.Round(ka); r.SpeedFactor = sf; r.Obstacles = ob; r.Rule = ru; r.HeadToHead = h2h;
                                changed = true;
                            }
                        }
                    }
                    GUILayout.EndVertical();
                }
                if (changed)
                {
                    RunPlanner.RefreshRoundTexts(rounds, plan.Entrants, plan.Language);
                    foreach (var ph in plan.Phases) if (ph.Round != null && ph.Kind != PhaseKind.ColdOpen && ph.Kind != PhaseKind.Intro) ph.Label = ph.Round.Title;
                    var teaser = plan.Phases.FirstOrDefault(p => p.Kind == PhaseKind.ColdOpen);
                    if (teaser != null && rounds.Count >= 5)
                    {
                        var t = rounds[4].Clone(); t.Index = 0; t.Scored = false; t.HeadToHead = true; t.Title = "Teaser"; teaser.Round = t;
                    }
                }
            }

            GUILayout.Space(8);
            GUI.enabled = !d.IsBusy && plan != null;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUILayout.Button("Start & Capture", GUILayout.Height(40))) app.StartAndCapture();
            GUI.backgroundColor = prevBg;
            GUI.enabled = true;

            if (d.IsBusy || d.Run != null)
            {
                GUILayout.Space(6);
                GUILayout.Label($"Progress {DescriptionComposer.Timestamp(app.Clock.Time)} / {DescriptionComposer.Timestamp(d.Plan?.TargetDurationSeconds ?? 600)} · {d.State}", h2);
                GUILayout.Label($"Hiện tại: {d.CurrentLabel} · Tiếp theo: {d.NextLabel} · Lượt đã chạy: {d.AttemptsRun}", wrap);
                GUILayout.Label($"Recorder: {app.Capture.Info?.Status} — {app.Capture.LastMessage}", wrap);
                if (d.LastError != null) GUILayout.Label(d.LastError, bad);
                GUILayout.BeginHorizontal();
                GUI.enabled = d.State == ProjectState.Recording;
                if (GUILayout.Button(d.Paused ? "Resume" : "Pause (đánh dấu Interrupted)")) d.TogglePause();
                if (GUILayout.Button("Bỏ qua lượt (Manual Override)")) d.SkipCurrentAttempt();
                GUI.enabled = d.IsBusy;
                if (GUILayout.Button("Abort run")) d.RequestAbort();
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Label("Pause/Abort sẽ khiến video phải được xác minh lại — không thể là bản liền mạch 10:00.", small);
            }
        }

        // ================================================================ 6. Video capture
        void CaptureTab()
        {
            var s = app.Settings;
            GUILayout.Label("Video Capture", h1);
            GUILayout.Label("Ứng dụng KHÔNG tự báo 'video đã xuất' nếu chưa kiểm tra tệp thật (tồn tại + độ dài).", small);
            var before = s.Backend;
            GUILayout.Label("Phương thức ghi", h2);
            if (GUILayout.Toggle(s.Backend == CaptureBackend.None, " Không ghi (chỉ mô phỏng → Content Pack dạng nháp)")) s.Backend = CaptureBackend.None;
            if (GUILayout.Toggle(s.Backend == CaptureBackend.UnityRecorder, " Unity Recorder (chỉ trong Unity Editor) " + (CaptureCoordinator.UnityRecorderAvailable ? "✓ sẵn sàng" : "✗ không khả dụng ở đây"))) s.Backend = CaptureBackend.UnityRecorder;
            if (GUILayout.Toggle(s.Backend == CaptureBackend.ObsWebSocket, " OBS qua WebSocket (OBS 28+, tự Start/Stop đúng lúc)")) s.Backend = CaptureBackend.ObsWebSocket;
            if (GUILayout.Toggle(s.Backend == CaptureBackend.ObsManual, " OBS thủ công (bạn tự bấm ghi/dừng)")) s.Backend = CaptureBackend.ObsManual;
            GUILayout.BeginHorizontal();
            GUILayout.Label("FPS", label, GUILayout.Width(160));
            if (GUILayout.Toggle(s.CaptureFps == 30, " 30", GUILayout.Width(60))) s.CaptureFps = 30;
            if (GUILayout.Toggle(s.CaptureFps == 60, " 60 (mượt hơn — khuyên dùng; chỉ quay lâu hơn)", GUILayout.Width(200))) s.CaptureFps = 60;
            GUILayout.EndHorizontal();

            if (s.Backend == CaptureBackend.ObsWebSocket || s.Backend == CaptureBackend.ObsManual)
            {
                GUILayout.Space(6);
                GUILayout.Label("OBS", h2);
                if (s.Backend == CaptureBackend.ObsWebSocket)
                {
                    s.ObsHost = Field("Host", s.ObsHost);
                    if (int.TryParse(Field("Port", s.ObsPort.ToString()), out int port)) s.ObsPort = Mathf.Clamp(port, 1, 65535);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Mật khẩu", label, GUILayout.Width(160));
                    s.ObsPassword = GUILayout.PasswordField(s.ObsPassword ?? "", '*', GUILayout.Width(240));
                    GUILayout.EndHorizontal();
                    s.RememberObsPassword = GUILayout.Toggle(s.RememberObsPassword, " Nhớ mật khẩu (lưu dạng văn bản trong settings.json của máy này)");
                    GUI.enabled = !obsTesting;
                    if (GUILayout.Button("Kiểm tra kết nối OBS")) TestObs();
                    GUI.enabled = true;
                    if (!string.IsNullOrEmpty(obsTestResult)) GUILayout.Label(obsTestResult, obsTestResult.StartsWith("✓") ? good : bad);
                    GUILayout.Label("Bật trong OBS: Tools ▸ WebSocket Server Settings ▸ Enable. Định dạng ghi nên là MP4 hoặc MKV.", small);
                }
                s.ObsRecordingFolder = Field("Thư mục ghi của OBS", s.ObsRecordingFolder, 380);
                GUILayout.Label("Dùng để tự tìm tệp mới nhất sau khi dừng ghi (OBS ▸ Settings ▸ Output ▸ Recording Path).", small);
            }
            if (s.Backend != before || GUI.changed) app.SaveSettings();

            GUILayout.Space(10);
            GUILayout.Label("Xác minh video của run gần nhất", h2);
            var run = app.Director.Run;
            if (run == null) { GUILayout.Label("Chưa có run.", small); return; }
            var cap = run.Capture;
            Status(cap.Verified ? CheckLevel.Pass : cap.Status == CaptureStatus.CapturePending ? CheckLevel.Pending : CheckLevel.Fail, $"{cap.Status}: {cap.Message}");
            if (cap.Video != null)
                GUILayout.Label($"{cap.Video.Path}\n{cap.Video.Container} · {cap.Video.SizeBytes / 1048576.0:0.0} MB · duration {(cap.Video.DurationSeconds.HasValue ? cap.Video.DurationSeconds.Value.ToString("0.00") + " s" : "?")} · video {cap.Video.HasVideoTrack} · audio {cap.Video.HasAudioTrack} · fps {cap.Video.VideoFps}", small);
            verifyPath = Field("Đường dẫn tệp", verifyPath, 520);
            GUILayout.BeginHorizontal();
            GUI.enabled = !app.Director.IsBusy;
            if (GUILayout.Button("Tìm video mới nhất trong thư mục OBS"))
            {
                var found = VideoFileInspector.FindNewestVideo(s.ObsRecordingFolder, app.Capture.RecordingStartedUtc.AddMinutes(-2));
                if (found != null) verifyPath = found; else app.Msg("Không tìm thấy video mới trong: " + s.ObsRecordingFolder);
            }
            if (GUILayout.Button("Verify")) app.Director.VerifyVideo(verifyPath.Trim().Trim('"'));
            if (cap.Status == CaptureStatus.Interrupted && GUILayout.Button("Tôi đã xem lại — chấp nhận take bị gián đoạn"))
            {
                app.Capture.AcceptInterruptedTake();
                run.ManualOverrides.Add("operator accepted interrupted take after review");
                app.Director.RegenerateContent();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!app.Director.IsBusy && cap.Status != CaptureStatus.CaptureVerified)
                GUILayout.Label("Gợi ý: nếu OBS ghi MKV chưa hoàn tất, dùng File ▸ Remux Recordings sang MP4 rồi Verify lại.", small);
        }

        async void TestObs()
        {
            obsTesting = true;
            obsTestResult = "Đang kết nối…";
            try { obsTestResult = await app.Capture.TestObsAsync(); }
            catch (Exception e) { obsTestResult = "✗ " + e.Message; }
            obsTesting = false;
        }
    }
}
