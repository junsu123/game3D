using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Fully automatic channel operation:
    ///   keep a buffer of finished episodes → upload each one as private with a scheduled publish time chosen for
    ///   the US + Europe audience (YouTube flips it public at that time) → thumbnail, English captions,
    ///   translated titles/descriptions. Runs while the studio is open, or daily via Windows Task Scheduler
    ///   ("-autopilot": the app starts, does what is needed, then quits).
    /// Uploads only episodes whose recording is CaptureVerified and whose Quality Gate passed the automated checks.
    /// </summary>
    public sealed class Autopilot : MonoBehaviour
    {
        public const string TaskName = "MYLTI Autopilot";
        public StudioApp App;
        public PublishSettings Settings { get; private set; }
        public PublishQueue Queue { get; private set; }
        public YouTubeClient YouTube { get; private set; }
        public string Status { get; private set; } = "Tắt";
        public bool Working { get; private set; }
        public bool LaunchedByScheduler { get; private set; }
        public float UploadProgress { get; private set; }

        string settingsPath, queuePath, tokenPath;
        DateTime nextCheckUtc;
        readonly DateTime startedUtc = DateTime.UtcNow;

        public void Init(StudioApp app)
        {
            App = app;
            settingsPath = Path.Combine(app.DataRoot, "publishing.json");
            queuePath = Path.Combine(app.DataRoot, "publish_queue.json");
            tokenPath = Path.Combine(app.DataRoot, "youtube_token.json");
            Settings = PublishSettings.Load(settingsPath);
            Queue = new PublishQueue(queuePath);
            if (Queue.LoadError != null) app.Msg(Queue.LoadError);
            RebuildClient();
            LaunchedByScheduler = Environment.GetCommandLineArgs().Any(a => a.Equals("-autopilot", StringComparison.OrdinalIgnoreCase));
            if (LaunchedByScheduler) { Settings.AutopilotEnabled = true; app.Msg("Autopilot: started by Windows Task Scheduler."); }
            nextCheckUtc = DateTime.UtcNow.AddSeconds(LaunchedByScheduler ? 5 : 20);
        }

        public void RebuildClient() => YouTube = new YouTubeClient(Settings.GoogleClientId, Settings.GoogleClientSecret, tokenPath);
        public void Save() { try { Settings.Save(settingsPath); } catch (Exception e) { App.Msg("publishing.json: " + e.Message); } }
        void SaveQueue() { try { Queue.Save(); } catch (Exception e) { App.Msg("publish_queue.json: " + e.Message); } }

        public List<DateTime> NextSlots(int count)
            => PublishPlanner.UpcomingSlots(Settings.Markets, Settings.VideosPerWeek, DateTime.UtcNow.AddHours(Settings.ReviewWindowHours), count, Queue.TakenSlots.ToList());

        // ================================================================ loop
        void Update()
        {
            if (Settings == null || !Settings.AutopilotEnabled) { if (!Working) Status = "Tắt"; return; }
            if (Working || DateTime.UtcNow < nextCheckUtc) return;
            nextCheckUtc = DateTime.UtcNow.AddSeconds(60);
            StartCoroutine(Cycle());
        }

        public void RunNow()
        {
            if (Working) return;
            nextCheckUtc = DateTime.UtcNow.AddSeconds(60);
            StartCoroutine(Cycle());
        }

        IEnumerator Cycle()
        {
            Working = true;
            var now = DateTime.UtcNow;
            Queue.Refresh(now);

            // 1. upload whatever is waiting
            if (YouTube.SignedIn)
                foreach (var item in Queue.Items.Where(i => i.State == PublishState.Produced
                                                         || (i.State == PublishState.Failed && i.Attempts < PublishQueue.MaxAttempts && (i.RetryAfterUtc ?? DateTime.MinValue) <= now)).ToList())
                    yield return StartCoroutine(Upload(item));

            // 2. produce a new episode if the buffer is short
            int ahead = Queue.ScheduledAhead(DateTime.UtcNow);
            bool needMore = ahead < Settings.BufferEpisodes;
            bool dayCapReached = Queue.ProducedOn(DateTime.UtcNow) >= Settings.MaxProductionsPerDay;
            if (needMore && !dayCapReached && !App.Producer.Busy && !App.Director.IsBusy)
            {
                Status = $"Đang sản xuất tập mới ({ahead}/{Settings.BufferEpisodes} tập đã sẵn)…";
                yield return StartCoroutine(Produce());
                if (YouTube.SignedIn)
                    foreach (var item in Queue.Items.Where(i => i.State == PublishState.Produced).ToList())
                        yield return StartCoroutine(Upload(item));
                ahead = Queue.ScheduledAhead(DateTime.UtcNow);
                needMore = ahead < Settings.BufferEpisodes;
                dayCapReached = Queue.ProducedOn(DateTime.UtcNow) >= Settings.MaxProductionsPerDay;
            }

            var next = Queue.Items.Where(i => i.State == PublishState.Scheduled && i.PublishAtUtc > DateTime.UtcNow).OrderBy(i => i.PublishAtUtc).FirstOrDefault();
            Status = (YouTube.SignedIn ? "" : "⚠ Chưa kết nối YouTube — video được làm sẵn và chờ. ")
                   + $"{ahead} tập đã sẵn/đã hẹn giờ" + (next != null ? $" · tập kế tiếp lên sóng {PublishPlanner.Describe(next.PublishAtUtc.Value, LocalOffset())}" : "")
                   + (needMore && dayCapReached ? " · đã đạt giới hạn sản xuất hôm nay" : "");
            Working = false;

            // 3. started by the scheduler: quit when there is nothing left for today
            bool pendingUpload = YouTube.SignedIn && Queue.Items.Any(i => i.State == PublishState.Produced);
            if (LaunchedByScheduler && !pendingUpload && (!needMore || dayCapReached || (DateTime.UtcNow - startedUtc).TotalHours > 7))
            {
                App.Msg("Autopilot: done for today, closing.");
                yield return new WaitForSecondsRealtime(3f);
                Application.Quit();
            }
        }

        // ================================================================ production
        IEnumerator Produce()
        {
            App.Producer.StartAutoEpisode();
            yield return null;
            while (App.Producer.Busy || App.Director.IsBusy) yield return new WaitForSecondsRealtime(1f);
            var d = App.Director;
            string video = App.Producer.FinalVideoPath;
            var item = new PublishItem { ProducedAtUtc = DateTime.UtcNow, RunId = d.Run?.RunId, RunFolder = d.RunFolder, VideoFile = video };
            string problem = null;
            if (string.IsNullOrEmpty(video) || !File.Exists(video)) problem = "no final video (" + (App.Producer.LastError ?? "unknown error") + ")";
            else if (d.Run?.Capture.Status != CaptureStatus.CaptureVerified) problem = "recording not verified: " + d.Run?.Capture.Status;
            else if (d.Pack == null || d.Pack.IsDraft) problem = "content pack is a draft";
            else if (d.Quality == null || d.Quality.Readiness < ReadinessLevel.ReadyForReview) problem = "quality gate: " + d.Quality?.Readiness;
            if (problem != null)
            {
                item.State = PublishState.Skipped;
                item.Error = problem;
                item.Title = d.Pack?.Titles.FirstOrDefault()?.Text;
                Queue.Items.Add(item);
                SaveQueue();
                App.Msg("Autopilot: episode not uploaded — " + problem);
                yield break;
            }

            // metadata prepared now (Claude translation needs the key; the facts come from the checked Content Pack)
            var meta = PublishMetadata.Build(d.Run, d.Pack, Settings, null);
            item.Title = meta.Title;
            if (Settings.TranslateMetadata && App.Settings.UseClaude)
            {
                Status = "Đang dịch tiêu đề/mô tả cho thị trường châu Âu…";
                var claude = new ClaudeClient(App.Settings.ClaudeApiKey, App.Settings.ClaudeModel);
                var t = PublishMetadata.TranslateAsync(meta.Title, meta.Description, Market.LanguagesFor(Settings.Markets), claude, m => App.Msg(m));
                while (!t.IsCompleted) yield return null;
                if (!t.IsFaulted) meta.Localizations = t.Result;
                item.Notes = $"{meta.Localizations.Count} translations";
            }
            string thumb = Path.Combine(d.RunFolder, "thumbnail_1280x720.jpg");
            string srt = Path.Combine(d.RunFolder, "captions_en.srt");
            try { PublishMetadata.SaveMeta(Path.Combine(d.RunFolder, "upload_meta.json"), meta, File.Exists(thumb) ? thumb : null, File.Exists(srt) ? srt : null); }
            catch (Exception e) { item.State = PublishState.Skipped; item.Error = "upload_meta.json: " + e.Message; Queue.Items.Add(item); SaveQueue(); yield break; }
            item.State = PublishState.Produced;
            Queue.Items.Add(item);
            SaveQueue();
            App.Msg("Autopilot: episode ready for upload — " + item.Title);
        }

        /// <summary>Queue the episode that was just produced by hand (Auto Episode button).</summary>
        public string EnqueueLastEpisode()
        {
            var d = App.Director;
            if (string.IsNullOrEmpty(App.Producer.FinalVideoPath) || d.Run == null) return "Chưa có video cuối nào trong phiên này.";
            if (Queue.Items.Any(i => i.VideoFile == App.Producer.FinalVideoPath)) return "Video này đã ở trong hàng đợi.";
            StartCoroutine(EnqueueRoutine());
            return null;
        }

        IEnumerator EnqueueRoutine()
        {
            Working = true;
            var d = App.Director;
            var meta = PublishMetadata.Build(d.Run, d.Pack, Settings, null);
            if (Settings.TranslateMetadata && App.Settings.UseClaude)
            {
                var t = PublishMetadata.TranslateAsync(meta.Title, meta.Description, Market.LanguagesFor(Settings.Markets), new ClaudeClient(App.Settings.ClaudeApiKey, App.Settings.ClaudeModel), m => App.Msg(m));
                while (!t.IsCompleted) yield return null;
                if (!t.IsFaulted) meta.Localizations = t.Result;
            }
            string thumb = Path.Combine(d.RunFolder, "thumbnail_1280x720.jpg"), srt = Path.Combine(d.RunFolder, "captions_en.srt");
            PublishMetadata.SaveMeta(Path.Combine(d.RunFolder, "upload_meta.json"), meta, File.Exists(thumb) ? thumb : null, File.Exists(srt) ? srt : null);
            Queue.Items.Add(new PublishItem
            {
                ProducedAtUtc = DateTime.UtcNow, RunId = d.Run.RunId, RunFolder = d.RunFolder, VideoFile = App.Producer.FinalVideoPath,
                Title = meta.Title, State = PublishState.Produced, Notes = "added by hand"
            });
            SaveQueue();
            Working = false;
            if (YouTube.SignedIn) RunNow();
        }

        // ================================================================ upload
        IEnumerator Upload(PublishItem item)
        {
            string metaPath = Path.Combine(item.RunFolder ?? "", "upload_meta.json");
            if (!File.Exists(item.VideoFile ?? "") || !File.Exists(metaPath))
            {
                item.State = PublishState.Skipped;
                item.Error = "video or upload_meta.json missing";
                SaveQueue();
                yield break;
            }
            VideoUploadMeta meta;
            string thumb, srt;
            try { meta = PublishMetadata.LoadMeta(metaPath, Settings, out thumb, out srt); }
            catch (Exception e) { item.State = PublishState.Skipped; item.Error = "upload_meta.json: " + e.Message; SaveQueue(); yield break; }

            var slot = NextSlots(1).FirstOrDefault();
            meta.PublishAtUtc = Settings.AutoPublish ? slot : (DateTime?)null;
            item.State = PublishState.Uploading;
            item.Attempts++;
            SaveQueue();
            Status = $"Đang tải lên YouTube: {item.Title}";
            UploadProgress = 0;
            var t = YouTube.UploadAsync(item.VideoFile, meta, (p, m) => { UploadProgress = p; });
            while (!t.IsCompleted) yield return null;
            var r = t.IsFaulted ? new UploadResult { Error = t.Exception.GetBaseException().Message } : t.Result;
            if (!r.Ok)
            {
                item.State = PublishState.Failed;
                item.Error = r.Error;
                item.RetryAfterUtc = DateTime.UtcNow.AddHours(r.Error != null && r.Error.Contains("quota") ? 24 : 1);
                SaveQueue();
                App.Msg("YouTube upload failed: " + r.Error);
                yield break;
            }
            item.VideoId = r.VideoId;
            item.UploadedAtUtc = DateTime.UtcNow;
            item.PublishAtUtc = slot;
            item.Manual = !Settings.AutoPublish;
            item.State = PublishState.Scheduled;
            item.Error = null;
            var notes = new List<string>(r.Warnings) { $"privacy={r.PrivacyStatus}, publishAt={r.PublishAt ?? "-"}", "uploaded by autopilot; no human review" };

            if (Settings.UploadThumbnail && !string.IsNullOrEmpty(thumb))
            {
                var th = YouTube.SetThumbnailAsync(r.VideoId, thumb);
                while (!th.IsCompleted) yield return null;
                if (!th.IsFaulted && th.Result != null) notes.Add("thumbnail: " + th.Result);
            }
            if (Settings.UploadCaptions && !string.IsNullOrEmpty(srt))
            {
                var cap = YouTube.UploadCaptionsAsync(r.VideoId, srt);
                while (!cap.IsCompleted) yield return null;
                if (!cap.IsFaulted && cap.Result != null) notes.Add("captions: " + cap.Result);
            }
            item.Notes = string.Join(" | ", notes);
            SaveQueue();
            try
            {
                File.WriteAllText(Path.Combine(item.RunFolder, "youtube_upload.json"), MiniJson.Serialize(item), new UTF8Encoding(false));
            }
            catch (Exception) { }
            App.Msg($"✓ YouTube: https://youtu.be/{r.VideoId} — " + (Settings.AutoPublish ? "lên sóng " + PublishPlanner.Describe(slot, LocalOffset()) : "đang để Riêng tư (bạn tự bấm công khai)"));
        }

        public static TimeSpan LocalOffset() => TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);

        // ================================================================ Windows Task Scheduler
        public string InstallWindowsTask()
        {
            if (Application.isEditor) return "Lịch Windows chỉ dùng cho bản .exe: menu MYLTI ▸ Build Windows x64, mở .exe rồi bấm nút này trong .exe.";
            if (Application.platform != RuntimePlatform.WindowsPlayer) return "Chỉ hỗ trợ Windows.";
            string exe;
            try { exe = Process.GetCurrentProcess().MainModule.FileName; }
            catch (Exception e) { return "Không xác định được đường dẫn .exe: " + e.Message; }
            string xmlPath = Path.Combine(App.DataRoot, "autopilot_task.xml");
            File.WriteAllText(xmlPath, PublishMetadata.TaskXml(exe, Settings.ProductionHourLocal, Settings.WakeComputer, DateTime.Now), Encoding.Unicode);
            string err = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
            if (err == null) return null;
            // fallback without wake-up
            string err2 = RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" -autopilot\" /SC DAILY /ST {Settings.ProductionHourLocal:00}:00 /F");
            return err2 == null ? "Đã tạo lịch (không có tự đánh thức máy): " + err : "Không tạo được lịch Windows: " + err + " / " + err2;
        }

        public string RemoveWindowsTask() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

        static string RunSchtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using (var p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(20000);
                    return p.ExitCode == 0 ? null : (err.Trim().Length > 0 ? err.Trim() : "exit " + p.ExitCode);
                }
            }
            catch (Exception e) { return e.Message; }
        }
    }
}
