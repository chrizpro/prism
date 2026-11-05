using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Cam;

/// <summary>
/// Characterizes the CNC operations on a 40 mm square outline at Z = 0 with a Ø6 tool: Profile (sides, direction, tabs),
/// Pocket, Adaptive Clearing, Drill (pecks), Face, 3D Finish and Turning, plus tool-library presets. Assertions read
/// the generated moves rather than G-code text.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cam/README.md">CAM guide.</seealso>
[Trait("Feature", "CAM")]
[Trait("Layer", "Headless")]
public class CncOperationTests
{
    static SceneObject Square(CadSession session, double size = 40) =>
        CreateCurve(session, [new(0, 0, 0), new(size, 0, 0), new(size, size, 0), new(0, size, 0)], closed: true);

    static ToolpathInfo Toolpath(Command command)
    {
        var added = Assert.Single(BuildOutput(command).Add);
        Assert.Equal(ObjKind.Toolpath, added.Kind);
        return Assert.IsType<ToolpathInfo>(added.Asm);
    }

    static List<(double x, double y, double z)> Moves(ToolpathInfo path, MoveKind kind) =>
        Enumerable.Range(0, path.MoveCount).Select(path.Move).Where(m => m.kind == kind).Select(m => (m.x, m.y, m.z)).ToList();

    static List<(double x, double y, double z)> Cuts(ToolpathInfo path) => Moves(path, MoveKind.Cut);

    static void AssertCutBox(ToolpathInfo path, double min, double max, string what)
    {
        var cuts = Cuts(path);
        Assert.NotEmpty(cuts);
        AssertClose(min, cuts.Min(c => c.x), 0.01, what + " minimum X (mm)");
        AssertClose(max, cuts.Max(c => c.x), 0.01, what + " maximum X (mm)");
        AssertClose(min, cuts.Min(c => c.y), 0.01, what + " minimum Y (mm)");
        AssertClose(max, cuts.Max(c => c.y), 0.01, what + " maximum Y (mm)");
    }

    static double SignedArea(IReadOnlyList<(double x, double y, double z)> loop)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++) { var p = loop[i]; var q = loop[(i + 1) % loop.Count]; a += p.x * q.y - q.x * p.y; }
        return a / 2;
    }

    static ProfileCommand Profile(CadSession session, string side, string direction = "Climb", double depth = 6)
    {
        var command = Begin(session, () => new ProfileCommand());
        Pick(command, "Outlines (curves, sketches, edges or flat faces)", Square(session));
        SetChoice(command, "Cut", side);
        SetChoice(command, "Direction", direction);
        SetNumber(command, "Total cutting depth", depth);
        return command;
    }

    // ------------------------------------------------------------------ Profile

    [AvaloniaTheory(DisplayName = "CNC-H001 | Profile offsets the tool by its radius for each cut side")]
    [InlineData("Cut out the part (outside the outline, inside the holes)", -3, 43)]
    [InlineData("Outside the line", -3, 43)]
    [InlineData("Inside the line", 3, 37)]
    [InlineData("On the line", 0, 40)]
    [Trait("TestId", "CNC-H001")]
    public void ProfileSides(string side, double min, double max)
    {
        var session = new CadSession();
        var command = Profile(session, side);
        var path = Toolpath(command);
        AssertCutBox(path, min, max, side);
        Assert.Equal(3, path.LayerCount);
        AssertNear(-6, Cuts(path).Min(c => c.z), "Final depth (mm)");
        Assert.StartsWith("about ", Result(command, "Machining time"));
    }

    [AvaloniaFact(DisplayName = "CNC-H002 | Profile depth per pass sets the number of passes down to the total depth")]
    [Trait("TestId", "CNC-H002")]
    public void ProfilePasses()
    {
        var session = new CadSession();
        var command = Profile(session, "On the line", depth: 5);
        SetNumber(command, "Depth per pass", 1.5);
        var path = Toolpath(command);
        Assert.Equal(4, path.LayerCount);
        Assert.Equal([-5.0, -4.5, -3.0, -1.5], Cuts(path).Select(c => Math.Round(c.z, 6)).Distinct().Order());
    }

    [AvaloniaFact(DisplayName = "CNC-H003 | Profile climb and conventional run an outside cut in opposite directions")]
    [Trait("TestId", "CNC-H003")]
    public void ProfileDirection()
    {
        double Winding(string side, string direction) =>
            SignedArea(Cuts(Toolpath(Profile(new CadSession(), side, direction, depth: 2))));
        Assert.True(Winding("Outside the line", "Climb") > 0, "Climb outside cuts run counter-clockwise.");
        Assert.True(Winding("Outside the line", "Conventional") < 0, "Conventional outside cuts run clockwise.");
        Assert.True(Winding("Inside the line", "Climb") < 0, "Climb inside cuts run clockwise.");
    }

    [AvaloniaFact(DisplayName = "CNC-H004 | Profile holding tabs stay at the tab height on the last pass")]
    [Trait("TestId", "CNC-H004")]
    public void ProfileTabs()
    {
        var session = new CadSession();
        var command = Profile(session, "Outside the line");
        SetInteger(command, "Holding tabs", 4);
        SetNumber(command, "Tab width", 6);
        SetNumber(command, "Tab height", 2);
        var cuts = Cuts(Toolpath(command));
        int lastPass = cuts.FindIndex(c => Math.Abs(c.z - -6) < 1e-9);
        Assert.True(lastPass >= 0, "The last pass reaches the full depth.");
        // During the last pass the tool climbs to the tab top (Z = -6 + 2) over each of the four tabs.
        Assert.True(cuts.Skip(lastPass).Count(c => Math.Abs(c.z - -4) < 1e-9) >= 4);
    }

    [AvaloniaFact(DisplayName = "CNC-H005 | Profile refuses an open outline")]
    [Trait("TestId", "CNC-H005")]
    public void ProfileRefusesOpen()
    {
        var session = new CadSession();
        var open = CreateCurve(session, [new(0, 0, 0), new(40, 0, 0), new(40, 40, 0)]);
        var command = Begin(session, () => new ProfileCommand());
        Pick(command, "Outlines (curves, sketches, edges or flat faces)", open);
        Assert.Null(command.TryBuild());
        Assert.NotNull(command.Error);
    }

    // ------------------------------------------------------------------ Pocket / Adaptive

    [AvaloniaTheory(DisplayName = "CNC-H006 | Pocket clears inward in rings; a smaller stepover makes more rings")]
    [InlineData(45)]
    [InlineData(20)]
    [Trait("TestId", "CNC-H006")]
    public void PocketRings(double stepover)
    {
        var session = new CadSession();
        var command = Begin(session, () => new PocketCommand());
        Pick(command, "Outlines of the pocket (islands inside are left)", Square(session));
        SetNumber(command, "Stepover", stepover);
        var path = Toolpath(command);
        AssertCutBox(path, 3, 37, "Pocket");
        int rings = int.Parse(Result(command, "Rings"));
        // Rings continue until the inward offset (3 mm + k × stepover) reaches the 20 mm half-width.
        Assert.Equal((int)Math.Ceiling(17 / (6 * stepover / 100)), rings);
    }

    [AvaloniaFact(DisplayName = "CNC-H007 | Pocket leaves an island inside the outline standing")]
    [Trait("TestId", "CNC-H007")]
    public void PocketIsland()
    {
        var session = new CadSession();
        var outer = Square(session);
        var island = CreateCurve(session, [new(15, 15, 0), new(25, 15, 0), new(25, 25, 0), new(15, 25, 0)], closed: true);
        var command = Begin(session, () => new PocketCommand());
        Pick(command, "Outlines of the pocket (islands inside are left)", outer, island);
        var cuts = Cuts(Toolpath(command));
        // Every cutter centre stays at least one tool radius (3 mm) from the 15..25 mm island square.
        static double DistanceToIsland((double x, double y, double z) c) =>
            Math.Sqrt(Math.Pow(Math.Max(0, Math.Max(15 - c.x, c.x - 25)), 2) + Math.Pow(Math.Max(0, Math.Max(15 - c.y, c.y - 25)), 2));
        Assert.All(cuts, c => Assert.True(DistanceToIsland(c) > 3 - 0.01, $"Cut at ({c.x:0.###}, {c.y:0.###}) gouges the island."));
    }

    [AvaloniaFact(DisplayName = "CNC-H008 | Pocket refuses an outline narrower than the tool")]
    [Trait("TestId", "CNC-H008")]
    public void PocketTooNarrow()
    {
        var session = new CadSession();
        var command = Begin(session, () => new PocketCommand());
        Pick(command, "Outlines of the pocket (islands inside are left)", Square(session, 4));
        AssertBuildFails(command, "too narrow");
    }

    [AvaloniaFact(DisplayName = "CNC-H009 | Adaptive Clearing cuts at full depth per pass and optionally finishes the walls")]
    [Trait("TestId", "CNC-H009")]
    public void AdaptiveClearing()
    {
        List<(double x, double y, double z)> Run(bool finish)
        {
            var session = new CadSession();
            var command = Begin(session, () => new AdaptiveClearCommand());
            Pick(command, "Outlines of the pocket (islands inside are left)", Square(session));
            SetToggle(command, "Finish the walls with one pass", finish);
            var path = Toolpath(command);
            Assert.Equal(2, path.LayerCount);
            return Cuts(path);
        }
        var finished = Run(true);
        var rough = Run(false);
        Assert.Equal([-12.0, -6.0], finished.Select(c => Math.Round(c.z, 6)).Distinct().Order());
        Assert.All(finished, c => Assert.InRange(c.x, 2.99, 37.01));
        Assert.All(finished, c => Assert.InRange(c.y, 2.99, 37.01));
        Assert.True(finished.Count > rough.Count, "The wall finish adds a pass around the reachable outline at each depth.");
    }

    // ------------------------------------------------------------------ Drill

    static (CadSession Session, SceneObject Plate) DrilledPlate()
    {
        var session = new CadSession();
        using var blank = BoxAt(new Vec3(0, 0, -10), 40, 20, 10);
        using var hole = CylinderAt(new Vec3(10, 10, -11), 3, 12);
        return (session, session.AddObject(Occ.Cut(blank, hole), "Plate", ObjKind.Solid));
    }

    static SelItem HoleWall(SceneObject body) => new(body.Id, SelKind.Face, Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(i =>
    {
        using var face = body.Shape.Sub(SubKind.Face, i);
        return Occ.FaceInfoOf(face).Type == "Cylinder";
    }));

    [AvaloniaFact(DisplayName = "CNC-H010 | Drill plunges at the hole centre through its full depth and warns about a mismatched tool")]
    [Trait("TestId", "CNC-H010")]
    public void DrillHole()
    {
        var (session, plate) = DrilledPlate();
        var command = Begin(session, () => new DrillCommand());
        Pick(command, "Holes (round faces or circular edges)", HoleWall(plate));
        var path = Toolpath(command);
        var plunge = Assert.Single(Moves(path, MoveKind.Plunge));
        AssertClose(10, plunge.x, 1e-6, "Hole centre X (mm)");
        AssertClose(10, plunge.y, 1e-6, "Hole centre Y (mm)");
        AssertClose(-10, plunge.z, 1e-6, "Hole bottom (mm)");
        Assert.Equal("1  (Ø6)", Result(command, "Holes"));
        Assert.True(Assert.Single(command.Results, r => r.Label == "Note").IsWarn);
    }

    [AvaloniaFact(DisplayName = "CNC-H011 | Drill peck depth backs out between pecks; matching the tool removes the warning")]
    [Trait("TestId", "CNC-H011")]
    public void DrillPecks()
    {
        var (session, plate) = DrilledPlate();
        var command = Begin(session, () => new DrillCommand());
        Pick(command, "Holes (round faces or circular edges)", HoleWall(plate));
        SetNumber(command, "Tool diameter", 6);
        SetNumber(command, "Peck depth (0 = straight in)", 3);
        var plunges = Moves(Toolpath(command), MoveKind.Plunge).Select(m => Math.Round(m.z, 6)).ToList();
        Assert.Equal([-3.0, -6.0, -9.0, -10.0], plunges);
        Assert.DoesNotContain(command.Results, r => r.Label == "Note");
    }

    [AvaloniaFact(DisplayName = "CNC-H012 | Drill uses the operation depth when 'as deep as it is' is off")]
    [Trait("TestId", "CNC-H012")]
    public void DrillFixedDepth()
    {
        var (session, plate) = DrilledPlate();
        var command = Begin(session, () => new DrillCommand());
        Pick(command, "Holes (round faces or circular edges)", HoleWall(plate));
        SetToggle(command, "Drill each hole as deep as it is", false);
        SetNumber(command, "Top of the material (Z)", 0);
        SetNumber(command, "Total cutting depth", 4);
        var plunge = Assert.Single(Moves(Toolpath(command), MoveKind.Plunge));
        AssertClose(-4, plunge.z, 1e-6, "Fixed drilling depth (mm)");
    }

    // ------------------------------------------------------------------ Face / 3D finish

    [AvaloniaTheory(DisplayName = "CNC-H013 | Face mills the rectangle plus the tool radius in passes along the chosen axis")]
    [InlineData("X")]
    [InlineData("Y")]
    [Trait("TestId", "CNC-H013")]
    public void FaceMill(string along)
    {
        var session = new CadSession();
        var command = Begin(session, () => new FaceMillCommand());
        SetNumber(command, "X from", 0);
        SetNumber(command, "X to", 30);
        SetNumber(command, "Y from", 0);
        SetNumber(command, "Y to", 20);
        SetChoice(command, "Passes run along", along);
        var cuts = Cuts(Toolpath(command));
        AssertClose(-6, cuts.Min(c => c.x), 1e-6, "Faced minimum X (mm)");
        AssertClose(-6, cuts.Min(c => c.y), 1e-6, "Faced minimum Y (mm)");
        Assert.All(cuts, c => AssertNear(-1, c.z, "Facing depth (mm)"));
        // Passes run the full extended length along the chosen axis and are spaced 70% of Ø12 across it;
        // the last pass reaches within one tool radius of the far edge.
        if (along == "X")
        {
            AssertClose(36, cuts.Max(c => c.x), 1e-6, "Pass end X (mm)");
            Assert.True(cuts.Max(c => c.y) + 6 >= 20);
            Assert.Equal(4, cuts.Select(c => Math.Round(c.y, 6)).Distinct().Count());
        }
        else
        {
            AssertClose(26, cuts.Max(c => c.y), 1e-6, "Pass end Y (mm)");
            Assert.True(cuts.Max(c => c.x) + 6 >= 30);
            Assert.Equal(6, cuts.Select(c => Math.Round(c.x, 6)).Distinct().Count());
        }
    }

    [AvaloniaTheory(DisplayName = "CNC-H014 | 3D Finish follows the flat top of a block with ball nose or flat cutters")]
    [InlineData("Ball nose")]
    [InlineData("Flat end mill")]
    [Trait("TestId", "CNC-H014")]
    public void ParallelFinishFlatTop(string cutter)
    {
        var session = new CadSession();
        var block = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var command = Begin(session, () => new ParallelFinishCommand());
        Pick(command, "Model to finish", block);
        SetChoice(command, "Cutter", cutter);
        SetNumber(command, "Stepover", 50);
        var path = Toolpath(command);
        var cuts = Cuts(path);
        // Level stretches are reduced to their end points, so passes across the middle of the top cut at Z = 10.
        Assert.Contains(cuts, c => Math.Abs(c.z - 10) < 0.01 && c.y > 5 && c.y < 15);
        Assert.All(cuts, c => Assert.True(c.z > -0.01, "The tool never goes below the model."));
        Assert.Contains(cutter == "Ball nose" ? "ball nose" : "end mill", path.Tool);
        Assert.True(int.Parse(Result(command, "Passes")) >= 7);
    }

    [AvaloniaFact(DisplayName = "CNC-H015 | 3D Finish refuses a material top below the model")]
    [Trait("TestId", "CNC-H015")]
    public void ParallelFinishNeedsClearance()
    {
        var session = new CadSession();
        var block = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var command = Begin(session, () => new ParallelFinishCommand());
        Pick(command, "Model to finish", block);
        SetNumber(command, "Top of the material (Z)", 0);
        AssertBuildFails(command, "Raise 'Top of the material'");
    }

    // ------------------------------------------------------------------ Turning

    static SceneObject StepShaftProfile(CadSession session)
    {
        // Half section on XZ (u = radius, v = axial Z): Ø20 for 20 mm, then Ø12 to the face at Z = 30.
        var model = new SketchModel();
        int a = model.AddPoint(0, 0), b = model.AddPoint(10, 0), c = model.AddPoint(10, 20), d = model.AddPoint(6, 20), e = model.AddPoint(6, 30), f = model.AddPoint(0, 30);
        model.AddLine(a, b); model.AddLine(b, c); model.AddLine(c, d); model.AddLine(d, e); model.AddLine(e, f); model.AddLine(f, a);
        session.EnterSketch(Plane3.XZ, model);
        session.ExitSketch(keep: true);
        return session.Objects.Last(o => o.Kind == ObjKind.Sketch);
    }

    [AvaloniaFact(DisplayName = "CNC-H016 | Turning faces, roughs and finishes a stepped shaft from automatic stock")]
    [Trait("TestId", "CNC-H016")]
    public void TurningAllStages()
    {
        var session = new CadSession();
        var command = Begin(session, () => new TurnCommand());
        Pick(command, "Profile of the part (half section on the XZ plane)", StepShaftProfile(session));
        // The first build sets "Top of the material" from the profile, which re-enters the preview and duplicates the
        // result rows; a second build reports them once. See the CAM guide's coverage gaps.
        BuildOutput(command);
        var path = Toolpath(command);
        Assert.Equal(3, path.LayerCount);
        Assert.Equal("20 mm", Result(command, "Largest diameter"));
        Assert.True(int.Parse(Result(command, "Roughing cuts")) > 0);
        // The finish pass (the third layer) runs along the profile: radius 10 up to Z = 20, then radius 6 to the face.
        var finish = Enumerable.Range(path.LayerStarts[2], path.MoveCount - path.LayerStarts[2]).Select(path.Move)
            .Where(m => m.kind == MoveKind.Cut && m.z > 19.99 && m.z < 30.01).ToList();
        AssertClose(6, finish.Min(c => c.x), 1e-3, "Finished small radius (mm)");
        Assert.Contains(finish, c => Math.Abs(c.x - 10) < 1e-3);
        Assert.Equal("Turning tool", path.Tool);
    }

    [AvaloniaTheory(DisplayName = "CNC-H017 | Turning stages can be switched off individually")]
    [InlineData("Face the end")]
    [InlineData("Rough turn")]
    [InlineData("Finish turn")]
    [Trait("TestId", "CNC-H017")]
    public void TurningStages(string stage)
    {
        var session = new CadSession();
        var command = Begin(session, () => new TurnCommand());
        Pick(command, "Profile of the part (half section on the XZ plane)", StepShaftProfile(session));
        SetToggle(command, stage, false);
        Assert.Equal(2, Toolpath(command).LayerCount);
        if (stage == "Rough turn") Assert.Equal("0", Result(command, "Roughing cuts"));
    }

    [AvaloniaFact(DisplayName = "CNC-H018 | Turning refuses stock that is not bigger than the part")]
    [Trait("TestId", "CNC-H018")]
    public void TurningStockTooSmall()
    {
        var session = new CadSession();
        var command = Begin(session, () => new TurnCommand());
        Pick(command, "Profile of the part (half section on the XZ plane)", StepShaftProfile(session));
        SetNumber(command, "Stock diameter (0 = automatic)", 20);
        AssertBuildFails(command, "stock is not bigger");
    }

    // ------------------------------------------------------------------ Tool library presets

    [AvaloniaFact(DisplayName = "CNC-H019 | Choosing a library tool fills in its diameter and speeds")]
    [Trait("TestId", "CNC-H019")]
    public void ToolLibraryPreset()
    {
        var session = new CadSession();
        var command = Begin(session, () => new PocketCommand());
        var preset = command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Tool from the library");
        int index = preset.Options.ToList().FindIndex(o => o.StartsWith("3 mm flat end mill"));
        Assert.True(index > 0);
        var tool = CamToolLibrary.Tools[index - 1];
        preset.Index = index;
        Assert.Equal(tool.Diameter, command.Params.OfType<NumberParam>().Single(p => p.Label == "Tool diameter").Value);
        Assert.Equal(tool.Rpm, command.Params.OfType<NumberParam>().Single(p => p.Label == "Spindle speed").Value);
        Assert.Equal(tool.Feed, command.Params.OfType<NumberParam>().Single(p => p.Label == "Cutting feed").Value);
        Assert.Equal(tool.StepDown, command.Params.OfType<NumberParam>().Single(p => p.Label == "Depth per pass").Value);
    }
}
