using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Grabs REAL rendered frames at highlight moments (apex, rollover) so thumbnail prompts can
    /// reference true gameplay (spec §23.1, §24.1). Textures are kept in memory during the take and
    /// encoded to PNG only after recording, to avoid hitches in the video.
    /// </summary>
    public sealed class HighlightFrameCapture : MonoBehaviour
    {
        public int MaxFrames = 10;

        sealed class Grab { public string AttemptId; public int EventId; public float Score; public Texture2D Tex; }
        readonly List<Grab> grabs = new List<Grab>();
        public readonly Dictionary<string, string> FileByAttempt = new Dictionary<string, string>();

        public void Clear()
        {
            foreach (var g in grabs) if (g.Tex != null) Destroy(g.Tex);
            grabs.Clear();
            FileByAttempt.Clear();
        }

        public void Request(string attemptId, int eventId, float score)
        {
            if (grabs.Count >= MaxFrames && grabs.Min(g => g.Score) >= score) return;
            StartCoroutine(GrabRoutine(attemptId, eventId, score));
        }

        IEnumerator GrabRoutine(string attemptId, int eventId, float score)
        {
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (System.Exception e) { Debug.LogWarning("[MYLTI] Frame capture failed: " + e.Message); }
            if (tex == null) yield break;
            // keep only the best grab per attempt
            var existing = grabs.FirstOrDefault(g => g.AttemptId == attemptId);
            if (existing != null)
            {
                if (existing.Score >= score) { Destroy(tex); yield break; }
                Destroy(existing.Tex);
                grabs.Remove(existing);
            }
            grabs.Add(new Grab { AttemptId = attemptId, EventId = eventId, Score = score, Tex = tex });
            if (grabs.Count > MaxFrames)
            {
                var worst = grabs.OrderBy(g => g.Score).First();
                Destroy(worst.Tex);
                grabs.Remove(worst);
            }
        }

        /// <summary>Encodes grabbed frames into &lt;runFolder&gt;/frames. Returns number of files written.</summary>
        public int SaveAll(string runFolder)
        {
            string dir = Path.Combine(runFolder, "frames");
            Directory.CreateDirectory(dir);
            int n = 0;
            foreach (var g in grabs)
            {
                if (g.Tex == null) continue;
                string name = $"frame_ev{g.EventId:0000}.png";
                try
                {
                    File.WriteAllBytes(Path.Combine(dir, name), g.Tex.EncodeToPNG());
                    FileByAttempt[g.AttemptId] = "frames/" + name;
                    n++;
                }
                catch (IOException e) { Debug.LogWarning("[MYLTI] Could not save frame: " + e.Message); }
            }
            return n;
        }
    }
}
