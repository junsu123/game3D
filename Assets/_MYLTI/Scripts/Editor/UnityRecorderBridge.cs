// Compiled only when com.unity.recorder (>= 4.0) is installed — see versionDefines in MYLTI.Editor.asmdef.
#if MYLTI_HAS_RECORDER
using System;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace Mylti.EditorTools
{
    /// <summary>
    /// Unity Recorder bridge (Editor only). Records the Game View to MP4 at a constant frame rate,
    /// so program time == media time. Not available in Windows builds — use OBS there.
    /// </summary>
    [InitializeOnLoad]
    public sealed class UnityRecorderBridge : IRecorderBridge
    {
        static UnityRecorderBridge()
        {
            RecorderBridgeRegistry.Instance = new UnityRecorderBridge();
        }

        RecorderController controller;
        RecorderControllerSettings controllerSettings;
        string expectedPath;

        public string Name => "Unity Recorder";
        public bool IsRecording => controller != null && controller.IsRecording();

        public bool Start(string outputPathWithoutExtension, int fps, int width, int height, bool captureAudio, out string error)
        {
            error = null;
            try
            {
                if (!EditorApplication.isPlaying) { error = "Enter Play Mode first."; return false; }
                controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                controller = new RecorderController(controllerSettings);

                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "MYLTI Movie Recorder";
                movie.Enabled = true;
                movie.EncoderSettings = new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High
                };
                movie.CaptureAudio = captureAudio;
                movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
                movie.OutputFile = outputPathWithoutExtension.Replace('\\', '/');

                controllerSettings.AddRecorderSettings(movie);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = fps;
                controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
                controllerSettings.CapFrameRate = true;
                RecorderOptions.VerboseMode = false;

                controller.PrepareRecording();
                if (!controller.StartRecording())
                {
                    error = "RecorderController.StartRecording() returned false (check the Console for Recorder errors).";
                    return false;
                }
                expectedPath = outputPathWithoutExtension + ".mp4";
                Debug.Log("[MYLTI] Unity Recorder started → " + expectedPath);
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        public string Stop()
        {
            try
            {
                if (controller != null && controller.IsRecording()) controller.StopRecording();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MYLTI] Recorder stop error: " + e.Message);
            }
            controller = null;
            return expectedPath;
        }
    }
}
#endif
