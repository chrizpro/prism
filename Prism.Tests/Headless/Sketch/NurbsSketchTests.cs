using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sketch;

/// <summary>On the NURBS tab every visit to Draw makes a sketch of its own, so a surface made from an earlier sketch is left alone (owner request 2026-11-14).</summary>
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

    [AvaloniaFact(DisplayName = "SKT-H056 | Each NURBS sketch visit is a separate sketch; extruding the second leaves the first surface where it was")]
    [Trait("TestId", "SKT-H056")]
    public void EachVisitIsItsOwnSketch()
    {
        var vm = new MainViewModel { HomeOpen = false };
        Visit(vm, 0);
        var first = vm.Session.Objects.Single(o => o.Kind == ObjKind.Sketch);
        var extrude = Begin(vm.Session, () => new ExtrudeCommand(surface: true));
        Pick(extrude, "Profile", first);
        SetNumber(extrude, "Distance", 5);
        vm.Session.CommitCommand();
        var surface = vm.Session.Objects.Single(o => o.Kind == ObjKind.Surface);
        double firstHeight = surface.Shape.Props().Max.Z;

        Visit(vm, 30);
        var sketches = vm.Session.Objects.Where(o => o.Kind == ObjKind.Sketch).ToList();
        Assert.Equal(2, sketches.Count);
        Assert.All(sketches, s => Assert.True(s.SketchData!.SharedNurbsCurves));
        var second = sketches.Single(s => s.Id != first.Id);
        Assert.Equal(4, second.SketchData!.Entities.Count(e => e.Type == EntType.Line));

        var again = Begin(vm.Session, () => new ExtrudeCommand(surface: true));
        Pick(again, "Profile", second);
        SetNumber(again, "Distance", 20);
        vm.Session.CommitCommand();
        var surfaces = vm.Session.Objects.Where(o => o.Kind == ObjKind.Surface).ToList();
        Assert.Equal(2, surfaces.Count);
        AssertClose(firstHeight, surfaces.Single(s => s.Id == surface.Id).Shape.Props().Max.Z, 1e-9, "First surface height (mm)");
        AssertClose(20, surfaces.Single(s => s.Id != surface.Id).Shape.Props().Max.Z, 1e-6, "Second surface height (mm)");
    }

    [AvaloniaFact(DisplayName = "SKT-H057 | The browser gathers NURBS sketches under one NURBS Curves folder")]
    [Trait("TestId", "SKT-H057")]
    public void BrowserGroupsNurbsSketches()
    {
        var vm = new MainViewModel { HomeOpen = false };
        Visit(vm, 0); Visit(vm, 30); Visit(vm, 60);
        vm.RebuildBrowser();
        var folder = vm.Browser.Single().Children.Single(n => n.Title == "Sketches").Children.Single(n => n.IsFolder && n.Title == "NURBS Curves");
        Assert.Equal(3, folder.Children.Count);
    }
}
