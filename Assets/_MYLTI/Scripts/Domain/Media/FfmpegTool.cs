using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    /// <summary>
    /// Locates (or downloads once) FFmpeg and runs it. FFmpeg is a separate open-source program (GPL build from
    /// github.com/BtbN/FFmpeg-Builds); it is downloaded to the user's data folder, not bundled in the game.
    /// </summary>
    public sealed class FfmpegTool
    {
        public const string DownloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        public string ExePath { get; private set; }
        public string LastError { get; private set; }
        public bool Available => !string.IsNullOrEmpty(ExePath) && File.Exists(ExePath);
        readonly string installDir;
        string cachedEncoder;

        public FfmpegTool(string installDir, string preferredPath = null)
        {
            this.installDir = installDir;
            Locate(preferredPath);
        }

        public string InstalledExe => Path.Combine(installDir, "ffmpeg.exe");

        public bool Locate(string preferredPath = null)
        {
            foreach (var p in new[] { preferredPath, InstalledExe, Path.Combine(installDir, "bin", "ffmpeg.exe") })
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) { ExePath = p; return true; }
            // PATH
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    foreach (var name in new[] { "ffmpeg.exe", "ffmpeg" })
                    {
                        string c = Path.Combine(dir.Trim('"'), name);
                        if (File.Exists(c)) { ExePath = c; return true; }
                    }
                }
                catch (ArgumentException) { }
            }
            ExePath = null;
            return false;
        }

        /// <summary>Downloads the FFmpeg zip (~100–200 MB) and extracts ffmpeg.exe. progress: 0..1.</summary>
        public async Task<bool> DownloadAsync(Action<float, string> progress, CancellationToken ct = default)
        {
            LastError = null;
            Directory.CreateDirectory(installDir);
            string zipPath = Path.Combine(installDir, "ffmpeg_download.zip");
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
                using (var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? -1;
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = File.Create(zipPath))
                    {
                        var buf = new byte[1 << 16];
                        long done = 0; int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            done += n;
                            progress?.Invoke(total > 0 ? (float)done / total : 0f, $"FFmpeg {done / 1048576} MB" + (total > 0 ? $" / {total / 1048576} MB" : ""));
                        }
                    }
                }
                progress?.Invoke(1f, "Extracting ffmpeg.exe…");
                using (var zfs = File.OpenRead(zipPath))
                using (var zip = new ZipArchive(zfs, ZipArchiveMode.Read))
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) { LastError = "ffmpeg.exe not found inside the downloaded archive."; return false; }
                    using (var es = entry.Open())
                    using (var fs = File.Create(InstalledExe)) await es.CopyToAsync(fs).ConfigureAwait(false);
                }
                File.Delete(zipPath);
                ExePath = InstalledExe;
                return true;
            }
            catch (Exception e)
            {
                LastError = "FFmpeg download failed: " + e.GetBaseException().Message + ". You can also place ffmpeg.exe at " + InstalledExe;
                return false;
            }
        }

        public sealed class Result { public int ExitCode; public string StdErr; }

        /// <summary>Runs ffmpeg to completion (no stdin).</summary>
        public async Task<Result> RunAsync(string args, int timeoutMs = 600000)
        {
            if (!Available) return new Result { ExitCode = -1, StdErr = "ffmpeg not available" };
            var psi = new ProcessStartInfo(ExePath, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
            };
            using (var p = new Process { StartInfo = psi })
            {
                var err = new StringBuilder();
                p.ErrorDataReceived += (s, e) => { if (e.Data != null && err.Length < 20000) err.AppendLine(e.Data); };
                p.OutputDataReceived += (s, e) => { if (e.Data != null && err.Length < 20000) err.AppendLine(e.Data); };
                p.Start();
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                bool done = await Task.Run(() => p.WaitForExit(timeoutMs)).ConfigureAwait(false);
                if (!done) { try { p.Kill(); } catch (Exception) { } return new Result { ExitCode = -2, StdErr = "ffmpeg timed out\n" + err }; }
                p.WaitForExit();
                return new Result { ExitCode = p.ExitCode, StdErr = err.ToString() };
            }
        }

        /// <summary>Best available H.264 encoder (libx264 in GPL builds; Windows Media Foundation otherwise).</summary>
        public string VideoEncoderArgs()
        {
            if (cachedEncoder != null) return cachedEncoder;
            string enc = "-c:v libx264 -preset veryfast -crf 19";
            try
            {
                var r = RunAsync("-hide_banner -encoders", 15000).GetAwaiter().GetResult();
                if (!r.StdErr.Contains("libx264")) enc = r.StdErr.Contains("h264_mf") ? "-c:v h264_mf -b:v 12M" : "-c:v mpeg4 -q:v 3";
            }
            catch (Exception) { }
            cachedEncoder = enc;
            return enc;
        }

        public static string Q(string path) => "\"" + path.Replace("\"", "") + "\"";

        /// <summary>Start ffmpeg reading raw RGBA frames from stdin.</summary>
        public Process StartRawVideoEncoder(int width, int height, int fps, bool flipVertical, string outputMp4)
        {
            string vf = flipVertical ? "-vf vflip " : "";
            string args = $"-y -hide_banner -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -framerate {fps} -i - {vf}{VideoEncoderArgs()} -pix_fmt yuv420p -movflags +faststart {Q(outputMp4)}";
            var psi = new ProcessStartInfo(ExePath, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true
            };
            var p = new Process { StartInfo = psi };
            p.Start();
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) LastError = e.Data; };
            p.BeginErrorReadLine();
            return p;
        }
    }
}
