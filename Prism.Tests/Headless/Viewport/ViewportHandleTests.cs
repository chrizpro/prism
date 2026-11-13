using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;
using static Prism.HeadlessTests.Mesh.MeshToolTests;

namespace Prism.HeadlessTests.Viewport;

/// <summary>
/// Characterizes the viewport drag handles (<see cref="HandleSpec"/>) that commands expose: every spatially
/// meaningful number should also be reachable by an arrow or ring on the model, with the panel as exact entry.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/viewport-interaction-handoff.md">Coverage and audit.</seealso>
[Trait("Feature", "Viewport handles")]
[Trait("Layer", "Headless")]
public class ViewportHandleTests
{
    static HandleSpec At(IReadOnlyList<HandleSpec> hs, string label) => Assert.Single(hs, h => h.Param.Label == label);

    // ------------------------------------------------------------------ primitives

    /// <summary>Shapes no longer show the move gizmo while they are inserted (owner direction 2026-11-13); these tests turn it on the way a user would.</summary>
    static void WithGizmo(Command command) => command.Params.OfType<BoolParam>().Single(p => p.Label == "Move with gizmo").Value = true;

    [AvaloniaFact(DisplayName = "HDL-H032 | A shape being inserted shows no move gizmo until Move with gizmo is on")]
    [Trait("TestId", "HDL-H032")]
    public void InsertingAShapeShowsNoGizmo()
    {
        var session = new CadSession();
        foreach (var create in new Func<Command>[] { () => new BoxCommand(), () => new CylinderCommand(), () => new SphereCommand(), () => new ConeCommand(), () => new TorusCommand() })
        {
            session.StartCommand(create);
            var command = session.ActiveCommand!;
            Assert.Null(command.Gizmo);
            Assert.Null(session.CommandGizmoInfo);
            WithGizmo(command);
            Assert.NotNull(command.Gizmo);
            session.CancelCommand();
        }
    }

    [AvaloniaFact(DisplayName = "HDL-H033 | Dragging the edge of a hole in the viewport resizes it, even when the hole is small")]
    [Trait("TestId", "HDL-H033")]
    public void HoleResizesByDraggingItsEdge()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 40, 40, 10);
        session.Camera.Width = 800; session.Camera.Height = 600; session.Camera.SetView(0, 90); session.Camera.Fit(new(0, 0, 0), new(40, 40, 10));
        var hole = Begin(session, () => new HoleCommand());
        hole.ConfigureInputs(() => SetSelection(hole, hole.Slots[0].Label, body));
        var diameter = hole.Params.OfType<NumberParam>().Single(p => p.Label == "Diameter");
        diameter.Value = 6;
        (double x, double y) Screen(double dx) { var p = session.Camera.Project(new Vec3(20 + dx, 20, 10))!.Value; return (p.x, p.y); }
        var rim = Screen(3);                                       // the edge of a 6 mm hole sits only a few pixels from its centre dot
        session.PointerDown(new PointerInfo(rim.x, rim.y, true, false, false, false));
        var to = Screen(9);
        session.PointerMove(new PointerInfo(to.x, to.y, true, false, false, false));
        session.PointerUp(new PointerInfo(to.x, to.y, false, false, false, false));
        AssertClose(18, diameter.Value, 0.6, "Hole diameter after dragging the edge out to a 9 mm radius (mm)");
        var overlay = new Overlay(); session.BuildOverlay(overlay);
        Assert.NotEmpty(overlay.Dots);                             // the circle is drawn with the centre dot
    }

    [AvaloniaFact(DisplayName = "HDL-H001 | Box shows three dimension arrows and the shared gizmo for its position")]
    [Trait("TestId", "HDL-H001")]
    public void BoxDimensionArrows()
    {
        var session = new CadSession();
        var box = Begin(session, () => new BoxCommand());
        WithGizmo(box);
        var hs = box.Handles;
        Assert.Equal(3, hs.Count);
        var gizmo = Assert.IsType<GizmoSpec>(box.Gizmo);
        Assert.Equal("Position X", gizmo.Move[0]!.Label); Assert.Equal("Position Z", gizmo.Move[2]!.Label);
        Assert.True(gizmo.Supports(0)); Assert.False(gizmo.Supports(1));
        AssertPoint(new Vec3(20, 15, 10), gizmo.Center, 1e-9, "Gizmo sits in the middle of the box");
        var h = At(hs, "Height (Z)");
        AssertPoint(Vec3.Zero, h.Origin, 1e-9, "Height arrow base");
        AssertPoint(Vec3.UnitZ, h.Dir, 1e-9, "Height arrow direction");
        Assert.True(Vec3.Dot(At(hs, "Width (X)").Dir, Vec3.UnitX) > 0.99, "Width arrow along +X");
        Assert.True(Vec3.Dot(At(hs, "Depth (Y)").Dir, Vec3.UnitY) > 0.99, "Depth arrow along +Y");
    }

    [AvaloniaFact(DisplayName = "HDL-H002 | Cylinder shows radius and height arrows")]
    [Trait("TestId", "HDL-H002")]
    public void CylinderDimensionArrows()
    {
        var session = new CadSession();
        var cyl = Begin(session, () => new CylinderCommand());
        WithGizmo(cyl);
        var hs = cyl.Handles;
        Assert.Equal(2, hs.Count);   // radius + height; the position is the gizmo
        Assert.True(Vec3.Dot(At(hs, "Radius").Dir, Vec3.UnitX) > 0.99, "Radius arrow points radially");
        var height = At(hs, "Height");
        Assert.True(Vec3.Dot(height.Dir, Vec3.UnitZ) > 0.99, "Height arrow along +Z");
        AssertPoint(new Vec3(12, 0, 0), height.Origin, 1e-9, "Height arrow runs up the wall, from the end of the radius arrow");
        AssertPoint(new Vec3(0, 0, 15), cyl.Gizmo!.Center, 1e-9, "Gizmo sits in the middle of the cylinder");
    }

    [AvaloniaFact(DisplayName = "HDL-H003 | Cone, torus and sphere anchor their size arrows on the shape, clear of the gizmo")]
    [Trait("TestId", "HDL-H003")]
    public void ConeTopRadiusAnchor()
    {
        var session = new CadSession();
        var cone = Begin(session, () => new ConeCommand());
        WithGizmo(cone);
        var top = At(cone.Handles, "Top radius");
        AssertClose(30, top.Origin.Z, 1e-9, "Top radius arrow height (mm)");
        AssertPoint(new Vec3(16, 0, 0), At(cone.Handles, "Height").Origin, 1e-9, "Height arrow stands beside the cone, level with its widest end");
        AssertPoint(new Vec3(0, 0, 15), cone.Gizmo!.Center, 1e-9, "Gizmo sits in the middle of the cone");
        session.CancelCommand();
        var torus = Begin(session, () => new TorusCommand());
        WithGizmo(torus);
        var minor = At(torus.Handles, "Minor radius");
        AssertClose(20, Math.Abs(minor.Origin.X), 1e-9, "Minor radius arrow on the ring's centre line (mm)");
        Assert.True(Vec3.Dot(minor.Dir, Vec3.UnitX) < -0.99, "Torus radii point away from the gizmo's X arrow");
        AssertPoint(Vec3.Zero, torus.Gizmo!.Center, 1e-9, "Gizmo sits in the middle of the torus");
        session.CancelCommand();
        var sphere = Begin(session, () => new SphereCommand());
        WithGizmo(sphere);
        Assert.True(Vec3.Dot(At(sphere.Handles, "Radius").Dir, Vec3.UnitX) < -0.99, "Sphere radius points away from the gizmo's X arrow");
    }

    [AvaloniaFact(DisplayName = "HDL-H025 | No primitive size arrow lies along one of the gizmo's arrows")]
    [Trait("TestId", "HDL-H025")]
    public void PrimitiveArrowsStayClearOfTheGizmo()
    {
        var session = new CadSession();
        Func<Command>[] primitives = [() => new BoxCommand(), () => new CylinderCommand(), () => new SphereCommand(), () => new ConeCommand(), () => new TorusCommand()];
        foreach (var create in primitives)
        {
            session.StartCommand(create);
            var command = session.ActiveCommand!;
            WithGizmo(command);
            SetNumber(command, "Position X", 7); SetNumber(command, "Position Y", -4); SetNumber(command, "Position Z", 3);
            var gizmo = Assert.IsType<GizmoSpec>(command.Gizmo);
            Assert.NotEmpty(command.Handles);
            foreach (var h in command.Handles)
                for (int axis = 0; axis < 3; axis++)
                {
                    // a size arrow collides with a gizmo arrow when both leave the same line in the same direction
                    var along = gizmo.Axes[axis]; var offset = h.Origin - gizmo.Center;
                    bool sameLine = (offset - along * Vec3.Dot(offset, along)).Length < 1e-6;
                    Assert.False(sameLine && Vec3.Dot(h.Dir.Normalized(), along) > 0.99, $"{command.Title}: '{h.Param.Label}' lies along the gizmo's arrow {axis}");
                }
            session.CancelCommand();
        }
    }

    // ------------------------------------------------------------------ curve tools and pipe

    [AvaloniaFact(DisplayName = "HDL-H004 | Pipe shows a radius arrow at each end of the path")]
    [Trait("TestId", "HDL-H004")]
    public void PipeRadiusArrows()
    {
        var session = new CadSession();
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(50, 0, 0)]);
        var pipe = Begin(session, () => new PipeCommand());
        Pick(pipe, "Path curve", path);
        var hs = pipe.Handles;
        Assert.Equal(2, hs.Count);
        AssertPoint(Vec3.Zero, At(hs, "Radius at the start").Origin, 1e-6, "Start arrow base");
        AssertPoint(new Vec3(50, 0, 0), At(hs, "Radius at the end").Origin, 1e-6, "End arrow base");
        Assert.True(Math.Abs(Vec3.Dot(hs[0].Dir, Vec3.UnitX)) < 1e-6, "Start arrow is radial, not along the path");
    }

    [AvaloniaFact(DisplayName = "HDL-H005 | Trim Curve anchors both %-handles at the curve start")]
    [Trait("TestId", "HDL-H005")]
    public void TrimCurveRangeHandles()
    {
        var session = new CadSession();
        var curve = CreateCurve(session, [Vec3.Zero, new Vec3(40, 0, 0)]);
        var trim = Begin(session, () => new TrimCurveCommand());
        Pick(trim, "Curve (a single segment)", curve);
        var hs = trim.Handles;
        Assert.Equal(2, hs.Count);
        AssertPoint(Vec3.Zero, At(hs, "Keep from (%)").Origin, 1e-6, "From-handle base");
        AssertPoint(Vec3.Zero, At(hs, "Keep to (%)").Origin, 1e-6, "To-handle base");
    }

    [AvaloniaFact(DisplayName = "HDL-H006 | Extend Curve puts an arrow on each free end")]
    [Trait("TestId", "HDL-H006")]
    public void ExtendCurveEndArrows()
    {
        var session = new CadSession();
        var curve = CreateCurve(session, [Vec3.Zero, new Vec3(40, 0, 0)]);
        var extend = Begin(session, () => new ExtendCurveCommand());
        Pick(extend, "Curve (a single segment)", curve);
        var end = Assert.Single(extend.Handles);                      // default extends the end only
        AssertPoint(new Vec3(40, 0, 0), end.Origin, 1e-6, "End arrow base");
        SetChoice(extend, "Extend", "Both ends");
        var hs = extend.Handles;
        Assert.Equal(2, hs.Count);
        AssertPoint(Vec3.Zero, hs[0].Origin, 1e-6, "Start-end arrow base");
        Assert.True(Vec3.Dot(hs[0].Dir, Vec3.UnitX) < -0.99, "Start arrow points away from the curve");
    }

    // ------------------------------------------------------------------ warp and deform

    [AvaloniaFact(DisplayName = "HDL-H007 | Warp (bend) shows the amount arrow plus start/end region arrows")]
    [Trait("TestId", "HDL-H007")]
    public void WarpBendHandles()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 40, 20, 20);
        var warp = Begin(session, () => new WarpCommand());
        Pick(warp, "What to warp", box);
        var hs = warp.Handles;
        Assert.Equal(3, hs.Count);   // bend arrow + start + end
        Assert.True(Vec3.Dot(At(hs, "Angle").Dir, Vec3.UnitZ) > 0.99, "Bend arrow points toward the bend side");
        Assert.True(Vec3.Dot(At(hs, "Warp starts at").Dir, Vec3.UnitX) > 0.99, "Start marker slides along X");
        Assert.True(Vec3.Dot(At(hs, "Warp ends at").Dir, Vec3.UnitX) > 0.99, "End marker slides along X");
    }

    [AvaloniaFact(DisplayName = "HDL-H008 | Warp (twist) shows a turning ring on the axis")]
    [Trait("TestId", "HDL-H008")]
    public void WarpTwistRing()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 40, 20, 20);
        var warp = Begin(session, () => new WarpCommand());
        Pick(warp, "What to warp", box);
        SetChoice(warp, "Warp", "Twist");
        var ring = Assert.Single(warp.Handles, h => h.Ref != null);
        Assert.True(ring.Radius > 0, "Twist handle is a ring of the part's radius");
        Assert.True(Vec3.Dot(ring.Dir, Vec3.UnitX) > 0.99, "Ring turns about the warp axis");
    }

    // ------------------------------------------------------------------ profile-based partials

    [AvaloniaFact(DisplayName = "HDL-H009 | Extrude shows the depth arrow plus a taper ring")]
    [Trait("TestId", "HDL-H009")]
    public void ExtrudeTaperRing()
    {
        var session = new CadSession();
        var rectangle = CreateRectangleSketch(session, Plane3.XY, width: 30, height: 20);
        var extrude = StartCommand(session, rectangle, () => new ExtrudeCommand());
        var hs = extrude.Handles;
        Assert.True(hs.Count >= 2, "Depth arrow plus taper ring");
        Assert.NotNull(hs.SingleOrDefault(h => h.Ref != null && h.Param.Label == "Taper angle"));
    }

    [AvaloniaFact(DisplayName = "HDL-H010 | Revolve in symmetric extent drags the total angle")]
    [Trait("TestId", "HDL-H010")]
    public void RevolveSymmetricAngle()
    {
        var session = new CadSession();
        var rectangle = CreateRectangleSketch(session, Plane3.XZ, width: 20, height: 30, startU: 10);   // off the axis so a radial exists
        var revolve = StartCommand(session, rectangle, () => new RevolveCommand());
        SetChoice(revolve, "Extent", "Symmetric");
        var h = Assert.Single(revolve.Handles);
        Assert.Equal("Angle", h.Param.Label);
    }

    [AvaloniaFact(DisplayName = "HDL-H011 | Fillet with an end radius shows a second arrow at the edge's end")]
    [Trait("TestId", "HDL-H011")]
    public void FilletEndRadiusArrow()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 40, 40, 40);
        var fillet = Begin(session, () => new FilletCommand());
        Pick(fillet, "Edges", EdgesWhere(box, p => true).First());
        Assert.Single(fillet.Handles);
        SetNumber(fillet, "End radius (0 = same as radius)", 5);
        Assert.Equal(2, fillet.Handles.Count);
    }

    // ------------------------------------------------------------------ direct edits

    [AvaloniaFact(DisplayName = "HDL-H012 | Move Face moves with the gizmo and keeps a tilt ring handle")]
    [Trait("TestId", "HDL-H012")]
    public void MoveFaceHandles()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 40, 40, 40);
        var move = Begin(session, () => new MoveFaceCommand());
        Pick(move, "Flat faces to move", FaceFacing(box, Vec3.UnitZ));
        var hs = move.Handles;
        Assert.Single(hs);   // the tilt ring; dx/dy/dz are the gizmo
        Assert.NotNull(hs.SingleOrDefault(h => h.Param.Label == "Tilt angle" && h.Ref != null));
        var gizmo = Assert.IsType<GizmoSpec>(move.Gizmo);
        AssertPoint(new Vec3(20, 20, 40), gizmo.Center, 1e-6, "Gizmo sits on the face");
        Assert.Equal("Move Z", gizmo.Move[2]!.Label);
    }

    [AvaloniaFact(DisplayName = "HDL-H013 | Move Hole moves with the gizmo and keeps a diameter arrow on the wall")]
    [Trait("TestId", "HDL-H013")]
    public void MoveHoleDiameterArrow()
    {
        var session = new CadSession();
        var plate = session.AddObject(Occ.Cut(BoxAt(new Vec3(-30, -30, 0), 60, 60, 10), CylinderAt(new Vec3(0, 0, -5), 5, 20)), "Plate", ObjKind.Solid);
        var hole = FeatureFinder.Features(plate).Single(f => f.Kind == FeatureKind.Hole);
        var move = Begin(session, () => new MoveHoleCommand());
        Pick(move, "A hole or boss (click its round face)", new SelItem(plate.Id, SelKind.Face, hole.Faces[0]));
        var hs = move.Handles;
        Assert.Single(hs);
        Assert.Equal(2, At(hs, "New diameter (0 = keep)").Mul);   // the tip sits at radius = dia/2
        var gizmo = Assert.IsType<GizmoSpec>(move.Gizmo);
        AssertPoint(new Vec3(0, 0, 5), gizmo.Center, 0.5, "Gizmo sits on the hole's centre");
    }

    [AvaloniaFact(DisplayName = "HDL-H014 | Edit Feature in Resize shows a diameter arrow for a round feature")]
    [Trait("TestId", "HDL-H014")]
    public void FeatureEditResizeArrow()
    {
        var session = new CadSession();
        var plate = session.AddObject(Occ.Cut(BoxAt(new Vec3(-30, -30, 0), 60, 60, 10), CylinderAt(new Vec3(0, 0, -5), 5, 20)), "Plate", ObjKind.Solid);
        var hole = FeatureFinder.Features(plate).Single(f => f.Kind == FeatureKind.Hole);
        var edit = Begin(session, () => new FeatureEditCommand());
        Pick(edit, "Feature to edit (click a hole, slot, pocket, boss or fillet)", new SelItem(plate.Id, SelKind.Face, hole.Faces[0]));
        SetChoice(edit, "Action", "Resize");
        var h = Assert.Single(edit.Handles);
        Assert.Equal("New diameter (0 = keep)", h.Param.Label);
        Assert.Equal(2, h.Mul);
    }

    // ------------------------------------------------------------------ assembly

    [AvaloniaFact(DisplayName = "HDL-H015 | Move Component is driven by the shared gizmo: three arrows and three rings")]
    [Trait("TestId", "HDL-H015")]
    public void MoveComponentHandles()
    {
        var session = new CadSession();
        var part = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        session.MakePart(part.Id);
        var move = Begin(session, () => new MoveComponentCommand());
        Pick(move, "Component", session.Find(part.Id)!);
        Assert.Empty(move.Handles);
        var gizmo = Assert.IsType<GizmoSpec>(move.Gizmo);
        Assert.All(gizmo.Move, p => Assert.NotNull(p)); Assert.All(gizmo.Turn, p => Assert.NotNull(p));
        AssertPoint(new Vec3(10, 10, 10), gizmo.Center, 1e-9, "Gizmo sits on the component's centre");
        gizmo.Move[1]!.Value = 5;
        AssertPoint(new Vec3(10, 15, 10), move.Gizmo!.Center, 1e-9, "Gizmo rides on the typed move");
    }

    [AvaloniaFact(DisplayName = "HDL-H024 | Arrows whose value is 0 keep a grabbable stub along their own axis")]
    [Trait("TestId", "HDL-H024")]
    public void ZeroValueArrowsKeepAStub()
    {
        var session = new CadSession();
        // Move Hole's diameter arrow starts at 0 (= keep): it must still show a stub to pull
        var plate = session.AddObject(Occ.Cut(BoxAt(new Vec3(-30, -30, 0), 60, 60, 10), CylinderAt(new Vec3(0, 0, -5), 5, 20)), "Plate", ObjKind.Solid);
        var hole = FeatureFinder.Features(plate).Single(f => f.Kind == FeatureKind.Hole);
        var move = Begin(session, () => new MoveHoleCommand());
        Pick(move, "A hole or boss (click its round face)", new SelItem(plate.Id, SelKind.Face, hole.Faces[0]));
        var dia = At(move.Handles, "New diameter (0 = keep)");
        Assert.Equal(0, dia.Param.Value);
        // the tip collapses onto the origin, the grip does not: it sits a fixed number of pixels along the arrow
        AssertPoint(dia.Origin, CadSession.HandleTip(dia), 1e-9, "Zero-value tip");
        double stub = session.Camera.PixelSizeAt(dia.Origin) * CadSession.HandleStubPixels;
        AssertPoint(dia.Origin + dia.Dir.Normalized() * stub, session.HandleGrip(dia), 1e-9, "Stub grip");
        // a handle with a real value keeps its tip as the grip
        dia.Param.Value = 16;
        AssertPoint(CadSession.HandleTip(dia), session.HandleGrip(dia), 1e-9, "Non-zero grip is the tip");
        Assert.True((session.HandleGrip(dia) - dia.Origin).Length > stub, "A real value lies past the stub");
    }

    [AvaloniaFact(DisplayName = "HDL-H022 | Dragging a zero-value arrow moves the value by exactly how far the pointer travelled")]
    [Trait("TestId", "HDL-H022")]
    public void DraggingAZeroArrowIsRelative()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        var move = Begin(session, () => new MoveFaceCommand());
        Pick(move, "Flat faces to move", FaceFacing(body, Vec3.UnitZ));
        SetChoice(move, "Tilt about", "World X");
        var tilt = At(move.Handles, "Tilt angle");
        Assert.NotNull(tilt.Ref);
        // the tilt ring is a feature handle; the move is the gizmo. Drag the gizmo's Y arrow by 10 mm.
        var gizmo = Assert.IsType<GizmoSpec>(move.Gizmo);
        double len = GizmoVisual.Length(session, gizmo.Center);
        var grip = gizmo.Center + Vec3.UnitY * len * 0.6;
        var a = session.Camera.Project(grip)!.Value; var b = session.Camera.Project(grip + Vec3.UnitY * 10)!.Value;
        session.PointerDown(new PointerInfo(a.x, a.y, true, false, false, false));
        session.PointerMove(new PointerInfo(b.x, b.y, true, false, false, true));     // Alt: no snapping
        session.PointerUp(new PointerInfo(b.x, b.y, false, false, false, true));
        AssertClose(10, gizmo.Move[1]!.Value, 0.05, "Move Y after a 10 mm drag along +Y");
        Assert.Equal(0, gizmo.Move[2]!.Value);
        AssertPoint(gizmo.Center + Vec3.UnitY * 10, move.Gizmo!.Center, 0.05, "The gizmo rides with the face");
    }

    [AvaloniaFact(DisplayName = "HDL-H023 | Transform Move, Rotate and Scale are driven by the shared gizmo; pressing a ring chooses the turn axis")]
    [Trait("TestId", "HDL-H023")]
    public void TransformUsesTheGizmo()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        var move = Begin(session, () => new TransformCommand(TransformCommand.Kind.Move));
        Pick(move, "Objects", body);
        Assert.Empty(move.Handles);
        var g = Assert.IsType<GizmoSpec>(move.Gizmo);
        AssertPoint(new Vec3(20, 10, 10), g.Center, 1e-9, "Move gizmo sits at the centre plus the default Δ X of 10");
        session.CancelCommand();
        var rotate = Begin(session, () => new TransformCommand(TransformCommand.Kind.Rotate));
        Pick(rotate, "Objects", body);
        g = Assert.IsType<GizmoSpec>(rotate.Gizmo);
        Assert.False(g.Supports(0)); Assert.True(g.Supports(1));
        Assert.All(g.Turn, ring => Assert.Equal("Angle", ring!.Label));   // a ring per axis, all turning the one angle
        var axis = rotate.Params.OfType<ChoiceParam>().Single(p => p.Label == "Axis");
        Assert.Equal(2, axis.Index);                                       // Z until a ring says otherwise
        Assert.Equal(45, g.Turn[2]!.Value);
        // press the X ring: the tool turns about X from then on, starting from no turn
        var (spec, length, _) = session.CommandGizmoInfo!.Value;
        var onRing = session.Camera.Project(spec.Center + (Vec3.UnitY + Vec3.UnitZ).Normalized() * length * .8)!.Value;
        session.PointerDown(new PointerInfo(onRing.x, onRing.y, true, false, false, false));
        session.PointerUp(new PointerInfo(onRing.x, onRing.y, false, false, false, false));
        Assert.Equal(0, axis.Index);
        Assert.Equal(0, g.Turn[0]!.Value);
        // pressing the ring of the axis already chosen keeps the angle
        SetNumber(rotate, "Angle", 30);
        session.PointerDown(new PointerInfo(onRing.x, onRing.y, true, false, false, false));
        session.PointerUp(new PointerInfo(onRing.x, onRing.y, false, false, false, false));
        Assert.Equal(30, g.Turn[0]!.Value);
        session.CancelCommand();
        var scale = Begin(session, () => new TransformCommand(TransformCommand.Kind.Scale));
        Pick(scale, "Objects", body);
        g = Assert.IsType<GizmoSpec>(scale.Gizmo);
        Assert.True(g.Supports(2)); Assert.Equal("Factor", g.Scale!.Label);
    }

    [AvaloniaFact(DisplayName = "HDL-H026 | Typed numbers, Tab and W / E / R work on the gizmo; only the engaged axis is labelled")]
    [Trait("TestId", "HDL-H026")]
    public void GizmoTakesTypedNumbers()
    {
        var session = new CadSession();
        var part = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        session.MakePart(part.Id);
        var move = Begin(session, () => new MoveComponentCommand());
        Pick(move, "Component", session.Find(part.Id)!);
        var g = Assert.IsType<GizmoSpec>(move.Gizmo);
        var atRest = new Overlay(); session.BuildOverlay(atRest);

        // hovering an arrow labels that arrow and nothing else
        var (spec, length, mode) = session.CommandGizmoInfo!.Value;
        Assert.Equal(0, mode);
        var onY = session.Camera.Project(spec.Center + Vec3.UnitY * length * .7)!.Value;
        session.PointerMove(new PointerInfo(onY.x, onY.y, false, false, false, false));
        var hovering = new Overlay(); session.BuildOverlay(hovering);
        Assert.Equal(atRest.Labels.Count + 1, hovering.Labels.Count);

        // a typed number sets the engaged axis; Tab steps to the next one
        Assert.True(session.Key("Char:1")); Assert.True(session.Key("Char:5"));
        Assert.Equal(15, g.Move[1]!.Value); Assert.Equal(0, g.Move[0]!.Value);
        var typing = new Overlay(); session.BuildOverlay(typing);
        Assert.Contains(typing.Labels, l => l.Text.StartsWith("15_") && l.Text.Contains("Enter to apply"));
        Assert.True(session.Key("Tab")); Assert.True(session.Key("Char:7"));
        Assert.Equal(7, g.Move[2]!.Value); Assert.Equal(15, g.Move[1]!.Value);
        Assert.True(session.Key("Minus"));
        Assert.Equal(-7, g.Move[2]!.Value);
        Assert.True(session.Key("Enter"));

        // E switches to the rings, and the next number is an angle
        Assert.True(session.Key("Char:E"));
        Assert.Equal(1, session.CommandGizmoMode);
        Assert.True(session.Key("Char:9")); Assert.True(session.Key("Char:0"));
        Assert.Equal(90, g.Turn[0]!.Value);
        Assert.Equal(-7, g.Move[2]!.Value);
        Assert.True(session.Key("Char:W"));
        Assert.Equal(0, session.CommandGizmoMode);
        Assert.False(session.Key("Char:R"));          // Move Component has no scale
    }

    [AvaloniaFact(DisplayName = "HDL-H027 | Direct Edit moves with the gizmo; the pull arrow stands in for the gizmo arrow along the face normal")]
    [Trait("TestId", "HDL-H027")]
    public void DirectEditUsesTheGizmo()
    {
        const string slot = "Click a face or feature (a hole, slot, pocket, boss or fillet)";
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 40, 40, 40);
        var edit = Begin(session, () => new DirectEditCommand());
        Pick(edit, slot, FaceFacing(body, Vec3.UnitZ));
        Assert.Equal(2, edit.Handles.Count);                    // pull and tilt: the three move arrows are the gizmo now
        var pull = At(edit.Handles, "Pull (positive moves faces out)");
        Assert.True(Vec3.Dot(pull.Dir.Normalized(), Vec3.UnitZ) > 0.99, "Pull arrow along the face normal");
        Assert.NotNull(At(edit.Handles, "Tilt").Ref);
        var g = Assert.IsType<GizmoSpec>(edit.Gizmo);
        AssertPoint(new Vec3(20, 20, 40), g.Center, 1e-6, "Gizmo sits in the middle of the face");
        AssertPoint(new Vec3(20, 20, 40), pull.Origin, 1e-6, "Pull arrow leaves the middle of the face");
        Assert.Equal("Move X", g.Move[0]!.Label); Assert.Equal("Move Y", g.Move[1]!.Label);
        Assert.Null(g.Move[2]);                                 // the pull arrow already leaves this spot along +Z
        Assert.False(g.Supports(1));

        // a number typed after touching the gizmo moves the face; it does not pull it
        double length = session.CommandGizmoInfo!.Value.length;
        var onX = session.Camera.Project(g.Center + Vec3.UnitX * length * .7)!.Value;
        session.PointerMove(new PointerInfo(onX.x, onX.y, false, false, false, false));
        Assert.True(session.Key("Char:7"));
        Assert.Equal(7, g.Move[0]!.Value); Assert.Equal(0, pull.Param.Value);
        // once the face is moving the pull arrow is gone, so the gizmo offers all three directions
        Assert.DoesNotContain(edit.Handles, h => h.Param.Label.StartsWith("Pull"));
        var moving = Assert.IsType<GizmoSpec>(edit.Gizmo);
        Assert.NotNull(moving.Move[2]);
        AssertPoint(new Vec3(27, 20, 40), moving.Center, 1e-6, "Gizmo rides with the move");
        session.CancelCommand();

        // a number typed after touching the pull arrow pulls the face; it does not move it
        edit = Begin(session, () => new DirectEditCommand());
        Pick(edit, slot, FaceFacing(body, Vec3.UnitZ));
        pull = At(edit.Handles, "Pull (positive moves faces out)");
        var onPull = session.Camera.Project(session.HandleGrip(pull))!.Value;
        session.PointerMove(new PointerInfo(onPull.x, onPull.y, false, false, false, false));
        Assert.True(session.Key("Char:3"));
        Assert.Equal(3, pull.Param.Value);
        Assert.Equal(0, edit.Gizmo!.Move[0]!.Value);
    }

    [AvaloniaFact(DisplayName = "HDL-H028 | Component Pattern sets its spacing and its axis point with the gizmo")]
    [Trait("TestId", "HDL-H028")]
    public void ComponentPatternUsesTheGizmo()
    {
        var session = new CadSession();
        var part = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        session.MakePart(part.Id);
        var pattern = Begin(session, () => new ComponentPatternCommand());
        Pick(pattern, "Component to repeat", session.Find(part.Id)!);
        Assert.Empty(pattern.Handles);
        var g = Assert.IsType<GizmoSpec>(pattern.Gizmo);
        Assert.Equal("Spacing X", g.Move[0]!.Label); Assert.Equal("Spacing Z", g.Move[2]!.Label);
        AssertPoint(new Vec3(30, 10, 10), g.Center, 1e-9, "Gizmo sits where the first copy lands: the component's centre plus one spacing");
        SetChoice(pattern, "Pattern", "Circular");
        var ring = Assert.Single(pattern.Handles);
        Assert.Equal("Angle between copies", ring.Param.Label); Assert.NotNull(ring.Ref);
        g = Assert.IsType<GizmoSpec>(pattern.Gizmo);
        Assert.Equal("Axis passes through X", g.Move[0]!.Label);
        AssertPoint(Vec3.Zero, g.Center, 1e-9, "Gizmo sits on the point the axis passes through");
        SetNumber(pattern, "Axis passes through Y", 12);
        AssertPoint(new Vec3(0, 12, 0), pattern.Gizmo!.Center, 1e-9, "Gizmo rides with the axis point");
    }

    [AvaloniaFact(DisplayName = "HDL-H029 | A turning handle without stops is a ring, grabbable anywhere, that turns by how far the pointer went round")]
    [Trait("TestId", "HDL-H029")]
    public void TurningRingDragsRelatively()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 40, 20, 20);
        var warp = Begin(session, () => new WarpCommand());
        Pick(warp, "What to warp", box);
        SetChoice(warp, "Warp", "Twist");
        var ring = Assert.Single(warp.Handles, h => h.Ref != null);
        Assert.Equal((0d, 360d), CadSession.TurnTrack(ring));       // -360..360 is more than a turn: the whole circle
        double start = ring.Param.Value;
        Vec3 OnRing(double degrees) => ring.Origin + JointVisuals.Rotate(ring.Ref!.Value.Normalized(), ring.Dir.Normalized(), degrees) * ring.Radius;
        // press the ring a quarter turn away from the grip, then go 30 degrees further round
        var a = session.Camera.Project(OnRing(start + 90))!.Value; var b = session.Camera.Project(OnRing(start + 120))!.Value;
        session.PointerDown(new PointerInfo(a.x, a.y, true, false, false, false));
        Assert.Equal(start, ring.Param.Value);                      // the press alone changes nothing
        session.PointerMove(new PointerInfo(b.x, b.y, true, false, false, true));     // Alt: no snapping
        session.PointerUp(new PointerInfo(b.x, b.y, false, false, false, true));
        AssertClose(start + 30, ring.Param.Value, 0.05, "Twist angle after going 30 degrees round the ring (degrees)");

        // a turning handle with stops keeps its arc: the track is just the stretch it may take
        session.CancelCommand();
        var body = CreateBoxBody(session, new Vec3(100, 0, 0), 20, 20, 20);
        var move = Begin(session, () => new MoveFaceCommand());
        Pick(move, "Flat faces to move", FaceFacing(body, Vec3.UnitZ));
        var tilt = At(move.Handles, "Tilt angle");
        var (from, to) = CadSession.TurnTrack(tilt);
        Assert.True(to - from < 360, "A tilt with stops covers less than a turn");
    }

    [AvaloniaFact(DisplayName = "HDL-H030 | Where a size arrow's grip and the gizmo overlap, the grip takes the press; elsewhere the gizmo does")]
    [Trait("TestId", "HDL-H030")]
    public void SizeGripBeatsTheGizmo()
    {
        var session = new CadSession();
        session.Camera.SetView(0, 90);        // from the top the cylinder's radius arrow lies under the gizmo's X arrow
        var cyl = Begin(session, () => new CylinderCommand());
        WithGizmo(cyl);
        double length = session.CommandGizmoInfo!.Value.length;
        SetNumber(cyl, "Radius", Math.Round(length * .6, 3));
        var radius = At(cyl.Handles, "Radius"); double r0 = radius.Param.Value;
        var gizmo = Assert.IsType<GizmoSpec>(cyl.Gizmo);

        // on the gizmo's arrow, over the radius arrow's shaft but away from its grip: the gizmo moves the cylinder
        var onArrow = gizmo.Center + Vec3.UnitX * length * .3;
        var a = session.Camera.Project(onArrow)!.Value; var b = session.Camera.Project(onArrow + Vec3.UnitX * 10)!.Value;
        session.PointerDown(new PointerInfo(a.x, a.y, true, false, false, false));
        session.PointerMove(new PointerInfo(b.x, b.y, true, false, false, true));
        session.PointerUp(new PointerInfo(b.x, b.y, false, false, false, true));
        AssertClose(10, gizmo.Move[0]!.Value, 0.05, "Position X after dragging the gizmo arrow 10 mm");
        AssertClose(r0, radius.Param.Value, 1e-9, "Radius is untouched by the gizmo drag");

        // on the radius arrow's own grip, which is also on the gizmo's arrow: the grip sets the radius
        radius = At(cyl.Handles, "Radius");
        var grip = session.HandleGrip(radius);
        a = session.Camera.Project(grip)!.Value; b = session.Camera.Project(grip + Vec3.UnitX * 5)!.Value;
        session.PointerDown(new PointerInfo(a.x, a.y, true, false, false, false));
        session.PointerMove(new PointerInfo(b.x, b.y, true, false, false, true));
        session.PointerUp(new PointerInfo(b.x, b.y, false, false, false, true));
        AssertClose(r0 + 5, radius.Param.Value, 0.05, "Radius after dragging its grip 5 mm");
        AssertClose(10, gizmo.Move[0]!.Value, 0.05, "Position X is untouched by the grip drag");
    }

    [AvaloniaFact(DisplayName = "HDL-H031 | A turning handle with stops follows the pointer, stops at each limit and says so")]
    [Trait("TestId", "HDL-H031")]
    public void LimitedTurningHandleStopsAtItsLimits()
    {
        var session = new CadSession();
        SceneObject Part(string name, double x, bool ground)
        {
            var o = session.AddObject(Occ.Translate(Occ.Box(10, 10, 10), new Vec3(x, 0, 0)), name, ObjKind.Solid);
            session.Update(o.Id, v => v.WithAsm(new ComponentInfo(CadSession.NewKey(), name, null, Xform.Identity.ToArray(), ground), v.Shape), false);
            return session.Find(o.Id)!;
        }
        var hub = Part("Hub", 0, true); var arm = Part("Arm", 20, false);
        var frame = JointInfo.Pack(Xform.Identity);
        var hinge = session.AddObject(Occ.Line(Vec3.Zero, new Vec3(0, 0, 0.01)), "Hinge", ObjKind.Joint);
        session.Update(hinge.Id, v => v.WithAsm(new JointInfo("hinge", "Hinge", JointType.Revolute, hub.Component!.Key, arm.Component!.Key, frame, frame, false, 0, 0, 0, true, -45, 90, 0, 0)), false);
        session.Camera.Width = 800; session.Camera.Height = 600; session.Camera.SetView(0, 90);

        session.StartCommand(() => new DriveJointsCommand());
        var drive = session.ActiveCommand!;
        var handle = Assert.Single(drive.Handles);
        Assert.NotNull(handle.Ref);
        Assert.Equal((-45d, 90d), CadSession.TurnTrack(handle));     // the arc is just the stretch the joint may take
        (double x, double y) Screen(double degrees)
        {
            var h = drive.Handles.Single();
            var p = session.Camera.Project(h.Origin + JointVisuals.Rotate(h.Ref!.Value.Normalized(), h.Dir, degrees) * h.Radius)!.Value;
            return (p.x, p.y);
        }
        void MoveTo(double degrees) { var (x, y) = Screen(degrees); session.PointerMove(new PointerInfo(x, y, true, false, false, false)); }
        var grab = Screen(0);
        session.PointerDown(new PointerInfo(grab.x, grab.y, true, false, false, false));
        MoveTo(60);
        AssertClose(60, drive.Handles.Single().Param.Value, 1.5, "Joint angle with the pointer 60 degrees round (degrees)");
        MoveTo(170);
        Assert.Equal(90, drive.Handles.Single().Param.Value);       // past the upper stop
        MoveTo(0); MoveTo(-120);                                    // a drag passes through the angles on the way
        Assert.Equal(-45, drive.Handles.Single().Param.Value);      // past the lower stop
        var atLimit = new Overlay(); session.BuildOverlay(atLimit);
        Assert.Contains(atLimit.Labels, l => l.Text.Contains("limit"));
        Assert.Contains(atLimit.Dots, d => d.Color == JointVisuals.HitColor);
        var end = Screen(-120);
        session.PointerUp(new PointerInfo(end.x, end.y, false, false, false, false));
    }

    // ------------------------------------------------------------------ meshes and sections

    [AvaloniaFact(DisplayName = "HDL-H016 | Section drags its offset along the cut normal")]
    [Trait("TestId", "HDL-H016")]
    public void SectionOffsetArrow()
    {
        var session = new CadSession();
        CreateBoxBody(session, Vec3.Zero, 40, 40, 40);
        var section = Begin(session, () => new SectionCommand());
        var h = section.Handle;
        Assert.NotNull(h);
        Assert.Equal("Offset", h.Param.Label);
        Assert.True(Vec3.Dot(h.Dir, Vec3.UnitZ) > 0.99, "Offset arrow along the XY-plane normal");
    }

    [AvaloniaFact(DisplayName = "HDL-H017 | Split Mesh drags the plane along its normal; Mesh Section adds a spacing arrow")]
    [Trait("TestId", "HDL-H017")]
    public void MeshCutAndSectionArrows()
    {
        var session = new CadSession();
        AddMesh(session, SphereMesh(20));
        var cut = Begin(session, () => new MeshCutCommand());
        var h = cut.Handle;
        Assert.NotNull(h);
        Assert.Equal("Position along the normal", h.Param.Label);
        Assert.True(Vec3.Dot(h.Dir, Vec3.UnitZ) > 0.99, "Cut arrow along the chosen plane normal");

        var sections = Begin(session, () => new MeshSectionCommand());
        Assert.Single(sections.Handles);
        SetInteger(sections, "Number of sections", 3);
        Assert.Equal(2, sections.Handles.Count);
        Assert.Equal("Spacing", sections.Handles[1].Param.Label);
    }

    [AvaloniaFact(DisplayName = "HDL-H018 | Offset Mesh shows a grow/shrink arrow")]
    [Trait("TestId", "HDL-H018")]
    public void MeshOffsetArrow()
    {
        var session = new CadSession();
        AddMesh(session, SphereMesh(20));
        var offset = Begin(session, () => new MeshOffsetCommand());
        var h = offset.Handle;
        Assert.NotNull(h);
        Assert.Equal("Offset (+ grows, - shrinks)", h.Param.Label);
    }

    // ------------------------------------------------------------------ freeform

    [AvaloniaFact(DisplayName = "HDL-H019 | Freeform Extrude shows distance and end-scale arrows")]
    [Trait("TestId", "HDL-H019")]
    public void FormExtrudeArrows()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 20, height: 20);
        var form = Begin(session, () => new FormExtrudeCommand());
        Pick(form, "Profile (curve or sketch)", profile);
        var hs = form.Handles;
        Assert.Equal(2, hs.Count);
        Assert.Equal("Distance", hs[0].Param.Label);
        Assert.Equal("Scale at the far end", hs[1].Param.Label);
    }

    [AvaloniaFact(DisplayName = "HDL-H020 | Freeform Revolve drags the sweep angle on a ring")]
    [Trait("TestId", "HDL-H020")]
    public void FormRevolveRing()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XZ, width: 10, height: 20, startU: 15);
        var form = Begin(session, () => new FormRevolveCommand());
        Pick(form, "Profile (curve or sketch)", profile);
        var h = Assert.Single(form.Handles);
        Assert.Equal("Angle (degrees)", h.Param.Label);
        Assert.NotNull(h.Ref);
        Assert.True(h.Radius > 0, "Angle ring of the profile's radius");
    }

    // ------------------------------------------------------------------ label policy

    [AvaloniaFact(DisplayName = "HDL-H021 | Handle labels appear only for the engaged handle")]
    [Trait("TestId", "HDL-H021")]
    public void HandleLabelsEngagementGated()
    {
        var session = new CadSession();
        session.Camera.SetView(-35, 28);
        var box = Begin(session, () => new BoxCommand());
        WithGizmo(box);
        var atRest = new Overlay();
        session.BuildOverlay(atRest);
        Assert.DoesNotContain(atRest.Labels, l => l.Text.Contains("mm") || l.Text.Contains('°'));

        var h = At(box.Handles, "Height (Z)");
        var tip = h.Origin + h.Dir.Normalized() * (h.Param.Value / h.Mul);
        var pr = session.Camera.Project(tip);
        Assert.True(pr != null, "Height arrow tip projects on screen");
        session.PointerMove(new PointerInfo(pr.Value.x, pr.Value.y, false, false, false, false));
        var hovering = new Overlay();
        session.BuildOverlay(hovering);
        Assert.Contains(hovering.Labels, l => l.Text.Contains($"{h.Param.Value:0.##}"));
    }
}
