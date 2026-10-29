using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Prism.Automation;

/// <summary>
/// Localhost-only JSON endpoint that exposes <see cref="AutomationApi"/> to the Prism MCP bridge.
/// Security: binds 127.0.0.1 only, requires a random per-run token (stored in a per-user file), and rejects browser requests (Origin header).
/// Disable entirely with PRISM_AUTOMATION=0.
/// </summary>
public sealed class AutomationServer : IDisposable
{
    readonly AutomationApi _api;
    readonly HttpListener _listener = new();
    readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    readonly CancellationTokenSource _cts = new();
    public static string DiscoveryPath => Environment.GetEnvironmentVariable("PRISM_AUTOMATION_DISCOVERY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "automation.json");
    public int Port { get; }
    public string Token => _token;
    /// <summary>The running server, so the script runner can hand scripts its address.</summary>
    public static AutomationServer? Current { get; private set; }

    public AutomationServer(AutomationApi api)
    {
        _api = api; Current = this;
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    public static AutomationServer? TryStart(AutomationApi api)
    {
        if (Environment.GetEnvironmentVariable("PRISM_AUTOMATION") == "0") return null;
        try
        {
            var s = new AutomationServer(api);
            s._listener.Start();
            Directory.CreateDirectory(Path.GetDirectoryName(DiscoveryPath)!);
            File.WriteAllText(DiscoveryPath, JsonSerializer.Serialize(new { port = s.Port, token = s._token, pid = Environment.ProcessId }));
            // keep the MCP bridge's offline tool list in step with this build, so a client that lists tools before the app is running sees them all
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(DiscoveryPath)!, "mcp-tools-cache.json"), api.Describe().ToJsonString()); } catch { }
            _ = Task.Run(s.Loop);
            return s;
        }
        catch { return null; }   // automation is optional; never block the app
    }

    async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    async Task Handle(HttpListenerContext ctx)
    {
        var rq = ctx.Request; var rs = ctx.Response;
        try
        {
            if (!string.IsNullOrEmpty(rq.Headers["Origin"]) || rq.Headers["X-Prism-Token"] != _token) { rs.StatusCode = 403; Write(rs, new JsonObject { ["ok"] = false, ["error"] = "forbidden" }); return; }
            var path = rq.Url!.AbsolutePath;
            if (path == "/ping") { Write(rs, new JsonObject { ["ok"] = true, ["pid"] = Environment.ProcessId }); return; }
            if (path == "/tools") { Write(rs, new JsonObject { ["ok"] = true, ["tools"] = _api.Describe() }); return; }
            if (path == "/call" && rq.HttpMethod == "POST")
            {
                using var sr = new StreamReader(rq.InputStream, Encoding.UTF8);
                var body = JsonNode.Parse(await sr.ReadToEndAsync())!.AsObject();
                try
                {
                    var res = await _api.CallAsync(body["tool"]!.ToString(), body["args"] as JsonObject ?? []);
                    Write(rs, new JsonObject { ["ok"] = true, ["result"] = res });
                }
                catch (Exception e)
                {
                    Write(rs, new JsonObject { ["ok"] = false, ["error"] = (e is AggregateException ae ? ae.GetBaseException() : e).Message });
                }
                return;
            }
            rs.StatusCode = 404; Write(rs, new JsonObject { ["ok"] = false, ["error"] = "not found" });
        }
        catch { try { rs.Abort(); } catch { } }
    }

    static void Write(HttpListenerResponse rs, JsonNode body)
    {
        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
        rs.ContentType = "application/json"; rs.ContentLength64 = bytes.Length;
        rs.OutputStream.Write(bytes); rs.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { if (File.Exists(DiscoveryPath) && File.ReadAllText(DiscoveryPath).Contains($"\"port\":{Port}")) File.Delete(DiscoveryPath); } catch { }
    }
}
