using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Meshing;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Scan-to-parametric workflows.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public class ReverseEngineerWorkflowTests
{
    static SceneObject Section(CadSession session, SceneObject scan, string plane, double offset, Action<MeshSectionSketchCommand>? inspect = null)
    {
        var command = Begin(session, () => new MeshSectionSketchCommand());
        Pick(command, "Mesh", scan);
        SetChoice(command, "Plane", plane); SetNumber(command, "Offset from the plane", offset);
        SetNumber(command, "Fit tolerance", .01);
        inspect?.Invoke(command);
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        return Assert.Single(session.Objects, o => o.Kind == ObjKind.Sketch);
    }

    static void CheckDeviation(CadSession session, SceneObject scan, SceneObject solid)
    {
        var command = Begin(session, () => new MeshDeviationCommand());
        Pick(command, "Mesh to check", scan);
        Pick(command, "Compare with (CAD body or mesh)", solid);
        SetNumber(command, "Tolerance", .05);
        Apply(command);
        Assert.Equal("100%", Result(command, "Points within tolerance"));
        session.CancelCommand();
    }

    [AvaloniaFact(DisplayName = "REV-H118 | Reverse Engineer exposes local fitting and separates sketches, rebuild and fallback conversion")]
    [Trait("TestId", "REV-H118")]
    public void RibbonShowsTheDifferentRoutes()
    {
        var vm = new MainViewModel(); var tab = vm.Tabs.Single(t => t.Title == "Reverse Engineer");
        Assert.Equal(new[] { "Select", "Datum", "Prepare", "Sketch from Scan", "Fit Surfaces", "Rebuild", "Check", "Transform", "Inspect", "Appearance" }, tab.Groups.Select(g => g.Title));
        var fits = tab.Groups.Single(g => g.Title == "Fit Surfaces").Items.OfType<RibbonButton>().ToArray();
        Assert.Equal(new[] { "Segment\n& Fit", "Fit Surface\nto Region" }, fits.Select(b => b.Label));
        Assert.All(fits, b => Assert.False(b.HasMenu));
        var fallback = tab.Groups.Single(g => g.Title == "Rebuild").Items.OfType<RibbonButton>().Single(b => b.Label == "Fallback");
        Assert.Equal("Mesh to\nSolid", Assert.Single(fallback.Menu!).Label);
    }

    [AvaloniaFact(DisplayName = "REV-H119 | A scanned circle accepts a diameter dimension, extrudes, compares to the scan and rebuilds after a dimension edit")]
    [Trait("TestId", "REV-H119")]
    public void CircleSectionBecomesDimensionDrivenExtrusion()
    {
        var session = new CadSession();
        using var cylinder = Occ.Cylinder(5, 20);
        var scan = AddMesh(session, MeshAnalysis.FromMeshData(cylinder.Tessellate(.002)), "Cylinder scan");
        var sketch = Section(session, scan, "XY (horizontal)", 10, command =>
        {
            var overlay = new Overlay(); command.BuildOverlay(session, overlay);
            Assert.NotEmpty(overlay.Lines);
            Assert.All(overlay.Lines, line =>
            {
                var mid = (line.A + line.B) * .5;
                Assert.InRange(Math.Sqrt(mid.X * mid.X + mid.Y * mid.Y), 4.98, 5.02);
            });
        });
        Assert.True(session.EditSketch(sketch));
        var model = session.Sketch!.Model;
        var circle = Assert.Single(model.Entities, e => e.Type == EntType.Circle);
        var dimension = model.Add(CType.Diameter, 10, dimension: true, refs: [circle.Id]);
        dimension.Name = "diameter";
        Assert.True(session.Sketch.Solve(session).Converged);
        session.ExitSketch(true);
        var extrude = StartCommand(session, session.Find(sketch.Id)!, () => new ExtrudeCommand());
        SetChoice(extrude, "Extent", "Symmetric"); SetNumber(extrude, "Distance", 20);
        session.CommitCommand(); Assert.Null(session.ActiveCommand);
        var solid = Assert.Single(session.Objects, o => o.Kind == ObjKind.Solid);
        AssertValidSolid(solid.Shape); AssertClose(500 * Math.PI, solid.Shape.Props().Volume, .05, "Cylinder volume");
        CheckDeviation(session, session.Find(scan.Id)!, solid);
        Assert.True(session.EditSketch(session.Find(sketch.Id)!));
        session.Sketch!.Model.Constraints.Single(c => c.Name == "diameter").Value = 12;
        Assert.True(session.Sketch.Solve(session).Converged);
        session.ExitSketch(true);
        solid = Assert.Single(session.Objects, o => o.Kind == ObjKind.Solid);
        AssertClose(720 * Math.PI, solid.Shape.Props().Volume, .05, "Edited diameter rebuilds the extrusion");
    }

    [AvaloniaFact(DisplayName = "REV-H120 | A tube section can be trimmed to one radial profile, dimensioned, revolved and compared to the scan")]
    [Trait("TestId", "REV-H120")]
    public void ManuallyTrimmedSectionBecomesRevolvedTube()
    {
        var session = new CadSession();
        using var outer = Occ.Cylinder(5, 8); using var inner = Occ.Cylinder(2, 8);
        using var tube = Occ.Cut(outer, inner);
        var scan = AddMesh(session, MeshAnalysis.FromMeshData(tube.Tessellate(.002)), "Tube scan");
        var sketch = Section(session, scan, "XZ", 0);
        Assert.True(session.EditSketch(sketch));
        var model = session.Sketch!.Model;
        model.Entities.RemoveAll(e => model.PointsOf(e).All(p => model.P(p).X < 0));
        Assert.Equal(4, model.Entities.Count);
        Assert.All(model.Entities, e => Assert.Equal(EntType.Line, e.Type));
        var height = model.Entities.First(e => Math.Abs(model.P(e.A).X - model.P(e.B).X) < .001);
        model.Add(CType.LineLength, 8, dimension: true, refs: [height.Id]);
        Assert.True(session.Sketch.Solve(session).Converged);
        session.ExitSketch(true);
        var revolve = StartCommand(session, session.Find(sketch.Id)!, () => new RevolveCommand());
        SetChoice(revolve, "Extent", "Full 360°");
        session.CommitCommand(); Assert.Null(session.ActiveCommand);
        var solid = Assert.Single(session.Objects, o => o.Kind == ObjKind.Solid);
        AssertValidSolid(solid.Shape); AssertClose(168 * Math.PI, solid.Shape.Props().Volume, .1, "Revolved tube volume");
        CheckDeviation(session, session.Find(scan.Id)!, solid);
    }
}
