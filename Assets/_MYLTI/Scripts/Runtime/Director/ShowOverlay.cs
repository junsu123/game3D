using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// In-video graphics that are PART of the show (title cards, lower thirds, scoreboard, replay badge).
    /// They only display real game state. Creator UI/HUD is separate and hidden in Capture Mode.
    /// Layout is authored for 1920×1080 and scaled to the screen.
    /// </summary>
    public sealed class ShowOverlay : MonoBehaviour
    {
        public sealed class ScoreRow { public string Name; public Color Color; public int Points; public string Extra; }

        public bool CardsEnabled = true;
        public bool ScoreboardEnabled = true;
        public Func<double> Now = () => Time.time;

        string cardTitle, cardSubtitle, cardBody;
        Color cardAccent = new Color(1f, 0.75f, 0.1f);
        double cardShownAt = -1;
        string ltTitle, ltDetail;
        double ltUntil = -1, ltShownAt;
        string replayText;
        string scoreHeader;
        List<ScoreRow> scoreRows = new List<ScoreRow>();
        List<ScoreRow> standings;
        string standingsTitle;

        GUIStyle big, mid, small, tiny;
        Texture2D white;

        public void ShowCard(string title, string subtitle, string body, Color? accent = null)
        {
            cardTitle = title; cardSubtitle = subtitle; cardBody = body;
            cardAccent = accent ?? new Color(1f, 0.75f, 0.1f);
            cardShownAt = Now();
        }

        public void HideCard() { cardTitle = null; }
        public void LowerThird(string title, string detail, double seconds) { ltTitle = title; ltDetail = detail; ltShownAt = Now(); ltUntil = ltShownAt + seconds; }
        public void ClearLowerThird() { ltUntil = -1; }
        public void SetReplay(string text) { replayText = text; }
        public void SetScoreboard(string header, List<ScoreRow> rows) { scoreHeader = header; scoreRows = rows ?? new List<ScoreRow>(); }
        public void ShowStandings(string title, List<ScoreRow> rows) { standingsTitle = title; standings = rows; }
        public void HideStandings() { standings = null; }
        public void ClearAll() { HideCard(); ClearLowerThird(); replayText = null; standings = null; scoreHeader = null; scoreRows.Clear(); }

        void EnsureStyles()
        {
            if (big != null) return;
            white = Texture2D.whiteTexture;
            big = new GUIStyle(GUI.skin.label) { fontSize = 76, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            mid = new GUIStyle(big) { fontSize = 40, fontStyle = FontStyle.Bold };
            small = new GUIStyle(big) { fontSize = 30, fontStyle = FontStyle.Normal };
            tiny = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            foreach (var s in new[] { big, mid, small, tiny }) s.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            EnsureStyles();
            GUI.depth = 10; // below the creator UI
            float scale = Screen.height / 1080f;
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float W = Screen.width / scale, H = 1080f;
            double now = Now();

            if (ScoreboardEnabled && scoreHeader != null && scoreRows.Count > 0 && standings == null)
            {
                float rowH = 40f, w = 360f;
                var r = new Rect(W - w - 30f, 30f, w, 50f + rowH * scoreRows.Count);
                Fill(r, new Color(0, 0, 0, 0.55f));
                Fill(new Rect(r.x, r.y, r.width, 6f), cardAccent);
                GUI.Label(new Rect(r.x + 16f, r.y + 8f, w - 20f, 40f), scoreHeader, tiny);
                for (int i = 0; i < scoreRows.Count; i++)
                {
                    var row = scoreRows[i];
                    float y = r.y + 48f + i * rowH;
                    Fill(new Rect(r.x + 16f, y + 8f, 14f, 24f), row.Color);
                    GUI.Label(new Rect(r.x + 40f, y, w - 120f, rowH), row.Name, tiny);
                    GUI.Label(new Rect(r.x + w - 90f, y, 80f, rowH), row.Points + " pt", tiny);
                }
            }

            if (replayText != null)
            {
                var r = new Rect(30f, 30f, 520f, 64f);
                Fill(r, new Color(0.8f, 0.05f, 0.1f, 0.85f));
                GUI.Label(new Rect(r.x + 18f, r.y, r.width - 20f, r.height), "REPLAY  ·  " + replayText, tiny);
            }

            if (CardsEnabled && ltUntil > now && ltTitle != null)
            {
                float a = Mathf.Clamp01((float)(now - ltShownAt) * 4f);
                var r = new Rect(60f - (1f - a) * 80f, H - 220f, 820f, 130f);
                Fill(r, new Color(0, 0, 0, 0.6f * a));
                Fill(new Rect(r.x, r.y, 10f, r.height), cardAccent);
                var t = new GUIStyle(mid) { alignment = TextAnchor.MiddleLeft };
                var d = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft };
                GUI.Label(new Rect(r.x + 30f, r.y + 8f, r.width - 40f, 60f), ltTitle, t);
                if (!string.IsNullOrEmpty(ltDetail)) GUI.Label(new Rect(r.x + 30f, r.y + 66f, r.width - 40f, 54f), ltDetail, d);
            }

            if (CardsEnabled && cardTitle != null)
            {
                float a = Mathf.Clamp01((float)(now - cardShownAt) * 3f);
                var band = new Rect(0, H * 0.5f - 230f, W, 460f);
                Fill(band, new Color(0.02f, 0.02f, 0.05f, 0.72f * a));
                Fill(new Rect(0, band.y, W, 8f), cardAccent);
                Fill(new Rect(0, band.yMax - 8f, W, 8f), cardAccent);
                GUI.color = new Color(1, 1, 1, a);
                GUI.Label(new Rect(80f, band.y + 30f, W - 160f, 120f), cardTitle, big);
                if (!string.IsNullOrEmpty(cardSubtitle)) GUI.Label(new Rect(80f, band.y + 150f, W - 160f, 70f), cardSubtitle, mid);
                if (!string.IsNullOrEmpty(cardBody)) GUI.Label(new Rect(160f, band.y + 225f, W - 320f, 210f), cardBody, small);
                GUI.color = Color.white;
            }

            if (standings != null)
            {
                float w = 1000f, rowH = 90f;
                var r = new Rect((W - w) * 0.5f, 170f, w, 150f + rowH * standings.Count);
                Fill(r, new Color(0.02f, 0.02f, 0.05f, 0.78f));
                Fill(new Rect(r.x, r.y, w, 10f), cardAccent);
                GUI.Label(new Rect(r.x, r.y + 20f, w, 100f), standingsTitle, mid);
                for (int i = 0; i < standings.Count; i++)
                {
                    var row = standings[i];
                    float y = r.y + 130f + i * rowH;
                    Fill(new Rect(r.x + 40f, y + 15f, 24f, 60f), row.Color);
                    var nameStyle = new GUIStyle(mid) { alignment = TextAnchor.MiddleLeft };
                    GUI.Label(new Rect(r.x + 90f, y, 520f, rowH), (i + 1) + ". " + row.Name, nameStyle);
                    var pts = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
                    GUI.Label(new Rect(r.x + 520f, y, w - 560f, rowH), row.Points + " pt   " + row.Extra, pts);
                }
            }
            GUI.matrix = old;
        }

        void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = prev;
        }
    }
}
