using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Mylti.Domain
{
    /// <summary>
    /// Vehicles from 3D model files the channel owner adds (realistic cars). Each model file has a small
    /// "&lt;name&gt;.vehicle.json" next to it with its name, class and specs; the app writes a template the
    /// first time it sees a new model. Physics values are derived from the class + mass + power.
    /// </summary>
    public sealed class UserVehicleSpec
    {
        public string ModelRef;           // file path or res:Vehicles/Name
        public string JsonPath;
        public string Brand = "", Model = "", GenericName = "";
        public string Class = "sedan";
        public float MassKg, PowerKw, TopSpeedKmh, LengthM, YawDeg;
        public string ColorHex = "#B8BCC2";
        public bool Repaint;
        public string License = "";

        public static readonly string[] Classes = { "hatchback", "sedan", "sports", "supercar", "suv", "pickup", "van", "truck", "bus" };

        public string WriteJson()
        {
            var w = new JsonWriter(true);
            w.BeginObject().Prop("brand", Brand).Prop("model", Model).Prop("genericName", GenericName).Prop("class", Class)
             .Prop("massKg", MassKg).Prop("powerKw", PowerKw).Prop("topSpeedKmh", TopSpeedKmh).Prop("lengthM", LengthM)
             .Prop("yawDeg", YawDeg).Prop("color", ColorHex).Prop("repaint", Repaint).Prop("license", License)
             .Prop("_help", "brand/model: real names (shown only when 'real brand names' is on). genericName: used otherwise. class: " + string.Join("|", Classes) + ". yawDeg: 0/90/180/270 if the car faces the wrong way. lengthM: real length for scaling (0 = auto). license: where the model came from and its licence.")
             .EndObject();
            return w.ToString();
        }

        public static UserVehicleSpec Parse(string json, string modelRef, string jsonPath)
        {
            var o = MiniJson.Obj(MiniJson.Parse(json));
            var s = new UserVehicleSpec
            {
                ModelRef = modelRef, JsonPath = jsonPath,
                Brand = MiniJson.GetString(o, "brand", "").Trim(), Model = MiniJson.GetString(o, "model", "").Trim(),
                GenericName = MiniJson.GetString(o, "genericName", "").Trim(),
                Class = (MiniJson.GetString(o, "class", "sedan") ?? "sedan").Trim().ToLowerInvariant(),
                MassKg = (float)MiniJson.GetDouble(o, "massKg", 0), PowerKw = (float)MiniJson.GetDouble(o, "powerKw", 0),
                TopSpeedKmh = (float)MiniJson.GetDouble(o, "topSpeedKmh", 0), LengthM = (float)MiniJson.GetDouble(o, "lengthM", 0),
                YawDeg = (float)MiniJson.GetDouble(o, "yawDeg", 0), ColorHex = MiniJson.GetString(o, "color", "#B8BCC2"),
                Repaint = MiniJson.GetBool(o, "repaint", false), License = MiniJson.GetString(o, "license", ""),
            };
            if (!Classes.Contains(s.Class)) s.Class = "sedan";
            return s;
        }

        /// <summary>First-time template for a model file: guesses the class from the file name.</summary>
        public static UserVehicleSpec Template(string modelRef, string jsonPath)
        {
            string name = Path.GetFileNameWithoutExtension(modelRef.Replace("res:", ""));
            string lower = name.ToLowerInvariant();
            string cls = lower.Contains("truck") || lower.Contains("lorry") ? "truck"
                       : lower.Contains("pickup") || lower.Contains("hilux") || lower.Contains("ranger") || lower.Contains("f150") || lower.Contains("f-150") ? "pickup"
                       : lower.Contains("suv") || lower.Contains("jeep") || lower.Contains("cruiser") ? "suv"
                       : lower.Contains("bus") ? "bus" : lower.Contains("van") ? "van"
                       : lower.Contains("gt") || lower.Contains("sport") || lower.Contains("911") || lower.Contains("supra") ? "sports" : "sedan";
            var p = Preset(cls);
            var parts = name.Replace('_', ' ').Replace('-', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return new UserVehicleSpec
            {
                ModelRef = modelRef, JsonPath = jsonPath, Class = cls,
                Brand = parts.Length > 1 ? Title(parts[0]) : "", Model = parts.Length > 1 ? Title(string.Join(" ", parts.Skip(1))) : Title(name),
                GenericName = p.GenericName, MassKg = p.MassKg, PowerKw = p.PowerKw, TopSpeedKmh = p.TopSpeedKmh,
            };
        }

        static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        public sealed class ClassPreset
        {
            public string GenericName, BodyStyle, NounEn, NounVi;
            public float MassKg, PowerKw, TopSpeedKmh, Width, Height, Length, WheelRadius, ComY, Suspension, AntiRoll, AirStability, Downforce, Steer;
        }

        public static ClassPreset Preset(string cls)
        {
            switch (cls)
            {
                case "hatchback": return new ClassPreset { GenericName = "Hatchback", BodyStyle = "sport", NounEn = "hatchback", NounVi = "xe hatchback", MassKg = 1150, PowerKw = 90, TopSpeedKmh = 185, Width = 1.75f, Height = 1.45f, Length = 4.0f, WheelRadius = 0.31f, ComY = -0.1f, Suspension = 0.22f, AntiRoll = 8000, AirStability = 1.4f, Downforce = 1.6f, Steer = 32 };
                case "sports": return new ClassPreset { GenericName = "Sports Car", BodyStyle = "sport", NounEn = "sports car", NounVi = "xe thể thao", MassKg = 1450, PowerKw = 300, TopSpeedKmh = 280, Width = 1.85f, Height = 1.3f, Length = 4.5f, WheelRadius = 0.34f, ComY = -0.15f, Suspension = 0.2f, AntiRoll = 9500, AirStability = 1.6f, Downforce = 2.4f, Steer = 30 };
                case "supercar": return new ClassPreset { GenericName = "Supercar", BodyStyle = "sport", NounEn = "supercar", NounVi = "siêu xe", MassKg = 1500, PowerKw = 520, TopSpeedKmh = 320, Width = 2.0f, Height = 1.15f, Length = 4.6f, WheelRadius = 0.35f, ComY = -0.18f, Suspension = 0.18f, AntiRoll = 10000, AirStability = 1.7f, Downforce = 3.0f, Steer = 30 };
                case "suv": return new ClassPreset { GenericName = "SUV", BodyStyle = "suv", NounEn = "SUV", NounVi = "SUV", MassKg = 2100, PowerKw = 190, TopSpeedKmh = 200, Width = 1.95f, Height = 1.75f, Length = 4.8f, WheelRadius = 0.40f, ComY = 0.05f, Suspension = 0.3f, AntiRoll = 12000, AirStability = 0.9f, Downforce = 1.5f, Steer = 30 };
                case "pickup": return new ClassPreset { GenericName = "Pickup Truck", BodyStyle = "suv", NounEn = "pickup truck", NounVi = "xe bán tải", MassKg = 2200, PowerKw = 150, TopSpeedKmh = 175, Width = 1.9f, Height = 1.85f, Length = 5.3f, WheelRadius = 0.40f, ComY = 0.08f, Suspension = 0.32f, AntiRoll = 12000, AirStability = 0.85f, Downforce = 1.3f, Steer = 30 };
                case "van": return new ClassPreset { GenericName = "Van", BodyStyle = "suv", NounEn = "van", NounVi = "xe van", MassKg = 2300, PowerKw = 120, TopSpeedKmh = 160, Width = 2.0f, Height = 2.3f, Length = 5.3f, WheelRadius = 0.36f, ComY = 0.15f, Suspension = 0.28f, AntiRoll = 14000, AirStability = 0.7f, Downforce = 1.0f, Steer = 30 };
                case "truck": return new ClassPreset { GenericName = "Heavy Truck", BodyStyle = "truck", NounEn = "heavy truck", NounVi = "xe tải nặng", MassKg = 7500, PowerKw = 300, TopSpeedKmh = 120, Width = 2.5f, Height = 3.2f, Length = 8.0f, WheelRadius = 0.52f, ComY = 0.2f, Suspension = 0.3f, AntiRoll = 40000, AirStability = 0.4f, Downforce = 1.0f, Steer = 26 };
                case "bus": return new ClassPreset { GenericName = "Bus", BodyStyle = "truck", NounEn = "bus", NounVi = "xe buýt", MassKg = 12000, PowerKw = 220, TopSpeedKmh = 100, Width = 2.55f, Height = 3.2f, Length = 12f, WheelRadius = 0.5f, ComY = 0.3f, Suspension = 0.3f, AntiRoll = 60000, AirStability = 0.35f, Downforce = 0.8f, Steer = 24 };
                default: return new ClassPreset { GenericName = "Sedan", BodyStyle = "sport", NounEn = "sedan", NounVi = "xe sedan", MassKg = 1500, PowerKw = 140, TopSpeedKmh = 210, Width = 1.85f, Height = 1.45f, Length = 4.8f, WheelRadius = 0.33f, ComY = -0.1f, Suspension = 0.22f, AntiRoll = 9000, AirStability = 1.4f, Downforce = 1.8f, Steer = 31 };
            }
        }

        public static string IdFor(string modelRef)
            => "user_" + ScenarioConfig.SanitizeId(Path.GetFileNameWithoutExtension(modelRef.Replace("res:", ""))).ToLowerInvariant();

        /// <summary>Vehicle definition for the catalogue (physics from class/mass/power; exact size comes from the model).</summary>
        public VehicleDefinition ToDefinition(bool realBrandNames)
        {
            var p = Preset(Class);
            float mass = MassKg > 100 ? MassKg : p.MassKg;
            float power = PowerKw > 10 ? PowerKw : p.PowerKw;
            float top = TopSpeedKmh > 40 ? TopSpeedKmh : p.TopSpeedKmh;
            float r = p.WheelRadius;
            // drive force at ~80 km/h from power, split over 4 wheels (game approximation)
            float torquePerWheel = Math.Max(300f, power * 1000f / 22f * r / 4f);
            string real = (Brand + " " + Model).Trim();
            string generic = string.IsNullOrWhiteSpace(GenericName) ? p.GenericName : GenericName.Trim();
            string name = realBrandNames && real.Length > 0 ? real : generic;
            return new VehicleDefinition
            {
                Id = IdFor(ModelRef), IsUserModel = true, Brand = Brand, ModelName = Model, ModelFile = ModelRef,
                ModelYawDeg = YawDeg, ModelLengthM = LengthM, Repaint = Repaint, PowerKw = power, LicenseNote = License,
                DisplayNameEn = name, DisplayNameVi = name, ClassNounEn = p.NounEn, ClassNounVi = p.NounVi, BodyStyle = p.BodyStyle,
                DefaultColorHex = string.IsNullOrEmpty(ColorHex) ? "#B8BCC2" : ColorHex,
                MassKg = mass, Width = p.Width, Height = p.Height, Length = LengthM > 1 ? LengthM : p.Length,
                WheelRadius = r, Wheelbase = (LengthM > 1 ? LengthM : p.Length) * 0.6f, Track = p.Width * 0.85f, CenterOfMassY = p.ComY,
                MotorTorquePerWheel = torquePerWheel, BrakeTorque = mass * 2.4f, MaxSteerDeg = p.Steer,
                TopSpeedKmh = top, DefaultTargetSpeedKmh = Math.Min(top * 0.75f, 160f),
                SuspensionDistance = p.Suspension, AntiRoll = p.AntiRoll * mass / p.MassKg, AirStability = p.AirStability, Downforce = p.Downforce,
            };
        }

        /// <summary>
        /// Find model files under 'dir' (*.glb, *.gltf, one level of sub-folders) plus extra references (Resources prefabs),
        /// read or create their .vehicle.json files and register them in the catalogue.
        /// </summary>
        public static List<UserVehicleSpec> LoadAll(string dir, IEnumerable<string> extraRefs, bool realBrandNames, List<string> log)
        {
            var specs = new List<UserVehicleSpec>();
            VehicleCatalog.ClearUser();
            VehicleCatalog.RealBrandsAllowed = realBrandNames;
            try { Directory.CreateDirectory(dir); } catch (Exception e) { log?.Add("Vehicles folder: " + e.Message); return specs; }
            var refs = new List<string>();
            foreach (var pattern in new[] { "*.glb", "*.gltf" })
                refs.AddRange(Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly).Concat(
                              Directory.GetDirectories(dir).SelectMany(d => Directory.GetFiles(d, pattern, SearchOption.TopDirectoryOnly))));
            if (extraRefs != null) refs.AddRange(extraRefs);
            foreach (var r in refs.Distinct())
            {
                bool res = r.StartsWith("res:", StringComparison.Ordinal);
                string json = res ? Path.Combine(dir, Path.GetFileName(r.Substring(4)) + ".vehicle.json") : Path.ChangeExtension(r, ".vehicle.json");
                UserVehicleSpec spec;
                try
                {
                    if (File.Exists(json)) spec = Parse(File.ReadAllText(json, Encoding.UTF8), r, json);
                    else
                    {
                        spec = Template(r, json);
                        File.WriteAllText(json, spec.WriteJson(), new UTF8Encoding(false));
                        log?.Add($"New vehicle model: {Path.GetFileName(r)} — check {Path.GetFileName(json)} (name, class, mass, power).");
                    }
                }
                catch (Exception e) { log?.Add($"{Path.GetFileName(json)} unreadable: {e.Message}"); continue; }
                specs.Add(spec);
                VehicleCatalog.RegisterUser(spec.ToDefinition(realBrandNames));
            }
            return specs;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(JsonPath)) return;
            File.WriteAllText(JsonPath, WriteJson(), new UTF8Encoding(false));
        }

        public string Describe() => string.Format(CultureInfo.InvariantCulture, "{0} {1} · {2} · {3:0} kg · {4:0} kW · {5:0} km/h", Brand, Model, Class, MassKg, PowerKw, TopSpeedKmh).Trim();
    }
}
