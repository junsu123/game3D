using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    public enum TtsProviderKind { WindowsVoice, GoogleCloud, ElevenLabs, None }

    /// <summary>Text-to-speech: each provider writes one audio file per line (wav or mp3) into a folder.</summary>
    public interface ITtsProvider
    {
        string Name { get; }
        /// <summary>Returns id → audio file path. Missing ids failed (see LastError).</summary>
        Task<Dictionary<string, string>> SynthesizeAsync(IList<KeyValuePair<string, string>> lines, string outDir, Action<string> log);
        string LastError { get; }
    }

    public static class TtsFactory
    {
        public static ITtsProvider Create(AppSettings s)
        {
            switch (s.TtsProvider)
            {
                case TtsProviderKind.GoogleCloud: return new GoogleCloudTts(s.GoogleTtsKey, s.TtsVoice);
                case TtsProviderKind.ElevenLabs: return new ElevenLabsTts(s.ElevenLabsKey, s.ElevenLabsVoiceId);
                case TtsProviderKind.WindowsVoice: return new WindowsVoiceTts(s.TtsVoice);
                default: return null;
            }
        }
    }

    /// <summary>
    /// Voices built into Windows (System.Speech via PowerShell). Free, offline, no key, unlimited.
    /// Quality is basic ("Microsoft David/Zira"); installed natural voices are used if SAPI exposes them.
    /// </summary>
    public sealed class WindowsVoiceTts : ITtsProvider
    {
        public string Name => "Windows built-in voice";
        public string LastError { get; private set; }
        readonly string voice;
        public int Rate = 1; // -10..10

        public WindowsVoiceTts(string voice) { this.voice = string.IsNullOrWhiteSpace(voice) || voice.StartsWith("en-US-") ? "" : voice; }

        const string Script = @"param([string]$inFile, [string]$outDir, [string]$voice, [int]$rate)
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
if ($voice) { try { $s.SelectVoice($voice) } catch { } } else {
  $en = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'en-*' } | Select-Object -First 1
  if ($en) { $s.SelectVoice($en.VoiceInfo.Name) } }
$s.Rate = $rate
foreach ($line in [IO.File]::ReadAllLines($inFile, [Text.Encoding]::UTF8)) {
  $p = $line.Split([char]9, 2)
  if ($p.Length -lt 2) { continue }
  $s.SetOutputToWaveFile((Join-Path $outDir ($p[0] + '.wav')))
  $s.Speak($p[1])
}
$s.SetOutputToNull()
$s.Dispose()";

        public async Task<Dictionary<string, string>> SynthesizeAsync(IList<KeyValuePair<string, string>> lines, string outDir, Action<string> log)
        {
            var result = new Dictionary<string, string>();
            Directory.CreateDirectory(outDir);
            string ps1 = Path.Combine(outDir, "_tts.ps1"), input = Path.Combine(outDir, "_lines.txt");
            File.WriteAllText(ps1, Script, new UTF8Encoding(true));
            File.WriteAllLines(input, lines.Select(l => l.Key + "\t" + l.Value.Replace("\t", " ").Replace("\n", " ")), new UTF8Encoding(false));
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{ps1}\" -inFile \"{input}\" -outDir \"{outDir}\" -voice \"{voice}\" -rate {Rate}")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string err = await Task.Run(() => p.StandardError.ReadToEnd()).ConfigureAwait(false);
                    await Task.Run(() => p.WaitForExit(600000)).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(err)) LastError = err.Trim();
                }
            }
            catch (Exception e)
            {
                LastError = "Windows voice unavailable: " + e.Message;
                return result;
            }
            foreach (var l in lines)
            {
                string f = Path.Combine(outDir, l.Key + ".wav");
                if (File.Exists(f) && new FileInfo(f).Length > 100) result[l.Key] = f;
            }
            if (result.Count < lines.Count) log?.Invoke($"Windows voice produced {result.Count}/{lines.Count} clips. {LastError}");
            return result;
        }

        public static async Task<List<string>> ListVoicesAsync()
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -Command \"Add-Type -AssemblyName System.Speech; (New-Object System.Speech.Synthesis.SpeechSynthesizer).GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Name + '|' + $_.VoiceInfo.Culture.Name }\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string o = await Task.Run(() => p.StandardOutput.ReadToEnd()).ConfigureAwait(false);
                    return o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }
            catch (Exception) { return new List<string>(); }
        }
    }

    /// <summary>Google Cloud Text-to-Speech (API key). Google publishes a monthly free tier; requires a Google Cloud project.</summary>
    public sealed class GoogleCloudTts : ITtsProvider
    {
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        readonly string key, voice;
        public string Name => "Google Cloud TTS";
        public string LastError { get; private set; }

        public GoogleCloudTts(string key, string voice)
        {
            this.key = key?.Trim();
            this.voice = string.IsNullOrWhiteSpace(voice) || !voice.StartsWith("en-") ? "en-US-Standard-J" : voice.Trim();
        }

        public async Task<Dictionary<string, string>> SynthesizeAsync(IList<KeyValuePair<string, string>> lines, string outDir, Action<string> log)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(key)) { LastError = "No Google Cloud API key."; return result; }
            Directory.CreateDirectory(outDir);
            string lang = voice.Substring(0, Math.Min(5, voice.Length));
            foreach (var l in lines)
            {
                var w = new JsonWriter(false);
                w.BeginObject().Key("input").BeginObject().Prop("text", l.Value).EndObject()
                 .Key("voice").BeginObject().Prop("languageCode", lang).Prop("name", voice).EndObject()
                 .Key("audioConfig").BeginObject().Prop("audioEncoding", "MP3").Prop("speakingRate", 1.05).EndObject().EndObject();
                try
                {
                    var resp = await http.PostAsync("https://texttospeech.googleapis.com/v1/text:synthesize?key=" + Uri.EscapeDataString(key),
                        new StringContent(w.ToString(), Encoding.UTF8, "application/json")).ConfigureAwait(false);
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) { LastError = $"Google TTS {(int)resp.StatusCode}: {Trim(body)}"; log?.Invoke(LastError); break; }
                    string b64 = MiniJson.GetString(MiniJson.Obj(MiniJson.Parse(body)), "audioContent");
                    string f = Path.Combine(outDir, l.Key + ".mp3");
                    File.WriteAllBytes(f, Convert.FromBase64String(b64));
                    result[l.Key] = f;
                }
                catch (Exception e) { LastError = "Google TTS error: " + e.GetBaseException().Message; log?.Invoke(LastError); break; }
            }
            return result;
        }

        static string Trim(string s) => s.Length > 300 ? s.Substring(0, 300) : s;
    }

    /// <summary>
    /// ElevenLabs (API key). Best voice quality. The free plan has a small monthly allowance and, per ElevenLabs' terms,
    /// no commercial licence — a paid plan is needed for monetised YouTube channels.
    /// </summary>
    public sealed class ElevenLabsTts : ITtsProvider
    {
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        readonly string key, voiceId;
        public string Model = "eleven_flash_v2_5";
        public string Name => "ElevenLabs";
        public string LastError { get; private set; }

        public ElevenLabsTts(string key, string voiceId)
        {
            this.key = key?.Trim();
            this.voiceId = string.IsNullOrWhiteSpace(voiceId) ? "JBFqnCBsd6RMkjVDRZzb" : voiceId.Trim();
        }

        public async Task<Dictionary<string, string>> SynthesizeAsync(IList<KeyValuePair<string, string>> lines, string outDir, Action<string> log)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(key)) { LastError = "No ElevenLabs API key."; return result; }
            Directory.CreateDirectory(outDir);
            foreach (var l in lines)
            {
                var w = new JsonWriter(false);
                w.BeginObject().Prop("text", l.Value).Prop("model_id", Model).EndObject();
                using (var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voiceId)}"))
                {
                    req.Headers.Add("xi-api-key", key);
                    req.Headers.Add("Accept", "audio/mpeg");
                    req.Content = new StringContent(w.ToString(), Encoding.UTF8, "application/json");
                    try
                    {
                        var resp = await http.SendAsync(req).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            LastError = $"ElevenLabs {(int)resp.StatusCode}: {(body.Length > 300 ? body.Substring(0, 300) : body)}";
                            log?.Invoke(LastError);
                            break;
                        }
                        string f = Path.Combine(outDir, l.Key + ".mp3");
                        File.WriteAllBytes(f, await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
                        result[l.Key] = f;
                    }
                    catch (Exception e) { LastError = "ElevenLabs error: " + e.GetBaseException().Message; log?.Invoke(LastError); break; }
                }
            }
            return result;
        }
    }
}
