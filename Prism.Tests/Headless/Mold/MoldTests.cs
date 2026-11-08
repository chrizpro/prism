using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Mold;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mold/README.md">Mold tab.</seealso>
[Trait("Feature", "Mold")]
[Trait("Layer", "Headless")]
public class MoldTests
{
    static async Task<JsonNode> Call(AutomationApi api, string tool, string json) => (await api.CallAsync(tool, (JsonObject)JsonNode.Parse(json)!))!;

    [AvaloniaTheory(DisplayName = "MOLD-H001 | Create Mold cuts the part out of the block, splits it where chosen and pulls the halves apart")]
    [InlineData(50, 40)]
    [InlineData(25, 10)]
    [Trait("TestId", "MOLD-H001")]
    public async Task BlockSplitsIntoTwoExplodedHalves(double percent, double gap)
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        var s = vm.Session;
        var block = s.AddObject(Occ.Box(100, 60, 40), "Block", ObjKind.Solid);
        var part = s.AddObject(Occ.Translate(Occ.Box(30, 20, 10), new Vec3(10, 0, 15)), "Part", ObjKind.Solid);
        await Call(api, "run_command", "{\"name\":\"mold\",\"slots\":{\"Mold block\":\"Block 1\",\"Part to cast\":\"Part 1\"},\"params\":{\"Split at\":" + percent + ",\"Explode distance\":" + gap + "}}");
        var solids = s.Objects.Where(o => o.Kind == ObjKind.Solid).ToList();
        Assert.Equal(3, solids.Count);
        var halves = solids.Where(o => o.Id != part.Id).OrderBy(o => o.Mesh.Min.Z).ToList();
        double split = 40 * percent / 100;   // the block's Z runs from 0 to 40
        Assert.InRange(halves[0].Mesh.Max.Z, split - gap / 2 - .01, split - gap / 2 + .01);
        Assert.InRange(halves[1].Mesh.Min.Z, split + gap / 2 - .01, split + gap / 2 + .01);
        double total = Occ.Mass(halves[0].Shape).Volume + Occ.Mass(halves[1].Shape).Volume;
        Assert.InRange(total, 100 * 60 * 40 - 30 * 20 * 10 - 1, 100 * 60 * 40 - 30 * 20 * 10 + 1);
        Assert.Equal(part.Mesh.Min.Z, s.Find(part.Id)!.Mesh.Min.Z);
    }

    [AvaloniaFact(DisplayName = "MOLD-H002 | Create Mold refuses a part that is not inside the block and honours a picked parting plane")]
    [Trait("TestId", "MOLD-H002")]
    public async Task OutsidePartIsRefusedAndPlaneSplitsTheBlock()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        var s = vm.Session;
        s.AddObject(Occ.Box(100, 60, 40), "Block", ObjKind.Solid);
        s.AddObject(Occ.Translate(Occ.Box(30, 20, 10), new Vec3(60, 0, 15)), "Outside", ObjKind.Solid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Call(api, "run_command", "{\"name\":\"mold\",\"slots\":{\"Mold block\":\"Block 1\",\"Part to cast\":\"Outside 1\"}}"));
        Assert.Equal(2, s.Objects.Count(o => o.Kind == ObjKind.Solid));
    }
}
