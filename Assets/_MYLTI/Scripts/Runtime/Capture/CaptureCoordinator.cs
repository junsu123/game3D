using System;
using System.IO;
using System.Threading.Tasks;
using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>Implemented in the Editor assembly (Unity Recorder). Runtime code never references UnityEditor.</summary>
    public interface IRecorderBridge
    {
        string Name { get; }
        bool IsRecording { get; }
        bool Start(string outputPathWithoutExtension, int fps, int width, int height, bool captureAudio, out string error);
        /// <summary>Stops recording and returns the expected output file path (with extension).</summary>
        string Stop();
    }

    public static class RecorderBridgeRegistry
    {
        public static IRecorderBridge Instance;
    }

    /// <summary>
    /// RecordingCoordinator + CaptureStatus (spec §17.3, §23, FR-14). Separates ChallengeFinished, CaptureStopped,
    /// CaptureVerified. Never marks a capture verified without inspecting the real file's duration.
    /// </summary>
    public sealed class CaptureCoordinator : MonoBehaviour
    {
        public AppSettings Settings;
        public BuiltInRecorder Recorder;
        public CaptureInfo Info { get; private set; } = new CaptureInfo();
        public string LastMessage => Info?.Message;
        public DateTime RecordingStartedUtc { get; private set; }

        ObsWebSocketClient obs;

        public static bool UnityRecorderAvailable => RecorderBridgeRegistry.Instance != null;

        public void Prepare(RunRecord run, string runFolder, double targetSeconds)
        {
            Info = run.Capture;
            Info.Backend = Settings.Backend;
            Info.Fps = Settings.CaptureFps;
            Info.TargetSeconds = targetSeconds;
            Info.ToleranceSeconds = 2;
            Info.Status = CaptureStatus.NotStarted;
            Info.Interrupted = false;
            Info.InterruptReason = null;
            Info.Video = null;
            Info.ExpectedPath = Settings.Backend == CaptureBackend.UnityRecorder ? Path.Combine(runFolder, "video", "mylti_" + run.RunId)
                              : Settings.Backend == CaptureBackend.BuiltInFfmpeg ? Path.Combine(runFolder, "video", "gameplay_silent.mp4") : null;
            if (Info.ExpectedPath != null) Directory.CreateDirectory(Path.GetDirectoryName(Info.ExpectedPath));
        }

        /// <summary>Starts the recorder. Returns false (with Info.Message) when recording could not start.</summary>
        public async Task<bool> BeginAsync()
        {
            RecordingStartedUtc = DateTime.UtcNow;
            switch (Info.Backend)
            {
                case CaptureBackend.None:
                    Info.Status = CaptureStatus.CapturePending;
                    Info.Message = "No recorder selected — simulation only. Content pack will be a draft.";
                    return true;

                case CaptureBackend.BuiltInFfmpeg:
                {
                    if (Recorder == null || !Recorder.StartRecording(Info.ExpectedPath, Info.Fps))
                    {
                        Info.Status = CaptureStatus.Failed;
                        Info.Message = Recorder?.Error ?? "Built-in recorder missing.";
                        return false;
                    }
                    Info.Status = CaptureStatus.Recording;
                    Info.Message = $"Recording 1920×1080 @ {Info.Fps} fps with the built-in recorder (FFmpeg). Picture only — UI is never recorded.";
                    return true;
                }

                case CaptureBackend.UnityRecorder:
                {
                    var bridge = RecorderBridgeRegistry.Instance;
                    if (bridge == null)
                    {
                        Info.Status = CaptureStatus.Failed;
                        Info.Message = "Unity Recorder is only available inside the Unity Editor with package com.unity.recorder installed. In a Windows build use OBS.";
                        return false;
                    }
                    if (!bridge.Start(Info.ExpectedPath, Info.Fps, 1920, 1080, Settings.EnableAudio, out string err))
                    {
                        Info.Status = CaptureStatus.Failed;
                        Info.Message = "Unity Recorder failed to start: " + err;
                        return false;
                    }
                    Info.Status = CaptureStatus.Recording;
                    Info.Message = "Recording with Unity Recorder (constant " + Info.Fps + " fps).";
                    return true;
                }

                case CaptureBackend.ObsWebSocket:
                {
                    obs?.Dispose();
                    obs = new ObsWebSocketClient();
                    bool ok = await obs.ConnectAsync(Settings.ObsHost, Settings.ObsPort, Settings.ObsPassword);
                    if (!ok) { Info.Status = CaptureStatus.Failed; Info.Message = obs.LastError; return false; }
                    var r = await obs.StartRecordAsync();
                    if (r == null) { Info.Status = CaptureStatus.Failed; Info.Message = obs.LastError; return false; }
                    Info.Status = CaptureStatus.Recording;
                    Info.Message = "OBS is recording (started via WebSocket).";
                    return true;
                }

                case CaptureBackend.ObsManual:
                    Info.Status = CaptureStatus.Recording;
                    Info.Message = "Manual OBS: the app cannot see OBS. Start/stop OBS yourself, then point to the file to verify.";
                    return true;
            }
            return false;
        }

        public void MarkInterrupted(string reason)
        {
            if (Info == null || Info.Interrupted) return;
            Info.Interrupted = true;
            Info.InterruptReason = reason;
        }

        /// <summary>Stops the recorder and verifies the file when the app can locate it.</summary>
        public async Task EndAsync()
        {
            string path = null;
            switch (Info.Backend)
            {
                case CaptureBackend.None:
                    Info.Status = CaptureStatus.CapturePending;
                    return;
                case CaptureBackend.BuiltInFfmpeg:
                    path = Recorder != null ? Recorder.StopRecording() : null;
                    Info.Status = CaptureStatus.Stopped;
                    if (path == null) Info.Message = "Built-in recorder failed: " + Recorder?.Error;
                    else if (Recorder.FramesRepeated > 0) Info.Message = $"{Recorder.FramesRepeated} frame(s) repeated because GPU readback failed.";
                    break;
                case CaptureBackend.UnityRecorder:
                    path = RecorderBridgeRegistry.Instance?.Stop();
                    Info.Status = CaptureStatus.Stopped;
                    await Task.Delay(1500); // let the encoder finalise the container
                    break;
                case CaptureBackend.ObsWebSocket:
                    path = obs != null ? await obs.StopRecordAsync() : null;
                    if (path == null) Info.Message = "OBS did not report an output path: " + obs?.LastError;
                    Info.Status = CaptureStatus.Stopped;
                    obs?.Dispose(); obs = null;
                    await Task.Delay(2000);
                    break;
                case CaptureBackend.ObsManual:
                    Info.Status = CaptureStatus.CapturePending;
                    Info.Message = "Stop OBS now, then press 'Find newest video' or paste the file path and press Verify.";
                    if (!string.IsNullOrEmpty(Settings.ObsRecordingFolder))
                    {
                        await Task.Delay(2500);
                        path = VideoFileInspector.FindNewestVideo(Settings.ObsRecordingFolder, RecordingStartedUtc.AddSeconds(-30));
                        if (path == null) return;
                    }
                    else return;
                    break;
            }
            if (path == null) { Info.Status = CaptureStatus.VideoMissing; Info.Message = Info.Message ?? "No output file path."; return; }
            Verify(path);
        }

        /// <summary>Inspect a file and classify it. Safe to call again later (e.g. after OBS remux).</summary>
        public CaptureStatus Verify(string path)
        {
            Info.ExpectedPath = path;
            var v = VideoFileInspector.Inspect(path);
            Info.Video = v;
            var status = VideoFileInspector.Classify(v, Info.TargetSeconds, Info.ToleranceSeconds, out string msg);
            if (status == CaptureStatus.CaptureVerified && Info.Interrupted)
            {
                status = CaptureStatus.Interrupted;
                msg = "File duration OK but the take was interrupted (" + Info.InterruptReason + "). Review the footage or re-record.";
            }
            Info.Status = status;
            Info.Message = msg;
            return status;
        }

        /// <summary>Operator confirms they reviewed an interrupted take and accept it.</summary>
        public void AcceptInterruptedTake()
        {
            if (Info.Status != CaptureStatus.Interrupted || Info.Video == null) return;
            Info.Interrupted = false;
            Verify(Info.Video.Path);
        }

        public async Task<string> TestObsAsync()
        {
            using (var c = new ObsWebSocketClient())
            {
                bool ok = await c.ConnectAsync(Settings.ObsHost, Settings.ObsPort, Settings.ObsPassword);
                if (!ok) return "✗ " + c.LastError;
                var rec = await c.IsRecordingAsync();
                return "✓ Connected to OBS" + (rec == true ? " (currently recording!)" : "");
            }
        }

        void OnDestroy()
        {
            obs?.Dispose();
            if (RecorderBridgeRegistry.Instance != null && RecorderBridgeRegistry.Instance.IsRecording) RecorderBridgeRegistry.Instance.Stop();
        }
    }
}
