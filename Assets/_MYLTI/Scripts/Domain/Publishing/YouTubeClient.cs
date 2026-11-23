using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    /// <summary>Saved Google sign-in for one channel (youtube_token.json in the user's data folder, never in the repo).</summary>
    public sealed class YouTubeToken : IJsonWritable
    {
        public string RefreshToken, AccessToken, ChannelId, ChannelTitle;
        public DateTime AccessExpiresUtc;

        public void WriteJson(JsonWriter w) => w.BeginObject().Prop("refreshToken", RefreshToken).Prop("channelId", ChannelId).Prop("channelTitle", ChannelTitle).EndObject();

        public static YouTubeToken Load(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var o = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)));
                var t = new YouTubeToken { RefreshToken = MiniJson.GetString(o, "refreshToken"), ChannelId = MiniJson.GetString(o, "channelId"), ChannelTitle = MiniJson.GetString(o, "channelTitle") };
                return string.IsNullOrEmpty(t.RefreshToken) ? null : t;
            }
            catch (Exception) { return null; }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, MiniJson.Serialize(this), new UTF8Encoding(false));
        }
    }

    /// <summary>Everything YouTube needs for one upload.</summary>
    public sealed class VideoUploadMeta
    {
        public string Title, Description, CategoryId = "20", DefaultLanguage = "en";
        public List<string> Tags = new List<string>();
        public DateTime? PublishAtUtc;
        public bool AutoPublish = true, NotifySubscribers = true, MadeForKids = false, ContainsSyntheticMedia = false;
        public Dictionary<string, KeyValuePair<string, string>> Localizations = new Dictionary<string, KeyValuePair<string, string>>();
    }

    public sealed class UploadResult
    {
        public bool Ok;
        public string VideoId, PrivacyStatus, PublishAt, UploadStatus, Error;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// YouTube Data API v3 over plain HTTPS: Google sign-in (OAuth 2.0 desktop flow with PKCE and a loopback
    /// redirect — the app never sees your password), resumable video upload with scheduled publishing
    /// (status.publishAt), translated titles/descriptions (localizations), custom thumbnail and English captions.
    /// </summary>
    public sealed class YouTubeClient
    {
        public const string Scopes = "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.force-ssl";
        static readonly HttpClient api = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };

        readonly string clientId, clientSecret, tokenPath;
        public YouTubeToken Token { get; private set; }
        public string LastError { get; private set; }
        public bool SignedIn => Token != null && !string.IsNullOrEmpty(Token.RefreshToken);

        public YouTubeClient(string clientId, string clientSecret, string tokenPath)
        {
            this.clientId = (clientId ?? "").Trim();
            this.clientSecret = (clientSecret ?? "").Trim();
            this.tokenPath = tokenPath;
            Token = YouTubeToken.Load(tokenPath);
        }

        public bool HasClient => clientId.Length > 0;

        // ------------------------------------------------------------------ sign-in
        public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string NewVerifier()
        {
            var b = new byte[48];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Base64Url(b);
        }

        public static string Challenge(string verifier)
        {
            using (var sha = SHA256.Create()) return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        public static string AuthUrl(string clientId, string redirect, string challenge, string state)
            => "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&access_type=offline&prompt=consent"
             + "&client_id=" + Uri.EscapeDataString(clientId) + "&redirect_uri=" + Uri.EscapeDataString(redirect)
             + "&scope=" + Uri.EscapeDataString(Scopes) + "&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + state;

        /// <summary>Opens the Google sign-in page in the browser and waits (up to 5 minutes) for the redirect.</summary>
        public async Task<bool> SignInAsync(Action<string> openBrowser, Action<string> log)
        {
            LastError = null;
            if (!HasClient) { LastError = "Missing OAuth client ID (Google Cloud Console → Clients → Desktop app)."; return false; }
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string redirect = "http://127.0.0.1:" + port;
                string verifier = NewVerifier(), state = NewVerifier().Substring(0, 16);
                openBrowser(AuthUrl(clientId, redirect, Challenge(verifier), state));
                log?.Invoke("Waiting for Google sign-in in the browser…");
                var accept = listener.AcceptTcpClientAsync();
                if (await Task.WhenAny(accept, Task.Delay(TimeSpan.FromMinutes(5))).ConfigureAwait(false) != accept) { LastError = "Sign-in timed out (5 min)."; return false; }
                string code = null, err = null, gotState = null;
                using (var client = accept.Result)
                using (var stream = client.GetStream())
                {
                    var buf = new byte[8192];
                    int n = await stream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                    string req = Encoding.ASCII.GetString(buf, 0, n);
                    string line = req.Split('\n')[0];
                    int q = line.IndexOf('?'), sp = line.LastIndexOf(" HTTP", StringComparison.Ordinal);
                    if (q > 0 && sp > q)
                        foreach (var kv in line.Substring(q + 1, sp - q - 1).Split('&'))
                        {
                            var p = kv.Split(new[] { '=' }, 2);
                            string v = p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "";
                            if (p[0] == "code") code = v; else if (p[0] == "error") err = v; else if (p[0] == "state") gotState = v;
                        }
                    bool ok = code != null && gotState == state;
                    string html = "<html><head><meta charset='utf-8'></head><body style='font-family:sans-serif;padding:40px'>"
                                + (ok ? "<h2>✓ Đã kết nối YouTube. Bạn có thể đóng tab này và quay lại app.</h2>" : "<h2>✗ Chưa kết nối: " + WebUtility.HtmlEncode(err ?? "sai state") + "</h2>")
                                + "</body></html>";
                    byte[] body = Encoding.UTF8.GetBytes(html);
                    byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
                    await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                }
                if (code == null || gotState != state) { LastError = "Google sign-in was not completed: " + (err ?? "state mismatch"); return false; }

                var form = new Dictionary<string, string>
                {
                    ["code"] = code, ["client_id"] = clientId, ["redirect_uri"] = redirect,
                    ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
                };
                if (clientSecret.Length > 0) form["client_secret"] = clientSecret;
                var tok = await PostForm("https://oauth2.googleapis.com/token", form).ConfigureAwait(false);
                if (tok == null) return false;
                string refresh = MiniJson.GetString(tok, "refresh_token");
                if (string.IsNullOrEmpty(refresh)) { LastError = "Google returned no refresh token."; return false; }
                Token = new YouTubeToken
                {
                    RefreshToken = refresh, AccessToken = MiniJson.GetString(tok, "access_token"),
                    AccessExpiresUtc = DateTime.UtcNow.AddSeconds(MiniJson.GetInt(tok, "expires_in", 3600) - 120)
                };
                await LoadChannelAsync().ConfigureAwait(false);
                Token.Save(tokenPath);
                log?.Invoke("YouTube connected: " + (Token.ChannelTitle ?? "(channel name unavailable)"));
                return true;
            }
            catch (Exception e) { LastError = "Sign-in error: " + e.GetBaseException().Message; return false; }
            finally { listener.Stop(); }
        }

        public void SignOut()
        {
            Token = null;
            try { if (File.Exists(tokenPath)) File.Delete(tokenPath); } catch (IOException) { }
        }

        async Task<Dictionary<string, object>> PostForm(string url, Dictionary<string, string> form)
        {
            using (var resp = await api.PostAsync(url, new FormUrlEncodedContent(form)).ConfigureAwait(false))
            {
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    LastError = $"Google token {(int)resp.StatusCode}: {Trim(body)}";
                    if (body.Contains("invalid_grant"))
                        LastError += " — the sign-in expired or was revoked. Sign in again; if this repeats every 7 days, set the OAuth consent screen to “In production” (Testing mode tokens last 7 days).";
                    return null;
                }
                return MiniJson.Obj(MiniJson.Parse(body));
            }
        }

        async Task<string> AccessTokenAsync()
        {
            if (!SignedIn) { LastError = "YouTube not connected."; return null; }
            if (!string.IsNullOrEmpty(Token.AccessToken) && DateTime.UtcNow < Token.AccessExpiresUtc) return Token.AccessToken;
            var form = new Dictionary<string, string> { ["client_id"] = clientId, ["refresh_token"] = Token.RefreshToken, ["grant_type"] = "refresh_token" };
            if (clientSecret.Length > 0) form["client_secret"] = clientSecret;
            var tok = await PostForm("https://oauth2.googleapis.com/token", form).ConfigureAwait(false);
            if (tok == null) return null;
            Token.AccessToken = MiniJson.GetString(tok, "access_token");
            Token.AccessExpiresUtc = DateTime.UtcNow.AddSeconds(MiniJson.GetInt(tok, "expires_in", 3600) - 120);
            return Token.AccessToken;
        }

        async Task<HttpResponseMessage> Send(HttpMethod method, string url, HttpContent content = null)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string at = await AccessTokenAsync().ConfigureAwait(false);
                if (at == null) return null;
                var req = new HttpRequestMessage(method, url) { Content = content };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", at);
                var resp = await api.SendAsync(req).ConfigureAwait(false);
                if ((int)resp.StatusCode == 401 && attempt == 0) { Token.AccessToken = null; if (content != null) return resp; resp.Dispose(); continue; } // bodies can't be resent
                return resp;
            }
            return null;
        }

        public async Task<bool> LoadChannelAsync()
        {
            using (var resp = await Send(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true").ConfigureAwait(false))
            {
                if (resp == null) return false;
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { LastError = $"channels.list {(int)resp.StatusCode}: {Trim(body)}"; return false; }
                var items = MiniJson.Arr(MiniJson.Get(MiniJson.Obj(MiniJson.Parse(body)), "items"));
                if (items == null || items.Count == 0) { LastError = "This Google account has no YouTube channel."; return false; }
                var it = MiniJson.Obj(items[0]);
                Token.ChannelId = MiniJson.GetString(it, "id");
                Token.ChannelTitle = MiniJson.GetString(MiniJson.Obj(MiniJson.Get(it, "snippet")), "title");
                return true;
            }
        }

        // ------------------------------------------------------------------ metadata
        public static string CleanTitle(string t)
        {
            t = (t ?? "").Replace("<", "").Replace(">", "").Replace("\n", " ").Trim();
            return t.Length > 100 ? t.Substring(0, 97).TrimEnd() + "..." : t;
        }

        public static string CleanDescription(string d)
        {
            d = (d ?? "").Replace("<", "(").Replace(">", ")");
            var enc = Encoding.UTF8;
            if (enc.GetByteCount(d) <= 5000) return d;
            int len = d.Length;
            while (len > 0 && enc.GetByteCount(d.Substring(0, len)) > 4990) len -= 50;
            return d.Substring(0, Math.Max(0, len)).TrimEnd() + "…";
        }

        public static List<string> CleanTags(IEnumerable<string> tags)
        {
            var list = new List<string>();
            int total = 0;
            foreach (var raw in tags ?? Enumerable.Empty<string>())
            {
                string t = (raw ?? "").Replace("<", "").Replace(">", "").Replace(",", " ").Trim();
                if (t.Length == 0 || list.Contains(t, StringComparer.OrdinalIgnoreCase)) continue;
                int cost = t.Length + (t.Contains(" ") ? 2 : 0) + (list.Count > 0 ? 1 : 0);
                if (total + cost > 480) break;
                list.Add(t);
                total += cost;
            }
            return list;
        }

        /// <summary>The JSON video resource sent with the upload.</summary>
        public static string BuildVideoJson(VideoUploadMeta m)
        {
            var w = new JsonWriter(false);
            w.BeginObject();
            w.Key("snippet").BeginObject().Prop("title", CleanTitle(m.Title)).Prop("description", CleanDescription(m.Description))
             .Prop("categoryId", m.CategoryId).Prop("defaultLanguage", m.DefaultLanguage).Prop("defaultAudioLanguage", m.DefaultLanguage)
             .PropStrings("tags", CleanTags(m.Tags)).EndObject();
            w.Key("status").BeginObject();
            if (m.AutoPublish && m.PublishAtUtc.HasValue)
                w.Prop("privacyStatus", "private").Prop("publishAt", PublishItem.Iso(m.PublishAtUtc.Value));
            else
                w.Prop("privacyStatus", m.AutoPublish ? "public" : "private");
            w.Prop("selfDeclaredMadeForKids", m.MadeForKids).Prop("containsSyntheticMedia", m.ContainsSyntheticMedia).Prop("embeddable", true).EndObject();
            if (m.Localizations.Count > 0)
            {
                w.Key("localizations").BeginObject();
                foreach (var kv in m.Localizations)
                    w.Key(kv.Key).BeginObject().Prop("title", CleanTitle(kv.Value.Key)).Prop("description", CleanDescription(kv.Value.Value)).EndObject();
                w.EndObject();
            }
            w.EndObject();
            return w.ToString();
        }

        // ------------------------------------------------------------------ upload
        public async Task<UploadResult> UploadAsync(string videoPath, VideoUploadMeta meta, Action<float, string> progress, CancellationToken ct = default)
        {
            var r = new UploadResult();
            if (!File.Exists(videoPath)) { r.Error = "Video not found: " + videoPath; return r; }
            long total = new FileInfo(videoPath).Length;
            string parts = meta.Localizations.Count > 0 ? "snippet,status,localizations" : "snippet,status";
            string url = $"https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part={parts}&notifySubscribers={(meta.NotifySubscribers ? "true" : "false")}";
            var init = new StringContent(BuildVideoJson(meta), Encoding.UTF8, "application/json");
            init.Headers.Add("X-Upload-Content-Type", "video/mp4");
            init.Headers.Add("X-Upload-Content-Length", total.ToString(CultureInfo.InvariantCulture));
            string session;
            using (var resp = await Send(HttpMethod.Post, url, init).ConfigureAwait(false))
            {
                if (resp == null) { r.Error = LastError; return r; }
                if (!resp.IsSuccessStatusCode) { r.Error = await Describe(resp, "videos.insert").ConfigureAwait(false); return r; }
                session = resp.Headers.Location?.ToString();
                if (string.IsNullOrEmpty(session)) { r.Error = "YouTube did not return an upload session."; return r; }
            }

            const int chunk = 8 * 1024 * 1024;  // multiple of 256 KiB
            long offset = 0;
            int failures = 0;
            string finalBody = null;
            using (var fs = File.OpenRead(videoPath))
            {
                var buf = new byte[chunk];
                while (finalBody == null)
                {
                    ct.ThrowIfCancellationRequested();
                    HttpResponseMessage resp = null;
                    try
                    {
                        fs.Position = offset;
                        int n = 0, read;
                        while (n < chunk && (read = fs.Read(buf, n, chunk - n)) > 0) n += read;
                        var content = new ByteArrayContent(buf, 0, n);
                        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
                        content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + n - 1, total);
                        resp = await Send(HttpMethod.Put, session, content).ConfigureAwait(false);
                        if (resp == null) { r.Error = LastError; return r; }
                        int code = (int)resp.StatusCode;
                        if (code == 200 || code == 201) { finalBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); break; }
                        if (code == 308) { offset = NextOffset(resp, offset + n); failures = 0; progress?.Invoke((float)offset / total, $"{offset / 1048576}/{total / 1048576} MB"); continue; }
                        if (code >= 500) throw new IOException("server " + code);
                        r.Error = await Describe(resp, "upload").ConfigureAwait(false);
                        return r;
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        if (++failures > 6) { r.Error = "Upload failed after retries: " + e.GetBaseException().Message; return r; }
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 2 << failures)), ct).ConfigureAwait(false);
                        // ask YouTube how much it already has
                        var probe = new ByteArrayContent(new byte[0]);
                        probe.Headers.ContentRange = new ContentRangeHeaderValue(total);
                        using (var pr = await Send(HttpMethod.Put, session, probe).ConfigureAwait(false))
                        {
                            if (pr == null) continue;
                            if ((int)pr.StatusCode == 308) offset = NextOffset(pr, 0);
                            else if (pr.IsSuccessStatusCode) { finalBody = await pr.Content.ReadAsStringAsync().ConfigureAwait(false); break; }
                        }
                    }
                    finally { resp?.Dispose(); }
                }
            }

            var video = MiniJson.Obj(MiniJson.Parse(finalBody));
            r.VideoId = MiniJson.GetString(video, "id");
            var st = MiniJson.Obj(MiniJson.Get(video, "status"));
            r.PrivacyStatus = MiniJson.GetString(st, "privacyStatus");
            r.PublishAt = MiniJson.GetString(st, "publishAt");
            r.UploadStatus = MiniJson.GetString(st, "uploadStatus");
            r.Ok = !string.IsNullOrEmpty(r.VideoId);
            if (!r.Ok) r.Error = "Upload finished but no video id returned.";
            else if (meta.AutoPublish && meta.PublishAtUtc.HasValue && string.IsNullOrEmpty(r.PublishAt))
                r.Warnings.Add("YouTube did not keep the scheduled publish time — check the video in YouTube Studio (it stays private).");
            progress?.Invoke(1f, "uploaded");
            return r;
        }

        static long NextOffset(HttpResponseMessage resp, long fallback)
        {
            if (resp.Headers.TryGetValues("Range", out var vals))
            {
                string v = vals.FirstOrDefault() ?? "";
                int dash = v.LastIndexOf('-');
                if (dash > 0 && long.TryParse(v.Substring(dash + 1), out long last)) return last + 1;
            }
            return fallback;
        }

        public async Task<string> SetThumbnailAsync(string videoId, string jpgPath)
        {
            if (!File.Exists(jpgPath)) return "no thumbnail file";
            var content = new ByteArrayContent(File.ReadAllBytes(jpgPath));
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            using (var resp = await Send(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/thumbnails/set?uploadType=media&videoId=" + Uri.EscapeDataString(videoId), content).ConfigureAwait(false))
            {
                if (resp == null) return LastError;
                if (resp.IsSuccessStatusCode) return null;
                string err = await Describe(resp, "thumbnails.set").ConfigureAwait(false);
                if ((int)resp.StatusCode == 403) err += " — custom thumbnails need a verified channel (youtube.com/verify).";
                return err;
            }
        }

        public async Task<string> UploadCaptionsAsync(string videoId, string srtPath, string language = "en", string name = "English")
        {
            if (!File.Exists(srtPath)) return "no captions file";
            var w = new JsonWriter(false);
            w.BeginObject().Key("snippet").BeginObject().Prop("videoId", videoId).Prop("language", language).Prop("name", name).Prop("isDraft", false).EndObject().EndObject();
            var multi = new MultipartContent("related");
            multi.Add(new StringContent(w.ToString(), Encoding.UTF8, "application/json"));
            var file = new ByteArrayContent(File.ReadAllBytes(srtPath));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multi.Add(file);
            using (var resp = await Send(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/captions?uploadType=multipart&part=snippet", multi).ConfigureAwait(false))
            {
                if (resp == null) return LastError;
                return resp.IsSuccessStatusCode ? null : await Describe(resp, "captions.insert").ConfigureAwait(false);
            }
        }

        /// <summary>Current privacy / schedule of a video, e.g. "private, publishAt 2026-11-24T17:30:00Z".</summary>
        public async Task<string> GetStatusAsync(string videoId)
        {
            using (var resp = await Send(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/videos?part=status&id=" + Uri.EscapeDataString(videoId)).ConfigureAwait(false))
            {
                if (resp == null) return null;
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { LastError = Trim(body); return null; }
                var items = MiniJson.Arr(MiniJson.Get(MiniJson.Obj(MiniJson.Parse(body)), "items"));
                if (items == null || items.Count == 0) return "not found";
                var st = MiniJson.Obj(MiniJson.Get(MiniJson.Obj(items[0]), "status"));
                return $"{MiniJson.GetString(st, "uploadStatus")}, {MiniJson.GetString(st, "privacyStatus")}" + (MiniJson.GetString(st, "publishAt") is string p ? ", publishAt " + p : "");
            }
        }

        async Task<string> Describe(HttpResponseMessage resp, string what)
        {
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            string reason = "";
            try
            {
                var err = MiniJson.Obj(MiniJson.Get(MiniJson.Obj(MiniJson.Parse(body)), "error"));
                var first = MiniJson.Obj(MiniJson.Arr(MiniJson.Get(err, "errors"))?.FirstOrDefault());
                reason = MiniJson.GetString(first, "reason") ?? "";
                body = MiniJson.GetString(err, "message") ?? body;
            }
            catch (Exception) { }
            string hint = reason == "quotaExceeded" || reason == "uploadLimitExceeded" ? " (daily quota/limit reached — the app will retry tomorrow)" : "";
            return LastError = $"{what} {(int)resp.StatusCode} {reason}: {Trim(body)}{hint}";
        }

        static string Trim(string s) => s == null ? "" : (s.Length > 300 ? s.Substring(0, 300) : s);
    }
}
