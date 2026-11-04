using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Solid.SolidRibbonTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/README.md">Sketch entry and reference points.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/construction-planes/README.md">Construction planes and saved datums.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class SketchAndDatumTests
{
    [AvaloniaFact(DisplayName = "SOL-H070 | Solid Create Sketch uses a picked face as its drawing plane")]
    [Trait("TestId", "SOL-H070")]
    public void CreateSketchUsesPickedFace()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var body = CreateBoxBody(session, Vec3.Zero, 4, 6, 8);
        var button = SolidButtons(vm).Single(item => item.Icon == "sketch");
        button.Invoke();
        var command = Assert.IsType<CreateSketchCommand>(session.ActiveCommand);
        int top = Enumerable.Range(0, body.Shape.Count(SubKind.Face)).Single(index =>
        {
            using var face = body.Shape.Sub(SubKind.Face, index);
            return Occ.FaceInfoOf(face).Normal.Z > 0.99;
        });
        command.ConfigureInputs(() => Assert.True(command.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, top))));
        session.CommitCommand();
        Assert.NotNull(session.Sketch);
        AssertNear(8, session.Sketch.Plane.Origin.Z, "Drawing plane height (mm)");
        AssertNear(1, session.Sketch.Plane.Normal.Z, "Drawing plane normal");
    }

    [AvaloniaFact(DisplayName = "SOL-H071 | Datum Point places a reference point on the sketch plane")]
    [Trait("TestId", "SOL-H071")]
    public void DatumPointPlacesReferenceOnActivePlane()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var point = SolidButtons(vm).Single(button => button.Label == "Point");
        point.Refresh();
        Assert.False(point.IsEnabled);

        session.EnterSketch(Plane3.XY with { Origin = new Vec3(0, 0, 7) });
        point.Refresh();
        Assert.True(point.IsEnabled);
        point.Invoke();
        var sketch = Assert.IsType<SketchContext>(session.Sketch);
        var tool = Assert.IsType<PointTool>(sketch.Tool);
        var pixel = session.Camera.Project(new Vec3(4, 6, 7));
        Assert.NotNull(pixel);
        tool.PointerDown(session, new(pixel.Value.x, pixel.Value.y, true, false, false, false));
        var entity = Assert.Single(sketch.Model.Entities);
        Assert.Equal(EntType.Point, entity.Type);
        var location = sketch.Model.P(entity.A);
        AssertNear(4, location.X, "Point U (mm)");
        AssertNear(6, location.Y, "Point V (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H072 | Datum Axis, Plane and Save gizmo persist with a finished sketch")]
    [Trait("TestId", "SOL-H072")]
    public void DatumButtonsSaveDistinctReferences()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var buttons = SolidButtons(vm);
        var axis = buttons.Single(button => button.Label == "Axis");
        var plane = buttons.Single(button => button.Label == "Plane");
        var gizmo = buttons.Single(button => button.Label == "Save gizmo");
        foreach (var button in new[] { axis, plane, gizmo })
        {
            button.Refresh();
            Assert.False(button.IsEnabled);
        }

        session.EnterSketch(Plane3.XY);
        foreach (var button in new[] { axis, plane, gizmo })
        {
            button.Refresh();
            Assert.True(button.IsEnabled);
            button.Invoke();
        }
        Assert.Equal(new[] { SketchDatumKind.Axis, SketchDatumKind.Plane, SketchDatumKind.Gizmo },
            session.Sketch!.Datums.Select(datum => datum.Kind));
        session.ExitSketch(keep: true);
        var saved = Assert.Single(session.Objects, item => item.Kind == ObjKind.Sketch);
        Assert.Equal(3, saved.SketchData!.Datums.Count);
    }

    [AvaloniaFact(DisplayName = "SOL-H073 | Three-point construction plane contains all three input points")]
    [Trait("TestId", "SOL-H073")]
    public void ConstructionPlaneContainsThreePoints()
    {
        var vm = new MainViewModel();
        SolidButtons(vm).Single(button => button.Icon == "origin-plane" &&
            button.Label.Replace('\n', ' ') == "Construction Plane").Invoke();
        var command = Assert.IsType<ConstructionPlaneCommand>(vm.Session.ActiveCommand);
        command.ConfigureInputs(() => SetChoice(command, "Method", "Through three points"));
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var plane = Assert.Single(output.Add).Plane;
        Assert.NotNull(plane);
        foreach (var point in new Vec3[] { Vec3.Zero, new(50, 0, 0), new(0, 50, 30) })
            AssertNear(0, Vec3.Dot(point - plane.Value.Origin, plane.Value.Normal), "Signed plane distance (mm)");
    }
}
