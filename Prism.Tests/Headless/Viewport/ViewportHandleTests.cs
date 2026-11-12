using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
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

    [AvaloniaFact(DisplayName = "HDL-H001 | Box shows three dimension arrows and the shared gizmo for its position")]
    [Trait("TestId", "HDL-H001")]
    public void BoxDimensionArrows()
    {
        var session = new CadSession();
        var box = Begin(session, () => new BoxCommand());
        var hs = box.Handles;
        Assert.Equal(3, hs.Count);
        var gizmo = Assert.IsType<GizmoSpec>(box.Gizmo);
        Assert.Equal("Position X", gizmo.Move[0]!.Label); Assert.Equal("Position Z", gizmo.Move[2]!.Label);
        Assert.True(gizmo.Supports(0)); Assert.False(gizmo.Supports(1));
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
        var hs = cyl.Handles;
        Assert.Equal(2, hs.Count);   // radius + height; the position is the gizmo
        Assert.True(Vec3.Dot(At(hs, "Radius").Dir, Vec3.UnitX) > 0.99, "Radius arrow points radially");
        Assert.True(Vec3.Dot(At(hs, "Height").Dir, Vec3.UnitZ) > 0.99, "Height arrow along +Z");
    }

    [AvaloniaFact(DisplayName = "HDL-H003 | Cone anchors the top-radius arrow at the top of the cone")]
    [Trait("TestId", "HDL-H003")]
    public void ConeTopRadiusAnchor()
    {
        var session = new CadSession();
        var cone = Begin(session, () => new ConeCommand());
        var top = At(cone.Handles, "Top radius");
        AssertClose(30, top.Origin.Z, 1e-9, "Top radius arrow height (mm)");
        var torus = Begin(session, () => new TorusCommand());
        var minor = At(torus.Handles, "Minor radius");
        AssertClose(20, minor.Origin.X, 1e-9, "Minor radius arrow on the ring's centre line (mm)");
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

    [AvaloniaFact(DisplayName = "HDL-H021 | Arrows whose value is 0 keep a grabbable stub along their own axis")]
    [Trait("TestId", "HDL-H021")]
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

    [AvaloniaFact(DisplayName = "HDL-H023 | Transform Move, Rotate and Scale are driven by the shared gizmo, with one ring for the chosen axis")]
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
        Assert.Null(g.Turn[0]); Assert.Null(g.Turn[1]); Assert.Equal("Angle", g.Turn[2]!.Label);   // default axis Z
        session.CancelCommand();
        var scale = Begin(session, () => new TransformCommand(TransformCommand.Kind.Scale));
        Pick(scale, "Objects", body);
        g = Assert.IsType<GizmoSpec>(scale.Gizmo);
        Assert.True(g.Supports(2)); Assert.Equal("Factor", g.Scale!.Label);
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
