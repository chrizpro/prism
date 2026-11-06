// Prism MCP bridge: speaks the Model Context Protocol over stdio and forwards tool calls to the running Prism app
// (which exposes its scripting API on a localhost-only, token-protected endpoint). stdout carries protocol messages only.
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var bridge = new Bridge();
await bridge.RunAsync();

sealed class Bridge
{
    static readonly string Discovery = Environment.GetEnvironmentVariable("PRISM_AUTOMATION_DISCOVERY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "automation.json");
    static readonly string CachePath = Path.Combine(Path.GetDirectoryName(Discovery)!, "mcp-tools-cache.json");
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(6) };
    string? _baseUrl, _token;

    const string Instructions =
        "Prism is a desktop CAD application. Prefer run_script (Python: `import cadkit as cad`) and batch many operations in one script, printing only what you need. " +
        "Use describe_api (no arguments lists tools one per line; names=[...] gives schemas) and call_api for single calls; run_command takes command names found with list_commands(query). " +
        "Face and edge indices are 1-based; form_info / form_op use 0-based cage indices. Units are millimetres, Z up. Any tool call launches the app if needed. " +
        "Slot and parameter labels match by partial, case-insensitive text; objects can be named by name or id prefix; commands seed empty slots from the selection; " +
        "call wait_idle after view changes before pointer events or screenshots. Prompts build_*, test_* and bug_hunt are ready-made scenarios.";

    // ------------------------------------------------------------ prompts: ready-made test scenarios
    static readonly (string name, string desc, string text)[] Prompts =
    [
        ("build_solid_part", "Build a small solid part (bracket with holes and fillets) and check it.",
         "Build a solid part to try out the solid modeling tools in Prism. Open a new document tab, then build a mounting bracket: a sketched L profile extruded 30 mm, two cylindrical holes cut through one leg (use Extrude with a negative distance on a face sketch), fillet the outer corner, and shell or chamfer something. Then run properties (apply a steel material), check, section, and take a screenshot of each stage. Report anything that looks wrong or surprising: errors, odd previews, wrong sizes, UI text that is confusing. Fix bugs you can fix and keep going."),
        ("build_lofted_surface", "Build a smooth lofted surface object and analyze its quality.",
         "Try out surfacing in Prism. Draw 5-8 closed spline sections at different heights (sketch_add spline with closed:true), loft them with loft_surface, close the ends with planar_surface, knit them, thicken the shell, then run curvature, zebra stripes and check. Look at the curvature comb while drawing the sections (the Curvature toggle in the Sketch tab). Report continuity problems, noisy analysis, or tools that refuse legitimate input."),
        ("build_freeform_model", "Build a subdivision (Freeform) model, convert it to NURBS and combine it.",
         "Try out Freeform (subdivision) modeling in Prism. In a new tab: make a Freeform Cylinder or Box, use form_op (extrude_faces, set_vertices, insert_loop, crease, level, mirror), build a handle or limb with form_sweep or form_loft, try edit_form actions (select loop/ring, bridge, freeze, align to curve), then convert to NURBS, union the pieces, and run check and properties. Note that form_op cap faces keep their index after an extrude. Report crashes, bad geometry, and misleading names."),
        ("test_2d_sketcher", "Exercise the 2D sketcher: constraints, typed entry, selection, splines.",
         "Test the 2D sketcher in Prism using sketch_input (it sends real pointer and key events). Draw lines with typed length and angle (keys Char:2, Char:5, Tab, Enter), a 3-point rectangle with H/V lock, an ellipse locked to a circle with Char:L, a spline then edit_spline (add and remove points). Add constraints in both orders (the second pick should follow the first), try a conflicting constraint and check the message, box-select and delete, and confirm closed profiles are tinted. Report anything that behaves differently from the on-screen help text."),
        ("test_3d_sketch", "Exercise the 3D sketch tool with the axis gizmo and curvature comb.",
         "Test the 3D Sketch command in Prism with the pointer tool (events: down/up/move/drag with world x,y,z; key events like Tab, Char:Z, Char:S). Draw a spline on the front plane, switch to the ground plane with Tab, lock an axis with Char:X/Y/Z and type a distance, draw an arc, circle and rectangle, move a point with the Move shape, then create the curves and sweep or loft something along them. Call wait_idle after view-changing keys. Report anything awkward."),
        ("bug_hunt", "Free-form bug hunt: build something, look at it critically, fix what is broken.",
         "Pick a small realistic object (something a user might model on day one), build it in Prism with whichever tools fit, and treat it as a bug hunt: use screenshots, the analysis commands, undo/redo, save and reopen, and unusual-but-legal inputs. For each bug: reproduce, find the cause in the source, fix it, rebuild, and verify. Finish with a short list of what you found and what is still open."),
    ];

    static void Log(string s) => Console.Error.WriteLine("[prism-mcp] " + s);

    // ------------------------------------------------------------ main loop
    public async Task RunAsync()
    {
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        string? line;
        while ((line = await stdin.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? msg;
            try { msg = JsonNode.Parse(line); } catch { continue; }
            if (msg is not JsonObject req) continue;
            var id = req["id"]?.DeepClone();
            var method = req["method"]?.ToString();
            if (method == null) continue;                     // a response to us; we send no requests
            if (id == null) continue;                          // notification (e.g. notifications/initialized)
            JsonNode response;
            try { response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = await HandleAsync(method, req["params"] as JsonObject) }; }
            catch (Exception e)
            {
                response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32603, ["message"] = e.Message } };
            }
            await stdout.WriteLineAsync(response.ToJsonString());
        }
    }

    async Task<JsonNode?> HandleAsync(string method, JsonObject? p)
    {
        switch (method)
        {
            case "initialize":
                return new JsonObject
                {
                    ["protocolVersion"] = p?["protocolVersion"]?.ToString() ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false }, ["prompts"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = "prism", ["version"] = "1.0.0" },
                    ["instructions"] = Instructions,
                };
            case "ping": return new JsonObject();
            case "tools/list": return new JsonObject { ["tools"] = await ListToolsAsync() };
            case "tools/call": return await CallToolAsync(p!["name"]!.ToString(), p["arguments"] as JsonObject ?? []);
            case "resources/list": return new JsonObject { ["resources"] = new JsonArray() };
            case "prompts/list":
                return new JsonObject { ["prompts"] = new JsonArray(Prompts.Select(x => (JsonNode)new JsonObject { ["name"] = x.name, ["description"] = x.desc }).ToArray()) };
            case "prompts/get":
            {
                var want = p!["name"]!.ToString();
                var found = Prompts.FirstOrDefault(x => x.name == want);
                if (found.name == null) throw new InvalidOperationException($"Unknown prompt '{want}'. Prompts: {string.Join(", ", Prompts.Select(x => x.name))}");
                return new JsonObject
                {
                    ["description"] = found.desc,
                    ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonObject { ["type"] = "text", ["text"] = found.text } }),
                };
            }
            default: throw new InvalidOperationException($"Method not found: {method}");
        }
    }

    // ------------------------------------------------------------ tools
    static JsonObject LocalTool(string name, string desc, JsonObject? props = null) => new()
    {
        ["name"] = name, ["description"] = desc,
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = props ?? new JsonObject() },
    };

    async Task<JsonArray> ListToolsAsync()
    {
        var tools = new JsonArray
        {
            LocalTool("app_status", "Report whether the Prism app is running and reachable."),
            LocalTool("app_launch", "Start the Prism desktop app if it is not already running (other tools do this automatically)."),
            LocalTool("app_close", "Request Prism to close. Unsaved documents show a save prompt; this never force-kills the app."),
            new JsonObject { ["name"] = "run_script", ["description"] = "Execute Python 3 against the live Prism document. import cadkit as cad; print results for the response. The script has the user's local process permissions. Use cad.history(name) to group edits into one undo step. Returns what the script printed, then any error. If it fails, its changes are undone (atomic:false keeps partial work). Timeout is 1–300 seconds.", ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["code"] = new JsonObject { ["type"] = "string" }, ["timeout_seconds"] = new JsonObject { ["type"] = "integer" }, ["atomic"] = new JsonObject { ["type"] = "boolean" } }, ["required"] = new JsonArray("code") } },
            LocalTool("describe_api", "Discover the application automation tools. No arguments: one line per tool (name, summary, argument names; * = required). query: only tools whose name or description contains it. names: full description and argument schema for just those tools. Then call them with call_api.", new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "string" },
                ["names"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            }),
            new JsonObject { ["name"] = "call_api", ["description"] = "Call one application automation tool by name; use describe_api for names and arguments.", ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["name"] = new JsonObject { ["type"] = "string" }, ["arguments"] = new JsonObject { ["type"] = "object" } }, ["required"] = new JsonArray("name") } },
        };
        // The full catalogue remains available on demand, and old direct calls remain supported.
        if (Environment.GetEnvironmentVariable("PRISM_MCP_FULL_TOOLS") != "1") return tools;
        JsonArray? remote = null;
        try
        {
            if (await ConnectAsync(launch: false)) { remote = await FetchToolsAsync(); File.WriteAllText(CachePath, remote!.ToJsonString()); }
        }
        catch (Exception e) { Log("fetch tools: " + e.Message); }
        if (remote == null && File.Exists(CachePath)) remote = JsonNode.Parse(File.ReadAllText(CachePath)) as JsonArray;
        if (remote == null)
        {
            // First ever run: start the app once so the tool catalogue can be learned (then cached).
            if (await ConnectAsync(launch: true)) { remote = await FetchToolsAsync(); File.WriteAllText(CachePath, remote!.ToJsonString()); }
        }
        if (remote != null) foreach (var t in remote) tools.Add(t!.DeepClone());
        return tools;
    }

    async Task<JsonNode?> CallToolAsync(string name, JsonObject args)
    {
        try
        {
            switch (name)
            {
                case "app_status":
                    bool up = await ConnectAsync(launch: false);
                    return Text(new JsonObject { ["running"] = up, ["endpoint"] = up ? _baseUrl : null }.ToJsonString());
                case "app_launch":
                    bool ok = await ConnectAsync(launch: true);
                    return ok ? Text("Prism is running.") : Error("Could not start Prism. Build the Desktop project (Release or Debug) or set PRISM_EXE to the .exe path.");
                case "app_close":
                    return Text(CloseApp());
            }
            if (!await ConnectAsync(launch: true)) return Error("Prism is not running and could not be launched. Set PRISM_EXE or build Prism.Desktop.");

            if (name == "describe_api") return Text(Describe((await FetchToolsAsync())!, args));
            if (name == "run_script")
            {
                var scriptResult = await Post("/script", args);
                if (scriptResult["ok"]?.GetValue<bool>() != true) return Error(scriptResult["error"]?.ToString() ?? "Script failed to start.");
                var scriptOutput = scriptResult["result"]!;
                foreach (var stream in new[] { "stdout", "stderr" })
                    if (scriptOutput[stream]?.ToString() is { } streamText) scriptOutput[stream] = Shorten(streamText);
                var response = Text(ScriptText(scriptOutput));
                if (scriptOutput["exit_code"]?.GetValue<int>() != 0) response["isError"] = true;
                return response;
            }
            if (name == "call_api") { name = args["name"]?.ToString() ?? throw new ArgumentException("name is required"); args = args["arguments"] as JsonObject ?? []; }

            var body = new JsonObject { ["tool"] = name, ["args"] = args.DeepClone() };
            var rs = await Post("/call", body);
            if (rs["ok"]?.GetValue<bool>() != true) return Error(rs["error"]?.ToString() ?? "Unknown error");

            var result = rs["result"];
            var content = new JsonArray();
            if (result is JsonObject ro && ro["_image"] is JsonObject img)
            {
                content.Add(new JsonObject { ["type"] = "image", ["data"] = img["data"]!.ToString(), ["mimeType"] = img["mime"]!.ToString() });
                ro.Remove("_image");
                if (ro.Count == 0) result = null;
            }
            if (result != null) content.Insert(0, new JsonObject { ["type"] = "text", ["text"] = result.ToJsonString(Compact) });
            return new JsonObject { ["content"] = content };
        }
        catch (Exception e) { return Error(e.Message); }
    }

    // Quotes and apostrophes stay as they are: the default escaping turns each into six characters ("), which costs tokens and reads badly.
    static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Script output as plain text: what the script printed, then anything that went wrong. No JSON wrapper to unescape.</summary>
    static string ScriptText(JsonNode r)
    {
        var sb = new StringBuilder(r["stdout"]?.ToString().TrimEnd() ?? "");
        if (r["stderr"]?.ToString().Trim() is { Length: > 0 } err) sb.Append(sb.Length > 0 ? "\n" : "").Append("[stderr]").Append('\n').Append(err);
        if (r["timed_out"]?.GetValue<bool>() == true) sb.Append('\n').Append("[timed out]");
        if (r["rolled_back"]?.GetValue<bool>() == true) sb.Append('\n').Append("[rolled back: the script failed, so its changes were undone; pass atomic:false to keep partial work]");
        if (r["exit_code"]?.GetValue<int>() is int code and not 0) sb.Append('\n').Append("[exit code ").Append(code).Append(']');
        return sb.Length == 0 ? "(no output)" : sb.ToString();
    }

    const int MaxScriptChars = 8000;

    /// <summary>Keeps the start and end of long script output (the end usually holds the error or the answer) and says what was dropped.</summary>
    static string Shorten(string s)
    {
        if (s.Length <= MaxScriptChars) return s;
        int head = MaxScriptChars / 3, tail = MaxScriptChars - head;
        return s[..head] + $"\n[... {s.Length - MaxScriptChars:N0} characters omitted: print less, or print a summary ...]\n" + s[^tail..];
    }

    /// <summary>The tool catalogue at the level asked for: a line per tool by default, full schemas only for named tools.</summary>
    static string Describe(JsonArray tools, JsonObject args)
    {
        var wanted = (args["names"] as JsonArray)?.Select(n => n!.ToString()).ToList();
        if (wanted is { Count: > 0 })
        {
            var picked = new JsonArray(tools.Where(t => wanted.Contains(t!["name"]!.ToString(), StringComparer.OrdinalIgnoreCase)).Select(t => t!.DeepClone()).ToArray());
            var missing = wanted.Where(w => !tools.Any(t => string.Equals(t!["name"]!.ToString(), w, StringComparison.OrdinalIgnoreCase))).ToList();
            return picked.ToJsonString(Compact) + (missing.Count > 0 ? $"\nUnknown: {string.Join(", ", missing)}. Call describe_api with no arguments for all names." : "");
        }
        var query = args["query"]?.ToString() ?? "";
        var lines = new List<string>();
        foreach (var t in tools)
        {
            string name = t!["name"]!.ToString(), desc = t["description"]?.ToString() ?? "";
            if (query.Length > 0 && !name.Contains(query, StringComparison.OrdinalIgnoreCase) && !desc.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            int end = desc.IndexOfAny(['.', '\n']); var summary = end > 0 && end < 140 ? desc[..end] : desc.Length > 140 ? desc[..140] + "…" : desc;
            var required = (t["inputSchema"]?["required"] as JsonArray)?.Select(r => r!.ToString()).ToHashSet() ?? [];
            var argNames = (t["inputSchema"]?["properties"] as JsonObject)?.Select(p => p.Key + (required.Contains(p.Key) ? "*" : "")) ?? [];
            lines.Add($"{name}: {summary} ({string.Join(", ", argNames)})");
        }
        return lines.Count == 0 ? $"No tools match '{query}'. Call describe_api with no arguments for all names."
            : string.Join("\n", lines) + "\nFull schema: describe_api(names=[...]).";
    }

    static JsonObject Text(string t) => new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = t }) };
    static JsonObject Error(string t) => new() { ["isError"] = true, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = t }) };

    // ------------------------------------------------------------ talking to the app
    async Task<JsonArray?> FetchToolsAsync()
    {
        var rs = await Get("/tools");
        return rs["tools"] as JsonArray;
    }

    async Task<JsonObject> Get(string path)
    {
        using var rq = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        rq.Headers.Add("X-Prism-Token", _token);
        using var rs = await _http.SendAsync(rq);
        return JsonNode.Parse(await rs.Content.ReadAsStringAsync())!.AsObject();
    }

    async Task<JsonObject> Post(string path, JsonObject body)
    {
        using var rq = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        rq.Headers.Add("X-Prism-Token", _token);
        using var rs = await _http.SendAsync(rq);
        return JsonNode.Parse(await rs.Content.ReadAsStringAsync())!.AsObject();
    }

    int? _launchedPid;

    async Task<bool> TryDiscoverAsync()
    {
        try
        {
            if (!File.Exists(Discovery)) return false;
            var d = JsonNode.Parse(File.ReadAllText(Discovery))!.AsObject();
            _baseUrl = $"http://127.0.0.1:{d["port"]}"; _token = d["token"]!.ToString();
            using var cts = new CancellationTokenSource(1500);
            using var rq = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/ping");
            rq.Headers.Add("X-Prism-Token", _token);
            using var rs = await _http.SendAsync(rq, cts.Token);
            return rs.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    async Task<bool> ConnectAsync(bool launch)
    {
        if (await TryDiscoverAsync()) return true;
        if (!launch) return false;
        var exe = FindExe();
        if (exe == null) { Log("Prism.Desktop.exe not found"); return false; }
        Log("launching " + exe);
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
        psi.Environment["PRISM_NO_HOME"] = "1";
        var proc = Process.Start(psi);
        _launchedPid = proc?.Id;
        for (int i = 0; i < 80; i++)            // up to ~20 s
        {
            await Task.Delay(250);
            if (await TryDiscoverAsync()) { await Task.Delay(400); return true; }
        }
        return false;
    }

    string CloseApp()
    {
        try
        {
            int? pid = _launchedPid;
            if (pid == null && File.Exists(Discovery)) pid = JsonNode.Parse(File.ReadAllText(Discovery))!["pid"]?.GetValue<int>();
            if (pid == null) return "No known Prism process.";
            var p = Process.GetProcessById(pid.Value);
            p.CloseMainWindow();
            if (!p.WaitForExit(4000)) return "Close requested. Prism is still open; complete or cancel its save prompt.";
            return "Prism closed.";
        }
        catch (Exception e) { return "Could not close: " + e.Message; }
    }

    static string? FindExe()
    {
        var env = Environment.GetEnvironmentVariable("PRISM_EXE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        // Walk up from the bridge's folder to the solution root and look in the Desktop project's output.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string executable = OperatingSystem.IsWindows() ? "Prism.Desktop.exe" : "Prism.Desktop";
            var packaged = Path.Combine(dir.FullName, executable);
            if (File.Exists(packaged)) return packaged;
            foreach (var cfg in new[] { "Release", "Debug" })
            {
                var cand = Path.Combine(dir.FullName, "Prism.Desktop", "bin", cfg, "net10.0", executable);
                if (File.Exists(cand)) return cand;
            }
        }
        return null;
    }
}
