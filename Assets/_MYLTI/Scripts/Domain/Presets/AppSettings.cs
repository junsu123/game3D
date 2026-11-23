using System;
using System.IO;
using System.Text;

namespace Mylti.Domain
{
    /// <summary>Per-user app settings (stored next to presets, never inside the project/repo).</summary>
    public sealed class AppSettings : IJsonWritable
    {
        public CaptureBackend Backend = CaptureBackend.BuiltInFfmpeg;
        public string FfmpegPath = "";
        public bool FlipVertical = true;

        // Claude (script + commentary). Billed by Anthropic per token; key stays on this PC.
        public bool UseClaude = true;
        public string ClaudeApiKey = "";
        public string ClaudeModel = ClaudeClient.DefaultModel;
        public bool LetClaudePickVehicles = true;
        public bool RememberApiKeys = true;

        // English voice-over
        public TtsProviderKind TtsProvider = TtsProviderKind.Kokoro;   // free local neural voice (Apache-2.0)
        public int KokoroVoice = 3;                                     // af_heart
        public string ElevenLabsModel = "eleven_multilingual_v2";
        public bool BurnTextCards = true;                               // intro / hook / result text in the final MP4
        public bool ShowNameplates = true;                              // name + speed above each vehicle
        public bool AllowRealBrands = true;                             // owner's real car models may be named by brand
        public float DamageStrength = 1f;                               // body deformation multiplier
        public bool MusicEnabled = true;                                // music files from <data>/Music
        public float MusicVolume = 0.6f;
        public bool CrowdEnabled = true;                                // animated spectators + crowd sound
        public float CrowdDensity = 1f;                                 // 0.3 … 1.5 (fewer people = faster PC)
        public float CrowdVolume = 0.7f;
        public float CrowdHeroShare = 0.6f;                             // share of spectators in MYLTI Squad hero costumes
        public bool UseHdriSky = true;                                  // photographed CC0 sky (Poly Haven)
        public string HdriId = "kloofendal_48d_partly_cloudy_puresky";
        public string TtsVoice = "";
        public string GoogleTtsKey = "";
        public string ElevenLabsKey = "";
        public string ElevenLabsVoiceId = "";
        public bool NarrationEnabled = true;
        public bool SoundDesignEnabled = true;
        public string ObsHost = "127.0.0.1";
        public int ObsPort = 4455;
        public bool RememberObsPassword = false;
        public string ObsPassword = "";           // only persisted when RememberObsPassword = true
        public string ObsRecordingFolder = "";    // for ObsManual auto-detection
        public int CaptureFps = 30;
        public bool ShowScoreboardInVideo = false;   // clean picture: no numbers on screen
        public bool ShowCardsInVideo = false;
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
                .Prop("ffmpegPath", FfmpegPath).Prop("flipVertical", FlipVertical)
                .Prop("useClaude", UseClaude).Prop("claudeModel", ClaudeModel).Prop("letClaudePickVehicles", LetClaudePickVehicles)
                .Prop("rememberApiKeys", RememberApiKeys)
                .Prop("claudeApiKey", RememberApiKeys ? ClaudeApiKey : "")
                .Prop("ttsProvider", TtsProvider.ToString()).Prop("ttsVoice", TtsVoice)
                .Prop("googleTtsKey", RememberApiKeys ? GoogleTtsKey : "")
                .Prop("elevenLabsKey", RememberApiKeys ? ElevenLabsKey : "").Prop("elevenLabsVoiceId", ElevenLabsVoiceId)
                .Prop("elevenLabsModel", ElevenLabsModel).Prop("kokoroVoice", KokoroVoice)
                .Prop("burnTextCards", BurnTextCards).Prop("showNameplates", ShowNameplates)
                .Prop("allowRealBrands", AllowRealBrands).Prop("damageStrength", DamageStrength).Prop("musicEnabled", MusicEnabled)
                .Prop("musicVolume", MusicVolume).Prop("useHdriSky", UseHdriSky).Prop("hdriId", HdriId)
                .Prop("crowdEnabled", CrowdEnabled).Prop("crowdDensity", CrowdDensity).Prop("crowdVolume", CrowdVolume).Prop("crowdHeroShare", CrowdHeroShare)
                .Prop("narrationEnabled", NarrationEnabled).Prop("soundDesignEnabled", SoundDesignEnabled)
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
                s.EnableAudio = MiniJson.GetBool(o, "enableAudio", true);
                s.DraftDuration = MiniJson.GetBool(o, "draftDuration", false);
                s.Language = ScenarioConfig.ParseEnum(MiniJson.GetString(o, "language"), s.Language);
                s.Quality = MiniJson.GetString(o, "quality", "High");
                s.ShowCardsInVideo = MiniJson.GetBool(o, "showCardsInVideo", false);
                s.ShowScoreboardInVideo = MiniJson.GetBool(o, "showScoreboardInVideo", false);
                s.FfmpegPath = MiniJson.GetString(o, "ffmpegPath", "");
                s.FlipVertical = MiniJson.GetBool(o, "flipVertical", true);
                s.UseClaude = MiniJson.GetBool(o, "useClaude", true);
                s.ClaudeModel = MiniJson.GetString(o, "claudeModel", ClaudeClient.DefaultModel);
                s.LetClaudePickVehicles = MiniJson.GetBool(o, "letClaudePickVehicles", true);
                s.RememberApiKeys = MiniJson.GetBool(o, "rememberApiKeys", true);
                s.ClaudeApiKey = MiniJson.GetString(o, "claudeApiKey", "");
                s.TtsProvider = ScenarioConfig.ParseEnum(MiniJson.GetString(o, "ttsProvider"), TtsProviderKind.Kokoro);
                s.KokoroVoice = MiniJson.GetInt(o, "kokoroVoice", 3);
                s.ElevenLabsModel = MiniJson.GetString(o, "elevenLabsModel", "eleven_multilingual_v2");
                s.BurnTextCards = MiniJson.GetBool(o, "burnTextCards", true);
                s.ShowNameplates = MiniJson.GetBool(o, "showNameplates", true);
                s.AllowRealBrands = MiniJson.GetBool(o, "allowRealBrands", true);
                s.DamageStrength = (float)Math.Max(0, Math.Min(3, MiniJson.GetDouble(o, "damageStrength", 1)));
                s.MusicEnabled = MiniJson.GetBool(o, "musicEnabled", true);
                s.MusicVolume = (float)Math.Max(0, Math.Min(1, MiniJson.GetDouble(o, "musicVolume", 0.6)));
                s.UseHdriSky = MiniJson.GetBool(o, "useHdriSky", true);
                s.CrowdEnabled = MiniJson.GetBool(o, "crowdEnabled", true);
                s.CrowdDensity = (float)Math.Max(0.3, Math.Min(1.5, MiniJson.GetDouble(o, "crowdDensity", 1)));
                s.CrowdVolume = (float)Math.Max(0, Math.Min(1, MiniJson.GetDouble(o, "crowdVolume", 0.7)));
                s.CrowdHeroShare = (float)Math.Max(0, Math.Min(1, MiniJson.GetDouble(o, "crowdHeroShare", 0.6)));
                s.HdriId = MiniJson.GetString(o, "hdriId", "kloofendal_48d_partly_cloudy_puresky");
                s.TtsVoice = MiniJson.GetString(o, "ttsVoice", "");
                s.GoogleTtsKey = MiniJson.GetString(o, "googleTtsKey", "");
                s.ElevenLabsKey = MiniJson.GetString(o, "elevenLabsKey", "");
                s.ElevenLabsVoiceId = MiniJson.GetString(o, "elevenLabsVoiceId", "");
                s.NarrationEnabled = MiniJson.GetBool(o, "narrationEnabled", true);
                s.SoundDesignEnabled = MiniJson.GetBool(o, "soundDesignEnabled", true);
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
