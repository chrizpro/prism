using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <summary>
/// Characterizes the Inspect tools shared across ribbons: Interference, Check Geometry, Curvature Map, Draft Analysis,
/// Surface Continuity and Match Surface. Analysis tools report rows and leave the document unchanged.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/solid/README.md">Living guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class AnalysisToolTests
{
    // ------------------------------------------------------------------ Interference

    [AvaloniaFact(DisplayName = "INS-H001 | Interference reports the 125 mm³ overlap of two offset cubes without changing the document")]
    [Trait("TestId", "INS-H001")]
    public void InterferenceReportsOverlap()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var second = CreateBoxBody(session, new Vec3(5, 5, 5), 10, 10, 10);
        var command = Begin(session, () => new InterferenceCommand());
        Pick(command, "Bodies to check (none = all solids)", first, second);
        var output = BuildOutput(command);
        Assert.True(output.NoOp);
        Assert.Equal("125 mm³", Result(command, $"{first.Name} ∩ {second.Name}"));
        Assert.Equal("1 of 1", Result(command, "Overlapping pairs"));
    }

    [AvaloniaFact(DisplayName = "INS-H002 | Interference can create the overlapping volume as a body")]
    [Trait("TestId", "INS-H002")]
    public void InterferenceCreatesBodies()
    {
        var session = new CadSession();
        CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        CreateBoxBody(session, new Vec3(5, 5, 5), 10, 10, 10);
        var command = Begin(session, () => new InterferenceCommand());
        SetToggle(command, "Create the overlapping volumes as bodies", true);
        var output = BuildOutput(command);
        Assert.False(output.NoOp);
        var overlap = Assert.Single(output.Add).Shape!;
        AssertValidSolid(overlap);
        AssertBounds(overlap, new Vec3(5, 5, 5), new Vec3(10, 10, 10));
    }

    [AvaloniaFact(DisplayName = "INS-H003 | Interference with nothing picked checks every solid and reports separated bodies as clear")]
    [Trait("TestId", "INS-H003")]
    public void InterferenceChecksAllWhenEmpty()
    {
        var session = new CadSession();
        CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        CreateBoxBody(session, new Vec3(20, 0, 0), 10, 10, 10);
        CreateBoxBody(session, new Vec3(40, 0, 0), 10, 10, 10);
        var command = Begin(session, () => new InterferenceCommand());
        BuildOutput(command);
        var result = Assert.Single(command.Results, line => line.Label == "Result");
        Assert.Equal("No interference between 3 bodies (3 pairs checked)", result.Value);
        Assert.True(result.IsGood);
    }

    [AvaloniaFact(DisplayName = "INS-H004 | Interference asks for a second body when only one exists")]
    [Trait("TestId", "INS-H004")]
    public void InterferenceNeedsTwoBodies()
    {
        var session = new CadSession();
        CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new InterferenceCommand());
        BuildOutput(command);
        Assert.True(Assert.Single(command.Results, line => line.Label == "Need two bodies").IsWarn);
    }

    // ------------------------------------------------------------------ Check Geometry

    [AvaloniaFact(DisplayName = "INS-H005 | Check Geometry reports a cube as valid, watertight and healthy")]
    [Trait("TestId", "INS-H005")]
    public void CheckReportsHealthyCube()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new CheckCommand());
        Pick(command, "Bodies (none = all)", box);
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("OK", Result(command, box.Name));
        Assert.Equal("Yes (closed)", Result(command, "   Watertight"));
        Assert.Equal("1 solid, 1 shell, 6 faces, 12 edges", Result(command, "   Contents"));
        Assert.Equal("All 1 body is healthy", Result(command, "Summary"));
    }

    [AvaloniaFact(DisplayName = "INS-H006 | Check Geometry flags the open edges of a box shell missing its top")]
    [Trait("TestId", "INS-H006")]
    public void CheckFlagsOpenShell()
    {
        var session = new CadSession();
        using var box = Occ.Box(10, 10, 10);
        var faces = Enumerable.Range(0, 6).Select(i => box.Sub(SubKind.Face, i))
            .Where(face => face.Props().Min.Z < 9.99).ToList();
        var open = session.AddObject(Occ.Sew(faces, 0.01, false), "Open box", ObjKind.Solid);
        var command = Begin(session, () => new CheckCommand());
        Pick(command, "Bodies (none = all)", open);
        BuildOutput(command);
        Assert.Equal("Problems found", Result(command, open.Name));
        Assert.Equal("No - 4 open edges", Result(command, "   Watertight"));
        Assert.Equal("1 of 1 need attention", Result(command, "Summary"));
    }

    [AvaloniaFact(DisplayName = "INS-H007 | Check Geometry accepts open edges on a surface and asks for a body when none exist")]
    [Trait("TestId", "INS-H007")]
    public void CheckAcceptsSurfaceEdges()
    {
        var session = new CadSession();
        var command = Begin(session, () => new CheckCommand());
        BuildOutput(command);
        Assert.True(Assert.Single(command.Results, line => line.Label == "No bodies").IsWarn);
        session.CancelCommand();

        using var loop = Occ.Polyline([Vec3.Zero, new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)], true);
        var sheet = session.AddObject(Occ.Profile([loop], true), "Sheet", ObjKind.Surface);
        command = Begin(session, () => new CheckCommand());
        Pick(command, "Bodies (none = all)", sheet);
        BuildOutput(command);
        Assert.Equal("OK", Result(command, sheet.Name));
        Assert.Equal("No - 4 open edges", Result(command, "   Watertight"));
    }

    // ------------------------------------------------------------------ Curvature Map / Draft Analysis

    [AvaloniaTheory(DisplayName = "INS-H008 | Curvature Map switches the viewport to curvature shading with automatic or fixed range")]
    [InlineData(true, "automatic")]
    [InlineData(false, "±1/40 mm")]
    [Trait("TestId", "INS-H008")]
    public void CurvatureMapSetsRange(bool automatic, string range)
    {
        var session = new CadSession();
        var command = Begin(session, () => new CurvatureCommand());
        SetToggle(command, "Automatic range", automatic);
        Assert.Equal(!automatic, command.Params.Single(p => p.Label == "Saturate at radius").IsVisible);
        SetNumber(command, "Saturate at radius", 40);
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal(CadSession.AnalysisKind.Curvature, session.Analysis);
        AssertNear(automatic ? 0 : 1.0 / 40, session.CurvRange, "Curvature saturation (1/mm)");
        Assert.Equal(range, Result(command, "Range"));
        session.CancelCommand();
        Assert.Equal(CadSession.AnalysisKind.None, session.Analysis);
    }

    [AvaloniaTheory(DisplayName = "INS-H009 | Draft Analysis uses the chosen pull direction and minimum angle")]
    [InlineData("Z (up)", 0, 0, 1)]
    [InlineData("-Z (down)", 0, 0, -1)]
    [InlineData("X", 1, 0, 0)]
    [InlineData("-X", -1, 0, 0)]
    [InlineData("Y", 0, 1, 0)]
    [InlineData("-Y", 0, -1, 0)]
    [Trait("TestId", "INS-H009")]
    public void DraftAnalysisDirection(string pull, double x, double y, double z)
    {
        var session = new CadSession();
        var command = Begin(session, () => new DraftCommand());
        SetChoice(command, "Pull direction", pull);
        SetNumber(command, "Minimum draft angle", 5);
        BuildOutput(command);
        Assert.Equal(CadSession.AnalysisKind.Draft, session.Analysis);
        AssertPoint(new Vec3(x, y, z), session.DraftDir, 1e-12, "Pull direction");
        AssertNear(5, session.DraftMin, "Minimum draft (°)");
        Assert.Equal("draft of at least 5° - releases cleanly", Result(command, "Green"));
    }

    // ------------------------------------------------------------------ Surface Continuity / Match Surface

    static SceneObject Patch(CadSession session, double x0, double z1, string name)
    {
        // A cubic-by-linear patch: four rows run along X, so U min/U max are its X = x0 and X = x0 + 10 boundaries and
        // there are enough rows for tangent and curvature matching to keep the far boundary fixed.
        var grid = new List<Vec3>();
        for (int row = 0; row < 4; row++)
        {
            double t = row / 3.0;
            grid.Add(new(x0 + 10 * t, 0, z1 * t));
            grid.Add(new(x0 + 10 * t, 10, z1 * t));
        }
        return session.AddObject(Occ.NurbsSurface([.. grid], 4, 2, 3, 1), name, ObjKind.Surface);
    }

    [AvaloniaFact(DisplayName = "INS-H010 | Surface Continuity reports a zero gap and zero angle along a shared flat seam")]
    [Trait("TestId", "INS-H010")]
    public void SurfaceContinuityAtFlatSeam()
    {
        var session = new CadSession();
        var left = Patch(session, 0, 0, "Left");
        var right = Patch(session, 10, 0, "Right");
        var command = Begin(session, () => new SurfaceContinuityCommand());
        Pick(command, "First surface or face", left);
        Pick(command, "Second surface or face", right);
        SetChoice(command, "First boundary", "U max");
        SetChoice(command, "Second boundary", "U min");
        SetInteger(command, "Seam samples", 11);
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("0 mm", Result(command, "Maximum gap"));
        Assert.Equal("0°", Result(command, "Maximum normal angle"));
        Assert.Equal("11", Result(command, "Samples"));
    }

    [AvaloniaFact(DisplayName = "INS-H011 | Surface Continuity measures a 2 mm gap between separated patches")]
    [Trait("TestId", "INS-H011")]
    public void SurfaceContinuityMeasuresGap()
    {
        var session = new CadSession();
        var left = Patch(session, 0, 0, "Left");
        var right = Patch(session, 12, 0, "Right");
        var command = Begin(session, () => new SurfaceContinuityCommand());
        Pick(command, "First surface or face", left);
        Pick(command, "Second surface or face", right);
        SetChoice(command, "First boundary", "U max");
        SetChoice(command, "Second boundary", "U min");
        BuildOutput(command);
        Assert.Equal("2 mm", Result(command, "Maximum gap"));
    }

    [AvaloniaTheory(DisplayName = "INS-H012 | Match Surface closes the gap to the reference seam at every continuity")]
    [InlineData("Position (G0)")]
    [InlineData("Tangent (G1)")]
    [InlineData("Curvature (G2)")]
    [Trait("TestId", "INS-H012")]
    public void MatchSurfaceClosesGap(string continuity)
    {
        var session = new CadSession();
        var reference = Patch(session, 0, 0, "Reference");
        var source = Patch(session, 12, 3, "Source");
        var command = Begin(session, () => new MatchSurfaceCommand());
        Pick(command, "Surface to change", source);
        Pick(command, "Reference surface or face", reference);
        SetChoice(command, "Source boundary", "U min");
        SetChoice(command, "Reference boundary", "U max");
        SetChoice(command, "Continuity", continuity);
        var (old, matched) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(source.Id, old.Id);
        AssertClose(10, matched.Props().Min.X, 1e-6, "Matched seam position (mm)");
        Assert.Equal("0 mm", Result(command, "Sampled gap"));
        if (continuity != "Position (G0)") Assert.Equal("0°", Result(command, "Sampled normal angle"));
    }

    [AvaloniaFact(DisplayName = "INS-H013 | Match Surface refuses the source as its own reference")]
    [Trait("TestId", "INS-H013")]
    public void MatchSurfaceRefusesSelf()
    {
        var session = new CadSession();
        var patch = Patch(session, 0, 0, "Only");
        var command = Begin(session, () => new MatchSurfaceCommand());
        Pick(command, "Surface to change", patch);
        Pick(command, "Reference surface or face", patch);
        AssertBuildFails(command, "different reference");
    }
}
