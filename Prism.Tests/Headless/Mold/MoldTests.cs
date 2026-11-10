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
    [AvaloniaFact(DisplayName = "MOLD-H007 | Mold-only Save As keeps two cavity solids and CNC/Resin import them as exact bodies")]
    [Trait("TestId", "MOLD-H007")]
    public async Task MoldOnlyFileRoundTripsAndImports()
    {
        var vm = new MainViewModel();
        var source = vm.Session;
        source.AddObject(Occ.Box(20, 20, 20), "Block", ObjKind.Solid);
        source.AddObject(Occ.Translate(Occ.Box(4, 4, 4), new Vec3(0, 0, 8)), "Part", ObjKind.Solid);
        source.AddObject(Occ.Box(2, 2, 2), "Unrelated", ObjKind.Solid);
        await Call(new AutomationApi(vm, null), "run_command",
            """{"name":"mold","slots":{"Mold block":"Block 1","Part to cast":"Part 1"}}""");
        string stem = Path.Combine(Path.GetTempPath(), "prism-mold-" + Guid.NewGuid());
        string mold = stem + NativeDocumentFile.Mold;
        string full = stem + NativeDocumentFile.Assembly;
        try
        {
            source.Save(full);
            var reopened = new CadSession();
            reopened.Open(full);
            Assert.Equal(4, reopened.Objects.Count);
            Assert.True(reopened.CanExportMold);
            source.AddObject(Occ.Box(1, 1, 1), "Unsaved", ObjKind.Solid);
            vm.PickNativeSaveFile = (_, _, extensions) =>
            {
                Assert.Equal(new[] { NativeDocumentFile.Assembly, ".prism", NativeDocumentFile.Mold }, extensions);
                return Task.FromResult<string?>(mold);
            };
            Assert.False(await vm.SaveDocumentAsync(vm.ActiveDocument!, saveAs: true));
            Assert.True(source.Dirty);
            Assert.Equal(full, source.FilePath);

            reopened.Open(mold);
            Assert.Equal(2, reopened.Objects.Count);
            Assert.All(reopened.Objects, o => Assert.True(o.IsMoldHalf));
            Assert.Equal(7936, reopened.Objects.Sum(o => Occ.Mass(o.Shape).Volume), 5);
            Assert.False(reopened.CanUndo);

            var target = new MainViewModel { PickOpenFile = (_, patterns) =>
            {
                Assert.Equal(new[] { "*.prism_mld" }, patterns);
                return Task.FromResult<string?>(mold);
            } };
            foreach (var title in new[] { "Subtractive", "3D Resin", "3D FDM" })
                Assert.Equal(title != "3D FDM", target.Tabs.Single(t => t.Title == title).Buttons.Any(b => b.Label == "Import mold"));
            var existing = target.Session.AddObject(Occ.Box(1, 1, 1), "Existing", ObjKind.Solid);
            await target.ImportManufacturingMold();
            Assert.Equal(3, target.Session.Objects.Count);
            Assert.Same(existing, target.Session.Find(existing.Id));
            Assert.All(target.Session.Objects.Where(o => o.IsMoldHalf), imported =>
                Assert.DoesNotContain(reopened.Objects, original => original.Id == imported.Id));
        }
        finally
        {
            File.Delete(mold);
            File.Delete(full);
        }
    }

    [AvaloniaFact(DisplayName = "MOLD-H006 | Import from file offers native Prism files and copies visible bodies as references")]
    [Trait("TestId", "MOLD-H006")]
    public async Task MoldImportsNativePartAsReference()
    {
        var source = new CadSession();
        var visible = source.AddObject(Occ.Box(8, 8, 8), "Cast part", ObjKind.Solid);
        var hidden = source.AddObject(Occ.Box(2, 2, 2), "Hidden part", ObjKind.Solid);
        source.Update(hidden.Id, obj => obj.WithVisible(false), checkpoint: false);
        string path = Path.Combine(Path.GetTempPath(), "prism-mold-" + Guid.NewGuid() + NativeDocumentFile.Part);
        try
        {
            source.Save(path);
            var vm = new MainViewModel
            {
                PickOpenFile = (_, patterns) =>
                {
                    Assert.Contains("*.prism", patterns);
                    Assert.Contains("*.prism_prt", patterns);
                    return Task.FromResult<string?>(path);
                },
            };
            await vm.ImportMoldPart();
            var imported = Assert.Single(vm.Session.Objects);
            Assert.True(imported.IsReference);
            Assert.NotEqual(visible.Id, imported.Id);
            Assert.Equal(512, Occ.Mass(imported.Shape).Volume, 6);
            Assert.Null(vm.Session.FilePath);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

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

    [AvaloniaFact(DisplayName = "MOLD-H005 | A part brought in from another open tab is a reference part that the saved file leaves out")]
    [Trait("TestId", "MOLD-H005")]
    public async Task PartFromOpenTabIsAReference()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        var other = new Prism.Core.CadSession();
        other.AddObject(Occ.Box(8, 8, 8), "Gear", ObjKind.Solid);
        Prism.Commands.MoldSources.Documents = () => [("Gear tab", other)];
        await Call(api, "run_command", "{\"name\":\"mold_part_from_tab\"}");
        var brought = Assert.Single(vm.Session.Objects);
        Assert.True(brought.IsReference);
        Assert.StartsWith("Gear", brought.Name);
        string path = Path.Combine(Path.GetTempPath(), "prism-tab-" + Guid.NewGuid().ToString("N") + ".prism");
        try { vm.Session.Save(path); var again = new Prism.Core.CadSession(); again.Open(path); Assert.Empty(again.Objects); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
