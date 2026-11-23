using System;
using System.Collections.Generic;

namespace Mylti.Domain
{
    /// <summary>
    /// Engine-agnostic vehicle definition. Built-in vehicles are generic placeholder designs; user vehicles
    /// come from 3D model files the channel owner adds (see UserVehicles). Units: metres, kilograms, newton-metres, km/h.
    /// </summary>
    public sealed class VehicleDefinition
    {
        public string Id;
        public string DisplayNameEn;
        public string DisplayNameVi;
        public string ClassNounEn;     // "sports car", "SUV", "heavy truck"
        public string ClassNounVi;
        public string BodyStyle;       // "sport" | "suv" | "truck" (used by the mesh factory)
        public string DefaultColorHex;

        public float MassKg;
        public float Width, Height, Length;
        public float WheelRadius, Wheelbase, Track;
        public float CenterOfMassY;        // local offset (negative = lower)
        public float MotorTorquePerWheel;  // Nm, all-wheel drive
        public float BrakeTorque;          // Nm per wheel
        public float MaxSteerDeg;
        public float TopSpeedKmh;
        public float DefaultTargetSpeedKmh;
        public float SuspensionDistance;
        public float AntiRoll;             // N per unit of travel difference
        public float AirStability;         // angular damping applied while airborne
        public float Downforce;            // N per (m/s)^2

        // ---- user 3D models (empty for the built-in placeholders)
        public bool IsUserModel;
        public string Brand = "";          // e.g. "Toyota" — shown in titles only when real brand names are enabled
        public string ModelName = "";      // e.g. "Hilux"
        public string ModelFile = "";      // absolute path to .glb/.gltf, or "res:Vehicles/Name" for a prefab in Resources
        public float ModelYawDeg;          // rotate the model so its nose points to +Z
        public float ModelLengthM;         // real length used to scale the model (0 = auto)
        public bool Repaint;               // false = keep the model's own paint
        public float PowerKw;
        public string LicenseNote = "";

        public string DisplayName(ContentLanguage lang) => lang == ContentLanguage.vi ? DisplayNameVi : DisplayNameEn;
        public string ClassNoun(ContentLanguage lang) => lang == ContentLanguage.vi ? ClassNounVi : ClassNounEn;
        public float PowerToWeight => MotorTorquePerWheel * 4f / WheelRadius / MassKg; // m/s² (approx.)
    }

    public static class VehicleCatalog
    {
        /// <summary>When true, the brand words of user vehicles may appear in titles, narration and cards.</summary>
        public static bool RealBrandsAllowed;

        public static void RegisterUser(VehicleDefinition d)
        {
            if (d == null || string.IsNullOrEmpty(d.Id)) return;
            d.IsUserModel = true;
            all.RemoveAll(x => string.Equals(x.Id, d.Id, StringComparison.OrdinalIgnoreCase));
            all.Add(d);
        }

        public static void ClearUser() => all.RemoveAll(x => x.IsUserModel);

        public static IEnumerable<VehicleDefinition> UserVehicles => all.FindAll(x => x.IsUserModel);

        /// <summary>Vehicles offered to the episode writers: the user's real models when there are at least two.</summary>
        public static IReadOnlyList<VehicleDefinition> Selectable
        {
            get
            {
                var user = all.FindAll(x => x.IsUserModel);
                return user.Count >= 2 ? user : (IReadOnlyList<VehicleDefinition>)all;
            }
        }

        /// <summary>All pairs and triples of selectable vehicles (capped) for the offline rotation.</summary>
        public static List<string[]> Lineups()
        {
            var ids = new List<string>();
            foreach (var d in Selectable) ids.Add(d.Id);
            var list = new List<string[]>();
            for (int i = 0; i < ids.Count; i++)
                for (int j = i + 1; j < ids.Count; j++)
                    list.Add(new[] { ids[i], ids[j] });
            for (int i = 0; i < ids.Count && list.Count < 40; i++)
                for (int j = i + 1; j < ids.Count; j++)
                    for (int k = j + 1; k < ids.Count && list.Count < 40; k++)
                        list.Add(new[] { ids[i], ids[j], ids[k] });
            return list;
        }

        /// <summary>A trademark word may be used when real brands are enabled and it belongs to a user vehicle.</summary>
        public static bool IsAllowedBrand(string trademarkLower)
        {
            if (!RealBrandsAllowed || string.IsNullOrEmpty(trademarkLower)) return false;
            foreach (var d in all)
                if (d.IsUserModel && ((d.Brand ?? "") + " " + (d.ModelName ?? "") + " " + (d.DisplayNameEn ?? "")).ToLowerInvariant().Contains(trademarkLower)) return true;
            return false;
        }

        private static readonly List<VehicleDefinition> all = new List<VehicleDefinition>
        {
            new VehicleDefinition
            {
                Id = "sport_01", DisplayNameEn = "Sports Car", DisplayNameVi = "Xe thể thao",
                ClassNounEn = "sports car", ClassNounVi = "xe thể thao", BodyStyle = "sport",
                DefaultColorHex = "#D12B38",
                MassKg = 1250, Width = 1.9f, Height = 1.15f, Length = 4.4f,
                WheelRadius = 0.34f, Wheelbase = 2.6f, Track = 1.62f, CenterOfMassY = -0.15f,
                MotorTorquePerWheel = 700, BrakeTorque = 3200, MaxSteerDeg = 30,
                TopSpeedKmh = 200, DefaultTargetSpeedKmh = 150,
                SuspensionDistance = 0.22f, AntiRoll = 9000, AirStability = 1.6f, Downforce = 2.2f
            },
            new VehicleDefinition
            {
                Id = "suv_01", DisplayNameEn = "SUV", DisplayNameVi = "Xe SUV",
                ClassNounEn = "SUV", ClassNounVi = "SUV", BodyStyle = "suv",
                DefaultColorHex = "#E8A33D",
                MassKg = 2000, Width = 2.0f, Height = 1.75f, Length = 4.7f,
                WheelRadius = 0.40f, Wheelbase = 2.8f, Track = 1.70f, CenterOfMassY = 0.05f,
                MotorTorquePerWheel = 900, BrakeTorque = 4200, MaxSteerDeg = 30,
                TopSpeedKmh = 170, DefaultTargetSpeedKmh = 125,
                SuspensionDistance = 0.30f, AntiRoll = 12000, AirStability = 0.9f, Downforce = 1.5f
            },
            new VehicleDefinition
            {
                Id = "truck_01", DisplayNameEn = "Heavy Truck", DisplayNameVi = "Xe tải nặng",
                ClassNounEn = "heavy truck", ClassNounVi = "xe tải nặng", BodyStyle = "truck",
                DefaultColorHex = "#2C82D3",
                MassKg = 3600, Width = 2.4f, Height = 2.9f, Length = 7.2f,
                WheelRadius = 0.55f, Wheelbase = 4.6f, Track = 2.0f, CenterOfMassY = 0.2f,
                MotorTorquePerWheel = 2000, BrakeTorque = 7000, MaxSteerDeg = 26,
                TopSpeedKmh = 140, DefaultTargetSpeedKmh = 105,
                SuspensionDistance = 0.35f, AntiRoll = 22000, AirStability = 0.45f, Downforce = 1.0f
            },
        };

        public static IReadOnlyList<VehicleDefinition> All => all;

        public static VehicleDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var v in all) if (string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        /// <summary>Human colour word for a hex colour (used in thumbnail prompts).</summary>
        public static string ColorName(string hex)
        {
            if (!TryParseHex(hex, out float r, out float g, out float b)) return "";
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float l = (max + min) / 2f;
            if (max - min < 0.12f) return l > 0.8f ? "white" : l < 0.2f ? "black" : "grey";
            float h;
            if (max == r) h = 60f * (((g - b) / (max - min)) % 6f);
            else if (max == g) h = 60f * (((b - r) / (max - min)) + 2f);
            else h = 60f * (((r - g) / (max - min)) + 4f);
            if (h < 0) h += 360f;
            if (h < 15 || h >= 345) return "red";
            if (h < 45) return "orange";
            if (h < 70) return "yellow";
            if (h < 165) return "green";
            if (h < 200) return "cyan";
            if (h < 255) return "blue";
            if (h < 290) return "purple";
            return "pink";
        }

        public static bool TryParseHex(string hex, out float r, out float g, out float b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex)) return false;
            string h = hex.Trim().TrimStart('#');
            if (h.Length != 6) return false;
            try
            {
                r = Convert.ToInt32(h.Substring(0, 2), 16) / 255f;
                g = Convert.ToInt32(h.Substring(2, 2), 16) / 255f;
                b = Convert.ToInt32(h.Substring(4, 2), 16) / 255f;
                return true;
            }
            catch (FormatException) { return false; }
        }
    }
}
