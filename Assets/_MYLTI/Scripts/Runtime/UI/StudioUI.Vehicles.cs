using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    public sealed partial class StudioUI
    {
        bool showVehicleHelp;
        int editVehicle = -1;

        /// <summary>Real 3D car models: where to put them, their specs, brand-name switch, damage strength.</summary>
        void UserVehiclesPanel()
        {
            var s = app.Settings;
            GUILayout.BeginVertical(box);
            GUILayout.Label("🚗 Xe 3D thật của bạn", h2);
            if (!VehicleModelLibrary.GltfAvailable)
                GUILayout.Label("Cần gói Unity glTFast để đọc file .glb: menu MYLTI ▸ Install glTFast (1 lần). File .fbx thì đặt vào Assets/_MYLTI/Resources/Vehicles.", warn);
            GUILayout.Label($"Thả file .glb/.gltf vào thư mục Vehicles rồi bấm Nạp lại. App tự xoay, chỉnh kích thước, tìm 4 bánh, gắn vật lý và móp méo. Hiện có {app.UserVehicles.Count} mẫu.", small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Mở thư mục Vehicles", GUILayout.Width(180))) OpenFolder(app.VehiclesDir);
            GUI.enabled = !app.Director.IsBusy && !app.Producer.Busy && !app.Models.Loading;
            if (GUILayout.Button(app.Models.Loading ? "Đang nạp…" : "Nạp lại xe", GUILayout.Width(140))) app.LoadUserVehicles(true);
            GUI.enabled = true;
            showVehicleHelp = GUILayout.Toggle(showVehicleHelp, " Lấy mẫu xe ở đâu?", GUILayout.Width(170));
            GUILayout.EndHorizontal();
            if (showVehicleHelp)
                GUILayout.Label(
                    "• Sketchfab (lọc Downloadable, định dạng glTF/GLB) — ghi lại giấy phép (CC-BY cần ghi tên tác giả: dán vào ô Giấy phép, app tự thêm vào mô tả video).\n" +
                    "• Unity Asset Store / CGTrader / TurboSquid: chọn loại cho phép dùng thương mại. Tránh mẫu ghi “Editorial use only” nếu kênh bật kiếm tiền.\n" +
                    "• Mẫu nên có bánh xe là các phần riêng tên chứa “wheel/tire/rim” để bánh quay được; 30k–300k tam giác là vừa.\n" +
                    "• Tên hãng xe là nhãn hiệu: app ghi rõ trong mô tả video đây là mô phỏng game và kênh không liên kết với hãng.", small);

            bool brands = GUILayout.Toggle(s.AllowRealBrands, " Dùng tên hãng thật trong tiêu đề, lời bình, chữ trên video (tắt = tên chung như “Pickup Truck”)");
            if (brands != s.AllowRealBrands) { s.AllowRealBrands = brands; app.SaveSettings(); app.LoadUserVehicles(true); }
            float dmg = Slider("Độ móp khi va chạm", s.DamageStrength, 0f, 2.5f, "0.0", "×");
            if (Math.Abs(dmg - s.DamageStrength) > 0.001f) { s.DamageStrength = dmg; VehicleDamage.Strength = dmg; }

            for (int i = 0; i < app.UserVehicles.Count; i++)
            {
                var u = app.UserVehicles[i];
                var def = VehicleCatalog.Find(UserVehicleSpec.IdFor(u.ModelRef));
                string state = app.Models.Templates.ContainsKey(def?.Id ?? "") ? "✓ đã nạp" : app.Models.Errors.TryGetValue(def?.Id ?? "", out var err) ? "✗ " + err : "…";
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{def?.DisplayNameEn} — {u.Describe()} · {state}", small, GUILayout.Width(560));
                if (GUILayout.Button(editVehicle == i ? "Đóng" : "Sửa", GUILayout.Width(60))) editVehicle = editVehicle == i ? -1 : i;
                GUILayout.EndHorizontal();
                if (def != null && app.Session.Vehicles.TryGetValue(def.Id, out var vc) && !string.IsNullOrEmpty(vc.ModelNotes))
                    GUILayout.Label("   " + vc.ModelNotes, vc.ModelNotes.Contains("not found") || vc.ModelNotes.Contains("backwards") ? warn : small);
                if (editVehicle != i) continue;
                u.Brand = Field("Hãng", u.Brand);
                u.Model = Field("Mẫu xe", u.Model);
                u.GenericName = Field("Tên chung", u.GenericName);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Loại", label, GUILayout.Width(160));
                foreach (var cls in UserVehicleSpec.Classes)
                    if (GUILayout.Toggle(u.Class == cls, cls, GUI.skin.button, GUILayout.Width(74))) u.Class = cls;
                GUILayout.EndHorizontal();
                u.MassKg = Slider("Khối lượng", u.MassKg <= 0 ? 1500 : u.MassKg, 800, 15000, "0", " kg");
                u.PowerKw = Slider("Công suất", u.PowerKw <= 0 ? 150 : u.PowerKw, 40, 900, "0", " kW");
                u.TopSpeedKmh = Slider("Tốc độ tối đa", u.TopSpeedKmh <= 0 ? 200 : u.TopSpeedKmh, 80, 400, "0", " km/h");
                u.LengthM = Slider("Chiều dài thật (0 = tự)", u.LengthM, 0, 14, "0.00", " m");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Xoay mẫu", label, GUILayout.Width(160));
                foreach (int y in new[] { 0, 90, 180, 270 })
                    if (GUILayout.Toggle(Mathf.RoundToInt(u.YawDeg) == y, y + "°", GUI.skin.button, GUILayout.Width(60))) u.YawDeg = y;
                GUILayout.EndHorizontal();
                u.Repaint = GUILayout.Toggle(u.Repaint, " Sơn lại theo màu đội (mặc định giữ màu gốc của mẫu)");
                u.License = Field("Giấy phép / tác giả", u.License, 520);
                if (GUILayout.Button("Lưu & nạp lại xe này", GUILayout.Width(200)))
                {
                    try { u.Save(); } catch (Exception e) { app.Msg("Lưu thất bại: " + e.Message); }
                    if (def != null) app.Models.Forget(def.Id);
                    app.LoadUserVehicles(true);
                    editVehicle = -1;
                }
            }
            GUILayout.EndVertical();
        }
    }
}
