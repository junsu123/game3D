using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    /// <summary>
    /// Kokoro-82M (open-weight English TTS, Apache-2.0) run locally through sherpa-onnx (Apache-2.0, github.com/k2-fsa/sherpa-onnx).
    /// Free, offline after a one-time download (~130 MB), no key, no usage limit, commercial use allowed.
    /// Downloads into &lt;installDir&gt;: the sherpa-onnx Windows x64 binaries + the kokoro-int8-multi-lang-v1_0 model.
    /// </summary>
    public sealed class KokoroTts : ITtsProvider
    {
        public const string ModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_0.tar.bz2";
        public const string FallbackBinUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.5/sherpa-onnx-v1.13.5-win-x64-shared-MT-Release.tar.bz2";
        const string ReleasesApi = "https://api.github.com/repos/k2-fsa/sherpa-onnx/releases?per_page=20";

        /// <summary>English voices of kokoro-multi-lang-v1_0 (speaker id → name). a = American, b = British; f = female, m = male.</summary>
        public static readonly KeyValuePair<int, string>[] Voices =
        {
            new KeyValuePair<int, string>(3, "af_heart (US female — highest rated)"),
            new KeyValuePair<int, string>(2, "af_bella (US female, energetic)"),
            new KeyValuePair<int, string>(16, "am_michael (US male)"),
            new KeyValuePair<int, string>(14, "am_fenrir (US male, deep)"),
            new KeyValuePair<int, string>(18, "am_puck (US male, lively)"),
            new KeyValuePair<int, string>(15, "am_liam (US male)"),
            new KeyValuePair<int, string>(11, "am_adam (US male)"),
            new KeyValuePair<int, string>(13, "am_eric (US male)"),
            new KeyValuePair<int, string>(17, "am_onyx (US male, low)"),
            new KeyValuePair<int, string>(26, "bm_george (UK male)"),
            new KeyValuePair<int, string>(25, "bm_fable (UK male)"),
            new KeyValuePair<int, string>(27, "bm_lewis (UK male)"),
            new KeyValuePair<int, string>(24, "bm_daniel (UK male)"),
            new KeyValuePair<int, string>(21, "bf_emma (UK female)"),
        };

        public static string VoiceName(int sid) => Voices.FirstOrDefault(v => v.Key == sid).Value ?? ("speaker " + sid);
        static bool IsBritish(int sid) => sid >= 20 && sid <= 27;

        public string Name => "Kokoro (local, " + VoiceName(sid).Split(' ')[0] + ")";
        public string LastError { get; private set; }
        readonly string installDir;
        readonly int sid;
        public Action<float, string> Progress;

        public KokoroTts(string installDir, int speakerId)
        {
            this.installDir = installDir;
            sid = speakerId < 0 || speakerId > 52 ? 16 : speakerId;
        }

        // ------------------------------------------------------------------ install
        public string ModelDir => Path.Combine(installDir, "kokoro-int8-multi-lang-v1_0");
        public string ExePath
        {
            get
            {
                if (!Directory.Exists(installDir)) return null;
                return Directory.GetFiles(installDir, "sherpa-onnx-offline-tts.exe", SearchOption.AllDirectories).FirstOrDefault();
            }
        }
        string ModelFile => new[] { "model.int8.onnx", "model.onnx" }.Select(f => Path.Combine(ModelDir, f)).FirstOrDefault(File.Exists);
        public bool Installed => ExePath != null && ModelFile != null && File.Exists(Path.Combine(ModelDir, "voices.bin"));

        public async Task<bool> EnsureInstalledAsync(Action<string> log, CancellationToken ct = default)
        {
            if (Installed) return true;
            Directory.CreateDirectory(installDir);
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MYLTI-Challenge-Studio");
                try
                {
                    if (ExePath == null)
                    {
                        string url = await FindBinaryUrlAsync(http, log).ConfigureAwait(false) ?? FallbackBinUrl;
                        log?.Invoke("Kokoro: downloading sherpa-onnx (" + Path.GetFileName(url) + ")…");
                        if (!await DownloadAndExtractAsync(http, url, "sherpa", ct).ConfigureAwait(false)) return false;
                    }
                    if (ModelFile == null)
                    {
                        log?.Invoke("Kokoro: downloading the voice model (~130 MB, one time)…");
                        if (!await DownloadAndExtractAsync(http, ModelUrl, "model", ct).ConfigureAwait(false)) return false;
                    }
                }
                catch (Exception e) { LastError = "Kokoro install failed: " + e.GetBaseException().Message; return false; }
            }
            if (!Installed) { LastError = "Kokoro install incomplete (sherpa-onnx-offline-tts.exe or model files missing in " + installDir + ")."; return false; }
            return true;
        }

        /// <summary>Newest sherpa-onnx release that has a Windows x64 build with TTS (static preferred: no DLL issues).</summary>
        static async Task<string> FindBinaryUrlAsync(HttpClient http, Action<string> log)
        {
            try
            {
                string json = await http.GetStringAsync(ReleasesApi).ConfigureAwait(false);
                foreach (var rel in MiniJson.Arr(MiniJson.Parse(json)) ?? new List<object>())
                {
                    var names = new List<KeyValuePair<string, string>>();
                    foreach (var a in MiniJson.Arr(MiniJson.Get(MiniJson.Obj(rel), "assets")) ?? new List<object>())
                    {
                        var ao = MiniJson.Obj(a);
                        names.Add(new KeyValuePair<string, string>(MiniJson.GetString(ao, "name") ?? "", MiniJson.GetString(ao, "browser_download_url")));
                    }
                    string pick = PickBinary(names.Select(n => n.Key));
                    if (pick != null) return names.First(n => n.Key == pick).Value;
                }
            }
            catch (Exception e) { log?.Invoke("Kokoro: GitHub release lookup failed (" + e.GetBaseException().Message + "), using known version."); }
            return null;
        }

        /// <summary>Choose the Windows x64 CLI archive: static-MT-Release, else shared-MT-Release; never -no-tts / -lib / cuda.</summary>
        public static string PickBinary(IEnumerable<string> assetNames)
        {
            var ok = assetNames.Where(n => Regex.IsMatch(n, @"^sherpa-onnx-v[\d.]+-win-x64-(static|shared)-MT-Release\.tar\.bz2$")).ToList();
            return ok.FirstOrDefault(n => n.Contains("-static-")) ?? ok.FirstOrDefault();
        }

        async Task<bool> DownloadAndExtractAsync(HttpClient http, string url, string label, CancellationToken ct)
        {
            string archive = Path.Combine(installDir, label + "_download.tar.bz2");
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode) { LastError = $"Download {(int)resp.StatusCode}: {url}"; return false; }
                long total = resp.Content.Headers.ContentLength ?? -1;
                using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = File.Create(archive))
                {
                    var buf = new byte[1 << 16];
                    long done = 0; int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                        done += n;
                        Progress?.Invoke(total > 0 ? (float)done / total : 0f, $"{label} {done / 1048576} MB" + (total > 0 ? $" / {total / 1048576} MB" : ""));
                    }
                }
            }
            // Windows 10/11 ship bsdtar as tar.exe, which reads .tar.bz2
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            if (!File.Exists(tar)) tar = "tar";
            var psi = new ProcessStartInfo(tar, $"-xjf \"{archive}\" -C \"{installDir}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi))
            {
                var errTask = p.StandardError.ReadToEndAsync();
                var outTask = p.StandardOutput.ReadToEndAsync();
                await Task.Run(() => p.WaitForExit(15 * 60 * 1000)).ConfigureAwait(false);
                string err = await errTask.ConfigureAwait(false);
                await outTask.ConfigureAwait(false);
                if (!p.HasExited || p.ExitCode != 0) { LastError = "Extract failed: " + err.Trim(); return false; }
            }
            try { File.Delete(archive); } catch (IOException) { }
            return true;
        }

        // ------------------------------------------------------------------ synthesis
        public async Task<Dictionary<string, string>> SynthesizeAsync(IList<KeyValuePair<string, string>> lines, string outDir, Action<string> log)
        {
            var result = new Dictionary<string, string>();
            if (!await EnsureInstalledAsync(log).ConfigureAwait(false)) { log?.Invoke(LastError); return result; }
            Directory.CreateDirectory(outDir);
            string exe = ExePath, md = ModelDir;
            string lexicon = Path.Combine(md, IsBritish(sid) ? "lexicon-gb-en.txt" : "lexicon-us-en.txt");
            if (!File.Exists(lexicon)) lexicon = Path.Combine(md, "lexicon-us-en.txt");
            string binDir = Path.GetDirectoryName(exe);
            string libDir = Path.Combine(Path.GetDirectoryName(binDir) ?? binDir, "lib");
            int threads = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));
            string common = $"--kokoro-model=\"{ModelFile}\" --kokoro-voices=\"{Path.Combine(md, "voices.bin")}\" --kokoro-tokens=\"{Path.Combine(md, "tokens.txt")}\""
                          + (File.Exists(lexicon) ? $" --kokoro-lexicon=\"{lexicon}\"" : "")
                          + (Directory.Exists(Path.Combine(md, "espeak-ng-data")) ? $" --kokoro-data-dir=\"{Path.Combine(md, "espeak-ng-data")}\"" : "")
                          + $" --num-threads={threads} --sid={sid}";
            foreach (var l in lines)
            {
                string f = Path.Combine(outDir, l.Key + ".wav");
                string text = Speakable(l.Value);
                if (text.Length == 0) continue;
                var psi = new ProcessStartInfo(exe, common + $" --output-filename=\"{f}\" \"{text}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = binDir };
                psi.EnvironmentVariables["PATH"] = binDir + ";" + libDir + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");
                try
                {
                    using (var p = Process.Start(psi))
                    {
                        var errTask = p.StandardError.ReadToEndAsync();
                        var outTask = p.StandardOutput.ReadToEndAsync();
                        await Task.Run(() => p.WaitForExit(120000)).ConfigureAwait(false);
                        string err = await errTask.ConfigureAwait(false);
                        await outTask.ConfigureAwait(false);
                        if (File.Exists(f) && new FileInfo(f).Length > 1000) result[l.Key] = f;
                        else
                        {
                            LastError = "Kokoro failed on line " + l.Key + ": " + Tail(err);
                            log?.Invoke(LastError);
                            if (result.Count == 0) break; // broken install — stop early, caller falls back
                        }
                    }
                }
                catch (Exception e) { LastError = "Kokoro error: " + e.GetBaseException().Message; log?.Invoke(LastError); break; }
            }
            return result;
        }

        static string Tail(string s)
        {
            s = (s ?? "").Trim();
            return s.Length > 300 ? s.Substring(s.Length - 300) : s;
        }

        // ------------------------------------------------------------------ text normalisation
        /// <summary>
        /// Kokoro reads words through a pronunciation lexicon, so numbers and symbols are spelled out
        /// ("18.5 m" → "eighteen point five metres"). Also keeps the command line plain ASCII.
        /// </summary>
        public static string Speakable(string text)
        {
            string s = (text ?? "").Replace('—', ',').Replace('–', ',').Replace('’', '\'').Replace('‘', '\'')
                                   .Replace('“', ' ').Replace('”', ' ').Replace("…", "...").Replace('"', ' ');
            s = Regex.Replace(s, @"(\d+)\s*km/h", "$1 kilometres per hour");
            s = Regex.Replace(s, @"(\d+(?:\.\d+)?)\s*m\b", "$1 metres");
            s = Regex.Replace(s, @"(\d+)\s*%", "$1 percent");
            s = Regex.Replace(s, @"#(\d+)", "number $1");
            s = Regex.Replace(s, @"\b(\d+)(st|nd|rd|th)\b", m => Ordinal(int.Parse(m.Groups[1].Value)));
            s = Regex.Replace(s, @"\d+(?:\.\d+)?", m => NumberWords(m.Value));
            s = s.Replace("&", " and ").Replace("+", " plus ").Replace("/", " ");
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(c < 128 && c >= 32 ? c : ' ');
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        static readonly string[] Ones = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

        public static string NumberWords(string num)
        {
            var parts = num.Split('.');
            if (!long.TryParse(parts[0], out long whole) || whole > 999999999) return num;
            string w = Int(whole);
            if (parts.Length > 1 && parts[1].Length > 0)
                w += " point " + string.Join(" ", parts[1].Select(ch => Ones[ch - '0']));
            return w;
        }

        static string Int(long n)
        {
            if (n < 20) return Ones[n];
            if (n < 100) return Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");
            if (n < 1000) return Ones[n / 100] + " hundred" + (n % 100 > 0 ? " and " + Int(n % 100) : "");
            if (n < 1000000) return Int(n / 1000) + " thousand" + (n % 1000 > 0 ? (n % 1000 < 100 ? " and " : " ") + Int(n % 1000) : "");
            return Int(n / 1000000) + " million" + (n % 1000000 > 0 ? " " + Int(n % 1000000) : "");
        }

        static string Ordinal(int n)
        {
            string[] special = { "zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth",
                "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth" };
            if (n < 20) return special[n];
            string w = Int(n);
            if (n % 10 == 0 && n < 100) return w.Substring(0, w.Length - 1) + "ieth";
            var idx = w.LastIndexOf(' ');
            string last = idx < 0 ? w : w.Substring(idx + 1);
            int lastVal = Array.IndexOf(Ones, last);
            return lastVal > 0 ? (idx < 0 ? "" : w.Substring(0, idx + 1)) + special[lastVal] : w + "th";
        }
    }
}
