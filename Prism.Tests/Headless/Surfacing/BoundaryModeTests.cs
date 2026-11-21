using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#1-surface-blending-and-fillets-lead-item">0.4 modeling gaps, item 1 (BLD-P1).</seealso>
[Trait("Feature", "BoundarySurface")]
[Trait("Layer", "Headless")]
public class BoundaryModeTests
{
    static SceneObject Line(CadSession session, Vec3 start, Vec3 end) => session.AddObject(Occ.Line(start, end), "Line", ObjKind.Curve);

    static SceneObject NurbsSheet(CadSession session, double x0, double width, double z = 0) =>
        session.AddObject(Occ.NurbsSurface([new(x0, 0, z), new(x0 + width, 0, z), new(x0, 10, z), new(x0 + width, 10, z)], 2, 2, 1, 1), "NURBS sheet", ObjKind.Surface);

    static SelItem EdgeAtX(SceneObject source, double x) =>
        Assert.Single(EdgesWhere(source, e => Math.Abs(e.Min.X - x) < 1e-6 && Math.Abs(e.Max.X - x) < 1e-6));

    static BoundarySurfaceCommand Start(CadSession session, string mode)
    {
        var command = Begin(session, () => new BoundarySurfaceCommand());
        SetChoice(command, "Mode", mode);
        return command;
    }

    static void KeepSeparate(Command command) => command.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Join the patch")).Value = false;

    [AvaloniaFact(DisplayName = "BND-H006 | Edge mode spans four lines of a 10 mm square with a 100 mm² sheet and two lines with a ruled strip")]
    [Trait("TestId", "BND-H006")]
    public void EdgeModeSpansCurves()
    {
        var session = new CadSession();
        var square = new[]
        {
            Line(session, new(0, 0, 0), new(10, 0, 0)), Line(session, new(10, 0, 0), new(10, 10, 0)),
            Line(session, new(10, 10, 0), new(0, 10, 0)), Line(session, new(0, 10, 0), new(0, 0, 0)),
        };
        var four = Start(session, "Edge: span two to four curves");
        Pick(four, "Boundary", square);
        KeepSeparate(four);
        AssertClose(100, Assert.Single(BuildOutput(four).Add).Shape!.Props().Area, 1e-3, "Edge mode square area (mm²)");

        var rails = new CadSession();
        var two = Start(rails, "Edge: span two to four curves");
        Pick(two, "Boundary", Line(rails, new(0, 0, 0), new(10, 0, 0)), Line(rails, new(0, 10, 5), new(10, 10, 5)));
        KeepSeparate(two);
        AssertClose(10 * Math.Sqrt(125), Assert.Single(BuildOutput(two).Add).Shape!.Props().Area, 1e-3, "Edge mode ruled area (mm²)");
    }

    [AvaloniaFact(DisplayName = "BND-H007 | Edge mode refuses one curve and says what it needs")]
    [Trait("TestId", "BND-H007")]
    public void EdgeModeRefusesOneCurve()
    {
        var session = new CadSession();
        var command = Start(session, "Edge: span two to four curves");
        Pick(command, "Boundary", Line(session, Vec3.Zero, new(10, 0, 0)));
        AssertBuildFails(command, "two to four");
    }

    [AvaloniaFact(DisplayName = "BND-H008 | Blend mode bridges two surface edges like Automatic does, and refuses a single edge")]
    [Trait("TestId", "BND-H008")]
    public void BlendModeBridgesTwoEdges()
    {
        double Area(string mode)
        {
            var session = new CadSession();
            var low = NurbsSheet(session, 0, 10);
            var high = NurbsSheet(session, 20, 10, z: 10);
            var command = Start(session, mode);
            Pick(command, "Boundary", EdgeAtX(low, 10), EdgeAtX(high, 20));
            KeepSeparate(command);
            var surface = Assert.Single(BuildOutput(command).Add).Shape!;
            Assert.True(surface.IsValid);
            return surface.Props().Area;
        }
        double blend = Area("Blend: bridge two surface edges");
        Assert.True(blend > 10 * Math.Sqrt(200) + 0.5, "G1 blend between flat sheets exceeds the straight strip");
        AssertClose(Area("Automatic"), blend, 1e-6, "Blend mode matches Automatic (mm²)");

        var one = new CadSession();
        var command = Start(one, "Blend: bridge two surface edges");
        Pick(command, "Boundary", EdgeAtX(NurbsSheet(one, 0, 10), 10));
        AssertBuildFails(command, "two surfaces");
    }

    [AvaloniaFact(DisplayName = "BND-H009 | Network mode needs curves across the surface")]
    [Trait("TestId", "BND-H009")]
    public void NetworkModeNeedsInsideCurves()
    {
        var session = new CadSession();
        var outline = new[]
        {
            Line(session, new(0, 0, 0), new(10, 0, 0)), Line(session, new(10, 0, 0), new(10, 10, 0)),
            Line(session, new(10, 10, 0), new(0, 10, 0)), Line(session, new(0, 10, 0), new(0, 0, 0)),
        };
        var command = Start(session, "Network: outline and curves across");
        Pick(command, "Boundary", outline);
        AssertBuildFails(command, "curves across");
    }

    [AvaloniaFact(DisplayName = "BND-H010 | A Boundary Surface step saved before Mode existed reopens in Automatic with its other settings")]
    [Trait("TestId", "BND-H010")]
    public void OldRecipesOpenInAutomatic()
    {
        var session = new CadSession();
        var source = Begin(session, () => new BoundarySurfaceCommand());
        source.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Join the patch")).Value = false;
        var saved = source.Capture()!;
        var old = saved with { Values = saved.Values.Skip(1).ToList() };   // the value list before Mode was added
        var reopened = Begin(session, () => new BoundarySurfaceCommand());
        reopened.Restore(old);
        Assert.Equal(0, reopened.Params.OfType<ChoiceParam>().Single(p => p.Label == "Mode").Index);
        Assert.False(reopened.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Join the patch")).Value);
    }

    static double DistanceTo(Shape surface, Vec3 point) { using var vertex = Occ.Vertex(point); return Occ.Distance(vertex, surface).Distance; }

    [AvaloniaFact(DisplayName = "BND-H011 | Edge mode on the four straight edges of a saddle gives the exact ruled surface")]
    [Trait("TestId", "BND-H011")]
    public void EdgeModeSaddleIsExact()
    {
        var session = new CadSession();
        // corners (0,0,0) (10,0,0) (10,10,10) (0,10,0): the surface z = x*y/10 (a hyperbolic paraboloid), every edge a straight line
        var edges = new[]
        {
            Line(session, new(0, 0, 0), new(10, 0, 0)), Line(session, new(10, 0, 0), new(10, 10, 10)),
            Line(session, new(10, 10, 10), new(0, 10, 0)), Line(session, new(0, 10, 0), new(0, 0, 0)),
        };
        var command = Start(session, "Edge: span two to four curves");
        Pick(command, "Boundary", edges);
        KeepSeparate(command);
        var surface = Assert.Single(BuildOutput(command).Add).Shape!;
        Assert.True(surface.IsValid);
        foreach (var (x, y) in new[] { (5.0, 5.0), (2.5, 7.5), (8.0, 3.0), (6.0, 9.0) })
            Assert.True(DistanceTo(surface, new(x, y, x * y / 10)) < 0.02, $"({x}, {y}) should lie on z = xy/10, was {DistanceTo(surface, new(x, y, x * y / 10)):G3} mm off");
        Assert.True(DistanceTo(surface, new(5, 5, 3.5)) > 0.3, "a point off the saddle is not on it");
    }

    [AvaloniaFact(DisplayName = "BND-H012 | Network mode follows the outline exactly and passes close to a curve across it")]
    [Trait("TestId", "BND-H012")]
    public void NetworkModeFollowsInsideCurve()
    {
        var session = new CadSession();
        var outline = new[]
        {
            Line(session, new(0, 0, 0), new(10, 0, 0)), Line(session, new(10, 0, 0), new(10, 10, 0)),
            Line(session, new(10, 10, 0), new(0, 10, 0)), Line(session, new(0, 10, 0), new(0, 0, 0)),
        };
        var across = session.AddObject(Occ.Spline([new(0, 5, 0), new(5, 5, 3), new(10, 5, 0)]), "Across", ObjKind.Curve);
        var command = Start(session, "Network: outline and curves across");
        Pick(command, "Boundary", outline);
        Pick(command, "Guide curves", across);
        KeepSeparate(command);
        var surface = Assert.Single(BuildOutput(command).Add).Shape!;
        Assert.True(surface.IsValid);
        double worstAcross = 0;
        foreach (var p in Occ.CurveSamples(across.Shape, 15, 1)[0].Select(s => s.p)) worstAcross = Math.Max(worstAcross, DistanceTo(surface, p));
        Assert.True(worstAcross < 0.25, $"the surface should pass within 0.25 mm of the curve across it, worst {worstAcross:G3} mm");
        double worstOutline = 0;
        foreach (var t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }) worstOutline = Math.Max(worstOutline, Math.Max(DistanceTo(surface, new(10 * t, 0, 0)), DistanceTo(surface, new(0, 10 * t, 0))));
        Assert.True(worstOutline < 0.05, $"the outline should be on the surface, worst {worstOutline:G3} mm off");
    }
}
