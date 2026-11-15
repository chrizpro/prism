using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Automation;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/MCP.md">Automation measurements and command reports.</seealso>
[Trait("Feature", "Automation")]
[Trait("Layer", "Headless")]
public class CommandReportTests
{
    [AvaloniaFact(DisplayName = "API-H002 | A scoped Interference call never converts an unrelated Freeform cage during startup")]
    [Trait("TestId", "API-H002")]
    public async Task ExplicitAnalysisPicksAreAppliedBeforeFirstPreview()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        async Task<JsonNode> Call(string tool, object args) => (await api.CallAsync(tool, JsonSerializer(args)))!;
        var a = await Call("run_command", new { name = "box", @params = new { Width = 10, Depth = 10, Height = 10 } });
        var b = await Call("run_command", new { name = "box", @params = new Dictionary<string, object> { ["Width"] = 10, ["Depth"] = 10, ["Height"] = 10, ["Position X"] = 40 } });
        var formResult = await Call("run_command", new { name = "form_box", @params = new Dictionary<string, object> { ["Width"] = 10, ["Depth"] = 10, ["Height"] = 10, ["Position X"] = 100, ["Faces X"] = 1, ["Faces Y"] = 1, ["Faces Z"] = 1 } });
        string formName = (string)formResult["created"]![0]!["name"]!;
        var form = vm.Session.Objects.Single(o => o.Name == formName);
        Assert.False(form.HasCachedShape);
        await Call("select", new { items = Array.Empty<string>() });
        var result = await Call("run_command", new { name = "interference", commit = false,
            slots = new Dictionary<string, object> { ["Bodies to check (none = all solids)"] = new[] { (string)a["created"]![0]!["name"]!, (string)b["created"]![0]!["name"]! } } });
        Assert.Contains("No interference between 2 bodies (1 pair checked)", result["activeCommand"]!["results"]!.ToJsonString());
        Assert.False(form.HasCachedShape); // Initial empty picks must not analyze the whole document.
        await Call("command_cancel", new { });
    }

    [AvaloniaFact(DisplayName = "API-H001 | Command reports keep exact measurements across hidden bodies, renames, shape replacement and undo/redo")]
    [Trait("TestId", "API-H001")]
    public async Task ShapeMeasurementsFollowGeometryRatherThanObjectNameOrVisibility()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        async Task<JsonNode> Call(string tool, object args) => (await api.CallAsync(tool, JsonSerializer(args)))!;
        var created = await Call("run_command", new { name = "box", @params = new { Width = 10, Depth = 20, Height = 30 } });
        string name = (string)created["created"]![0]!["name"]!;
        var initial = await Call("object_info", new { @object = name });
        Assert.Equal(6000, (double)initial["volume"]!, 4);
        await Call("object_update", new { @object = name, name = "Hidden coachwork", visible = false });
        await Call("run_command", new { name = "curve_through", points = new[] { new[] { 0, 0, 100 }, new[] { 10, 0, 100 } } });
        var unchanged = await Call("object_info", new { @object = "Hidden coachwork" });
        Assert.Equal(initial["volume"]!.ToJsonString(), unchanged["volume"]!.ToJsonString());
        Assert.False((bool)unchanged["visible"]!);
        var scaled = await Call("run_command", new { name = "scale", slots = new { Objects = "Hidden coachwork" }, @params = new { Factor = 2, Pivot = "World origin" } });
        var changed = Assert.Single(scaled["changed"]!.AsArray());
        Assert.Equal("Hidden coachwork", (string?)changed!["name"]);
        Assert.Equal(48000, (double)changed["volume"]!, 4);
        Assert.Equal(42000, (double)changed["volumeDelta"]!, 4);
        Assert.Equal(6600, (double)changed["areaDelta"]!, 4); // box area 2200, scaled area 8800
        Assert.Equal("[20,40,60]", changed["size"]!.ToJsonString());
        await Call("history", new { action = "undo" });
        Assert.Equal(6000, (double)(await Call("object_info", new { @object = "Hidden coachwork" }))["volume"]!, 4);
        await Call("history", new { action = "redo" });
        Assert.Equal(48000, (double)(await Call("object_info", new { @object = "Hidden coachwork" }))["volume"]!, 4);
        var moved = await Call("run_command", new { name = "move", slots = new { Objects = "Hidden coachwork" }, @params = new Dictionary<string, object> { ["Δ X"] = 5, ["Δ Y"] = 0, ["Δ Z"] = 0 } });
        Assert.Equal(0, (double)moved["changed"]![0]!["volumeDelta"]!, 4);
        Assert.Equal(5, (double)(await Call("object_info", new { @object = "Hidden coachwork" }))["center"]![0]!, 4);
    }

    static JsonObject JsonSerializer(object value) => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(value))!.AsObject();
}
