using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Mylti.Domain
{
    /// <summary>
    /// Operator preset (spec §8). Serialized to JSON in a user-writable folder.
    /// Every value is clamped on load; unknown vehicles are dropped with a warning.
    /// </summary>
    public sealed class ScenarioConfig : IJsonWritable
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string ScenarioId = "megaramp_default_001";
        public string Name = "Mega Ramp Default";
        public string ChallengeType = "MegaRamp";
        public int Seed = 12345;
        public RoundSpec Sandbox = new RoundSpec { Index = 0, Title = "Sandbox", KickerHeight = 8, KickerAngleDeg = 20 };
        public List<Entrant> Entrants = new List<Entrant>();
        public string CameraPresetId = "cinematic_basic";

        // director
        public string DirectorMode = "AutoYouTube";
        public double TargetDurationSeconds = 600;
        public string PacingPresetId = "escalating_five_rounds_v1";
        public string Difficulty = "Progressive";
        public float AutoRecoverStuckSeconds = 8;
        public ContentLanguage Language = ContentLanguage.en;
        public string AudienceRegion = "Global";
        public string SeriesId = "VehicleVersus";

        // capture
        public string AspectRatio = "16:9";
        public int TargetFps = 30;
        public bool HideUi = true;
        public CaptureBackend Recorder = CaptureBackend.None;
        public string WinCondition = "FurthestValidLanding";

        public static ScenarioConfig CreateDefault()
        {
            var c = new ScenarioConfig();
            c.Entrants.Add(new Entrant { VehicleId = "sport_01", ColorHex = "#D12B38", Lane = 0, Mode = DriveMode.AI, TargetSpeedKmh = 150 });
            c.Entrants.Add(new Entrant { VehicleId = "truck_01", ColorHex = "#2C82D3", Lane = 1, Mode = DriveMode.AI, TargetSpeedKmh = 105 });
            return c;
        }

        public ScenarioConfig Clone()
        {
            var json = ToJson();
            return FromJson(json, out _);
        }

        // ------------------------------------------------------------ VALIDATION
        /// <summary>Clamp every value into a safe range. Returns warnings (empty = all good).</summary>
        public List<string> Validate()
        {
            var warnings = new List<string>();
            if (SchemaVersion > CurrentSchemaVersion)
                warnings.Add($"schemaVersion {SchemaVersion} is newer than supported ({CurrentSchemaVersion}); loading best-effort.");
            SchemaVersion = CurrentSchemaVersion;
            if (string.IsNullOrWhiteSpace(ScenarioId)) ScenarioId = "scenario_" + Seed;
            ScenarioId = SanitizeId(ScenarioId);
            if (string.IsNullOrWhiteSpace(Name)) Name = ScenarioId;
            if (ChallengeType != "MegaRamp") { warnings.Add($"challengeType '{ChallengeType}' not available in MVP → MegaRamp."); ChallengeType = "MegaRamp"; }

            if (Sandbox == null) Sandbox = new RoundSpec();
            ClampRound(Sandbox, warnings, "sandbox");

            if (Entrants == null) Entrants = new List<Entrant>();
            var cleaned = new List<Entrant>();
            foreach (var e in Entrants)
            {
                if (e == null) continue;
                var def = VehicleCatalog.Find(e.VehicleId);
                if (def == null) { warnings.Add($"Vehicle '{e.VehicleId}' does not exist — removed."); continue; }
                if (cleaned.Any(x => x.VehicleId == def.Id)) { warnings.Add($"Duplicate vehicle '{def.Id}' removed."); continue; }
                e.VehicleId = def.Id;
                if (!VehicleCatalog.TryParseHex(e.ColorHex, out _, out _, out _)) { warnings.Add($"Bad colour for {def.Id} → default."); e.ColorHex = def.DefaultColorHex; }
                e.TargetSpeedKmh = Clamp(e.TargetSpeedKmh <= 0 ? def.DefaultTargetSpeedKmh : e.TargetSpeedKmh, 30, def.TopSpeedKmh, warnings, $"{def.Id}.targetSpeedKmh");
                cleaned.Add(e);
                if (cleaned.Count == 3) break;
            }
            if (Entrants.Count > 3) warnings.Add("Max 3 vehicles — extra entries ignored.");
            if (cleaned.Count == 0)
            {
                warnings.Add("No valid vehicle — default Sports Car added.");
                var d = VehicleCatalog.Find("sport_01");
                cleaned.Add(new Entrant { VehicleId = d.Id, ColorHex = d.DefaultColorHex, TargetSpeedKmh = d.DefaultTargetSpeedKmh });
            }
            for (int i = 0; i < cleaned.Count; i++) cleaned[i].Lane = i;
            Entrants = cleaned;

            if (TargetDurationSeconds != 600 && (TargetDurationSeconds < 240 || TargetDurationSeconds > 600))
            {
                warnings.Add($"targetDurationSeconds {TargetDurationSeconds} → 600.");
                TargetDurationSeconds = 600;
            }
            AutoRecoverStuckSeconds = Clamp(AutoRecoverStuckSeconds, 3, 30, warnings, "autoRecoverStuckSeconds");
            if (TargetFps != 30 && TargetFps != 60) { warnings.Add($"targetFps {TargetFps} → 30."); TargetFps = 30; }
            if (AspectRatio != "16:9" && AspectRatio != "9:16") { warnings.Add($"aspectRatio {AspectRatio} → 16:9."); AspectRatio = "16:9"; }
            if (!ConceptGenerator.SeriesIds.Contains(SeriesId)) { warnings.Add($"series '{SeriesId}' unknown → VehicleVersus."); SeriesId = "VehicleVersus"; }
            return warnings;
        }

        public static void ClampRound(RoundSpec r, List<string> warnings, string label)
        {
            r.KickerHeight = Clamp(r.KickerHeight, 2, 30, warnings, label + ".kickerHeight");
            r.KickerAngleDeg = Clamp(r.KickerAngleDeg, 8, 40, warnings, label + ".kickerAngle");
            r.SpeedFactor = Clamp(r.SpeedFactor, 0.4f, 1.3f, warnings, label + ".speedFactor");
            r.RunwayLength = Clamp(r.RunwayLength, 160, 400, warnings, label + ".runwayLength");
            if (r.BridgeWidth != 0) r.BridgeWidth = Clamp(r.BridgeWidth, 3, 8, warnings, label + ".bridgeWidth");
            r.WallDistance = Clamp(r.WallDistance, 25, 200, warnings, label + ".wallDistance");
            r.TargetDistance = Clamp(r.TargetDistance, 20, 200, warnings, label + ".targetDistance");
            r.BudgetSeconds = Clamp(r.BudgetSeconds, 20, 200, warnings, label + ".budgetSeconds");
            r.CrashSpeedKmh = Clamp(r.CrashSpeedKmh, 30, 160, warnings, label + ".crashSpeedKmh");
            r.DropHeightM = Clamp(r.DropHeightM, 5, 60, warnings, label + ".dropHeightM");
            if (r.AttemptPasses < 1) r.AttemptPasses = 1;
            if (r.AttemptPasses > 3) r.AttemptPasses = 3;
            if (r.MaxExtraPasses < 0) r.MaxExtraPasses = 0;
            if (r.MaxExtraPasses > 2) r.MaxExtraPasses = 2;
        }

        private static float Clamp(float v, float min, float max, List<string> warnings, string name)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) { warnings?.Add($"{name} invalid → {min}."); return min; }
            if (v < min) { warnings?.Add($"{name} {v} < {min} → clamped."); return min; }
            if (v > max) { warnings?.Add($"{name} {v} > {max} → clamped."); return max; }
            return v;
        }

        public static string SanitizeId(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.Trim())
                sb.Append(char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '_');
            string r = sb.ToString().Trim('_');
            return r.Length == 0 ? "scenario" : (r.Length > 64 ? r.Substring(0, 64) : r);
        }

        // ------------------------------------------------------------- JSON IO
        public string ToJson() => MiniJson.Serialize(this);

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject()
                .Prop("schemaVersion", SchemaVersion).Prop("scenarioId", ScenarioId).Prop("name", Name)
                .Prop("challengeType", ChallengeType).Prop("seed", Seed);
            w.Key("ramp").BeginObject()
                .Prop("kickerHeightMeters", Sandbox.KickerHeight).Prop("inclineDegrees", Sandbox.KickerAngleDeg)
                .Prop("runwayLengthMeters", Sandbox.RunwayLength).Prop("speedFactor", Sandbox.SpeedFactor)
                .Prop("bridgeWidthMeters", Sandbox.BridgeWidth).Prop("landingRamp", Sandbox.LandingRamp)
                .Prop("obstacles", Sandbox.Obstacles.ToString()).Prop("wallDistanceMeters", Sandbox.WallDistance)
                .Prop("targetDistanceMeters", Sandbox.TargetDistance).Prop("rule", Sandbox.Rule.ToString())
                .Prop("headToHead", Sandbox.HeadToHead)
                .EndObject();
            w.Key("vehicles"); w.Value(Entrants);
            w.Prop("cameraPresetId", CameraPresetId);
            w.Key("director").BeginObject()
                .Prop("mode", DirectorMode).Prop("targetDurationSeconds", TargetDurationSeconds)
                .Prop("pacingPresetId", PacingPresetId).Prop("difficulty", Difficulty)
                .Prop("autoRecoverStuckSeconds", AutoRecoverStuckSeconds).Prop("language", Language.ToString())
                .Prop("audienceRegion", AudienceRegion).Prop("seriesId", SeriesId)
                .EndObject();
            w.Key("capture").BeginObject()
                .Prop("aspectRatio", AspectRatio).Prop("targetFps", TargetFps).Prop("hideUi", HideUi)
                .Prop("recorder", Recorder.ToString())
                .EndObject();
            w.Prop("winCondition", WinCondition);
            w.EndObject();
        }

        /// <summary>Parse + validate. Throws JsonException only for unreadable JSON.</summary>
        public static ScenarioConfig FromJson(string json, out List<string> warnings)
        {
            var root = MiniJson.Obj(MiniJson.Parse(json));
            if (root == null) throw new JsonException("Preset root must be a JSON object");
            var c = new ScenarioConfig();
            c.SchemaVersion = MiniJson.GetInt(root, "schemaVersion", 0);
            if (c.SchemaVersion <= 0) throw new JsonException("Missing or invalid schemaVersion");
            c.ScenarioId = MiniJson.GetString(root, "scenarioId", c.ScenarioId);
            c.Name = MiniJson.GetString(root, "name", c.Name);
            c.ChallengeType = MiniJson.GetString(root, "challengeType", c.ChallengeType);
            c.Seed = MiniJson.GetInt(root, "seed", c.Seed);

            var ramp = MiniJson.Obj(MiniJson.Get(root, "ramp"));
            if (ramp != null)
            {
                var s = c.Sandbox;
                s.KickerHeight = (float)MiniJson.GetDouble(ramp, "kickerHeightMeters", MiniJson.GetDouble(ramp, "takeoffHeightMeters", s.KickerHeight));
                s.KickerAngleDeg = (float)MiniJson.GetDouble(ramp, "inclineDegrees", s.KickerAngleDeg);
                s.RunwayLength = (float)MiniJson.GetDouble(ramp, "runwayLengthMeters", MiniJson.GetDouble(ramp, "lengthMeters", s.RunwayLength));
                s.SpeedFactor = (float)MiniJson.GetDouble(ramp, "speedFactor", s.SpeedFactor);
                s.BridgeWidth = (float)MiniJson.GetDouble(ramp, "bridgeWidthMeters", s.BridgeWidth);
                s.LandingRamp = MiniJson.GetBool(ramp, "landingRamp", s.LandingRamp);
                s.Obstacles = ParseEnum(MiniJson.GetString(ramp, "obstacles"), s.Obstacles);
                s.WallDistance = (float)MiniJson.GetDouble(ramp, "wallDistanceMeters", s.WallDistance);
                s.TargetDistance = (float)MiniJson.GetDouble(ramp, "targetDistanceMeters", MiniJson.GetDouble(ramp, "landingDistanceMeters", s.TargetDistance));
                s.Rule = ParseEnum(MiniJson.GetString(ramp, "rule"), s.Rule);
                s.HeadToHead = MiniJson.GetBool(ramp, "headToHead", s.HeadToHead);
            }

            var vehicles = MiniJson.Arr(MiniJson.Get(root, "vehicles"));
            if (vehicles != null)
            {
                foreach (var o in vehicles)
                {
                    var v = MiniJson.Obj(o);
                    if (v == null) continue;
                    c.Entrants.Add(new Entrant
                    {
                        VehicleId = MiniJson.GetString(v, "vehicleId"),
                        ColorHex = MiniJson.GetString(v, "colorHex"),
                        Lane = MiniJson.GetInt(v, "lane"),
                        Mode = ParseEnum(MiniJson.GetString(v, "mode"), DriveMode.AI),
                        TargetSpeedKmh = (float)MiniJson.GetDouble(v, "targetSpeedKmh", 0)
                    });
                }
            }
            c.CameraPresetId = MiniJson.GetString(root, "cameraPresetId", c.CameraPresetId);

            var dir = MiniJson.Obj(MiniJson.Get(root, "director"));
            if (dir != null)
            {
                c.DirectorMode = MiniJson.GetString(dir, "mode", c.DirectorMode);
                c.TargetDurationSeconds = MiniJson.GetDouble(dir, "targetDurationSeconds", c.TargetDurationSeconds);
                c.PacingPresetId = MiniJson.GetString(dir, "pacingPresetId", c.PacingPresetId);
                c.Difficulty = MiniJson.GetString(dir, "difficulty", c.Difficulty);
                c.AutoRecoverStuckSeconds = (float)MiniJson.GetDouble(dir, "autoRecoverStuckSeconds", c.AutoRecoverStuckSeconds);
                c.Language = ParseEnum(MiniJson.GetString(dir, "language"), c.Language);
                c.AudienceRegion = MiniJson.GetString(dir, "audienceRegion", c.AudienceRegion);
                c.SeriesId = MiniJson.GetString(dir, "seriesId", c.SeriesId);
            }
            var cap = MiniJson.Obj(MiniJson.Get(root, "capture"));
            if (cap != null)
            {
                c.AspectRatio = MiniJson.GetString(cap, "aspectRatio", c.AspectRatio);
                c.TargetFps = MiniJson.GetInt(cap, "targetFps", c.TargetFps);
                c.HideUi = MiniJson.GetBool(cap, "hideUi", c.HideUi);
                string rec = MiniJson.GetString(cap, "recorder", "None");
                c.Recorder = rec == "ExternalOBSOrEditorRecorder" ? CaptureBackend.None : ParseEnum(rec, CaptureBackend.None);
            }
            c.WinCondition = MiniJson.GetString(root, "winCondition", c.WinCondition);
            warnings = c.Validate();
            return c;
        }

        public static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            if (!string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out T v) && Enum.IsDefined(typeof(T), v)) return v;
            return fallback;
        }
    }

    /// <summary>File-backed preset store with corrupt-file handling.</summary>
    public sealed class PresetRepository
    {
        public readonly string Folder;
        public PresetRepository(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
        }

        public string PathFor(string scenarioId) => Path.Combine(Folder, ScenarioConfig.SanitizeId(scenarioId) + ".json");

        public string Save(ScenarioConfig c)
        {
            c.Validate();
            string path = PathFor(c.ScenarioId);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, c.ToJson(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return path;
        }

        public List<string> List()
        {
            if (!Directory.Exists(Folder)) return new List<string>();
            return Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(s => s).ToList();
        }

        /// <summary>Load a preset. On corrupt JSON the file is renamed *.corrupt and null is returned with an error.</summary>
        public ScenarioConfig Load(string scenarioId, out List<string> warnings, out string error)
        {
            warnings = new List<string>();
            error = null;
            string path = PathFor(scenarioId);
            if (!File.Exists(path)) { error = "Preset not found: " + path; return null; }
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                return ScenarioConfig.FromJson(text, out warnings);
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidCastException)
            {
                string bad = path + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".corrupt";
                try { File.Move(path, bad); } catch (IOException) { }
                error = $"Preset file is corrupt ({e.Message}). Moved to {Path.GetFileName(bad)}.";
                return null;
            }
            catch (IOException e)
            {
                error = "Could not read preset: " + e.Message;
                return null;
            }
        }

        public bool Delete(string scenarioId)
        {
            string path = PathFor(scenarioId);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }
}
