using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sketch;

/// <summary>
/// On the NURBS tab Draw opens the one shared 3D sketch, so any curve can be edited without switching sketches, while every curve stays independent:
/// features bind the curves they were made from and a whole-sketch pick takes only the curves no feature uses yet (owner request 2026-11-15).
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/README.md">Sketches.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class NurbsSketchTests
{
    static void Visit(MainViewModel vm, double x)
    {
        vm.SelectedTab = vm.Tabs.Single(t => t.Title == "NURBS");
        vm.StartFluidSketch();
        var sketch = vm.Session.Sketch!;
        sketch.Edit(vm.Session, "Add rectangle", () =>
        {
            var m = sketch.Model;
            int a = m.AddPoint(x, 0), b = m.AddPoint(x + 10, 0), c = m.AddPoint(x + 10, 10), d = m.AddPoint(x, 10);
            m.AddLine(a, b); m.AddLine(b, c); m.AddLine(c, d); m.AddLine(d, a);
        });
        vm.Session.ExitSketch(true);
    }

    static SceneObject ExtrudeWholeSketch(MainViewModel vm, SceneObject sketch, double distance)
    {
        var extrude = Begin(vm.Session, () => new ExtrudeCommand(surface: true));
        Assert.True(extrude.Pick(SelItem.Of(sketch), additive: false));
        SetNumber(extrude, "Distance", distance);
        vm.Session.CommitCommand();
        return vm.Session.Objects.Where(o => o.Kind == ObjKind.Surface).OrderBy(o => o.Id == default ? 0 : 1).Last();
    }

    [AvaloniaFact(DisplayName = "SKT-H056 | NURBS Draw reopens one shared sketch; extruding the second rectangle leaves the first surface alone")]
    [Trait("TestId", "SKT-H056")]
    public void ShareOneSketchKeepCurvesIndependent()
    {
        var vm = new MainViewModel { HomeOpen = false };
        Visit(vm, 0);
        var sketch = vm.Session.Objects.Single(o => o.Kind == ObjKind.Sketch);
        ExtrudeWholeSketch(vm, sketch, 5);
        var first = vm.Session.Objects.Single(o => o.Kind == ObjKind.Surface);
        double firstHeight = first.Shape.Props().Max.Z;

        Visit(vm, 30);                                                  // the same sketch again, now with two rectangles
        sketch = vm.Session.Objects.Single(o => o.Kind == ObjKind.Sketch);
        Assert.True(sketch.SketchData!.SharedNurbsCurves);
        Assert.Equal(8, sketch.SketchData.Entities.Count(e => e.Type == EntType.Line));

        ExtrudeWholeSketch(vm, sketch, 20);                             // picks the sketch, which means only the curves not used yet
        var surfaces = vm.Session.Objects.Where(o => o.Kind == ObjKind.Surface).OrderBy(s => s.Shape.Props().Min.X).ToList();
        Assert.Equal(2, surfaces.Count);
        // reopening the sketch replays the first extrude, which may remake the object, so the surfaces are told apart by where they stand
        AssertClose(firstHeight, surfaces[0].Shape.Props().Max.Z, 1e-9, "First surface height (mm)");
        AssertClose(10, surfaces[0].Shape.Props().Max.X, 1e-6, "First surface still ends at its own rectangle (mm)");
        AssertClose(20, surfaces[1].Shape.Props().Max.Z, 1e-6, "Second surface height (mm)");
        AssertClose(30, surfaces[1].Shape.Props().Min.X, 1e-6, "Second surface starts at its own rectangle (mm)");
    }

    [AvaloniaFact(DisplayName = "SKT-H057 | A whole-sketch pick of the shared NURBS sketch takes only the curves no feature uses")]
    [Trait("TestId", "SKT-H057")]
    public void WholeSketchPickTakesUnusedCurves()
    {
        var vm = new MainViewModel { HomeOpen = false };
        Visit(vm, 0);
        var sketch = vm.Session.Objects.Single(o => o.Kind == ObjKind.Sketch);
        Assert.Equal(4, vm.Session.UnusedSketchCurves(SelItem.Of(sketch))!.Count);
        ExtrudeWholeSketch(vm, sketch, 5);
        Assert.Null(vm.Session.UnusedSketchCurves(SelItem.Of(sketch)));          // all four are used now
        Visit(vm, 30);
        sketch = vm.Session.Objects.Single(o => o.Kind == ObjKind.Sketch);
        Assert.Equal(4, vm.Session.UnusedSketchCurves(SelItem.Of(sketch))!.Count);
    }

    static void AddRectangle(MainViewModel vm, double x)
    {
        var sketch = vm.Session.Sketch!;
        sketch.Edit(vm.Session, "Add rectangle", () =>
        {
            var m = sketch.Model;
            int a = m.AddPoint(x, 0), b = m.AddPoint(x + 10, 0), c = m.AddPoint(x + 10, 10), d = m.AddPoint(x, 10);
            m.AddLine(a, b); m.AddLine(b, c); m.AddLine(c, d); m.AddLine(d, a);
        });
    }

    [AvaloniaFact(DisplayName = "SKT-H058 | Switching 3D sketch off on the NURBS tab gives an independent 2D sketch")]
    [Trait("TestId", "SKT-H058")]
    public void TwoDSwitchIsIndependentSketch()
    {
        var vm = new MainViewModel { HomeOpen = false };
        vm.SelectedTab = vm.Tabs.Single(t => t.Title == "NURBS");
        vm.StartFluidSketch();
        Assert.True(vm.Session.Sketch!.Model.SharedNurbsCurves);
        vm.SketchSpatialEnabled = true;         // the panel shows it on for the shared sketch
        vm.SketchSpatialEnabled = false;
        Assert.False(vm.Session.Sketch!.Model.SharedNurbsCurves);
        AddRectangle(vm, 0);
        vm.Session.ExitSketch(true);
        var sketch = vm.Session.Objects.Single(o => o.Kind == ObjKind.Sketch);
        Assert.False(sketch.SketchData!.SharedNurbsCurves);
        Assert.Equal(4, sketch.SketchData.Entities.Count(e => e.Type == EntType.Line));
    }

    [AvaloniaFact(DisplayName = "SKT-H059 | Switching 3D sketch back on folds the 2D work into the one shared NURBS sketch")]
    [Trait("TestId", "SKT-H059")]
    public void ThreeDSwitchJoinsSharedSketch()
    {
        var vm = new MainViewModel { HomeOpen = false };
        Visit(vm, 0);                                                    // the shared sketch already holds one rectangle
        vm.StartFluidSketch();
        vm.SketchSpatialEnabled = true;         // the panel shows it on for the shared sketch
        vm.SketchSpatialEnabled = false;
        AddRectangle(vm, 30);
        vm.SketchSpatialEnabled = true;
        Assert.True(vm.Session.Sketch!.Model.SharedNurbsCurves);
        Assert.Equal(8, vm.Session.Sketch.Model.Entities.Count(e => e.Type == EntType.Line));
        vm.Session.ExitSketch(true);
        var sketch = vm.Session.Objects.Single(o => o.Kind == ObjKind.Sketch);
        Assert.Equal(8, sketch.SketchData!.Entities.Count(e => e.Type == EntType.Line));
    }
}
