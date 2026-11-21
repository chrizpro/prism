using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Prism.IntegrationTests;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/MCP.md">Busy-instance discovery.</seealso>
[Trait("Feature", "Automation")]
[Trait("Layer", "Integration")]
public class McpDiscoveryTests
{
    [Theory(DisplayName = "MCP-I001 | A slow ping from a live discovered process preserves its session instead of launching another app")]
    [InlineData("call_api")]
    [InlineData("app_status")]
    [Trait("TestId", "MCP-I001")]
    public async Task BusyDiscoveredProcessIsNotReplaced(string tool)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string folder = Path.Combine(root.FullName, "artifacts/tests/integration/MCP-I001", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var reserve = new TcpListener(IPAddress.Loopback, 0);
        reserve.Start(); int port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { break; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool ping = context.Request.Url!.AbsolutePath == "/ping";
                        if (ping) await Task.Delay(2500); // bridge ping budget is 1500 ms
                        string body = ping ? "{\"ok\":true}" : "{\"ok\":true,\"result\":{\"marker\":\"existing-session\"}}";
                        byte[] bytes = Encoding.UTF8.GetBytes(body);
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes);
                        context.Response.Close();
                    }
                    catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or IOException) { }
                });
            }
        });
        string discovery = Path.Combine(folder, "automation.json");
        string original = JsonSerializer.Serialize(new { port, token = "local-test-token", pid = Environment.ProcessId });
        await File.WriteAllTextAsync(discovery, original);
        string binary = Environment.GetEnvironmentVariable("PRISM_MCP") ?? Path.Combine(root.FullName, "Prism.Mcp/bin/Release/net10.0/Prism.Mcp.dll");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(binary);
        start.Environment["PRISM_AUTOMATION_DISCOVERY"] = discovery;
        // If the regression returns, use the existing runtime as a harmless launch sentinel.
        start.Environment["PRISM_EXE"] = Environment.ProcessPath!;
        using var bridge = Process.Start(start)!;
        var errors = bridge.StandardError.ReadToEndAsync();
        try
        {
            await bridge.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "Discovery test", version = "1" } } }));
            await bridge.StandardInput.FlushAsync();
            await bridge.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await bridge.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = tool, arguments = new { name = "get_state", arguments = new { } } } }));
            await bridge.StandardInput.FlushAsync();
            var response = JsonNode.Parse((await bridge.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!)!;
            var result = response["result"]!;
            Assert.NotEqual(true, (bool?)result["isError"]);
            string text = (string)result["content"]![0]!["text"]!;
            if (tool == "call_api") Assert.Contains("existing-session", text);
            else Assert.True((bool)JsonNode.Parse(text)!["running"]!);
            Assert.Equal(original, await File.ReadAllTextAsync(discovery));
            bridge.StandardInput.Close();
            await bridge.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.DoesNotContain("launching", await errors);
        }
        finally
        {
            if (!bridge.HasExited) bridge.Kill(entireProcessTree: true);
            await bridge.WaitForExitAsync();
            listener.Stop(); await server;
        }
    }
}
