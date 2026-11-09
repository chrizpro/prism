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
}
