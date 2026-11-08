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

    [AvaloniaFact(DisplayName = "MOLD-H003 | Create Mold makes its own block, grows the cavity by shrinkage and offset, recolours the part and makes it see-through")]
    [Trait("TestId", "MOLD-H003")]
    public async Task AutoBlockShrinkageOffsetAndPartLook()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        var s = vm.Session;
        var part = s.AddObject(Occ.Translate(Occ.Box(20, 20, 20), new Vec3(0, 0, 10)), "Part", ObjKind.Solid);
        await Call(api, "run_command", "{\"name\":\"mold\",\"slots\":{\"Part to cast\":\"Part 1\"},\"params\":{\"Block margin (when no block is picked)\":15.0,\"Shrinkage allowance\":2.0,\"Cavity offset (clearance)\":1.0,\"Explode distance\":30.0,\"Part opacity while exploded\":40.0}}");
        var halves = s.Objects.Where(o => o.Kind == ObjKind.Solid && o.Id != part.Id).OrderBy(o => o.Mesh.Min.Z).ToList();
        Assert.Equal(2, halves.Count);
        double side = 20 + 2 * 1.2 + 2 * 15;   // part, the cavity's growth (offset 1 mm plus 2 percent shrinkage) and the margin on each side
        double cavity = Occ.Mass(Occ.Offset(Occ.Scale(Occ.Translate(Occ.Box(20, 20, 20), new Vec3(0, 0, 10)), new Vec3(0, 0, 20), 1.02), 1)).Volume;
        double total = Occ.Mass(halves[0].Shape).Volume + Occ.Mass(halves[1].Shape).Volume;
        Assert.InRange(total, (side * side * side - cavity) * .99, (side * side * side - cavity) * 1.01);
        var styled = s.Find(part.Id)!;
        Assert.Equal(0xFFE8873Au, styled.Color);
        Assert.InRange(styled.Look.Surface!.Transmission, .5, .6);   // 40 percent opaque
        Assert.False(styled.IsReference);
    }

    [AvaloniaFact(DisplayName = "MOLD-H004 | A reference part is shown in the session but is not saved with the file")]
    [Trait("TestId", "MOLD-H004")]
    public void ReferencePartIsNotSaved()
    {
        var s = new Prism.Core.CadSession();
        s.AddObject(Occ.Box(10, 10, 10), "Casing", ObjKind.Solid);
        var part = s.AddObject(Occ.Translate(Occ.Box(4, 4, 4), new Vec3(0, 0, 3)), "Imported part", ObjKind.Solid);
        s.Update(part.Id, x => x.WithReference(true), checkpoint: false);
        string path = Path.Combine(Path.GetTempPath(), "prism-ref-" + Guid.NewGuid().ToString("N") + ".prism");
        try
        {
            s.Save(path);
            var reopened = new Prism.Core.CadSession(); reopened.Open(path);
            Assert.Equal(2, s.Objects.Count);
            Assert.StartsWith("Casing", Assert.Single(reopened.Objects).Name);
            Assert.Throws<InvalidOperationException>(() => s.GenerateDrawingView(s.Drawings.Sheets.FirstOrDefault() ?? throw new InvalidOperationException("no sheet"), "front", [part.Id]));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
