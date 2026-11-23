using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Creator UI (IMGUI — no extra packages, works in Editor and Windows build).
    /// Creator Mode: full panels. Capture Mode (F1): nothing but the 3D world and in-video graphics.
    /// Labels are Vietnamese; internal codes stay English (spec §7).
    /// </summary>
    public sealed partial class StudioUI : MonoBehaviour
    {
        StudioApp app;
        int tab;
        Vector2 leftScroll, rightScroll, packScroll;
        string presetName = "";
        string verifyPath = "";
        string obsTestResult = "";
        bool obsTesting;
        string lastExportFolder;
        GUIStyle h1, h2, label, wrap, small, box, bad, good, warn, mono;
        Texture2D white;
        float scale = 1f;

        static readonly string[] Tabs =
        {
            "▶ Auto Episode", "Thử thách", "Xe", "Camera", "Preset", "Concept Lab", "10-Min Director", "Video Capture", "YouTube Pack", "Quality", "📅 Lịch & Đăng"
        };

        void Start()
        {
            app = StudioApp.Instance;
            presetName = app.Config.ScenarioId;
        }

        void Styles()
        {
            if (h1 != null) return;
            white = Texture2D.whiteTexture;
            h1 = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            h2 = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            wrap = new GUIStyle(label) { wordWrap = true };
            small = new GUIStyle(wrap) { fontSize = 11 };
            mono = new GUIStyle(small) { fontSize = 11, richText = false };
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 6, 6) };
            good = new GUIStyle(wrap); good.normal.textColor = new Color(0.45f, 0.95f, 0.5f);
            warn = new GUIStyle(wrap); warn.normal.textColor = new Color(1f, 0.8f, 0.3f);
            bad = new GUIStyle(wrap); bad.normal.textColor = new Color(1f, 0.45f, 0.45f);
        }

        void OnGUI()
        {
            if (app == null) return;
            Styles();
            GUI.depth = 0;
            if (app.Mode == UiMode.Capture) return; // Clean Capture: no HUD, no panels, no text
            scale = Mathf.Clamp(Screen.height / 1000f, 0.8f, 2f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float W = Screen.width / scale, H = Screen.height / scale;

            DrawTopBar(W);
            if (app.HudVisible) DrawHud(W, H);
            bool wide = tab == 0 || tab == 8 || tab == 9 || tab == 10;
            float leftW = wide ? Mathf.Min(W - 40f, 980f) : 440f;
            var left = new Rect(10, 46, leftW, H - 150);
            Fill(left, new Color(0.06f, 0.07f, 0.09f, 0.88f));
            GUILayout.BeginArea(new Rect(left.x + 8, left.y + 6, left.width - 16, left.height - 12));
            leftScroll = GUILayout.BeginScrollView(leftScroll);
            try
            {
                switch (tab)
                {
                    case 0: AutoTab(); break;
                    case 1: ChallengeTab(); break;
                    case 2: VehicleTab(); break;
                    case 3: CameraTab(); break;
                    case 4: PresetTab(); break;
                    case 5: ConceptTab(); break;
                    case 6: DirectorTab(); break;
                    case 7: CaptureTab(); break;
                    case 8: PackTab(); break;
                    case 9: QualityTab(); break;
                    case 10: PublishTab(); break;
                }
            }
            catch (Exception e) when (!(e is ExitGUIException))
            {
                GUILayout.Label("UI error: " + e.Message, bad);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (!wide) DrawRightPanel(W, H);
            DrawBottomBar(W, H);
        }

        // ------------------------------------------------------------------ frame
        void DrawTopBar(float W)
        {
            Fill(new Rect(0, 0, W, 40), new Color(0.03f, 0.03f, 0.05f, 0.92f));
            GUI.Label(new Rect(12, 8, 260, 28), "MYLTI Challenge Studio", h1);
            float x = 280;
            for (int i = 0; i < Tabs.Length; i++)
            {
                float w = Tabs[i].Length * 8.2f + 22f;
                var prev = GUI.backgroundColor;
                if (i == tab) GUI.backgroundColor = new Color(1f, 0.75f, 0.2f);
                if (GUI.Button(new Rect(x, 7, w, 26), Tabs[i])) { tab = i; leftScroll = Vector2.zero; }
                GUI.backgroundColor = prev;
                x += w + 4;
            }
            GUI.Label(new Rect(W - 230, 10, 220, 24), "F1: Creator/Capture · H: HUD", small);
        }

        void DrawHud(float W, float H)
        {
            var d = app.Director;
            var vc = app.Session.ActiveVehicles.FirstOrDefault() ?? app.Session.Vehicles.Values.FirstOrDefault();
            string speed = vc != null ? $"{vc.SpeedKmh:0} km/h" : "—";
            string clock = d.IsBusy ? $"{DescriptionComposer.Timestamp(app.Clock.Time)} / {DescriptionComposer.Timestamp(d.Plan?.TargetDurationSeconds ?? 600)}" : "—";
            var r = new Rect(W - 400, 46, 390, 62);
            Fill(r, new Color(0, 0, 0, 0.6f));
            GUI.Label(new Rect(r.x + 10, r.y + 4, 380, 24), $"{d.State}  ·  {speed}  ·  {app.Cam.CurrentShotName}", h2);
            GUI.Label(new Rect(r.x + 10, r.y + 32, 380, 24), $"Program {clock}   Media: {MediaText()}", label);
        }

        string MediaText()
        {
            var c = app.Director.Run?.Capture;
            if (c == null) return "—";
            if (c.Verified) return $"verified {c.Video?.DurationSeconds:0.0}s";
            return c.Status.ToString();
        }

        void DrawRightPanel(float W, float H)
        {
            var r = new Rect(W - 400, 114, 390, H - 218);
            Fill(r, new Color(0.06f, 0.07f, 0.09f, 0.85f));
            GUILayout.BeginArea(new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 12));
            rightScroll = GUILayout.BeginScrollView(rightScroll);
            GUILayout.Label("Event timeline", h2);
            var lines = app.Log.RecentLines;
            for (int i = Math.Max(0, lines.Count - 18); i < lines.Count; i++) GUILayout.Label(lines[i], mono);
            GUILayout.Space(8);
            GUILayout.Label("Thông báo", h2);
            for (int i = Math.Max(0, app.Messages.Count - 10); i < app.Messages.Count; i++) GUILayout.Label(app.Messages[i], small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawBottomBar(float W, float H)
        {
            var r = new Rect(0, H - 96, W, 96);
            Fill(r, new Color(0.03f, 0.03f, 0.05f, 0.92f));
            GUILayout.BeginArea(new Rect(10, H - 90, W - 20, 84));
            GUILayout.BeginHorizontal();
            bool busy = app.Director.IsBusy;
            GUI.enabled = !busy;
            if (GUILayout.Button("Start (sandbox)", GUILayout.Height(32), GUILayout.Width(130))) app.StartSandbox();
            if (GUILayout.Button("Reset", GUILayout.Height(32), GUILayout.Width(70))) app.ResetSandbox();
            GUI.enabled = app.Director.State == ProjectState.Recording;
            if (GUILayout.Button(app.Director.Paused ? "Resume" : "Pause", GUILayout.Height(32), GUILayout.Width(80))) app.Director.TogglePause();
            GUI.enabled = true;
            if (GUILayout.Button("Camera: " + app.Cam.Requested, GUILayout.Height(32), GUILayout.Width(170)))
                app.Cam.Requested = app.Cam.Requested == Shot.Free ? Shot.Auto : (Shot)(((int)app.Cam.Requested + 1) % 9);
            if (GUILayout.Button(app.HudVisible ? "HUD On" : "HUD Off", GUILayout.Height(32), GUILayout.Width(80))) app.HudVisible = !app.HudVisible;
            GUI.enabled = !busy;
            if (GUILayout.Button("Save preset", GUILayout.Height(32), GUILayout.Width(100))) SavePreset();
            GUI.enabled = true;
            if (GUILayout.Button("Capture Mode (F1)", GUILayout.Height(32), GUILayout.Width(150))) app.Mode = UiMode.Capture;
            GUILayout.EndHorizontal();
            var d = app.Director;
            string status = $"Trạng thái: {d.State}   ·   Vòng: {d.CurrentLabel} → {d.NextLabel}   ·   Lượt đã chạy: {d.AttemptsRun}   ·   Recorder: {app.Capture.Info?.Status} ({app.Settings.Backend})";
            GUILayout.Label(status, small);
            if (app.Messages.Count > 0) GUILayout.Label(app.Messages[app.Messages.Count - 1], small);
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ widgets
        void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = prev;
        }

        float Slider(string name, float value, float min, float max, string fmt = "0.0", string unit = "")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(160));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(170));
            GUILayout.Label(v.ToString(fmt, CultureInfo.InvariantCulture) + unit, label, GUILayout.Width(70));
            GUILayout.EndHorizontal();
            return v;
        }

        T Cycle<T>(string name, T value) where T : struct, Enum
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(160));
            if (GUILayout.Button(value.ToString(), GUILayout.Width(240)))
            {
                var vals = (T[])Enum.GetValues(typeof(T));
                int i = Array.IndexOf(vals, value);
                value = vals[(i + 1) % vals.Length];
            }
            GUILayout.EndHorizontal();
            return value;
        }

        string Field(string name, string value, float width = 240)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(160));
            value = GUILayout.TextField(value ?? "", GUILayout.Width(width));
            GUILayout.EndHorizontal();
            return value;
        }

        void Status(CheckLevel l, string text)
        {
            GUILayout.Label((l == CheckLevel.Pass ? "✓ " : l == CheckLevel.Fail ? "✗ " : l == CheckLevel.Warn ? "! " : "… ") + text,
                l == CheckLevel.Pass ? good : l == CheckLevel.Fail ? bad : warn);
        }

        static void Copy(string text) { GUIUtility.systemCopyBuffer = text ?? ""; }

        void SavePreset()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(presetName)) app.Config.ScenarioId = presetName;
                string path = app.Presets.Save(app.Config);
                presetName = app.Config.ScenarioId;
                app.Msg("Preset saved: " + path);
            }
            catch (Exception e) { app.Msg("Save failed: " + e.Message); }
        }

        static void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Application.OpenURL("file:///" + path.Replace('\\', '/'));
        }
    }
}
