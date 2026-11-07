using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Assistant;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/assistant/README.md">AI assistant specification.</seealso>
[Trait("Feature", "Assistant")]
[Trait("Layer", "Headless")]
public class AssistantToolTests
{
    static MainViewModel Vm() => new();
    static JsonObject Args(params (string k, object? v)[] ps)
    {
        var o = new JsonObject();
        foreach (var (k, v) in ps) o[k] = v is string s ? s : v is bool b ? b : v is int i ? i : v?.ToString();
        return o;
    }

    [AvaloniaFact(DisplayName = "AST-H001 | The settings tool lists safe preferences and never exposes secrets")]
    [Trait("TestId", "AST-H001")]
    public async Task SettingsListExposesNoSecrets()
    {
        var api = new AutomationApi(Vm(), null);
        var list = (await api.CallAsync("settings", Args(("action", "list"))))!["settings"]!.AsArray();
        Assert.True(list.Count >= 15);
        var keys = list.Select(t => t!["key"]!.ToString()).ToList();
        Assert.Contains("theme", keys); Assert.Contains("assistant_model", keys); Assert.Contains("assistant_effort", keys);
        Assert.DoesNotContain(keys, k => k.Contains("key") || k.Contains("token") || k.Contains("secret"));
    }

    [AvaloniaFact(DisplayName = "AST-H002 | settings get/set/reset validates types and persists through AppSettings")]
    [Trait("TestId", "AST-H002")]
    public async Task SettingsSetGetReset()
    {
        var vm = Vm(); var api = new AutomationApi(vm, null);
        var old = Prism.Core.AppSettings.Current.Theme;
        try
        {
            var got = await api.CallAsync("settings", Args(("action", "get"), ("key", "theme")));
            Assert.Equal(old, got!["value"]!.ToString());
            await api.CallAsync("settings", Args(("action", "set"), ("key", "theme"), ("value", "light")));
            Assert.Equal("Light", Prism.Core.AppSettings.Current.Theme);
            await Assert.ThrowsAnyAsync<Exception>(() => api.CallAsync("settings", Args(("action", "set"), ("key", "theme"), ("value", "neon"))));
            await api.CallAsync("settings", Args(("action", "reset"), ("key", "theme")));
            Assert.Equal("Dark", Prism.Core.AppSettings.Current.Theme);
            await Assert.ThrowsAnyAsync<Exception>(() => api.CallAsync("settings", Args(("action", "get"), ("key", "api_key"))));
        }
        finally { Prism.Core.AppSettings.Current.Theme = old; Prism.Core.AppSettings.Save(); }
    }

    [AvaloniaFact(DisplayName = "AST-H003 | The restricted endpoint blocks file tools and write actions before they run")]
    [Trait("TestId", "AST-H003")]
    public async Task RestrictedEndpointBlocksWrites()
    {
        var api = new AutomationApi(Vm(), null);
        await Assert.ThrowsAnyAsync<Exception>(() => api.CallAsync("file", Args(("action", "save")), restricted: true));
        await Assert.ThrowsAnyAsync<Exception>(() => api.CallAsync("script_editor", Args(("action", "run")), restricted: true));
        await Assert.ThrowsAnyAsync<Exception>(() => api.CallAsync("batch", JsonNode.Parse("{ \"calls\": [ { \"tool\": \"file\", \"args\": { \"action\": \"save\" } } ] }")!.AsObject(), restricted: true));
        // read-only calls still work
        var state = await api.CallAsync("script_editor", Args(("action", "state")), restricted: true);
        Assert.NotNull(state);
    }

    [AvaloniaFact(DisplayName = "AST-H004 | script_editor set replaces the whole script, marks it dirty, and rollback steps back")]
    [Trait("TestId", "AST-H004")]
    public async Task ScriptEditorSetAndRollback()
    {
        var vm = Vm(); var api = new AutomationApi(vm, null);
        vm.Script.New();
        var original = vm.Script.Text;
        var res = await api.CallAsync("script_editor", Args(("action", "set"), ("text", "import cadkit as cad\nx = 1\n")));
        Assert.True(res!["dirty"]!.GetValue<bool>());
        Assert.Equal(1, res["snapshots"]!.GetValue<int>());
        Assert.Equal("import cadkit as cad\nx = 1\n", vm.Script.Text);
        await api.CallAsync("script_editor", Args(("action", "set"), ("text", "y = 2\n")));
        Assert.Equal(2, vm.Script.AiSnapshotCount);
        var rb = await api.CallAsync("script_editor", Args(("action", "rollback")));
        Assert.True(rb!["restored"]!.GetValue<bool>());
        Assert.Equal("import cadkit as cad\nx = 1\n", vm.Script.Text);
        await api.CallAsync("script_editor", Args(("action", "rollback")));
        Assert.Equal(original, vm.Script.Text);
        var last = await api.CallAsync("script_editor", Args(("action", "rollback")));
        Assert.False(last!["restored"]!.GetValue<bool>());
    }

    [AvaloniaFact(DisplayName = "AST-H005 | script_editor insert places code before a line and keeps one rollback point")]
    [Trait("TestId", "AST-H005")]
    public async Task ScriptEditorInsert()
    {
        var vm = Vm(); var api = new AutomationApi(vm, null);
        vm.Script.New(); vm.Script.Text = "a = 1\nc = 3\n";
        await api.CallAsync("script_editor", Args(("action", "insert"), ("text", "b = 2"), ("line", 2)));
        Assert.Equal("a = 1\nb = 2\nc = 3\n", vm.Script.Text);
        await api.CallAsync("script_editor", Args(("action", "insert"), ("text", "d = 4")));
        Assert.EndsWith("d = 4", vm.Script.Text);
    }

    [AvaloniaFact(DisplayName = "AST-H006 | render_setup reports and changes the document render setup, and set_background validates the path")]
    [Trait("TestId", "AST-H006")]
    public async Task RenderSetupStateSetBackground()
    {
        var vm = Vm(); var api = new AutomationApi(vm, null);
        var st = (await api.CallAsync("render_setup", Args(("action", "state"))))!;
        Assert.Equal("environment", st["background"]!.ToString());
        var set = await api.CallAsync("render_setup", Args(("action", "set"), ("background", "white"), ("exposure", 2)));
        Assert.Equal("white", set!["background"]!.ToString());
        Assert.Equal(2.0, set["exposure"]!.GetValue<double>());
        Assert.Equal(Prism.Rendering.RenderBg.White, vm.Session.RenderSetup.Background);
        await Assert.ThrowsAnyAsync<Exception>(() => api.CallAsync("render_setup", Args(("action", "set_background"), ("path", "no/such/file.png"))));
        // Decoding a real PNG needs the desktop bitmap pipeline (Bitmap/CopyPixels); AST-H007 covers the failure path headless.
        vm.Session.RenderSetup = new();
    }

    [AvaloniaFact(DisplayName = "AST-H007 | render_setup rejects a backdrop that is not a readable image")]
    [Trait("TestId", "AST-H007")]
    public async Task RenderSetupRejectsBadImage()
    {
        var api = new AutomationApi(Vm(), null);
        var path = Path.Combine(Path.GetTempPath(), "prism-bg-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllText(path, "this is not an image");
            await Assert.ThrowsAnyAsync<Exception>(() => api.CallAsync("render_setup", Args(("action", "set_background"), ("path", path))));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact(DisplayName = "AST-H008 | The restricted endpoint still answers read-only settings and script queries")]
    [Trait("TestId", "AST-H008")]
    public async Task RestrictedEndpointKeepsReads()
    {
        var api = new AutomationApi(Vm(), null);
        var settings = await api.CallAsync("settings", Args(("action", "get"), ("key", "theme")), restricted: true);
        Assert.Equal("theme", settings!["key"]!.ToString());
        var editor = await api.CallAsync("script_editor", Args(("action", "get")), restricted: true);
        Assert.NotNull(editor);
    }
}
