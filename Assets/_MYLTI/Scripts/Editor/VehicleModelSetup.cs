using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mylti.EditorTools
{
    /// <summary>
    /// One-time project setup for real 3D car models:
    /// • offers to install Unity glTFast (reads .glb/.gltf at runtime);
    /// • keeps glTF mesh data readable (GLTFAST_KEEP_MESH_DATA) so bodies can dent;
    /// • makes FBX models in Resources/Vehicles readable;
    /// • puts the glTF and panoramic-sky shaders into builds.
    /// </summary>
    [InitializeOnLoad]
    public static class VehicleModelSetup
    {
        const string Package = "com.unity.cloud.gltfast";
        const string Define = "GLTFAST_KEEP_MESH_DATA";
        const string AskedKey = "MYLTI.AskedGltfast";

        static VehicleModelSetup()
        {
            EditorApplication.delayCall += () =>
            {
                EnsureDefine();
                if (!EditorPrefs.GetBool(AskedKey, false) && !GltfInstalled())
                {
                    EditorPrefs.SetBool(AskedKey, true);
                    if (EditorUtility.DisplayDialog("MYLTI — xe 3D thật",
                        "Cài gói Unity glTFast để app đọc được mẫu xe .glb/.gltf (khuyên dùng)?\n\nCó thể cài sau qua menu MYLTI ▸ Install glTFast.",
                        "Cài ngay", "Để sau"))
                        InstallGltfast();
                }
            };
        }

        static bool GltfInstalled()
        {
            try { return System.IO.File.ReadAllText("Packages/manifest.json").Contains(Package); }
            catch (System.Exception) { return false; }
        }

        [MenuItem("MYLTI/Install glTFast (for .glb car models)", priority = 30)]
        public static void InstallGltfast()
        {
            UnityEditor.PackageManager.Client.Add(Package);
            Debug.Log("[MYLTI] Installing " + Package + " … Unity recompiles when it is done.");
        }

        static void EnsureDefine()
        {
            var target = NamedBuildTarget.Standalone;
            var defs = PlayerSettings.GetScriptingDefineSymbols(target);
            if (defs.Split(';').Contains(Define)) return;
            PlayerSettings.SetScriptingDefineSymbols(target, string.IsNullOrEmpty(defs) ? Define : defs + ";" + Define);
        }

        /// <summary>Add the shaders that are only found by name at runtime to Graphics ▸ Always Included Shaders.</summary>
        public static void IncludeRuntimeShaders()
        {
            var names = new[] { "Skybox/Panoramic", "Shader Graphs/glTF-pbrMetallicRoughness", "Shader Graphs/glTF-unlit", "Shader Graphs/glTF-pbrSpecularGlossiness" };
            var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null) return;
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            var have = new HashSet<Object>();
            for (int i = 0; i < arr.arraySize; i++) have.Add(arr.GetArrayElementAtIndex(i).objectReferenceValue);
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (sh == null || have.Contains(sh)) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                have.Add(sh);
            }
            so.ApplyModifiedProperties();
        }
    }

    /// <summary>FBX/OBJ car models placed in Resources/Vehicles must keep CPU mesh data for denting.</summary>
    public sealed class VehicleModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Vehicles/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.isReadable = true;
            mi.addCollider = false;
        }
    }
}
