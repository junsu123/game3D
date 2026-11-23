using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mylti.EditorTools
{
    /// <summary>One-click project setup: creates the demo scene, build settings and player settings.</summary>
    public static class MyltiSetupMenu
    {
        public const string ScenePath = "Assets/_MYLTI/Scenes/MegaRampDemo.unity";

        [MenuItem("MYLTI/Setup Demo Scene", priority = 0)]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(60f, 25f, 200f);
                cam.transform.rotation = Quaternion.Euler(20f, -60f, 0f);
                cam.farClipPlane = 2500f;
            }
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                sun.intensity = 1.25f;
                sun.shadows = LightShadows.Soft;
            }
            var root = new GameObject("MYLTI_Studio");
            root.AddComponent<StudioApp>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            PlayerSettings.companyName = "MYLTI";
            VehicleModelSetup.IncludeRuntimeShaders();
            PlayerSettings.productName = "MYLTI Challenge Studio";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            Time.fixedDeltaTime = 0.02f;

            Debug.Log("[MYLTI] Demo scene created at " + ScenePath + " and added to Build Profiles. Press Play.");
        }

        [MenuItem("MYLTI/Open Data Folder (presets, runs, videos)", priority = 20)]
        public static void OpenData()
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }

        [MenuItem("MYLTI/Build Windows x64", priority = 40)]
        public static void BuildWindows()
        {
            // Build whatever scenes are enabled (the template's SampleScene works: the studio boots itself).
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Setup();
                scenes = new[] { ScenePath };
            }
            PlayerSettings.productName = "MYLTI Challenge Studio";
            PlayerSettings.runInBackground = true;
            string outDir = Path.GetFullPath("Builds/Windows");
            Directory.CreateDirectory(outDir);
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, "MYLTI Challenge Studio.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[MYLTI] Build succeeded: {opts.locationPathName} ({report.summary.totalSize / 1048576} MB)");
                EditorUtility.RevealInFinder(opts.locationPathName);
            }
            else Debug.LogError($"[MYLTI] Build {report.summary.result}: {report.summary.totalErrors} error(s). See Console.");
        }
    }
}
