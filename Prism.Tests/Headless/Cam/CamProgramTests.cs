using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Cam;

/// <summary>
/// Characterizes the CAM program and manufacturing-check tools around the operations: 3D Rough, Tool Library, Select
/// Setup, Operation Order, Playback, Review, Post G-code, Stock Review, CNC Design Check, FDM and Resin Preparation.
/// The fixture is a 20 × 20 × 10 block with a saved setup (2 mm side, 1 mm top allowance).
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cam/README.md">CAM guide.</seealso>
[Trait("Feature", "CAM")]
[Trait("Layer", "Headless")]
public class CamProgramTests
{
    static (CadSession Session, SceneObject Block) BlockWithSetup(string name = "Setup 1")
    {
        var session = new CadSession();
        var block = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var setup = Begin(session, () => new CamSetupCommand());
        Pick(setup, "Model body", block);
        SetText(setup, "Setup name", name);
        SetNumber(setup, "Stock allowance XY", 2);
        SetNumber(setup, "Top allowance", 1);
        session.CommitCommand();
        Assert.NotNull(session.CamSetup);
        return (session, block);
    }

    static SceneObject FaceOperation(CadSession session)
    {
        var face = Begin(session, () => new FaceMillCommand());
        SetNumber(face, "Total cutting depth", 1);
        session.CommitCommand();
        return session.Objects.Last(o => o.Kind == ObjKind.Toolpath);
    }

    static void Run(Command command, string action) =>
        command.Params.OfType<ActionParam>().Single(a => a.Label == action).Run();

    // ------------------------------------------------------------------ 3D Rough

    [AvaloniaFact(DisplayName = "CAM-H101 | 3D Rough clears the setup stock down to the model, leaving the allowance")]
    [Trait("TestId", "CAM-H101")]
    public void RoughLeavesAllowance()
    {
        var (session, block) = BlockWithSetup();
        var command = Begin(session, () => new Rough3DCommand());
        Assert.Equal(block.Id, Assert.Single(command.Slots.Single(s => s.Label == "Model to rough").Items).Obj);
        SetNumber(command, "Stock to leave", 0.5);
        SetNumber(command, "Depth per pass", 5);
        var path = Assert.IsType<ToolpathInfo>(Assert.Single(BuildOutput(command).Add).Asm);
        var cuts = Enumerable.Range(0, path.MoveCount).Select(path.Move).Where(m => m.kind == MoveKind.Cut).ToList();
        Assert.NotEmpty(cuts);
        // No cutter centre above the block footprint (grown by the tool radius and allowance) goes below its top.
        Assert.All(cuts.Where(c => c.x > -3.4 && c.x < 23.4 && c.y > -3.4 && c.y < 23.4), c => Assert.True(c.z >= 10 - 1e-6));
        Assert.Contains(cuts, c => c.z < 5);
        Assert.Equal("0.5 mm", Result(command, "Stock to leave"));
    }

    [AvaloniaFact(DisplayName = "CAM-H102 | 3D Rough requires a CAM setup")]
    [Trait("TestId", "CAM-H102")]
    public void RoughNeedsSetup()
    {
        var session = new CadSession();
        var block = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var command = Begin(session, () => new Rough3DCommand());
        Pick(command, "Model to rough", block);
        AssertBuildFails(command, "Create rectangular stock in CAM setup");
    }

    // ------------------------------------------------------------------ Tool Library / Select Setup / Order

    [AvaloniaFact(DisplayName = "CAM-H103 | Tool Library copies a preset's values into the form")]
    [Trait("TestId", "CAM-H103")]
    public void ToolLibraryCopiesPreset()
    {
        var session = new CadSession();
        var command = Begin(session, () => new CamToolLibraryCommand());
        BuildOutput(command);
        Assert.Equal(CamToolLibrary.Tools.Length.ToString(), Result(command, "Available tools"));
        var tool = CamToolLibrary.Tools.First(t => t.Ball);
        var preset = command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Copy from");
        preset.Index = Array.IndexOf(CamToolLibrary.Tools, tool) + 1;
        Assert.Equal(tool.Name, command.Params.OfType<TextParam>().Single(p => p.Label == "Tool name / material").Value);
        Assert.Equal("Ball nose", command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Cutter shape").Selected);
        Assert.Equal(tool.Diameter, command.Params.OfType<NumberParam>().Single(p => p.Label == "Diameter").Value);
    }

    [AvaloniaFact(DisplayName = "CAM-H104 | Select CAM Setup switches the active setup between saved setups")]
    [Trait("TestId", "CAM-H104")]
    public void SelectSetupSwitches()
    {
        var (session, block) = BlockWithSetup("Top");
        var second = Begin(session, () => new CamSetupCommand());
        Pick(second, "Model body", block);
        SetText(second, "Setup name", "Bottom");
        session.CommitCommand();
        Assert.Equal("Bottom", session.CamSetup!.Name);
        var command = Begin(session, () => new SelectCamSetupCommand());
        BuildOutput(command);
        Assert.Equal("Bottom", Result(command, "Active setup"));
        Assert.Equal("2", Result(command, "Saved setups"));
        command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Saved setup").Index = 0;
        Run(command, "Use setup");
        Assert.Equal("Top", session.CamSetup!.Name);
    }

    [AvaloniaFact(DisplayName = "CAM-H105 | Operation Order moves an operation to the chosen position")]
    [Trait("TestId", "CAM-H105")]
    public void OperationOrder()
    {
        var (session, _) = BlockWithSetup();
        var first = FaceOperation(session);
        var second = FaceOperation(session);
        var command = Begin(session, () => new CamProgramOrderCommand());
        SetChoice(command, "Operation", $"2. {second.Name}");
        SetInteger(command, "Move to position", 1);
        BuildOutput(command);
        Assert.Equal(second.Name, Result(command, "1"));
        Assert.Equal(first.Name, Result(command, "2"));
        session.CommitCommand();
        Assert.Equal([second.Id, first.Id], session.MillingProgram().Select(o => o.Id));
    }

    [AvaloniaFact(DisplayName = "CAM-H106 | Operation Order asks for an operation when there are none")]
    [Trait("TestId", "CAM-H106")]
    public void OperationOrderEmpty()
    {
        var session = new CadSession();
        var command = Begin(session, () => new CamProgramOrderCommand());
        Assert.Null(command.TryBuild());
        Assert.Equal("Create a machining operation first.", Result(command, "Program"));
    }

    // ------------------------------------------------------------------ Playback / Review / Post

    [AvaloniaFact(DisplayName = "CAM-H107 | Toolpath Playback seeks along a path and reports the tool tip")]
    [Trait("TestId", "CAM-H107")]
    public void PlaybackSeek()
    {
        var (session, _) = BlockWithSetup();
        FaceOperation(session);
        var command = Begin(session, () => new CamPlaybackCommand());
        BuildOutput(command);
        Assert.Equal("Choose a path, then Play or Seek.", Result(command, "Playback"));
        SetNumber(command, "Seek position", 100);
        Run(command, "Seek");
        BuildOutput(command);
        Assert.Equal("Paused", Result(command, "Status"));
        Assert.StartsWith("X ", Result(command, "Tool tip"));
        session.CancelCommand();
        Assert.Null(session.CamTimeline);
    }

    [AvaloniaFact(DisplayName = "CAM-H108 | CAM Review lists each operation and flags one whose model changed")]
    [Trait("TestId", "CAM-H108")]
    public void ReviewFlagsStaleOperation()
    {
        var (session, block) = BlockWithSetup();
        var op = FaceOperation(session);
        var command = Begin(session, () => new CamReviewCommand());
        BuildOutput(command);
        Assert.Equal("Setup 1", Result(command, "Setup"));
        Assert.Contains("moves", Result(command, op.Name));
        session.CancelCommand();

        session.Update(block.Id, o => o.WithShape(BoxAt(Vec3.Zero, 20, 20, 12)));
        command = Begin(session, () => new CamReviewCommand());
        BuildOutput(command);
        Assert.Contains(command.Results, r => r.IsBad);
    }

    [AvaloniaFact(DisplayName = "CAM-H109 | CAM Review saves an HTML and JSON setup sheet")]
    [Trait("TestId", "CAM-H109")]
    public void ReviewSavesSetupSheet()
    {
        var (session, _) = BlockWithSetup();
        FaceOperation(session);
        var folder = Path.Combine(Path.GetTempPath(), $"prism-cam-{Guid.NewGuid():N}");
        try
        {
            var command = Begin(session, () => new CamReviewCommand());
            SetText(command, "Setup sheet folder", folder);
            Run(command, "Save setup sheet");
            Assert.Contains("<h1>Setup 1</h1>", File.ReadAllText(Path.Combine(folder, "setup.html")));
            Assert.True(File.Exists(Path.Combine(folder, "setup.json")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaTheory(DisplayName = "CAM-H110 | Post G-code writes the program for each controller")]
    [InlineData("GRBL (hobby routers and mills)")]
    [InlineData("Mach3")]
    [InlineData("LinuxCNC")]
    [Trait("TestId", "CAM-H110")]
    public void PostWritesProgram(string machine)
    {
        var (session, _) = BlockWithSetup();
        FaceOperation(session);
        var file = Path.Combine(Path.GetTempPath(), $"prism-post-{Guid.NewGuid():N}.nc");
        try
        {
            var command = Begin(session, () => new CncPostCommand());
            SetChoice(command, "Machine", machine);
            SetText(command, "Save to (blank = Documents\\Prism\\GCode)", file);
            Run(command, "Save G-code");
            var text = File.ReadAllText(file);
            Assert.Contains("G21", text);
            Assert.Contains("G1", text);
            Assert.Contains("M3", text);
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact(DisplayName = "CAM-H111 | Post G-code without operations writes nothing")]
    [Trait("TestId", "CAM-H111")]
    public void PostWithoutOperations()
    {
        var (session, _) = BlockWithSetup();
        var file = Path.Combine(Path.GetTempPath(), $"prism-post-{Guid.NewGuid():N}.nc");
        var command = Begin(session, () => new CncPostCommand());
        SetText(command, "Save to (blank = Documents\\Prism\\GCode)", file);
        Run(command, "Save G-code");
        Assert.False(File.Exists(file));
    }

    // ------------------------------------------------------------------ Stock review / manufacturing checks

    [AvaloniaFact(DisplayName = "CAM-H112 | Stock Review waits for Run checks, then reports removed and remaining stock")]
    [Trait("TestId", "CAM-H112")]
    public void StockReview()
    {
        var (session, block) = BlockWithSetup();
        FaceOperation(session);
        var command = Begin(session, () => new CncStockCommand());
        Pick(command, "Model", block);
        BuildOutput(command);
        Assert.Equal("Choose the model and settings, then Run checks.", Result(command, "Checks"));
        Run(command, "Run checks");
        BuildOutput(command);
        Assert.NotEqual("0 cm³", Result(command, "Removed stock"));
        Assert.Contains(command.Results, r => r.Label == "Possible gouges");
    }

    [AvaloniaFact(DisplayName = "CAM-H113 | CNC Design Check finds the hidden underside and the area beyond reach")]
    [Trait("TestId", "CAM-H113")]
    public void DesignCheck()
    {
        var session = new CadSession();
        var block = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var command = Begin(session, () => new CncDesignCheckCommand());
        Pick(command, "Model", block);
        SetNumber(command, "Usable cutter reach", 5);
        Run(command, "Run checks");
        BuildOutput(command);
        // The underside (400 mm²) is always included. Vertical walls are currently counted as well, because the upward
        // probe from a wall runs along the wall itself; that is not asserted. See the CAM guide's coverage gaps.
        double hidden = double.Parse(Result(command, "Top-down inaccessible area").Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(hidden >= 400 - 1e-6);
        Assert.StartsWith("400 mm²", Result(command, "Beyond selected reach"));
        Assert.Equal("Closed, manifold, outward oriented", Result(command, "Mesh health"));
        Assert.Equal("Use an inside radius of at least 3 mm for this cutter; allow extra radius for smoother engagement.", Result(command, "Inside corner target"));
    }

    [AvaloniaTheory(DisplayName = "CAM-H114 | FDM Preparation checks fit and walls for the block")]
    [InlineData(256, "Yes, after placing on the bed", false)]
    [InlineData(15, "Rotate, split or resize the part", false)]
    [Trait("TestId", "CAM-H114")]
    public void FdmPreparation(double bed, string fits, bool thin)
    {
        var session = new CadSession();
        var block = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var command = Begin(session, () => new FdmCheckCommand());
        Pick(command, "Model", block);
        SetNumber(command, "Build width X", bed);
        Run(command, "Run checks");
        BuildOutput(command);
        Assert.Equal(fits, Result(command, "Fits build volume"));
        Assert.Equal("0.8 mm (2 × 0.4 mm nozzle)", Result(command, "Wall target"));
        Assert.Equal(thin, Result(command, "Thin wall samples").StartsWith("0 ") == false);
        Assert.EndsWith("excludes supports, brim and purge", Result(command, "Estimated filament"));
    }

    [AvaloniaFact(DisplayName = "CAM-H115 | Changing a setting clears FDM results until checks run again")]
    [Trait("TestId", "CAM-H115")]
    public void FdmResultsInvalidate()
    {
        var session = new CadSession();
        var block = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var command = Begin(session, () => new FdmCheckCommand());
        Pick(command, "Model", block);
        Run(command, "Run checks");
        BuildOutput(command);
        Assert.Contains(command.Results, r => r.Label == "Wall target");
        SetNumber(command, "Nozzle diameter", 0.6);
        BuildOutput(command);
        Assert.DoesNotContain(command.Results, r => r.Label == "Wall target");
    }

    [AvaloniaFact(DisplayName = "CAM-H116 | Resin Preparation reports a solid block with no sealed cavities and its resin estimate")]
    [Trait("TestId", "CAM-H116")]
    public void ResinPreparation()
    {
        var session = new CadSession();
        var block = CreateBoxBody(session, Vec3.Zero, 20, 20, 10);
        var command = Begin(session, () => new ResinCheckCommand());
        Pick(command, "Model", block);
        Run(command, "Run checks");
        BuildOutput(command);
        Assert.StartsWith("0;", Result(command, "Sealed cavities"));
        Assert.Equal("4 ml · 4.4 g; excludes supports", Result(command, "Solid resin estimate"));
        Assert.Equal("Yes, before supports and raft", Result(command, "Fits build volume"));
    }

    [AvaloniaFact(DisplayName = "CAM-H117 | Resin Preparation finds a sealed cavity inside a hollow block")]
    [Trait("TestId", "CAM-H117")]
    public void ResinFindsCavity()
    {
        var session = new CadSession();
        using var outer = BoxAt(Vec3.Zero, 20, 20, 20);
        using var inner = BoxAt(new Vec3(5, 5, 5), 10, 10, 10);
        var hollow = session.AddObject(Occ.Cut(outer, inner), "Hollow", ObjKind.Solid);
        var command = Begin(session, () => new ResinCheckCommand());
        Pick(command, "Model", hollow);
        Run(command, "Run checks");
        BuildOutput(command);
        Assert.StartsWith("1;", Result(command, "Sealed cavities"));
        Assert.True(Assert.Single(command.Results, r => r.Label == "Sealed cavities").IsBad);
    }
}
