using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Curves;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/MCP.md">Automation API.</seealso>
[Trait("Feature", "Curves")]
[Trait("Layer", "Headless")]
public class CurveApiTests
{
    [AvaloniaFact(DisplayName = "CRV-H030 | A parameter whose label is shared by a number and a text field is set through the one that fits the value")]
    [Trait("TestId", "CRV-H030")]
    public async Task SharedLabelResolvesByValueType()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        // Curve from Data has a "Points" count (a number) and a "Points" table (text)
        await api.CallAsync("run_command", (JsonObject)JsonNode.Parse("{\"name\":\"curve_table\",\"params\":{\"Points from\":\"Table or file\",\"Points\":\"0,0,0; 20,10,5; 40,0,10; 60,-10,15\"}}")!);
        var curve = Assert.Single(vm.Session.Objects, o => o.Kind == Prism.Core.ObjKind.Curve);
        Assert.InRange(curve.Mesh.Max.X - curve.Mesh.Min.X, 59, 61);
        Assert.InRange(curve.Mesh.Max.Z - curve.Mesh.Min.Z, 14, 16);
    }

    [AvaloniaFact(DisplayName = "CRV-H031 | Fill Opening closes the circular end of a tube with its default continuity")]
    [Trait("TestId", "CRV-H031")]
    public async Task FillOpeningClosesACircularEnd()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        async Task<JsonNode> Call(string tool, string json) => (await api.CallAsync(tool, (JsonObject)JsonNode.Parse(json)!))!;
        await Call("sketch_begin", "{\"plane\":\"XY\"}");
        await Call("sketch_add", "{\"entities\":[{\"type\":\"circle\",\"center\":[0,0],\"radius\":10}]}");
        string circle = (string)(await Call("sketch_finish", "{}"))["sketch"]!;
        await Call("run_command", "{\"name\":\"extrude_surface\",\"slots\":{\"Profile\":\"" + circle + "\"},\"params\":{\"Extent\":\"Distance\",\"Distance\":20.0}}");
        var tube = vm.Session.Objects.Single(o => o.Kind == Prism.Core.ObjKind.Surface);
        double before = Prism.Kernel.Occ.Mass(tube.Shape).Area;
        await Call("run_command", "{\"name\":\"fill_opening\",\"slots\":{\"An open edge\":{\"object\":\"" + tube.Name + "\",\"edge\":2}}}");
        double after = vm.Session.Objects.Where(o => o.Kind == Prism.Core.ObjKind.Surface || o.Kind == Prism.Core.ObjKind.Solid).Max(o => Prism.Kernel.Occ.Mass(o.Shape).Area);
        Assert.True(after > before + 250, $"area {before:0} -> {after:0}: the cap (about 314 mm2) should have been added");
    }
}
