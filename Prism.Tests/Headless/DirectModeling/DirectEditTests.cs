using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>
/// Characterizes the history-free direct edits not covered by <see cref="FaceEditTests"/>: Push / Pull, Move Hole / Boss,
/// Delete Face, Offset Face, Rotate Face, Move Face, Remove Fillets, Move Body, Mirror Feature and Check &amp; Repair.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/direct-modeling/README.md">Direct Modeling guide.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public class DirectEditTests
{
    /// <summary>A 40 × 20 × 10 plate with one Ø6 through-hole whose axis is vertical at (10, 10).</summary>
    static SceneObject DrilledPlate(CadSession session)
    {
        using var plate = BoxAt(Vec3.Zero, 40, 20, 10);
        using var drill = CylinderAt(new Vec3(10, 10, -1), 3, 12);
        return session.AddObject(Occ.Cut(plate, drill), "Drilled plate", ObjKind.Solid);
    }

    static SelItem HoleWall(SceneObject body) => new(body.Id, SelKind.Face,
        Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(i =>
        {
            using var face = body.Shape.Sub(SubKind.Face, i);
            return Occ.FaceInfoOf(face).Type == "Cylinder";
        }));

    static double HoleVolume(double radius) => Math.PI * radius * radius * 10;

    static Shape Replaced(Command command, SceneObject body)
    {
        var (old, shape) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(body.Id, old.Id);
        AssertValidSolid(shape);
        return shape;
    }

    static double[] HoleCentresX(CadSession session, Shape shape)
    {
        var body = session.AddObject(shape, "Result", ObjKind.Solid);
        return FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Hole).Select(f => Math.Round(f.Centre.X, 3)).Order().ToArray();
    }

    // ------------------------------------------------------------------ Push / Pull

    [AvaloniaTheory(DisplayName = "DIR-H006 | Push / Pull moves the top face of a cube out or in by the signed distance")]
    [InlineData(5, 1500)]
    [InlineData(-4, 600)]
    [Trait("TestId", "DIR-H006")]
    public void PushPullTopFace(double distance, double volume)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new PushPullCommand());
        Pick(command, "Faces to push or pull", FaceFacing(box, Vec3.UnitZ));
        SetNumber(command, "Distance (positive pulls out, negative pushes in)", distance);
        var shape = Replaced(command, box);
        AssertNear(volume, shape.Props().Volume, "Pushed/pulled volume (mm³)");
        AssertNear(10 + distance, shape.Props().Max.Z, "New top height (mm)");
        Assert.Equal($"1  by {distance:0.##} mm", Result(command, "Faces moved"));
    }

    [AvaloniaFact(DisplayName = "DIR-H007 | Push / Pull with zero distance does nothing")]
    [Trait("TestId", "DIR-H007")]
    public void PushPullZeroDistance()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new PushPullCommand());
        Pick(command, "Faces to push or pull", FaceFacing(box, Vec3.UnitZ));
        SetNumber(command, "Distance (positive pulls out, negative pushes in)", 0);
        Assert.Null(command.TryBuild());
        Assert.Null(command.Error);
    }

    [AvaloniaFact(DisplayName = "DIR-H008 | Push / Pull sweeps a flat surface face into a new 300 mm³ solid")]
    [Trait("TestId", "DIR-H008")]
    public void PushPullSurfaceFace()
    {
        var session = new CadSession();
        using var loop = Occ.Polyline([Vec3.Zero, new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)], true);
        var sheet = session.AddObject(Occ.Profile([loop], true), "Sheet", ObjKind.Surface);
        var command = Begin(session, () => new PushPullCommand());
        Pick(command, "Faces to push or pull", new SelItem(sheet.Id, SelKind.Face, 0));
        SetNumber(command, "Distance (positive pulls out, negative pushes in)", 3);
        var output = BuildOutput(command);
        Assert.Empty(output.Replace);
        var solid = Assert.Single(output.Add);
        Assert.Equal(ObjKind.Solid, solid.Kind);
        AssertNear(300, Math.Abs(solid.Shape!.Props().Volume), "Swept volume (mm³)");
    }

    [AvaloniaTheory(DisplayName = "DIR-H009 | Push / Pull on a hole wall changes the hole radius by the distance")]
    [InlineData(1, 2)]
    [InlineData(-1, 4)]
    [Trait("TestId", "DIR-H009")]
    public void PushPullHoleWall(double distance, double newRadius)
    {
        var session = new CadSession();
        var plate = DrilledPlate(session);
        var command = Begin(session, () => new PushPullCommand());
        Pick(command, "Faces to push or pull", HoleWall(plate));
        SetNumber(command, "Distance (positive pulls out, negative pushes in)", distance);
        var shape = Replaced(command, plate);
        AssertClose(8000 - HoleVolume(newRadius), shape.Props().Volume, 1e-3, "Resized hole plate volume (mm³)");
    }

    // ------------------------------------------------------------------ Move Hole / Boss

    [AvaloniaFact(DisplayName = "DIR-H010 | Move Hole slides a Ø6 hole 15 mm along X and keeps the plate volume")]
    [Trait("TestId", "DIR-H010")]
    public void MoveHoleSlides()
    {
        var session = new CadSession();
        var plate = DrilledPlate(session);
        var command = Begin(session, () => new MoveHoleCommand());
        Pick(command, "A hole or boss (click its round face)", HoleWall(plate));
        SetNumber(command, "Move X", 15);
        var shape = Replaced(command, plate);
        AssertClose(8000 - HoleVolume(3), shape.Props().Volume, 1e-3, "Plate volume (mm³)");
        Assert.Equal([25.0], HoleCentresX(session, shape));
        Assert.Equal("Ø6  →  Ø6,  moved 15 mm", Result(command, "Hole"));
    }

    [AvaloniaFact(DisplayName = "DIR-H011 | Move Hole 'New diameter' resizes the hole in place")]
    [Trait("TestId", "DIR-H011")]
    public void MoveHoleResizes()
    {
        var session = new CadSession();
        var plate = DrilledPlate(session);
        var command = Begin(session, () => new MoveHoleCommand());
        Pick(command, "A hole or boss (click its round face)", HoleWall(plate));
        SetNumber(command, "New diameter (0 = keep)", 10);
        var shape = Replaced(command, plate);
        AssertClose(8000 - HoleVolume(5), shape.Props().Volume, 1e-3, "Plate volume (mm³)");
        Assert.Equal([10.0], HoleCentresX(session, shape));
    }

    [AvaloniaFact(DisplayName = "DIR-H012 | Move Hole with no move and no new diameter does nothing")]
    [Trait("TestId", "DIR-H012")]
    public void MoveHoleNoChange()
    {
        var session = new CadSession();
        var plate = DrilledPlate(session);
        var command = Begin(session, () => new MoveHoleCommand());
        Pick(command, "A hole or boss (click its round face)", HoleWall(plate));
        Assert.Null(command.TryBuild());
    }

    // ------------------------------------------------------------------ Delete Face

    [AvaloniaFact(DisplayName = "DIR-H013 | Delete Face removes a hole and the plate closes to its full 8000 mm³")]
    [Trait("TestId", "DIR-H013")]
    public void DeleteFaceRemovesHole()
    {
        var session = new CadSession();
        var plate = DrilledPlate(session);
        var command = Begin(session, () => new DeleteFaceCommand());
        Pick(command, "Faces to remove (a hole, fillet, pocket...)", HoleWall(plate));
        var shape = Replaced(command, plate);
        AssertClose(8000, shape.Props().Volume, 1e-3, "Healed volume (mm³)");
        Assert.Equal(6, shape.Count(SubKind.Face));
    }

    [AvaloniaFact(DisplayName = "DIR-H014 | Delete Face on a surface body leaves the other faces with a gap")]
    [Trait("TestId", "DIR-H014")]
    public void DeleteFaceOnSurfaceLeavesGap()
    {
        var session = new CadSession();
        using var box = BoxAt(Vec3.Zero, 10, 10, 10);
        var shell = session.AddObject(Occ.Sew(Enumerable.Range(0, 6).Select(i => box.Sub(SubKind.Face, i)).ToList(), 0.01, false), "Shell", ObjKind.Surface);
        var command = Begin(session, () => new DeleteFaceCommand());
        Pick(command, "Faces to remove (a hole, fillet, pocket...)", FaceFacing(shell, Vec3.UnitZ));
        var (old, rest) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(shell.Id, old.Id);
        Assert.Equal(5, rest.Count(SubKind.Face));
        AssertNear(500, rest.Props().Area, "Remaining area (mm²)");
        Assert.Equal("1", Result(command, "Faces removed"));
    }

    // ------------------------------------------------------------------ Offset / Rotate / Move Face

    [AvaloniaTheory(DisplayName = "DIR-H015 | Offset Face moves a flat face along its normal; positive grows the body")]
    [InlineData(2, 1200)]
    [InlineData(-3, 700)]
    [Trait("TestId", "DIR-H015")]
    public void OffsetFaceFlat(double distance, double volume)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new OffsetFaceCommand());
        Pick(command, "Faces to offset", FaceFacing(box, Vec3.UnitX));
        SetNumber(command, "Offset distance (positive moves faces out)", distance);
        var shape = Replaced(command, box);
        AssertNear(volume, shape.Props().Volume, "Offset volume (mm³)");
        AssertNear(10 + distance, shape.Props().Max.X, "Moved face position (mm)");
    }

    [AvaloniaFact(DisplayName = "DIR-H016 | Offset Face out on a hole wall makes the hole smaller")]
    [Trait("TestId", "DIR-H016")]
    public void OffsetFaceShrinksHole()
    {
        var session = new CadSession();
        var plate = DrilledPlate(session);
        var command = Begin(session, () => new OffsetFaceCommand());
        Pick(command, "Faces to offset", HoleWall(plate));
        SetNumber(command, "Offset distance (positive moves faces out)", 1);
        var shape = Replaced(command, plate);
        AssertClose(8000 - HoleVolume(2), shape.Props().Volume, 1e-3, "Plate volume (mm³)");
        Assert.Equal("wall moved 1 mm", Result(command, "Round features resized"));
    }

    [AvaloniaTheory(DisplayName = "DIR-H017 | Rotate Face tilts the top face about a world axis through its middle without changing volume")]
    [InlineData("World X")]
    [InlineData("World Y")]
    [Trait("TestId", "DIR-H017")]
    public void RotateFaceAboutWorldAxis(string axis)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new RotateFaceCommand());
        Pick(command, "Flat faces to rotate", FaceFacing(box, Vec3.UnitZ));
        SetChoice(command, "Rotate about", axis);
        SetNumber(command, "Rotation angle", 10);
        var shape = Replaced(command, box);
        AssertClose(1000, shape.Props().Volume, 1e-6, "Volume is kept by a symmetric tilt (mm³)");
        AssertClose(10 + 5 * Math.Tan(Math.PI / 18), shape.Props().Max.Z, 1e-6, "Raised edge height (mm)");
        Assert.Equal("1  by 10°", Result(command, "Faces rotated"));
    }

    [AvaloniaFact(DisplayName = "DIR-H018 | Rotate Face about a picked edge hinges the top face on that edge")]
    [Trait("TestId", "DIR-H018")]
    public void RotateFaceAboutEdge()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var hinge = Assert.Single(EdgesWhere(box, e => e.Max.X < 1e-6 && e.Min.Z > 9.99));
        var command = Begin(session, () => new RotateFaceCommand());
        Pick(command, "Flat faces to rotate", FaceFacing(box, Vec3.UnitZ));
        Pick(command, "Axis: a straight edge (optional)", hinge);
        SetNumber(command, "Rotation angle", 10);
        var shape = Replaced(command, box);
        double wedge = 0.5 * 10 * 10 * Math.Tan(Math.PI / 18) * 10;
        AssertClose(wedge, Math.Abs(shape.Props().Volume - 1000), 1e-6, "Hinged wedge volume (mm³)");
        AssertClose(0, shape.Props().Min.X, 1e-9, "Body still starts at the hinge (mm)");
    }

    [AvaloniaFact(DisplayName = "DIR-H019 | Rotate Face refuses a curved edge as its axis")]
    [Trait("TestId", "DIR-H019")]
    public void RotateFaceRefusesCurvedAxis()
    {
        var session = new CadSession();
        var plate = DrilledPlate(session);
        var circle = EdgesWhere(plate, e => e.Size.X > 5.9 && e.Size.X < 6.1 && e.Max.Z > 9.99).First();
        var command = Begin(session, () => new RotateFaceCommand());
        Pick(command, "Flat faces to rotate", FaceFacing(plate, Vec3.UnitX));
        Pick(command, "Axis: a straight edge (optional)", circle);
        AssertBuildFails(command, "straight edge");
    }

    [AvaloniaTheory(DisplayName = "DIR-H020 | Move Face uses only the part of the move square to the face")]
    [InlineData(0, 0, 3, 1300)]
    [InlineData(4, 0, 3, 1300)]
    [InlineData(0, 0, -2, 800)]
    [Trait("TestId", "DIR-H020")]
    public void MoveFaceNormalComponent(double x, double y, double z, double volume)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new MoveFaceCommand());
        Pick(command, "Flat faces to move", FaceFacing(box, Vec3.UnitZ));
        SetNumber(command, "Move X", x);
        SetNumber(command, "Move Y", y);
        SetNumber(command, "Move Z", z);
        var shape = Replaced(command, box);
        AssertNear(volume, shape.Props().Volume, "Moved face volume (mm³)");
        AssertBounds(shape, Vec3.Zero, new Vec3(10, 10, 10 + z));
    }

    [AvaloniaTheory(DisplayName = "DIR-H021 | Move Face tilt about each axis choice keeps a symmetric top-face tilt volume")]
    [InlineData("The face's own X direction")]
    [InlineData("The face's own Y direction")]
    [InlineData("World X")]
    [InlineData("World Y")]
    [Trait("TestId", "DIR-H021")]
    public void MoveFaceTilt(string axis)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new MoveFaceCommand());
        Pick(command, "Flat faces to move", FaceFacing(box, Vec3.UnitZ));
        SetChoice(command, "Tilt about", axis);
        SetNumber(command, "Tilt angle", 10);
        var shape = Replaced(command, box);
        AssertClose(1000, shape.Props().Volume, 1e-6, "Tilted volume (mm³)");
        AssertClose(10 + 5 * Math.Tan(Math.PI / 18), shape.Props().Max.Z, 1e-6, "Raised edge height (mm)");
    }

    // ------------------------------------------------------------------ Remove Fillets

    static SceneObject FilletedBox(CadSession session)
    {
        using var box = BoxAt(Vec3.Zero, 10, 10, 10);
        int edge = Enumerable.Range(0, box.Count(SubKind.Edge)).First(i =>
        {
            using var e = box.Sub(SubKind.Edge, i);
            var p = e.Props();
            return p.Size.Z > 9 && p.Min.X > 9.99 && p.Min.Y > 9.99;
        });
        return session.AddObject(Occ.Fillet(box, [edge], 2), "Filleted box", ObjKind.Solid);
    }

    [AvaloniaFact(DisplayName = "DIR-H022 | Remove Fillets restores the sharp corner of a picked round")]
    [Trait("TestId", "DIR-H022")]
    public void RemoveFilletsPicked()
    {
        var session = new CadSession();
        var body = FilletedBox(session);
        var round = new SelItem(body.Id, SelKind.Face, Enumerable.Range(0, body.Shape.Count(SubKind.Face)).Single(i =>
        {
            using var face = body.Shape.Sub(SubKind.Face, i);
            return Occ.FaceInfoOf(face).Type == "Cylinder";
        }));
        var command = Begin(session, () => new RemoveFilletsCommand());
        Pick(command, "Fillets to remove (click one: the whole chain is picked)", round);
        var shape = Replaced(command, body);
        AssertClose(1000, shape.Props().Volume, 1e-6, "Sharp volume (mm³)");
        Assert.Equal("1", Result(command, "Fillet faces removed"));
    }

    [AvaloniaTheory(DisplayName = "DIR-H023 | Remove Fillets on a body removes only rounds up to the given radius")]
    [InlineData(3, true)]
    [InlineData(1, false)]
    [Trait("TestId", "DIR-H023")]
    public void RemoveFilletsByRadius(double radius, bool removed)
    {
        var session = new CadSession();
        var body = FilletedBox(session);
        var command = Begin(session, () => new RemoveFilletsCommand());
        Pick(command, "Or a body to clean up", body);
        SetNumber(command, "Also remove all rounds up to radius (0 = only picked)", radius);
        var output = command.TryBuild();
        if (removed)
        {
            Assert.NotNull(output);
            AssertClose(1000, Assert.Single(output.Replace).New.Props().Volume, 1e-6, "Sharp volume (mm³)");
        }
        else
        {
            Assert.Null(output);
            Assert.Equal("none found", Result(command, "Fillets removed"));
        }
    }

    [AvaloniaFact(DisplayName = "DIR-H024 | Remove Fillets refuses a face that is not a round")]
    [Trait("TestId", "DIR-H024")]
    public void RemoveFilletsRefusesFlatFace()
    {
        var session = new CadSession();
        var body = FilletedBox(session);
        var command = Begin(session, () => new RemoveFilletsCommand());
        Pick(command, "Fillets to remove (click one: the whole chain is picked)", FaceFacing(body, Vec3.UnitZ));
        AssertBuildFails(command, "not a fillet");
    }

    // ------------------------------------------------------------------ Move Body

    [AvaloniaFact(DisplayName = "DIR-H025 | Move Body snaps faces flush (touching) and limits the typed move to the shared plane")]
    [Trait("TestId", "DIR-H025")]
    public void MoveBodyFlushTouching()
    {
        var session = new CadSession();
        var mover = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var anchor = CreateBoxBody(session, new Vec3(30, 0, 20), 10, 10, 10);
        var command = Begin(session, () => new MoveBodyCommand());
        Pick(command, "Face on the body to move", FaceFacing(mover, Vec3.UnitZ));
        Pick(command, "Face on the other body to keep to", FaceFacing(anchor, -Vec3.UnitZ));
        SetNumber(command, "Move X", 5);
        SetNumber(command, "Move Z", 7);
        var shape = Replaced(command, mover);
        AssertBounds(shape, new Vec3(5, 0, 10), new Vec3(15, 10, 20));
        Assert.Equal("faces flush (touching)", Result(command, "Relationship kept"));
        Assert.True(Assert.Single(command.Results, r => r.Label == "Typed move limited").IsWarn);
    }

    [AvaloniaFact(DisplayName = "DIR-H026 | Move Body 'Aligned' flush turns the face to point the same way in the same plane")]
    [Trait("TestId", "DIR-H026")]
    public void MoveBodyFlushAligned()
    {
        var session = new CadSession();
        var mover = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var anchor = CreateBoxBody(session, new Vec3(30, 0, 20), 10, 10, 10);
        var command = Begin(session, () => new MoveBodyCommand());
        Pick(command, "Face on the body to move", FaceFacing(mover, Vec3.UnitZ));
        Pick(command, "Face on the other body to keep to", FaceFacing(anchor, -Vec3.UnitZ));
        SetChoice(command, "Flush means", "Aligned (same plane, same direction)");
        var shape = Replaced(command, mover);
        AssertClose(20, shape.Props().Min.Z, 1e-6, "Flipped body bottom sits in the anchor face plane (mm)");
        AssertClose(30, shape.Props().Max.Z, 1e-6, "Flipped body top (mm)");
        Assert.Equal("faces flush (aligned)", Result(command, "Relationship kept"));
    }

    [AvaloniaFact(DisplayName = "DIR-H027 | Move Body coaxial keeps two pins on one axis and only moves along it")]
    [Trait("TestId", "DIR-H027")]
    public void MoveBodyCoaxial()
    {
        var session = new CadSession();
        var pin = session.AddObject(CylinderAt(new Vec3(5, 5, 0), 2, 10), "Pin", ObjKind.Solid);
        var shaft = session.AddObject(CylinderAt(new Vec3(20, 0, 0), 3, 10), "Shaft", ObjKind.Solid);
        SelItem Round(SceneObject body) => new(body.Id, SelKind.Face, Enumerable.Range(0, body.Shape.Count(SubKind.Face)).Single(i =>
        {
            using var face = body.Shape.Sub(SubKind.Face, i);
            return Occ.FaceInfoOf(face).Type == "Cylinder";
        }));
        var command = Begin(session, () => new MoveBodyCommand());
        Pick(command, "Face on the body to move", Round(pin));
        Pick(command, "Face on the other body to keep to", Round(shaft));
        SetChoice(command, "Keep", "Holes / pins coaxial");
        Assert.False(command.Params.Single(p => p.Label == "Flush means").IsVisible);
        SetNumber(command, "Move X", 9);
        SetNumber(command, "Move Z", 4);
        var shape = Replaced(command, pin);
        AssertBounds(shape, new Vec3(18, -2, 4), new Vec3(22, 2, 14));
        Assert.Equal("holes / pins coaxial", Result(command, "Relationship kept"));
    }

    [AvaloniaFact(DisplayName = "DIR-H028 | Move Body refuses two faces on the same body")]
    [Trait("TestId", "DIR-H028")]
    public void MoveBodyRefusesSameBody()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new MoveBodyCommand());
        Pick(command, "Face on the body to move", FaceFacing(box, Vec3.UnitZ));
        Pick(command, "Face on the other body to keep to", FaceFacing(box, -Vec3.UnitZ));
        AssertBuildFails(command, "different body");
    }

    // ------------------------------------------------------------------ Mirror Feature

    [AvaloniaFact(DisplayName = "DIR-H029 | Mirror Feature adds a twin hole across the plate's mid-plane")]
    [Trait("TestId", "DIR-H029")]
    public void MirrorFeatureAddsTwinHole()
    {
        var session = new CadSession();
        var plate = DrilledPlate(session);
        var mirror = session.AddObject(null, "Mid plane", ObjKind.Plane, plane: Plane3.YZ with { Origin = new Vec3(20, 0, 0) });
        var command = Begin(session, () => new MirrorFeatureCommand());
        Pick(command, "Features to mirror (click a hole, slot, pocket or boss)", HoleWall(plate));
        Pick(command, "Mirror plane or flat face", mirror);
        var shape = Replaced(command, plate);
        AssertClose(8000 - 2 * HoleVolume(3), shape.Props().Volume, 1e-3, "Two-hole volume (mm³)");
        Assert.Equal([10.0, 30.0], HoleCentresX(session, shape));
        Assert.Equal("1", Result(command, "Features mirrored"));
    }

    [AvaloniaFact(DisplayName = "DIR-H030 | Mirror Feature refuses a plain flat face")]
    [Trait("TestId", "DIR-H030")]
    public void MirrorFeatureRefusesFlatFace()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new MirrorFeatureCommand());
        Pick(command, "Features to mirror (click a hole, slot, pocket or boss)", FaceFacing(box, Vec3.UnitZ));
        Pick(command, "Mirror plane or flat face", FaceFacing(box, Vec3.UnitX));
        AssertBuildFails(command, "holes, slots, pockets and bosses");
    }

    // ------------------------------------------------------------------ Check & Repair

    static SceneObject OpenShellAsSurface(CadSession session, bool dropTop)
    {
        // Each face is built from its own outline so neighbouring faces do not share edges: the repair has real gaps to sew.
        Vec3[][] outlines =
        [
            [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)],
            [new(0, 0, 10), new(10, 0, 10), new(10, 10, 10), new(0, 10, 10)],
            [new(0, 0, 0), new(10, 0, 0), new(10, 0, 10), new(0, 0, 10)],
            [new(0, 10, 0), new(10, 10, 0), new(10, 10, 10), new(0, 10, 10)],
            [new(0, 0, 0), new(0, 10, 0), new(0, 10, 10), new(0, 0, 10)],
            [new(10, 0, 0), new(10, 10, 0), new(10, 10, 10), new(10, 0, 10)],
        ];
        var faces = outlines.Where((_, i) => !dropTop || i != 1).Select(points =>
        {
            using var loop = Occ.Polyline(points, true);
            return Occ.Profile([loop], true);
        }).ToList();
        return session.AddObject(Occ.Compound(faces), "Loose faces", ObjKind.Surface);
    }

    [AvaloniaFact(DisplayName = "DIR-H031 | Check & Repair only reports when no fix is ticked")]
    [Trait("TestId", "DIR-H031")]
    public void CheckRepairReportsOnly()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new CheckRepairCommand());
        Pick(command, "Body to check (solid or surface)", box);
        Assert.Null(command.TryBuild());
        Assert.Null(command.Error);
        Assert.Equal("tick an option to fix problems", Result(command, "Repair"));
        Assert.Equal("none", Result(command, "Non-manifold edges"));
    }

    [AvaloniaFact(DisplayName = "DIR-H032 | Check & Repair closes six loose faces into a 1000 mm³ solid")]
    [Trait("TestId", "DIR-H032")]
    public void CheckRepairClosesIntoSolid()
    {
        var session = new CadSession();
        var loose = OpenShellAsSurface(session, dropTop: false);
        var command = Begin(session, () => new CheckRepairCommand());
        Pick(command, "Body to check (solid or surface)", loose);
        SetToggle(command, "Close an open surface into a solid when it seals", true);
        var (old, repaired) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(loose.Id, old.Id);
        Assert.True(repaired.HasSolids);
        AssertNear(1000, repaired.Props().Volume, "Repaired volume (mm³)");
        Assert.Contains("now a solid", Result(command, "Sewn"));
    }

    [AvaloniaFact(DisplayName = "DIR-H033 | Check & Repair reports an open box that cannot seal into a solid")]
    [Trait("TestId", "DIR-H033")]
    public void CheckRepairCannotSeal()
    {
        var session = new CadSession();
        using var box = BoxAt(Vec3.Zero, 10, 10, 10);
        var walls = Enumerable.Range(0, 6).Select(i => box.Sub(SubKind.Face, i)).Where(face => face.Props().Min.Z < 9.99).ToList();
        var open = session.AddObject(Occ.Sew(walls, 0.01, false), "Open box", ObjKind.Surface);
        var command = Begin(session, () => new CheckRepairCommand());
        Pick(command, "Body to check (solid or surface)", open);
        SetToggle(command, "Close an open surface into a solid when it seals", true);
        Assert.Null(command.TryBuild());
        Assert.Equal("the surfaces do not seal within 0.01 mm: 4 open edges remain", Result(command, "Close into solid"));
        Assert.Equal("nothing could be changed", Result(command, "Repair"));
    }

    [AvaloniaFact(DisplayName = "DIR-H035 | Check & Repair 'Close' on loose faces with a missing side sews them but leaves a surface")]
    [Trait("TestId", "DIR-H035")]
    public void CheckRepairCloseOnOpenLooseFaces()
    {
        var session = new CadSession();
        var loose = OpenShellAsSurface(session, dropTop: true);
        var command = Begin(session, () => new CheckRepairCommand());
        Pick(command, "Body to check (solid or surface)", loose);
        SetToggle(command, "Close an open surface into a solid when it seals", true);
        var (_, sewn) = Assert.Single(BuildOutput(command).Replace);
        Assert.False(sewn.HasSolids);
        Assert.Equal("open edges 20 → 4", Result(command, "Sewn"));
    }

    [AvaloniaFact(DisplayName = "DIR-H034 | Check & Repair 'Heal' sews loose faces and reduces open edges")]
    [Trait("TestId", "DIR-H034")]
    public void CheckRepairHealSews()
    {
        var session = new CadSession();
        var loose = OpenShellAsSurface(session, dropTop: true);
        var command = Begin(session, () => new CheckRepairCommand());
        Pick(command, "Body to check (solid or surface)", loose);
        SetToggle(command, "Heal: sew gaps, drop duplicate faces, merge split faces", true);
        var (_, healed) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(4, Occ.Check(healed).FreeEdges);
        Assert.StartsWith("open edges 20 → 4", Result(command, "Sewn"));
    }
}
