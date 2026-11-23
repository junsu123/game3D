using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    public enum StepState { Pending, Running, Done, Failed, Skipped }

    public sealed class ProductionStep
    {
        public string Name;
        public StepState State;
        public string Detail = "";
    }

    /// <summary>
    /// One-button pipeline: Claude (or offline) writes the episode → the game plays and records itself →
    /// Claude (or templates) writes English commentary from the REAL events → text-to-speech →
    /// offline sound design → final MP4 (video + voice) → content pack, captions, thumbnail.
    /// </summary>
    public sealed class EpisodeProducer : MonoBehaviour
    {
        public StudioApp App;
        public FfmpegTool Ffmpeg;
        public EpisodeLog Episodes;

        public bool Busy { get; private set; }
        public readonly List<ProductionStep> Steps = new List<ProductionStep>();
        public string FinalVideoPath { get; private set; }
        public string LastError { get; private set; }
        public NarrationScript Narration { get; private set; }
        string scriptSource = "offline";

        AppSettings S => App.Settings;

        ProductionStep Step(string name)
        {
            var s = Steps.FirstOrDefault(x => x.Name == name);
            if (s == null) { s = new ProductionStep { Name = name }; Steps.Add(s); }
            return s;
        }

        void Set(string name, StepState st, string detail = null)
        {
            var s = Step(name);
            s.State = st;
            if (detail != null) s.Detail = detail;
            if (st == StepState.Failed) { LastError = name + ": " + detail; App.Msg("✗ " + LastError); }
        }

        ClaudeClient Claude() => S.UseClaude ? new ClaudeClient(S.ClaudeApiKey, S.ClaudeModel) : null;

        // ================================================================ entry points
        public void StartAutoEpisode()
        {
            if (Busy || App.Director.IsBusy) { App.Msg("Already running."); return; }
            StartCoroutine(Guard(AutoEpisode()));
        }

        public void StartPostProduction()
        {
            if (Busy || App.Director.IsBusy) return;
            StartCoroutine(Guard(PostProductionOnly()));
        }

        public void DownloadFfmpeg()
        {
            if (Busy) return;
            StartCoroutine(Guard(EnsureFfmpeg()));
        }

        IEnumerator Guard(IEnumerator inner)
        {
            Busy = true;
            LastError = null;
            yield return StartCoroutine(inner);
            Busy = false;
        }

        IEnumerator PostProductionOnly()
        {
            Steps.Clear();
            yield return StartCoroutine(PostProduce());
        }

        IEnumerator AutoEpisode()
        {
            Steps.Clear();
            FinalVideoPath = null;
            foreach (var n in new[] { "1. FFmpeg", "2. Kịch bản (Claude)", "3. Chạy & tự quay 10:00", "4. Lời bình tiếng Anh", "5. Giọng đọc (TTS)", "6. Âm thanh", "7. Xuất MP4 cuối", "8. Tiêu đề, mô tả, phụ đề, thumbnail" })
                Step(n);

            yield return StartCoroutine(EnsureFfmpeg());
            if (!Ffmpeg.Available) yield break;

            // ---------------------------------------------------- 2. script
            int seed = Environment.TickCount & 0x7fffffff;
            Set("2. Kịch bản (Claude)", StepState.Running, "Đang viết kịch bản…");
            EpisodeScript script = null;
            var claude = Claude();
            if (claude != null && claude.HasKey)
            {
                var t = EpisodeWriter.WriteWithClaudeAsync(claude, Episodes.Items, App.History.Items, App.Config.Entrants, S.LetClaudePickVehicles, App.Config.Difficulty, seed, m => App.Msg(m));
                while (!t.IsCompleted) yield return null;
                script = t.IsFaulted ? null : t.Result;
                if (script == null) App.Msg("Claude không viết được kịch bản (" + claude.LastError + ") → dùng bộ viết offline.");
            }
            if (script == null)
                script = EpisodeWriter.WriteOffline(Episodes.Items, App.History.Items, App.Config.Entrants, S.LetClaudePickVehicles, App.Config.Difficulty, seed);
            scriptSource = script.Source;
            App.Config.Entrants = script.Entrants.Select(e => e.Clone()).ToList();
            App.Config.Language = ContentLanguage.en;
            App.Concepts = new List<ConceptBrief> { script.Concept };
            App.SelectedConcept = 0;
            App.Plan = RunPlanner.CreatePlan(script.Concept, script.Entrants, seed, S.DraftDuration ? 300 : 600, ContentLanguage.en, App.Config.Difficulty, out var warns, script.Rounds);
            foreach (var w in warns.Concat(script.Notes)) App.Msg("Script: " + w);
            Set("2. Kịch bản (Claude)", StepState.Done, $"{(script.Source == "offline" ? "Offline" : "Claude")}: “{script.Concept.ViewerQuestion}” · {string.Join(" vs ", script.Entrants.Select(e => e.Def.DisplayNameEn))}");

            // ---------------------------------------------------- 3. play + record
            Set("3. Chạy & tự quay 10:00", StepState.Running, "Đang quay… (F1 để xem bảng điều khiển; không xuất hiện trong video)");
            S.Backend = CaptureBackend.BuiltInFfmpeg;
            S.ShowCardsInVideo = false;
            S.ShowScoreboardInVideo = false;
            App.Recorder.FlipVertical = S.FlipVertical;
            App.Config.HideUi = true;
            if (!App.Director.Begin(App.Plan, out string err)) { Set("3. Chạy & tự quay 10:00", StepState.Failed, err); yield break; }
            while (App.Director.IsBusy)
            {
                Step("3. Chạy & tự quay 10:00").Detail = $"{DescriptionComposer.Timestamp(App.Clock.Time)} / {DescriptionComposer.Timestamp(App.Plan.TargetDurationSeconds)} · {App.Director.CurrentLabel} · {App.Recorder.FramesWritten} frames";
                yield return null;
            }
            if (App.Director.State != ProjectState.ContentReady)
            {
                Set("3. Chạy & tự quay 10:00", StepState.Failed, $"{App.Director.State}: {App.Director.LastError ?? App.Capture.LastMessage}");
                yield break;
            }
            Set("3. Chạy & tự quay 10:00", StepState.Done, $"{App.Director.Run.Capture.Status} — {App.Capture.LastMessage}");
            yield return StartCoroutine(PostProduce());
        }

        IEnumerator EnsureFfmpeg()
        {
            const string n = "1. FFmpeg";
            if (Ffmpeg.Locate(S.FfmpegPath)) { Set(n, StepState.Done, Ffmpeg.ExePath); yield break; }
            Set(n, StepState.Running, "Đang tải FFmpeg (chỉ lần đầu, ~150 MB)…");
            var t = Ffmpeg.DownloadAsync((p, msg) => Step(n).Detail = $"{msg} ({p:P0})");
            while (!t.IsCompleted) yield return null;
            if (t.IsFaulted || !t.Result) Set(n, StepState.Failed, Ffmpeg.LastError ?? t.Exception?.GetBaseException().Message);
            else Set(n, StepState.Done, Ffmpeg.ExePath);
        }

        // ================================================================ post-production
        IEnumerator PostProduce()
        {
            var d = App.Director;
            var run = d.Run;
            if (run == null || d.Pack == null) { LastError = "Chưa có run để hậu kỳ."; yield break; }
            if (!Ffmpeg.Locate(S.FfmpegPath)) { yield return StartCoroutine(EnsureFfmpeg()); if (!Ffmpeg.Available) yield break; }
            string silent = Path.Combine(d.RunFolder, "video", "gameplay_silent.mp4");
            if (!File.Exists(silent)) { Set("7. Xuất MP4 cuối", StepState.Failed, "Không thấy video gốc: " + silent); yield break; }
            double duration = VideoFileInspector.Inspect(silent).DurationSeconds ?? run.ProgramDurationSeconds;
            string audioDir = Path.Combine(d.RunFolder, "audio");
            Directory.CreateDirectory(audioDir);

            // ---------------------------------------------------- 4. commentary
            const string sN = "4. Lời bình tiếng Anh";
            var slots = NarrationSlotBuilder.Build(run);
            Narration = new NarrationScript { Writer = "none" };
            if (S.NarrationEnabled)
            {
                Set(sN, StepState.Running, $"{slots.Count} đoạn lời bình…");
                var t = NarrationWriter.WriteAsync(run, slots, d.Pack.Facts, Claude(), run.RunId.GetHashCode(), m => App.Msg(m));
                while (!t.IsCompleted) yield return null;
                if (t.IsFaulted) { Set(sN, StepState.Failed, t.Exception.GetBaseException().Message); yield break; }
                Narration = t.Result;
                int fromClaude = Narration.Lines.Count(l => l.Source == "claude");
                Set(sN, StepState.Done, $"{Narration.Lines.Count} câu · {fromClaude} từ Claude, {Narration.Lines.Count - fromClaude} từ mẫu offline (đã kiểm tra không bịa)");
            }
            else Set(sN, StepState.Skipped, "Đã tắt lời bình");

            // ---------------------------------------------------- 5. TTS
            const string tN = "5. Giọng đọc (TTS)";
            var clips = new Dictionary<string, float[]>();
            if (S.NarrationEnabled && Narration.Lines.Count > 0)
            {
                Set(tN, StepState.Running, "Đang đọc lời bình…");
                var lines = Narration.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).Select(l => new KeyValuePair<string, string>(l.SlotId, l.Text)).ToList();
                var provider = TtsFactory.Create(S);
                Dictionary<string, string> ttsFiles = new Dictionary<string, string>();
                if (provider != null)
                {
                    var t = provider.SynthesizeAsync(lines, Path.Combine(audioDir, "voice_raw"), m => App.Msg(m));
                    while (!t.IsCompleted) yield return null;
                    if (!t.IsFaulted) ttsFiles = t.Result;
                }
                if (ttsFiles.Count < lines.Count && S.TtsProvider != TtsProviderKind.WindowsVoice)
                {
                    App.Msg($"{provider?.Name ?? "TTS"}: {provider?.LastError} → dùng giọng Windows cho các câu còn thiếu.");
                    var missing = lines.Where(l => !ttsFiles.ContainsKey(l.Key)).ToList();
                    var t2 = new WindowsVoiceTts("").SynthesizeAsync(missing, Path.Combine(audioDir, "voice_raw_windows"), m => App.Msg(m));
                    while (!t2.IsCompleted) yield return null;
                    if (!t2.IsFaulted) foreach (var kv in t2.Result) ttsFiles[kv.Key] = kv.Value;
                }
                // normalise every clip to 44.1 kHz mono WAV, speeding up slightly if it overruns its window
                var ordered = Narration.Lines.OrderBy(l => l.Start).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    var line = ordered[i];
                    if (!ttsFiles.TryGetValue(line.SlotId, out string src)) continue;
                    double next = i + 1 < ordered.Count ? ordered[i + 1].Start : duration;
                    double avail = Math.Max(1.0, Math.Min(next - 0.15, line.End + 1.0) - line.Start);
                    string wav = Path.Combine(audioDir, line.SlotId + ".wav");
                    var conv = Ffmpeg.RunAsync($"-y -hide_banner -loglevel error -i {FfmpegTool.Q(src)} -ac 1 -ar 44100 {FfmpegTool.Q(wav)}", 60000);
                    while (!conv.IsCompleted) yield return null;
                    if (!File.Exists(wav)) continue;
                    float[] samples = Wav.ReadMono(wav, out int rate);
                    double secs = samples.Length / (double)rate;
                    if (secs > avail)
                    {
                        double tempo = Math.Min(1.35, secs / avail);
                        var conv2 = Ffmpeg.RunAsync($"-y -hide_banner -loglevel error -i {FfmpegTool.Q(src)} -ac 1 -ar 44100 -filter:a atempo={tempo.ToString("0.000", CultureInfo.InvariantCulture)} {FfmpegTool.Q(wav)}", 60000);
                        while (!conv2.IsCompleted) yield return null;
                        samples = Wav.ReadMono(wav, out rate);
                        secs = samples.Length / (double)rate;
                    }
                    line.AudioFile = "audio/" + line.SlotId + ".wav";
                    line.AudioSeconds = secs;
                    clips[line.SlotId] = samples;
                    Step(tN).Detail = $"{clips.Count}/{lines.Count} câu";
                }
                if (clips.Count == 0) Set(tN, StepState.Failed, (provider?.LastError ?? "Không có bộ đọc") + " — video sẽ không có lời bình.");
                else Set(tN, StepState.Done, $"{clips.Count}/{lines.Count} câu · {provider?.Name}");
            }
            else Set(tN, StepState.Skipped);

            // ---------------------------------------------------- 6. soundtrack (background thread)
            const string aN = "6. Âm thanh";
            Set(aN, StepState.Running, "Đang dựng âm thanh…");
            string mixPath = Path.Combine(audioDir, "final_mix.wav");
            var speedTracks = S.SoundDesignEnabled ? App.Replay.ExportSpeedTracks() : new List<SpeedTrack>();
            var attempts = run.Attempts.ToList();
            var events = run.Events.ToList();
            var segments = run.Segments.ToList();
            var lineList = Narration.Lines.ToList();
            bool sfx = S.SoundDesignEnabled;
            var mixTask = Task.Run(() =>
            {
                var st = new Soundtrack(duration);
                if (sfx)
                {
                    st.AddBed();
                    foreach (var tr in speedTracks) st.AddEngine(tr, speedTracks.Count > 1 ? 0.08f : 0.11f);
                    foreach (var a in attempts.Where(a => a.TakeoffProgramTime.HasValue))
                        st.AddWind(a.TakeoffProgramTime.Value, a.LandingProgramTime ?? a.TakeoffProgramTime.Value + 3, 0.09f);
                    foreach (var e in events)
                    {
                        if (e.Type == "Landed") st.AddImpact(e.ProgramTime, (float)(e.Values.TryGetValue("impactSpeed", out var v) ? v / 15.0 : 0.4));
                        else if (e.Type == "ObstacleHit") st.AddImpact(e.ProgramTime, 0.35f);
                        else if (e.Type == "Rollover") st.AddImpact(e.ProgramTime - 0.8, 0.8f);
                    }
                    foreach (var s in segments.Where(x => x.Kind == SegmentKind.Replay)) st.AddWind(s.Start, s.End, 0.05f);
                }
                var ordered = lineList.OrderBy(l => l.Start).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (!clips.TryGetValue(ordered[i].SlotId, out var c)) continue;
                    double next = i + 1 < ordered.Count ? ordered[i + 1].Start : duration;
                    st.AddVoice(c, ordered[i].Start, 1f, Math.Max(0.5, next - ordered[i].Start - 0.05));
                }
                st.Render(mixPath, duration);
            });
            while (!mixTask.IsCompleted) yield return null;
            if (mixTask.IsFaulted) { Set(aN, StepState.Failed, mixTask.Exception.GetBaseException().Message); yield break; }
            Set(aN, StepState.Done, sfx ? "Lời bình + tiếng động cơ/gió/va chạm tổng hợp" : "Chỉ lời bình");

            // ---------------------------------------------------- 7. final mp4
            const string fN = "7. Xuất MP4 cuối";
            Set(fN, StepState.Running, "Đang ghép video + âm thanh…");
            string title = d.Pack.Titles.FirstOrDefault(t => t.Recommended)?.Text ?? run.RunId;
            string final = Path.Combine(d.RunFolder, "FINAL_" + Slug(title) + ".mp4");
            var mux = Ffmpeg.RunAsync($"-y -hide_banner -loglevel error -i {FfmpegTool.Q(silent)} -i {FfmpegTool.Q(mixPath)} -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192k -ac 2 -shortest -movflags +faststart {FfmpegTool.Q(final)}", 600000);
            while (!mux.IsCompleted) yield return null;
            if (mux.IsFaulted || mux.Result.ExitCode != 0 || !File.Exists(final))
            {
                Set(fN, StepState.Failed, mux.IsFaulted ? mux.Exception.GetBaseException().Message : mux.Result.StdErr);
                yield break;
            }
            var status = App.Capture.Verify(final);
            FinalVideoPath = final;
            run.ManualOverrides.Add("post-production: narration + soundtrack muxed into " + Path.GetFileName(final));
            Set(fN, status == CaptureStatus.CaptureVerified ? StepState.Done : StepState.Failed, $"{status}: {App.Capture.LastMessage}");

            // ---------------------------------------------------- 8. metadata, captions, thumbnail
            const string mN = "8. Tiêu đề, mô tả, phụ đề, thumbnail";
            Set(mN, StepState.Running);
            d.RegenerateContent();
            var best = run.Highlights.Where(h => !string.IsNullOrEmpty(h.FrameFile)).OrderByDescending(h => h.Score).FirstOrDefault();
            if (best != null)
            {
                string thumb = Path.Combine(d.RunFolder, "thumbnail_1280x720.jpg");
                var th = Ffmpeg.RunAsync($"-y -hide_banner -loglevel error -i {FfmpegTool.Q(Path.Combine(d.RunFolder, best.FrameFile))} -vf scale=1280:720 -q:v 2 {FfmpegTool.Q(thumb)}", 60000);
                while (!th.IsCompleted) yield return null;
            }
            List<string> files = null;
            try
            {
                File.WriteAllText(Path.Combine(d.RunFolder, "narration_script.json"), MiniJson.Serialize(Narration), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(d.RunFolder, "captions_en.srt"), Narration.ToSrt(), new UTF8Encoding(false));
                files = d.Export();
                Episodes.Add(EpisodeLog.FromRun(run, d.Pack, scriptSource));
            }
            catch (Exception e) { Set(mN, StepState.Failed, e.Message); yield break; }
            Set(mN, StepState.Done, $"{files.Count + 3} tệp · readiness: {d.Quality?.Readiness}");
            App.Msg("✓ Video cuối: " + final);
        }

        static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            string r = sb.ToString().Trim('_');
            return r.Length > 60 ? r.Substring(0, 60) : (r.Length == 0 ? "episode" : r);
        }

        /// <summary>3-second test recording to check orientation/colour before a full episode.</summary>
        public void TestRecording()
        {
            if (Busy || App.Director.IsBusy) return;
            StartCoroutine(Guard(TestRecordingRoutine()));
        }

        IEnumerator TestRecordingRoutine()
        {
            Steps.Clear();
            yield return StartCoroutine(EnsureFfmpeg());
            if (!Ffmpeg.Available) yield break;
            string path = Path.Combine(App.RunsRoot, "test_recording.mp4");
            App.Recorder.FlipVertical = S.FlipVertical;
            if (!App.Recorder.StartRecording(path, 30)) { Set("Quay thử", StepState.Failed, App.Recorder.Error); yield break; }
            App.StartSandbox();
            Set("Quay thử", StepState.Running, "3 giây…");
            for (int i = 0; i < 90; i++) yield return null;
            string outp = App.Recorder.StopRecording();
            if (outp == null) { Set("Quay thử", StepState.Failed, App.Recorder.Error); yield break; }
            Set("Quay thử", StepState.Done, outp + " — mở xem: nếu hình bị lộn ngược, đổi ô 'Lật dọc'.");
            Application.OpenURL("file:///" + outp.Replace('\\', '/'));
        }
    }
}
