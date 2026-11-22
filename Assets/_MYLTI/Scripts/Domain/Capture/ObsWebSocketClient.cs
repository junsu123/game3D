using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mylti.Domain
{
    /// <summary>
    /// Minimal obs-websocket v5 client (OBS 28+ has the server built in: Tools ▸ WebSocket Server Settings).
    /// Opt-in only. Used to start/stop recording exactly at program start/end and to get the output path.
    /// The password is never written to source; the app stores it in the user's settings file.
    /// </summary>
    public sealed class ObsWebSocketClient : IDisposable
    {
        ClientWebSocket ws;
        int requestCounter;
        public bool Connected => ws != null && ws.State == WebSocketState.Open;
        public string LastError { get; private set; }

        public async Task<bool> ConnectAsync(string host, int port, string password, int timeoutMs = 4000)
        {
            Dispose();
            ws = new ClientWebSocket();
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    await ws.ConnectAsync(new Uri($"ws://{host}:{port}"), cts.Token);
                    var hello = MiniJson.Obj(MiniJson.Parse(await ReceiveAsync(cts.Token)));
                    if (MiniJson.GetInt(hello, "op", -1) != 0) { LastError = "Unexpected first message from OBS"; return false; }
                    var d = MiniJson.Obj(MiniJson.Get(hello, "d"));
                    var auth = MiniJson.Obj(MiniJson.Get(d, "authentication"));
                    var identify = new JsonWriter(false);
                    identify.BeginObject().Prop("op", 1).Key("d").BeginObject().Prop("rpcVersion", 1).Prop("eventSubscriptions", 0);
                    if (auth != null)
                    {
                        if (string.IsNullOrEmpty(password)) { LastError = "OBS requires a password (Tools ▸ WebSocket Server Settings)"; return false; }
                        identify.Prop("authentication", ComputeAuth(password, MiniJson.GetString(auth, "salt"), MiniJson.GetString(auth, "challenge")));
                    }
                    identify.EndObject().EndObject();
                    await SendAsync(identify.ToString(), cts.Token);
                    var identified = MiniJson.Obj(MiniJson.Parse(await ReceiveAsync(cts.Token)));
                    if (MiniJson.GetInt(identified, "op", -1) != 2) { LastError = "OBS authentication failed"; return false; }
                    LastError = null;
                    return true;
                }
                catch (Exception e)
                {
                    LastError = "Cannot connect to OBS at ws://" + host + ":" + port + " — " + e.Message;
                    return false;
                }
            }
        }

        public static string ComputeAuth(string password, string salt, string challenge)
        {
            using (var sha = SHA256.Create())
            {
                string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
            }
        }

        /// <summary>Send a request and wait for its response (op 7). Returns responseData or null on failure.</summary>
        public async Task<Dictionary<string, object>> RequestAsync(string requestType, int timeoutMs = 5000)
        {
            if (!Connected) { LastError = "Not connected to OBS"; return null; }
            string id = "mylti_" + Interlocked.Increment(ref requestCounter);
            var w = new JsonWriter(false);
            w.BeginObject().Prop("op", 6).Key("d").BeginObject().Prop("requestType", requestType).Prop("requestId", id).EndObject().EndObject();
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    await SendAsync(w.ToString(), cts.Token);
                    while (true)
                    {
                        var msg = MiniJson.Obj(MiniJson.Parse(await ReceiveAsync(cts.Token)));
                        if (MiniJson.GetInt(msg, "op", -1) != 7) continue; // ignore events
                        var d = MiniJson.Obj(MiniJson.Get(msg, "d"));
                        if (MiniJson.GetString(d, "requestId") != id) continue;
                        var status = MiniJson.Obj(MiniJson.Get(d, "requestStatus"));
                        if (!MiniJson.GetBool(status, "result"))
                        {
                            LastError = $"{requestType} failed: {MiniJson.GetString(status, "comment", "code " + MiniJson.GetInt(status, "code"))}";
                            return null;
                        }
                        return MiniJson.Obj(MiniJson.Get(d, "responseData")) ?? new Dictionary<string, object>();
                    }
                }
                catch (Exception e)
                {
                    LastError = requestType + " error: " + e.Message;
                    return null;
                }
            }
        }

        public Task<Dictionary<string, object>> StartRecordAsync() => RequestAsync("StartRecord");

        /// <summary>Returns the output file path reported by OBS, or null.</summary>
        public async Task<string> StopRecordAsync()
        {
            var r = await RequestAsync("StopRecord", 15000);
            return r == null ? null : MiniJson.GetString(r, "outputPath");
        }

        public async Task<bool?> IsRecordingAsync()
        {
            var r = await RequestAsync("GetRecordStatus");
            if (r == null) return null;
            return MiniJson.GetBool(r, "outputActive");
        }

        async Task SendAsync(string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        async Task<string> ReceiveAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            using (var ms = new System.IO.MemoryStream())
            {
                while (true)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("OBS closed the connection");
                    ms.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        public void Dispose()
        {
            try { ws?.Abort(); ws?.Dispose(); } catch (Exception) { }
            ws = null;
        }
    }
}
