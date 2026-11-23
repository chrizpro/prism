using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Automation;

/// <summary>
/// The move_gizmo and shortcut automation tools (owner request 2026-11-23: let the agent drive the Move gizmo, its pivot and operation, the way the
/// panel and the D key do). The behavior they drive is GZM-H001 to GZM-H011.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/MCP.md">Automation tools.</seealso>
[Trait("Feature", "Automation")]
[Trait("Layer", "Headless")]
public class MoveGizmoApiTests
{
    static JsonObject Args(object value) => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(value))!.AsObject();

    [AvaloniaFact(DisplayName = "API-H013 | move_gizmo grabs a bar, places the pivot on its corner, turns it -90° about Y to stand it up, and scales it")]
    [Trait("TestId", "API-H013")]
    public async Task DrivesTheMoveGizmo()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var api = new AutomationApi(vm, null);
        async Task<JsonObject> Call(object args) => (await api.CallAsync("move_gizmo", Args(args)))!.AsObject();
        var bar = CreateBoxBody(s, Vec3.Zero, 100, 10, 10);
        s.SelectItems([SelItem.Of(bar)]);

        var started = await Call(new { action = "start" });
        Assert.True(started["moveMode"]!.GetValue<bool>());
        Assert.Single(started["held"]!.AsArray());
        Assert.Equal("[50,5,5]", started["origin"]!.ToJsonString());

        Assert.True((await Call(new { action = "pivot" }))["pivotMode"]!.GetValue<bool>());
        await Call(new { action = "set", origin = new[] { 0, 0, 0 } });
        AssertClose(0, s.Find(bar.Id)!.Shape.Props().Min.X, 1e-9, "the bar stays while the pivot moves");
        Assert.False((await Call(new { action = "pivot" }))["pivotMode"]!.GetValue<bool>());

        var turned = await Call(new { action = "set", rotation = new[] { 0, -90, 0 } });
        Assert.Equal("move", turned["operation"]!.GetValue<string>());
        var p = s.Find(bar.Id)!.Shape.Props();
        AssertClose(100, p.Max.Z, 1e-4, "stood up to 100 mm");
        AssertClose(-10, p.Min.X, 1e-4, "on the pivot corner");

        var op = await Call(new { action = "operation", operation = "scale" });
        Assert.Equal("scale", op["operation"]!.GetValue<string>());
        await Call(new { action = "set", scale = new[] { 1, 1, 0.5 } });       // Z of the turned gizmo runs along the bar
        AssertClose(10000 * 0.5, s.Find(bar.Id)!.Shape.Props().Volume, 1e-3, "half as long (mm³)");

        Assert.Empty((await Call(new { action = "release" }))["held"]!.AsArray());
        Assert.False((await Call(new { action = "done" }))["visible"]!.GetValue<bool>());
    }

    [AvaloniaFact(DisplayName = "API-H014 | shortcut lists every action with its key and refuses an unknown id")]
    [Trait("TestId", "API-H014")]
    public async Task ListsShortcuts()
    {
        var api = new AutomationApi(new MainViewModel(), null);
        var list = (await api.CallAsync("shortcut", new JsonObject()))!["actions"]!.AsArray();
        Assert.Contains(list, a => a!["id"]!.GetValue<string>() == "gizmo.pivot" && a["key"]!.GetValue<string>() == "D");
        Assert.Contains(list, a => a!["id"]!.GetValue<string>() == "edit.repeat");
        await Assert.ThrowsAnyAsync<Exception>(() => api.CallAsync("shortcut", Args(new { id = "no.such.action" })));
    }
}
