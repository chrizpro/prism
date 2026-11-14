using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <summary>
/// Characterizes Warp (Bend, Twist, Taper, Stretch with start/end range) on meshes, curves and solids. Mesh vertices are
/// moved directly, so their positions give exact expectations; solids are warped through their NURBS control nets.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/transform/README.md">Transform guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class WarpTests
{
    static SceneObject CubeMesh(CadSession session, Vec3 min, double size)
    {
        var mesh = Mesh.MeshToolTests.BoxMesh(min, size, size, size);
        return Mesh.MeshToolTests.AddMesh(session, mesh);
    }

    static TriMesh Warped(Command command, SceneObject source)
    {
        var (old, mesh) = Assert.Single(BuildOutput(command).ReplaceMeshes);
        Assert.Equal(source.Id, old.Id);
        return mesh;
    }

    static WarpCommand Warp(CadSession session, SceneObject target, string kind, string axis)
    {
        var command = Begin(session, () => new WarpCommand());
        Pick(command, "What to warp", target);
        SetChoice(command, "Warp", kind);
        SetChoice(command, "Along the axis", axis);
        return command;
    }

    [AvaloniaTheory(DisplayName = "WRP-H001 | Stretch lengthens the warped range and carries the rest rigidly")]
    [InlineData(0, 100, 30)]
    [InlineData(0, 50, 25)]
    [InlineData(50, 100, 25)]
    [Trait("TestId", "WRP-H001")]
    public void StretchRange(double start, double end, double newLength)
    {
        var session = new CadSession();
        var cube = CubeMesh(session, Vec3.Zero, 20);
        var command = Warp(session, cube, "Stretch (lengthen or shorten)", "X");
        Assert.True(command.Params.Single(p => p.Label == "Amount").IsVisible);
        Assert.False(command.Params.Single(p => p.Label == "Angle").IsVisible);
        SetNumber(command, "Amount", 50);
        SetNumber(command, "Warp starts at", start);
        SetNumber(command, "Warp ends at", end);
        var mesh = Warped(command, cube);
        AssertClose(0, mesh.Min.X, 1e-5, "Fixed start (mm)");
        AssertClose(newLength, mesh.Max.X, 1e-5, "Stretched length (mm)");
        AssertClose(20 * 20 * newLength, mesh.Volume(), 1e-2, "Stretched volume (mm³)");
        Assert.Equal("20 mm", Result(command, "Length along the axis"));
    }

    [AvaloniaFact(DisplayName = "WRP-H002 | Taper narrows the cross-section toward the far end")]
    [Trait("TestId", "WRP-H002")]
    public void Taper()
    {
        var session = new CadSession();
        var cube = CubeMesh(session, Vec3.Zero, 20);
        var command = Warp(session, cube, "Taper (narrow or widen toward one end)", "Z");
        SetNumber(command, "Amount", -50);
        var mesh = Warped(command, cube);
        var top = Enumerable.Range(0, mesh.VertexCount).Select(mesh.P).Where(p => p.Z > 19.99).ToList();
        AssertClose(5, top.Min(p => p.X), 1e-5, "Tapered top minimum X (mm)");
        AssertClose(15, top.Max(p => p.X), 1e-5, "Tapered top maximum X (mm)");
        AssertClose(0, mesh.Min.X, 1e-5, "Untouched base (mm)");
    }

    [AvaloniaFact(DisplayName = "WRP-H003 | Twist turns the far end about the axis by the angle")]
    [Trait("TestId", "WRP-H003")]
    public void Twist()
    {
        var session = new CadSession();
        var cube = CubeMesh(session, Vec3.Zero, 20);
        var command = Warp(session, cube, "Twist", "Z");
        SetNumber(command, "Angle", 90);
        var mesh = Warped(command, cube);
        // The top vertex that started at (0, 0, 20) turns 90° about the centre line (10, 10).
        Assert.Contains(Enumerable.Range(0, mesh.VertexCount).Select(mesh.P), p => p.DistanceTo(new Vec3(20, 0, 20)) < 1e-4);
        Assert.Contains(Enumerable.Range(0, mesh.VertexCount).Select(mesh.P), p => p.DistanceTo(new Vec3(0, 0, 0)) < 1e-6);
    }

    [AvaloniaFact(DisplayName = "WRP-H004 | Bend curls a 100 mm line centred on the origin into a quarter circle")]
    [Trait("TestId", "WRP-H004")]
    public void BendCentredLine()
    {
        var session = new CadSession();
        var line = session.AddObject(Occ.Line(new Vec3(-50, 0, 0), new Vec3(50, 0, 0)), "Line", ObjKind.Curve);
        var command = Warp(session, line, "Bend", "X");
        Assert.True(command.Params.Single(p => p.Label == "Bend toward").IsVisible);
        SetChoice(command, "Bend toward", "+Z");
        SetNumber(command, "Angle", 90);
        var (_, bent) = Assert.Single(BuildOutput(command).Replace);
        double radius = 100 / (Math.PI / 2);
        var (start, end) = Ends(bent);
        AssertPoint(new Vec3(-50, 0, 0), start, 1e-3, "Bend start stays");
        AssertPoint(new Vec3(-50 + radius, 0, radius), end, 0.05, "Quarter-circle end");
        AssertClose(100, Length(bent), 1, "Bent length within 1% (control-net approximation, mm)");
    }

    [AvaloniaFact(DisplayName = "WRP-H007 | Bend keeps a line that starts at the origin attached at its start")]
    [Trait("TestId", "WRP-H007")]
    public void BendOffCentreLine()
    {
        var session = new CadSession();
        var line = session.AddObject(Occ.Line(Vec3.Zero, new Vec3(100, 0, 0)), "Line", ObjKind.Curve);
        var command = Warp(session, line, "Bend", "X");
        SetChoice(command, "Bend toward", "+Z");
        SetNumber(command, "Angle", 90);
        var (_, bent) = Assert.Single(BuildOutput(command).Replace);
        double radius = 100 / (Math.PI / 2);
        var (start, end) = Ends(bent);
        AssertPoint(Vec3.Zero, start, 1e-3, "Bend start stays");
        AssertPoint(new Vec3(radius, 0, radius), end, 0.05, "Quarter-circle end");
        AssertClose(100, Length(bent), 1, "Bent length within 1% (control-net approximation, mm)");
    }

    [AvaloniaFact(DisplayName = "WRP-H005 | Bend refuses a direction along the bend axis")]
    [Trait("TestId", "WRP-H005")]
    public void BendRefusesAxisDirection()
    {
        var session = new CadSession();
        var cube = CubeMesh(session, Vec3.Zero, 20);
        var command = Warp(session, cube, "Bend", "X");
        SetChoice(command, "Bend toward", "+X");
        AssertBuildFails(command, "right angles to the axis");
    }

    [AvaloniaFact(DisplayName = "WRP-H006 | Stretching a solid keeps it a valid closed body")]
    [Trait("TestId", "WRP-H006")]
    public void StretchSolid()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 20, 10, 10);
        var command = Warp(session, box, "Stretch (lengthen or shorten)", "X");
        SetNumber(command, "Amount", 50);
        var (_, shape) = Assert.Single(BuildOutput(command).Replace);
        AssertValidSolid(shape);
        AssertClose(30 * 10 * 10, shape.Props().Volume, 1, "Stretched solid volume (mm³)");
        AssertClose(30, shape.Props().Max.X, 1e-3, "Stretched solid length (mm)");
    }

    // The build meshes the stored shape for its bounds before the NURBS convert step, and that used to leave the
    // converted faces with no surface at all (the command reported "Face has no surface."). A flat patch rolled
    // 90° along its 80 mm span becomes a quarter cylinder of radius 80/(π/2) ≈ 50.9 mm.
    [AvaloniaFact(DisplayName = "WRP-H008 | Bend rolls a NURBS surface patch into a cylindrical sheet")]
    [Trait("TestId", "WRP-H008")]
    public void BendNurbsSurface()
    {
        var session = new CadSession();
        var poles = new Vec3[16];
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
            poles[r * 4 + c] = new Vec3(-40 + c * (80.0 / 3), -30 + r * 20, 0);
        var sheet = session.AddObject(Occ.NurbsSurface(poles, 4, 4, 3, 3), "Sheet", ObjKind.Surface);
        var command = Warp(session, sheet, "Bend", "X");
        SetChoice(command, "Bend toward", "+Z");
        SetNumber(command, "Angle", 90);
        var (_, bent) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(ShapeKind.Face, bent.Kind);
        double radius = 80 / (Math.PI / 2);
        var bounds = bent.Props();
        AssertClose(-40, bounds.Min.X, 0.1, "Bend start edge stays (mm)");
        AssertClose(-40 + radius, bounds.Max.X, 2, "Rolled edge X (mm)");
        AssertClose(radius, bounds.Max.Z, 2, "Rolled height (mm)");
        AssertClose(-30, bounds.Min.Y, 0.1, "Width minimum (mm)");
        AssertClose(30, bounds.Max.Y, 0.1, "Width maximum (mm)");
    }
}
