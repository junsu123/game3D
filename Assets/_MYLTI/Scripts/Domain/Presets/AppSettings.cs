using System;
using System.IO;
using System.Text;

namespace Mylti.Domain
{
    /// <summary>Per-user app settings (stored next to presets, never inside the project/repo).</summary>
    public sealed class AppSettings : IJsonWritable
    {
        public CaptureBackend Backend = CaptureBackend.None;
        public string ObsHost = "127.0.0.1";
        public int ObsPort = 4455;
        public bool RememberObsPassword = false;
        public string ObsPassword = "";           // only persisted when RememberObsPassword = true
        public string ObsRecordingFolder = "";    // for ObsManual auto-detection
        public int CaptureFps = 30;
        public bool ShowScoreboardInVideo = true;
        public bool ShowCardsInVideo = true;
        public bool EnableAudio = true;
        public bool DraftDuration = false;        // 5:00 test mode, never for final delivery
        public ContentLanguage Language = ContentLanguage.en;
        public string Quality = "High";           // Low / Medium / High

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("schemaVersion", 1).Prop("backend", Backend.ToString())
                .Prop("obsHost", ObsHost).Prop("obsPort", ObsPort).Prop("rememberObsPassword", RememberObsPassword)
                .Prop("obsPassword", RememberObsPassword ? ObsPassword : "")
                .Prop("obsRecordingFolder", ObsRecordingFolder).Prop("captureFps", CaptureFps)
                .Prop("showScoreboardInVideo", ShowScoreboardInVideo).Prop("showCardsInVideo", ShowCardsInVideo)
                .Prop("enableAudio", EnableAudio).Prop("draftDuration", DraftDuration)
                .Prop("language", Language.ToString()).Prop("quality", Quality)
                .EndObject();
        }

        public static AppSettings Load(string path, out string error)
        {
            error = null;
            var s = new AppSettings();
            if (!File.Exists(path)) return s;
            try
            {
                var o = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)));
                s.Backend = ScenarioConfig.ParseEnum(MiniJson.GetString(o, "backend"), s.Backend);
                s.ObsHost = MiniJson.GetString(o, "obsHost", s.ObsHost);
                s.ObsPort = Math.Max(1, Math.Min(65535, MiniJson.GetInt(o, "obsPort", s.ObsPort)));
                s.RememberObsPassword = MiniJson.GetBool(o, "rememberObsPassword", false);
                s.ObsPassword = s.RememberObsPassword ? MiniJson.GetString(o, "obsPassword", "") : "";
                s.ObsRecordingFolder = MiniJson.GetString(o, "obsRecordingFolder", "");
                s.CaptureFps = MiniJson.GetInt(o, "captureFps", 30) == 60 ? 60 : 30;
                s.ShowScoreboardInVideo = MiniJson.GetBool(o, "showScoreboardInVideo", true);
                s.ShowCardsInVideo = MiniJson.GetBool(o, "showCardsInVideo", true);
                s.EnableAudio = MiniJson.GetBool(o, "enableAudio", true);
                s.DraftDuration = MiniJson.GetBool(o, "draftDuration", false);
                s.Language = ScenarioConfig.ParseEnum(MiniJson.GetString(o, "language"), s.Language);
                s.Quality = MiniJson.GetString(o, "quality", "High");
            }
            catch (Exception e)
            {
                error = "Settings file unreadable, defaults used: " + e.Message;
            }
            return s;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, MiniJson.Serialize(this), new UTF8Encoding(false));
        }
    }
}
