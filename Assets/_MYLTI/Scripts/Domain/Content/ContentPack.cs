using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Mylti.Domain
{
    /// <summary>YouTube Content Pack (spec §18.2 + §24.1): 5 titles, 1 SEO description, 3 title/thumbnail pairs.</summary>
    public sealed class ContentPack : IJsonWritable
    {
        public string RunId;
        public string Generator = "MYLTI Offline Template Engine v1 (rule-based templates, not AI)";
        public string GeneratedAtIso;
        public bool IsDraft;                  // true when the video is not CaptureVerified
        public string DraftReason;
        public List<TitleOption> Titles = new List<TitleOption>();
        public string Description;
        public List<TitleThumbnailPair> Pairs = new List<TitleThumbnailPair>();
        public List<ClaimIssue> Issues = new List<ClaimIssue>();
        public string WorkingTitle;
        public string WorkingTitleCheck;
        public string WorkingTitleSuggestion;
        public ContentFactSheet Facts;

        /// <summary>Main prompt + 2 variants (spec §18.2.3) = the three pair prompts.</summary>
        public IEnumerable<string> ThumbnailPrompts => Pairs.Select(p => p.ThumbnailPrompt);

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("runId", RunId).Prop("generator", Generator).Prop("generatedAt", GeneratedAtIso)
                .Prop("isDraft", IsDraft).Prop("draftReason", DraftReason);
            w.Key("titles"); w.Value(Titles);
            w.Prop("description", Description);
            w.Key("titleThumbnailPairs"); w.Value(Pairs);
            w.Key("workingTitleCheck").BeginObject().Prop("original", WorkingTitle).Prop("status", WorkingTitleCheck)
                .Prop("honestRewrite", WorkingTitleSuggestion).EndObject();
            w.Key("truthIssues").BeginArray();
            foreach (var i in Issues) w.BeginObject().Prop("item", i.Item).Prop("level", i.Level.ToString()).Prop("message", i.Message).EndObject();
            w.EndArray();
            w.Prop("note", "Suggestions for the channel owner to review in YouTube Studio. No A/B test has been run; no view/CTR outcome is implied.");
            w.EndObject();
        }
    }

    /// <summary>Tier-2 hook (optional LLM). The offline engine is always the fallback.</summary>
    public interface IContentGenerator
    {
        string Name { get; }
        ContentPack Generate(RunRecord run, ContentFactSheet facts);
    }

    public sealed class OfflineContentGenerator : IContentGenerator
    {
        public string Name => "OfflineTemplateEngine";

        public ContentPack Generate(RunRecord run, ContentFactSheet fs)
        {
            bool vi = fs.Language == ContentLanguage.vi;
            var pack = new ContentPack
            {
                RunId = run.RunId,
                GeneratedAtIso = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                Facts = fs,
                IsDraft = !run.Capture.Verified,
                DraftReason = run.Capture.Verified ? null : $"Video not verified (capture status: {run.Capture.Status}). Metadata is a draft."
            };
            pack.Titles = TitleGenerator.Generate(fs);
            pack.Description = DescriptionComposer.Compose(fs, run.Plan);
            pack.Issues.AddRange(ClaimChecker.Check("description", pack.Description, fs, isTitle: false));

            // Three distinct title/thumbnail angles.
            var hl = run.Highlights.OrderByDescending(h => h.Score).ToList();
            Highlight pick(Func<Highlight, bool> pred) => hl.FirstOrDefault(pred) ?? hl.FirstOrDefault();
            var specs = run.Plan?.Phases.Where(p => p.Round != null).Select(p => p.Round).ToList() ?? new List<RoundSpec>();
            RoundSpec specOf(Highlight h) => h == null ? null : specs.FirstOrDefault(r => r.Index == h.RoundIndex);
            AttemptRecord attemptOf(Highlight h) => h == null ? null : run.Attempts.FirstOrDefault(a => a.AttemptId == h.AttemptId);

            var used = new HashSet<string>();
            Highlight pickDistinct(Func<Highlight, bool> pred)
            {
                var h = hl.FirstOrDefault(x => pred(x) && !used.Contains(x.HighlightId))
                     ?? hl.FirstOrDefault(x => !used.Contains(x.HighlightId))
                     ?? hl.FirstOrDefault();
                if (h != null) used.Add(h.HighlightId);
                return h;
            }
            double maxH = fs.MaxKickerHeightM ?? 0;
            var hA = pickDistinct(h => specOf(h)?.HeadToHead == true && attemptOf(h)?.ValidLanding == true);
            // B: the most dramatic real moment on the tallest kicker (rollover first, then biggest jump)
            var hB = hl.Where(h => !used.Contains(h.HighlightId) && Math.Abs((specOf(h)?.KickerHeight ?? -1) - maxH) < 0.6)
                       .OrderByDescending(h => attemptOf(h)?.Outcome == AttemptOutcome.Rollover ? 1 : 0).ThenByDescending(h => h.Score).FirstOrDefault()
                     ?? pickDistinct(h => attemptOf(h)?.Outcome == AttemptOutcome.Rollover);
            if (hB != null) used.Add(hB.HighlightId);
            var hC = pickDistinct(h => { var s = specOf(h); return s != null && (s.Obstacles != ObstacleSet.None || s.Rule != ScoringRule.FurthestValidLanding || s.BridgeWidth > 0); });
            var titleBy = pack.Titles.ToDictionary(t => t.Category, t => t.Text);
            string H = fs.MaxKickerHeightM.HasValue ? Math.Floor(fs.MaxKickerHeightM.Value).ToString(CultureInfo.InvariantCulture) : null;

            pack.Pairs.Add(MakePair(fs, run, "A", vi ? "Đối đầu / tương phản" : "Versus / contrast",
                titleBy.TryGetValue("Versus", out var tA) ? tA : pack.Titles[0].Text, "mega ramp " + (fs.Vehicles.Count > 1 ? "vehicle versus" : "jump"),
                fs.Vehicles.Count > 1 ? (vi ? "AI THẮNG?" : "WHO WINS?") : "", hA,
                "wide side-on view that shows both vehicles and the size difference between them"));
            // B's title must describe the same vehicle/moment as its thumbnail.
            var aB = attemptOf(hB);
            var bSpec = specOf(hB);
            var heavyId = fs.Vehicles.OrderByDescending(v => VehicleCatalog.Find(v.VehicleId)?.MassKg ?? 0).First().VehicleId;
            string bTitle;
            if (aB != null && aB.VehicleId == fs.LongestJumpVehicleId && aB.RoundIndex == fs.LongestJumpRound && aB.ValidLanding)
                bTitle = titleBy["ExtremeChallenge"];                       // thumbnail shows exactly the record jump
            else if (aB != null && aB.VehicleId == heavyId)
                bTitle = titleBy["Curiosity"];                              // question about the heavy vehicle it shows
            else
            {
                string bh = bSpec != null ? Math.Floor(bSpec.KickerHeight).ToString(CultureInfo.InvariantCulture) : H;
                bTitle = vi ? $"Xe Nào Trụ Được Trên Mega Ramp {bh}m?" : $"Who Can Handle the {bh}m Mega Ramp?";
            }
            string bText = bSpec != null ? (vi ? $"{Math.Floor(bSpec.KickerHeight)}M?!" : $"{Math.Floor(bSpec.KickerHeight)}M RAMP") : "";
            pack.Pairs.Add(MakePair(fs, run, "B", vi ? "Mức cược cao / tò mò kết quả" : "High stakes / result curiosity",
                bTitle, "mega ramp challenge", bText, hB,
                "dramatic low angle from below the ramp lip looking up at the vehicle against the sky"));
            pack.Pairs.Add(MakePair(fs, run, "C", vi ? "Từ khóa tìm kiếm + điểm khác biệt" : "Search intent + unique twist",
                titleBy["SearchFriendly"], "car jump challenge", "", hC,
                "three-quarter elevated view that shows the twist (obstacles, wall, target or bridge) clearly"));

            foreach (var p in pack.Pairs)
            {
                var issues = ClaimChecker.Check("pair" + p.PairId, p.Title + "\n" + p.ThumbnailText + "\n" + p.OneSentencePromise, fs, true);
                issues.AddRange(ClaimChecker.Check("prompt" + p.PairId, p.ThumbnailPrompt, fs, false));
                p.TruthCheckStatus = ClaimChecker.Status(issues);
                pack.Issues.AddRange(issues);
            }

            // Re-check the pre-run working title against what actually happened (spec §21.1).
            pack.WorkingTitle = fs.WorkingTitle;
            if (!string.IsNullOrEmpty(fs.WorkingTitle))
            {
                var wt = ClaimChecker.Check("workingTitle", fs.WorkingTitle, fs, true);
                pack.WorkingTitleCheck = ClaimChecker.Status(wt);
                pack.WorkingTitleSuggestion = wt.Any(i => i.Level == CheckLevel.Fail)
                    ? pack.Titles.First(t => t.Recommended).Text
                    : fs.WorkingTitle;
            }
            return pack;
        }

        static TitleThumbnailPair MakePair(ContentFactSheet fs, RunRecord run, string id, string angleLabel, string title,
            string keyword, string text, Highlight h, string cameraAngle)
        {
            bool vi = fs.Language == ContentLanguage.vi;
            string promise;
            var winner = fs.OverallWinnerId != null ? fs.V(fs.OverallWinnerId) : null;
            string versus = string.Join(" vs ", fs.Vehicles.Select(v => v.Name));
            switch (id)
            {
                case "A": promise = vi ? $"{versus}: cùng một Mega Ramp, cùng luật chơi." : $"{versus} on the same Mega Ramp with the same rules."; break;
                case "B": promise = vi ? "Dốc cao dần qua từng vòng — xem xe nào trụ được tới chung kết." : "The ramp gets taller every round — see which vehicle holds up to the final."; break;
                default: promise = vi ? "Thử thách Mega Ramp nhiều vòng với điểm khác biệt riêng ở mỗi vòng." : "A multi-round Mega Ramp challenge with a new twist in each round."; break;
            }
            return new TitleThumbnailPair
            {
                PairId = id,
                Angle = angleLabel,
                Title = title,
                PrimaryKeyword = keyword,
                OneSentencePromise = promise,
                ThumbnailFrameId = h?.HighlightId,
                ThumbnailFrameFile = h?.FrameFile,
                ThumbnailText = text,
                ThumbnailPrompt = ThumbnailPromptBuilder.Build(fs, run, h, cameraAngle, text)
            };
        }
    }

    /// <summary>Builds facts → content pack, and writes the export folder (spec §18.2.4, §24.1).</summary>
    public static class ContentPackService
    {
        public static ContentPack Generate(RunRecord run, IContentGenerator generator = null)
        {
            var fs = FactSheetBuilder.Build(run);
            var offline = new OfflineContentGenerator();
            if (generator == null || generator is OfflineContentGenerator) return offline.Generate(run, fs);
            try
            {
                var pack = generator.Generate(run, fs);
                // Every claim of an external generator is re-checked; any failure → offline fallback.
                var issues = new List<ClaimIssue>();
                foreach (var t in pack.Titles) issues.AddRange(ClaimChecker.Check("title", t.Text, fs, true));
                issues.AddRange(ClaimChecker.Check("description", pack.Description, fs, false));
                if (issues.Any(i => i.Level == CheckLevel.Fail)) return offline.Generate(run, fs);
                return pack;
            }
            catch (Exception)
            {
                return offline.Generate(run, fs);
            }
        }

        public static string UniqueRunFolder(string root, string runId)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, runId);
            int k = 2;
            while (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any(f => !f.EndsWith("frames") && !f.EndsWith("video")))
                path = Path.Combine(root, runId + "_" + (k++));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Writes every deliverable. Titles/description/prompts come from the (possibly user-edited) pack.</summary>
        public static List<string> Export(string folder, RunRecord run, ContentPack pack, QualityReport quality)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentException("folder");
            string full = Path.GetFullPath(folder);
            Directory.CreateDirectory(full);
            var enc = new UTF8Encoding(false);
            var written = new List<string>();
            void Write(string name, string content)
            {
                string p = Path.Combine(full, name);
                if (!Path.GetFullPath(p).StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new IOException("Path escapes export folder: " + name);
                File.WriteAllText(p, content, enc);
                written.Add(p);
            }

            var t = new StringBuilder();
            if (pack.IsDraft) t.Append("# DRAFT — ").Append(pack.DraftReason).Append("\n\n");
            foreach (var title in pack.Titles)
                t.Append(title.Recommended ? "[RECOMMENDED] " : "").Append('[').Append(title.Category).Append("] ").Append(title.Text)
                 .Append("  (").Append(title.Length).Append(" chars, truth: ").Append(title.TruthCheckStatus).Append(")\n");
            Write("youtube_titles.txt", t.ToString());
            Write("youtube_description.txt", pack.Description ?? "");

            var p2 = new StringBuilder();
            p2.Append("Prompts only — no thumbnail image has been generated by MYLTI.\n\n");
            foreach (var p in pack.Pairs)
            {
                p2.Append(p.PairId == "A" ? "MAIN PROMPT (pair A — " : $"VARIANT (pair {p.PairId} — ").Append(p.Angle).Append(")\n");
                p2.Append("Title: ").Append(p.Title).Append('\n');
                p2.Append("Thumbnail text: ").Append(string.IsNullOrEmpty(p.ThumbnailText) ? "(no text)" : p.ThumbnailText).Append('\n');
                p2.Append("Reference frame: ").Append(p.ThumbnailFrameFile ?? "(not captured — use a screenshot from the video)").Append('\n');
                p2.Append(p.ThumbnailPrompt).Append("\n\n");
            }
            Write("thumbnail_prompts.txt", p2.ToString());

            Write("content_pack.json", MiniJson.Serialize(pack));
            var yp = new Dictionary<string, object>
            {
                ["runId"] = run.RunId,
                ["isDraft"] = pack.IsDraft,
                ["titles"] = pack.Titles,
                ["description"] = pack.Description,
                ["pairs"] = pack.Pairs,
                ["note"] = "Use the three pairs for YouTube Studio 'Test & compare' if your channel has it. Nothing here predicts views."
            };
            Write("youtube_pack.json", MiniJson.Serialize(yp));
            Write("fact_sheet.json", MiniJson.Serialize(pack.Facts));
            Write("round_results.json", MiniJson.Serialize(run.Rounds));
            Write("events.json", MiniJson.Serialize(run.Events));
            Write("run_record.json", MiniJson.Serialize(run));
            if (quality != null) Write("quality_report.json", MiniJson.Serialize(quality));

            var csv = new StringBuilder("pair,angle,title,primary_keyword,thumbnail_text,frame_id,frame_file,truth_check,prompt\n");
            foreach (var p in pack.Pairs)
                csv.Append(Csv(p.PairId)).Append(',').Append(Csv(p.Angle)).Append(',').Append(Csv(p.Title)).Append(',').Append(Csv(p.PrimaryKeyword))
                   .Append(',').Append(Csv(p.ThumbnailText)).Append(',').Append(Csv(p.ThumbnailFrameId)).Append(',').Append(Csv(p.ThumbnailFrameFile))
                   .Append(',').Append(Csv(p.TruthCheckStatus)).Append(',').Append(Csv(p.ThumbnailPrompt)).Append('\n');
            Write("titles_and_thumbnail_variants.csv", csv.ToString());

            var edl = new StringBuilder("highlight_id,event_id,start,end,start_tc,end_tc,score,camera,reason,source_clip,frame_file\n");
            foreach (var h in run.Highlights.OrderBy(x => x.Start))
                edl.Append(Csv(h.HighlightId)).Append(',').Append(h.EventId).Append(',')
                   .Append(h.Start.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(h.End.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                   .Append(DescriptionComposer.Timestamp(h.Start)).Append(',').Append(DescriptionComposer.Timestamp(h.End)).Append(',')
                   .Append(h.Score.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(Csv(h.Camera)).Append(',')
                   .Append(Csv(h.Reason)).Append(',').Append(Csv(h.SourceClip)).Append(',').Append(Csv(h.FrameFile)).Append('\n');
            Write("highlights_edl.csv", edl.ToString());

            var shots = new StringBuilder("start,end,kind,label,camera,source_clip\n");
            foreach (var s in run.Segments)
                shots.Append(s.Start.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(s.End.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                     .Append(s.Kind).Append(',').Append(Csv(s.Label)).Append(',').Append(Csv(s.Camera)).Append(',').Append(Csv(s.SourceClip)).Append('\n');
            Write("shot_list.csv", shots.ToString());

            Write("asset_manifest.json", MiniJson.Serialize(new Dictionary<string, object>
            {
                ["vehicles"] = "Procedural primitive meshes generated by MYLTI (original, no third-party models)",
                ["audio"] = "Procedurally synthesised engine/impact sounds generated at runtime (original)",
                ["music"] = "None",
                ["fonts"] = "Unity built-in runtime font",
                ["trademarks"] = "None used — generic vehicle classes only"
            }));
            return written;
        }

        static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
