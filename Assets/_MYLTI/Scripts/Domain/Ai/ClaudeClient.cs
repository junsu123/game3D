using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    /// <summary>
    /// Minimal client for the Claude Messages API (https://api.anthropic.com/v1/messages).
    /// The API key is supplied by the user (Settings) or the ANTHROPIC_API_KEY environment variable;
    /// it is never stored in source. API usage is billed by Anthropic per token (separate from a Claude.ai plan).
    /// </summary>
    public sealed class ClaudeClient
    {
        public const string DefaultModel = "claude-sonnet-5-5";
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };

        public string ApiKey;
        public string Model = DefaultModel;
        public string LastError { get; private set; }

        public ClaudeClient(string apiKey, string model = null)
        {
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") : apiKey.Trim();
            if (!string.IsNullOrWhiteSpace(model)) Model = model.Trim();
        }

        public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

        /// <summary>Tests only: answer locally instead of calling the API.</summary>
        public Func<string, string, Task<string>> Responder;

        /// <summary>Single-turn completion. Returns the concatenated text blocks, or null on failure (see LastError).</summary>
        public async Task<string> CompleteAsync(string system, string user, int maxTokens = 4096, CancellationToken ct = default)
        {
            LastError = null;
            if (!HasKey) { LastError = "No Claude API key."; return null; }
            if (Responder != null) return await Responder(system, user).ConfigureAwait(false);
            var w = new JsonWriter(false);
            w.BeginObject().Prop("model", Model).Prop("max_tokens", maxTokens);
            if (!string.IsNullOrEmpty(system)) w.Prop("system", system);
            w.Key("messages").BeginArray().BeginObject().Prop("role", "user").Prop("content", user).EndObject().EndArray();
            w.EndObject();

            using (var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages"))
            {
                req.Headers.Add("x-api-key", ApiKey);
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Content = new StringContent(w.ToString(), Encoding.UTF8, "application/json");
                try
                {
                    using (var resp = await http.SendAsync(req, ct).ConfigureAwait(false))
                    {
                        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var root = MiniJson.Obj(MiniJson.Parse(body));
                        if (!resp.IsSuccessStatusCode)
                        {
                            var err = MiniJson.Obj(MiniJson.Get(root, "error"));
                            LastError = $"Claude API {(int)resp.StatusCode}: {MiniJson.GetString(err, "message", body)}";
                            return null;
                        }
                        var sb = new StringBuilder();
                        var content = MiniJson.Arr(MiniJson.Get(root, "content"));
                        if (content != null)
                            foreach (var block in content)
                            {
                                var b = MiniJson.Obj(block);
                                if (MiniJson.GetString(b, "type") == "text") sb.Append(MiniJson.GetString(b, "text"));
                            }
                        if (MiniJson.GetString(root, "stop_reason") == "max_tokens") LastError = "Response was cut off (max_tokens).";
                        return sb.ToString();
                    }
                }
                catch (Exception e)
                {
                    LastError = "Claude API request failed: " + e.GetBaseException().Message;
                    return null;
                }
            }
        }

        /// <summary>Pulls the outermost JSON object/array out of a model reply (tolerates ``` fences and prose).</summary>
        public static string ExtractJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int a = text.IndexOf('{'), b = text.IndexOf('[');
            int start = a < 0 ? b : b < 0 ? a : Math.Min(a, b);
            if (start < 0) return null;
            char open = text[start], close = open == '{' ? '}' : ']';
            int depth = 0; bool inStr = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                if (c == '"') inStr = true;
                else if (c == open) depth++;
                else if (c == close && --depth == 0) return text.Substring(start, i - start + 1);
            }
            return null;
        }
    }
}
