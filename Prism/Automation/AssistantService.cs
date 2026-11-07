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

// Values are stored in assistant.json. 0 was the Claude command-line login, which is no longer offered:
// Claude is used with an API key only. Load() moves a stored 0 to Anthropic.
public enum AiProvider { CodexCli = 1, Anthropic = 2, OpenAi = 3 }
public enum ChatRole { User, Assistant, Tool, Note, Error }

public sealed class AssistantSettings
{
    public AiProvider Provider { get; set; } = AiProvider.CodexCli;
    public string CliModel { get; set; } = "";
    public string AnthropicModel { get; set; } = "claude-sonnet-5-5";
    public string OpenAiModel { get; set; } = "gpt-5";
    public string CliEffort { get; set; } = "";
    public string AnthropicEffort { get; set; } = "";
    public string OpenAiEffort { get; set; } = "";
    public string AnthropicKey { get; set; } = "";
    public string OpenAiKey { get; set; } = "";
    public bool AllowFiles { get; set; }
    public bool RememberKeys { get; set; }
}

/// <summary>An image the user attached to a chat message.</summary>
public sealed record AssistantImage(string Name, string Mime, byte[] Data)
{
    Avalonia.Media.Imaging.Bitmap? _preview;
    /// <summary>Decoded on demand for the chat thumbnails (UI thread).</summary>
    public Avalonia.Media.Imaging.Bitmap Preview => _preview ??= new Avalonia.Media.Imaging.Bitmap(new MemoryStream(Data));
}

/// <summary>One model in a provider's catalog: the id to send and the reasoning levels it supports (empty = no effort control).</summary>
public sealed record AiModelInfo(string Id, string? Label, string[] Efforts);

/// <summary>
/// The in-app AI assistant. It can use your Codex command-line login (nothing to paste), or an Anthropic or OpenAI API key, and it works the CAD by calling
/// the same tools the MCP bridge offers: so everything it does is undoable and looks like interactive work.
/// </summary>
public sealed class AssistantService(AutomationApi api)
{
    public AssistantSettings Settings { get; } = Load();
    public event Action<ChatRole, string, IReadOnlyList<AssistantImage>?>? Said;
    public bool Busy { get; private set; }
    void Say(ChatRole role, string text, IReadOnlyList<AssistantImage>? images = null) => Said?.Invoke(role, text, images);

    /// <summary>PRISM_ASSISTANT_FILE points the settings somewhere else, so tests never touch the real ones.</summary>
    static string SettingsFile => Environment.GetEnvironmentVariable("PRISM_ASSISTANT_FILE") is { Length: > 0 } custom ? custom : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "assistant.json");

    static AssistantSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var saved = JsonSerializer.Deserialize<AssistantSettings>(File.ReadAllText(SettingsFile)) ?? new();
                if (!Enum.IsDefined(saved.Provider)) saved.Provider = AiProvider.Anthropic;
                return saved;
            }
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
    readonly List<JsonObject> _history = [];
    string? _codexThread;                     // Codex session to resume (keeps earlier turns + tool calls in context)
    readonly List<string> _turns = [];        // compact "User: …"/"Assistant: …" transcript, only for the Codex resume fallback
    string? _sentContext;                     // the Context already delivered to the running Codex session

    /// <summary>A sticky intent set by the entry point (script editor, render background). Travels as guidance until Reset / a new conversation.</summary>
    public string? Context { get; set; }
    /// <summary>True after Codex answered with a sign-in error; the panel shows a remedy button.</summary>
    public bool NeedsLogin { get; private set; }
    /// <summary>True after an API provider rejected the key; the panel shows a remedy button.</summary>
    public bool NeedsKeyFix { get; private set; }
    public void ClearKeyFix() => NeedsKeyFix = false;
    string System => SystemPrompt + (Context == null ? "" : "\n\nCurrent context: " + Context);

    public void Reset() { _history.Clear(); _codexThread = null; _turns.Clear(); _sentContext = null; Context = null; NeedsLogin = false; NeedsKeyFix = false; }
    public void Cancel() { try { _cts?.Cancel(); } catch { } }

    /// <summary>Runs `codex login` in a visible terminal so the user can sign in again.</summary>
    public void LaunchCodexLogin()
    {
        var exe = FindOnPath("codex") ?? throw new InvalidOperationException("The Codex command-line tool was not found.");
        Process.Start(new ProcessStartInfo(exe, "login") { UseShellExecute = true });
        NeedsLogin = false;
    }

    /// <summary>True when the CLI failed because the login expired or was never done.</summary>
    internal static bool IsLoginError(string err) =>
        err.Contains("login", StringComparison.OrdinalIgnoreCase) || err.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
        err.Contains("401", StringComparison.Ordinal) || err.Contains("authentication", StringComparison.OrdinalIgnoreCase) || err.Contains("not logged in", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tools the model may see; file-writing tools are hidden when file access is off (enforced again per call).</summary>
    IEnumerable<JsonNode?> Tools() => api.Describe().Where(t => t!["name"]!.ToString() != "assistant" && (Settings.AllowFiles || t["name"]!.ToString() != "file"));

    public async Task SendAsync(string text, IReadOnlyList<AssistantImage>? images = null)
    {
        if (Busy || (string.IsNullOrWhiteSpace(text) && (images == null || images.Count == 0))) return;
        Busy = true; _cts = new CancellationTokenSource();
        NeedsKeyFix = false;
        Say(ChatRole.User, text, images is { Count: > 0 } ? images : null);
        try
        {
            switch (Settings.Provider)
            {
                case AiProvider.CodexCli: await RunCodexCli(text, images, _cts.Token); break;
                case AiProvider.Anthropic: await RunAnthropic(text, images, _cts.Token); break;
                default: await RunOpenAi(text, images, _cts.Token); break;
            }
        }
        catch (OperationCanceledException) { Say(ChatRole.Note, "Stopped."); }
        catch (Exception e)
        {
            Say(ChatRole.Error, e.Message);
            if (Settings.Provider != AiProvider.CodexCli && IsLoginError(e.Message))
            {
                NeedsKeyFix = true;
                Say(ChatRole.Note, "The API key was rejected. Press \"Check the API key\" above or open ⚙ settings, fix the key, then send again.");
            }
        }
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
    public static (string command, string[] args)? McpBridge()
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

    /// <summary>Ready-to-paste client configuration pointing at the discovered bridge ("claude" JSON or "codex" TOML).</summary>
    public static string McpSnippet(string client)
    {
        if (McpBridge() is not { } b) return "The Prism.Mcp bridge was not found. Build it with: dotnet build Prism.Mcp -c Release";
        var args = "[" + string.Join(", ", b.args.Select(a => JsonSerializer.Serialize(a))) + "]";
        var nl = Environment.NewLine;
        if (client == "codex")
            return "# add to " + McpConfigPath("codex") + nl +
                "[mcp_servers.prism]" + nl +
                "command = " + JsonSerializer.Serialize(b.command) + nl +
                "args = " + args + nl;
        return "// merge into " + McpConfigPath("claude") + " (or Claude Code .mcp.json)" + nl +
            "{" + nl + "  \"mcpServers\": {" + nl + "    \"prism\": { \"command\": " + JsonSerializer.Serialize(b.command) + ", \"args\": " + args + " }" + nl + "  }" + nl + "}" + nl;
    }

    /// <summary>The user-level MCP config file of the given client: Claude Desktop (or the same shape for Claude Code) vs. Codex.</summary>
    public static string McpConfigPath(string client)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (client == "codex") return Path.Combine(home, ".codex", "config.toml");
        if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Application Support", "Claude", "claude_desktop_config.json");
        if (OperatingSystem.IsWindows()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json");
        return Path.Combine(home, ".config", "Claude", "claude_desktop_config.json");
    }

    /// <summary>Claude config JSON with mcpServers.prism added; other servers and keys are kept. Malformed JSON throws — the caller reports it.</summary>
    public static string MergeClaudeJson(string? existing, string command, string[] args)
    {
        var root = string.IsNullOrWhiteSpace(existing) ? [] : JsonNode.Parse(existing) as JsonObject ?? throw new FormatException("The file is not a JSON object.");
        if (root["mcpServers"] is not JsonObject servers) root["mcpServers"] = servers = [];
        servers["prism"] = new JsonObject { ["command"] = command, ["args"] = new JsonArray(args.Select(a => (JsonNode)a).ToArray()) };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Codex config.toml with the [mcp_servers.prism] table added or replaced; other tables are kept.</summary>
    public static string MergeCodexToml(string? existing, string command, string[] args)
    {
        var block = new[] { "[mcp_servers.prism]", "command = " + JsonSerializer.Serialize(command), "args = [" + string.Join(", ", args.Select(a => JsonSerializer.Serialize(a))) + "]" };
        var lines = (existing ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        var start = lines.FindIndex(l => l.Trim() == "[mcp_servers.prism]");
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.AddRange(block);
            return string.Join('\n', lines);
        }
        var end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, block);
        return string.Join('\n', lines);
    }

    /// <summary>Writes the bridge into the client's own config file, keeping a .bak of what was there. Returns a human note for the UI.</summary>
    public static string InstallMcpConfig(string client)
    {
        if (McpBridge() is not { } b) return "The Prism.Mcp bridge was not found. Build it with: dotnet build Prism.Mcp -c Release";
        var path = McpConfigPath(client);
        try
        {
            var existing = File.Exists(path) ? File.ReadAllText(path) : null;
            var merged = client == "codex" ? MergeCodexToml(existing, b.command, b.args) : MergeClaudeJson(existing, b.command, b.args);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllText(path, merged);
            return $"Wrote {path}" + (File.Exists(path + ".bak") ? " (the previous file is saved as .bak)" : "") +
                (client == "codex" ? ". Restart Codex to pick it up." : ". Restart Claude to pick it up.");
        }
        catch (Exception e) { return $"Could not update {path}: {e.Message} — use the Copy button and edit the file yourself."; }
    }

    async Task<(int code, string err)> RunProcess(string exe, IEnumerable<string> args, string stdin, Action<string> onLine, CancellationToken ct)
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
        var error = err.ToString().Trim();
        if (p.ExitCode != 0 && error.Length > 0) Say(ChatRole.Error, error.Split('\n').Last().Trim());
        return (p.ExitCode, error);
    }

    async Task RunCodexCli(string text, IReadOnlyList<AssistantImage>? images, CancellationToken ct)
    {
        var exe = FindOnPath("codex") ?? throw new InvalidOperationException("The Codex command-line tool was not found. Install it and run 'codex login', or choose another provider.");
        var bridge = McpBridge() ?? throw new InvalidOperationException("Could not find the Prism MCP bridge (Prism.Mcp). Build it with: dotnet build Prism.Mcp -c Release");
        var tempImages = new List<string>();
        try
        {
            if (images != null)
                foreach (var img in images)
                {
                    var ext = img.Mime.Split('/').LastOrDefault() is { Length: > 0 } e ? e.Replace("jpeg", "jpg") : "png";
                    var file = Path.Combine(Path.GetTempPath(), "prism-ai-" + Guid.NewGuid().ToString("N") + "." + ext);
                    File.WriteAllBytes(file, img.Data); tempImages.Add(file);
                }
            AddTurn("User", text);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var resumed = _codexThread != null;
                var (code, err) = await CodexExec(exe, bridge, text, tempImages, resumed, ct);
                if (resumed && code != 0 && !IsLoginError(err))
                {
                    // the stored session is gone or unreadable: start a fresh one and try again once
                    _codexThread = null;
                    Say(ChatRole.Note, "The earlier Codex conversation could not be resumed — a new session started with the conversation so far.");
                    continue;
                }
                if (code != 0 && IsLoginError(err))
                {
                    NeedsLogin = true;
                    Say(ChatRole.Note, "Codex needs a fresh sign-in. Press \"Sign in to Codex\" below or run 'codex login' in a terminal, then send the message again.");
                }
                else NeedsLogin = false;
                break;
            }
        }
        finally { foreach (var f in tempImages) try { File.Delete(f); } catch { } }
    }

    /// <summary>One line of the compact transcript a fresh Codex session gets when the stored one cannot be resumed.</summary>
    void AddTurn(string role, string text)
    {
        _turns.Add(role + ": " + (text.Length > 600 ? text[..600] + "…" : text));
        if (_turns.Count > 24) _turns.RemoveRange(0, _turns.Count - 24);
    }

    /// <summary>`codex exec`/`exec resume` arguments: the Prism MCP bridge, model/effort picks and a read-only sandbox.
    /// The sandbox travels as a `-c` override because `exec resume` has no `--sandbox` flag.</summary>
    public static List<string> BuildCodexArgs((string command, string[] args) bridge, bool allowFiles, string? sessionId, string? model, string? effort, IReadOnlyList<string>? imageFiles)
    {
        var bridgeArgs = bridge.args.ToList();
        if (!allowFiles) bridgeArgs.AddRange(["--discovery", AutomationServer.AssistantDiscoveryPath]);   // restricted endpoint: no run_script/file writes
        var args = new List<string> { "exec" };
        if (sessionId != null) args.AddRange(["resume", sessionId]);
        args.AddRange(["--json", "--skip-git-repo-check",
            "-c", "sandbox_mode=\"read-only\"",
            "-c", "mcp_servers.prism.default_tools_approval_mode=\"approve\"",
            "-c", $"mcp_servers.prism.command={JsonSerializer.Serialize(bridge.command)}",
            "-c", $"mcp_servers.prism.args=[{string.Join(", ", bridgeArgs.Select(a => JsonSerializer.Serialize(a)))}]",
            "-c", "mcp_servers.prism.disabled_tools=" + (allowFiles ? "[\"assistant\"]" : "[\"assistant\", \"file\", \"run_script\"]")]);
        if (!string.IsNullOrWhiteSpace(model)) args.AddRange(["-m", model]);
        if (!string.IsNullOrWhiteSpace(effort)) args.AddRange(["-c", $"model_reasoning_effort={effort}"]);
        if (imageFiles != null) foreach (var f in imageFiles) args.AddRange(["--image", f]);
        args.Add("-");
        return args;
    }

    async Task<(int code, string err)> CodexExec(string exe, (string command, string[] args) bridge, string text, List<string> imageFiles, bool resumed, CancellationToken ct)
    {
        var args = BuildCodexArgs(bridge, Settings.AllowFiles, resumed ? _codexThread : null, Settings.CliModel, Settings.CliEffort, imageFiles);
        // Codex has no system-prompt flag: the guidance travels with the first message of a session. On resume it lives
        // in the session already; a fresh session after a lost thread gets the transcript so "it"/"that" still resolves.
        string prompt;
        if (resumed)
            if (Context != null && Context != _sentContext) { prompt = "Current context: " + Context + "\n\nUser: " + text; _sentContext = Context; }
            else prompt = text;
        else
        {
            prompt = System + (_turns.Count <= 1 ? "\n\nUser: " + text : "\n\nConversation so far:\n" + string.Join("\n", _turns));
            _sentContext = Context;
        }
        bool said = false;
        var r = await RunProcess(exe, args, prompt, line =>
        {
            JsonNode? n; try { n = JsonNode.Parse(line); } catch { if (!string.IsNullOrWhiteSpace(line)) { Say(ChatRole.Assistant, line); said = true; AddTurn("Assistant", line); } return; }
            if ((n?["thread_id"] ?? n?["session_id"]) is { } id && !string.IsNullOrWhiteSpace(id.ToString())) _codexThread = id.ToString();
            var item = n?["item"] ?? n?["msg"] ?? n;
            if ((item?["thread_id"] ?? item?["session_id"]) is { } iid && !string.IsNullOrWhiteSpace(iid.ToString())) _codexThread = iid.ToString();
            var type = item?["type"]?.ToString() ?? n?["type"]?.ToString() ?? "";
            if (type.Contains("agent_message", StringComparison.OrdinalIgnoreCase) && (item?["text"] ?? item?["message"]) is { } t && !string.IsNullOrWhiteSpace(t.ToString())) { Say(ChatRole.Assistant, t.ToString()); said = true; AddTurn("Assistant", t.ToString()); }
            else if (type.Contains("mcp_tool_call", StringComparison.OrdinalIgnoreCase) && n?["type"]?.ToString() != "item.completed") Say(ChatRole.Tool, item?["tool"]?.ToString() ?? "tool");
            else if (type == "turn.completed" && (item?["usage"] ?? n?["usage"]) is { } u)
            {
                long In(JsonNode? x) => x?.GetValue<long>() ?? 0;
                Say(ChatRole.Note, UsageText(In(u["input_tokens"]), In(u["output_tokens"]), In(u["cached_input_tokens"])));
            }
        }, ct);
        if (!said && r.code == 0) Say(ChatRole.Note, "Codex finished without a message.");
        return r;
    }

    static string ToolLine(string name, JsonObject? input)
    {
        string a = input == null ? "" : input.ToJsonString();
        if (a.Length > 140) a = a[..140] + "…";
        return a.Length > 2 ? $"{name}  {a}" : name;
    }

    /// <summary>The real model id for a picker row shown as "id   (label)" — only the id is ever sent to a provider.</summary>
    public static string ModelIdOf(string display) { var i = display.IndexOf("   (", StringComparison.Ordinal); return i < 0 ? display : display[..i]; }

    /// <summary>The faint usage line shown after a reply: input, output and cached tokens when the provider reports them.</summary>
    public static string UsageText(long input, long output, long cached = 0) =>
        $"usage: {input:N0} in · {output:N0} out" + (cached > 0 ? $" · {cached:N0} cached" : "");

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
            if (!Settings.AllowFiles && AutomationApi.Restricted(name, args)) return (AutomationApi.RestrictedMessage, null, true);
            var (t, img) = ResultText(await api.CallAsync(name, args));
            return (t, img, false);
        }
        catch (Exception e) { return (e.Message, null, true); }
    }

    async Task RunAnthropic(string text, IReadOnlyList<AssistantImage>? images, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Settings.AnthropicKey)) throw new InvalidOperationException("Enter your Anthropic API key in the assistant settings.");
        var tools = new JsonArray(Tools().Select(t => (JsonNode)new JsonObject { ["name"] = t!["name"]!.ToString(), ["description"] = t["description"]!.ToString(), ["input_schema"] = t["inputSchema"]!.DeepClone() }).ToArray());
        JsonNode userContent = text;
        if (images is { Count: > 0 })
        {
            var parts = new JsonArray();
            foreach (var im in images) parts.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = im.Mime, ["data"] = Convert.ToBase64String(im.Data) } });
            parts.Add(new JsonObject { ["type"] = "text", ["text"] = string.IsNullOrWhiteSpace(text) ? "(see attached image)" : text });
            userContent = parts;
        }
        _history.Add(new JsonObject { ["role"] = "user", ["content"] = userContent });
        long tokIn = 0, tokOut = 0, tokCached = 0;
        for (int turn = 0; turn < 30; turn++)
        {
            var body = new JsonObject { ["model"] = Settings.AnthropicModel, ["max_tokens"] = 4096, ["system"] = System, ["tools"] = tools.DeepClone(), ["messages"] = new JsonArray(_history.Select(m => m.DeepClone()).ToArray()) };
            if (!string.IsNullOrWhiteSpace(Settings.AnthropicEffort)) body["output_config"] = new JsonObject { ["effort"] = Settings.AnthropicEffort };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.Add("x-api-key", Settings.AnthropicKey); req.Headers.Add("anthropic-version", "2023-06-01");
            using var resp = await Http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Anthropic API: {(int)resp.StatusCode} {JsonNode.Parse(raw)?["error"]?["message"] ?? raw}");
            var j = JsonNode.Parse(raw)!;
            if (j["usage"] is { } u) { tokIn += u["input_tokens"]?.GetValue<long>() ?? 0; tokOut += u["output_tokens"]?.GetValue<long>() ?? 0; tokCached += (u["cache_read_input_tokens"]?.GetValue<long>() ?? 0) + (u["cache_creation_input_tokens"]?.GetValue<long>() ?? 0); }
            var content = j["content"]!.AsArray();
            _history.Add(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });
            var results = new JsonArray();
            foreach (var block in content)
            {
                var bt = block!["type"]!.ToString();
                if (bt == "text" && !string.IsNullOrWhiteSpace(block["text"]!.ToString())) Say(ChatRole.Assistant, block["text"]!.ToString().Trim());
                else if (bt == "tool_use")
                {
                    var name = block["name"]!.ToString(); var input = block["input"] as JsonObject ?? [];
                    Say(ChatRole.Tool, ToolLine(name, input));
                    var (rt, img, err) = await CallTool(name, (JsonObject)input.DeepClone());
                    var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = rt } };
                    if (img != null) parts.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = img } });
                    results.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = block["id"]!.ToString(), ["content"] = parts, ["is_error"] = err });
                }
            }
            if (results.Count == 0) { if (tokIn + tokOut > 0) Say(ChatRole.Note, UsageText(tokIn, tokOut, tokCached)); return; }
            _history.Add(new JsonObject { ["role"] = "user", ["content"] = results });
            ct.ThrowIfCancellationRequested();
        }
        Say(ChatRole.Note, "Stopped after 30 steps. Ask me to continue if there is more to do.");
    }

    async Task RunOpenAi(string text, IReadOnlyList<AssistantImage>? images, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Settings.OpenAiKey)) throw new InvalidOperationException("Enter your OpenAI API key in the assistant settings.");
        var tools = new JsonArray(Tools().Select(t => (JsonNode)new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = t!["name"]!.ToString(), ["description"] = t["description"]!.ToString(), ["parameters"] = t["inputSchema"]!.DeepClone() } }).ToArray());
        if (_history.Count == 0) _history.Add(new JsonObject { ["role"] = "system", ["content"] = System });
        JsonNode userContent = text;
        if (images is { Count: > 0 })
        {
            var parts = new JsonArray();
            foreach (var im in images) parts.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{im.Mime};base64,{Convert.ToBase64String(im.Data)}" } });
            parts.Add(new JsonObject { ["type"] = "text", ["text"] = string.IsNullOrWhiteSpace(text) ? "(see attached image)" : text });
            userContent = parts;
        }
        _history.Add(new JsonObject { ["role"] = "user", ["content"] = userContent });
        long tokIn = 0, tokOut = 0, tokCached = 0;
        for (int turn = 0; turn < 30; turn++)
        {
            var body = new JsonObject { ["model"] = Settings.OpenAiModel, ["tools"] = tools.DeepClone(), ["messages"] = new JsonArray(_history.Select(m => m.DeepClone()).ToArray()) };
            if (!string.IsNullOrWhiteSpace(Settings.OpenAiEffort)) body["reasoning_effort"] = Settings.OpenAiEffort;
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.Add("Authorization", "Bearer " + Settings.OpenAiKey);
            using var resp = await Http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"OpenAI API: {(int)resp.StatusCode} {JsonNode.Parse(raw)?["error"]?["message"] ?? raw}");
            var root = JsonNode.Parse(raw)!;
            if (root["usage"] is { } u) { tokIn += u["prompt_tokens"]?.GetValue<long>() ?? 0; tokOut += u["completion_tokens"]?.GetValue<long>() ?? 0; tokCached += u["prompt_tokens_details"]?["cached_tokens"]?.GetValue<long>() ?? 0; }
            var msg = root["choices"]![0]!["message"]!.AsObject();
            _history.Add((JsonObject)msg.DeepClone());
            if (!string.IsNullOrWhiteSpace(msg["content"]?.ToString())) Say(ChatRole.Assistant, msg["content"]!.ToString().Trim());
            var calls = msg["tool_calls"]?.AsArray();
            if (calls == null || calls.Count == 0) { if (tokIn + tokOut > 0) Say(ChatRole.Note, UsageText(tokIn, tokOut, tokCached)); return; }
            foreach (var call in calls)
            {
                var fn = call!["function"]!; var name = fn["name"]!.ToString();
                JsonObject input; try { input = JsonNode.Parse(fn["arguments"]?.ToString() ?? "{}") as JsonObject ?? []; } catch { input = []; }
                Say(ChatRole.Tool, ToolLine(name, input));
                var (rt, img, _) = await CallTool(name, input);
                if (img != null) rt += "\n(A screenshot was taken but this provider cannot show images here.)";
                _history.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["id"]!.ToString(), ["content"] = rt });
            }
            ct.ThrowIfCancellationRequested();
        }
        Say(ChatRole.Note, "Stopped after 30 steps. Ask me to continue if there is more to do.");
    }

    // ================================================================== model catalog

    /// <summary>The models this provider currently reports; empty when discovery is unavailable (the saved id keeps working).</summary>
    public async Task<IReadOnlyList<AiModelInfo>> ModelsAsync(AiProvider provider, CancellationToken ct = default)
    {
        try
        {
            switch (provider)
            {
                case AiProvider.CodexCli: return await CodexModels(ct);
                case AiProvider.Anthropic:
                    if (string.IsNullOrWhiteSpace(Settings.AnthropicKey)) return [];
                    if (await GetJson("https://api.anthropic.com/v1/models?limit=1000", r => { r.Headers.Add("x-api-key", Settings.AnthropicKey); r.Headers.Add("anthropic-version", "2023-06-01"); }, ct) is { } a) return ParseAnthropicModels(a);
                    return [];
                default:
                    if (string.IsNullOrWhiteSpace(Settings.OpenAiKey)) return [];
                    if (await GetJson("https://api.openai.com/v1/models", r => r.Headers.Add("Authorization", "Bearer " + Settings.OpenAiKey), ct) is { } o) return ParseOpenAiModels(o);
                    return [];
            }
        }
        catch { return []; }
    }

    static async Task<JsonNode?> GetJson(string url, Action<HttpRequestMessage> headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        headers(req);
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    static async Task<IReadOnlyList<AiModelInfo>> CodexModels(CancellationToken ct)
    {
        if (FindOnPath("codex") is { } exe)
        {
            var parsed = ParseCodexModels(await CaptureProcess(exe, ["debug", "models"], ct));
            if (parsed.Count > 0) return parsed;
        }
        // fall back to the CLI's own cache so the picker still works when `codex debug models` is unavailable
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "models_cache.json");
        if (File.Exists(cache))
            try { var parsed = ParseCodexModels(File.ReadAllText(cache)); if (parsed.Count > 0) return parsed; } catch { }
        return [];
    }

    static async Task<string> CaptureProcess(string exe, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return "";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var reg = timeout.Token.Register(() => { try { p.Kill(true); } catch { } });
            var stdout = await p.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return stdout;
        }
        catch { return ""; }
    }

    /// <summary>Reads `codex debug models` output (or the same shape in models_cache.json): slug, display_name, supported_reasoning_levels.</summary>
    public static List<AiModelInfo> ParseCodexModels(string? json)
    {
        var list = new List<AiModelInfo>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            var root = JsonNode.Parse(json);
            if ((root as JsonArray ?? root?["models"] as JsonArray) is not { } arr) return list;
            foreach (var m in arr)
            {
                var id = m?["slug"]?.ToString() ?? m?["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                var vis = m?["visibility"]?.ToString();
                if (vis != null && !vis.Equals("list", StringComparison.OrdinalIgnoreCase)) continue;
                var efforts = (m?["supported_reasoning_levels"] as JsonArray)
                    ?.Select(e => e?["effort"]?.ToString() ?? e?.ToString()).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToArray() ?? [];
                list.Add(new AiModelInfo(id!, m?["display_name"]?.ToString(), efforts));
            }
        }
        catch { }
        return list;
    }

    /// <summary>Claude ids from GET /v1/models. The catalog does not list per-model effort levels, so the known Messages-API set is offered.</summary>
    public static List<AiModelInfo> ParseAnthropicModels(JsonNode? root)
    {
        var list = new List<AiModelInfo>();
        if (root?["data"] is not JsonArray arr) return list;
        foreach (var m in arr)
        {
            var id = m?["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("claude", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new AiModelInfo(id!, m?["display_name"]?.ToString(), ["low", "medium", "high"]));
        }
        return list;
    }

    /// <summary>Tool-using chat ids from GET /v1/models (the list also returns audio, image, embedding and moderation models).</summary>
    public static List<AiModelInfo> ParseOpenAiModels(JsonNode? root)
    {
        var list = new List<AiModelInfo>();
        if (root?["data"] is not JsonArray arr) return list;
        foreach (var m in arr)
        {
            var id = m?["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(id) || !IsChatModel(id)) continue;
            list.Add(new AiModelInfo(id!, null, IsReasoningModel(id) ? ["minimal", "low", "medium", "high"] : []));
        }
        return list.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsChatModel(string id)
    {
        var i = id.ToLowerInvariant();
        if (i.Contains("embedding") || i.Contains("whisper") || i.Contains("tts") || i.Contains("dall-e") || i.Contains("moderation") || i.Contains("audio") || i.Contains("image") || i.Contains("transcribe") || i.Contains("search")) return false;
        return i.StartsWith("gpt") || i.StartsWith("o1") || i.StartsWith("o3") || i.StartsWith("o4") || i.StartsWith("codex") || i.StartsWith("chatgpt");
    }

    /// <summary>Reasoning models accept reasoning_effort; plain chat models (gpt-4o, gpt-4.1, chatgpt-*) reject it.</summary>
    public static bool IsReasoningModel(string id)
    {
        var i = id.ToLowerInvariant();
        if (i.StartsWith("gpt-4o") || i.StartsWith("gpt-4.1") || i.StartsWith("chatgpt")) return false;
        return i.StartsWith("o1") || i.StartsWith("o3") || i.StartsWith("o4") || i.StartsWith("gpt-5") || i.StartsWith("codex") || i.Contains("reasoning");
    }
}
