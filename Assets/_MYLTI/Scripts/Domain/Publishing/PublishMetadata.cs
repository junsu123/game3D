using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    /// <summary>Builds the upload metadata from the run's checked Content Pack and translates it for Europe.</summary>
    public static class PublishMetadata
    {
        public static VideoUploadMeta Build(RunRecord run, ContentPack pack, PublishSettings ps, DateTime? publishAtUtc)
        {
            var m = new VideoUploadMeta
            {
                Title = pack.Titles.FirstOrDefault(t => t.Recommended)?.Text ?? pack.Titles.FirstOrDefault()?.Text ?? run.Plan.Concept.ViewerQuestion,
                Description = pack.Description,
                CategoryId = ps.CategoryId,
                PublishAtUtc = publishAtUtc,
                AutoPublish = ps.AutoPublish,
                NotifySubscribers = ps.NotifySubscribers,
                ContainsSyntheticMedia = ps.DeclareSyntheticMedia,
                MadeForKids = ps.MadeForKids,
            };
            m.Tags = Tags(run);
            return m;
        }

        public static List<string> Tags(RunRecord run)
        {
            var tags = new List<string> { "mega ramp", "car jump", "vehicle challenge", "car simulation", "3d simulation", "ramp jump challenge" };
            foreach (var e in run.Plan.Entrants) tags.Add(e.Def.DisplayNameEn.ToLowerInvariant());
            if (run.Plan.Entrants.Count >= 2) tags.Add(string.Join(" vs ", run.Plan.Entrants.Select(e => e.Def.DisplayNameEn.ToLowerInvariant())));
            var specs = run.Plan.ScoredRounds.ToList();
            if (specs.Any(r => r.BridgeWidth > 0)) tags.Add("narrow bridge");
            if (specs.Any(r => r.Obstacles != ObstacleSet.None)) tags.Add("obstacle course");
            if (run.Attempts.Any(a => a.Outcome == AttemptOutcome.Rollover)) tags.Add("rollover");
            return tags;
        }

        // ------------------------------------------------------------------ translation (Claude, checked)
        static readonly Dictionary<string, string> LanguageNames = new Dictionary<string, string>
        {
            ["de"] = "German", ["fr"] = "French", ["es"] = "Spanish (Spain)", ["it"] = "Italian", ["nl"] = "Dutch", ["pl"] = "Polish",
            ["sv"] = "Swedish", ["da"] = "Danish", ["fi"] = "Finnish", ["pt"] = "Portuguese",
        };

        public const string TranslateSystem =
@"You translate YouTube titles and descriptions for an English 3D vehicle-challenge channel (a game simulation with generic, unbranded vehicles).
Translate naturally for viewers in each language, keep the curiosity of the title, keep it under 100 characters.
Keep EVERY number, unit and timestamp exactly as written (write decimals with a point, e.g. 18.5 m; keep chapter lines like 01:23 unchanged and in the same order).
Do not add facts, results, brand names, emojis, hashtags or links that are not in the English text. Vehicle names may be translated (e.g. Heavy Truck → Schwerer Lkw).
Reply JSON only: {""de"":{""title"":""..."",""description"":""...""}, ...}";

        public static async Task<Dictionary<string, KeyValuePair<string, string>>> TranslateAsync(string title, string description, IList<string> languages, ClaudeClient claude, Action<string> log)
        {
            var result = new Dictionary<string, KeyValuePair<string, string>>();
            var langs = languages.Where(l => LanguageNames.ContainsKey(l)).Distinct().ToList();
            if (claude == null || !claude.HasKey || langs.Count == 0) return result;
            var w = new JsonWriter(true);
            w.BeginObject().Prop("title", title).Prop("description", description).Key("languages").BeginObject();
            foreach (var l in langs) w.Prop(l, LanguageNames[l]);
            w.EndObject().EndObject();
            string reply = await claude.CompleteAsync(TranslateSystem, w.ToString(), 12000).ConfigureAwait(false);
            if (reply == null) { log?.Invoke("Translation failed: " + claude.LastError); return result; }
            Dictionary<string, object> o;
            try { o = MiniJson.Obj(MiniJson.Parse(ClaudeClient.ExtractJson(reply) ?? "{}")); }
            catch (Exception e) { log?.Invoke("Translation JSON invalid: " + e.Message); return result; }
            foreach (var l in langs)
            {
                var d = MiniJson.Obj(MiniJson.Get(o, l));
                string t = MiniJson.GetString(d, "title"), desc = MiniJson.GetString(d, "description");
                string why = CheckTranslation(title, description, t, desc);
                if (why == null) result[l] = new KeyValuePair<string, string>(t.Trim(), desc.Trim());
                else log?.Invoke($"Translation {l} dropped ({why}).");
            }
            return result;
        }

        static string NormalizeNumbers(string s) => Regex.Replace(s ?? "", @"(\d),(\d)", "$1.$2");

        /// <summary>Null when the translation keeps the facts: same numbers and chapter timestamps, no brands, sane length.</summary>
        public static string CheckTranslation(string srcTitle, string srcDesc, string title, string desc)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(desc)) return "empty";
            if (title.Length > 100) return "title too long";
            if (title.Contains("<") || title.Contains(">") || desc.Contains("<") || desc.Contains(">")) return "angle brackets";
            string lower = (title + " " + desc).ToLowerInvariant();
            string blocked = ClaimChecker.FindBlockedBrand(lower);
            if (blocked != null && !ClaimChecker.ContainsWord((srcTitle + " " + srcDesc).ToLowerInvariant(), blocked)) return "brand " + blocked;
            var srcNums = new HashSet<string>(ClaimChecker.ExtractNumbers(srcTitle + " " + srcDesc));
            foreach (var n in ClaimChecker.ExtractNumbers(NormalizeNumbers(title + " " + desc)))
                if (!srcNums.Contains(n)) return "number " + n + " not in the English text";
            var ts = new Regex(@"\b\d{1,2}:\d{2}\b");
            var a = ts.Matches(srcDesc ?? "").Cast<Match>().Select(x => x.Value).ToList();
            var b = ts.Matches(desc).Cast<Match>().Select(x => x.Value).ToList();
            if (!a.SequenceEqual(b)) return "chapter timestamps changed";
            return null;
        }

        // ------------------------------------------------------------------ upload_meta.json (prepared at production time)
        public static void SaveMeta(string path, VideoUploadMeta m, string thumbnail, string captions)
        {
            var w = new JsonWriter(true);
            w.BeginObject().Prop("title", m.Title).Prop("description", m.Description).Prop("categoryId", m.CategoryId)
             .Prop("defaultLanguage", m.DefaultLanguage).PropStrings("tags", m.Tags).Prop("thumbnail", thumbnail).Prop("captions", captions);
            w.Key("localizations").BeginObject();
            foreach (var kv in m.Localizations) w.Key(kv.Key).BeginObject().Prop("title", kv.Value.Key).Prop("description", kv.Value.Value).EndObject();
            w.EndObject().EndObject();
            File.WriteAllText(path, w.ToString(), new UTF8Encoding(false));
        }

        public static VideoUploadMeta LoadMeta(string path, PublishSettings ps, out string thumbnail, out string captions)
        {
            var o = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)));
            var m = new VideoUploadMeta
            {
                Title = MiniJson.GetString(o, "title"), Description = MiniJson.GetString(o, "description"),
                CategoryId = ps.CategoryId, DefaultLanguage = MiniJson.GetString(o, "defaultLanguage", "en"),
                AutoPublish = ps.AutoPublish, NotifySubscribers = ps.NotifySubscribers, ContainsSyntheticMedia = ps.DeclareSyntheticMedia,
                MadeForKids = ps.MadeForKids,
            };
            foreach (var t in MiniJson.Arr(MiniJson.Get(o, "tags")) ?? new List<object>()) if (t is string ts) m.Tags.Add(ts);
            var loc = MiniJson.Obj(MiniJson.Get(o, "localizations"));
            if (loc != null)
                foreach (var kv in loc)
                {
                    var d = MiniJson.Obj(kv.Value);
                    m.Localizations[kv.Key] = new KeyValuePair<string, string>(MiniJson.GetString(d, "title"), MiniJson.GetString(d, "description"));
                }
            thumbnail = MiniJson.GetString(o, "thumbnail");
            captions = MiniJson.GetString(o, "captions");
            return m;
        }

        // ------------------------------------------------------------------ Windows Task Scheduler
        /// <summary>Task Scheduler XML: start the studio with -autopilot every day at the given local hour (optionally waking the PC).</summary>
        public static string TaskXml(string exePath, int hourLocal, bool wake, DateTime todayLocal)
        {
            string X(string s) => SecurityElement.Escape(s ?? "");
            string start = todayLocal.Date.AddHours(hourLocal).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            string dir = System.IO.Path.GetDirectoryName(exePath) ?? "";
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
            sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");
            sb.Append("  <RegistrationInfo><Description>MYLTI Challenge Studio autopilot: produce, upload and schedule episodes.</Description></RegistrationInfo>\r\n");
            sb.Append($"  <Triggers><CalendarTrigger><StartBoundary>{start}</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger></Triggers>\r\n");
            sb.Append("  <Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\r\n");
            sb.Append("  <Settings>\r\n");
            sb.Append("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n");
            sb.Append("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n");
            sb.Append("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n");
            sb.Append("    <AllowHardTerminate>true</AllowHardTerminate>\r\n");
            sb.Append("    <StartWhenAvailable>true</StartWhenAvailable>\r\n");
            sb.Append("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n");
            sb.Append("    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\r\n");
            sb.Append("    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n");
            sb.Append("    <Enabled>true</Enabled>\r\n");
            sb.Append("    <Hidden>false</Hidden>\r\n");
            sb.Append("    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n");
            sb.Append($"    <WakeToRun>{(wake ? "true" : "false")}</WakeToRun>\r\n");
            sb.Append("    <ExecutionTimeLimit>PT8H</ExecutionTimeLimit>\r\n");
            sb.Append("    <Priority>7</Priority>\r\n");
            sb.Append("  </Settings>\r\n");
            sb.Append($"  <Actions Context=\"Author\"><Exec><Command>{X(exePath)}</Command><Arguments>-autopilot</Arguments><WorkingDirectory>{X(dir)}</WorkingDirectory></Exec></Actions>\r\n");
            sb.Append("</Task>\r\n");
            return sb.ToString();
        }
    }
}
