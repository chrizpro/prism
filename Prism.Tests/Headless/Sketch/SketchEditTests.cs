using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using GlViewport = Prism.Rendering.GlViewport;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/README.md">Sketch editing, constraints and dimensions.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SketchEditTests
{
    [AvaloniaTheory(DisplayName = "SKT-H010 | Dimension mode determines the measured component")]
    [InlineData(DimMode.Smart, CType.LineLength, 50)]
    [InlineData(DimMode.Horizontal, CType.HDistance, 30)]
    [InlineData(DimMode.Vertical, CType.VDistance, 40)]
    [Trait("TestId", "SKT-H010")]
    public void DimensionModeMeasuresSelectedLine(DimMode mode, CType type, int length)
    {
        var (session, sketch, _) = LineSketch();
        var tool = new DimensionTool(mode);
        sketch.SetTool(session, tool);
        Click(session, tool, 35, 40);
        tool.PanelSet(session, "apply", null);
        var dimension = Assert.Single(sketch.Model.Constraints, item => !item.Hidden);
        Assert.Equal(type, dimension.Type);
        AssertNear(length, Math.Abs(dimension.Value), "Dimension value (mm)");
        Assert.True(sketch.Result.Converged);
    }

    [AvaloniaTheory(DisplayName = "SKT-H011 | Axis constraints solve an oblique line")]
    [InlineData(CType.Horizontal)]
    [InlineData(CType.Vertical)]
    [Trait("TestId", "SKT-H011")]
    public void AxisConstraintAlignsLine(CType type)
    {
        var (session, sketch, id) = LineSketch();
        var tool = new ConstraintTool(type);
        sketch.SetTool(session, tool);
        Click(session, tool, 35, 40);
        Assert.Equal(type, Assert.Single(sketch.Model.Constraints, item => !item.Hidden).Type);
        var line = sketch.Model.E(id);
        var a = sketch.Model.P(line.A);
        var b = sketch.Model.P(line.B);
        AssertNear(0, type == CType.Horizontal ? a.Y - b.Y : a.X - b.X, "Perpendicular displacement (mm)");
        Assert.True(sketch.Result.Converged);
    }

    [AvaloniaFact(DisplayName = "SKT-H012 | Offset creates a parallel copy 3 mm from the source")]
    [Trait("TestId", "SKT-H012")]
    public void OffsetCopiesWithoutMovingSource()
    {
        var (session, sketch, id) = LineSketch();
        sketch.SelEntities.Add(id);
        var tool = new OffsetTool();
        sketch.SetTool(session, tool);
        tool.Key(session, "Enter");
        tool.PanelSet(session, "dist", 3d);
        tool.PanelSet(session, "apply", null);
        var model = sketch.Model;
        var original = model.E(id);
        var copy = Assert.Single(model.Entities, entity => entity.Id != id && entity.Type == EntType.Line);
        var a = model.P(original.A);
        var b = model.P(original.B);
        var c = model.P(copy.A);
        var d = model.P(copy.B);
        AssertNear(20, a.X, "Original start U (mm)");
        AssertNear(20, a.Y, "Original start V (mm)");
        var direction = new Vec3(b.X - a.X, b.Y - a.Y, 0).Normalized();
        AssertNear(3, Vec3.Cross(direction, new Vec3(c.X - a.X, c.Y - a.Y, 0)).Length, "Offset (mm)");
        AssertNear(0, Vec3.Cross(direction, new Vec3(d.X - c.X, d.Y - c.Y, 0)).Length, "Parallel deviation");
    }

    [AvaloniaFact(DisplayName = "SKT-H013 | Paint select picks the curves the stroke crosses")]
    [Trait("TestId", "SKT-H013")]
    public void PaintSelectCrossesCurves()
    {
        var (session, sketch, id) = LineSketch();
        var tool = new SelectTool();
        tool.UseMethod(2);      // paint — UseMethod keeps the choice off the shared Remembered defaults
        sketch.SetTool(session, tool);
        // Stroke from above the line's start across it to below its end: it crosses but encloses nothing.
        var a = session.Camera.Project(new Vec3(30, 20, 0))!.Value;
        var b = session.Camera.Project(new Vec3(40, 70, 0))!.Value;
        Stroke(session, tool, a, b);
        Assert.Contains(id, sketch.SelEntities);
    }

    [AvaloniaFact(DisplayName = "SKT-H014 | Select offers box, lasso and paint; the edit tool shares them")]
    [Trait("TestId", "SKT-H014")]
    public void SelectMethodOptions()
    {
        var select = new SelectTool();
        Assert.Equal(["Box", "Lasso", "Paint"], select.Options.Select(o => o.Label).ToArray());
        var edit = new SketchGeometryEditTool();
        Assert.Equal(3, edit.Options.Count);
        edit.SetOption(new CadSession(), 2);
        Assert.Equal(2, edit.OptionIndex);
        edit.SetOption(new CadSession(), 0);
    }

    [AvaloniaFact(DisplayName = "SKT-H015 | The sketch Select group is the same as every other tab's, and works in the sketch")]
    [Trait("TestId", "SKT-H015")]
    public void SketchSelectRibbon()
    {
        var vm = new MainViewModel();
        static string[] Layout(RibbonTab tab) => tab.Groups.Single(g => g.Title == "Select").Items.OfType<RibbonButton>()
            .SelectMany(b => new[] { b.Label }.Concat(b.Menu?.Select(m => "  " + m.Label) ?? [])).ToArray();
        var sketchTab = vm.Tabs.Single(t => t.Title == "Sketch");
        Assert.Equal(Layout(vm.Tabs.Single(t => t.Title == "Solid")), Layout(sketchTab));
        Assert.DoesNotContain(sketchTab.Groups.Single(g => g.Title == "Modify").Items.OfType<RibbonButton>(), b => b.Label == "Edit");   // Select edits geometry directly

        vm.Session.EnterSketch(Plane3.XY);
        var buttons = sketchTab.Buttons.Where(b => b.Menu == null).ToList();   // the variants, not the split buttons that show one of them
        foreach (var b in buttons) b.Refresh();
        Assert.True(buttons.Single(b => b.Label == "Object").IsEnabled);
        Assert.False(buttons.Single(b => b.Label == "Face").IsEnabled);          // body pick levels do not apply to sketch curves
        buttons.Single(b => b.Label == "Lasso").Invoke();
        Assert.IsType<SelectTool>(vm.Session.Sketch!.Tool);
        Assert.Equal(1, vm.Session.Sketch.Tool!.OptionIndex);                    // the sketch Select tool now drags a lasso
        // The panel does not repeat the method: Select's overview has no "Sketch tool" section.
        Assert.DoesNotContain(vm.ToolSections, s => s.Title == "Sketch tool");
    }

    [AvaloniaFact(DisplayName = "SKT-H047 | Sketch exposes Spline, Trim and Offset without opening a dropdown")]
    [Trait("TestId", "SKT-H047")]
    public void FrequentlyUsedSketchToolsAreDirectButtons()
    {
        var vm = new MainViewModel();
        var tab = vm.Tabs.Single(tab => tab.Title == "Sketch");
        RibbonButton[] Buttons(string group) => tab.Groups.Single(item => item.Title == group).Items.OfType<RibbonButton>().ToArray();
        static IEnumerable<string> Labels(IEnumerable<RibbonButton> buttons) => buttons.Select(button => button.Label.Replace('\n', ' '));
        var draw = Buttons("Draw");
        var modify = Buttons("Modify");
        Assert.Equal(new[] { "Line", "Rectangle", "Ellipse", "Arc", "Spline", "More drawing tools" }, Labels(draw));
        Assert.Equal(new[] { "Polygon", "Slot", "Text" }, Labels(draw[^1].Menu!));
        Assert.Equal(new[] { "Trim", "Offset", "Transform", "More edits" }, Labels(modify));
        Assert.Equal(new[] { "Insert Point", "Extend", "Fillet", "Chamfer", "Mirror", "Pattern" }, Labels(modify[^1].Menu!));
        Assert.False(draw[^1].IsSplit);
        Assert.False(modify[^1].IsSplit);
        Assert.All(Buttons("Constrain"), button => Assert.False(button.HasMenu));

        vm.Session.EnterSketch(Plane3.XY);
        var sketch = vm.Session.Sketch!;
        modify[0].Invoke();
        Assert.IsType<TrimTool>(sketch.Tool);
        modify[1].Invoke();
        Assert.IsType<OffsetTool>(sketch.Tool);
        sketch.Set3DEnabled(vm.Session, true);
        draw[4].Invoke();
        Assert.IsType<SplineTool>(sketch.Tool);
        draw[4].Refresh();
        Assert.True(draw[4].IsChecked);
    }

    [AvaloniaFact(DisplayName = "SKT-H016 | Sketch grid: perspective keeps the entry plane, ortho shows the plane it faces")]
    [Trait("TestId", "SKT-H016")]
    public void SketchGridFollowsView()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: true);
        var sketch = session.Sketch!;
        session.Camera.SetView(-35, 28);
        session.Camera.Orthographic = false;
        Assert.True(Math.Abs(Vec3.Dot(GlViewport.DisplayGridPlane(session).Normal, sketch.GridPlane.Normal)) > 0.99, "Perspective keeps the entry plane's grid");
        session.Camera.SetView(0, 0);      // ortho preset looking at the front
        session.Camera.Orthographic = true;
        Assert.True(Math.Abs(Vec3.Dot(GlViewport.DisplayGridPlane(session).Normal, Vec3.UnitZ)) < 0.01, "Front ortho does not draw the edge-on XY grid");
        session.Camera.SetView(0, 90);     // ortho top
        Assert.True(Math.Abs(Vec3.Dot(GlViewport.DisplayGridPlane(session).Normal, Vec3.UnitZ)) > 0.99, "Top ortho faces XY");
    }

    /// <summary>Draw-in-space: a click on a curve drawn on another (inactive) plane switches to its plane and picks it, so it can be edited.</summary>
    [AvaloniaFact]
    [Trait("Feature", "SketchSelection"), Trait("Layer", "Headless"), Trait("TestId", "SKT-H017")]
    public void ClickCurveOnInactivePlaneSwitchesSectionAndSelects()
    {
        var session = new CadSession();
        var root = new SketchModel();
        int line = root.AddLine(root.AddPoint(20, 20), root.AddPoint(50, 60));
        root.Sections.Add(new SketchSection { Name = "Plane 2", Plane = Plane3.XZ });
        session.EnterSketch(Plane3.XY, root, alignView: true);
        session.Ortho = true;
        var sketch = session.Sketch!;
        sketch.Set3DEnabled(session, true);       // draw in space: the reported mode — flat drawing already switched planes on click
        sketch.SelectPlane(session, 1);
        Assert.Equal(1, sketch.ActivePlane);
        var tool = new SelectTool();
        sketch.SetTool(session, tool);
        var px = session.Camera.Project(new Vec3(35, 40, 0))!.Value;   // midpoint of the line on the inactive plane
        tool.PointerDown(session, new(px.x, px.y, true, false, false, false));
        Assert.Equal(0, sketch.ActivePlane);
        Assert.Contains(line, sketch.SelEntities);
    }

    /// <summary>Geometry editing routes a click on an inactive-plane curve through the bound-target path: the plane switches and the curve binds.</summary>
    [AvaloniaFact]
    [Trait("Feature", "SketchSelection"), Trait("Layer", "Headless"), Trait("TestId", "SKT-H018")]
    public void EditModeClickOnInactivePlaneCurveRetargetsSelection()
    {
        var session = new CadSession();
        var root = new SketchModel();
        int line = root.AddLine(root.AddPoint(20, 20), root.AddPoint(50, 60));
        root.Sections.Add(new SketchSection { Name = "Plane 2", Plane = Plane3.XZ });
        session.EnterSketch(Plane3.XY, root, alignView: true);
        session.Ortho = true;
        var sketch = session.Sketch!;
        sketch.Set3DEnabled(session, true);
        sketch.SelectPlane(session, 1);
        sketch.SetTool(session, new SketchGeometryEditTool());
        Assert.True(sketch.GeometryEditing);
        var px = session.Camera.Project(new Vec3(35, 40, 0))!.Value;
        Assert.True(session.PointerDown(new(px.x, px.y, true, false, false, false)));
        Assert.Equal(0, sketch.ActivePlane);
        Assert.Contains(line, sketch.SelEntities);
    }

    static (CadSession Session, SketchContext Sketch, int Line) LineSketch()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: true);
        session.Ortho = true;
        var sketch = session.Sketch!;
        var model = sketch.Model;
        int line = model.AddLine(model.AddPoint(20, 20), model.AddPoint(50, 60));
        sketch.Solve(session);
        return (session, sketch, line);
    }

    static void Stroke(CadSession session, SketchTool tool, (double x, double y, double depth) from, (double x, double y, double depth) to)
    {
        tool.PointerDown(session, new(from.x, from.y, true, false, false, false));
        tool.PointerMove(session, new((from.x + to.x) / 2, (from.y + to.y) / 2, true, false, false, false));
        tool.PointerMove(session, new(to.x, to.y, true, false, false, false));
        tool.PointerUp(session, new(to.x, to.y, true, false, false, false));
    }

    static void Click(CadSession session, SketchTool tool, double u, double v)
    {
        var pixel = session.Camera.Project(new Vec3(u, v, 0))!.Value;
        tool.PointerDown(session, new(pixel.x, pixel.y, true, false, false, false));
    }
}
