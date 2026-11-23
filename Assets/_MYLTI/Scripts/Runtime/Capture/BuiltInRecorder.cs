using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Mylti.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mylti
{
    /// <summary>
    /// Built-in screen recorder (Editor AND Windows build, no OBS):
    /// the main camera renders into a 1920×1080 RenderTexture, every frame is read back with AsyncGPUReadback
    /// and piped as raw RGBA into FFmpeg (H.264 MP4). Time.captureFramerate locks the game to the video frame
    /// rate, so the file has exactly one frame per program frame (frame-exact 10:00, no stutter on slow PCs).
    /// Only the 3D picture is recorded — UI, HUD and IMGUI text are never in the video.
    /// </summary>
    public sealed class BuiltInRecorder : MonoBehaviour
    {
        public Camera Cam;
        public FfmpegTool Ffmpeg;
        public int Width = 1920, Height = 1080;
        public bool FlipVertical = true;

        public bool IsRecording { get; private set; }
        public long FramesWritten => Interlocked.Read(ref framesWritten);
        public long FramesRepeated { get; private set; }
        public string OutputPath { get; private set; }
        public string Error { get; private set; }

        RenderTexture rt;
        Process proc;
        Stream stdin;
        Thread writer;
        BlockingCollection<byte[]> queue;
        readonly ConcurrentBag<byte[]> pool = new ConcurrentBag<byte[]>();
        byte[] lastFrame;
        int pending;
        long framesWritten;
        float prevListenerVolume = 1f;
        int fps;

        public bool StartRecording(string outputMp4, int framesPerSecond)
        {
            Error = null;
            if (IsRecording) { Error = "Already recording."; return false; }
            if (Ffmpeg == null || !Ffmpeg.Available) { Error = "FFmpeg not found. Press 'Download FFmpeg' in the Auto Episode tab."; return false; }
            if (!SystemInfo.supportsAsyncGPUReadback) { Error = "This GPU/driver does not support AsyncGPUReadback."; return false; }
            if (Cam == null) Cam = Camera.main;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputMp4));
                fps = framesPerSecond;
                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "MYLTI_Capture" };
                rt.Create();
                Cam.targetTexture = rt;
                proc = Ffmpeg.StartRawVideoEncoder(Width, Height, fps, FlipVertical, outputMp4);
                stdin = proc.StandardInput.BaseStream;
                queue = new BlockingCollection<byte[]>(6);
                writer = new Thread(WriterLoop) { IsBackground = true, Name = "MYLTI ffmpeg writer" };
                writer.Start();
                OutputPath = outputMp4;
                framesWritten = 0;
                FramesRepeated = 0;
                lastFrame = null;
                Time.captureFramerate = fps;
                prevListenerVolume = AudioListener.volume;
                AudioListener.volume = 0f; // live audio is replaced by the offline soundtrack
                IsRecording = true;
                StartCoroutine(CaptureLoop());
                return true;
            }
            catch (Exception e)
            {
                Error = "Recorder start failed: " + e.Message;
                Cleanup();
                return false;
            }
        }

        void WriterLoop()
        {
            try
            {
                foreach (var buf in queue.GetConsumingEnumerable())
                {
                    stdin.Write(buf, 0, buf.Length);
                    Interlocked.Increment(ref framesWritten);
                    pool.Add(buf);
                }
            }
            catch (Exception e) { Error = "FFmpeg pipe closed: " + e.Message + " " + Ffmpeg.LastError; }
        }

        IEnumerator CaptureLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (IsRecording)
            {
                yield return eof;
                if (!IsRecording) break;
                if (pending > 3) AsyncGPUReadback.WaitAllRequests();
                pending++;
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, OnReadback);
            }
        }

        void OnReadback(AsyncGPUReadbackRequest req)
        {
            pending--;
            if (queue == null || queue.IsAddingCompleted) return;
            byte[] buf;
            if (req.hasError)
            {
                if (lastFrame == null) return;
                buf = Rent(lastFrame.Length);      // keep timing: repeat the previous frame instead of dropping one
                Buffer.BlockCopy(lastFrame, 0, buf, 0, lastFrame.Length);
                FramesRepeated++;
            }
            else
            {
                var data = req.GetData<byte>();
                buf = Rent(data.Length);
                data.CopyTo(buf);
                if (lastFrame == null || lastFrame.Length != buf.Length) lastFrame = new byte[buf.Length];
                Buffer.BlockCopy(buf, 0, lastFrame, 0, buf.Length);
            }
            try { queue.Add(buf); } // blocks when the encoder is behind; captureFramerate keeps the video timing exact
            catch (InvalidOperationException) { }
        }

        byte[] Rent(int len)
        {
            if (pool.TryTake(out var b) && b.Length == len) return b;
            return new byte[len];
        }

        /// <summary>Finish the file. Returns the MP4 path (or null on failure).</summary>
        public string StopRecording()
        {
            if (!IsRecording) return OutputPath;
            IsRecording = false;
            AsyncGPUReadback.WaitAllRequests();
            queue.CompleteAdding();
            writer.Join(120000);
            try { stdin.Flush(); stdin.Close(); } catch (Exception) { }
            bool exited = proc.WaitForExit(180000);
            if (!exited) { Error = "FFmpeg did not finish in time."; try { proc.Kill(); } catch (Exception) { } }
            else if (proc.ExitCode != 0) Error = "FFmpeg exit code " + proc.ExitCode + ": " + Ffmpeg.LastError;
            Cleanup();
            return Error == null ? OutputPath : null;
        }

        void Cleanup()
        {
            Time.captureFramerate = 0;
            AudioListener.volume = prevListenerVolume;
            if (Cam != null) Cam.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
            try { proc?.Dispose(); } catch (Exception) { }
            proc = null;
            queue = null;
        }

        void OnGUI()
        {
            // the camera renders off-screen while recording: show the same picture in the window
            if (!IsRecording || rt == null || Event.current.type != EventType.Repaint) return;
            GUI.depth = 100;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), rt, ScaleMode.ScaleToFit, false);
        }

        void OnDestroy()
        {
            if (IsRecording) StopRecording();
        }
    }
}
