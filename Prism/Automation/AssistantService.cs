using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Prism.Automation;

public enum AiProvider { ClaudeCli, CodexCli, Anthropic, OpenAi }
public enum ChatRole { User, Assistant, Tool, Note, Error }

public sealed class AssistantSettings
{
    public AiProvider Provider { get; set; } = AiProvider.ClaudeCli;
    public string CliModel { get; set; } = "";
    public string AnthropicModel { get; set; } = "claude-sonnet-5-5";
    public string OpenAiModel { get; set; } = "gpt-5";
    public string AnthropicKey { get; set; } = "";
    public string OpenAiKey { get; set; } = "";
    public bool AllowFiles { get; set; }
    public bool RememberKeys { get; set; }
}

/// <summary>
/// The in-app AI assistant. It can use your Claude or Codex command-line login (nothing to paste), or an API key, and it works the CAD by calling
/// the same tools the MCP bridge offers: so everything it does is undoable and looks like interactive work.
/// </summary>
public sealed class AssistantService(AutomationApi api)
{
    public AssistantSettings Settings { get; } = Load();
    public event Action<ChatRole, string>? Said;
    public bool Busy { get; private set; }

    static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "assistant.json");

    static AssistantSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile)) return JsonSerializer.Deserialize<AssistantSettings>(File.ReadAllText(SettingsFile)) ?? new();
        }
        catch { }
        var s = new AssistantSettings();
        s.AnthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
        s.OpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            var copy = JsonSerializer.Deserialize<AssistantSettings>(JsonSerializer.Serialize(Settings))!;
            if (!Settings.RememberKeys) { copy.AnthropicKey = ""; copy.OpenAiKey = ""; }
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    const string SystemPrompt =
        "You are the assistant built into Prism, a desktop CAD program (Open CASCADE kernel). You work the program through tools: " +
        "call get_state first when you need to know what is in the document, and use run_command for modeling commands (box, cylinder, extrude, fillet, subtract, push_pull, move_hole, flatten, warp, cnc_profile, slice, ...). " +
        "Sizes are millimetres, Z is up, face and edge numbers are 1-based like the screen labels. " +
        "Work in small verified steps (check results with get_state / object_info, use screenshot to look). Everything you do can be undone with the Undo button, so say what you did. " +
        "Be brief. Ask before doing anything large or destructive, and never claim success without checking.";

    CancellationTokenSource? _cts;
    string? _claudeSession;
    readonly List<JsonObject> _history = [];

    public void Reset() { _claudeSession = null; _history.Clear(); }
    public void Cancel() { try { _cts?.Cancel(); } catch { } }

    IEnumerable<JsonNode?> Tools() => api.Describe().Where(t => t!["name"]!.ToString() != "assistant" && (Settings.AllowFiles || t["name"]!.ToString() != "file"));

    public async Task SendAsync(string text)
    {
        if (Busy || string.IsNullOrWhiteSpace(text)) return;
        Busy = true; _cts = new CancellationTokenSource();
        Said?.Invoke(ChatRole.User, text);
        try
        {
            switch (Settings.Provider)
            {
                case AiProvider.ClaudeCli: await RunClaudeCli(text, _cts.Token); break;
                case AiProvider.CodexCli: await RunCodexCli(text, _cts.Token); break;
                case AiProvider.Anthropic: await RunAnthropic(text, _cts.Token); break;
                default: await RunOpenAi(text, _cts.Token); break;
            }
        }
        catch (OperationCanceledException) { Said?.Invoke(ChatRole.Note, "Stopped."); }
        catch (Exception e) { Said?.Invoke(ChatRole.Error, e.Message); }
        finally { Busy = false; }
    }

    // ================================================================== command-line logins
    static string? FindOnPath(string name)
    {
        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : [""];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            foreach (var ext in exts)
            {
                try { var f = Path.Combine(dir.Trim('"'), name + ext); if (File.Exists(f)) return f; } catch { }
            }
        return null;
    }

    /// <summary>The MCP bridge that lets a command-line agent reach this running app.</summary>
    static (string command, string[] args)? McpBridge()
    {
        var env = Environment.GetEnvironmentVariable("PRISM_MCP");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ("dotnet", [env]) : (env, []);
        var dir = AppContext.BaseDirectory;
        for (int up = 0; up < 8 && dir != null; up++, dir = Path.GetDirectoryName(dir))
        {
            var packaged = Path.Combine(dir, "mcp", OperatingSystem.IsWindows() ? "Prism.Mcp.exe" : "Prism.Mcp");
            if (File.Exists(packaged)) return (packaged, []);
            foreach (var cand in new[] { Path.Combine(dir, "mcp", "Prism.Mcp.dll"), Path.Combine(dir, "Prism.Mcp", "bin", "Release", "net10.0", "Prism.Mcp.dll"), Path.Combine(dir, "Prism.Mcp", "bin", "Debug", "net10.0", "Prism.Mcp.dll") })
                if (File.Exists(cand)) return ("dotnet", [cand]);
        }
        return null;
    }

    async Task<int> RunProcess(string exe, IEnumerable<string> args, string stdin, Action<string> onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath(), StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + exe);
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var err = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) err.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        await p.StandardInput.WriteAsync(stdin); p.StandardInput.Close();
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null) onLine(line);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0 && err.Length > 0) Said?.Invoke(ChatRole.Error, err.ToString().Trim().Split('\n').Last().Trim());
        return p.ExitCode;
    }

    async Task RunClaudeCli(string text, CancellationToken ct)
    {
        var exe = FindOnPath("claude") ?? throw new InvalidOperationException("The Claude command-line tool was not found. Install Claude Code and run 'claude' once to log in, or choose another provider.");
        var bridge = McpBridge() ?? throw new InvalidOperationException("Could not find the Prism MCP bridge (Prism.Mcp). Build it with: dotnet build Prism.Mcp -c Release");
        var cfg = Path.Combine(Path.GetTempPath(), $"prism-mcp-{Environment.ProcessId}.json");
        File.WriteAllText(cfg, new JsonObject { ["mcpServers"] = new JsonObject { ["prism"] = new JsonObject { ["command"] = bridge.command, ["args"] = new JsonArray(bridge.args.Select(a => (JsonNode)a).ToArray()) } } }.ToJsonString());
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--mcp-config", cfg, "--strict-mcp-config", "--allowedTools", "mcp__prism", "--disallowedTools", "Bash", "Edit", "Write", "NotebookEdit", "WebFetch", "WebSearch", "Task", "Read", "Glob", "Grep" };
        args.Add("mcp__prism__assistant");
        if (!Settings.AllowFiles) { args.Add("mcp__prism__file"); }
        args.AddRange(["--append-system-prompt", SystemPrompt]);
        if (!string.IsNullOrWhiteSpace(Settings.CliModel)) args.AddRange(["--model", Settings.CliModel]);
        if (_claudeSession != null) args.AddRange(["--resume", _claudeSession]);
        bool said = false;
        await RunProcess(exe, args, text, line =>
        {
            JsonNode? n; try { n = JsonNode.Parse(line); } catch { return; }
            var type = n?["type"]?.ToString();
            if (type == "system" && n?["session_id"] != null) _claudeSession = n["session_id"]!.ToString();
            else if (type == "assistant")
            {
                foreach (var block in n!["message"]?["content"]?.AsArray() ?? [])
                {
                    var bt = block?["type"]?.ToString();
                    if (bt == "text" && !string.IsNullOrWhiteSpace(block!["text"]?.ToString())) { Said?.Invoke(ChatRole.Assistant, block["text"]!.ToString().Trim()); said = true; }
                    else if (bt == "tool_use" && block!["name"]?.ToString() != "ToolSearch") Said?.Invoke(ChatRole.Tool, ToolLine(block!["name"]?.ToString()?.Replace("mcp__prism__", "") ?? "tool", block["input"] as JsonObject));
                }
            }
            else if (type == "result")
            {
                if (n!["session_id"] != null) _claudeSession = n["session_id"]!.ToString();
                if (!said && n["result"]?.ToString() is { Length: > 0 } r) Said?.Invoke(ChatRole.Assistant, r);
                if (n["is_error"]?.GetValue<bool>() == true && n["result"] != null) Said?.Invoke(ChatRole.Error, n["result"]!.ToString());
            }
        }, ct);
        try { File.Delete(cfg); } catch { }
    }

    async Task RunCodexCli(string text, CancellationToken ct)
    {
        var exe = FindOnPath("codex") ?? throw new InvalidOperationException("The Codex command-line tool was not found. Install it and run 'codex login', or choose another provider.");
        var bridge = McpBridge() ?? throw new InvalidOperationException("Could not find the Prism MCP bridge (Prism.Mcp). Build it with: dotnet build Prism.Mcp -c Release");
        var args = new List<string> { "exec", "--json", "--skip-git-repo-check", "--sandbox", "read-only",
            "-c", "mcp_servers.prism.default_tools_approval_mode=\"approve\"", "-c", $"mcp_servers.prism.command=\"{bridge.command}\"", "-c", $"mcp_servers.prism.args=[{string.Join(", ", bridge.args.Select(a => JsonSerializer.Serialize(a)))}]" };
        args.AddRange(["-c", "mcp_servers.prism.disabled_tools=" + (Settings.AllowFiles ? "[\"assistant\"]" : "[\"assistant\", \"file\"]")]);
        if (!string.IsNullOrWhiteSpace(Settings.CliModel)) args.AddRange(["-m", Settings.CliModel]);
        args.Add("-");
        // Codex has no system-prompt flag, so the guidance travels with the first message
        string prompt = _history.Count == 0 ? SystemPrompt + "\n\nUser: " + text : text;
        _history.Add(new JsonObject { ["role"] = "user" });
        bool said = false;
        await RunProcess(exe, args, prompt, line =>
        {
            JsonNode? n; try { n = JsonNode.Parse(line); } catch { if (!string.IsNullOrWhiteSpace(line)) { Said?.Invoke(ChatRole.Assistant, line); said = true; } return; }
            var item = n?["item"] ?? n?["msg"] ?? n;
            var type = item?["type"]?.ToString() ?? n?["type"]?.ToString() ?? "";
            if (type.Contains("agent_message", StringComparison.OrdinalIgnoreCase) && (item?["text"] ?? item?["message"]) is { } t && !string.IsNullOrWhiteSpace(t.ToString())) { Said?.Invoke(ChatRole.Assistant, t.ToString()); said = true; }
            else if (type.Contains("mcp_tool_call", StringComparison.OrdinalIgnoreCase) && n?["type"]?.ToString() != "item.completed") Said?.Invoke(ChatRole.Tool, item?["tool"]?.ToString() ?? "tool");
        }, ct);
        if (!said) Said?.Invoke(ChatRole.Note, "Codex finished without a message.");
    }

    static string ToolLine(string name, JsonObject? input)
    {
        string a = input == null ? "" : input.ToJsonString();
        if (a.Length > 140) a = a[..140] + "…";
        return a.Length > 2 ? $"{name}  {a}" : name;
    }

    // ================================================================== API keys
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>A tool result as text (with the picture split off).</summary>
    static (string text, string? imageB64) ResultText(JsonNode? res)
    {
        string? img = null;
        if (res is JsonObject o && o["_image"] is JsonObject im) { img = im["data"]?.ToString(); res = new JsonObject(o.Where(kv => kv.Key != "_image").Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone()))); }
        var s = res?.ToJsonString() ?? "null";
        if (s.Length > 14000) s = s[..14000] + "… (truncated)";
        return (s, img);
    }

    async Task<(string text, string? img, bool error)> CallTool(string name, JsonObject args)
    {
        try
        {
            if (name == "assistant") return ("Not available.", null, true);
            if (!Settings.AllowFiles && name == "file") return ("The file tool is switched off in the assistant settings.", null, true);
            var (t, img) = ResultText(await api.CallAsync(name, args));
            return (t, img, false);
        }
        catch (Exception e) { return (e.Message, null, true); }
    }

    async Task RunAnthropic(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Settings.AnthropicKey)) throw new InvalidOperationException("Enter your Anthropic API key in the assistant settings.");
        var tools = new JsonArray(Tools().Select(t => (JsonNode)new JsonObject { ["name"] = t!["name"]!.ToString(), ["description"] = t["description"]!.ToString(), ["input_schema"] = t["inputSchema"]!.DeepClone() }).ToArray());
        _history.Add(new JsonObject { ["role"] = "user", ["content"] = text });
        for (int turn = 0; turn < 30; turn++)
        {
            var body = new JsonObject { ["model"] = Settings.AnthropicModel, ["max_tokens"] = 4096, ["system"] = SystemPrompt, ["tools"] = tools.DeepClone(), ["messages"] = new JsonArray(_history.Select(m => m.DeepClone()).ToArray()) };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.Add("x-api-key", Settings.AnthropicKey); req.Headers.Add("anthropic-version", "2023-06-01");
            using var resp = await Http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Anthropic API: {(int)resp.StatusCode} {JsonNode.Parse(raw)?["error"]?["message"] ?? raw}");
            var j = JsonNode.Parse(raw)!;
            var content = j["content"]!.AsArray();
            _history.Add(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });
            var results = new JsonArray();
            foreach (var block in content)
            {
                var bt = block!["type"]!.ToString();
                if (bt == "text" && !string.IsNullOrWhiteSpace(block["text"]!.ToString())) Said?.Invoke(ChatRole.Assistant, block["text"]!.ToString().Trim());
                else if (bt == "tool_use")
                {
                    var name = block["name"]!.ToString(); var input = block["input"] as JsonObject ?? [];
                    Said?.Invoke(ChatRole.Tool, ToolLine(name, input));
                    var (rt, img, err) = await CallTool(name, (JsonObject)input.DeepClone());
                    var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = rt } };
                    if (img != null) parts.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = img } });
                    results.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = block["id"]!.ToString(), ["content"] = parts, ["is_error"] = err });
                }
            }
            if (results.Count == 0) return;
            _history.Add(new JsonObject { ["role"] = "user", ["content"] = results });
            ct.ThrowIfCancellationRequested();
        }
        Said?.Invoke(ChatRole.Note, "Stopped after 30 steps. Ask me to continue if there is more to do.");
    }

    async Task RunOpenAi(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Settings.OpenAiKey)) throw new InvalidOperationException("Enter your OpenAI API key in the assistant settings.");
        var tools = new JsonArray(Tools().Select(t => (JsonNode)new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = t!["name"]!.ToString(), ["description"] = t["description"]!.ToString(), ["parameters"] = t["inputSchema"]!.DeepClone() } }).ToArray());
        if (_history.Count == 0) _history.Add(new JsonObject { ["role"] = "system", ["content"] = SystemPrompt });
        _history.Add(new JsonObject { ["role"] = "user", ["content"] = text });
        for (int turn = 0; turn < 30; turn++)
        {
            var body = new JsonObject { ["model"] = Settings.OpenAiModel, ["tools"] = tools.DeepClone(), ["messages"] = new JsonArray(_history.Select(m => m.DeepClone()).ToArray()) };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.Add("Authorization", "Bearer " + Settings.OpenAiKey);
            using var resp = await Http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"OpenAI API: {(int)resp.StatusCode} {JsonNode.Parse(raw)?["error"]?["message"] ?? raw}");
            var msg = JsonNode.Parse(raw)!["choices"]![0]!["message"]!.AsObject();
            _history.Add((JsonObject)msg.DeepClone());
            if (!string.IsNullOrWhiteSpace(msg["content"]?.ToString())) Said?.Invoke(ChatRole.Assistant, msg["content"]!.ToString().Trim());
            var calls = msg["tool_calls"]?.AsArray();
            if (calls == null || calls.Count == 0) return;
            foreach (var call in calls)
            {
                var fn = call!["function"]!; var name = fn["name"]!.ToString();
                JsonObject input; try { input = JsonNode.Parse(fn["arguments"]?.ToString() ?? "{}") as JsonObject ?? []; } catch { input = []; }
                Said?.Invoke(ChatRole.Tool, ToolLine(name, input));
                var (rt, img, _) = await CallTool(name, input);
                if (img != null) rt += "\n(A screenshot was taken but this provider cannot show images here.)";
                _history.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["id"]!.ToString(), ["content"] = rt });
            }
            ct.ThrowIfCancellationRequested();
        }
        Said?.Invoke(ChatRole.Note, "Stopped after 30 steps. Ask me to continue if there is more to do.");
    }
}
