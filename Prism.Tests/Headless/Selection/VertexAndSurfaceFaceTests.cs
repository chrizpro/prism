using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Selection;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/selection/README.md#direct-edits-and-the-vertex-level-on-surfaces">Vertex level and direct edits on surface faces.</seealso>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public class VertexAndSurfaceFaceTests
{
    /// <summary>A flat 10 x 10 sheet cut into two faces along x = 5.</summary>
    static SceneObject SplitSheet(CadSession s)
    {
        using var a = Occ.Polyline([Vec3.Zero, new(5, 0, 0), new(5, 10, 0), new(0, 10, 0)], true);
        using var b = Occ.Polyline([new(5, 0, 0), new(10, 0, 0), new(10, 10, 0), new(5, 10, 0)], true);
        return s.AddObject(Occ.Sew([Occ.Profile([a], true), Occ.Profile([b], true)], 0.01, false), "Sheet", ObjKind.Surface);
    }

    static int FaceLeftOf(SceneObject o, double x)
    {
        for (int i = 0; i < o.Shape.Count(SubKind.Face); i++) { using var f = o.Shape.Sub(SubKind.Face, i); if (f.Props().Center.X < x) return i; }
        throw new InvalidOperationException("no face on that side");
    }

    [AvaloniaFact(DisplayName = "SEL-H016 | The Vertex level picks a corner point of a body and reports where it is")]
    [Trait("TestId", "SEL-H016")]
    public void VertexLevelPicksCorner()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        var corner = new Vec3(box.Mesh.Max.X, box.Mesh.Min.Y, box.Mesh.Max.Z);
        Assert.Equal(8, s.VertexPoints(box).Count);
        var px = s.Camera.Project(corner)!.Value;

        Assert.NotEqual(SelKind.Vertex, s.Pick(px.x, px.y, SelFilter.Face | SelFilter.Edge)!.Value.Item.Kind);   // other levels are unchanged
        s.Mode = SelMode.Vertex;
        var hit = s.Pick(px.x, px.y, s.ActiveFilter);
        Assert.Equal(SelKind.Vertex, hit!.Value.Item.Kind);
        Assert.True((hit.Value.Point - corner).Length < 1e-6);
        Assert.StartsWith("Vertex ", s.Describe(hit.Value.Item));
        Assert.True((s.VertexPoint(hit.Value.Item)!.Value - corner).Length < 1e-6);
    }

    [AvaloniaFact(DisplayName = "SEL-H017 | Delete with only a corner point selected removes nothing")]
    [Trait("TestId", "SEL-H017")]
    public void DeleteLeavesBodyWhenOnlyVertexSelected()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        s.SetSelection(new SelItem(box.Id, SelKind.Vertex, 0), false);
        s.DeleteSelection();
        Assert.NotNull(s.Find(box.Id));
        Assert.True(s.StatusIsError);
    }

    [AvaloniaFact(DisplayName = "SEL-H018 | Move Face carries a surface face and joins it back with a wall along the step")]
    [Trait("TestId", "SEL-H018")]
    public void MoveFaceOnSurface()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var sheet = SplitSheet(s);
        var cmd = new MoveFaceCommand(); s.StartCommand(cmd);
        cmd.ConfigureInputs(() =>
        {
            cmd.Slots[0].TryAdd(new SelItem(sheet.Id, SelKind.Face, FaceLeftOf(sheet, 5)));
            cmd.Params.OfType<NumberParam>().Single(p => p.Label == "Move Z").Value = 3;
        });
        var output = cmd.TryBuild();
        Assert.True(output != null, cmd.Error);
        using var result = Assert.Single(output.Replace).New;
        Assert.True(result.IsValid);
        Assert.Equal(3, result.Count(SubKind.Face));                     // two panels and one wall; no walls on the open border
        Assert.Equal(100 + 10 * 3, result.Props().Area, 3);
        Assert.Equal(3, result.Props().Max.Z, 6);
        Assert.Equal(0, result.Count(SubKind.Solid));
    }

    [AvaloniaFact(DisplayName = "SEL-H019 | Offset Face moves a surface face along its normal with a wall along the step")]
    [Trait("TestId", "SEL-H019")]
    public void OffsetFaceOnSurface()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var sheet = SplitSheet(s);
        var cmd = new OffsetFaceCommand(); s.StartCommand(cmd);
        cmd.ConfigureInputs(() =>
        {
            cmd.Slots[0].TryAdd(new SelItem(sheet.Id, SelKind.Face, FaceLeftOf(sheet, 5)));
            cmd.Params.OfType<NumberParam>().Single().Value = 2;
        });
        var output = cmd.TryBuild();
        Assert.True(output != null, cmd.Error);
        using var result = Assert.Single(output.Replace).New;
        Assert.True(result.IsValid);
        Assert.Equal(3, result.Count(SubKind.Face));
        Assert.Equal(100 + 10 * 2, result.Props().Area, 3);
        Assert.Equal(2, result.Props().Size.Z, 6);
    }

    [AvaloniaTheory(DisplayName = "SEL-H020 | Curve tools sit in one Curves dropdown on every modeling tab")]
    [InlineData("Home")]
    [InlineData("Solid")]
    [InlineData("Surface")]
    [InlineData("NURBS")]
    [InlineData("Freeform")]
    [Trait("TestId", "SEL-H020")]
    public void CurvesAreOneDropdown(string tab)
    {
        var vm = new MainViewModel();
        var ribbon = vm.Tabs.Single(t => t.Title == tab);
        Assert.DoesNotContain(ribbon.Groups, g => g.Title == "Edit curves");
        var curves = Assert.Single(ribbon.Groups.Single(g => g.Title == "Curves").Items.OfType<RibbonButton>());
        Assert.Equal("Curves", curves.Label);
        var labels = curves.Menu!.Select(b => b.Label.Replace('\n', ' ')).ToList();
        foreach (var tool in new[] { "Curve from Data", "Intersection Curve", "Extract Edges", "Rebuild Curve" })
            Assert.Contains(tool, labels);
        // owner direction 2026-11-14: these seven are [Legacy] and no longer offered (see CRV-H034)
        foreach (var tool in new[] { "Curve", "Blend Curve", "Join", "Explode", "Trim Curve", "Extend Curve", "Offset Curve" })
            Assert.DoesNotContain(tool, labels);
        Assert.All(curves.Menu!, b => Assert.Null(b.Menu));              // one level deep
    }

    [AvaloniaFact(DisplayName = "SEL-H021 | Dragging a box in Vertex mode picks the corner points inside it")]
    [Trait("TestId", "SEL-H021")]
    public void VertexDragSelect()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        s.Mode = SelMode.Vertex; s.XRay = true;
        var corner = new Vec3(box.Mesh.Max.X, box.Mesh.Min.Y, box.Mesh.Max.Z);
        var px = s.Camera.Project(corner)!.Value;
        var region = new RegionSelect.Region { IsBox = true };
        region.Points.Add((px.x - 6, px.y - 6)); region.Points.Add((px.x + 6, px.y + 6));
        s.ApplyRegion(region, new PointerInfo(0, 0, true, false, false, false));
        var picked = Assert.Single(s.Selection);
        Assert.Equal(SelKind.Vertex, picked.Kind);
        Assert.True((s.VertexPoint(picked)!.Value - corner).Length < 1e-6);
    }

    [AvaloniaFact(DisplayName = "SEL-H022 | Merge Faces joins the two faces of a split surface back into one")]
    [Trait("TestId", "SEL-H022")]
    public void MergeSurfaceFaces()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var sheet = SplitSheet(s);
        var cmd = new MergeFacesCommand(); s.StartCommand(cmd);
        cmd.ConfigureInputs(() =>
        {
            cmd.Slots[0].TryAdd(new SelItem(sheet.Id, SelKind.Face, 0));
            cmd.Slots[0].TryAdd(new SelItem(sheet.Id, SelKind.Face, 1));
        });
        var output = cmd.TryBuild();
        Assert.True(output != null, cmd.Error);
        using var result = Assert.Single(output.Replace).New;
        Assert.Equal(1, result.Count(SubKind.Face));
        Assert.Equal(100, result.Props().Area, 3);
    }

    [AvaloniaFact(DisplayName = "SEL-H023 | Push / Pull on a curved surface face grows a solid slab along its normals")]
    [Trait("TestId", "SEL-H023")]
    public void PushPullCurvedSurfaceFace()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        using var ball = Occ.Sphere(10);
        var dome = s.AddObject(ball.Sub(SubKind.Face, 0), "Dome", ObjKind.Surface);
        var cmd = new PushPullCommand(); s.StartCommand(cmd);
        cmd.ConfigureInputs(() =>
        {
            cmd.Slots[0].TryAdd(new SelItem(dome.Id, SelKind.Face, 0));
            cmd.Params.OfType<NumberParam>().First().Value = 2;
        });
        var output = cmd.TryBuild();
        Assert.True(output != null, cmd.Error);
        var slab = Assert.Single(output.Add).Shape!;
        Assert.True(slab.Count(SubKind.Solid) >= 1);
        Assert.True(slab.Props().Volume > 100);
    }

    [AvaloniaFact(DisplayName = "SEL-H024 | Ctrl+A selects everything at the current pick level")]
    [Trait("TestId", "SEL-H024")]
    public void SelectAllAtCurrentLevel()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var a = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 10, 10, 10);
        var b = Support.ModelingScenario.CreateBoxBody(s, new Vec3(30, 0, 0), 10, 10, 10);
        Assert.Contains(Prism.Core.Hotkeys.Actions, h => h.Id == "edit.selectall" && h.Default == "Ctrl+A");
        s.SelectAll();
        Assert.Equal(2, s.Selection.Count(i => i.Kind == SelKind.Object));
        s.Mode = SelMode.Face; s.SelectAll();
        Assert.Equal(12, s.Selection.Count(i => i.Kind == SelKind.Face));
        s.Mode = SelMode.Edge; s.SelectAll();
        Assert.Equal(24, s.Selection.Count(i => i.Kind == SelKind.Edge));
        s.Mode = SelMode.Vertex; s.SelectAll();
        Assert.Equal(16, s.Selection.Count(i => i.Kind == SelKind.Vertex));
    }
}
