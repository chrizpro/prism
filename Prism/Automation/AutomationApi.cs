using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Drawing;
using Prism.Core.Form;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Rendering;
using Prism.ViewModels;

namespace Prism.Automation;

/// <summary>
/// The app's scripting surface. Every tool runs on the UI thread and goes through the same session/command code paths
/// as the mouse, so scripted work is undoable and looks identical to interactive work. Indices that refer to faces and
/// edges are 1-based, matching the labels shown in the UI ("Face 3 of Box 1").
/// </summary>
public sealed partial class AutomationApi
{
    // the commands the tools can start are found again by name when a file with history is opened
    static AutomationApi() { AutoRegister(); foreach (var make in Registry.Values) CommandCatalog.Register(make); }

    /// <summary>Every command class that no hand-written entry already starts gets a name from its class (SketchFilletCommand becomes sketch_fillet), so a new command is reachable without touching this file.</summary>
    static void AutoRegister()
    {
        var known = new HashSet<Type>();
        foreach (var make in Registry.Values) { try { known.Add(make().GetType()); } catch { } }
        foreach (var type in typeof(Command).Assembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsSubclassOf(typeof(Command)) || known.Contains(type) || type.GetConstructor(Type.EmptyTypes) == null) continue;
            var key = System.Text.RegularExpressions.Regex.Replace(type.Name.EndsWith("Command") ? type.Name[..^7] : type.Name, "(?<=[a-z0-9])(?=[A-Z])", "_").ToLowerInvariant();
            if (key.Length == 0 || Registry.ContainsKey(key)) continue;
            var t = type;
            Registry[key] = () => (Command)Activator.CreateInstance(t)!;
        }
        // commands whose constructor takes an optional argument, or that come in several variants
        Registry.TryAdd("create_sketch", () => new CreateSketchCommand());
        Registry.TryAdd("lights", () => new LightsCommand());
        foreach (var joint in Enum.GetValues<Prism.Core.Assembly.JointType>()) Registry.TryAdd("joint_" + joint.ToString().ToLowerInvariant(), () => new JointCommand(joint));
    }

    HistoryNode? _scriptStart;

    /// <summary>Start treating a script's changes as one transaction (they fold into a single undo step).</summary>
    public Task ScriptBeginAsync() => Dispatcher.UIThread.InvokeAsync(() => { S.BeginGroup(); _scriptStart = S.Current; }).GetTask();

    /// <summary>Finish the script's transaction; when it failed, undo everything it did. Returns whether anything was rolled back.</summary>
    public Task<bool> ScriptEndAsync(bool rollback) => Dispatcher.UIThread.InvokeAsync(() =>
    {
        S.EndGroup("Script");
        if (!rollback || _scriptStart == null || S.Current == _scriptStart) return false;
        S.Undo(); return true;
    }).GetTask();

    /// <summary>Identity of every command run_command can start, in the same form the command catalogue uses.</summary>
    public static IReadOnlyList<string> CommandKeys()
    {
        var keys = new List<string>();
        foreach (var make in Registry.Values) { try { keys.Add(CommandCatalog.KeyOf(make())); } catch { } }
        return keys;
    }

    static string CommandHint(string name)
    {
        var close = Registry.Keys.Where(k => k.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(k, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
        return close.Count > 0 ? $" Did you mean: {string.Join(", ", close)}?" : " Use list_commands(query) to search the names.";
    }

    readonly MainViewModel _vm;
    readonly Window? _window;
    CadSession S => _vm.Session;

    sealed record Tool(string Name, string Description, JsonObject Schema, Func<JsonObject, Task<JsonNode?>> Run);
    readonly Dictionary<string, Tool> _tools = [];

    public AutomationApi(MainViewModel vm, Window? window)
    {
        _vm = vm; _window = window;
        Register();
    }

    // ================================================================= protocol

    /// <summary>Tools that read/write files outright — never offered to the in-app assistant when "open, save and export files" is off.</summary>
    static readonly HashSet<string> RestrictedTools = new(StringComparer.OrdinalIgnoreCase) { "file", "mesh_export", "mesh_import", "drawing_export" };
    /// <summary>Otherwise-safe tools whose individual actions write files or run code.</summary>
    static readonly Dictionary<string, string[]> RestrictedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["script_editor"] = ["run", "save"],
        ["render_control"] = ["save"],
        ["render_setup"] = ["set_background"],
    };
    public const string RestrictedMessage = "File access is off for the assistant (\"Let the assistant open, save and export files\" is unchecked): saving, exporting, importing, opening files and running scripts are unavailable.";

    /// <summary>True when this call would touch the file system or run code — used for the restricted assistant endpoint and the in-app assistant when file access is off.</summary>
    public static bool Restricted(string name, JsonObject? args)
    {
        if (RestrictedTools.Contains(name)) return true;
        if (RestrictedActions.TryGetValue(name, out var acts) && acts.Contains(args?["action"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)) return true;
        if (name.Equals("batch", StringComparison.OrdinalIgnoreCase) && args?["calls"] is JsonArray calls)
            return calls.Any(c => c is JsonObject co && co["tool"] != null && Restricted(co["tool"]!.ToString(), co["args"] as JsonObject));
        return false;
    }

    public JsonArray Describe(bool restricted = false)
    {
        var arr = new JsonArray();
        foreach (var t in _tools.Values)
        {
            if (restricted && RestrictedTools.Contains(t.Name)) continue;
            arr.Add(new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.Schema.DeepClone() });
        }
        return arr;
    }

    public Task<JsonNode?> CallAsync(string name, JsonObject args, bool restricted = false) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (restricted && Restricted(name, args)) throw new InvalidOperationException(RestrictedMessage);
            if (!_tools.TryGetValue(name, out var t))
            {
                var close = _tools.Keys.Where(k => k.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(k, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
                throw new ArgumentException($"Unknown tool '{name}'." + (close.Count > 0 ? $" Did you mean: {string.Join(", ", close)}?" : " Call describe_api for the tool names.") + "");
            }
            if (t.Schema["required"] is JsonArray req)
            {
                var missing = req.Select(r => r!.ToString()).Where(r => args[r] == null).ToList();
                if (missing.Count > 0) throw new ArgumentException($"'{name}' needs: {string.Join(", ", missing)}. Arguments it accepts: {ArgSummary(t.Schema)}");
            }
            try { return await t.Run(args); }
            catch (Exception e) when (e is NullReferenceException or InvalidCastException or FormatException or System.Text.Json.JsonException || (e is InvalidOperationException ioe && ioe.Message.StartsWith("The node must be of type")))
            {
                // a wrong argument shape usually surfaces as one of these deep inside a tool: say what the tool expects instead
                throw new ArgumentException($"'{name}' could not use those arguments ({e.GetType().Name}). Arguments it accepts: {ArgSummary(t.Schema)}");
            }
        });

    static string ArgSummary(JsonObject schema)
    {
        var props = schema["properties"] as JsonObject; if (props == null || props.Count == 0) return "(none)";
        return string.Join("; ", props.Select(p => $"{p.Key} ({p.Value?["type"]?.ToString() ?? "any"}) {p.Value?["description"]}"));
    }

    // ================================================================= settings whitelist

    /// <summary>One safe-to-expose preference. Secrets (API keys, the automation token, recent files) are deliberately absent.</summary>
    sealed record SettingDef(string Key, string Label, string Page, string Type, string[]? Choices, JsonNode? Default, Func<JsonNode?> Get, Action<JsonNode?> Set);

    List<SettingDef> SettingsDefs()
    {
        var s = AppSettings.Current; var st = _vm;
        static JsonNode Need(JsonNode? v) => v ?? throw new ArgumentException("value is required.");
        static bool B(JsonNode? v) => v is JsonValue jv && jv.TryGetValue<bool>(out var b) ? b : bool.Parse(Need(v).ToString());
        static int I(JsonNode? v) => int.TryParse(Need(v).ToString(), out var i) ? i : throw new ArgumentException("value must be a whole number.");
        static string T(JsonNode? v) => Need(v).ToString();
        static string Pick(JsonNode? v, string[] choices) => choices.FirstOrDefault(c => c.Equals(T(v), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"value must be one of: {string.Join(", ", choices)}.");
        return
        [
            new("theme", "Theme", "appearance", "choice", ["Dark", "Light", "System"], "Dark", () => s.Theme, v => { s.Theme = Pick(v, ["Dark", "Light", "System"]); AppSettings.Save(); }),
            new("accent_color", "Interface accent color", "appearance", "choice", ["Blue", "Teal", "Purple", "Orange", "Rose"], "Blue", () => s.AccentColor, v => { s.AccentColor = Pick(v, ["Blue", "Teal", "Purple", "Orange", "Rose"]); AppSettings.Save(); }),
            new("grid_style", "3D grid style", "appearance", "choice", ["classic", "fine", "none"], "classic", () => s.GridStyle, v => { s.GridStyle = Pick(v, ["classic", "fine", "none"]); AppSettings.Save(); }),
            new("show_grid_on_start", "Show the grid when Prism starts", "appearance", "bool", null, true, () => s.ShowGridOnStart, v => { s.ShowGridOnStart = B(v); AppSettings.Save(); }),
            new("help_context_location", "Help context location", "interaction", "choice", ["viewport", "status_bar"], "viewport", () => s.HelpContextLocation, v => { s.HelpContextLocation = Pick(v, ["viewport", "status_bar"]); AppSettings.Save(); }),
            new("reverse_scroll_zoom", "Reverse scroll-wheel zoom", "interaction", "bool", null, false, () => s.ReverseScrollZoom, v => { s.ReverseScrollZoom = B(v); AppSettings.Save(); }),
            new("marking_menu", "Right-click marking menu", "interaction", "bool", null, true, () => s.MarkingMenu, v => { s.MarkingMenu = B(v); AppSettings.Save(); }),
            new("marking_hold_ms", "Hold time for the right-click menu (ms)", "interaction", "int", null, 260, () => s.MarkingHoldMs, v => { s.MarkingHoldMs = Math.Clamp(I(v), 60, 2000); AppSettings.Save(); }),
            new("workspace_path", "Workspace folder", "workspace", "path", null, null, () => s.WorkspacePath, v => { s.WorkspacePath = v?.ToString() is { Length: > 0 } p ? p : null; AppSettings.Save(); }),
            new("show_home_on_start", "Home screen at startup", "workspace", "bool", null, true, () => s.ShowHomeOnStart, v => { s.ShowHomeOnStart = B(v); AppSettings.Save(); }),
            new("auto_recovery", "Autosave and recovery copies", "workspace", "bool", null, false, () => s.AutoRecoveryEnabled, v => { s.AutoRecoveryEnabled = B(v); AppSettings.Save(); }),
            new("auto_recovery_minutes", "Recovery copy interval (minutes)", "workspace", "int", null, 5, () => s.AutoRecoveryIntervalMinutes, v => { s.AutoRecoveryIntervalMinutes = I(v); AppSettings.Save(); }),
            new("quick_bar", "Quick bar over the 3D view", "toolbars", "bool", null, true, () => s.ShowQuickBar, v => { s.ShowQuickBar = B(v); AppSettings.Save(); }),
            new("assistant_provider", "AI provider", "assistant", "choice", ["codex", "anthropic", "openai"], "codex", () => ((AiProvider)(st.AssistantProviderIndex + 1)) switch { AiProvider.CodexCli => "codex", AiProvider.Anthropic => "anthropic", _ => "openai" }, v => st.AssistantProviderIndex = Pick(v, ["codex", "anthropic", "openai"]) switch { "codex" => 0, "anthropic" => 1, _ => 2 }),
            new("assistant_model", "AI model id (pick from the model list or list)", "assistant", "string", null, "", () => AssistantService.ModelIdOf(st.AssistantModel), v => st.AssistantModel = AssistantService.ModelIdOf(T(v))),
            new("assistant_effort", "AI reasoning effort (empty = the model default)", "assistant", "string", null, "", () => st.CurrentEffort, v => { if (!st.SetAssistantEffort(T(v))) throw new ArgumentException($"Effort '{T(v)}' is not offered by the current model."); }),
            new("assistant_allow_files", "Let the assistant open, save and export files", "assistant", "bool", null, false, () => st.AssistantAllowFiles, v => st.AssistantAllowFiles = B(v)),
        ];
    }

    // ================================================================= schema helpers
    static JsonObject Schema(string[]? required, params (string name, string type, string desc)[] props)
    {
        var p = new JsonObject();
        foreach (var (n, t, d) in props)
        {
            var o = new JsonObject { ["description"] = d };
            if (t == "array") { o["type"] = "array"; o["items"] = new JsonObject(); }
            else if (t == "any") { }
            else o["type"] = t;
            p[n] = o;
        }
        var s = new JsonObject { ["type"] = "object", ["properties"] = p };
        if (required is { Length: > 0 }) s["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
        return s;
    }

    void Add(string name, string desc, JsonObject schema, Func<JsonObject, Task<JsonNode?>> run) => _tools[name] = new(name, desc, schema, run);
    void Add(string name, string desc, JsonObject schema, Func<JsonObject, JsonNode?> run) => _tools[name] = new(name, desc, schema, a => Task.FromResult(run(a)));

    static string Str(JsonObject a, string k, string? def = null) =>
        a[k] is JsonNode n ? n.ToString() : def ?? throw new ArgumentException($"Missing required argument '{k}'.");
    // numbers and booleans may arrive as text ("5", "true") from some clients: accept both
    static double Dbl(JsonObject a, string k, double def)
    {
        if (a[k] is not JsonNode n) return def;
        if (n is JsonValue v && v.TryGetValue<string>(out var str)) return double.TryParse(str, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : throw new ArgumentException($"'{k}' must be a number, not '{str}'.");
        if (n is JsonValue num && num.TryGetValue<double>(out var val)) return val;
        return double.TryParse(n.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new ArgumentException($"'{k}' must be a number.");
    }
    static bool Bool(JsonObject a, string k, bool def)
    {
        if (a[k] is not JsonNode n) return def;
        if (n is JsonValue v && v.TryGetValue<string>(out var str)) return bool.TryParse(str, out var b) ? b : throw new ArgumentException($"'{k}' must be true or false, not '{str}'.");
        return n.GetValue<bool>();
    }
    static Vec3 V3(JsonNode n) { var a = n.AsArray(); return new(a[0]!.GetValue<double>(), a[1]!.GetValue<double>(), a.Count > 2 ? a[2]!.GetValue<double>() : 0); }
    static JsonArray Arr(Vec3 v) => new(Math.Round(v.X, 4), Math.Round(v.Y, 4), Math.Round(v.Z, 4));

    // ================================================================= lookups
    /// <summary>Letters and digits only, lower case: lets "k_factor" find "K-factor" and "bend_radius" find "Bend radius (inside)".</summary>
    static string Compact(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    SceneObject FindObject(string key)
    {
        var o = S.Objects.FirstOrDefault(x => x.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
             ?? S.Objects.FirstOrDefault(x => x.Id.ToString().StartsWith(key, StringComparison.OrdinalIgnoreCase));
        return o ?? throw new ArgumentException($"No object named '{key}'. Objects: {string.Join(", ", S.Objects.Select(x => x.Name))}");
    }

    Core.Form.SubdMesh FormOf(string name) => FindObject(name).Form ?? throw new ArgumentException($"{name} is not a Form.");

    static JsonObject FormTopology(SubdMesh mesh)
    {
        var r = CageReport.Inspect(mesh);
        var validFaces = mesh.F.Where(f => f.Length >= 3 && f.Distinct().Count() == f.Length && f.All(v => v >= 0 && v < mesh.V.Count)).ToList();
        var used = validFaces.SelectMany(f => f).Distinct().Count();
        var edgeCount = validFaces.SelectMany(f => Enumerable.Range(0, f.Length).Select(i => SubdMesh.Key(f[i], f[(i+1)%f.Length]))).Distinct().Count();
        return new JsonObject
        {
            ["boundaryEdges"] = r.BoundaryEdges, ["nonManifoldEdges"] = r.NonManifoldEdges,
            ["windingConflicts"] = r.WindingConflicts, ["connectedComponents"] = r.Components,
            ["eulerCharacteristic"] = used - edgeCount + validFaces.Count,
            ["cageValid"] = r.Valid, ["invalidFaces"] = r.InvalidFaces, ["degenerateFaces"] = r.DegenerateFaces,
            ["nonFiniteVertices"] = r.NonFiniteVertices, ["isolatedVertices"] = r.IsolatedVertices,
            ["disconnectedVertexFans"] = r.DisconnectedVertexFans, ["extraordinaryVertices"] = r.ExtraordinaryVertices,
        };
    }

    SelItem ParseItem(JsonNode n)
    {
        if (n is JsonValue) return SelItem.Of(FindObject(n.ToString()));
        var o = n.AsObject();
        if (o["origin"] is JsonNode og)
        {
            // origin planes by name (XY, XZ, YZ) and axes (X axis, Y axis, Z axis), or by index
            string key = og.ToString().Trim();
            int idx = int.TryParse(key, out var i0) ? i0 : key.ToUpperInvariant() switch { "XY" => 0, "XZ" => 1, "YZ" => 2, "X" => 3, "Y" => 4, "Z" => 5, _ => -1 };
            if (idx < 0) idx = Array.FindIndex(CadSession.OriginNames, nm => nm.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) throw new ArgumentException($"Unknown origin item '{key}'. Use XY, XZ, YZ, X, Y or Z.");
            return new SelItem(Guid.Empty, SelKind.Origin, idx);
        }
        var obj = FindObject(Str(o, "object"));
        if (o["face"] is JsonNode f) return new SelItem(obj.Id, SelKind.Face, f.GetValue<int>() - 1);
        if (o["face_at"] is JsonArray fa)
        {
            // the face whose surface is closest to this point (handy because face numbers change after booleans)
            var md = obj.Mesh; var tm = new Meshing.TriMesh((float[])md.Positions.Clone(), (int[])md.Indices.Clone());
            var hit = tm.Bvh.ClosestPoint(new Vec3(fa[0]!.GetValue<double>(), fa[1]!.GetValue<double>(), fa[2]!.GetValue<double>())) ?? throw new ArgumentException("No face near that point.");
            return new SelItem(obj.Id, SelKind.Face, md.TriFace[hit.Tri]);
        }
        if (o["edge_at"] is JsonArray ea)
        {
            // the edge passing closest to this point
            var md = obj.Mesh; var E = md.EdgePoints; var q = new Vec3(ea[0]!.GetValue<double>(), ea[1]!.GetValue<double>(), ea[2]!.GetValue<double>());
            int best = -1; double bd = double.MaxValue;
            for (int s = 0; s < md.SegEdge.Length; s++)
            {
                var p0 = new Vec3(E[6 * s], E[6 * s + 1], E[6 * s + 2]); var p1 = new Vec3(E[6 * s + 3], E[6 * s + 4], E[6 * s + 5]);
                var d = p1 - p0; double l2 = Vec3.Dot(d, d), t = l2 < 1e-18 ? 0 : Math.Clamp(Vec3.Dot(q - p0, d) / l2, 0, 1);
                double dist = (p0 + d * t - q).Length;
                if (dist < bd) { bd = dist; best = md.SegEdge[s]; }
            }
            if (best < 0) throw new ArgumentException("No edge near that point.");
            return new SelItem(obj.Id, SelKind.Edge, best);
        }
        if (o["edge"] is JsonNode e) return new SelItem(obj.Id, SelKind.Edge, e.GetValue<int>() - 1);
        return SelItem.Of(obj);
    }

    JsonArray DocsJson() => new(_vm.Documents.Select((d, i) => (JsonNode)new JsonObject { ["index"] = i, ["title"] = d.Title, ["active"] = d.IsActive, ["dirty"] = d.IsDirty, ["objects"] = d.Session.Objects.Count }).ToArray());

    JsonObject StudyJson()
    {
        var st = S.ActiveStudy ?? throw new InvalidOperationException("No study yet. Call study_new.");
        var j = new JsonObject
        {
            ["name"] = st.Name, ["body"] = S.Find(st.BodyId)?.Name, ["elementSize"] = st.ElementSize,
            ["fixtures"] = new JsonArray(st.Fixtures.Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["kind"] = f.Kind.ToString(), ["faces"] = f.Faces.Count }).ToArray()),
            ["loads"] = new JsonArray(st.Loads.Select(l => (JsonNode)new JsonObject { ["name"] = l.Name, ["kind"] = l.Kind.ToString(), ["value"] = l.Value, ["faces"] = l.Faces.Count }).ToArray()),
        };
        if (st.Results is { } r)
            j["results"] = new JsonObject
            {
                ["elements"] = r.Mesh.TetCount, ["nodes"] = r.Mesh.NodeCount, ["minQuality"] = Math.Round(r.Mesh.MinQuality, 3),
                ["maxVonMises_MPa"] = Math.Round(r.MaxVonMises, 3), ["yield_MPa"] = double.IsFinite(r.Yield)?JsonValue.Create(Math.Round(r.Yield,3)):null, ["maxDisplacement_mm"] = Math.Round(r.MaxDisplacement, 5), ["minSafetyFactor"] = double.IsFinite(r.MinSafety)?JsonValue.Create(Math.Round(r.MinSafety,3)):null,
                ["reactionNet_N"] = Math.Round(r.Solution.Reaction.Length, 3), ["appliedNet_N"] = Math.Round(r.Solution.Applied, 3), ["solveSeconds"] = Math.Round(r.SolveSeconds, 2), ["material"] = r.MaterialName,
                ["notes"] = new JsonArray(r.Notes.Select(n => (JsonNode)n).ToArray()),
            };
        return j;
    }

    JsonObject Describe(SceneObject o, bool detail = false)
    {
        if (o.Form is { } fm)
        {
            var (mn, mx) = fm.Bounds();
            var fj = new JsonObject
            {
                ["id"] = o.Id.ToString("N")[..8], ["name"] = o.Name, ["kind"] = "Form", ["visible"] = o.Visible, ["color"] = $"#{o.Color & 0xFFFFFF:X6}",
                ["min"] = Arr(mn), ["max"] = Arr(mx), ["vertices"] = fm.V.Count, ["faces"] = fm.F.Count, ["closed"] = fm.IsClosed(), ["smoothLevel"] = fm.Level,
            };
            if (detail) { fj["size"] = Arr(mx - mn); fj["center"] = Arr((mn + mx) * 0.5); fj["appearance"] = AppearanceJson(o); }
            return fj;
        }
        if (o.Tri is { } tm)
        {
            var rep = Meshing.MeshAnalysis.Report(tm);
            return new JsonObject
            {
                ["id"] = o.Id.ToString("N")[..8], ["name"] = o.Name, ["kind"] = "Mesh", ["visible"] = o.Visible, ["color"] = $"#{o.Color & 0xFFFFFF:X6}",
                ["min"] = Arr(tm.Min), ["max"] = Arr(tm.Max), ["triangles"] = tm.TriCount, ["vertices"] = tm.VertexCount,
                ["watertight"] = rep.Closed, ["printable"] = rep.Printable, ["holes"] = rep.HoleLoops, ["shells"] = rep.Shells, ["volume"] = Math.Round(rep.Volume, 3), ["area"] = Math.Round(rep.Area, 3),
            };
        }
        var m = o.Mesh;
        var j = new JsonObject
        {
            ["id"] = o.Id.ToString("N")[..8], ["name"] = o.Name, ["kind"] = o.Kind.ToString(), ["visible"] = o.Visible,
            ["color"] = $"#{o.Color & 0xFFFFFF:X6}", ["min"] = Arr(m.Min), ["max"] = Arr(m.Max),
            ["faces"] = m.FaceCount, ["edges"] = m.EdgeCount,
        };
        if(o.FlatMap is { } map)j["flatPattern"]=new JsonObject{["refoldable"]=true,["width"]=map.Width,["height"]=map.Height,["x"]=map.FlatX,["y"]=map.FlatY,["sourceFace"]=map.FaceIndex+1};
        if (detail)
        {
            j["wires"] = o.Shape.Count(SubKind.Wire);
            if (o.SketchPlane is { } p) j["sketchPlane"] = new JsonObject { ["origin"] = Arr(p.Origin), ["u"] = Arr(p.U), ["v"] = Arr(p.V), ["normal"] = Arr(p.Normal) };
            var pr = o.Shape.Props();
            if (o.Shape.HasSolids) j["volume"] = Math.Round(pr.Volume, 4);          // an open surface has no volume
            j["area"] = Math.Round(pr.Area, 4); j["size"] = Arr(pr.Size); j["center"] = Arr(pr.Center);
            j["valid"] = o.Shape.IsValid;
            j["appearance"] = AppearanceJson(o);
        }
        return j;
    }

    static JsonObject AppearanceJson(SceneObject o) => new()
    {
        ["materialId"] = o.Look.MaterialId, ["material"] = o.Material?.Name,
        ["metal"] = o.Look.Metal, ["gloss"] = o.Look.Gloss,
        ["pattern"] = o.Look.Pattern.ToString(), ["patternScale_mm"] = o.Look.PatternScale, ["patternAngle_deg"] = o.Look.PatternAngle,
        ["physicalDataRevision"] = o.Material?.Evidence.Revision, ["physicalDataPinned"] = o.Look.PhysicalSnapshot != null,
        ["physicalProperties"] = o.Material is { } m ? new JsonObject {
            ["density_g_cm3"]=m.Density,["youngs_GPa"]=m.Youngs,["poisson"]=m.Poisson,
            ["yield_MPa"]=double.IsFinite(m.Yield)?JsonValue.Create(m.Yield):null,["strength_MPa"]=m.Ultimate,["strengthBasis"]=m.Evidence.StrengthBasis,
            ["conductivity_W_mK"]=m.Conductivity,["expansion_um_mK"]=m.Expansion,["specificHeat_J_kgK"]=m.SpecificHeat,
            ["source"]=m.Evidence.Source,["condition"]=m.Evidence.Condition,["checkedProperties"]=m.Evidence.CheckedProperties
        } : null,
    };

    string Describe(SelItem it) => S.Describe(it);

    /// <summary>The state of a command panel. Brief (used after command_set / form_select) lists just the current values of the visible parameters, which keeps replies small; full (run_command preview, get_state) also lists types, options and actions.</summary>
    JsonObject CommandJson(Command? c, bool full = true)
    {
        if (c == null) return new JsonObject();
        if (!full)
        {
            var vals = new JsonObject();
            foreach (var p in c.Params.Where(p => p.IsVisible))
                switch (p)
                {
                    case NumberParam n: vals[p.Label] = n.Value; break;
                    case IntParam i: vals[p.Label] = i.Value; break;
                    case BoolParam b: vals[p.Label] = b.Value; break;
                    case ChoiceParam ch: vals[p.Label] = ch.Selected; break;
                    case TextParam tp: vals[p.Label] = tp.Value; break;
                }
            var sl = new JsonObject();
            foreach (var s in c.Slots.Where(s => s.IsVisible)) sl[s.Label] = new JsonArray(s.Items.Select(i => (JsonNode)Describe(i)).ToArray());
            return new JsonObject
            {
                ["title"] = c.Title, ["values"] = vals, ["slots"] = sl,
                ["results"] = new JsonArray(c.Results.Select(r => (JsonNode)$"{r.Label}: {r.Value}").ToArray()), ["error"] = S.CommandError,
                ["note"] = "Brief view. get_state (or run_command with commit:false) lists every parameter with its type, options and the action buttons.",
            };
        }
        var ps = new JsonArray();
        foreach (var p in c.Params)
        {
            var j = new JsonObject { ["label"] = p.Label, ["visible"] = p.IsVisible };
            switch (p)
            {
                case NumberParam n: j["type"] = "number"; j["value"] = n.Value; j["unit"] = n.Unit; break;
                case IntParam i: j["type"] = "integer"; j["value"] = i.Value; break;
                case BoolParam b: j["type"] = "boolean"; j["value"] = b.Value; break;
                case ChoiceParam ch: j["type"] = "choice"; j["value"] = ch.Selected; j["options"] = new JsonArray(ch.Options.Select(o => (JsonNode)o).ToArray()); break;
                case TextParam tp: j["type"] = "text"; j["value"] = tp.Value; break;
                case ActionParam: j["type"] = "action"; break;
            }
            ps.Add(j);
        }
        var slots = new JsonArray();
        foreach (var s in c.Slots)
            slots.Add(new JsonObject
            {
                ["label"] = s.Label, ["multi"] = s.Multi, ["optional"] = s.Optional, ["visible"] = s.IsVisible, ["active"] = s.IsActive,
                ["items"] = new JsonArray(s.Items.Select(i => (JsonNode)Describe(i)).ToArray()),
            });
        var results = new JsonArray(c.Results.Select(r => (JsonNode)new JsonObject { ["label"] = r.Label, ["value"] = r.Value, ["level"] = r.Level }).ToArray());
        return new JsonObject { ["title"] = c.DisplayTitle, ["hint"] = c.PanelHint, ["panelStep"] = c.HasPanelStep, ["params"] = ps, ["slots"] = slots, ["results"] = results, ["error"] = S.CommandError };
    }

    /// <summary>The short form of a sketch state: status, degrees of freedom and the entity list (no point coordinates or constraint detail). Ask for verbose:true or call sketch_state for everything.</summary>
    static JsonObject CompactSketch(JsonObject j)
    {
        var r = new JsonObject { ["status"] = j["status"]?.DeepClone(), ["degreesOfFreedom"] = j["degreesOfFreedom"]?.DeepClone(), ["fullyConstrained"] = j["fullyConstrained"]?.DeepClone() };
        if (j["added"] != null) r["added"] = j["added"]!.DeepClone();
        r["entities"] = new JsonArray((j["entities"] as JsonArray ?? []).Select(e => (JsonNode)JsonValue.Create($"{e!["id"]} {e["type"]}")!).ToArray());
        r["constraintCount"] = (j["constraints"] as JsonArray)?.Count ?? 0;
        r["activePlane"] = j["activePlane"]?.DeepClone(); r["planeCount"] = j["planeCount"]?.DeepClone();
        return r;
    }

    JsonObject SketchStateJson()
    {
        var sk = S.Sketch ?? throw new InvalidOperationException("No sketch is open.");
        var m = sk.Model; var r = sk.Result;
        var cons = new JsonArray();
        foreach (var c in m.Constraints.Where(c => !c.Hidden))
        {
            var ip = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
            cons.Add(new JsonObject
            {
                ["id"] = c.Id, ["type"] = c.Type.ToString(), ["dimension"] = c.IsDimension,
                ["refs"] = new JsonArray(c.Refs.Select((id, i) => (JsonNode)((ip[i] ? "p" : "e") + id)).ToArray()),
                ["value"] = c.IsDimension || c.Type == CType.Fixed ? Math.Round(c.Value, 5) : null,
            });
        }
        return new JsonObject
        {
            ["status"] = r.Status, ["converged"] = r.Converged, ["degreesOfFreedom"] = r.Dof, ["fullyConstrained"] = r.FullyConstrained,
            ["editing"] = sk.EditingId != null,
            ["activePlane"] = sk.ActivePlane + 1, ["planeCount"] = sk.DrawingPlanes.Count,
            ["plane"] = new JsonObject { ["origin"] = Arr(sk.Plane.Origin), ["u"] = Arr(sk.Plane.U), ["v"] = Arr(sk.Plane.V), ["normal"] = Arr(sk.Plane.Normal) },
            ["snapOtherCurves"] = sk.SnapOtherCurves, ["projectOtherCurves"] = sk.ProjectOtherCurves,
            ["points"] = new JsonArray(m.Points.Select(p => (JsonNode)new JsonObject { ["id"] = "p" + p.Id, ["u"] = Math.Round(p.X, 5), ["v"] = Math.Round(p.Y, 5) }).ToArray()),
            ["entities"] = new JsonArray(m.Entities.Select(e => (JsonNode)new JsonObject
            {
                ["id"] = "e" + e.Id, ["type"] = e.Type.ToString(), ["construction"] = e.Construction,
                ["points"] = new JsonArray(m.PointsOf(e).Select(i => (JsonNode)("p" + i)).ToArray()),
                ["radius"] = e.Type is EntType.Circle or EntType.Arc ? Math.Round(m.Radius(e), 5) : null,
                ["free"] = r.FreeEntities.Contains(e.Id),
                ["curvePoints"] = e.Type == EntType.Spline ? new JsonArray(Enumerable.Range(1, 3).Select(k => { var sm = m.SplineSamples(e); var q = sm[sm.Count * k / 4]; return (JsonNode)new JsonArray(Math.Round(q.x, 4), Math.Round(q.y, 4)); }).ToArray()) : null,
            }).ToArray()),
            ["constraints"] = cons,
        };
    }

    JsonObject State()
    {
        var cam = S.Camera;
        var st = new JsonObject
        {
            ["status"] = S.Status, ["statusIsError"] = S.StatusIsError, ["document"] = S.FilePath ?? "(unsaved)", ["dirty"] = S.Dirty,
            ["objects"] = new JsonArray(S.Objects.Select(o => (JsonNode)Describe(o)).ToArray()),
            ["selection"] = new JsonArray(S.Selection.Select(i => (JsonNode)Describe(i)).ToArray()),
            ["selectionMode"] = S.Mode.ToString(),
            ["canUndo"] = S.CanUndo, ["canRedo"] = S.CanRedo,
            ["display"] = S.Display.ToString(), ["xray"] = S.XRay, ["zebra"] = S.Zebra, ["grid"] = S.ShowGrid, ["orthographic"] = S.Ortho,
            ["camera"] = new JsonObject { ["target"] = Arr(cam.Target), ["distance"] = Math.Round(cam.Distance, 3), ["yawDeg"] = Math.Round(cam.Yaw * 180 / Math.PI, 2), ["pitchDeg"] = Math.Round(cam.Pitch * 180 / Math.PI, 2) },
            ["activeCommand"] = S.ActiveCommand != null ? CommandJson(S.ActiveCommand) : null,
            ["render"] = RenderJson(),
            ["cae"] = CaeJson(),
            ["tool"] = S.ActiveTool?.Prompt,
        };
        if (S.Sketch is { } sk)
            st["sketch"] = new JsonObject { ["entities"] = sk.Model.Entities.Count, ["tool"] = sk.Tool is { } tl ? tl.Name + (tl.Mode.Length > 0 ? " (" + tl.Mode + ")" : "") : null, ["plane"] = new JsonObject { ["origin"] = Arr(sk.Plane.Origin), ["u"] = Arr(sk.Plane.U), ["v"] = Arr(sk.Plane.V) } };
        return st;
    }

    JsonNode? RenderJson() => S.Render is { } r ? new JsonObject
    {
        ["running"] = r.Running, ["finished"] = r.Finished, ["samples"] = r.Samples,
        ["totalSamples"] = r.TotalSamples, ["seconds"] = Math.Round(r.Seconds, 2),
        ["width"] = r.Width, ["height"] = r.Height, ["status"] = r.Status,
        ["savedTo"] = r.SavedTo, ["error"] = r.Error,
    } : null;

    JsonNode? CaeJson() => S.Cae is { } job ? new JsonObject
    {
        ["running"]=job.Running,["status"]=job.Status,["error"]=job.Error,
        ["resultFolder"]=job.Outcome?.Folder,["kind"]=job.Outcome?.Kind.ToString(),
        ["frequenciesHz"]=job.Outcome is { } r ? new JsonArray(r.Frequencies.Select(f=>(JsonNode)JsonValue.Create(f)!).ToArray()) : null,
        ["minimumTemperature"]=job.Outcome is {Temperatures.Length:>0} t ? JsonValue.Create(t.Temperatures.Min()) : null,
        ["maximumTemperature"]=job.Outcome is {Temperatures.Length:>0} t2 ? JsonValue.Create(t2.Temperatures.Max()) : null,
    } : null;

    // ================================================================= command helpers
    static readonly Dictionary<string, Func<Command>> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cae_select_study"] = () => new SelectStudyCommand(), ["cae_save_faces"] = () => new NamedFacesCommand(), ["cae_recall_faces"] = () => new RecallFacesCommand(), ["cae_saved"] = () => new SavedCaeCommand(), ["cae_convergence"] = () => new ConvergenceCommand(), ["cae_export"] = () => new ExportStudyCommand(),
        ["cam_setup"] = () => new CamSetupCommand(), ["cam_playback"] = () => new CamPlaybackCommand(), ["cam_review"] = () => new CamReviewCommand(), ["cam_import_gcode"] = () => new ImportGcodeCommand(),
        ["fdm_check"] = () => new FdmCheckCommand(), ["resin_check"] = () => new ResinCheckCommand(), ["cnc_design_check"] = () => new CncDesignCheckCommand(), ["cnc_stock_review"] = () => new CncStockCommand(),
        ["cam_select_setup"] = () => new SelectCamSetupCommand(), ["cam_operation_order"] = () => new CamProgramOrderCommand(), ["resin_export"] = () => new PrintExportCommand(true), ["print_export"] = () => new PrintExportCommand(),
        ["cam_tool_library"] = () => new CamToolLibraryCommand(),
        ["cnc_rough"] = () => new Rough3DCommand(),
        ["cae_pipe_network"] = () => new PipeNetworkCommand(), ["cae_flow"] = () => new FlowStudyCommand(), ["cae_thermal"] = () => new CaeSolveCommand(true), ["cae_modal"] = () => new CaeSolveCommand(false), ["cae_pipe_flow"] = () => new PipeFlowCommand(), ["cae_fatigue"] = () => new FatigueCommand(), ["cae_kinematics"] = () => new MotionStudyCommand(false), ["cae_motion"] = () => new MotionStudyCommand(true), ["cae_robot_path"] = () => new MotionStudyCommand(MotionKind.Robot), ["motion_spring"] = () => new MotionSpringCommand(), ["motion_load_to_static"] = () => new MotionLoadToStaticCommand(), ["motion_force"] = () => new MotionLoadCommand(false), ["motion_torque"] = () => new MotionLoadCommand(true), ["gear_pair"] = () => new CouplingCommand(), ["rack_pinion"] = () => new CouplingCommand(),
        ["pcb_board"] = () => new PcbBoardCommand(), ["pcb_from_outline"] = () => new PcbOutlineCommand(), ["component_envelope"] = () => new ComponentEnvelopeCommand(), ["pcb_hole"] = () => new PcbHoleCommand(),
        ["match_surface"] = () => new MatchSurfaceCommand(), ["surface_continuity"] = () => new SurfaceContinuityCommand(), ["check_cage"] = () => new CheckCageCommand(),
        ["pattern_linear"] = () => new LinearPatternCommand(), ["pattern_circular"] = () => new CircularPatternCommand(), ["pattern_curve"] = () => new CurvePatternCommand(),
        ["pattern_fill"] = () => new FillPatternCommand(), ["pattern_table"] = () => new TablePatternCommand(),
        ["mesh_select"] = () => new MeshRegionCommand(), ["mesh_section_sketch"] = () => new MeshSectionSketchCommand(), ["mesh_fit_surface"] = () => new MeshFitSurfaceCommand(), ["form_from_scan"] = () => new FormFromScanCommand(),
        ["mesh_repair"] = () => new MeshRepairCommand(), ["mesh_fill_holes"] = () => new MeshRepairCommand(true), ["mesh_check"] = () => new MeshCheckCommand(),
        ["mesh_reduce"] = () => new MeshDecimateCommand(), ["mesh_smooth"] = () => new MeshSmoothCommand(), ["mesh_offset"] = () => new MeshOffsetCommand(), ["mesh_split"] = () => new MeshCutCommand(),
        ["mesh_section"] = () => new MeshSectionCommand(), ["mesh_thickness"] = () => new MeshThicknessCommand(), ["mesh_overhang"] = () => new MeshOverhangCommand(),
        ["mesh_deviation"] = () => new MeshDeviationCommand(), ["mesh_fit"] = () => new MeshSegmentCommand(), ["mesh_align"] = () => new MeshAlignCommand(), ["mesh_rebuild"] = () => new MeshRebuildCommand(), ["mesh_section_fit"] = () => new MeshSectionFitCommand(),
        ["mesh_to_solid"] = () => new MeshToSolidCommand(), ["solid_to_mesh"] = () => new SolidToMeshCommand(),
        ["sim_fixed"] = () => new FixtureCommand(Simulation.FixtureKind.Fixed), ["sim_roller"] = () => new FixtureCommand(Simulation.FixtureKind.Slider), ["sim_fixed_dirs"] = () => new FixtureCommand(Simulation.FixtureKind.Directional),
        ["sim_force"] = () => new LoadCommand(Simulation.LoadKind.Force), ["sim_pressure"] = () => new LoadCommand(Simulation.LoadKind.Pressure), ["sim_torque"] = () => new LoadCommand(Simulation.LoadKind.Torque), ["sim_gravity"] = () => new LoadCommand(Simulation.LoadKind.Gravity),
        ["sim_probe"] = () => new ProbeCommand(),
        ["measure"] = () => new MeasureCommand(), ["properties"] = () => new PropertiesCommand(), ["section"] = () => new SectionCommand(),
        ["interference"] = () => new InterferenceCommand(), ["check"] = () => new CheckCommand(), ["curvature"] = () => new CurvatureCommand(), ["draft"] = () => new DraftCommand(),
        ["box"] = () => new BoxCommand(), ["cylinder"] = () => new CylinderCommand(), ["sphere"] = () => new SphereCommand(),
        ["cone"] = () => new ConeCommand(), ["torus"] = () => new TorusCommand(),
        ["hole"] = () => new HoleCommand(),
        ["extrude"] = () => new ExtrudeCommand(), ["extrude_surface"] = () => new ExtrudeCommand(true), ["rib"] = () => new RibCommand(), ["emboss"] = () => new EmbossCommand(),
        ["revolve"] = () => new RevolveCommand(), ["revolve_surface"] = () => new RevolveCommand(true),
        ["sweep"] = () => new SweepCommand(), ["sweep_surface"] = () => new SweepCommand(true),
        ["loft"] = () => new LoftCommand(), ["loft_surface"] = () => new LoftCommand(true),
        ["fillet"] = () => new FilletCommand(), ["chamfer"] = () => new ChamferCommand(), ["shell"] = () => new ShellCommand(),
        ["union"] = () => new BooleanCommand(BooleanCommand.Kind.Union), ["subtract"] = () => new BooleanCommand(BooleanCommand.Kind.Subtract),
        ["intersect"] = () => new BooleanCommand(BooleanCommand.Kind.Intersect),
        ["move"] = () => new TransformCommand(TransformCommand.Kind.Move), ["rotate"] = () => new TransformCommand(TransformCommand.Kind.Rotate),
        ["scale"] = () => new TransformCommand(TransformCommand.Kind.Scale), ["mirror"] = () => new TransformCommand(TransformCommand.Kind.Mirror),
        ["curve_intersect"] = () => new CurveIntersectCommand(),
        ["curve_fillet"] = () => new CurveFilletCommand(false), ["curve_chamfer"] = () => new CurveFilletCommand(true), ["curve_offset"] = () => new OffsetCurveCommand(),
        ["curve_blend"] = () => new BlendCurveCommand(), ["curve_join"] = () => new JoinCurvesCommand(), ["curve_explode"] = () => new ExplodeCurveCommand(),
        ["curve_trim"] = () => new TrimCurveCommand(), ["curve_split"] = () => new SplitCurveCommand(), ["curve_extend"] = () => new ExtendCurveCommand(),
        ["curve_divide"] = () => new DivideCurveCommand(), ["curve_rebuild"] = () => new RebuildCurveCommand(), ["curve_project"] = () => new ProjectCurveCommand(),
        ["curve_isocurve"] = () => new IsoCurveCommand(), ["curve_intersect_surfaces"] = () => new IntersectSurfacesCommand(), ["curve_contour"] = () => new ContourCommand(),
        ["curve_duplicate_edge"] = () => new DuplicateEdgeCommand(), ["curve_curvature"] = () => new CurveCurvatureCommand(),
        ["edge_surface"] = () => new EdgeSurfaceCommand(), ["blend_surface"] = () => new BlendSurfaceCommand(), ["pipe"] = () => new PipeCommand(),
        ["sweep_2_rails"] = () => new SweepTwoRailsCommand(), ["network_surface"] = () => new NetworkSurfaceCommand(), ["deform"] = () => new DeformCommand(), ["edit_points"] = () => new EditPointsCommand(), ["rebuild_surface"] = () => new RebuildSurfaceCommand(),
        ["refold"] = () => new RefoldCommand(),
        ["planar_surface"] = () => new PlanarSurfaceCommand(), ["ruled_surface"] = () => new RuledSurfaceCommand(), ["patch"] = () => new PatchCommand(),
        ["offset_surface"] = () => new OffsetSurfaceCommand(), ["thicken"] = () => new ThickenCommand(), ["knit"] = () => new SewCommand(),
        ["form_plane"] = () => new FormPlaneCommand(), ["form_box"] = () => new FormBoxCommand(), ["form_sphere"] = () => new FormSphereCommand(),
        ["form_cylinder"] = () => new FormCylinderCommand(), ["form_torus"] = () => new FormTorusCommand(),
        ["edit_form"] = () => new EditFormCommand(), ["create_face"] = () => new CreateFaceCommand(),
        ["nurbs_patch"] = () => new NurbsPatchCommand(), ["curve_control"] = () => new CurveByPointsCommand(false), ["curve_through"] = () => new CurveByPointsCommand(true), ["sketch_3d"] = () => new Sketch3DCommand(), ["construction_plane"] = () => new ConstructionPlaneCommand(), ["render"] = () => new RenderCommand(), ["form_loft"] = () => new FormLoftCommand(), ["form_sweep"] = () => new FormSweepCommand(), ["form_extrude"] = () => new FormExtrudeCommand(), ["place_image"] = () => new PlaceImageCommand(), ["edit_image"] = () => new EditImageCommand(), ["set_image_scale"] = () => new CalibrateImageCommand(), ["contour_flange"] = () => new ContourFlangeCommand(), ["fold"] = () => new FoldCommand(), ["edge_flange"] = () => new EdgeFlangeCommand(), ["flat_pattern"] = () => new FlatPatternCommand(), ["unstitch"] = () => new UnstitchCommand(), ["untrim"] = () => new UntrimCommand(), ["extend_surface"] = () => new ExtendSurfaceCommand(), ["reverse_normal"] = () => new ReverseNormalCommand(), ["drawing"] = () => new DrawingCommand(), ["form_revolve"] = () => new FormRevolveCommand(), ["form_pipe"] = () => new FormPipeCommand(), ["trim_surface"] = () => new TrimSurfaceCommand(), ["boundary_fill"] = () => new BoundaryFillCommand(), ["fill_opening"] = () => new FillOpeningCommand(), ["generative"] = () => new GenerativeCommand(), ["cnc_profile"] = () => new ProfileCommand(), ["cnc_pocket"] = () => new PocketCommand(), ["cnc_drill"] = () => new DrillCommand(), ["cnc_face"] = () => new FaceMillCommand(), ["cnc_post"] = () => new CncPostCommand(), ["slice"] = () => new SliceCommand(), ["toolpath_view"] = () => new ToolpathViewCommand(), ["push_pull"] = () => new PushPullCommand(), ["draft_face"] = () => new DraftFaceCommand(), ["replace_face"] = () => new ReplaceFaceCommand(), ["move_face"] = () => new MoveFaceCommand(), ["split_body"] = () => new SplitBodyCommand(), ["move_hole"] = () => new MoveHoleCommand(), ["delete_face"] = () => new DeleteFaceCommand(), ["offset_face"] = () => new OffsetFaceCommand(), ["rotate_face"] = () => new RotateFaceCommand(), ["remove_fillets"] = () => new RemoveFilletsCommand(), ["split_face"] = () => new SplitFaceCommand(), ["merge_faces"] = () => new MergeFacesCommand(), ["fill_cap"] = () => new FillCapCommand(), ["extend_trim_face"] = () => new ExtendTrimFaceCommand(), ["match_face"] = () => new MatchFaceCommand(), ["flatten"] = () => new FlattenCommand(), ["warp"] = () => new WarpCommand(), ["intersect_curves"] = () => new IntersectCurvesCommand(), ["render_animation"] = () => new RenderAnimationCommand(), ["ar"] = () => new ArCommand(), ["insert_component"] = () => new InsertComponentCommand(), ["joint_rigid"] = () => new JointCommand(Core.Assembly.JointType.Rigid), ["joint_revolute"] = () => new JointCommand(Core.Assembly.JointType.Revolute), ["joint_slider"] = () => new JointCommand(Core.Assembly.JointType.Slider), ["joint_cylindrical"] = () => new JointCommand(Core.Assembly.JointType.Cylindrical), ["drive_joints"] = () => new DriveJointsCommand(), ["move_component"] = () => new MoveComponentCommand(), ["explode"] = () => new ExplodeCommand(), ["animation"] = () => new AnimationCommand(), ["curve_equation"] = () => new EquationCurveCommand(false), ["curve_table"] = () => new EquationCurveCommand(true),
    };

    // Actions (e.g. Run checks) are collected, not run: inside ConfigureInputs a command's OnChanged is suppressed, so an action run there would do nothing visible.
    List<ActionParam> ApplyParams(Command c, JsonObject? ps)
    {
        var actions = new List<ActionParam>();
        if (ps == null) return actions;
        foreach (var (key, val) in ps)
        {
            if (val == null) continue;
            var p = c.Params.FirstOrDefault(x => x.Label.Equals(key, StringComparison.OrdinalIgnoreCase))
                 ?? c.Params.FirstOrDefault(x => x.Label.Contains(key, StringComparison.OrdinalIgnoreCase))
                 ?? c.Params.FirstOrDefault(x => Compact(x.Label).Contains(Compact(key)))
                 ?? throw new ArgumentException($"Command '{c.Title}' has no parameter '{key}'. Parameters: {string.Join(", ", c.Params.Select(x => x.Label))}");
            switch (p)
            {
                case NumberParam n: n.Value = val.GetValue<double>(); break;
                case IntParam i: i.Value = (int)val.GetValue<double>(); break;
                case BoolParam b: b.Value = val.GetValue<bool>(); break;
                case TextParam t: t.Value = val.ToString(); break;
                case ChoiceParam ch:
                    int idx = val is JsonValue jv && jv.TryGetValue<double>(out var d) ? (int)d : ch.Options.ToList().FindIndex(o => o.Contains(val.ToString(), StringComparison.OrdinalIgnoreCase));
                    if (idx < 0 || idx >= ch.Options.Count) throw new ArgumentException($"'{val}' is not an option of '{key}'. Options: {string.Join(", ", ch.Options)}");
                    ch.Index = idx; break;
                case ActionParam a: if (!(val is JsonValue av && av.TryGetValue<bool>(out var on) && !on)) actions.Add(a); break;
            }
        }
        return actions;
    }

    void ApplySlots(Command c, JsonObject? slots)
    {
        if (slots == null) return;
        foreach (var (key, val) in slots)
        {
            var slot = c.Slots.FirstOrDefault(x => x.Label.Equals(key, StringComparison.OrdinalIgnoreCase))
                    ?? c.Slots.FirstOrDefault(x => x.Label.Contains(key, StringComparison.OrdinalIgnoreCase))
                    ?? c.Slots.FirstOrDefault(x => Compact(x.Label).Contains(Compact(key)))
                    ?? throw new ArgumentException($"Command '{c.Title}' has no selection slot '{key}'. Slots: {string.Join(", ", c.Slots.Select(x => x.Label))}");
            var items = (val is JsonArray ja ? ja.Select(x => x!).ToList() : [val!]).Select(ParseItem).ToList();
            if (slot.Multi && items.Distinct().Count() > slot.MaxCount)
                throw new ArgumentException($"'{slot.Label}' accepts at most {slot.MaxCount} selections.");
            slot.ResetChain();
            if (slot.ProfileSelection) slot.SelectionRuleIndex = items.Any(it => it.Kind == SelKind.Edge) ? 1 : 0;
            slot.Items.Clear();
            foreach (var it in items)
            {
                if (!S.Matches(it, slot.Filter)) throw new ArgumentException($"'{S.Describe(it)}' is not accepted by slot '{slot.Label}'.");
                slot.TryAdd(it);
                if (!slot.Multi) break;
            }
        }
    }

    sealed record BodySnap(string Name, Shape? Shape, double Volume, double Area, int Faces, int Edges, Vec3 Size);

    Dictionary<Guid, BodySnap> Snapshot()
    {
        var d = new Dictionary<Guid, BodySnap>();
        foreach (var o in S.Objects)
        {
            if (!IsBody(o)) { d[o.Id] = new(o.Name, null, 0, 0, 0, 0, default); continue; }
            try { var sh = o.Shape; var p = sh.Props(); d[o.Id] = new(o.Name, sh, sh.HasSolids ? p.Volume : 0, p.Area, sh.Count(SubKind.Face), sh.Count(SubKind.Edge), p.Size); }
            catch { d[o.Id] = new(o.Name, null, 0, 0, 0, 0, default); }
        }
        return d;
    }

    JsonObject CommandResult(Dictionary<Guid, BodySnap> before)
    {
        var created = S.Objects.Where(o => !before.ContainsKey(o.Id)).Select(o => (JsonNode)Describe(o, true)).ToArray();
        var res = new JsonObject { ["status"] = S.Status, ["statusIsError"] = S.StatusIsError, ["created"] = new JsonArray(created), ["objectCount"] = S.Objects.Count };
        var changed = new JsonArray();
        foreach (var o in S.Objects)
        {
            if (!before.TryGetValue(o.Id, out var b) || b.Shape == null || !IsBody(o)) continue;
            Shape sh; try { sh = o.Shape; } catch { continue; }
            if (ReferenceEquals(sh, b.Shape)) continue;
            var p = sh.Props(); double vol = sh.HasSolids ? p.Volume : 0; int nf = sh.Count(SubKind.Face), ne = sh.Count(SubKind.Edge);
            var c = new JsonObject
            {
                ["name"] = o.Name, ["volume"] = Math.Round(vol, 4), ["volumeDelta"] = Math.Round(vol - b.Volume, 4),
                ["areaDelta"] = Math.Round(p.Area - b.Area, 4), ["faces"] = $"{b.Faces}->{nf}", ["edges"] = $"{b.Edges}->{ne}",
                ["size"] = Arr(p.Size), ["valid"] = sh.IsValid,
            };
            if (!Arr(b.Size).ToJsonString().Equals(Arr(p.Size).ToJsonString())) c["sizeBefore"] = Arr(b.Size);
            changed.Add(c);
        }
        if (changed.Count > 0) res["changed"] = changed;
        var removed = before.Where(kv => S.Find(kv.Key) == null).Select(kv => (JsonNode)kv.Value.Name).ToArray();
        if (removed.Length > 0) res["removed"] = new JsonArray(removed);
        foreach (var n in created) if (n is JsonObject cj && cj["valid"] is { } v && !v.GetValue<bool>()) res["warning"] = $"'{cj["name"]}' is not a valid shape (run solid_check).";
        return res;
    }

    // ================================================================= tools
    void Register()
    {
        RegisterDrawingDocument();
        RegisterSolid();
        Add("get_state", "Full snapshot: objects (with bounding boxes), selection, active command and its parameters/slots, sketch, camera, display flags, status line.",
            Schema(null, ("fields", "array", "Only these top-level keys, e.g. [\"objects\",\"selection\",\"status\"]. Default: all."),
                ("objects", "string", "full (default) | names (one 'name [kind]' string per object, far smaller) | none."),
                ("limit", "integer", "Most objects to list; objectsTotal gives the real count.")), a =>
            {
                var st = State();
                var mode = Str(a, "objects", "full");
                if (st["objects"] is JsonArray all)
                {
                    int total = all.Count, limit = a["limit"] != null ? Math.Max(0, (int)Dbl(a, "limit", total)) : total;
                    var kept = all.Take(limit).Select(o => o!.DeepClone()).ToList();
                    st["objects"] = mode.Equals("none", StringComparison.OrdinalIgnoreCase) ? null
                        : new JsonArray(kept.Select(o => mode.Equals("names", StringComparison.OrdinalIgnoreCase) ? (JsonNode)$"{o["name"]} [{o["kind"]}]" : o).ToArray());
                    st["objectsTotal"] = total;
                }
                if (a["fields"] is JsonArray fields && fields.Count > 0)
                {
                    var want = fields.Select(f => f!.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase); want.Add("objectsTotal");
                    foreach (var key in st.Select(p => p.Key).ToList()) if (!want.Contains(key)) st.Remove(key);
                }
                return st;
            });

        Add("wait_idle", "Wait until the view has stopped animating (after view, a tool that turns the camera, Tab in 3D sketch...) so the next pointer event or screenshot sees the final camera. Optional extra ms to wait afterwards.",
            Schema(null, ("timeout_ms", "integer", "Longest wait (default 3000)."), ("extra_ms", "integer", "Extra pause after it settles (default 100).")), async a =>
            {
                int limit = (int)Dbl(a, "timeout_ms", 3000), extra = (int)Dbl(a, "extra_ms", 100); var sw = System.Diagnostics.Stopwatch.StartNew();
                while (S.IsAnimating && sw.ElapsedMilliseconds < limit) await Task.Delay(30);
                await Task.Delay(extra);
                return new JsonObject { ["settled"] = !S.IsAnimating, ["waited_ms"] = sw.ElapsedMilliseconds };
            });

        Add("get_log", "Recent status/error messages shown in the status bar (newest last).",
            Schema(null, ("count", "integer", "How many entries (default 30).")), a =>
            {
                int n = (int)Dbl(a, "count", 30);
                return new JsonArray(S.Log.TakeLast(n).Select(l => (JsonNode)new JsonObject { ["time"] = l.Time.ToString("HH:mm:ss"), ["error"] = l.IsError, ["message"] = l.Message }).ToArray());
            });

        Add("screenshot", "Capture the app window as a PNG. Whole window by default (ribbon, panels, viewport); viewport:true captures only the 3D view, which is what you usually want to judge a model and costs far fewer tokens. max_width shrinks the picture (e.g. 900).",
            Schema(null, ("viewport", "boolean", "Only the 3D viewport."), ("max_width", "integer", "Scale down to at most this many pixels wide."), ("wait_ms", "integer", "Milliseconds to wait for a fresh frame before capturing (default 250).")), async a =>
            {
                S.Invalidate();
                await Task.Delay((int)Dbl(a, "wait_ms", 250));
                bool viewportOnly = a["viewport"]?.GetValue<bool>() == true;
                int maxWidth = a["max_width"] != null ? Math.Clamp((int)Dbl(a, "max_width", 0), 64, 8000) : 0;
                byte[] png;
                if (!viewportOnly && maxWidth == 0) png = WindowCapture.CapturePng(_window);
                else
                {
                    var window = _window ?? throw new InvalidOperationException("No window.");
                    double scale = window.RenderScaling; int x = 0, y = 0, w = (int)(window.Bounds.Width * scale), h = (int)(window.Bounds.Height * scale);
                    if (viewportOnly)
                    {
                        var view = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Prism.Rendering.GlViewport>().FirstOrDefault() ?? throw new InvalidOperationException("The 3D viewport is not visible.");
                        var origin = Avalonia.VisualExtensions.TranslatePoint(view, new Avalonia.Point(0, 0), window) ?? throw new InvalidOperationException("The 3D viewport is not visible.");
                        x = (int)(origin.X * scale); y = (int)(origin.Y * scale); w = (int)(view.Bounds.Width * scale); h = (int)(view.Bounds.Height * scale);
                    }
                    var shot = WindowCapture.CaptureThumbnail(window, x, y, w, h, maxWidth == 0 ? int.MaxValue : maxWidth) ?? throw new InvalidOperationException("Could not capture the window.");
                    png = Prism.Rendering.PngWriter.Encode(shot.pixels, shot.width, shot.height);
                }
                return new JsonObject { ["_image"] = new JsonObject { ["mime"] = "image/png", ["data"] = Convert.ToBase64String(png) } };
            });

        Add("list_ribbon", "List every ribbon tab, group and button (label, enabled, checked). Use with ribbon_click.",
            Schema(null), _ =>
            {
                var tabs = new JsonArray();
                foreach (var t in _vm.Tabs)
                {
                    var groups = new JsonArray();
                    foreach (var g in t.Groups)
                        groups.Add(new JsonObject
                        {
                            ["group"] = g.Title,
                            ["buttons"] = new JsonArray(g.Items.OfType<Ribbon.RibbonButton>().Select(b => { b.Refresh(); return (JsonNode)new JsonObject { ["label"] = b.Label.Replace("\n", " "), ["enabled"] = b.IsEnabled, ["checked"] = b.IsChecked, ["description"] = b.Description }; }).ToArray()),
                        });
                    tabs.Add(new JsonObject { ["tab"] = t.Title, ["visible"] = t.IsVisible, ["groups"] = groups });
                }
                return tabs;
            });

        Add("ribbon_click", "Click a ribbon button by its label (exactly what a user would click). Optionally pass 'tab' to disambiguate (e.g. 'Extrude' exists on Solid and Surface). Commands open their panel; use command_set / command_commit next.",
            Schema(["label"], ("label", "string", "Button label, e.g. 'Box', 'Create Sketch', 'Finish Sketch'."), ("tab", "string", "Tab name, e.g. 'Solid', 'Surface', 'Freeform'.")), a =>
            {
                var label = Str(a, "label").Replace("\n", " ");
                var tab = a["tab"]?.ToString();
                var hits = _vm.Tabs.Where(t => tab == null || t.Title.Equals(tab, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(t => t.Buttons.Select(b => (t, b)))
                    .Where(x => !x.b.HasMenu && x.b.Label.Replace("\n", " ").Equals(label, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 0) throw new ArgumentException($"No ribbon button '{label}'. Call list_ribbon.");
                var pick = hits.FirstOrDefault(h => { h.b.Refresh(); return h.b.IsEnabled; });
                if (pick.b == null) throw new InvalidOperationException($"'{label}' is currently disabled (e.g. no selection, or wrong mode).");
                _vm.EnsureTabVisible(pick.t);
                _vm.SelectTab(pick.t);
                pick.b.Invoke();
                _vm.RibbonCollapsed = false;
                return State();
            });

        Add("list_commands", "List the command names run_command accepts, optionally only those containing 'query' (case-insensitive). Without a query returns the count and the first 60.",
            Schema(null, ("query", "string", "Text the name contains, e.g. 'fillet' or 'cam'."), ("limit", "integer", "Most names to return (default 60).")), a =>
            {
                var query = Str(a, "query", "");
                var limit = a["limit"] != null ? Math.Clamp(a["limit"]!.GetValue<int>(), 1, 500) : 60;
                var names = Registry.Keys.Where(k => query.Length == 0 || k.Contains(query, StringComparison.OrdinalIgnoreCase)).Order().ToList();
                return new JsonObject { ["count"] = names.Count, ["names"] = new JsonArray(names.Take(limit).Select(n => (JsonNode)n).ToArray()) };
            });

        Add("describe_command", "What a command needs before you run it: its selection slots (what each accepts, whether it is optional or takes several) and its parameters (type, default, unit, range, options). Nothing is started or changed. Then pass slots and params to run_command using these labels.",
            Schema(["name"], ("name", "string", "Command name from list_commands.")), a =>
            {
                var name = Str(a, "name");
                if (!Registry.TryGetValue(name, out var make)) throw new ArgumentException($"Unknown command '{name}'." + CommandHint(name));
                var c = make();
                var slots = new JsonArray(c.Slots.Select(s => (JsonNode)new JsonObject { ["label"] = s.Label, ["accepts"] = s.Filter.ToString(), ["multi"] = s.Multi, ["optional"] = s.Optional }).ToArray());
                var ps = new JsonArray();
                foreach (var p in c.Params)
                {
                    var j = new JsonObject { ["label"] = p.Label };
                    switch (p)
                    {
                        case NumberParam n:
                            j["type"] = "number"; j["default"] = n.Value; j["unit"] = n.Unit;
                            if (n.Min > double.MinValue / 2) j["min"] = n.Min;
                            if (n.Max < double.MaxValue / 2) j["max"] = n.Max;
                            break;
                        case IntParam i: j["type"] = "integer"; j["default"] = i.Value; j["min"] = i.Min; j["max"] = i.Max; break;
                        case BoolParam b: j["type"] = "boolean"; j["default"] = b.Value; break;
                        case ChoiceParam ch: j["type"] = "choice"; j["default"] = ch.Selected; j["options"] = new JsonArray(ch.Options.Select(o => (JsonNode)o).ToArray()); break;
                        case TextParam tp: j["type"] = "text"; j["default"] = tp.Value; break;
                        case ActionParam: j["type"] = "action"; break;
                    }
                    if (p.IsAdvanced) j["advanced"] = true;
                    if (p.HelpText.Length > 0) j["help"] = p.HelpText;
                    ps.Add(j);
                }
                return new JsonObject { ["name"] = name, ["title"] = c.Title, ["hint"] = c.Hint, ["slots"] = slots, ["params"] = ps,
                    ["note"] = "Slots and parameters some commands add only once started are not listed; run_command with commit:false shows them." };
            });

        Add("run_command", "Run a modeling command end-to-end: start it, fill selection slots and parameters, then (by default) commit. " +
            "Find names with list_commands(query). 'slots' maps a slot label to an object name, {object,face|edge} or an array of those; 'params' maps parameter labels to values. " +
            "For curve_control/curve_through pass 'points' as [[x,y,z],...]. Use get_state afterwards, or call with commit=false to preview first.",
            Schema(["name"], ("name", "string", "Command name."), ("slots", "object", "Slot label -> selection."), ("params", "object", "Parameter label -> value."),
                ("points", "array", "Curve points for curve_control / curve_through."), ("commit", "boolean", "Commit immediately (default true).")), a =>
            {
                var name = Str(a, "name");
                if (!Registry.TryGetValue(name, out var make)) throw new ArgumentException($"Unknown command '{name}'." + CommandHint(name));
                var before = Snapshot();
                var cmd = make(); cmd.Factory = make;
                _vm.CloseHome();
                S.StartCommand(cmd);
                if (S.ActiveCommand != cmd) throw new InvalidOperationException(S.Status);
                try
                {
                    var actions = new List<ActionParam>();
                    cmd.ConfigureInputs(() =>
                    {
                        ApplySlots(cmd, a["slots"] as JsonObject);
                        actions = ApplyParams(cmd, a["params"] as JsonObject);
                        if (a["points"] is JsonArray pts && cmd is CurveByPointsCommand cc) cc.SetPoints(pts.Select(p => V3(p!)));
                    });
                    cmd.OnChanged();
                    foreach (var act in actions) act.Run();
                    if (Bool(a, "commit", true))
                    {
                        S.CommitCommand();
                        if (S.ActiveCommand == cmd)
                        {
                            var err = cmd.Error ?? S.Status; S.CancelCommand();
                            throw new InvalidOperationException($"{cmd.Title} failed: {err}. Slots: {string.Join("; ", cmd.Slots.Where(x => x.IsVisible).Select(x => $"{x.Label} ({x.Items.Count} picked)"))}. Params: {string.Join(", ", cmd.Params.Where(x => x.IsVisible).Select(x => x.Label))}. Check that slots hold the right kind of item (solid_find returns face/edge indices) and values are in range.");
                        }
                    }
                }
                catch { if (S.ActiveCommand == cmd) S.CancelCommand(); throw; }
                var res = CommandResult(before);
                if (S.ActiveCommand != null) res["activeCommand"] = CommandJson(S.ActiveCommand);
                return res;
            });

        Add("command_set", "Change parameters and/or selection slots of the command currently open in the panel (live preview updates).",
            Schema(null, ("params", "object", "Parameter label -> value."), ("slots", "object", "Slot label -> selection.")), a =>
            {
                var c = S.ActiveCommand ?? throw new InvalidOperationException("No command is active.");
                var actions = new List<ActionParam>();
                c.ConfigureInputs(() => { ApplySlots(c, a["slots"] as JsonObject); actions = ApplyParams(c, a["params"] as JsonObject); });
                c.OnChanged();
                foreach (var act in actions) act.Run();
                return CommandJson(c, full: false);
            });

        Add("command_commit", "Press OK on the active command.", Schema(null), _ =>
        {
            var c = S.ActiveCommand ?? throw new InvalidOperationException("No command is active.");
            var before = Snapshot();
            bool panelStep=c.HasPanelStep;
            S.AcceptCommandPanel();
            if (!panelStep && S.ActiveCommand == c) throw new InvalidOperationException(c.Error ?? S.Status);
            return CommandResult(before);
        });

        Add("command_cancel", "Cancel the active command (or the sketch if one is open and no command is active).", Schema(null), _ =>
        {
            if (S.ActiveCommand != null) S.CancelCommandPanel(); else if (S.Sketch != null) S.ExitSketch(false);
            return State();
        });

        Add("select", "Set the document selection (outside of commands). Items are object names, or {object, face} / {object, edge} (1-based).",
            Schema(["items"], ("items", "array", "Selection items."), ("additive", "boolean", "Add to the existing selection.")), a =>
            {
                var items = a["items"]!.AsArray().Select(x => ParseItem(x!)).ToList();
                if (!Bool(a, "additive", false)) S.ClearSelection();
                foreach (var it in items) if (!S.Selection.Contains(it)) S.SetSelection(it, true);
                return new JsonArray(S.Selection.Select(i => (JsonNode)Describe(i)).ToArray());
            });

        Add("object_info", "Detailed information about one object: volume, area, size, center, validity, sketch plane.",
            Schema(["object"], ("object", "string", "Object name.")), a => Describe(FindObject(Str(a, "object")), true));

        Add("object_update", "Rename, recolor or show/hide an object.",
            Schema(["object"], ("object", "string", "Object name."), ("name", "string", "New name."), ("color", "string", "Hex colour like #E5484D."), ("visible", "boolean", "Visibility.")), a =>
            {
                var o = FindObject(Str(a, "object"));
                S.Update(o.Id, x =>
                {
                    if (a["name"] != null) x = x.WithName(a["name"]!.ToString());
                    if (a["color"] != null) x = x.WithColor(0xFF000000 | Convert.ToUInt32(a["color"]!.ToString().TrimStart('#'), 16));
                    if (a["visible"] != null) x = x.WithVisible(a["visible"]!.GetValue<bool>());
                    return x;
                });
                var u = FindObject(a["name"]?.ToString() ?? o.Name);
                return new JsonObject { ["name"] = u.Name, ["kind"] = u.Kind.ToString(), ["visible"] = u.Visible, ["color"] = $"#{u.Color & 0xFFFFFF:X6}" };
            });

        Add("material_list", "List library materials with engineering data (density g/cm3, modulus GPa, yield/tensile MPa, conductivity, expansion, max temp, ratings). Short by default (id, name, category, density); pass brief:false for the full engineering data. Optional 'category' or 'search' filter.",
            Schema(null, ("category", "string", "e.g. Steel, Aluminum, Plastic."), ("search", "string", "Text to match in name, family or uses."), ("brief", "boolean", "Default true: id, name, category and density only.")), a =>
            {
                if (Bool(a, "brief", true))
                {
                    var c0 = a["category"]?.ToString(); var q0 = a["search"]?.ToString();
                    return new JsonObject { ["materials"] = new JsonArray(Materials.All.Where(m => (c0 == null || m.Category.Equals(c0, StringComparison.OrdinalIgnoreCase)) && (q0 == null || $"{m.Name} {m.Category} {m.Uses}".Contains(q0, StringComparison.OrdinalIgnoreCase)))
                        .Select(m => (JsonNode)new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["category"] = m.Category, ["density"] = m.Density }).ToArray()) };
                }
                var cat = a["category"]?.ToString(); var q = a["search"]?.ToString();
                var list = Materials.All.Where(m => (cat == null || m.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)) && (q == null || $"{m.Name} {m.Category} {m.Uses}".Contains(q, StringComparison.OrdinalIgnoreCase)));
                return new JsonObject { ["materials"] = new JsonArray(list.Select(m => (JsonNode)new JsonObject
                {
                    ["id"] = m.Id, ["name"] = m.Name, ["category"] = m.Category, ["density"] = m.Density, ["youngs_GPa"] = m.Youngs, ["yield_MPa"] = double.IsNaN(m.Yield) ? null : m.Yield,
                    ["tensile_MPa"] = m.Ultimate, ["poisson"] = m.Poisson, ["conductivity"] = m.Conductivity, ["expansion"] = m.Expansion, ["max_temp_C"] = m.MaxTemp,
                    ["strengthBasis"] = m.Evidence.StrengthBasis, ["specificHeat_J_kgK"] = m.SpecificHeat,
                    ["dataRevision"] = m.Evidence.Revision, ["condition"] = m.Evidence.Condition, ["source"] = m.Evidence.Source, ["checkedProperties"] = m.Evidence.CheckedProperties,
                    ["isotropicStructural"] = m.Evidence.IsotropicStructural, ["isotropicThermal"] = m.Evidence.IsotropicThermal,
                    ["cost"] = m.Cost, ["corrosion"] = m.Corrosion, ["machinability"] = m.Machining, ["uses"] = m.Uses, ["watch_out"] = m.Watch,
                }).ToArray()) };
            });
        Add("material_apply", "Give an object a library material (color, finish and density come with it), or just set its finish/color. 'material' is an id or name from material_list; explicit visual overrides survive material assignment. Omit it to only edit appearance. Returns the object's mass when it is a solid.",
            Schema(["object"], ("object", "string", "Object name."), ("material", "string", "Material id or name."), ("metal", "number", "0..1"), ("gloss", "number", "0..1"), ("color", "string", "Hex colour override like #C87941."), ("clear", "boolean", "Remove the material."),
                ("pattern","string","Automatic | Plain | Wood | Brushed | Cast | CarbonWeave. Appearance only."),("patternScale","number","Pattern size in mm, 0.1–10000."),("patternAngle","number","Pattern angle about world Z, degrees."), ("roughness","number","0 polished to 1 matte; exclusive with gloss."), ("surface","object","Replace visual layers using SurfaceStyle JSON: Gradient 0..4, Axis 0..2, Color2/3/4 ARGB integers, GradientSize/Offset/Angle, Weathering 0..2, Coverage, PatchSize, WeatheringRoughness, BottomOnly, WeatherHeight, Seed, Bump, Coat, CoatRoughness, TextureSize/Angle and embedded ColorMap/RoughnessMap/BumpMap."), ("resetAppearance","boolean","Restore assigned material/default appearance.")), a =>
            {
                var o = FindObject(Str(a, "object"));
                Material? m = null;
                if (a["material"] != null)
                {
                    var key = a["material"]!.ToString();
                    m = Materials.Find(key) ?? Materials.All.FirstOrDefault(x => x.Name.Contains(key, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Unknown material '{key}'. Call material_list.");
                }
                S.Update(o.Id, x =>
                {
                    if (m != null) x = x.WithMaterial(m);
                    if (Bool(a, "clear", false)) x = x.WithLook(x.Look with { MaterialId = null, PhysicalSnapshot = null });
                    if (a["metal"] != null) {double value=a["metal"]!.GetValue<double>();if(!double.IsFinite(value)||value<0||value>1)throw new ArgumentException("Metallic must be between 0 and 1.");x=x.WithLook(x.Look with {Metal=(float)value});}
                    if (a["gloss"] != null) {double value=a["gloss"]!.GetValue<double>();if(!double.IsFinite(value)||value<0||value>1)throw new ArgumentException("Gloss must be between 0 and 1.");x=x.WithLook(x.Look with {Gloss=(float)value});}
                    if(a["pattern"]!=null)
                    {
                        if(!Enum.TryParse<SurfacePattern>(a["pattern"]!.ToString(),true,out var pattern)||!Enum.IsDefined(pattern))throw new ArgumentException("Unknown surface pattern.");
                        x=x.WithLook(x.Look with{Pattern=pattern});
                    }
                    if(a["patternScale"]!=null){double value=a["patternScale"]!.GetValue<double>();if(!double.IsFinite(value)||value<.1||value>10000)throw new ArgumentException("Pattern scale must be 0.1–10000 mm.");x=x.WithLook(x.Look with{PatternScale=(float)value});}
                    if(a["patternAngle"]!=null){double value=a["patternAngle"]!.GetValue<double>();if(!double.IsFinite(value))throw new ArgumentException("Pattern angle must be finite.");x=x.WithLook(x.Look with{PatternAngle=(float)(value%360)});}
                    if (a["roughness"] != null)
                    {
                        if(a["gloss"]!=null)throw new ArgumentException("Supply roughness or gloss, not both.");
                        double value=a["roughness"]!.GetValue<double>();if(!double.IsFinite(value)||value<0||value>1)throw new ArgumentException("Roughness must be between 0 and 1.");
                        x=x.WithLook(x.Look with {Gloss=1-(float)value});
                    }
                    if (a["surface"] is { } surface)
                    {
                        var style=(System.Text.Json.JsonSerializer.Deserialize<SurfaceStyle>(surface.ToJsonString())??new()).Validated() with {Override=true};
                        foreach(var data in new[]{style.ColorMap,style.RoughnessMap,style.BumpMap,style.DecalMap}) if(data!=null)_=Rendering.SurfaceImage.Decode(data,data==style.DecalMap);
                        x=x.WithLook(x.Look with {Surface=style});
                    }
                    if (a["color"] != null)
                    {
                        if(!ColorCode.TryParse(a["color"]!.ToString(),out uint color))throw new ArgumentException("Invalid color code.");
                        x=x.WithColor(color);
                    }
                    if(new[]{"color","metal","gloss","roughness","pattern","patternScale","patternAngle"}.Any(key=>a[key]!=null))
                        x=x.WithLook(x.Look with {Surface=(x.Look.Surface??new()) with {Override=true}});
                    if(Bool(a,"resetAppearance",false))x=x.WithColor(x.Material?.Color??Palette.DefaultFor(x.Kind,0)).WithLook(x.Material?.Look??Appearance.Default);
                    return x;
                }, label: m != null ? $"Material: {m.Name}" : "Appearance");
                var n = FindObject(o.Name); var r = new JsonObject { ["object"] = n.Name, ["material"] = n.Material?.Name, ["metal"] = n.Look.Metal, ["gloss"] = n.Look.Gloss, ["color"] = $"#{n.Color & 0xFFFFFF:X6}" };
                if (n.Material is { } nm && n.Kind == ObjKind.Solid) r["mass_g"] = Math.Round(n.Shape.Props().Volume * nm.Density / 1000.0, 3);
                return r;
            });
        Add("render_control", "Inspect or control the current document's render. action: status | stop | save | close. Start with run_command(name=render, commit=false), then command_set parameters including 'Start render'. Status reports sample progress, completion and errors; save writes completed samples to PNG.",
            Schema(["action"], ("action", "string", "status | stop | save | close"), ("path", "string", "Optional full PNG path for save.")), a =>
            {
                switch (Str(a, "action").ToLowerInvariant())
                {
                    case "status": break;
                    case "stop": S.StopRender(); break;
                    case "close": S.CloseRender(); break;
                    case "save":
                        if (S.SaveRender(a["path"]?.ToString()) == null) throw new InvalidOperationException(S.Status);
                        break;
                    default: throw new ArgumentException("action must be status, stop, save or close.");
                }
                return RenderJson();
            });
        Add("render_setup", "The picture-wide render choices stored on the document. action: state (current setup) | set (env, background, exposure, fit, offsets) | set_background (import a PNG/JPEG file as the backdrop image; produces it first with run_script if asked to generate one).",
            Schema(["action"], ("action", "string", "state | set | set_background"), ("env", "string", "studio | sky | dark_studio | lights_only | hdr"), ("background", "string", "environment | white | transparent | green | black | gradient | image"),
                ("exposure", "number", "0.1-10."), ("fit", "string", "fill | fit | stretch (background image only)."), ("offset_x", "number", "-1..1"), ("offset_y", "number", "-1..1"),
                ("path", "string", "Full path of the image file for set_background (≤16 MB; the generated folder " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "generated") + " is the right place for AI-made backdrops).")), a =>
            {
                JsonObject SetupJson()
                {
                    var st = S.RenderSetup;
                    return new JsonObject
                    {
                        ["env"] = st.Env.ToString().ToLowerInvariant().Replace("darkstudio", "dark_studio").Replace("lightsonly", "lights_only"), ["background"] = st.Background.ToString().ToLowerInvariant(),
                        ["ground"] = st.Ground, ["exposure"] = st.Exposure, ["has_background_image"] = st.BackgroundImage != null, ["image_fit"] = st.ImageFit.ToString().ToLowerInvariant(),
                        ["image_offset_x"] = st.ImageOffsetX, ["image_offset_y"] = st.ImageOffsetY,
                    };
                }
                switch (Str(a, "action").ToLowerInvariant())
                {
                    case "state": return SetupJson();
                    case "set":
                    {
                        var st = S.RenderSetup;
                        if (a["env"] != null) st.Env = Str(a, "env").ToLowerInvariant() switch { "studio" => RenderEnv.Studio, "sky" => RenderEnv.Sky, "dark_studio" => RenderEnv.DarkStudio, "lights_only" => RenderEnv.LightsOnly, "hdr" => RenderEnv.Hdr, var u => throw new ArgumentException($"Unknown env '{u}'.") };
                        if (a["background"] != null) st.Background = Str(a, "background").ToLowerInvariant() switch { "environment" => RenderBg.Environment, "white" => RenderBg.White, "transparent" => RenderBg.Transparent, "green" => RenderBg.Green, "black" => RenderBg.Black, "gradient" => RenderBg.Gradient, "image" => RenderBg.Image, var u => throw new ArgumentException($"Unknown background '{u}'.") };
                        if (a["exposure"] != null) st.Exposure = Math.Clamp(Dbl(a, "exposure", st.Exposure), .1, 10);
                        if (a["fit"] != null) st.ImageFit = Str(a, "fit").ToLowerInvariant() switch { "fill" => BackgroundFit.Fill, "fit" => BackgroundFit.Fit, "stretch" => BackgroundFit.Stretch, var u => throw new ArgumentException($"Unknown fit '{u}'.") };
                        if (a["offset_x"] != null) st.ImageOffsetX = Math.Clamp(Dbl(a, "offset_x", st.ImageOffsetX), -1, 1);
                        if (a["offset_y"] != null) st.ImageOffsetY = Math.Clamp(Dbl(a, "offset_y", st.ImageOffsetY), -1, 1);
                        if (st.Background == RenderBg.Image && st.BackgroundImage == null) throw new ArgumentException("Background 'image' needs an image first: set_background with a path.");
                        S.RenderSetup = st; S.Dirty = true; S.RaiseState();
                        return SetupJson();
                    }
                    case "set_background":
                    {
                        var st = S.RenderSetup;
                        st.BackgroundImage = Rendering.SurfaceImage.Import(Str(a, "path"));   // validates size and decodes
                        st.Background = RenderBg.Image;
                        S.RenderSetup = st; S.Dirty = true; S.RaiseState();
                        return SetupJson();
                    }
                    default: throw new ArgumentException("action must be state, set or set_background.");
                }
            });
        Add("script_editor", "The built-in script editor (one Python file). action: open | state | get | set (replace ALL of the script; keeps a rollback point) | insert (text before 'line', default: at the end) | rollback (restore the previous version; repeat to step back) | run | save.",
            Schema(["action"], ("action", "string", "open | state | get | set | insert | rollback | run | save"), ("text", "string", "Source code for set/insert."), ("line", "integer", "Insert before this 1-based line; default appends."), ("path", "string", "Save path for save.")), async a =>
            {
                var s = _vm.Script;
                JsonObject ScriptJson() => new()
                {
                    ["open"] = _vm.ShowScriptPanel, ["name"] = s.DisplayName, ["path"] = s.Path, ["language"] = s.Language.Id,
                    ["lines"] = s.Text.Length == 0 ? 0 : s.Text.Split('\n').Length, ["dirty"] = s.Dirty, ["running"] = s.Running, ["snapshots"] = s.AiSnapshotCount,
                };
                switch (Str(a, "action").ToLowerInvariant())
                {
                    case "open": _vm.ShowScriptPanel = true; if (s.Path == null && s.Text.Length == 0) s.New(); return ScriptJson();
                    case "state": return ScriptJson();
                    case "get": return new JsonObject { ["text"] = s.Text };
                    case "set": _vm.ShowScriptPanel = true; if (s.Path == null && s.Text.Length == 0) s.New(); s.SetAiText(Str(a, "text")); return ScriptJson();
                    case "insert": _vm.ShowScriptPanel = true; if (s.Path == null && s.Text.Length == 0) s.New(); s.InsertAiText(a["line"] is { } ln ? (int?)ln.GetValue<int>() : null, Str(a, "text")); return ScriptJson();
                    case "rollback": return new JsonObject { ["restored"] = s.RollbackAi(), ["snapshots"] = s.AiSnapshotCount };
                    case "run": return new JsonObject { ["exit_code"] = await s.RunAsync() };
                    case "save": s.Save(a["path"]?.ToString()); return ScriptJson();
                    default: throw new ArgumentException("action must be open, state, get, set, insert, rollback, run or save.");
                }
            });
        Add("settings", "Application and assistant settings. action: list | get | set | reset | open (open the Settings window at 'page'). 'key' comes from list, 'value' for set. API keys and the automation token are never exposed.",
            Schema(["action"], ("action", "string", "list | get | set | reset | open"), ("key", "string", "Setting key from list."), ("value", "any", "New value."), ("page", "string", "Settings page for open: appearance | interaction | workspace | toolbars | ribbon | assistant | keyboard")), a =>
            {
                var act = Str(a, "action").ToLowerInvariant();
                if (act == "open") { _vm.RequestSettings?.Invoke(Str(a, "page", "appearance").ToLowerInvariant()); return new JsonObject { ["opened"] = true }; }
                var defs = SettingsDefs();
                JsonObject Describe(SettingDef d) => new() { ["key"] = d.Key, ["label"] = d.Label, ["page"] = d.Page, ["type"] = d.Type, ["value"] = d.Get(), ["choices"] = d.Choices is { } c ? new JsonArray(c.Select(x => (JsonNode)x).ToArray()) : null };
                SettingDef Find() => defs.FirstOrDefault(d => d.Key.Equals(Str(a, "key"), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Unknown setting key. Call settings action=list for the keys.");
                switch (act)
                {
                    case "list": return new JsonObject { ["settings"] = new JsonArray(defs.Select(d => (JsonNode)Describe(d)).ToArray()) };
                    case "get": { var d = Find(); return new JsonObject { ["key"] = d.Key, ["value"] = d.Get(), ["choices"] = d.Choices is { } c ? new JsonArray(c.Select(x => (JsonNode)x).ToArray()) : null }; }
                    case "set": { var d = Find(); d.Set(a["value"]); return new JsonObject { ["key"] = d.Key, ["value"] = d.Get() }; }
                    case "reset": { var d = Find(); d.Set(d.Default); return new JsonObject { ["key"] = d.Key, ["value"] = d.Get() }; }
                    default: throw new ArgumentException("action must be list, get, set, reset or open.");
                }
            });
        Add("materials_panel", "Open (open=true) or close the Materials workspace tab. Optionally select a library material in it.",
            Schema(null, ("open", "boolean", "Default true."), ("material", "string", "Material id to highlight in the list.")), a =>
            {
                if (Bool(a, "open", true)) _vm.OpenMaterials(); else _vm.CloseMaterials();
                if (a["material"] != null) _vm.SelectedMaterialRow = _vm.MaterialList.FirstOrDefault(r => r.Material.Id.Equals(a["material"]!.ToString(), StringComparison.OrdinalIgnoreCase) || r.Material.Name.Contains(a["material"]!.ToString(), StringComparison.OrdinalIgnoreCase));
                return State();
            });

        Add("mesh_import", "Import a mesh file (STL, OBJ, PLY, 3MF) as one or more mesh objects. Without options it imports as-is (mm, Z-up, no repair). Colours and parts in the file are kept.",
            Schema(["path"], ("path", "string", "Full path of the file."), ("units", "string", "auto (default: the 3MF's own unit, else mm) | mm | cm | m | inch."), ("up_axis", "string", "z (default, keep) | y (rotate Y-up to Z-up) | x (rotate X-up to Z-up)."),
                ("weld_tolerance", "number", "Merge vertices closer than this many mm (default 0: identical corners only)."), ("auto_repair", "boolean", "Merge, clean, fix normals and fill small holes (default false)."),
                ("split", "string", "auto (default: 3MF objects become separate bodies) | none | groups (file parts: 3MF object, OBJ object/group, STL solid) | components (connected pieces).")), a =>
        {
            var opts = new Meshing.MeshImportOptions
            {
                Units = Str(a, "units", "auto").ToLowerInvariant() switch { "mm" or "millimeter" or "millimetre" => Meshing.ImportUnits.Millimeter, "cm" or "centimeter" => Meshing.ImportUnits.Centimeter, "m" or "meter" or "metre" => Meshing.ImportUnits.Meter, "in" or "inch" or "inches" => Meshing.ImportUnits.Inch, "auto" => Meshing.ImportUnits.Auto, var u => throw new ArgumentException($"Unknown units '{u}'. Use auto, mm, cm, m or inch.") },
                UpAxis = Str(a, "up_axis", "z").ToLowerInvariant() switch { "z" => Meshing.ImportUpAxis.Z, "y" => Meshing.ImportUpAxis.Y, "x" => Meshing.ImportUpAxis.X, var u => throw new ArgumentException($"Unknown up_axis '{u}'. Use z, y or x.") },
                WeldTolerance = Math.Max(0, Dbl(a, "weld_tolerance", 0)), AutoRepair = Bool(a, "auto_repair", false),
                Split = Str(a, "split", "auto").ToLowerInvariant() switch { "auto" => Meshing.ImportSplit.Auto, "none" => Meshing.ImportSplit.None, "groups" => Meshing.ImportSplit.Groups, "components" => Meshing.ImportSplit.Components, var u => throw new ArgumentException($"Unknown split '{u}'. Use auto, none, groups or components.") },
            };
            var all = S.ImportMeshAll(Str(a, "path"), opts);
            var d = Describe(all[0], true);
            if (all.Count > 1) d["bodies"] = new JsonArray(all.Select(o => (JsonNode?)Describe(o)).ToArray());
            return d;
        });
        Add("drawing_export", "Produce an engineering drawing of the model and save it as .svg, .pdf or .dxf (chosen by the extension). Hidden-line-removed orthographic views in third-angle layout (front, top, right, iso by default; first_angle:true to swap) at an automatic standard scale, with overall dimensions, hole callouts (Ø, count, THRU or depth), centerlines and locating dimensions, optional section and detail views, hand-placed annotations (dimension, diameter, radius, note, balloon, datum, gdt frames, surface finish), a bill of materials when several bodies are drawn, general tolerance and notes, and a title block. Drawn from the named objects, or every visible solid/surface/Freeform when none are named. View coordinates in sections/details/annotations are the view's drawing coordinates in mm: front x = model X, y = model Z; top x = X, y = Y; right x = Y, y = Z.",
            Schema(["path"], ("path", "string", "Output file: .svg, .pdf or .dxf."), ("objects", "array", "Object names to draw (default: all visible bodies)."), ("views", "array", "front | back | top | bottom | left | right | iso (default front, top, right, iso)."),
                ("sheet", "string", "A4 | A3 (default) | A2 | A1 | A0 | Letter | Tabloid | ANSI-C | ANSI-D."), ("scale", "number", "Drawing scale, e.g. 0.5 for 1:2 (default: automatic)."), ("units", "string", "mm (default) or in: dimension text units."),
                ("title", "string", "Title (default: the object name)."), ("part_number", "string", "Part number."), ("material", "string", "Material note (default: the first object's material)."), ("drawn_by", "string", "Drawn by."), ("company", "string", "Company name."), ("revision", "string", "Revision letter (default A)."),
                ("notes", "array", "General note lines."), ("tolerance", "string", "General tolerance text (default LINEAR ±0.1 ANGULAR ±0.5°)."),
                ("dimensions", "boolean", "Overall dimensions (default true)."), ("hole_callouts", "boolean", "Ø callouts for holes and bosses (default true)."), ("centerlines", "boolean", "Centerlines and center marks (default true)."), ("locating_dimensions", "boolean", "Hole position dimensions (default true)."),
                ("first_angle", "boolean", "First-angle projection."), ("bom", "boolean", "Bill of materials table (default: when more than one part)."),
                ("sections", "array", "Section views: [{parent:'front', direction:'vertical'|'horizontal', offset: position in the parent view's mm coordinates (default the middle)}]. Lettered A-A, B-B, hatched, with the cutting line on the parent."),
                ("details", "array", "Detail views: [{parent:'front', center:[x,y], radius, ratio (magnification relative to the sheet scale, default 3)}]."),
                ("annotations", "array", "Hand-placed items: {type: dimension|diameter|radius|note|balloon|datum|gdt|finish, view, from:[x,y], to:[x,y], at:[x,y], center:[x,y], radius, angle, offset, orientation: horizontal|vertical|aligned, text, tolerance (e.g. '+0.1/-0.05'), number (balloon), symbol (gdt: flatness, straightness, circularity, cylindricity, perpendicularity, parallelism, angularity, position, concentricity, symmetry, runout, total_runout, profile_line, profile_surface), datums:'A|B'}.")), a =>
            {
                var named = (a["objects"] as JsonArray)?.Select(x => FindObject(x!.ToString())).ToList();
                var objs = named ?? S.Objects.Where(o => o.Visible && !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form).ToList();
                if (objs.Count == 0) throw new ArgumentException("Nothing to draw: there are no visible solids, surfaces or Freeform bodies.");
                var shapes = objs.Select(o => o.Shape).ToList();
                var shape = shapes.Count == 1 ? shapes[0] : Occ.Compound(shapes);
                var sheet = Str(a, "sheet", "A3");
                if (!SheetFormat.All.TryGetValue(sheet, out var fmt)) throw new ArgumentException($"Unknown sheet '{sheet}'. Use {string.Join(", ", SheetFormat.All.Keys)}.");
                (double x, double y) P2(JsonNode? n) { var v = n!.AsArray(); return (v[0]!.GetValue<double>(), v[1]!.GetValue<double>()); }
                var opt = new DrawingOptions
                {
                    Format = fmt, Units = Str(a, "units", "mm"), Scale = a["scale"] != null ? Dbl(a, "scale", 1) : null,
                    Title = new TitleBlock { Title = Str(a, "title", objs[0].Name), Number = Str(a, "part_number", ""), Material = Str(a, "material", objs[0].Material?.Name ?? ""), Drawn = Str(a, "drawn_by", ""), Company = Str(a, "company", ""), Revision = Str(a, "revision", "A") },
                    Views = (a["views"] as JsonArray)?.Select(x => x!.ToString()).ToList() ?? ["front", "top", "right", "iso"],
                    Dimensions = Bool(a, "dimensions", true), HoleCallouts = Bool(a, "hole_callouts", true), Centerlines = Bool(a, "centerlines", true), LocatingDimensions = Bool(a, "locating_dimensions", true),
                    ThirdAngle = !Bool(a, "first_angle", false), Bom = a["bom"] != null ? Bool(a, "bom", false) : null,
                    Notes = (a["notes"] as JsonArray)?.Select(x => x!.ToString()).ToList() ?? [],
                };
                if (a["tolerance"] != null) opt.Tolerance = Str(a, "tolerance");
                foreach (var s in a["sections"] as JsonArray ?? [])
                    opt.Sections.Add(new SectionSpec { Parent = s!["parent"]?.ToString() ?? "front", Direction = s["direction"]?.ToString() ?? "vertical", Offset = s["offset"] != null ? s["offset"]!.GetValue<double>() : double.NaN });
                foreach (var d in a["details"] as JsonArray ?? [])
                {
                    var det = new DetailSpec { Parent = d!["parent"]?.ToString() ?? "front", Radius = d["radius"]?.GetValue<double>() ?? 10, Ratio = d["ratio"]?.GetValue<double>() ?? 3 };
                    if (d["center"] != null) { var c = P2(d["center"]); det.Cx = c.x; det.Cy = c.y; }
                    opt.Details.Add(det);
                }
                foreach (var n in a["annotations"] as JsonArray ?? [])
                {
                    var an = new Annotation
                    {
                        Type = n!["type"]?.ToString() ?? "note", View = n["view"]?.ToString() ?? "front", Text = n["text"]?.ToString() ?? "", Tolerance = n["tolerance"]?.ToString() ?? "", Orientation = n["orientation"]?.ToString() ?? "aligned",
                        Symbol = n["symbol"]?.ToString() ?? "", Datums = n["datums"]?.ToString() ?? "", Offset = n["offset"]?.GetValue<double>() ?? 8, Radius = n["radius"]?.GetValue<double>() ?? 0, Angle = n["angle"]?.GetValue<double>() ?? 45, Number = n["number"]?.GetValue<int>() ?? 0,
                    };
                    if (n["from"] != null) an.From = P2(n["from"]); if (n["to"] != null) an.To = P2(n["to"]); if (n["at"] != null) an.At = P2(n["at"]); if (n["center"] != null) an.Center = P2(n["center"]);
                    opt.Annotations.Add(an);
                }
                var bom = objs.GroupBy(o => o.Name).Select(g => new BomItem { Name = g.Key, Qty = g.Count(), Material = g.First().Material?.Name ?? "" }).ToList();
                var d2 = DrawingSheet.Create(shape, bom, opt);
                DrawingOutput.Save(Str(a, "path"), d2.Prims, fmt.W, fmt.H);
                var res = new JsonObject { ["exported"] = Str(a, "path"), ["sheet"] = fmt.Name, ["scale"] = d2.ScaleText, ["views"] = new JsonArray(d2.Views.Select(v => (JsonNode)v.Name).ToArray()), ["dimensions"] = d2.DimensionCount, ["holes"] = d2.HoleCount, ["sections"] = d2.SectionCount, ["details"] = d2.DetailCount, ["annotations"] = d2.AnnotationCount };
                if (d2.Warnings.Count > 0) res["warnings"] = new JsonArray(d2.Warnings.Select(w => (JsonNode)w).ToArray());
                return res;
            });
        Add("mesh_export","Export meshes/bodies to STL, 3MF, OBJ or PLY (chosen by the extension). Exports the selection, or everything when nothing is selected.", Schema(["path"], ("path", "string", "Output path.")), a =>
        {
            S.ExportMesh(Str(a, "path"));
            return new JsonObject { ["exported"] = Str(a, "path") };
        });
        Add("mesh_region", "Select triangles on a mesh (the region later used by mesh_fit_surface, extract and delete). action: smart (flood a smooth patch from the triangle nearest 'at', with 'angle'), brush (triangles within 'radius' of 'at'), box (triangles whose centroid is inside 'min'/'max'), grow, shrink, invert, all, clear, extract (new mesh), delete. mode: replace (default) | add | subtract.",
            Schema(["object", "action"], ("object", "string", "Mesh object name."), ("action", "string", "smart | brush | box | grow | shrink | invert | all | clear | extract | delete"), ("at", "array", "[x,y,z] point on or near the surface."),
                ("angle", "number", "Edge angle in degrees for organic surfaces (default 20)."), ("tolerance", "number", "Surface tolerance in mm for the shape fit (default 0.3% of the mesh size)."), ("radius", "number", "Brush radius in mm."), ("min", "array", "[x,y,z] box corner."), ("max", "array", "[x,y,z] box corner."), ("mode", "string", "replace | add | subtract")), a =>
            {
                var o = FindObject(Str(a, "object")); if (o.Tri == null) throw new ArgumentException($"{o.Name} is not a mesh.");
                var sel = S.RegionFor(o); var m = o.Tri;
                Vec3 P(string k) { var v = a[k]!.AsArray(); return new Vec3(v[0]!.GetValue<double>(), v[1]!.GetValue<double>(), v[2]!.GetValue<double>()); }
                var mode = a["mode"]?.ToString() ?? "replace";
                void Apply(IEnumerable<int> tris) { var l = tris.ToList(); if (mode == "subtract") sel.Remove(l); else { if (mode == "replace") sel.Clear(); sel.Add(l); } }
                switch (Str(a, "action").ToLowerInvariant())
                {
                    case "smart": { var c = m.Bvh.ClosestPoint(P("at")) ?? throw new ArgumentException("No surface near that point."); Apply(sel.SmartFit(c.Tri, a["tolerance"]?.GetValue<double>() ?? Math.Max(m.Size.Length * 0.003, 0.01), a["angle"]?.GetValue<double>() ?? 20, out _)); break; }
                    case "brush": Apply(sel.Brush(P("at"), a["radius"]?.GetValue<double>() ?? 5)); break;
                    case "box": { var lo = P("min"); var hi = P("max"); Apply(Enumerable.Range(0, m.TriCount).Where(t => { var c = (m.P(m.T[3 * t]) + m.P(m.T[3 * t + 1]) + m.P(m.T[3 * t + 2])) / 3; return c.X >= lo.X && c.X <= hi.X && c.Y >= lo.Y && c.Y <= hi.Y && c.Z >= lo.Z && c.Z <= hi.Z; })); break; }
                    case "grow": sel.Grow(); break;
                    case "shrink": sel.Shrink(); break;
                    case "invert": sel.Invert(); break;
                    case "all": sel.All(); break;
                    case "clear": sel.Clear(); break;
                    case "extract": { var part = sel.Extract(); var r = new CommandOutput { Label = "Extract region" }; r.Add.Add(new(null, o.Name + " part", ObjKind.Mesh, o.Color, null, null, o.Look, part)); S.Apply(r); S.DropRegion(); return State(); }
                    case "delete": { var r = new CommandOutput { Label = "Delete region" }; r.ReplaceMeshes.Add((o, sel.Without())); S.Apply(r); S.DropRegion(); return State(); }
                    default: throw new ArgumentException("Unknown action.");
                }
                S.RefreshRegion();
                return new JsonObject { ["selectedTriangles"] = sel.Tris.Count, ["area"] = Math.Round(sel.Area(), 3), ["of"] = m.TriCount };
            });
        Add("assistant", "Drive the in-app AI assistant panel (for testing the assistant itself). action: open | close | send (text) | transcript | new | provider (index 0 Codex login, 1 Claude API key, 2 OpenAI/Codex API key).",
            Schema(["action"], ("action", "string", "open | close | send | transcript | new | provider"), ("text", "string", "Message for send."), ("index", "integer", "Provider index.")), async a =>
            {
                switch (Str(a, "action"))
                {
                    case "open": _vm.AssistantOpen = true; break;
                    case "close": _vm.AssistantOpen = false; break;
                    case "new": _vm.NewAssistantChat(); break;
                    case "provider": _vm.AssistantProviderIndex = (int)Dbl(a, "index", 0); break;
                    case "send": _vm.AssistantOpen = true; _vm.AssistantInput = Str(a, "text"); await _vm.SendAssistant(); break;
                }
                return new JsonObject { ["open"] = _vm.AssistantOpen, ["messages"] = new JsonArray(_vm.ChatMessages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role.ToString(), ["text"] = m.Text }).ToArray()) };
            });
        Add("workspace", "Switch workspace: design | drawing | scan | cae (or simulate) | assemble | render | ar | make | script | all. CAE includes Static, Thermal, Dynamics, Motion (kinematics and rigid-body motion of an assembly) and Fluids. make opens Manufacture/CAM.", Schema(["id"], ("id", "string", "Workspace id; cae opens CAE, make opens CAM.")), a => { _vm.SetWorkspace(Str(a, "id")); return State(); });
        Add("study_new", "Start a static study on a solid body (the selected one, or the named one). Then add fixtures and loads with run_command sim_fixed / sim_roller / sim_force / sim_pressure / sim_torque / sim_gravity and call study_run.",
            Schema(null, ("object", "string", "Body name (default: the selection)."), ("element_size", "number", "Target element size in mm (0 = automatic).")), a =>
            {
                if (a["object"] != null) S.SelectObjects([FindObject(a["object"]!.ToString()).Id]);
                _vm.NewStudy();
                if (S.ActiveStudy is { } st && a["element_size"] != null) { st.ElementSize = a["element_size"]!.GetValue<double>(); _vm.SimElementSize = st.ElementSize; }
                return StudyJson();
            });
        Add("study_run", "Mesh and solve the active study (blocks until done) and return the key results. Optionally switch the displayed result: von Mises, displacement, safety.",
            Schema(null, ("show", "string", "von Mises | displacement | safety")), a =>
            {
                var res = _vm.RunStudySync();
                if (a["show"]?.ToString() is { } k) _vm.SetResultKind(k.StartsWith("d", StringComparison.OrdinalIgnoreCase) ? Simulation.ResultKind.Displacement : k.StartsWith("s", StringComparison.OrdinalIgnoreCase) ? Simulation.ResultKind.SafetyFactor : Simulation.ResultKind.VonMises);
                return StudyJson();
            });
        Add("cae_state", "CAE solver status and results. Start cae_thermal or cae_modal with run_command(commit=false), configure body/faces/material and parameters, then command_set with 'Run analysis':true. action: status | stop. Case files remain in resultFolder. Fluids currently use the separate cae_pipe_flow reduced-order command.", Schema(null,("action","string","status (default) | stop")), a =>
        {
            string action=a["action"]?.ToString()??"status";
            if(action=="stop") S.StopCae(); else if(action!="status") throw new ArgumentException("action must be status or stop.");
            return CaeJson();
        });
        Add("workflow_state", "Saved CAE setups, named selections, CAM setup and live path playback. Optional staticStudy selects a static study by name.", Schema(null,("staticStudy","string","Optional saved static study name.")), a =>
        {
            if(a["staticStudy"] is{} name){S.ActiveStudy=S.Studies.FirstOrDefault(st=>st.Name==name.ToString())??throw new ArgumentException("No such static study.");S.Dirty=true;S.ShowResult(null);}
            return new JsonObject {
                ["staticStudies"]=new JsonArray(S.Studies.Select(st=>(JsonNode)new JsonObject {["name"]=st.Name,["body"]=S.Find(st.BodyId)?.Name,["active"]=st==S.ActiveStudy}).ToArray()),
                ["faceSets"]=new JsonArray(S.FaceSets.Select(f=>(JsonNode)new JsonObject {["name"]=f.Name,["body"]=S.Find(f.BodyId)?.Name,["faces"]=new JsonArray(f.Faces.Select(i=>(JsonNode)JsonValue.Create(i+1)!).ToArray())}).ToArray()),
                ["caeSetups"]=new JsonArray(S.CaeCases.Select(c=>(JsonNode)new JsonObject {["name"]=c.Name,["kind"]=c.Settings.Kind.ToString(),["body"]=S.Find(c.BodyId)?.Name}).ToArray()),
                ["camSetup"]=S.CamSetup==null?null:JsonSerializer.SerializeToNode(S.CamSetup),
                ["playback"]=S.CamTimeline==null?null:new JsonObject {["playing"]=S.CamPlaying,["seconds"]=S.CamSeconds,["duration"]=S.CamTimeline.Duration,["speed"]=S.CamSpeed,["position"]=Arr(S.CamTimeline.At(S.CamSeconds).tip)}
            };
        });
        Add("study_state", "The active static study: fixtures, loads, mesh size and (if solved) the key results and notes.", Schema(null), _ => StudyJson());
        Add("document_new", "Open a new empty document in its own tab.", Schema(null), _ => { _vm.NewDocumentTab(); _vm.CloseHome(); return DocsJson(); });
        Add("document_list", "The open documents (tabs).", Schema(null), _ => DocsJson());
        Add("document_switch", "Bring a document tab to the front by index (0-based) or title.", Schema(["document"], ("document", "string", "Index or title.")), a =>
        {
            var key = Str(a, "document");
            var t = int.TryParse(key, out var i) && i >= 0 && i < _vm.Documents.Count ? _vm.Documents[i] : _vm.Documents.FirstOrDefault(d => d.Title.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("No such document.");
            _vm.ActivateDocument(t); _vm.CloseHome(); return DocsJson();
        });
        Add("document_close", "Close a document tab (discards unsaved changes).", Schema(["document"], ("document", "string", "Index or title.")), a =>
        {
            var key = Str(a, "document");
            var t = int.TryParse(key, out var i) && i >= 0 && i < _vm.Documents.Count ? _vm.Documents[i] : _vm.Documents.FirstOrDefault(d => d.Title.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("No such document.");
            t.ConfirmClose = true; _vm.CloseDocument(t); return DocsJson();
        });
        Add("delete_objects", "Delete objects by name.", Schema(["objects"], ("objects", "array", "Object names.")), a =>
        {
            var objs = a["objects"]!.AsArray().Select(x => FindObject(x!.ToString())).ToList();
            S.ClearSelection(); S.SelectObjects(objs.Select(o => o.Id)); S.DeleteSelection();
            return new JsonObject { ["deleted"] = objs.Count, ["remaining"] = S.Objects.Count };
        });

        Add("sketch_begin", "Start a sketch. plane: 'XY', 'XZ' or 'YZ' (optional offset along the normal), a planar face {object, face}, or an origin plane name. To edit an existing sketch pass edit:'Sketch 1' (its constraints and dimensions are preserved).",
            Schema(null, ("plane", "any", "'XY' | 'XZ' | 'YZ' | the name of a construction plane | {object, face}"), ("offset", "number", "Offset along the plane normal (mm)."), ("edit", "string", "Name of an existing sketch object to edit."), ("look_normal", "boolean", "Align the camera when starting; false preserves the current 3D view.")), a =>
            {
                if (S.ActiveCommand != null) S.CancelCommand();
                if (S.Sketch != null) throw new InvalidOperationException("A sketch is already open. Call sketch_finish first.");
                if (a["edit"] != null)
                {
                    if (!S.EditSketch(FindObject(Str(a, "edit")))) throw new InvalidOperationException(S.Status);
                    return SketchStateJson();
                }
                Plane3 pl;
                var spec = a["plane"];
                if (spec is JsonObject fo)
                {
                    var it = ParseItem(fo);
                    pl = S.PlaneOf(it) ?? throw new ArgumentException("That is not a plane or a flat face.");
                }
                else if (spec is JsonValue nv && S.Objects.FirstOrDefault(o => o.Kind == ObjKind.Plane && o.Name.Equals(nv.ToString(), StringComparison.OrdinalIgnoreCase)) is { SketchPlane: { } named })
                {
                    pl = new Plane3(named.Origin + named.Normal * Dbl(a, "offset", 0), named.U, named.V);   // a construction plane by name
                }
                else
                {
                    var b = (spec?.ToString() ?? "XY").ToUpperInvariant().Replace(" PLANE", "") switch { "XZ" => Plane3.XZ, "YZ" => Plane3.YZ, _ => Plane3.XY };
                    pl = new Plane3(b.Origin + b.Normal * Dbl(a, "offset", 0), b.U, b.V);
                }
                S.EnterSketch(pl, alignView: Bool(a, "look_normal", true));
                return SketchStateJson();
            });

        Add("sketch_plane", "Control the unified sketch work plane. Actions: select (1-based index), new, xy, xz, yz, origin ([x,y,z] mm), rotate ([x,y,z] world degrees), size (display multiplier), snapping (other_curves and project), normal, gizmo, edit. Existing curves retain their original drawing planes.",
            Schema(["action"], ("action", "string", "Plane action."), ("index", "integer", "1-based plane index."), ("value", "any", "[x,y,z] for origin/rotate; number for size."), ("other_curves", "boolean", "Enable external curve snaps."), ("project", "boolean", "Allow explicitly labeled off-plane projection.")), a =>
            {
                var sk = S.Sketch ?? throw new InvalidOperationException("No sketch is open.");
                switch (Str(a, "action").ToLowerInvariant())
                {
                    case "select":
                        int index = (int)Dbl(a, "index", 0) - 1;
                        if (index < 0 || index >= sk.DrawingPlanes.Count) throw new ArgumentException("Plane index is out of range.");
                        sk.SelectPlane(S, index); break;
                    case "new": sk.SetDrawingPlane(S, sk.Plane, forceNew: true); break;
                    case "xy": sk.PresetPlane(S,0); break;
                    case "xz": sk.PresetPlane(S,1); break;
                    case "yz": sk.PresetPlane(S,2); break;
                    case "origin": sk.MoveDrawingPlane(S,V3(a["value"] ?? throw new ArgumentException("value required"))); break;
                    case "rotate": sk.RotateDrawingPlane(S,V3(a["value"] ?? throw new ArgumentException("value required"))); break;
                    case "size": sk.SetGizmoSize(S,Dbl(a,"value",1)); break;
                    case "snapping": sk.SnapOtherCurves = Bool(a,"other_curves",sk.SnapOtherCurves); sk.ProjectOtherCurves = Bool(a,"project",sk.ProjectOtherCurves); S.RaiseState(); break;
                    case "normal": sk.LookNormal(S); break;
                    case "gizmo": sk.SetGizmoEditing(S,true); break;
                    case "edit": sk.SetGizmoEditing(S,false); break;
                    default: throw new ArgumentException("Unknown sketch plane action.");
                }
                return SketchStateJson();
            });

        Add("sketch_add", "Add geometry to the open sketch using the same tools as the mouse (so auto-constraints apply: coincident joints, horizontal/vertical, tangents...). Coordinates are 2D [u,v] on the sketch plane. Entity types: " +
            "line {points:[[u,v],[u,v]]}; polyline {points, closed?}; rect {corner1, corner2} | {center, corner} | {points:[p1,p2,p3]} (3-point); " +
            "circle {center, radius} | {points:[p1,p2]} (2-point) | {points:[p1,p2,p3]} (3-point); arc {points:[start,mid,end]} | {center, start, end}; " +
            "ellipse {center, major:[u,v] (end of one axis), minor (other radius)}; line also takes midpoint:true ([middle, end]); polygon {center, radius, sides, circumscribed?}; slot {center1, center2, width}; spline {points, closed?, control?}; point {at:[u,v]}. Add \"construction\":true for reference geometry.",
            Schema(["entities"], ("entities", "array", "List of entity objects.")), a =>
            {
                var sk = S.Sketch ?? throw new InvalidOperationException("No sketch is open. Call sketch_begin first.");
                int before = sk.Model.Entities.Count;
                (double, double) P(JsonNode n) { var v = n.AsArray(); return (v[0]!.GetValue<double>(), v[1]!.GetValue<double>()); }
                foreach (var e in a["entities"]!.AsArray())
                {
                    var o = e!.AsObject(); var type = Str(o, "type").ToLowerInvariant();
                    bool construction = Bool(o, "construction", false);
                    bool prevCons = sk.Construction; sk.Construction = construction;
                    try
                    {
                        var pts = o["points"]?.AsArray().Select(p => P(p!)).ToList() ?? [];
                        ClickTool tool; List<(double, double)> feed; bool closed = Bool(o, "closed", false);
                        switch (type)
                        {
                            case "line": tool = new LineTool(Bool(o, "midpoint", false) ? 1 : 0); feed = pts; break;
                            case "polyline": tool = new LineTool(); feed = [.. pts]; if (closed && pts.Count > 2) feed.Add(pts[0]); break;
                            case "rect":
                                if (o["corner1"] != null) { tool = new RectTool(RectMode.TwoCorner); feed = [P(o["corner1"]!), P(o["corner2"]!)]; }
                                else if (o["center"] != null) { tool = new RectTool(RectMode.Center); feed = [P(o["center"]!), P(o["corner"]!)]; }
                                else { tool = new RectTool(RectMode.ThreePoint); feed = pts; }
                                break;
                            case "circle":
                                if (o["center"] != null) { var c = P(o["center"]!); double r = o["radius"]!.GetValue<double>(); tool = new CircleTool(CircleMode.CenterRadius); feed = [c, (c.Item1 + r, c.Item2)]; }
                                else { tool = new CircleTool(pts.Count == 2 ? CircleMode.TwoPoint : CircleMode.ThreePoint); feed = pts; }
                                break;
                            case "ellipse":
                            {
                                var c = P(o["center"]!); var mj = P(o["major"]!); double mn = o["minor"]!.GetValue<double>();
                                double dx = mj.Item1 - c.Item1, dy = mj.Item2 - c.Item2, len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-9);
                                tool = new CircleTool(CircleMode.Ellipse); feed = [c, mj, (c.Item1 - dy / len * mn, c.Item2 + dx / len * mn)];
                                break;
                            }
                            case "arc":
                                if (o["center"] != null) { tool = new ArcTool(ArcMode.CenterStartEnd); feed = [P(o["center"]!), P(o["start"]!), P(o["end"]!)]; }
                                else { tool = new ArcTool(ArcMode.ThreePoint); feed = [pts[0], pts[2], pts[1]]; }
                                break;
                            case "polygon":
                            {
                                var c = P(o["center"]!); double r = o["radius"]!.GetValue<double>();
                                var pt = new PolygonTool(Bool(o, "circumscribed", false)) { Sides = (int)Dbl(o, "sides", 6) };
                                tool = pt; feed = [c, (c.Item1 + r, c.Item2)];
                                break;
                            }
                            case "slot":
                            {
                                var c1 = P(o["center1"]!); var c2 = P(o["center2"]!); double w = o["width"]!.GetValue<double>();
                                double dx = c2.Item1 - c1.Item1, dy = c2.Item2 - c1.Item2, len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-9);
                                tool = new SlotTool(); feed = [c1, c2, (c1.Item1 - dy / len * w / 2, c1.Item2 + dx / len * w / 2)];
                                break;
                            }
                            case "spline": tool = new SplineTool(Bool(o, "control", false)); feed = pts; break;
                            case "point": tool = new PointTool(); feed = [P(o["at"]!)]; break;
                            default: throw new ArgumentException($"Unknown sketch entity type '{type}'.");
                        }
                        foreach (var (u, v) in feed) tool.Feed(S, u, v);
                        if (type is "line" or "polyline" or "spline") tool.FinishFeed(S, closed && type == "spline");
                    }
                    finally { sk.Construction = prevCons; }
                }
                var j = SketchStateJson();
                j["added"] = sk.Model.Entities.Count - before;
                return Bool(a, "verbose", false) ? j : CompactSketch(j);
            });

        Add("sketch_state", "The open sketch's model: points, entities (ids), constraints and dimensions, degrees of freedom and solver status. Refs use 'p<id>' for points and 'e<id>' for entities.",
            Schema(null), _ => SketchStateJson());

        Add("sketch_constrain", "Add a geometric constraint. type: horizontal | vertical | coincident | parallel | perpendicular | tangent | equal | midpoint | concentric | collinear | symmetric | fix. " +
            "refs: ids like 'e3' (entity) and 'p5' (point) - e.g. horizontal ['e3'] or ['p1','p2']; coincident ['p1','p2'] or ['p1','e4'] (point on curve); parallel ['e1','e2']; midpoint ['p3','e1']; symmetric ['p1','p2','e5']; fix ['p1'] or ['e2'].",
            Schema(["type", "refs"], ("type", "string", "Constraint type."), ("refs", "array", "Point/entity ids, e.g. [\"e1\",\"p3\"].")), a =>
            {
                var sk = S.Sketch ?? throw new InvalidOperationException("No sketch is open.");
                var refs = a["refs"]!.AsArray().Select(r => r!.ToString()).ToList();
                var type = Str(a, "type").ToLowerInvariant();
                int Id(string r) => int.Parse(r.TrimStart('p', 'e'));
                bool IsP(string r) => r.StartsWith('p');
                var m = sk.Model;
                bool ok = sk.Edit(S, $"The {type} constraint", () =>
                {
                    switch (type)
                    {
                        case "horizontal": m.Add(CType.Horizontal, refs: refs.Select(Id).ToArray()); break;
                        case "vertical": m.Add(CType.Vertical, refs: refs.Select(Id).ToArray()); break;
                        case "coincident": if (IsP(refs[1])) m.Add(CType.Coincident, refs: [Id(refs[0]), Id(refs[1])]); else m.Add(CType.PointOnEntity, refs: [Id(refs[0]), Id(refs[1])]); break;
                        case "parallel": m.Add(CType.Parallel, refs: [Id(refs[0]), Id(refs[1])]); break;
                        case "perpendicular": m.Add(CType.Perpendicular, refs: [Id(refs[0]), Id(refs[1])]); break;
                        case "collinear": m.Add(CType.Collinear, refs: [Id(refs[0]), Id(refs[1])]); break;
                        case "equal": m.Add(CType.Equal, refs: [Id(refs[0]), Id(refs[1])]); break;
                        case "concentric": m.Add(CType.Concentric, refs: [Id(refs[0]), Id(refs[1])]); break;
                        case "midpoint": m.Add(CType.Midpoint, refs: [Id(refs[0]), Id(refs[1])]); break;
                        case "symmetric": m.Add(CType.Symmetric, refs: [Id(refs[0]), Id(refs[1]), Id(refs[2])]); break;
                        case "tangent":
                        {
                            var ea = m.E(Id(refs[0])); var eb = m.E(Id(refs[1])); double sign = 1;
                            if (ea.Type != EntType.Line && eb.Type != EntType.Line)
                            {
                                var ca = m.P(ea.A); var cb = m.P(eb.A);
                                double d = Math.Sqrt((ca.X - cb.X) * (ca.X - cb.X) + (ca.Y - cb.Y) * (ca.Y - cb.Y)), ra = m.Radius(ea), rb = m.Radius(eb);
                                sign = Math.Abs(d - (ra + rb)) <= Math.Abs(d - Math.Abs(ra - rb)) ? 1 : -1;
                            }
                            m.Add(CType.Tangent, sign, 0, false, Id(refs[0]), Id(refs[1]));
                            break;
                        }
                        case "fix":
                        {
                            var pts = IsP(refs[0]) ? [Id(refs[0])] : m.PointsOf(m.E(Id(refs[0]))).Distinct().ToList();
                            foreach (var pid in pts) { var p = m.P(pid); m.Add(CType.Fixed, p.X, p.Y, false, pid); }
                            break;
                        }
                        default: throw new ArgumentException($"Unknown constraint '{type}'.");
                    }
                });
                if (!ok) throw new InvalidOperationException(S.Status);
                return SketchStateJson();
            });

        Add("sketch_dimension", "Add a driving dimension; the sketch is solved to match. type: length (line) | distance (two points) | hdistance | vdistance | radius | diameter | angle (two lines, degrees) | point_line. refs like constrain. value in mm (or degrees).",
            Schema(["type", "refs", "value"], ("type", "string", "Dimension type."), ("refs", "array", "Ids, e.g. [\"e1\"] or [\"p1\",\"p2\"]."), ("value", "number", "Target value.")), a =>
            {
                var sk = S.Sketch ?? throw new InvalidOperationException("No sketch is open.");
                var refs = a["refs"]!.AsArray().Select(r => int.Parse(r!.ToString().TrimStart('p', 'e'))).ToArray();
                double v = Dbl(a, "value", 0);
                var t = Str(a, "type").ToLowerInvariant() switch
                {
                    "length" => CType.LineLength, "distance" => CType.Distance, "hdistance" => CType.HDistance, "vdistance" => CType.VDistance,
                    "radius" => CType.Radius, "diameter" => CType.Diameter, "angle" => CType.Angle, "point_line" => CType.PointLineDistance,
                    var x => throw new ArgumentException($"Unknown dimension type '{x}'."),
                };
                if (!sk.Edit(S, "That dimension", () => sk.Model.Add(t, v, 0, true, refs))) throw new InvalidOperationException(S.Status);
                return SketchStateJson();
            });

        Add("sketch_set_dimension", "Change the value of an existing dimension (by constraint id from sketch_state) - the sketch updates parametrically.",
            Schema(["constraint", "value"], ("constraint", "integer", "Constraint id."), ("value", "number", "New value.")), a =>
            {
                var sk = S.Sketch ?? throw new InvalidOperationException("No sketch is open.");
                int id = (int)Dbl(a, "constraint", -1); double v = Dbl(a, "value", 0);
                var c = sk.Model.Constraints.FirstOrDefault(x => x.Id == id && x.IsDimension) ?? throw new ArgumentException($"No dimension with id {id}.");
                if (!sk.Edit(S, "That value", () => sk.Model.Constraints.First(x => x.Id == id).Value = c.Type is CType.Angle or CType.HDistance or CType.VDistance ? (c.Value < 0 ? -Math.Abs(v) : Math.Abs(v)) : v)) throw new InvalidOperationException(S.Status);
                return SketchStateJson();
            });

        Add("sketch_project", "Bring geometry from outside into the open sketch (locked in place). mode edge: project one edge of an object (object name or id, edge index) | body: project all edges of an object | section: the curves where an object crosses the sketch plane. Good for lofts: copy or slice an existing body to get matching profiles.",
            Schema(["object", "mode"], ("object", "string", "Object name or id."), ("mode", "string", "edge | body | section"), ("edge", "integer", "Edge index (edge mode).")), a =>
            {
                var o = FindObject(Str(a, "object"));
                int n = Str(a, "mode").ToLowerInvariant() switch
                {
                    "edge" => ProjectTool.ProjectEdge(S, o, (int)Dbl(a, "edge", 0)),
                    "body" => ProjectTool.ProjectBody(S, o),
                    "section" => ProjectTool.SectionBody(S, o),
                    var m => throw new ArgumentException($"Unknown mode '{m}'."),
                };
                var j = SketchStateJson(); j["added"] = n; return j;
            });

        Add("sketch_mirror", "Mirror sketch entities across a line entity. live=true keeps the copy tied to the original with symmetry constraints.",
            Schema(["entities", "axis"], ("entities", "array", "Entity ids like ['e3','e4']."), ("axis", "string", "Line entity id to mirror across, like 'e7'."), ("live", "boolean", "Keep symmetric (default true).")), a =>
            {
                var sk = S.Sketch ?? throw new InvalidOperationException("No sketch is open.");
                var ids = a["entities"]!.AsArray().Select(n => int.Parse(n!.ToString().TrimStart('e', 'E'))).ToList();
                var axis = sk.Model.EntityOrNull(int.Parse(Str(a, "axis").TrimStart('e', 'E'))) is { Type: Core.Sketching.EntType.Line } l ? l : throw new ArgumentException("The axis must be a line.");
                sk.Edit(S, "That mirror", () => SketchMirrorTool.Mirror(sk.Model, ids.Where(i => i != axis.Id).ToList(), axis, Bool(a, "live", true)));
                return SketchStateJson();
            });

        Add("sketch_trim", "Trim a line, arc or circle: removes the part of curve 'e3' nearest to the point at:[u,v], cutting back to the crossing curves on each side (a curve nothing crosses is deleted).",
            Schema(["entity", "at"], ("entity", "string", "Entity id like 'e3'."), ("at", "array", "Point [u,v] on the part to remove.")), a =>
            {
                var at = a["at"]!.AsArray();
                TrimTool.Run(S, int.Parse(Str(a, "entity").TrimStart('e', 'E')), at[0]!.GetValue<double>(), at[1]!.GetValue<double>());
                return SketchStateJson();
            });

        Add("sketch_pattern", "Repeat sketch entities. type linear: offset [du,dv] between copies. type circular: center [u,v] and optionally angle (degrees between copies; default = evenly around 360). count = total copies including the original.",
            Schema(["entities", "type", "count"], ("entities", "array", "Entity ids like ['e3','e4']."), ("type", "string", "linear | circular"), ("count", "integer", "Total copies including the original."),
                ("offset", "array", "Linear: [du,dv]."), ("center", "array", "Circular: [u,v]."), ("angle", "number", "Circular: degrees between copies.")), a =>
            {
                var ids = a["entities"]!.AsArray().Select(n => int.Parse(n!.ToString().TrimStart('e', 'E'))).ToArray();
                bool circ = Str(a, "type").ToLowerInvariant().StartsWith("c");
                var v = (circ ? a["center"] : a["offset"])!.AsArray();
                SketchPatternTool.Run(S, ids, circ, (int)Dbl(a, "count", 3), v[0]!.GetValue<double>(), v[1]!.GetValue<double>(), Dbl(a, "angle", 0) * Math.PI / 180);
                return SketchStateJson();
            });

        Add("sketch_input", "Drive the open sketch like the mouse and keyboard. Optionally pick a tool (line, line_mid, rect, rect_center, rect_3pt, ellipse, circle, circle_2pt, circle_3pt, polygon, slot, spline, spline_control, edit_spline, select, constrain:<type>), then send events: {type:'move'|'down'|'up'|'click'|'dclick'|'key', u, v (sketch plane coords), shift, alt} or {type:'key', key:'Char:5'|'Tab'|'Enter'|'Escape'|'Back'|'Delete'|'RightClick'|'Char:L'|'Char:C'|'Char:X'}. Returns the on-screen read-out labels, the status line and the sketch state.",
            Schema(null, ("tool", "string", "Tool to activate first."), ("events", "array", "List of event objects."), ("type", "string", "Event types, see above.")), a =>
            {
                var sk = S.Sketch ?? throw new InvalidOperationException("No sketch is open.");
                SketchTool? MakeTool(string n)
                {
                    n = n.ToLowerInvariant();
                    if (n.StartsWith("constrain:")) return new ConstraintTool(Enum.Parse<CType>(n[10..], true));
                    return n switch
                    {
                        "line" => new LineTool(0), "line_mid" => new LineTool(1),
                        "rect" => new RectTool(RectMode.TwoCorner), "rect_center" => new RectTool(RectMode.Center), "rect_3pt" => new RectTool(RectMode.ThreePoint),
                        "ellipse" => new EllipseTool(EllipseMode.Center), "ellipse_2pt" => new EllipseTool(EllipseMode.TwoPoint), "ellipse_3pt" => new EllipseTool(EllipseMode.ThreePoint), "arc" or "arc_3pt" => new ArcTool(ArcMode.ThreePoint), "arc_center" => new ArcTool(ArcMode.CenterStartEnd), "arc_tangent" => new ArcTool(ArcMode.Tangent), "circle" => new CircleTool(CircleMode.CenterRadius), "circle_2pt" => new CircleTool(CircleMode.TwoPoint), "circle_3pt" => new CircleTool(CircleMode.ThreePoint),
                        "polygon" => new PolygonTool(false), "slot" => new SlotTool(), "text" => new SketchTextTool(), "spline" => new SplineTool(false), "spline_control" => new SplineTool(true),
                        "edit_spline" => new SketchGeometryEditTool(1), "edit_sketch" => new SketchGeometryEditTool(), "select" => new SelectTool(), "extend" => new ExtendTool(), "offset" => new OffsetTool(), "fillet" => new FilletTool(false), "chamfer" => new FilletTool(true), "trim" => new TrimTool(), "pattern" => SketchPatternTool.Make(0), "pattern_circular" => SketchPatternTool.Make(1), "pattern_curve" => SketchPatternTool.Make(2),
                        _ => throw new ArgumentException($"Unknown sketch tool '{n}'."),
                    };
                }
                if (a["tool"] != null) sk.SetTool(S, MakeTool(Str(a, "tool")));
                (double x, double y) Px(JsonNode e)
                {
                    var q = S.Camera.Project(sk.Plane.PointAt(e["u"]?.GetValue<double>() ?? 0, e["v"]?.GetValue<double>() ?? 0));
                    return q is { } v ? (v.x, v.y) : throw new InvalidOperationException("That point is off screen.");
                }
                foreach (var e in a["events"]?.AsArray() ?? [])
                {
                    string type = e!["type"]!.GetValue<string>();
                    bool shift = e["shift"]?.GetValue<bool>() ?? false, alt = e["alt"]?.GetValue<bool>() ?? false;
                    if (type == "key") { S.Key(e["key"]!.GetValue<string>()); continue; }
                    var (px, py) = e["x"] != null ? (e["x"]!.GetValue<double>(), e["y"]!.GetValue<double>()) : Px(e);
                    px += e["dx"]?.GetValue<double>() ?? 0; py += e["dy"]?.GetValue<double>() ?? 0;
                    PointerInfo P(bool left, int clicks = 1) => new(px, py, left, shift, false, alt, clicks);
                    switch (type)
                    {
                        case "move": S.PointerMove(P(false)); break;
                        case "down": S.PointerMove(P(false)); S.PointerDown(P(true)); break;
                        case "up": S.PointerUp(P(true)); break;
                        case "click": S.PointerMove(P(false)); S.PointerDown(P(true)); S.PointerUp(P(true)); break;
                        case "dclick": S.PointerMove(P(false)); S.PointerDown(P(true)); S.PointerUp(P(true)); S.PointerDown(P(true, 2)); S.PointerUp(P(true, 2)); break;
                        case "drag": S.PointerMove(P(false)); S.PointerDown(P(true)); { var (x2, y2) = Px(new JsonObject { ["u"] = e["u2"]!.GetValue<double>(), ["v"] = e["v2"]!.GetValue<double>() }); S.PointerMove(new PointerInfo(x2, y2, true, shift, false, alt)); S.PointerUp(new PointerInfo(x2, y2, true, shift, false, alt)); } break;
                        case "path":
                        {
                            var path = e["path"]!.AsArray().Select(q => Px(new JsonObject { ["u"] = q![0]!.GetValue<double>(), ["v"] = q[1]!.GetValue<double>() })).ToList();
                            S.PointerMove(new PointerInfo(path[0].x, path[0].y, false, shift, false, alt)); S.PointerDown(new PointerInfo(path[0].x, path[0].y, true, shift, false, alt));
                            foreach (var q in path.Skip(1)) S.PointerMove(new PointerInfo(q.x, q.y, true, shift, false, alt));
                            S.PointerUp(new PointerInfo(path[^1].x, path[^1].y, true, shift, false, alt));
                            break;
                        }
                        default: throw new ArgumentException($"Unknown event type '{type}'.");
                    }
                }
                var o = new Overlay(); S.BuildOverlay(o);
                var res = SketchStateJson();
                res["status"] = S.Status; res["statusIsError"] = S.StatusIsError; res["glyphs"] = string.Join(" ", sk.GlyphDebug(S));
                res["labels"] = new JsonArray(o.Labels.Select(l => (JsonNode)JsonValue.Create($"[{l.Style}] {l.Text}")!).ToArray());
                return res;
            });

        Add("pointer", "Send mouse events to the 3D viewport at world positions: events [{type:'move'|'down'|'up'|'drag', x,y,z, x2,y2,z2 (drag end)}]. Returns the active command's grab handle (origin, tip, value) and the camera.",
            Schema(["events"], ("events", "array", "Event objects.")), a =>
            {
                (double x, double y) Px(double x, double y, double z) { var q = S.Camera.Project(new Vec3(x, y, z)); return q is { } v ? (v.x, v.y) : throw new InvalidOperationException("Off screen."); }
                foreach (var e in a["events"]!.AsArray())
                {
                    string type = e!["type"]!.GetValue<string>();
                    if (type == "key") { S.Key(e["key"]!.GetValue<string>()); continue; }
                    bool shiftKey = e["shift"]?.GetValue<bool>() ?? false;
                    var (px, py) = Px(e["x"]?.GetValue<double>() ?? 0, e["y"]?.GetValue<double>() ?? 0, e["z"]?.GetValue<double>() ?? 0);
                    bool altKey = e["alt"]?.GetValue<bool>() ?? false;
                    PointerInfo P(double x, double y, bool left) => new(x, y, left, shiftKey, false, altKey, e["clicks"]?.GetValue<int>() ?? 1);
                    switch (type)
                    {
                        case "move": S.PointerMove(P(px, py, false)); break;
                        case "down": S.PointerMove(P(px, py, false)); S.PointerDown(P(px, py, true)); break;
                        case "up": S.PointerUp(P(px, py, false)); break;
                        case "drag":
                        {
                            S.PointerMove(P(px, py, false)); S.PointerDown(P(px, py, true));
                            var (x2, y2) = Px(e["x2"]!.GetValue<double>(), e["y2"]!.GetValue<double>(), e["z2"]!.GetValue<double>());
                            S.PointerMove(P(x2, y2, true)); S.PointerUp(P(x2, y2, false)); break;
                        }
                    }
                }
                var r = new JsonObject { ["yawDeg"] = Math.Round(S.Camera.Yaw * 180 / Math.PI, 1), ["pitchDeg"] = Math.Round(S.Camera.Pitch * 180 / Math.PI, 1), ["status"] = S.Status };
                if (S.ActiveCommand?.Handle is { } h)
                {
                    var tip = h.Origin + h.Dir.Normalized() * (h.Param.Value / h.Mul);
                    r["handle"] = new JsonObject { ["origin"] = new JsonArray(h.Origin.X, h.Origin.Y, h.Origin.Z), ["tip"] = new JsonArray(Math.Round(tip.X, 3), Math.Round(tip.Y, 3), Math.Round(tip.Z, 3)), ["value"] = h.Param.Value };
                }
                return r;
            });

        Add("datum", "Model datums and the move gizmo. action: arm (kind: point|axis|plane — the persistent click-to-place tool; click with the pointer tool), finish (stop placing), gizmo (show the free gizmo; move:true = the Move tool's grab mode), bind (bind the current selection to the gizmo), list (placed datums).",
            Schema(null, ("action", "string", "arm | finish | gizmo | bind | list."), ("kind", "string", "point | axis | plane."), ("move", "boolean", "Arm Move mode on the gizmo.")), a =>
            {
                switch ((a["action"]?.GetValue<string>() ?? "list").ToLowerInvariant())
                {
                    case "arm":
                        var kind = (a["kind"]?.GetValue<string>() ?? "point").ToLowerInvariant() switch
                        { "axis" => SketchDatumKind.Axis, "plane" => SketchDatumKind.Plane, _ => SketchDatumKind.Point };
                        S.SetFreeTool(new DatumPickTool(kind));
                        break;
                    case "finish": S.SetFreeTool(null); break;
                    case "gizmo":
                        S.ShowDatumGizmo(S.ViewportCursor ?? Vec3.Zero);
                        if (Bool(a, "move", false)) S.Datum!.MoveMode = true;
                        break;
                    case "bind":
                        if (S.Datum == null) S.ShowDatumGizmo(S.ViewportCursor ?? Vec3.Zero);
                        S.Datum!.MoveMode = true;
                        var sel = S.Selection.Where(i => i.Kind is SelKind.Object or SelKind.Datum).ToList();
                        if (sel.Count > 0) S.Datum.BindTargets(S, sel);
                        break;
                    case "list":
                        var arr = new JsonArray();
                        foreach (var o in S.Objects.Where(o => o.SketchData != null))
                            for (int i = 0; i < o.SketchData!.Datums.Count; i++)
                            {
                                var d = o.SketchData.Datums[i];
                                arr.Add(new JsonObject { ["object"] = o.Name, ["index"] = i, ["name"] = d.Name, ["kind"] = d.Kind.ToString(), ["origin"] = Arr(d.Frame.Origin) });
                            }
                        return new JsonObject { ["datums"] = arr };
                    default: throw new InvalidOperationException("action must be arm | finish | gizmo | bind | list.");
                }
                return new JsonObject { ["status"] = S.Status, ["tool"] = S.FreeTool?.GetType().Name, ["gizmo"] = S.DatumVisible };
            });

        Add("sketch_delete", "Delete sketch entities ('e3') or constraints (by id).",
            Schema(null, ("entities", "array", "Entity ids, e.g. [\"e3\"]."), ("constraints", "array", "Constraint ids (integers).")), a =>
            {
                var sk = S.Sketch ?? throw new InvalidOperationException("No sketch is open.");
                sk.Edit(S, "Deleting that", () =>
                {
                    foreach (var e in a["entities"]?.AsArray() ?? []) sk.Model.RemoveEntity(int.Parse(e!.ToString().TrimStart('e')));
                    foreach (var c in a["constraints"]?.AsArray() ?? []) { int id = c!.GetValue<int>(); var cc = sk.Model.Constraints.FirstOrDefault(x => x.Id == id && !x.Hidden); if (cc != null) sk.Model.Remove(cc); }
                });
                return SketchStateJson();
            });

        Add("sketch_finish", "Finish the open sketch (keep=true creates/updates the Sketch object, false discards it). Returns a short status; verbose:true returns the whole document state.",
            Schema(null, ("keep", "boolean", "Default true."), ("verbose", "boolean", "Return the full state.")), a =>
            {
                if (S.Sketch == null) throw new InvalidOperationException("No sketch is open.");
                S.ExitSketch(Bool(a, "keep", true));
                if (Bool(a, "verbose", false)) return State();
                var last = S.Objects.LastOrDefault(o => o.Kind == ObjKind.Sketch);
                return new JsonObject { ["status"] = S.Status, ["sketch"] = last?.Name, ["objectCount"] = S.Objects.Count };
            });
        Add("form_info", "Read a Form object (subdivision cage): vertices, faces (vertex index lists, 0-based, CCW from outside), creased edges, smoothing level. Indices here are 0-based and are what form_op expects.",
            Schema(["object"], ("object", "string", "Form object name."), ("summary", "boolean", "Counts and bounding box only (no vertex or face lists)."), ("only", "array", "Vertex indices: return only the positions of these vertices.")), a =>
            {
                var f = FormOf(Str(a, "object"));
                if (a["only"] is JsonArray only)
                {
                    var pos = new JsonObject();
                    foreach (var n in only) { int i = n!.GetValue<int>(); if (i >= 0 && i < f.V.Count) pos[i.ToString()] = Arr(f.V[i]); }
                    return new JsonObject { ["positions"] = pos };
                }
                if (Bool(a, "summary", false))
                {
                    var lo = new Vec3(f.V.Min(v => v.X), f.V.Min(v => v.Y), f.V.Min(v => v.Z)); var hi = new Vec3(f.V.Max(v => v.X), f.V.Max(v => v.Y), f.V.Max(v => v.Z));
                    return new JsonObject { ["vertexCount"] = f.V.Count, ["faceCount"] = f.F.Count, ["creaseCount"] = f.Crease.Count(c => c.Value > 0), ["symmetry"] = f.Sym, ["asymmetry"] = FormSymmetry.Of(f).Active ? Math.Round(FormSymmetry.Asymmetry(f, FormSymmetry.Of(f)), 6) : 0, ["smoothLevel"] = f.Level, ["closed"] = f.IsClosed(), ["min"] = Arr(lo), ["max"] = Arr(hi), ["topology"] = FormTopology(f) };
                }
                return new JsonObject
                {
                    ["vertices"] = new JsonArray(f.V.Select(v => (JsonNode)Arr(v)).ToArray()),
                    ["faces"] = new JsonArray(f.F.Select(fc => (JsonNode)new JsonArray(fc.Select(i => (JsonNode)i).ToArray())).ToArray()),
                    ["creases"] = new JsonArray(f.Crease.Where(c => c.Value > 0).Select(c => { var (x, y) = SubdMesh.Unkey(c.Key); return (JsonNode)new JsonObject { ["edge"] = new JsonArray(x, y), ["sharpness"] = c.Value }; }).ToArray()),
                    ["smoothLevel"] = f.Level, ["closed"] = f.IsClosed(), ["frozenFaces"] = new JsonArray(Enumerable.Range(0, f.F.Count).Where(f.FaceFrozen).Select(i => (JsonNode)i).ToArray()),
                    ["topology"] = FormTopology(f),
                };
            });

        Add("form_analyze", "Surface quality of a Form: cage statistics (quads, triangles, n-gons, poles with their positions, edge lengths, stretched quads), curvature of the smooth surface (mean, tightest radius and where), and optionally the deviation to a reference object (scan, body, form): within-tolerance percentage, RMS, bias, largest gaps. display:true also colors the form by that deviation in the viewport.",
            Schema(["object"], ("object", "string", "Form object name."), ("reference", "string", "Object to compare the smooth surface with."), ("tolerance", "number", "Deviation tolerance in mm (default 0.2)."), ("range", "number", "Color range ± in mm (default 1)."), ("display", "boolean", "Color the form by deviation."), ("poles", "boolean", "Include pole positions (default true).")), a =>
            {
                var o = FindObject(Str(a, "object")); var cage = o.Form ?? throw new ArgumentException($"{o.Name} is not a Form.");
                var q = FormQuality.Analyze(cage);
                var res = new JsonObject
                {
                    ["cage"] = new JsonObject { ["points"] = q.Vertices, ["faces"] = q.Faces, ["quads"] = q.Quads, ["triangles"] = q.Triangles, ["nGons"] = q.NGons, ["borderPoints"] = q.BoundaryVertices },
                    ["poles"] = new JsonObject { ["threeEdges"] = q.Poles3, ["fiveOrMore"] = q.Poles5OrMore, ["valenceHistogram"] = new JsonObject(q.Valence.Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key.ToString(), kv.Value))) },
                    ["edges"] = new JsonObject { ["shortest"] = Math.Round(q.ShortestEdge, 4), ["longest"] = Math.Round(q.LongestEdge, 4), ["mean"] = Math.Round(q.MeanEdge, 4), ["stretchedFaces"] = q.StretchedFaces, ["worstAspect"] = Math.Round(q.WorstAspect, 2) },
                    ["curvature"] = new JsonObject { ["mean"] = Math.Round(q.MeanCurvature, 5), ["max"] = Math.Round(q.MaxCurvature, 4), ["tightestRadius"] = q.MaxCurvature > 1e-9 ? Math.Round(1 / q.MaxCurvature, 3) : null, ["at"] = Arr(q.CurvatureHotSpot), ["spread"] = Math.Round(q.CurvatureSpread, 2) },
                };
                if (Bool(a, "poles", true)) res["poleList"] = new JsonArray(q.Poles.Take(60).Select(p => (JsonNode)new JsonObject { ["vertex"] = p.vertex, ["valence"] = p.valence, ["at"] = Arr(cage.V[p.vertex]) }).ToArray());
                if (a["reference"] != null)
                {
                    var refObj = FindObject(Str(a, "reference"));
                    var surface = Meshing.MeshAnalysis.FromMeshData(cage.BuildMesh(3));
                    var refMesh = S.PrintMesh(refObj) ?? throw new ArgumentException($"{refObj.Name} has no surface to compare with.");
                    var dev = Meshing.MeshAnalysis.Deviation(surface, refMesh, Dbl(a, "tolerance", 0.2));
                    res["deviation"] = new JsonObject { ["withinTolerancePercent"] = Math.Round(dev.WithinTolerance, 2), ["rms"] = Math.Round(dev.Rms, 4), ["bias"] = Math.Round(dev.Mean, 4), ["maxAbove"] = Math.Round(dev.Max, 4), ["maxBelow"] = Math.Round(dev.Min, 4) };
                    if (Bool(a, "display", false))
                    {
                        double range = Dbl(a, "range", 1);
                        var (disp, scalars) = surface.ToScalarMeshData(dev.PerVertex); disp.Scalars = scalars;
                        S.SetField(o, surface, disp, 1, (float)-range, (float)range);
                    }
                }
                return res;
            });

        Add("form_find","Find cage components by geometry so you do not have to read the whole cage. kind: vertices | faces | edges (default faces). Filters (all optional, combined with AND): box [[x0,y0,z0],[x1,y1,z1]] (a vertex, or a face/edge centre, inside), near [x,y,z] + radius, facing [dx,dy,dz] + angle (faces whose normal is within angle degrees, default 30), open:true (edges used by one face only), all:true. Returns 0-based indices ready for form_op / form_select, with centres (and normals for faces).",
            Schema(["object"], ("object", "string", "Form object name."), ("kind", "string", "vertices | faces | edges."), ("box", "array", "[[x0,y0,z0],[x1,y1,z1]]"), ("near", "array", "[x,y,z]"), ("radius", "number", "With near: distance (mm)."),
                ("facing", "array", "Faces whose outward normal points this way [dx,dy,dz]."), ("angle", "number", "With facing: tolerance in degrees (default 30)."), ("open", "boolean", "Edges with one face only (boundary)."), ("all", "boolean", "Every component."), ("limit", "integer", "Maximum results (default 200).")), a =>
            {
                var f = FormOf(Str(a, "object"));
                string kind = (a["kind"]?.ToString() ?? "faces").ToLowerInvariant();
                Vec3? lo = null, hi = null;
                if (a["box"] is JsonArray bx && bx.Count == 2) { var p = V3(bx[0]!); var q = V3(bx[1]!); lo = new Vec3(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Min(p.Z, q.Z)); hi = new Vec3(Math.Max(p.X, q.X), Math.Max(p.Y, q.Y), Math.Max(p.Z, q.Z)); }
                Vec3? near = a["near"] != null ? V3(a["near"]!) : null; double radius = Dbl(a, "radius", 10);
                Vec3? facing = a["facing"] != null ? V3(a["facing"]!).Normalized() : null; double cosTol = Math.Cos(Dbl(a, "angle", 30) * Math.PI / 180);
                int limit = (int)Dbl(a, "limit", 200);
                bool Pass(Vec3 c, Vec3? normal)
                {
                    if (lo is { } l && hi is { } h && (c.X < l.X - 1e-9 || c.Y < l.Y - 1e-9 || c.Z < l.Z - 1e-9 || c.X > h.X + 1e-9 || c.Y > h.Y + 1e-9 || c.Z > h.Z + 1e-9)) return false;
                    if (near is { } n && c.DistanceTo(n) > radius) return false;
                    if (facing is { } d) { if (normal is not { } nn || Vec3.Dot(nn.Normalized(), d) < cosTol) return false; }
                    return true;
                }
                if (lo == null && near == null && facing == null && !Bool(a, "open", false) && !Bool(a, "all", false))
                    throw new ArgumentException("Give at least one filter: box, near, facing, open or all.");
                var hits = new JsonArray(); int count = 0;
                if (kind is "vertices" or "vertex")
                {
                    for (int i = 0; i < f.V.Count && count < limit; i++) if (facing == null && Pass(f.V[i], null)) { hits.Add(new JsonObject { ["index"] = i, ["at"] = Arr(f.V[i]) }); count++; }
                }
                else if (kind is "edges" or "edge")
                {
                    var uses = new Dictionary<long, int>(); var ends = new Dictionary<long, (int a, int b)>();
                    foreach (var fc in f.F) for (int i = 0; i < fc.Length; i++) { int x = fc[i], y = fc[(i + 1) % fc.Length]; long k = SubdMesh.Key(x, y); uses[k] = uses.GetValueOrDefault(k) + 1; ends[k] = (Math.Min(x, y), Math.Max(x, y)); }
                    foreach (var (k, (x, y)) in ends)
                    {
                        if (count >= limit) break;
                        if (Bool(a, "open", false) && uses[k] != 1) continue;
                        var mid = (f.V[x] + f.V[y]) * 0.5;
                        if (facing == null && Pass(mid, null)) { hits.Add(new JsonObject { ["edge"] = new JsonArray(x, y), ["at"] = Arr(mid) }); count++; }
                    }
                }
                else
                {
                    for (int i = 0; i < f.F.Count && count < limit; i++)
                    {
                        var c = f.F[i].Aggregate(Vec3.Zero, (s, v) => s + f.V[v]) / f.F[i].Length; var nrm = f.FaceNormal(i);
                        if (Pass(c, nrm)) { hits.Add(new JsonObject { ["index"] = i, ["centre"] = Arr(c), ["normal"] = Arr(nrm) }); count++; }
                    }
                }
                return new JsonObject { ["kind"] = kind, ["count"] = hits.Count, ["results"] = hits, ["truncated"] = count >= limit };
            });

        Add("form_op", "Edit a Form cage (undoable). op: chamfer (edges or faces, amount: replaces each inner edge with a strip of faces) | slide (edges, amount -1..1: slide an edge loop along its rails) | crease_vertices (vertices, value: sharp corner points) | set_symmetry (symmetry x|y|z|radial:N|off, enforce:true makes the cage exactly symmetric from the + side, keep_positive:false for the - side) | move | rotate | scale | bridge | fill_holes | merge_faces | merge_edges | unweld_edges | pull | collapse_edges | split_face | smooth | flatten | set_vertices | extrude_faces | extrude_edges | inset | bevel | insert_loop | subdivide | delete_faces | weld | flip | crease | mirror | add_face | thicken | level. " +
            "A form with a persistent symmetry (set_symmetry) repeats extrude/inset/bevel/delete/crease/flatten/smooth/weld/collapse/unweld/pull/insert_loop/merge_faces on the mirror images of whatever you select; pass symmetry:off to bypass it for one call. pull: vertices (or all) move onto the smooth limit surface (strength 0..1). merge_edges: edges:[[a,b]..] removes inner edges, joining the faces either side. unweld_edges: edges (or faces) split the cage along them so each side gets its own points. " +
            "bridge: faces:[a,b] removes two separate equal-corner cap faces and joins their loops (for integrated handles), or edges:[[a,b],...] selects two separate boundary chains. " +
            "move/rotate/scale act on the vertices of whatever you select and accept soft_radius (+ falloff smooth|linear|sharp) and symmetry (x | y | z mirror, or radial:N around Z; mirrored points follow). " +
            "rotate: axis X|Y|Z, angle (degrees), optional pivot [x,y,z] (default the selection centre). scale: factor, or scale [sx,sy,sz], optional pivot. " +
            "inset: faces, amount, optional depth and individual:true. bevel: edges or faces, amount (support loops that tighten the edge). insert_loop: edge [a,b], optional count and position (0..1 from a). " +
            "Components are 0-based indices from form_info: vertices:[i..], faces:[i..], edges:[[a,b]..]. move: delta [dx,dy,dz] applied to the vertices of whatever you select. " +
            "set_vertices: positions {\"3\":[x,y,z]}. extrude_faces/edges: distance. insert_loop: edge [a,b]. crease: value (0 clears). mirror: plane XY|XZ|YZ (welds the seam). " +
            "add_face: vertices [ids] and/or points [[x,y,z]] (new corners) - ids first. level: value 1-4.",
            Schema(["object", "op"], ("object", "string", "Form object name."), ("op", "string", "Operation."), ("vertices", "array", "Vertex indices."), ("faces", "array", "Face indices."),
                ("edges", "array", "Edges as [a,b] pairs."), ("edge", "array", "One edge [a,b]."), ("delta", "array", "[dx,dy,dz]."), ("distance", "number", "Extrude distance."),
                ("value", "number", "Crease sharpness or smoothing level."), ("plane", "string", "Mirror plane."), ("positions", "object", "Vertex index -> [x,y,z]."), ("points", "array", "New corner points."),
                ("angle", "number", "rotate: degrees."), ("axis", "string", "rotate: X | Y | Z."), ("factor", "number", "scale: uniform factor."), ("scale", "array", "scale: [sx,sy,sz]."), ("pivot", "array", "rotate/scale pivot [x,y,z]."),
                ("soft_radius", "number", "Soft selection radius (mm; 0 = off)."), ("falloff", "string", "smooth | linear | sharp."), ("symmetry", "string", "x | y | z | radial:N | off."),
                ("all", "boolean", "move/rotate/scale/smooth/flatten: act on every vertex when nothing is selected."), ("amount", "number", "inset / bevel size (mm)."), ("depth", "number", "inset: push out (+) or in (-)."), ("individual", "boolean", "inset each face on its own."), ("count", "integer", "insert_loop: number of loops."), ("position", "number", "insert_loop: 0..1 from the first vertex of the edge.")), a =>
            {
                var o = FindObject(Str(a, "object")); var m = (o.Form ?? throw new ArgumentException($"{o.Name} is not a Form.")).Clone();
                var op = Str(a, "op").ToLowerInvariant();
                var faceSet = a["faces"]?.AsArray().Select(x => x!.GetValue<int>()).ToHashSet() ?? [];
                var edgeList = a["edges"]?.AsArray().Select(e => (a: e!.AsArray()[0]!.GetValue<int>(), b: e.AsArray()[1]!.GetValue<int>())).ToList() ?? [];
                var vertSet = a["vertices"]?.AsArray().Select(x => x!.GetValue<int>()).ToHashSet() ?? [];
                if (faceSet.Any(f => f < 0 || f >= m.F.Count)) throw new ArgumentException($"Face indices must be between 0 and {m.F.Count - 1}.");
                if (vertSet.Any(v => v < 0 || v >= m.V.Count) || edgeList.Any(e => e.a < 0 || e.b < 0 || e.a >= m.V.Count || e.b >= m.V.Count))
                    throw new ArgumentException($"Vertex indices must be between 0 and {m.V.Count - 1}.");
                // symmetry: an explicit "symmetry" argument wins, otherwise the form's own (persistent) symmetry applies
                var symText = a["symmetry"]?.ToString();
                var effSym = symText != null ? Symmetry.Parse(symText) : FormSymmetry.Of(m);
                bool mirrorSets = effSym.Active && op is "extrude_faces" or "extrude_edges" or "inset" or "bevel" or "delete_faces" or "crease" or "crease_vertices" or "chamfer" or "slide" or "flatten" or "smooth" or "weld" or "collapse_edges" or "unweld_edges" or "pull";
                if (effSym.Active && mirrorSets) FormSymmetry.Expand(m, effSym, vertSet, faceSet, edgeList);
                foreach (var f in faceSet) foreach (var v in m.F[f]) vertSet.Add(v);
                foreach (var (x, y) in edgeList) { vertSet.Add(x); vertSet.Add(y); }
                if (Bool(a, "all", false) && vertSet.Count == 0) vertSet = Enumerable.Range(0, m.V.Count).ToHashSet();   // all:true acts on the whole cage
                var info = new JsonObject();
                switch (op)
                {
                    case "move" or "rotate" or "scale":
                    {
                        if (vertSet.Count == 0) throw new ArgumentException("Select vertices, edges or faces to transform.");
                        var sym = effSym;
                        var fall = (a["falloff"]?.ToString() ?? "smooth").ToLowerInvariant() switch { "linear" => Falloff.Linear, "sharp" => Falloff.Sharp, _ => Falloff.Smooth };
                        var centre = vertSet.Aggregate(Vec3.Zero, (acc, v) => acc + m.V[v]) / vertSet.Count;
                        var pivot = a["pivot"] != null ? V3(a["pivot"]!) : centre;
                        Func<Vec3, Vec3> xf;
                        if (op == "move") xf = FormTransform.MoveBy(V3(a["delta"] ?? throw new ArgumentException("delta required")));
                        else if (op == "rotate")
                        {
                            var ax = (a["axis"]?.ToString() ?? "Z").ToUpperInvariant() switch { "X" => Vec3.UnitX, "Y" => Vec3.UnitY, _ => Vec3.UnitZ };
                            xf = FormTransform.RotateAbout(pivot, ax, Dbl(a, "angle", 15) * Math.PI / 180);
                        }
                        else
                        {
                            double fx = Dbl(a, "factor", 1), fy = fx, fz = fx;
                            if (a["scale"] is JsonArray sc) { fx = sc[0]!.GetValue<double>(); fy = sc[1]!.GetValue<double>(); fz = sc[2]!.GetValue<double>(); }
                            xf = FormTransform.ScaleAbout(pivot, fx, fy, fz);
                        }
                        FormTransform.Apply(m, m.V.ToArray(), vertSet, xf, Dbl(a, "soft_radius", 0), fall, sym, sym.Active ? sym.Partners(m) : null);
                        break;
                    }
                    case "set_vertices":
                    {
                        if (a["positions"] is not JsonObject positions || positions.Count == 0) throw new ArgumentException("positions is required: vertex index -> [x,y,z].");
                        foreach (var (key, point) in positions)
                        {
                            if (!int.TryParse(key, out int index) || index < 0 || index >= m.V.Count)
                                throw new ArgumentException($"Vertex index '{key}' must be between 0 and {m.V.Count - 1}.");
                            if (point is not JsonArray xyz || xyz.Count != 3) throw new ArgumentException($"Vertex {key} needs exactly three coordinates [x,y,z].");
                            var value = V3(point);
                            if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z)) throw new ArgumentException($"Vertex {key} coordinates must be finite.");
                        }
                        var symv = effSym;
                        var partners = symv.Active ? symv.Partners(m) : null;
                        var set = a["positions"]!.AsObject().Select(kv => int.Parse(kv.Key)).ToHashSet();
                        foreach (var (k, p) in a["positions"]!.AsObject())
                        {
                            int vi = int.Parse(k); var np = V3(p!); m.V[vi] = np;
                            if (partners != null) foreach (var (pi, mk) in partners[vi]) { if (pi == vi) m.V[vi] = symv.Constrain(np); else if (!set.Contains(pi)) m.V[pi] = symv.Map(mk, np); }
                        }
                        break;
                    }
                    case "bridge":
                        if (faceSet.Count == 2 && edgeList.Count == 0)
                        {
                            var caps = faceSet.ToArray(); FormOps.BridgeFaces(m, caps[0], caps[1]);
                        }
                        else if (faceSet.Count == 0)
                        {
                            var chains = FormOps.Chains(edgeList);
                            if (chains.Count != 2) throw new ArgumentException("Bridge requires two faces, or two separate chains of boundary edges.");
                            FormOps.Bridge(m, chains[0].verts, chains[0].closed, chains[1].verts, chains[1].closed);
                        }
                        else throw new ArgumentException("Bridge requires exactly two faces, or two separate chains of boundary edges.");
                        info["closed"] = m.IsClosed();
                        break;
                    case "inset":
                        if (faceSet.Count == 0) throw new ArgumentException("faces required");
                        FormOps.InsetFaces(m, faceSet, Dbl(a, "amount", 3), Dbl(a, "depth", 0), Bool(a, "individual", false));
                        info["selectFaces"] = new JsonArray(faceSet.Select(f => (JsonNode)f).ToArray());
                        break;
                    case "bevel":
                    {
                        var be = edgeList.ToList();
                        foreach (var f in faceSet) { var fc = m.F[f]; for (int i = 0; i < fc.Length; i++) be.Add((fc[i], fc[(i + 1) % fc.Length])); }
                        info["loopsAdded"] = FormOps.BevelEdges(m, be, Dbl(a, "amount", 2));
                        break;
                    }
                    case "extrude_faces":
                        FormOps.ExtrudeFaces(m, faceSet, Dbl(a, "distance", 5));
                        info["selectFaces"] = new JsonArray(faceSet.Select(f => (JsonNode)f).ToArray());
                        break;
                    case "extrude_edges":
                        var top = FormOps.ExtrudeEdges(m, edgeList);
                        if (top.Count == 0) throw new InvalidOperationException("Only boundary edges can be extruded.");
                        double dist = Dbl(a, "distance", 5);
                        var topo = m.Topo();
                        foreach (var (x, y) in top)
                            foreach (var v in new[] { x, y }) { var n = topo.VertFaces[v].Aggregate(Vec3.Zero, (s, f) => s + m.FaceNormal(f)).Normalized(); m.V[v] += n * dist; }
                        info["newEdges"] = new JsonArray(top.Select(e => (JsonNode)new JsonArray(e.Item1, e.Item2)).ToArray());
                        break;
                    case "insert_loop":
                    {
                        var ea = a["edge"]!.AsArray(); int ea0 = ea[0]!.GetValue<int>(), ea1 = ea[1]!.GetValue<int>();
                        var mirrored = new List<(int a, int b)>();
                        if (effSym.Active) { var one = new List<(int a, int b)> { (ea0, ea1) }; FormSymmetry.Expand(m, effSym, new HashSet<int>(), new HashSet<int>(), one); mirrored = one.Skip(1).ToList(); }
                        int count = (int)Dbl(a, "count", 1); double where = Dbl(a, "position", 0.5);
                        var nv = FormOps.InsertEdgeLoops(m, ea0, ea1, count, where);
                        foreach (var (x, y) in mirrored)
                            if (m.Topo().EdgeFaces.ContainsKey(SubdMesh.Key(x, y)))        // the loop may already have run through the mirror edge
                            {
                                // a mirror image reverses the direction along the edge only when its ends map in the opposite order, which Partners() preserves
                                nv.AddRange(FormOps.InsertEdgeLoops(m, x, y, count, where));
                            }
                        info["newVertices"] = new JsonArray(nv.Select(i => (JsonNode)i).ToArray());
                        break;
                    }
                    case "set_symmetry":
                    {
                        var wanted = Symmetry.Parse(a["symmetry"]?.ToString() ?? a["value"]?.ToString() ?? "off");
                        m.Sym = wanted.Text;
                        if (Bool(a, "enforce", false)) info["pointsMoved"] = FormSymmetry.Enforce(m, wanted, Bool(a, "keep_positive", true));
                        break;
                    }
                    case "subdivide":
                        var r = m.Refine(out _); m.ReplaceWith(r);
                        break;
                    case "delete_faces": FormOps.DeleteFaces(m, faceSet); break;
                    case "weld": m.Weld(vertSet); break;
                    case "thicken": FormOps.Solidify(m, Dbl(a, "amount", 2)); break;
                    case "fill_holes": info["filled"] = FormOps.FillHoles(m); break;
                    case "merge_faces":
                    {
                        var twins = effSym.Active && effSym.Maps == 1 ? FormSymmetry.ImagesOf(m, effSym, faceSet) : [];
                        int firstFace = faceSet.Count > 0 ? faceSet.Min() : 0; var removed = faceSet.Where(f => f != firstFace).ToList();
                        if (!FormOps.MergeFaces(m, faceSet)) throw new ArgumentException("The faces must be two or more touching faces with a single outline.");
                        // the mirror image is merged too (its face numbers shift down by the faces removed in front of it)
                        var shifted = twins.Select(f => f - removed.Count(r => r < f)).ToHashSet();
                        if (shifted.Count >= 2) { try { FormOps.MergeFaces(m, shifted); } catch (Exception) { } }
                        break;
                    }
                    case "collapse_edges":
                    {
                        var ce = edgeList.ToList();
                        foreach (var f in faceSet) { var fc = m.F[f]; for (int i = 0; i < fc.Length; i++) ce.Add((fc[i], fc[(i + 1) % fc.Length])); }
                        if (ce.Count == 0) throw new ArgumentException("edges (or faces) required");
                        info["collapsed"] = FormOps.CollapseEdges(m, ce);
                        break;
                    }
                    case "split_face":
                        var corners = a["vertices"]?.AsArray().Select(x => x!.GetValue<int>()).ToList() ?? [];
                        if (faceSet.Count != 1 || corners.Count != 2) throw new ArgumentException("Give one face and the two corner vertices (vertices:[a,b]) to cut between.");
                        if (!FormOps.SplitFace(m, faceSet.First(), corners[0], corners[1])) throw new ArgumentException("Pick two corners of that face that are not neighbours.");
                        break;
                    case "smooth":
                        FormOps.SmoothVertices(m, vertSet.Count > 0 ? vertSet : Enumerable.Range(0, m.V.Count).ToHashSet(), Dbl(a, "strength", 0.5), (int)Dbl(a, "passes", 1));
                        break;
                    case "flatten":
                        if (!FormOps.FlattenVertices(m, vertSet, Dbl(a, "strength", 1))) throw new ArgumentException("Select at least three points (or faces) to flatten.");
                        break;
                    case "flip": FormOps.FlipNormals(m); break;
                    case "crease_vertices":
                        info["creasedPoints"] = FormOps.SetVertexCrease(m, vertSet, Dbl(a, "value", 1));
                        break;
                    case "chamfer":
                    {
                        var ce2 = edgeList.ToList();
                        foreach (var f in faceSet) { var fc = m.F[f]; for (int i = 0; i < fc.Length; i++) ce2.Add((fc[i], fc[(i + 1) % fc.Length])); }
                        if (ce2.Count == 0) throw new ArgumentException("edges (or faces) required");
                        info["newFaces"] = FormOps.ChamferEdges(m, ce2, Dbl(a, "amount", 2));
                        break;
                    }
                    case "slide":
                    {
                        if (edgeList.Count == 0) throw new ArgumentException("edges required: the edge loop to slide");
                        info["slid"] = FormOps.SlideEdges(m, edgeList, Dbl(a, "amount", 0.3));
                        break;
                    }
                    case "pull":
                        info["pulled"] = FormOps.PullToLimit(m, vertSet.Count > 0 ? vertSet : Enumerable.Range(0, m.V.Count).ToHashSet(), Dbl(a, "strength", 1));
                        break;
                    case "merge_edges":
                    {
                        var me = edgeList.ToList();
                        if (me.Count == 0) throw new ArgumentException("edges required");
                        info["merged"] = FormOps.MergeEdges(m, me);
                        break;
                    }
                    case "unweld_edges":
                    {
                        var ue = edgeList.ToList();
                        foreach (var f in faceSet) { var fc = m.F[f]; for (int i = 0; i < fc.Length; i++) ue.Add((fc[i], fc[(i + 1) % fc.Length])); }
                        if (ue.Count == 0) throw new ArgumentException("edges (or faces) required");
                        info["duplicatedPoints"] = FormOps.UnweldEdges(m, ue);
                        break;
                    }
                    case "crease":
                        var edges = edgeList.ToList();
                        foreach (var f in faceSet) { var fc = m.F[f]; for (int i = 0; i < fc.Length; i++) edges.Add((fc[i], fc[(i + 1) % fc.Length])); }
                        FormOps.SetCrease(m, edges, Dbl(a, "value", 1));
                        break;
                    case "mirror":
                        var n2 = (a["plane"]?.ToString() ?? "XZ").ToUpperInvariant() switch { "XY" => Vec3.UnitZ, "YZ" => Vec3.UnitX, _ => Vec3.UnitY };
                        FormOps.MirrorWeld(m, Vec3.Zero, n2);
                        break;
                    case "add_face":
                        var ids = a["vertices"]?.AsArray().Select(x => x!.GetValue<int>()).ToList() ?? [];
                        if (a["points"] is JsonArray pts) foreach (var p in pts) { ids.Add(m.V.Count); m.V.Add(V3(p!)); }
                        if (ids.Count < 3) throw new ArgumentException("A face needs at least 3 corners.");
                        FormOps.AddFace(m, ids, S.Camera.EyeDirection);
                        info["faceIndex"] = m.F.Count - 1;
                        break;
                    case "level": m.Level = (int)Math.Clamp(Dbl(a, "value", 3), 0, 4); break;
                    default: throw new ArgumentException($"Unknown form op '{op}'.");
                }
                var co = new CommandOutput { Label = "Freeform: " + op, Icon = "form-edit" };
                co.ReplaceForms.Add((o, m)); S.Apply(co);
                info["vertexCount"] = m.V.Count; info["faceCount"] = m.F.Count;
                if (FormSymmetry.Of(m).Active) info["symmetry"] = m.Sym;
                info["topology"] = FormTopology(m);
                return info;
            });

        Add("form_select", "While Edit Freeform is open (run_command edit_form with commit=false), select cage components so the manipulator and highlights show. mode: vertices|edges|faces. Indices are 0-based as in form_info.",
            Schema(["mode"], ("mode", "string", "vertices | edges | faces"), ("all", "boolean", "Select every component of that kind."), ("op", "string", "grow | shrink | invert | all | angle (flood faces from the selection) | boundary | creased: selection tools applied after any indices given."), ("angle", "number", "With op angle: dihedral limit in degrees (default 30)."), ("vertices", "array", "Vertex indices."), ("edges", "array", "[[a,b],..]"), ("faces", "array", "Face indices.")), a =>
            {
                var c = S.ActiveCommand as EditFormCommand ?? throw new InvalidOperationException("Edit Freeform is not open. Use run_command {name:edit_form, slots:{Freeform:..}, commit:false}.");
                c.Mode.Index = Str(a, "mode").ToLowerInvariant() switch { "vertices" or "vertex" => 0, "edges" or "edge" => 1, _ => 2 };
                c.ClearSelection();
                if (Bool(a, "all", false) && c.Work is { } whole)
                {
                    if (c.Mode.Index == 0) for (int i = 0; i < whole.V.Count; i++) c.Verts.Add(i);
                    else if (c.Mode.Index == 1) foreach (var (x, y) in whole.Edges()) c.Edges.Add(SubdMesh.Key(x, y));
                    else for (int i = 0; i < whole.F.Count; i++) c.Faces.Add(i);
                }
                foreach (var v in a["vertices"]?.AsArray() ?? []) c.Verts.Add(v!.GetValue<int>());
                foreach (var e in a["edges"]?.AsArray() ?? []) c.Edges.Add(SubdMesh.Key(e!.AsArray()[0]!.GetValue<int>(), e.AsArray()[1]!.GetValue<int>()));
                foreach (var f in a["faces"]?.AsArray() ?? []) c.Faces.Add(f!.GetValue<int>());
                if (a["op"] != null)
                {
                    if (a["angle"] != null) c.Params.OfType<NumberParam>().First(p => p.Label.StartsWith("Flood-select angle")).Value = Dbl(a, "angle", 30);
                    c.SelectOp(Str(a, "op").ToLowerInvariant());
                }
                c.SelectionNotify(); c.OnChanged();
                return CommandJson(c, full: false);
            });

        Add("origin", "Show or hide the origin helpers (they also appear in the browser): planes, axes, origin point.",
            Schema(null, ("planes", "boolean", "XY/XZ/YZ planes."), ("axes", "boolean", "X/Y/Z axes."), ("point", "boolean", "Origin point.")), a =>
            {
                if (a["planes"] != null) for (int i = 0; i < 3; i++) S.SetOriginVisible(i, a["planes"]!.GetValue<bool>());
                if (a["axes"] != null) for (int i = 3; i < 6; i++) S.SetOriginVisible(i, a["axes"]!.GetValue<bool>());
                if (a["point"] != null) S.SetOriginVisible(6, a["point"]!.GetValue<bool>());
                _vm.RebuildBrowser();
                return new JsonArray(CadSession.OriginNames.Select((n, i) => (JsonNode)new JsonObject { ["name"] = n, ["visible"] = S.OriginVisible[i] }).ToArray());
            });

        Add("history_view", "Inspect or resize the design history panel. Collapsed mode shows a compact, interactive trail of the active branch.",
            Schema(null, ("expanded", "boolean", "Show the full branch graph, or the compact trail when false."), ("height", "number", "Expanded graph height, clamped to 100–360.")), a =>
            {
                if (a["expanded"] != null) _vm.HistoryExpanded = Bool(a, "expanded", true);
                if (a["height"] != null) _vm.HistoryHeight = Math.Clamp(Dbl(a, "height", 180), 100, 360);
                return new JsonObject { ["expanded"] = _vm.HistoryExpanded, ["height"] = _vm.HistoryHeight, ["summary"] = _vm.HistorySummary };
            });

        Add("history_tree", "The full history tree: every step with its parent, label, time, checkpoint name and which one is current. Branches exist when an edit was made after rolling back.",
            Schema(null), _ =>
            {
                var arr = new JsonArray();
                foreach (var n in S.AllNodes())
                    arr.Add(new JsonObject { ["id"] = n.Id, ["parent"] = n.Parent?.Id, ["label"] = n.Label, ["time"] = n.Time.ToString("HH:mm:ss"), ["pin"] = n.Pin, ["current"] = n == S.Current, ["children"] = n.Children.Count });
                return arr;
            });

        Add("history_goto", "Jump to a history step by id (see history_tree). Use this to switch between branches.",
            Schema(["id"], ("id", "integer", "Node id.")), a =>
            {
                int id = (int)Dbl(a, "id", -1);
                var n = S.AllNodes().FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException($"No history step {id}.");
                S.GoTo(n);
                return new JsonObject { ["current"] = n.Id, ["label"] = n.Label, ["objects"] = S.Objects.Count };
            });

        Add("form_to_nurbs", "Convert a Form into exact NURBS geometry (a solid if the cage is closed, otherwise a surface). The result can be used with booleans, fillets, STEP export, etc.",
            Schema(["object"], ("object", "string", "Form object name."), ("detail", "string", "exact (default) | fewer: one fitted patch per irregular face instead of four, for lighter models.")), a =>
            {
                var o = FindObject(Str(a, "object"));
                if (o.Form == null) throw new ArgumentException($"{o.Name} is not a Form.");
                var shape = (a["detail"]?.ToString() ?? "exact").StartsWith("fewer", StringComparison.OrdinalIgnoreCase) ? FormToBrep.ToShape(o.Form, ConvertDetail.Fewer) : o.Shape;
                var co = new CommandOutput { Label = "Convert to NURBS", Icon = "form-nurbs" };
                co.Replace.Add((o, shape)); S.Apply(co);
                return Describe(FindObject(o.Name), true);
            });

        Add("get_control_points", "Read the NURBS control net of a curve or single-patch surface (rows x cols, row-major).",
            Schema(["object"], ("object", "string", "Object name.")), a =>
            {
                var m = NetModel.From(FindObject(Str(a, "object")));
                return new JsonObject
                {
                    ["isSurface"] = m.IsSurface, ["rows"] = m.Net.Rows, ["cols"] = m.Net.Cols, ["degreeU"] = m.Net.DegU, ["degreeV"] = m.Net.DegV,
                    ["points"] = new JsonArray(m.Points.Select(p => (JsonNode)Arr(p)).ToArray()),
                };
            });

        Add("set_control_points", "Replace the control points of a curve/surface (same count and order as get_control_points). Undoable.",
            Schema(["object", "points"], ("object", "string", "Object name."), ("points", "array", "[[x,y,z],...] - same length as get_control_points.")), a =>
            {
                var o = FindObject(Str(a, "object"));
                var m = NetModel.From(o);
                var pts = a["points"]!.AsArray().Select(p => V3(p!)).ToArray();
                if (pts.Length != m.Points.Length) throw new ArgumentException($"Expected {m.Points.Length} points, got {pts.Length}.");
                var co = new CommandOutput(); co.Replace.Add((o, m.With(pts))); S.Apply(co);
                return Describe(FindObject(o.Name));
            });

        Add("view", "Control the camera and display. preset: front|back|left|right|top|bottom|iso. Or yaw/pitch in degrees. fit=true zooms to extents.",
            Schema(null, ("preset", "string", "Standard view."), ("yaw", "number", "Yaw degrees."), ("pitch", "number", "Pitch degrees."), ("fit", "boolean", "Zoom to extents."),
                ("orthographic", "boolean", "Projection."), ("display", "string", "shaded | shaded_edges | wireframe"), ("xray", "boolean", "Enable or disable translucent X-ray, independently of display style."), ("zebra", "boolean", "Zebra stripes."), ("grid", "boolean", "Grid visibility."),
                ("select_mode", "string", "object | face | edge"), ("distance", "number", "Camera distance."), ("target", "array", "Camera target [x,y,z]."),
                ("look_at", "boolean", "Look straight at the selected plane, face or sketch (or the open sketch)."), ("layout", "string", "single | dual (model + top) | quad (front, right, top + model)"), ("link", "boolean", "Link pan/zoom between the views of a multi-view layout.")), a =>
            {
                if (Bool(a, "look_at", false)) S.LookAtSelection();
                if (a["layout"] != null) S.SetLayout(a["layout"]!.ToString().ToLowerInvariant() switch { "dual" => ViewLayout.Dual, "quad" => ViewLayout.Quad, _ => ViewLayout.Single });
                if (a["link"] != null) S.LinkViews = a["link"]!.GetValue<bool>();
                if (a["preset"] != null)
                {
                    var (y, p) = a["preset"]!.ToString().ToLowerInvariant() switch
                    {
                        "front" => (0.0, 0.0), "back" => (180.0, 0.0), "left" => (-90.0, 0.0), "right" => (90.0, 0.0), "top" => (0.0, 90.0), "bottom" => (0.0, -90.0), "iso" => (-35.0, 28.0),
                        var x => throw new ArgumentException($"Unknown preset '{x}'."),
                    };
                    S.Cameras[0].SetView(y, p);
                }
                if (a["yaw"] != null || a["pitch"] != null) S.Cameras[0].SetView(Dbl(a, "yaw", S.Cameras[0].Yaw * 180 / Math.PI), Dbl(a, "pitch", S.Cameras[0].Pitch * 180 / Math.PI));
                if (a["orthographic"] != null) S.Ortho = a["orthographic"]!.GetValue<bool>();
                if (a["display"] != null) S.Display = a["display"]!.ToString().ToLowerInvariant() switch { "shaded" => DisplayMode.Shaded, "wireframe" => DisplayMode.Wireframe, "shaded_edges" or "shaded-edges" or "shaded + edges" or "shadededges" => DisplayMode.ShadedEdges, var value => throw new ArgumentException($"Unknown display style '{value}'. Use shaded, shaded_edges, or wireframe.") };
                if (a["xray"] != null) S.XRay = Bool(a,"xray",false);
                if (a["zebra"] != null) S.Zebra = a["zebra"]!.GetValue<bool>();
                if (a["grid"] != null) S.ShowGrid = a["grid"]!.GetValue<bool>();
                if (a["select_mode"] != null) S.Mode = a["select_mode"]!.ToString().ToLowerInvariant() switch { "face" => SelMode.Face, "edge" => SelMode.Edge, _ => SelMode.Object };
                if (a["distance"] != null) S.Camera.Distance = Dbl(a, "distance", S.Camera.Distance);
                if (a["target"] != null) S.Camera.Target = V3(a["target"]!);
                if (Bool(a, "fit", false)) S.ZoomExtents();
                S.Invalidate();
                var view = State()["camera"]!.DeepClone().AsObject();
                view["display"] = S.Display.ToString(); view["xray"] = S.XRay;
                view["grid"] = S.ShowGrid; view["orthographic"] = S.Ortho;
                return view;
            });

        Add("script_dialog", "Show a dialog and wait for the user: fields [{key, label, type: number|text|check|choice, value, min, max, step, options, unit}]. Returns {ok:true, values:{key: value}} or {ok:false} when cancelled. Meant for scripts that need input.",
            Schema(["fields"], ("title", "string", "Dialog title."), ("fields", "array", "Field definitions.")), async a =>
            {
                var fields = a["fields"] as JsonArray ?? throw new ArgumentException("fields required");
                var res = await Dispatcher.UIThread.InvokeAsync(() => Prism.Views.ScriptDialog.ShowAsync(Str(a, "title", "Script"), fields));
                return res == null ? new JsonObject { ["ok"] = false } : new JsonObject { ["ok"] = true, ["values"] = res };
            });
        Add("marking_menu", "The right-click marking menu. action: settings (open the Settings window) | menu (the 6 o'clock plain menu) | list (the eight entries for what is selected right now, clockwise from the top), open (show it at x,y in the viewport), run (index 0-7: do that entry).",
            Schema(["action"], ("action", "string", "list | open | run"), ("index", "integer", "Entry 0-7 for run."), ("x", "number", "Viewport x for open."), ("y", "number", "Viewport y for open.")), a =>
            {
                var vm = _vm;
                switch (Str(a, "action"))
                {
                    case "settings": vm.RequestSettings?.Invoke(null); return new JsonObject { ["opened"] = true };
                    case "menu":
                    {
                        JsonNode Spec(Prism.ViewModels.MenuSpec m) => m.IsSeparator ? "-" : m.Sub != null ? new JsonObject { [m.Label] = new JsonArray(m.Sub.Select(Spec).ToArray()) } : m.Label + (m.Enabled ? "" : " (off)");
                        return new JsonObject { ["items"] = new JsonArray(vm.BuildTraditionalMenu().Select(Spec).ToArray()) };
                    }
                    case "open": vm.ShowMarkingMenu(Dbl(a, "x", 400), Dbl(a, "y", 300)); return new JsonObject { ["opened"] = true };
                    case "run":
                    {
                        var items = vm.BuildMarkingItems(); int i = (int)Dbl(a, "index", 0);
                        var it = i >= 0 && i < items.Count ? items[i] : null;
                        if (it == null) throw new ArgumentException("That slot is empty."); it.Run(); return new JsonObject { ["ran"] = it.Label };
                    }
                    default:
                        return new JsonObject { ["items"] = new JsonArray(vm.BuildMarkingItems().Select(m => (JsonNode)(m == null ? "-" : m.Label)).ToArray()) };
                }
            });
        Add("edit_feature", "Edit an earlier feature (the model rolls back to before it and its command reopens with the same picks and numbers; change values with command_set, then command_commit rebuilds the later steps). action: list (steps that can be edited) | open (step = history step id; omitted = the step that made the selected object).",
            Schema(["action"], ("action", "string", "list | open"), ("step", "integer", "History step id from list or history_tree.")), a =>
            {
                if (Str(a, "action") == "list")
                    return new JsonObject { ["steps"] = new JsonArray(_vm.Session.AllNodes().Where(n => n.Recipe != null).Select(n => (JsonNode)new JsonObject { ["step"] = n.Id, ["label"] = n.Label }).ToArray()) };
                var node = a.ContainsKey("step") ? _vm.Session.AllNodes().FirstOrDefault(n => n.Id == (int)Dbl(a, "step", -1)) : _vm.SelectedFeatureNode();
                if (node == null) throw new ArgumentException("No such step, or nothing editable is selected.");
                if (!_vm.Session.EditFeature(node)) throw new InvalidOperationException(_vm.Session.Status);
                return new JsonObject { ["editing"] = node.Label };
            });
        Add("history_group", "Fold a run of steps into ONE undo step. Call with action:begin, make changes, then action:end with a label. Groups nest.",
            Schema(["action"], ("action", "string", "begin | end"), ("label", "string", "Name of the history step (end).")), a =>
            {
                if (Str(a, "action") == "begin") S.BeginGroup(); else S.EndGroup(Str(a, "label", "Script"));
                return new JsonObject { ["ok"] = true };
            });
        Add("history", "Undo or redo.", Schema(["action"], ("action", "string", "undo | redo"), ("count", "integer", "Repeat count (default 1).")), a =>
        {
            int n = (int)Dbl(a, "count", 1);
            for (int i = 0; i < n; i++) { if (Str(a, "action") == "redo") S.Redo(); else S.Undo(); }
            return new JsonObject { ["canUndo"] = S.CanUndo, ["canRedo"] = S.CanRedo, ["objects"] = S.Objects.Count };
        });

        Add("file", "Document I/O. action: new | open | save | save_as | import_step | import_file (by extension: STEP, IGES, BREP, DXF, STL, OBJ, PLY, 3MF) | export_step | export_iges | export_brep | export_glb | export_dxf | export_stl (also .3mf/.obj/.ply by extension). 'path' required except for new/save.",
            Schema(["action"], ("action", "string", "Action."), ("path", "string", "File path.")), a =>
            {
                var path = a["path"]?.ToString();
                switch (Str(a, "action"))
                {
                    case "new": S.NewDocument(); _vm.CloseHome(); break;
                    case "open": S.Open(path ?? throw new ArgumentException("path required")); _vm.CloseHome(); break;
                    case "save": S.Save(path ?? S.FilePath ?? throw new ArgumentException("path required for an unsaved document")); break;
                    case "save_as": S.Save(path ?? throw new ArgumentException("path required")); break;
                    case "import_step": S.ImportStep(path ?? throw new ArgumentException("path required")); _vm.CloseHome(); break;
                    case "import_file": S.ImportFile(path ?? throw new ArgumentException("path required")); _vm.CloseHome(); break;
                    case "export_step": S.ExportStep(path ?? throw new ArgumentException("path required")); break;
                    case "export_iges": S.ExportIges(path ?? throw new ArgumentException("path required")); break;
                    case "export_brep": S.ExportBrep(path ?? throw new ArgumentException("path required")); break;
                    case "export_glb": S.ExportGlb(path ?? throw new ArgumentException("path required")); break;
                    case "export_dxf": S.ExportDxf(path ?? throw new ArgumentException("path required")); break;
                    case "export_stl": S.ExportStl(path ?? throw new ArgumentException("path required")); break;
                    default: throw new ArgumentException("Unknown file action.");
                }
                return new JsonObject { ["status"] = S.Status, ["objects"] = S.Objects.Count };
            });

        Add("batch", "Run several tool calls in order in one round trip. Stops at the first error. calls: [{tool, args}].",
            Schema(["calls"], ("calls", "array", "[{\"tool\": \"...\", \"args\": {...}}, ...]")), async a =>
            {
                var results = new JsonArray();
                foreach (var c in a["calls"]!.AsArray())
                {
                    var o = c!.AsObject(); var name = Str(o, "tool");
                    if (name == "batch") throw new ArgumentException("batch cannot be nested.");
                    try
                    {
                        var r = await _tools.GetValueOrDefault(name)!.Run(o["args"] as JsonObject ?? []);
                        if (r is JsonObject ro && ro.ContainsKey("_image")) ro.Remove("_image");
                        results.Add(new JsonObject { ["tool"] = name, ["result"] = r });
                    }
                    catch (Exception e)
                    {
                        results.Add(new JsonObject { ["tool"] = name, ["error"] = e.Message });
                        break;
                    }
                }
                return results;
            });
    }
}
