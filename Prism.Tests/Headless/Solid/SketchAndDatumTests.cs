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
        Assert.True(point.IsEnabled);           // global now: places model datums outside sketches
        point.Invoke();
        var pick = Assert.IsType<DatumPickTool>(session.FreeTool);
        Assert.Equal(SketchDatumKind.Point, pick.Kind);
        Assert.True(pick.Key(session, "Escape"));
        Assert.Null(session.FreeTool);

        session.EnterSketch(Plane3.XY with { Origin = new Vec3(0, 0, 7) });
        point.Invoke();
        Assert.Null(session.FreeTool);          // the datum button does nothing inside a sketch; the sketch's own Point tool draws points
        var sketch = Assert.IsType<SketchContext>(session.Sketch);
        var tool = new PointTool();
        sketch.SetTool(session, tool);
        var pixel = session.Camera.Project(new Vec3(4, 6, 7));
        Assert.NotNull(pixel);
        tool.PointerDown(session, new(pixel.Value.x, pixel.Value.y, true, false, false, false));
        var entity = Assert.Single(sketch.Model.Entities);
        Assert.Equal(EntType.Point, entity.Type);
        var location = sketch.Model.P(entity.A);
        AssertNear(4, location.X, "Point U (mm)");
        AssertNear(6, location.Y, "Point V (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H072 | Datum Axis and Plane place model datums; there is no sketch-only Save gizmo, and a sketch makes no datums")]
    [Trait("TestId", "SOL-H072")]
    public void DatumButtonsBelongToModelSpace()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var buttons = SolidButtons(vm);
        Assert.DoesNotContain(buttons, button => button.Label == "Save gizmo");
        var axis = buttons.Single(button => button.Label == "Axis");
        var plane = buttons.Single(button => button.Label == "Plane");
        foreach (var button in new[] { axis, plane }) { button.Refresh(); Assert.True(button.IsEnabled); }

        session.EnterSketch(Plane3.XY);
        axis.Invoke(); plane.Invoke();
        Assert.Empty(session.Sketch!.Datums);
        Assert.Null(session.FreeTool);
        Assert.DoesNotContain(vm.Tabs.Single(tab => tab.Title == "Sketch").Groups, group => group.Title == "Datum");
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

    [AvaloniaFact(DisplayName = "SOL-H078 | A plane pick reveals hidden planes and the browser eyes stay in sync")]
    [Trait("TestId", "SOL-H078")]
    public void PlanePickRevealsHiddenPlanesInBrowser()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var plane = session.AddObject(null, "Work plane", ObjKind.Plane,
            plane: Plane3.XZ with { Origin = new Vec3(0, 5, 0) });
        session.Update(plane.Id, o => o.WithVisible(false), checkpoint: false);

        session.StartCommand(new CreateSketchCommand());
        Assert.True(session.PlaneShown(session.Find(plane.Id)!), "hidden construction plane is revealed while the command wants a plane");
        var node = vm.AllBrowserNodes().Single(n => n.IsObject && n.ObjectId == plane.Id);
        Assert.True(node.IsVisible, "browser eye matches the revealed plane");
        var origin = vm.AllBrowserNodes().Single(n => n.Kind == NodeKind.Origin && n.OriginIndex == 1);
        Assert.True(origin.IsVisible, "origin row is checked while its plane is drawn for the pick");

        node.IsVisible = false;                                                  // the eye click, as the user would do it
        Assert.False(session.PlaneShown(session.Find(plane.Id)!), "toggling the row suppresses the plane for this pick");

        session.CancelCommand();
        Assert.False(session.PlaneShown(session.Find(plane.Id)!));
        Assert.False(session.Find(plane.Id)!.Visible, "the plane keeps its saved hidden state");

        session.StartCommand(new CreateSketchCommand());
        Assert.True(session.PlaneShown(session.Find(plane.Id)!), "the next pick reveals it again");
        session.CancelCommand();
    }

    [AvaloniaFact(DisplayName = "SOL-H080 | Reference plane size scales the origin-plane pick bounds")]
    [Trait("TestId", "SOL-H080")]
    public void PlaneScaleScalesOriginPlanePickBounds()
    {
        var session = new CadSession();
        session.SetOriginVisible(0, true);          // XY plane always on for this pick
        session.Camera = new Camera { Orthographic = true, Target = new(30, 0, 0), Pitch = Math.PI / 2 };
        var pixel = session.Camera.Project(new Vec3(30, 0, 0))!.Value;
        double prev = AppSettings.Current.PlaneScale;
        try
        {
            AppSettings.Current.PlaneScale = 1;
            var hit = session.Pick(pixel.x, pixel.y, SelFilter.Plane);
            Assert.Equal(new SelItem(Guid.Empty, SelKind.Origin, 0), hit?.Item);
            AppSettings.Current.PlaneScale = 0.25;  // the same click is outside the shrunken quad
            Assert.Null(session.Pick(pixel.x, pixel.y, SelFilter.Plane));
        }
        finally { AppSettings.Current.PlaneScale = prev; }
    }

    [AvaloniaFact(DisplayName = "SOL-H079 | Hiding a visible plane during the pick stays hidden afterwards")]
    [Trait("TestId", "SOL-H079")]
    public void VisiblePlaneHiddenDuringPickStaysHidden()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var plane = session.AddObject(null, "Work plane", ObjKind.Plane,
            plane: Plane3.XZ with { Origin = new Vec3(0, 5, 0) });

        session.StartCommand(new CreateSketchCommand());
        var node = vm.AllBrowserNodes().Single(n => n.IsObject && n.ObjectId == plane.Id);
        Assert.True(node.IsVisible);

        node.IsVisible = false;
        Assert.False(session.Find(plane.Id)!.Visible, "a real visibility switch persists, like any other object");
        session.CancelCommand();
        Assert.False(session.PlaneShown(session.Find(plane.Id)!));
    }

    [AvaloniaFact(DisplayName = "SOL-H086 | Datum Point places repeated model datums and undoes them one by one")]
    [Trait("TestId", "SOL-H086")]
    public void DatumPointPlacesRepeatedModelDatums()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        session.Camera.SetView(0, 90);                    // look straight down at the XY work plane
        SolidButtons(vm).Single(button => button.Label == "Point").Invoke();
        var tool = Assert.IsType<DatumPickTool>(session.FreeTool);

        var pixel = session.Camera.Project(new Vec3(3, 4, 0))!.Value;
        tool.PointerDown(session, new(pixel.x, pixel.y, true, false, false, false));
        tool.PointerDown(session, new(pixel.x + 40, pixel.y + 30, true, false, false, false));

        var carrier = session.DatumCarrier();
        var datums = carrier.SketchData!.Datums;
        Assert.Equal(2, datums.Count);
        Assert.All(datums, d => Assert.Equal(SketchDatumKind.Point, d.Kind));
        Assert.Equal("Point 1", datums[0].Name);
        Assert.Equal("Point 2", datums[1].Name);
        AssertNear(3, datums[0].Frame.Origin.X, "first point X (mm)");
        AssertNear(4, datums[0].Frame.Origin.Y, "first point Y (mm)");
        AssertNear(0, datums[0].Frame.Origin.Z, "first point lies on the work plane");

        session.Undo();
        Assert.Single(session.Find(carrier.Id)!.SketchData!.Datums);
        Assert.Same(session.DatumCarrier(), session.Find(carrier.Id));   // no second carrier after undo

        Assert.True(tool.Key(session, "Escape"));
        Assert.Null(session.FreeTool);
        Assert.Single(session.Objects, o => o.Kind == ObjKind.Sketch);  // placements survive
    }

    [AvaloniaFact(DisplayName = "SOL-H087 | Datum Axis and Plane adopt the picked face and edge directions")]
    [Trait("TestId", "SOL-H087")]
    public void DatumAxisAndPlaneAdoptPickedGeometry()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var body = CreateBoxBody(session, Vec3.Zero, 4, 6, 8);
        session.Camera.SetView(0, 90);
        var buttons = SolidButtons(vm);

        // Axis on the top face becomes that face's normal at the hit point.
        buttons.Single(button => button.Label == "Axis").Invoke();
        var axisTool = Assert.IsType<DatumPickTool>(session.FreeTool);
        var top = session.Camera.Project(new Vec3(2, 3, 8))!.Value;
        axisTool.PointerDown(session, new(top.x, top.y, true, false, false, false));
        var carrier = session.DatumCarrier();
        var axis = Assert.Single(carrier.SketchData!.Datums);
        Assert.Equal(SketchDatumKind.Axis, axis.Kind);
        AssertNear(1, axis.Frame.U.Z, "axis direction = face normal");
        AssertNear(8, axis.Frame.Origin.Z, "axis root on the top face");

        // Plane on the same face matches its orientation.
        buttons.Single(button => button.Label == "Plane").Invoke();
        var planeTool = Assert.IsType<DatumPickTool>(session.FreeTool);
        planeTool.PointerDown(session, new(top.x, top.y, true, false, false, false));
        var plane = session.Find(carrier.Id)!.SketchData!.Datums[^1];
        Assert.Equal(SketchDatumKind.Plane, plane.Kind);
        AssertNear(1, Math.Abs(plane.Frame.Normal.Z), "plane normal = face normal");

        // Axis on a solid edge follows the edge direction: in a front view the box's
        // vertical right-front edge is a line on screen.
        session.Camera.SetView(0, 0);
        buttons.Single(button => button.Label == "Axis").Invoke();
        var edgeTool = Assert.IsType<DatumPickTool>(session.FreeTool);
        var mid = session.Camera.Project(new Vec3(4, 0, 4))!.Value;
        var hit = session.Pick(mid.x, mid.y, SelFilter.Any);
        Assert.Equal(SelKind.Edge, hit?.Item.Kind);
        edgeTool.PointerDown(session, new(mid.x, mid.y, true, false, false, false));
        var edgeAxis = session.Find(carrier.Id)!.SketchData!.Datums[^1];
        Assert.Equal(SketchDatumKind.Axis, edgeAxis.Kind);
        AssertNear(1, Math.Abs(edgeAxis.Frame.U.Z), "axis direction = edge direction");
    }

    [AvaloniaFact(DisplayName = "SOL-H088 | Datum picks on empty space use the work plane and Esc keeps the datums")]
    [Trait("TestId", "SOL-H088")]
    public void DatumEmptySpaceUsesWorkPlane()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        session.Camera.SetView(0, 90);
        var buttons = SolidButtons(vm);

        buttons.Single(button => button.Label == "Plane").Invoke();
        var tool = Assert.IsType<DatumPickTool>(session.FreeTool);
        var pixel = session.Camera.Project(new Vec3(5, 5, 0))!.Value;
        tool.PointerDown(session, new(pixel.x, pixel.y, true, false, false, false));
        var plane = Assert.Single(session.DatumCarrier().SketchData!.Datums);
        Assert.Equal(SketchDatumKind.Plane, plane.Kind);
        AssertNear(5, plane.Frame.Origin.X, "plane origin X (mm)");
        AssertNear(1, Math.Abs(plane.Frame.Normal.Z), "work-plane normal");

        // Right-click finishes the tool and keeps the placements.
        tool.PointerDown(session, new(pixel.x, pixel.y, false, false, false, false));
        Assert.Null(session.FreeTool);
        Assert.Single(session.Find(session.DatumCarrier().Id)!.SketchData!.Datums);
    }

    [AvaloniaFact(DisplayName = "SOL-H090 | A datum click lands under the cursor — even on a drawn origin axis")]
    [Trait("TestId", "SOL-H090")]
    public void DatumClickNearOriginAxisDoesNotCenter()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        session.Camera.SetView(0, 90);                    // top view: the X origin axis projects through y = 0
        SolidButtons(vm).Single(button => button.Label == "Point").Invoke();
        var tool = Assert.IsType<DatumPickTool>(session.FreeTool);

        // A click right on the X axis at x = 8 is an origin-axis pick — it must still land at the click.
        var onAxis = session.Camera.Project(new Vec3(8, 0, 0))!.Value;
        tool.PointerDown(session, new(onAxis.x, onAxis.y, true, false, false, false));
        var datum = Assert.Single(session.DatumCarrier().SketchData!.Datums);
        AssertNear(8, datum.Frame.Origin.X, "point X (mm)");
        AssertNear(0, datum.Frame.Origin.Y, "point Y (mm)");
        Assert.True(datum.Frame.Origin.Length > 1, "origin-axis picks must not teleport the datum to the world origin");
    }

    [AvaloniaFact(DisplayName = "SOL-H096 | In a Front view, datum points land on the front grid where clicked")]
    [Trait("TestId", "SOL-H096")]
    public void DatumPointInFrontViewLandsOnFrontGrid()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        session.Camera.SetView(0, 0);                      // Front: the ground plane is edge-on here
        session.Camera.Orthographic = true;
        SolidButtons(vm).Single(button => button.Label == "Point").Invoke();
        foreach (var target in new[] { new Vec3(12, 0, 9), new Vec3(-20, 0, 4) })
        {
            var pixel = session.Camera.Project(target)!.Value;
            session.PointerDown(new(pixel.x, pixel.y, true, false, false, false));
            session.PointerUp(new(pixel.x, pixel.y, false, false, false, false));
        }
        var datums = session.DatumCarrier().SketchData!.Datums;
        Assert.Equal(2, datums.Count);
        AssertNear(12, datums[0].Frame.Origin.X, "first point X (mm)");
        AssertNear(9, datums[0].Frame.Origin.Z, "first point Z (mm)");
        AssertNear(-20, datums[1].Frame.Origin.X, "second point X (mm)");
        AssertNear(4, datums[1].Frame.Origin.Z, "second point Z (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H094 | Datum Point places points even while the move gizmo is showing")]
    [Trait("TestId", "SOL-H094")]
    public void DatumPointWinsOverMoveGizmo()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        CreateBoxBody(session, Vec3.Zero, 4, 6, 8);
        session.Camera.SetView(0, 90);
        session.ShowDatumGizmo(Vec3.Zero);
        session.Datum!.MoveMode = true;                    // in Move mode a click on a body would otherwise grab it
        SolidButtons(vm).Single(button => button.Label == "Point").Invoke();

        foreach (var target in new[] { new Vec3(20, 10, 0), new Vec3(1.5, 2, 8) })
        {
            var pixel = session.Camera.Project(target)!.Value;
            session.PointerDown(new(pixel.x, pixel.y, true, false, false, false));
            session.PointerUp(new(pixel.x, pixel.y, false, false, false, false));
        }

        var datums = session.DatumCarrier().SketchData!.Datums;
        Assert.Equal(2, datums.Count);
        AssertNear(20, datums[0].Frame.Origin.X, "empty-space point X (mm)");
        AssertNear(10, datums[0].Frame.Origin.Y, "empty-space point Y (mm)");
        AssertNear(8, datums[1].Frame.Origin.Z, "point on the box top (mm)");
        Assert.IsType<DatumPickTool>(session.FreeTool);
    }

    [AvaloniaFact(DisplayName = "SOL-H091 | Datum points are pickable and repositionable")]
    [Trait("TestId", "SOL-H091")]
    public void DatumPointPicksAndMoves()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        session.Camera.SetView(0, 90);
        SolidButtons(vm).Single(button => button.Label == "Point").Invoke();
        var tool = Assert.IsType<DatumPickTool>(session.FreeTool);
        var pixel = session.Camera.Project(new Vec3(3, 4, 0))!.Value;
        tool.PointerDown(session, new(pixel.x, pixel.y, true, false, false, false));
        tool.Key(session, "Escape");

        // The placed point is a real pick target now — object mode and the Datum flag both reach it.
        var hit = session.Pick(pixel.x, pixel.y, SelFilter.Datum);
        Assert.NotNull(hit);
        Assert.Equal(SelKind.Datum, hit!.Value.Item.Kind);
        Assert.NotNull(session.DatumOf(hit.Value.Item));

        // Moving a misplaced point is a frame edit, not a delete-and-replace.
        session.SetDatumFrame(hit.Value.Item, new Plane3(new Vec3(9, 9, 0), Vec3.UnitX, Vec3.UnitY));
        var moved = session.DatumOf(hit.Value.Item);
        Assert.NotNull(moved);
        AssertNear(9, moved!.Frame.Origin.X, "moved point X (mm)");
        AssertNear(9, moved.Frame.Origin.Y, "moved point Y (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H092 | Move gizmo drags bound objects and datums")]
    [Trait("TestId", "SOL-H092")]
    public void MoveGizmoTransformsTargets()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var body = CreateBoxBody(session, Vec3.Zero, 4, 6, 8);
        var overlay = new Overlay();

        session.ShowDatumGizmo(Vec3.Zero);
        var gizmo = Assert.IsType<DatumGizmoTool>(session.Datum);
        gizmo.MoveMode = true;
        gizmo.BindTargets(session, [SelItem.Of(body)]);

        // The payload follows the gizmo frame: typing an origin lands the body's center there.
        gizmo.Binding.SetOrigin(new Vec3(5, 0, 0));
        gizmo.BuildOverlay(session, overlay);
        AssertNear(5, session.Find(body.Id)!.Mesh.Min.X + 2, "moved body center X (mm)");
        AssertNear(-3, session.Find(body.Id)!.Mesh.Min.Y, "moved body center Y (mm)");

        // A single datum adopts the gizmo frame directly — panel values move it exactly.
        SolidButtons(vm).Single(button => button.Label == "Point").Invoke();
        var pick = Assert.IsType<DatumPickTool>(session.FreeTool);
        pick.PointerDown(session, new(10, 10, true, false, false, false));
        pick.Key(session, "Escape");
        var datumItem = new SelItem(session.DatumCarrier().Id, SelKind.Datum, 0);
        session.ShowDatumGizmo(Vec3.Zero);
        gizmo = Assert.IsType<DatumGizmoTool>(session.Datum);
        gizmo.MoveMode = true;
        gizmo.BindTargets(session, [datumItem]);
        gizmo.Binding.SetOrigin(new Vec3(20, 20, 0));
        gizmo.BuildOverlay(session, overlay);
        var moved = session.DatumOf(datumItem);
        Assert.NotNull(moved);
        AssertNear(20, moved!.Frame.Origin.X, "gizmo-moved datum X (mm)");

        // The ribbon Move button arms this path instead of a slot command.
        Assert.Null(session.ActiveCommand);
    }

    [AvaloniaFact(DisplayName = "SOL-H093 | Circular pattern preview spreads instances on a ring, not a line")]
    [Trait("TestId", "SOL-H093")]
    public void CircularPatternExampleIsCircular()
    {
        var topic = Prism.Help.ToolHelp.Find("pattern-circular");
        Assert.NotNull(topic);
        var example = Prism.Help.ToolHelp.Example(topic!);
        var pos = example.After.Mesh.Positions;
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i + 1 < pos.Length; i += 3)
        {
            minX = Math.Min(minX, pos[i]); maxX = Math.Max(maxX, pos[i]);
            minY = Math.Min(minY, pos[i + 1]); maxY = Math.Max(maxY, pos[i + 1]);
        }
        double spanX = maxX - minX, spanY = maxY - minY;
        Assert.True(spanX > 20 && spanY > 20, "the ring should be drawn at a readable size");
        // A row of copies would stretch ~3:1 along one axis; a ring stays balanced in plan.
        Assert.True(Math.Abs(spanX - spanY) < spanX * 0.4, $"circular pattern spans {spanX:0.#} x {spanY:0.#} — reads as a line, not a ring");
    }
}
