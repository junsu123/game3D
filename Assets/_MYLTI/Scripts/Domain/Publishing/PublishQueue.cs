using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Mylti.Domain
{
    public enum PublishState { Produced, Uploading, Scheduled, Published, Failed, Skipped }

    public sealed class PublishItem : IJsonWritable
    {
        public string RunId, RunFolder, VideoFile, Title;
        public PublishState State;
        public string VideoId;
        public DateTime? PublishAtUtc, UploadedAtUtc;
        public DateTime ProducedAtUtc;
        public int Attempts;
        public bool Manual;               // uploaded private: the owner publishes by hand
        public DateTime? RetryAfterUtc;
        public string Error;
        public string Notes;

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("runId", RunId).Prop("runFolder", RunFolder).Prop("videoFile", VideoFile).Prop("title", Title)
             .Prop("state", State.ToString()).Prop("videoId", VideoId)
             .Prop("publishAtUtc", PublishAtUtc.HasValue ? Iso(PublishAtUtc.Value) : null)
             .Prop("uploadedAtUtc", UploadedAtUtc.HasValue ? Iso(UploadedAtUtc.Value) : null)
             .Prop("producedAtUtc", Iso(ProducedAtUtc)).Prop("attempts", Attempts).Prop("manual", Manual).Prop("retryAfterUtc", RetryAfterUtc.HasValue ? Iso(RetryAfterUtc.Value) : null).Prop("error", Error).Prop("notes", Notes).EndObject();
        }

        public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        public static DateTime? ParseIso(string s)
            => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTime?)null;

        public static PublishItem FromJson(Dictionary<string, object> o) => new PublishItem
        {
            RunId = MiniJson.GetString(o, "runId"), RunFolder = MiniJson.GetString(o, "runFolder"), VideoFile = MiniJson.GetString(o, "videoFile"),
            Title = MiniJson.GetString(o, "title"), State = ScenarioConfig.ParseEnum(MiniJson.GetString(o, "state"), PublishState.Produced),
            VideoId = MiniJson.GetString(o, "videoId"), PublishAtUtc = ParseIso(MiniJson.GetString(o, "publishAtUtc")),
            UploadedAtUtc = ParseIso(MiniJson.GetString(o, "uploadedAtUtc")), ProducedAtUtc = ParseIso(MiniJson.GetString(o, "producedAtUtc")) ?? DateTime.UtcNow,
            Attempts = MiniJson.GetInt(o, "attempts", 0), Manual = MiniJson.GetBool(o, "manual", false), RetryAfterUtc = ParseIso(MiniJson.GetString(o, "retryAfterUtc")), Error = MiniJson.GetString(o, "error"), Notes = MiniJson.GetString(o, "notes"),
        };
    }

    /// <summary>Finished episodes waiting to be uploaded / scheduled. Saved as publish_queue.json.</summary>
    public sealed class PublishQueue
    {
        readonly string path;
        public readonly List<PublishItem> Items = new List<PublishItem>();
        public string LoadError;

        public PublishQueue(string path)
        {
            this.path = path;
            if (!File.Exists(path)) return;
            try
            {
                foreach (var x in MiniJson.Arr(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8))) ?? new List<object>())
                    Items.Add(PublishItem.FromJson(MiniJson.Obj(x)));
            }
            catch (Exception e)
            {
                LoadError = "publish_queue.json unreadable: " + e.Message;
                try { File.Move(path, path + ".corrupt_" + DateTime.UtcNow.Ticks); } catch (IOException) { }
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, MiniJson.Serialize(Items), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public IEnumerable<DateTime> TakenSlots => Items.Where(i => i.PublishAtUtc.HasValue && (i.State == PublishState.Scheduled || i.State == PublishState.Published || i.State == PublishState.Uploading)).Select(i => i.PublishAtUtc.Value);

        /// <summary>Episodes already scheduled to go live after 'now' — the production buffer.</summary>
        public int ScheduledAhead(DateTime nowUtc) => Items.Count(i => i.State == PublishState.Scheduled && i.PublishAtUtc > nowUtc)
                                                     + Items.Count(i => i.State == PublishState.Produced || i.State == PublishState.Uploading || (i.State == PublishState.Failed && i.Attempts < MaxAttempts));

        public const int MaxAttempts = 4;

        public int ProducedOn(DateTime dayUtc) => Items.Count(i => i.ProducedAtUtc.Date == dayUtc.Date);

        /// <summary>Scheduled items whose time has passed become Published (YouTube flips them; we only mirror it).</summary>
        public void Refresh(DateTime nowUtc)
        {
            foreach (var i in Items.Where(i => i.State == PublishState.Scheduled && !i.Manual && i.PublishAtUtc <= nowUtc)) i.State = PublishState.Published;
        }
    }

    /// <summary>Settings for scheduling + automatic upload. Saved as publishing.json (local only, never in the repo).</summary>
    public sealed class PublishSettings : IJsonWritable
    {
        public bool AutopilotEnabled;
        public int VideosPerWeek = 3;
        public int ReviewWindowHours = 12;    // upload at least this long before going public (time to check in YouTube Studio)
        public int BufferEpisodes = 2;        // keep this many episodes produced/scheduled ahead
        public int MaxProductionsPerDay = 2;
        public int ProductionHourLocal = 13;  // Windows Task Scheduler start time (PC local time)
        public bool WakeComputer = true;
        public bool AutoPublish = true;       // false = upload as private, you publish by hand
        public bool NotifySubscribers = true;
        public bool TranslateMetadata = true; // translated titles/descriptions for European markets (needs Claude key)
        public bool UploadCaptions = true;
        public bool UploadThumbnail = true;
        public bool DeclareSyntheticMedia = false; // game footage + generic TTS voice: YouTube does not require the label
        public bool MadeForKids = false;           // owner's legal call (COPPA): true if the videos are aimed at children
        public string CategoryId = "20";           // 20 = Gaming, 2 = Autos & Vehicles
        public string GoogleClientId = "", GoogleClientSecret = "";
        public List<Market> Markets = Market.Defaults();

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("schemaVersion", 1).Prop("autopilotEnabled", AutopilotEnabled).Prop("videosPerWeek", VideosPerWeek)
             .Prop("reviewWindowHours", ReviewWindowHours).Prop("bufferEpisodes", BufferEpisodes).Prop("maxProductionsPerDay", MaxProductionsPerDay)
             .Prop("productionHourLocal", ProductionHourLocal).Prop("wakeComputer", WakeComputer).Prop("autoPublish", AutoPublish)
             .Prop("notifySubscribers", NotifySubscribers).Prop("translateMetadata", TranslateMetadata).Prop("uploadCaptions", UploadCaptions)
             .Prop("uploadThumbnail", UploadThumbnail).Prop("declareSyntheticMedia", DeclareSyntheticMedia).Prop("madeForKids", MadeForKids).Prop("categoryId", CategoryId)
             .Prop("googleClientId", GoogleClientId).Prop("googleClientSecret", GoogleClientSecret);
            w.Key("marketWeights").BeginObject();
            foreach (var m in Markets) w.Prop(m.Code, Math.Round(m.Weight, 4));
            w.EndObject().EndObject();
        }

        public static PublishSettings Load(string path)
        {
            var s = new PublishSettings();
            if (!File.Exists(path)) return s;
            try
            {
                var o = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)));
                s.AutopilotEnabled = MiniJson.GetBool(o, "autopilotEnabled", false);
                s.VideosPerWeek = Math.Max(1, Math.Min(7, MiniJson.GetInt(o, "videosPerWeek", 3)));
                s.ReviewWindowHours = Math.Max(1, Math.Min(168, MiniJson.GetInt(o, "reviewWindowHours", 12)));
                s.BufferEpisodes = Math.Max(1, Math.Min(10, MiniJson.GetInt(o, "bufferEpisodes", 2)));
                s.MaxProductionsPerDay = Math.Max(1, Math.Min(6, MiniJson.GetInt(o, "maxProductionsPerDay", 2)));
                s.ProductionHourLocal = Math.Max(0, Math.Min(23, MiniJson.GetInt(o, "productionHourLocal", 13)));
                s.WakeComputer = MiniJson.GetBool(o, "wakeComputer", true);
                s.AutoPublish = MiniJson.GetBool(o, "autoPublish", true);
                s.NotifySubscribers = MiniJson.GetBool(o, "notifySubscribers", true);
                s.TranslateMetadata = MiniJson.GetBool(o, "translateMetadata", true);
                s.UploadCaptions = MiniJson.GetBool(o, "uploadCaptions", true);
                s.UploadThumbnail = MiniJson.GetBool(o, "uploadThumbnail", true);
                s.DeclareSyntheticMedia = MiniJson.GetBool(o, "declareSyntheticMedia", false);
                s.MadeForKids = MiniJson.GetBool(o, "madeForKids", false);
                s.CategoryId = MiniJson.GetString(o, "categoryId", "20");
                s.GoogleClientId = MiniJson.GetString(o, "googleClientId", "");
                s.GoogleClientSecret = MiniJson.GetString(o, "googleClientSecret", "");
                var wts = MiniJson.Obj(MiniJson.Get(o, "marketWeights"));
                if (wts != null)
                    foreach (var m in s.Markets)
                        if (wts.TryGetValue(m.Code, out var v) && v is double d) m.Weight = Math.Max(0, Math.Min(1, d));
            }
            catch (Exception) { /* defaults */ }
            return s;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, MiniJson.Serialize(this), new UTF8Encoding(false));
        }
    }
}
