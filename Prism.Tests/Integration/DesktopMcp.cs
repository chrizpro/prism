using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Prism.IntegrationTests;

internal sealed class DesktopMcp : IAsyncDisposable
{
    readonly Process bridge;
    readonly Task<string> stderr;
    readonly string discovery;
    readonly string output;
    int requestId;
    public string OutputDirectory => output;

    public DesktopMcp(string scenario, string? drawingTheme = null, int? recoveryIntervalMinutes = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx")))
            root = root.Parent;
        if (root == null)
            throw new DirectoryNotFoundException("Run the integration project from the Prism checkout.");

        string artifacts = Path.Combine(root.FullName, "artifacts", "tests", "integration");
        string bin = Environment.GetEnvironmentVariable("PRISM_INTEGRATION_BIN") ?? Path.Combine(artifacts, "bin");
        string desktop = Path.Combine(bin, "Prism.Desktop.exe");
        string mcp = Path.Combine(bin, "Prism.Mcp.dll");
        if (!File.Exists(desktop) || !File.Exists(mcp))
            throw new FileNotFoundException("Build Prism.Desktop and Prism.Mcp into artifacts/tests/integration/bin first.");

        output = Path.Combine(artifacts, scenario, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        discovery = Path.Combine(output, "automation.json");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(mcp);
        start.Environment["PRISM_EXE"] = desktop;
        start.Environment["PRISM_AUTOMATION_DISCOVERY"] = discovery;
        start.Environment["PRISM_AUTOMATION"] = "1";
        start.Environment["PRISM_RECOVERY_DIR"] = Path.Combine(output, "recovery");
        if (drawingTheme != null || recoveryIntervalMinutes != null)
        {
            string settings = Path.Combine(output, "settings.json");
            File.WriteAllText(settings, System.Text.Json.JsonSerializer.Serialize(new { Theme = drawingTheme ?? "Light", ShowHomeOnStart = false,
                AutoRecoveryEnabled = recoveryIntervalMinutes.HasValue, AutoRecoveryIntervalMinutes = recoveryIntervalMinutes ?? 5 }));
            start.Environment["PRISM_SETTINGS_FILE"] = settings;
            start.Environment["PRISM_NO_HOME"] = "1";
        }
        bridge = Process.Start(start) ?? throw new InvalidOperationException("Could not start Prism.Mcp.");
        stderr = bridge.StandardError.ReadToEndAsync();
    }

    public async Task Initialize()
    {
        await Request("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "Prism.IntegrationTests", version = "1" },
        });
        await bridge.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        var tools = await Request("tools/list", new { });
        if (!tools["tools"]!.AsArray().Any(tool => (string?)tool?["name"] == "call_api"))
            throw new InvalidOperationException("Prism.Mcp did not advertise call_api.");
    }

    public async Task<JsonNode> Call(string name, object? args = null)
    {
        var response = await Tool("call_api", new { name, arguments = args ?? new { } });
        return JsonNode.Parse(Text(response)) ?? throw new InvalidOperationException($"Missing result for {name}.");
    }

    public async Task Capture(string stage, bool viewport = true)
    {
        var idle = await Call("wait_idle", new { timeout_ms = 5000, extra_ms = 200 });
        if ((bool?)idle["settled"] != true)
            throw new InvalidOperationException("The camera did not settle.");
        var response = await Tool("call_api", new { name = "screenshot", arguments = new { viewport, max_width = viewport ? 1000 : 1500 } });
        var image = response["content"]!.AsArray().Single(item => (string?)item?["type"] == "image");
        byte[] png = Convert.FromBase64String((string)image!["data"]!);
        if ((string?)image["mimeType"] != "image/png" || !png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidOperationException("Screenshot did not contain a PNG.");
        await File.WriteAllBytesAsync(Path.Combine(output, stage + ".png"), png);
    }

    public async Task VerifyAuthentication()
    {
        var info = JsonNode.Parse(await File.ReadAllTextAsync(discovery))!;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        string endpoint = $"http://127.0.0.1:{info["port"]}/ping";
        using var missing = await http.GetAsync(endpoint);
        Xunit.Assert.Equal(System.Net.HttpStatusCode.Forbidden, missing.StatusCode);
        using var origin = new HttpRequestMessage(HttpMethod.Get, endpoint);
        origin.Headers.Add("X-Prism-Token", (string)info["token"]!);
        origin.Headers.Add("Origin", "http://example.invalid");
        using var crossOrigin = await http.SendAsync(origin);
        Xunit.Assert.Equal(System.Net.HttpStatusCode.Forbidden, crossOrigin.StatusCode);
    }

    async Task<JsonNode> Tool(string name, object arguments)
    {
        var response = await Request("tools/call", new { name, arguments });
        if ((bool?)response["isError"] == true)
            throw new InvalidOperationException(Text(response));
        return response;
    }

    static string Text(JsonNode response) =>
        (string)response["content"]!.AsArray().First(item => (string?)item?["type"] == "text")!["text"]!;

    async Task<JsonNode> Request(string method, object parameters)
    {
        int id = ++requestId;
        string request = System.Text.Json.JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        await bridge.StandardInput.WriteLineAsync(request);
        await bridge.StandardInput.FlushAsync();
        string? line = await bridge.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
        if (line == null)
            throw new InvalidOperationException("Prism.Mcp closed its output: " + await stderr);
        var envelope = JsonNode.Parse(line)!;
        if ((int?)envelope["id"] != id || envelope["result"] is not { } result)
            throw new InvalidOperationException($"Invalid MCP response to {method}: {envelope["error"]}");
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!bridge.HasExited && File.Exists(discovery))
                await Tool("app_close", new { });
        }
        finally
        {
            if (!bridge.HasExited)
                bridge.Kill(entireProcessTree: true);
            await bridge.WaitForExitAsync();
            bridge.Dispose();
            File.Delete(discovery);
        }
    }
}
