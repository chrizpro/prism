using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <summary>
/// Characterizes the Surface/NURBS ribbon tools that build, join, cut and repair surfaces: Edge Surface, Blend Surface,
/// Sweep 2 Rails, Rebuild Surface, Network Surface, Planar Surface, Knit, Trim Surface, Enclosed Body, Unsew, Untrim,
/// Extend, Reverse Normal and Contour.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/surface-and-nurbs/README.md">Living guide.</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class SurfaceToolTests
{
    static SceneObject Line(CadSession session, Vec3 start, Vec3 end) =>
        session.AddObject(Occ.Line(start, end), "Line", ObjKind.Curve);

    /// <summary>A flat 10 × 10 mm planar sheet whose corner is at the given point (XY plane).</summary>
    static SceneObject Sheet(CadSession session, double x0 = 0, double z = 0, double size = 10)
    {
        using var boundary = Occ.Polyline([new(x0, 0, z), new(x0 + size, 0, z), new(x0 + size, size, z), new(x0, size, z)], true);
        return session.AddObject(Occ.Profile([boundary], true), "Sheet", ObjKind.Surface);
    }

    /// <summary>An exact bilinear NURBS sheet (a true B-spline patch, unlike the planar sheet).</summary>
    static SceneObject NurbsSheet(CadSession session, double x0, double width, double z = 0)
    {
        var grid = Occ.NurbsSurface([new(x0, 0, z), new(x0 + width, 0, z), new(x0, 10, z), new(x0 + width, 10, z)], 2, 2, 1, 1);
        return session.AddObject(grid, "NURBS sheet", ObjKind.Surface);
    }

    static SelItem EdgeAtX(SceneObject source, double x) =>
        Assert.Single(EdgesWhere(source, e => Math.Abs(e.Min.X - x) < 1e-6 && Math.Abs(e.Max.X - x) < 1e-6));

    static Shape SingleSurface(CommandOutput output)
    {
        var added = Assert.Single(output.Add);
        Assert.NotNull(added.Shape);
        Assert.True(added.Shape.IsValid);
        return added.Shape;
    }

    // ------------------------------------------------------------------ Edge Surface

    [AvaloniaFact(DisplayName = "SRF-H001 | Edge Surface spans four lines of a 10 mm square with a 100 mm² sheet")]
    [Trait("TestId", "SRF-H001")]
    public void EdgeSurfaceFromFourCurves()
    {
        var session = new CadSession();
        var curves = new[]
        {
            Line(session, new(0, 0, 0), new(10, 0, 0)), Line(session, new(10, 0, 0), new(10, 10, 0)),
            Line(session, new(10, 10, 0), new(0, 10, 0)), Line(session, new(0, 10, 0), new(0, 0, 0)),
        };
        var command = Begin(session, () => new EdgeSurfaceCommand());
        Pick(command, "Boundary curves (2 to 4)", curves);
        var surface = SingleSurface(BuildOutput(command));
        AssertClose(100, surface.Props().Area, 1e-3, "Edge surface area (mm²)");
        AssertBounds(surface, Vec3.Zero, new Vec3(10, 10, 0));
    }

    [AvaloniaFact(DisplayName = "SRF-H002 | Edge Surface with two curves makes a ruled sheet between them")]
    [Trait("TestId", "SRF-H002")]
    public void EdgeSurfaceFromTwoCurves()
    {
        var session = new CadSession();
        var first = Line(session, new(0, 0, 0), new(10, 0, 0));
        var second = Line(session, new(0, 10, 5), new(10, 10, 5));
        var command = Begin(session, () => new EdgeSurfaceCommand());
        Pick(command, "Boundary curves (2 to 4)", first, second);
        var surface = SingleSurface(BuildOutput(command));
        AssertClose(10 * Math.Sqrt(125), surface.Props().Area, 1e-3, "Ruled area (mm²)");
    }

    [AvaloniaFact(DisplayName = "SRF-H003 | Edge Surface refuses a single curve")]
    [Trait("TestId", "SRF-H003")]
    public void EdgeSurfaceRefusesOneCurve()
    {
        var session = new CadSession();
        var command = Begin(session, () => new EdgeSurfaceCommand());
        Pick(command, "Boundary curves (2 to 4)", Line(session, Vec3.Zero, new(10, 0, 0)));
        AssertBuildFails(command, "two, three or four");
    }

    // ------------------------------------------------------------------ Blend Surface

    static double BlendArea(string continuity, double fullness)
    {
        var session = new CadSession();
        var low = NurbsSheet(session, 0, 10);
        var high = NurbsSheet(session, 20, 10, z: 10);
        var command = Begin(session, () => new BlendSurfaceCommand());
        Pick(command, "First edge", EdgeAtX(low, 10));
        Pick(command, "Second edge", EdgeAtX(high, 20));
        SetChoice(command, "Continuity", continuity);
        SetNumber(command, "Fullness", fullness);
        var surface = SingleSurface(BuildOutput(command));
        var bounds = surface.Props();
        AssertClose(10, bounds.Min.X, 1e-3, "Blend starts at the first edge (X)");
        AssertClose(20, bounds.Max.X, 1e-3, "Blend ends at the second edge (X)");
        return bounds.Area;
    }

    [AvaloniaFact(DisplayName = "SRF-H004 | Position Blend Surface is the straight ruled strip between the two edges")]
    [Trait("TestId", "SRF-H004")]
    public void BlendSurfacePositionIsRuled() =>
        AssertClose(10 * Math.Sqrt(200), BlendArea("Position (G0)", 1), 1e-2, "G0 blend area (mm²)");

    [AvaloniaTheory(DisplayName = "SRF-H005 | Tangent and curvature Blend Surfaces leave both sheets flat, so they exceed the ruled strip")]
    [InlineData("Tangent (G1)")]
    [InlineData("Curvature (G2)")]
    [Trait("TestId", "SRF-H005")]
    public void BlendSurfaceSmoothContinuity(string continuity) =>
        Assert.True(BlendArea(continuity, 1) > 10 * Math.Sqrt(200) + 0.5);

    [AvaloniaFact(DisplayName = "SRF-H006 | A fuller tangent Blend Surface bulges further than a tight one")]
    [Trait("TestId", "SRF-H006")]
    public void BlendSurfaceFullness() =>
        Assert.True(BlendArea("Tangent (G1)", 3) > BlendArea("Tangent (G1)", 0.3) + 0.5);

    // ------------------------------------------------------------------ Sweep 2 Rails

    [AvaloniaFact(DisplayName = "SRF-H007 | Sweep 2 Rails carries a 10 mm line along two parallel 50 mm rails as a 500 mm² surface")]
    [Trait("TestId", "SRF-H007")]
    public void SweepTwoRailsSurface()
    {
        var session = new CadSession();
        var profile = Line(session, new(0, 0, 0), new(10, 0, 0));
        var first = Line(session, new(0, 0, 0), new(0, 50, 0));
        var second = Line(session, new(10, 0, 0), new(10, 50, 0));
        var command = Begin(session, () => new SweepTwoRailsCommand());
        Assert.Equal("Surface", command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Result").Selected);
        Pick(command, "Profile", profile);
        Pick(command, "First rail", first);
        Pick(command, "Second rail", second);
        var output = BuildOutput(command);
        var added = Assert.Single(output.Add);
        Assert.Equal(ObjKind.Surface, added.Kind);
        AssertClose(500, added.Shape!.Props().Area, 1e-3, "Swept area (mm²)");
        AssertBounds(added.Shape, Vec3.Zero, new Vec3(10, 50, 0));
    }

    [AvaloniaFact(DisplayName = "SRF-H008 | Sweep 2 Rails 'Solid (capped)' closes a square profile into a 5000 mm³ body")]
    [Trait("TestId", "SRF-H008")]
    public void SweepTwoRailsSolid()
    {
        var session = new CadSession();
        var profile = CreateCurve(session, [new(0, 0, 0), new(10, 0, 0), new(10, 0, 10), new(0, 0, 10)], closed: true);
        var first = Line(session, new(0, 0, 0), new(0, 50, 0));
        var second = Line(session, new(10, 0, 0), new(10, 50, 0));
        var command = Begin(session, () => new SweepTwoRailsCommand());
        Pick(command, "Profile", profile);
        Pick(command, "First rail", first);
        Pick(command, "Second rail", second);
        SetChoice(command, "Result", "Solid (capped)");
        var added = Assert.Single(BuildOutput(command).Add);
        Assert.Equal(ObjKind.Solid, added.Kind);
        AssertValidSolid(added.Shape!);
        AssertClose(5000, added.Shape!.Props().Volume, 1e-2, "Swept volume (mm³)");
    }

    // ------------------------------------------------------------------ Rebuild Surface

    [AvaloniaTheory(DisplayName = "SRF-H009 | Rebuild Surface reproduces a flat sheet and honours Keep the original")]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("TestId", "SRF-H009")]
    public void RebuildSurfaceFlatSheet(bool keep)
    {
        var session = new CadSession();
        var sheet = NurbsSheet(session, 0, 10);
        var command = Begin(session, () => new RebuildSurfaceCommand());
        Pick(command, "Surface or face", sheet);
        SetInteger(command, "Points in U", 5);
        SetInteger(command, "Points in V", 6);
        SetToggle(command, "Keep the original", keep);
        var output = BuildOutput(command);
        var rebuilt = SingleSurface(output);
        AssertClose(100, rebuilt.Props().Area, 1e-3, "Rebuilt area (mm²)");
        AssertBounds(rebuilt, Vec3.Zero, new Vec3(10, 10, 0));
        // The "Largest deviation (approx.)" readout measures distance to tessellation vertices rather than to the surface,
        // so it reports several millimetres for this exact flat result. It is deliberately not asserted here; see the
        // coverage gaps in the Surface and NURBS guide.
        Assert.Single(command.Results, line => line.Label == "Largest deviation (approx.)");
        Assert.Equal(keep ? 0 : 1, output.Remove.Count);
    }

    // ------------------------------------------------------------------ Network Surface

    [AvaloniaFact(DisplayName = "SRF-H010 | Network Surface lifts a square outline toward a raised curve across it")]
    [Trait("TestId", "SRF-H010")]
    public void NetworkSurfaceFollowsInnerCurve()
    {
        var session = new CadSession();
        var outline = new[]
        {
            Line(session, new(0, 0, 0), new(20, 0, 0)), Line(session, new(20, 0, 0), new(20, 20, 0)),
            Line(session, new(20, 20, 0), new(0, 20, 0)), Line(session, new(0, 20, 0), new(0, 0, 0)),
        };
        var across = session.AddObject(Occ.Spline([new(0, 10, 0), new(10, 10, 4), new(20, 10, 0)]), "Across", ObjKind.Curve);
        var command = Begin(session, () => new NetworkSurfaceCommand());
        Pick(command, "Outline curves (a closed loop)", outline);
        Pick(command, "Curves across the surface", across);
        var surface = SingleSurface(BuildOutput(command));
        var bounds = surface.Props();
        AssertClose(0, bounds.Min.X, 0.05, "Outline minimum X (mm)");
        AssertClose(20, bounds.Max.X, 0.05, "Outline maximum X (mm)");
        AssertClose(4, bounds.Max.Z, 0.5, "Surface reaches the inner curve height (mm)");
        Assert.True(bounds.Area > 400);
        Assert.Equal("1", Result(command, "Inner curves used"));
        Assert.True(Assert.Single(command.Results, r => r.Label == "Overshoot past the curves").IsGood);
    }

    // ------------------------------------------------------------------ Planar Surface

    [AvaloniaFact(DisplayName = "SRF-H011 | Planar Surface fills a closed square and treats a nested square as a hole")]
    [Trait("TestId", "SRF-H011")]
    public void PlanarSurfaceWithHole()
    {
        var session = new CadSession();
        var outer = CreateCurve(session, [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)], closed: true);
        var inner = CreateCurve(session, [new(3, 3, 0), new(7, 3, 0), new(7, 7, 0), new(3, 7, 0)], closed: true);
        var command = Begin(session, () => new PlanarSurfaceCommand());
        Pick(command, "Closed boundary", outer);
        AssertClose(100, SingleSurface(BuildOutput(command)).Props().Area, 1e-6, "Filled area (mm²)");
        Pick(command, "Closed boundary", outer, inner);
        AssertClose(84, SingleSurface(BuildOutput(command)).Props().Area, 1e-6, "Area with hole (mm²)");
    }

    [AvaloniaFact(DisplayName = "SRF-H012 | Planar Surface refuses an open curve")]
    [Trait("TestId", "SRF-H012")]
    public void PlanarSurfaceRefusesOpenCurve()
    {
        var session = new CadSession();
        var open = CreateCurve(session, [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0)]);
        var command = Begin(session, () => new PlanarSurfaceCommand());
        Pick(command, "Closed boundary", open);
        Assert.Null(command.TryBuild());
        Assert.NotNull(command.Error);
    }

    // ------------------------------------------------------------------ Knit (Sew)

    static SceneObject[] BoxFacesAsSurfaces(CadSession session)
    {
        using var box = BoxAt(Vec3.Zero, 10, 10, 10);
        return Enumerable.Range(0, box.Count(SubKind.Face))
            .Select(i => session.AddObject(box.Sub(SubKind.Face, i), "Face", ObjKind.Surface)).ToArray();
    }

    [AvaloniaFact(DisplayName = "SRF-H013 | Knit closes the six faces of a cube into a 1000 mm³ solid and consumes them")]
    [Trait("TestId", "SRF-H013")]
    public void KnitMakesSolid()
    {
        var session = new CadSession();
        var faces = BoxFacesAsSurfaces(session);
        var command = Begin(session, () => new SewCommand());
        Pick(command, "Surfaces", faces);
        var output = BuildOutput(command);
        Assert.Equal(6, output.Remove.Count);
        var body = SingleSurface(output);
        AssertValidSolid(body);
        AssertNear(1000, body.Props().Volume, "Knitted volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SRF-H014 | Knit with 'Make solid if closed' off leaves a closed shell, not a solid")]
    [Trait("TestId", "SRF-H014")]
    public void KnitWithoutSolid()
    {
        var session = new CadSession();
        var faces = BoxFacesAsSurfaces(session);
        var command = Begin(session, () => new SewCommand());
        Pick(command, "Surfaces", faces);
        SetToggle(command, "Make solid if closed", false);
        var shell = SingleSurface(BuildOutput(command));
        Assert.Equal(0, shell.Count(SubKind.Solid));
        Assert.Equal(1, shell.Count(SubKind.Shell));
        AssertNear(600, shell.Props().Area, "Shell area (mm²)");
    }

    [AvaloniaFact(DisplayName = "SRF-H015 | Knit tolerance bridges a small gap that the default tolerance leaves open")]
    [Trait("TestId", "SRF-H015")]
    public void KnitToleranceBridgesGap()
    {
        var session = new CadSession();
        var left = Sheet(session);
        var right = Sheet(session, x0: 10.2);
        int Shells(double tolerance)
        {
            var command = Begin(session, () => new SewCommand());
            Pick(command, "Surfaces", left, right);
            SetNumber(command, "Tolerance", tolerance);
            var shape = SingleSurface(BuildOutput(command));
            session.CancelCommand();
            return shape.Count(SubKind.Edge);
        }
        // Sewn sheets share their middle edge, so they have fewer edges than two separate squares.
        Assert.Equal(8, Shells(0.01));
        Assert.True(Shells(0.5) < 8);
    }

    // ------------------------------------------------------------------ Trim Surface

    [AvaloniaTheory(DisplayName = "SRF-H016 | Trim Surface with a solid keeps the part outside (front) or inside (back) it")]
    [InlineData(true, 0, 5)]
    [InlineData(false, 5, 10)]
    [Trait("TestId", "SRF-H016")]
    public void TrimSurfaceWithSolid(bool front, double minX, double maxX)
    {
        var session = new CadSession();
        var sheet = Sheet(session);
        var tool = CreateBoxBody(session, new Vec3(5, -5, -5), 20, 20, 10);
        var command = Begin(session, () => new TrimSurfaceCommand());
        Pick(command, "Surface to trim", sheet);
        Pick(command, "Cutting tool (plane, surface, face or solid)", tool);
        SetChoice(command, "Keep", front
            ? "The front side (where the tool's normal points, or outside a solid)"
            : "The back side (the other side, or inside a solid)");
        var (old, trimmed) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(sheet.Id, old.Id);
        AssertNear(50, trimmed.Props().Area, "Kept area (mm²)");
        AssertNear(minX, trimmed.Props().Min.X, "Kept minimum X (mm)");
        AssertNear(maxX, trimmed.Props().Max.X, "Kept maximum X (mm)");
    }

    [AvaloniaFact(DisplayName = "SRF-H017 | Trim Surface with a crossing surface keeps one of two pieces; Keep flips the side")]
    [Trait("TestId", "SRF-H017")]
    public void TrimSurfaceWithSurface()
    {
        var session = new CadSession();
        var sheet = Sheet(session);
        using var wall = Occ.Polyline([new(4, -5, -5), new(4, 15, -5), new(4, 15, 5), new(4, -5, 5)], true);
        var cutter = session.AddObject(Occ.Profile([wall], true), "Wall", ObjKind.Surface);
        var areas = new List<double>();
        foreach (var keep in new[] { "The front side (where the tool's normal points, or outside a solid)", "The back side (the other side, or inside a solid)" })
        {
            var command = Begin(session, () => new TrimSurfaceCommand());
            Pick(command, "Surface to trim", sheet);
            Pick(command, "Cutting tool (plane, surface, face or solid)", cutter);
            SetChoice(command, "Keep", keep);
            areas.Add(Assert.Single(BuildOutput(command).Replace).New.Props().Area);
            Assert.Equal("2, keeping 1", Result(command, "Pieces"));
            session.CancelCommand();
        }
        Assert.Equal([40.0, 60.0], areas.Order().Select(a => Math.Round(a, 6)));
    }

    [AvaloniaFact(DisplayName = "SRF-H018 | Trim Surface reports a tool that does not cross the surface")]
    [Trait("TestId", "SRF-H018")]
    public void TrimSurfaceRefusesMissingTool()
    {
        var session = new CadSession();
        var sheet = Sheet(session);
        using var wall = Occ.Polyline([new(40, -5, -5), new(40, 15, -5), new(40, 15, 5), new(40, -5, 5)], true);
        var cutter = session.AddObject(Occ.Profile([wall], true), "Far wall", ObjKind.Surface);
        var command = Begin(session, () => new TrimSurfaceCommand());
        Pick(command, "Surface to trim", sheet);
        Pick(command, "Cutting tool (plane, surface, face or solid)", cutter);
        Assert.Null(command.TryBuild());
        Assert.NotNull(command.Error);
    }

    // ------------------------------------------------------------------ Enclosed Body (Boundary Fill)

    [AvaloniaFact(DisplayName = "SRF-H019 | Enclosed Body splits a cube cut by a sheet into two 500 mm³ spaces")]
    [Trait("TestId", "SRF-H019")]
    public void EnclosedBodyFindsBothSpaces()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        using var loop = Occ.Polyline([new(-5, -5, 5), new(15, -5, 5), new(15, 15, 5), new(-5, 15, 5)], true);
        var sheet = session.AddObject(Occ.Profile([loop], true), "Divider", ObjKind.Surface);
        var command = Begin(session, () => new BoundaryFillCommand());
        Pick(command, "Surfaces, planes and bodies that enclose the space", box, sheet);
        var output = BuildOutput(command);
        Assert.Equal(2, output.Add.Count);
        Assert.All(output.Add, cell => AssertNear(500, cell.Shape!.Props().Volume, "Space volume (mm³)"));
        Assert.Equal("500 mm³", Result(command, "Space 1"));
        Assert.Equal("500 mm³", Result(command, "Space 2"));
    }

    [AvaloniaFact(DisplayName = "SRF-H020 | Enclosed Body 'Spaces to keep' selects listed spaces and rejects missing ones")]
    [Trait("TestId", "SRF-H020")]
    public void EnclosedBodyKeepsListedSpaces()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        using var loop = Occ.Polyline([new(-5, -5, 5), new(15, -5, 5), new(15, 15, 5), new(-5, 15, 5)], true);
        var sheet = session.AddObject(Occ.Profile([loop], true), "Divider", ObjKind.Surface);
        var command = Begin(session, () => new BoundaryFillCommand());
        Pick(command, "Surfaces, planes and bodies that enclose the space", box, sheet);
        SetText(command, "Spaces to keep (blank = all)", "2");
        Assert.Single(BuildOutput(command).Add);
        SetText(command, "Spaces to keep (blank = all)", "7");
        AssertBuildFails(command, "None of the listed spaces");
    }

    [AvaloniaFact(DisplayName = "SRF-H021 | Enclosed Body reports surfaces that enclose nothing")]
    [Trait("TestId", "SRF-H021")]
    public void EnclosedBodyRefusesOpenSet()
    {
        var session = new CadSession();
        var command = Begin(session, () => new BoundaryFillCommand());
        Pick(command, "Surfaces, planes and bodies that enclose the space", Sheet(session));
        AssertBuildFails(command, "do not enclose");
    }

    // ------------------------------------------------------------------ Unsew / Untrim

    [AvaloniaFact(DisplayName = "SRF-H022 | Unsew breaks a whole cube into six separate surfaces")]
    [Trait("TestId", "SRF-H022")]
    public void UnsewWholeBody()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new UnstitchCommand());
        Pick(command, "Faces, or whole surfaces and bodies", box);
        var output = BuildOutput(command);
        Assert.Equal(6, output.Add.Count);
        Assert.All(output.Add, face => AssertNear(100, face.Shape!.Props().Area, "Face area (mm²)"));
        Assert.Equal(box.Id, Assert.Single(output.Remove).Id);
        Assert.Equal("6", Result(command, "Separate surfaces"));
    }

    [AvaloniaFact(DisplayName = "SRF-H023 | Unsew pulls one picked face out and keeps the other five sewn together")]
    [Trait("TestId", "SRF-H023")]
    public void UnsewOneFace()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new UnstitchCommand());
        Pick(command, "Faces, or whole surfaces and bodies", FaceFacing(box, Vec3.UnitZ));
        var output = BuildOutput(command);
        Assert.Equal(2, output.Add.Count);
        AssertNear(100, output.Add[0].Shape!.Props().Area, "Pulled face (mm²)");
        AssertNear(500, output.Add[1].Shape!.Props().Area, "Remaining sewn faces (mm²)");
        Assert.Equal("1", Result(command, "Separate surfaces"));
    }

    [AvaloniaFact(DisplayName = "SRF-H024 | Unsew leaves a single-face surface alone and explains why")]
    [Trait("TestId", "SRF-H024")]
    public void UnsewSingleFace()
    {
        var session = new CadSession();
        var sheet = Sheet(session);
        var command = Begin(session, () => new UnstitchCommand());
        Pick(command, "Faces, or whole surfaces and bodies", sheet);
        Assert.Null(command.TryBuild());
        Assert.Null(command.Error);
        Assert.Contains("only one face", Result(command, sheet.Name));
    }

    [AvaloniaFact(DisplayName = "SRF-H025 | Untrim 'Fill the holes only' closes a cut-out in a flat sheet")]
    [Trait("TestId", "SRF-H025")]
    public void UntrimFillsHoles()
    {
        var session = new CadSession();
        using var outer = Occ.Polyline([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)], true);
        using var inner = Occ.Polyline([new(3, 3, 0), new(7, 3, 0), new(7, 7, 0), new(3, 7, 0)], true);
        var holed = session.AddObject(Occ.Profile([outer, inner], true), "Holed sheet", ObjKind.Surface);
        AssertNear(84, holed.Shape.Props().Area, "Starting area (mm²)");
        var command = Begin(session, () => new UntrimCommand());
        Pick(command, "Surface or face", holed);
        SetChoice(command, "What to restore", "Fill the holes only (keep the outline)");
        var (old, filled) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(holed.Id, old.Id);
        AssertNear(100, filled.Props().Area, "Filled area (mm²)");
    }

    [AvaloniaFact(DisplayName = "SRF-H026 | Untrim 'The whole natural surface' restores a trimmed NURBS patch")]
    [Trait("TestId", "SRF-H026")]
    public void UntrimRestoresNaturalSurface()
    {
        var session = new CadSession();
        using var patch = Occ.NurbsSurface([new(0, 0, 0), new(10, 0, 0), new(0, 10, 0), new(10, 10, 0)], 2, 2, 1, 1);
        using var cutter = BoxAt(new Vec3(5, -5, -5), 10, 20, 10);
        var trimmed = session.AddObject(Occ.Cut(patch, cutter), "Trimmed patch", ObjKind.Surface);
        AssertNear(50, trimmed.Shape.Props().Area, "Trimmed area (mm²)");
        AssertNear(5, trimmed.Shape.Props().Max.X, "Trimmed edge (mm)");
        var command = Begin(session, () => new UntrimCommand());
        Pick(command, "Surface or face", trimmed);
        var (_, restored) = Assert.Single(BuildOutput(command).Replace);
        AssertNear(100, restored.Props().Area, "Restored area (mm²)");
    }

    // ------------------------------------------------------------------ Extend / Reverse Normal

    [AvaloniaFact(DisplayName = "SRF-H027 | Extend grows a 10 mm NURBS sheet by 5 mm past the picked edge")]
    [Trait("TestId", "SRF-H027")]
    public void ExtendSurfacePastEdge()
    {
        var session = new CadSession();
        var sheet = NurbsSheet(session, 0, 10);
        var command = Begin(session, () => new ExtendSurfaceCommand());
        Pick(command, "Surface or face", sheet);
        Pick(command, "Edge to extend", EdgeAtX(sheet, 10));
        SetNumber(command, "Extend by", 5);
        Assert.NotNull(command.Handle);
        var (old, extended) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(sheet.Id, old.Id);
        AssertClose(150, extended.Props().Area, 1e-3, "Extended area (mm²)");
        AssertClose(15, extended.Props().Max.X, 1e-3, "Extended edge position (mm)");
        AssertClose(0, extended.Props().Min.X, 1e-3, "Opposite edge stays (mm)");
    }

    [AvaloniaFact(DisplayName = "SRF-H028 | Reverse Normal flips a sheet so its normal points the other way")]
    [Trait("TestId", "SRF-H028")]
    public void ReverseNormalFlipsSheet()
    {
        var session = new CadSession();
        var sheet = Sheet(session);
        using var original = sheet.Shape.Sub(SubKind.Face, 0);
        var before = Occ.FaceInfoOf(original).Normal;
        var command = Begin(session, () => new ReverseNormalCommand());
        Pick(command, "Surfaces", sheet);
        var (old, flipped) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(sheet.Id, old.Id);
        using var face = flipped.Sub(SubKind.Face, 0);
        AssertNear(-1, Vec3.Dot(before, Occ.FaceInfoOf(face).Normal), "Normal dot product");
        AssertNear(100, flipped.Props().Area, "Area unchanged (mm²)");
    }

    // ------------------------------------------------------------------ Contour

    [AvaloniaTheory(DisplayName = "CRV-H031 | Contour slices a 10 × 20 × 30 box at 5 mm spacing along the chosen axis")]
    [InlineData("X", 1)]
    [InlineData("Y", 3)]
    [InlineData("Z", 5)]
    [Trait("TestId", "CRV-H031")]
    public void ContourSlicesAlongAxis(string axis, int slices)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 20, 30);
        var command = Begin(session, () => new ContourCommand());
        Pick(command, "Body or surface", box);
        SetChoice(command, "Slice along", axis);
        SetNumber(command, "Spacing", 5);
        var output = BuildOutput(command);
        Assert.Equal(slices, output.Add.Count);
        Assert.All(output.Add, c => Assert.Equal(ObjKind.Curve, c.Kind));
        Assert.Equal(slices.ToString(), Result(command, "Slices"));
    }

    [AvaloniaFact(DisplayName = "CRV-H032 | Contour refuses a spacing that would make more than 300 slices")]
    [Trait("TestId", "CRV-H032")]
    public void ContourRefusesTooManySlices()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 30);
        var command = Begin(session, () => new ContourCommand());
        Pick(command, "Body or surface", box);
        SetNumber(command, "Spacing", 0.05);
        AssertBuildFails(command, "more than 300 slices");
    }
}
