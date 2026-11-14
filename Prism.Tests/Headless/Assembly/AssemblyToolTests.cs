using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Assembly;

/// <summary>
/// Characterizes the Assemble ribbon tools beyond joint creation: Make Part, Move Component, Component Pattern, Mate,
/// Drive Joints, Parts List, Exploded View and Create Part. Components are 2 × 3 × 4 mm blocks.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/assembly/README.md">Assembly guide.</seealso>
[Trait("Feature", "Assembly")]
[Trait("Layer", "Headless")]
public class AssemblyToolTests
{
    [AvaloniaFact(DisplayName = "ASM-H122 | The gallery orrery's Joints eye hides and shows every joint without changing the mechanism")]
    [Trait("TestId", "ASM-H122")]
    public void OrreryJointFolderToggleControlsMarkersAndDriveHandles()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var vm = new MainViewModel();
        var session = vm.Session;
        session.Open(Path.Combine(root.FullName, "gallery", "models", "meridian-orrery.prism"));
        var joints = session.JointObjects.Select(j => j.joint).ToArray();
        Assert.Equal(18, joints.Length);
        var drive = Begin(session, () => new DriveJointsCommand());
        Assert.NotEmpty(drive.Handles);
        vm.BrowserFilter = "revolute";
        BrowserNode Folder() => Assert.Single(vm.AllBrowserNodes(), row => row.IsFolder && row.Title == "Joints");
        Assert.True(Folder().IsVisible);
        int rebuilds = 0;
        vm.BrowserRebuilt += () => rebuilds++;

        Folder().IsVisible = false;
        Assert.Equal(1, rebuilds);
        Assert.All(session.Objects.Where(obj => obj.Kind == ObjKind.Joint), joint => Assert.False(joint.Visible));
        Assert.False(Folder().IsVisible);
        Assert.Empty(drive.Handles);
        Assert.Equal(joints, session.JointObjects.Select(j => j.joint));
        Folder().IsVisible = true;
        Assert.Equal(2, rebuilds);
        Assert.All(session.Objects.Where(obj => obj.Kind == ObjKind.Joint), joint => Assert.True(joint.Visible));
        Assert.NotEmpty(drive.Handles);
    }

    /// <summary>Two parts: the first is grounded, the second is free and starts 10 mm along X.</summary>
    static (CadSession Session, SceneObject Fixed, SceneObject Free) TwoParts()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 2, 3, 4);
        var second = CreateBoxBody(session, new(10, 0, 0), 2, 3, 4);
        session.MakePart(first.Id, checkpoint: true);
        session.MakePart(second.Id, checkpoint: true);
        return (session, session.Find(first.Id)!, session.Find(second.Id)!);
    }

    static void Commit(CadSession session) => session.CommitCommand();

    // ------------------------------------------------------------------ Make Part

    [AvaloniaFact(DisplayName = "ASM-H101 | Make Part turns bodies into components and grounds the first one")]
    [Trait("TestId", "ASM-H101")]
    public void MakePartFromBodies()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 2, 3, 4);
        var second = CreateBoxBody(session, new(10, 0, 0), 2, 3, 4);
        var command = Begin(session, () => new MakePartCommand());
        Pick(command, "Bodies", first, second);
        BuildOutput(command);
        Assert.Equal("2", Result(command, "Parts to make"));
        Commit(session);
        Assert.Equal(2, session.Components.Count());
        Assert.True(session.Find(first.Id)!.Component!.Grounded);
        Assert.False(session.Find(second.Id)!.Component!.Grounded);
        AssertBounds(session.Find(second.Id)!.Shape, new(10, 0, 0), new(12, 3, 4));
    }

    [AvaloniaFact(DisplayName = "ASM-H102 | Make Part ignores bodies that are already parts")]
    [Trait("TestId", "ASM-H102")]
    public void MakePartSkipsComponents()
    {
        var (session, fixedPart, _) = TwoParts();
        var command = Begin(session, () => new MakePartCommand());
        Pick(command, "Bodies", fixedPart);
        Assert.Null(command.TryBuild());
    }

    // ------------------------------------------------------------------ Move Component

    [AvaloniaFact(DisplayName = "ASM-H103 | Move Component moves a free component and turns it about its centre")]
    [Trait("TestId", "ASM-H103")]
    public void MoveFreeComponent()
    {
        var (session, _, free) = TwoParts();
        var command = Begin(session, () => new MoveComponentCommand());
        Pick(command, "Component", free);
        SetNumber(command, "Move Y", 5);
        SetNumber(command, "Turn about Z", 90);
        BuildOutput(command);
        Commit(session);
        // Turned about its centre (11, 1.5): the 2 × 3 footprint becomes 3 × 2, then moves 5 mm along Y.
        AssertBounds(session.Find(free.Id)!.Shape, new(9.5, 5.5, 0), new(12.5, 7.5, 4));
    }

    [AvaloniaFact(DisplayName = "ASM-H104 | Move Component refuses grounded components and plain bodies")]
    [Trait("TestId", "ASM-H104")]
    public void MoveComponentRefusals()
    {
        var (session, fixedPart, _) = TwoParts();
        var command = Begin(session, () => new MoveComponentCommand());
        Pick(command, "Component", fixedPart);
        SetNumber(command, "Move X", 1);
        AssertBuildFails(command, "grounded");
        session.CancelCommand();

        var body = CreateBoxBody(session, new(30, 0, 0), 1, 1, 1);
        command = Begin(session, () => new MoveComponentCommand());
        Pick(command, "Component", body);
        AssertBuildFails(command, "Pick a component");
    }

    [AvaloniaFact(DisplayName = "ASM-H105 | Move Component refuses a component held by a joint")]
    [Trait("TestId", "ASM-H105")]
    public void MoveComponentRefusesJointed()
    {
        var (session, fixedPart, free) = TwoParts();
        var joint = Begin(session, () => new JointCommand(JointType.Slider));
        Pick(joint, "On the first part (face, circle edge or round face)", FaceFacing(fixedPart, Vec3.UnitZ));
        Pick(joint, "On the part that moves", FaceFacing(free, -Vec3.UnitZ));
        Commit(session);
        var command = Begin(session, () => new MoveComponentCommand());
        Pick(command, "Component", session.Find(free.Id)!);
        SetNumber(command, "Move X", 1);
        AssertBuildFails(command, "held by a joint");
    }

    [AvaloniaFact(DisplayName = "ASM-H123 | The Move gizmo moves a free part's placement with its shape and refuses held parts")]
    [Trait("TestId", "ASM-H123")]
    public void MoveGizmoMovesPlacementAndRefusesHeldParts()
    {
        var (session, fixedPart, free) = TwoParts();
        Assert.Contains("grounded", session.WhyPartIsHeld(fixedPart)!);
        Assert.Null(session.WhyPartIsHeld(free));
        session.TransformTarget(SelItem.Of(free), Xform.Translation(new Vec3(0, 5, 0)), checkpoint: true);
        var moved = session.Find(free.Id)!;
        AssertBounds(moved.Shape, new(10, 5, 0), new(12, 8, 4));
        AssertPoint(new Vec3(0, 5, 0), moved.Component!.Placement.T, 1e-9, "Placement follows the shape");
        // the solver reads placements: re-solving must leave the part where the gizmo put it
        session.SolveAssembly();
        AssertBounds(session.Find(free.Id)!.Shape, new(10, 5, 0), new(12, 8, 4));
        // a grounded part does not move at all
        session.TransformTarget(SelItem.Of(fixedPart), Xform.Translation(new Vec3(0, 5, 0)), checkpoint: true);
        AssertBounds(session.Find(fixedPart.Id)!.Shape, new(0, 0, 0), new(2, 3, 4));
        Assert.True(session.StatusIsError);
    }

    [AvaloniaFact(DisplayName = "ASM-H124 | A revolute joint placed Keep where it is leaves both parts put and turns about the first pick's axis")]
    [Trait("TestId", "ASM-H124")]
    public void RevoluteKeepWhereItIs()
    {
        var (session, fixedPart, free) = TwoParts();
        var joint = Begin(session, () => new JointCommand(JointType.Revolute));
        SetChoice(joint, "Placement", "Keep where it is");
        Pick(joint, "On the first part (face, circle edge or round face)", FaceFacing(fixedPart, Vec3.UnitZ));   // axis: Z through (1, 1.5, 4)
        Pick(joint, "On the part that moves", FaceFacing(free, -Vec3.UnitX));
        BuildOutput(joint);
        Assert.Equal("Both parts stay where they are", Result(joint, "Placement"));
        Commit(session);
        AssertBounds(session.Find(free.Id)!.Shape, new(10, 0, 0), new(12, 3, 4));
        var drive = Begin(session, () => new DriveJointsCommand());
        SetNumber(drive, $"{session.JointObjects.Single().joint.Name}  (angle)", 90);
        BuildOutput(drive);
        Commit(session);
        // a quarter turn about the vertical axis through the fixed part's top-face centre (1, 1.5): x 10..12 becomes y 10.5..12.5
        AssertBounds(session.Find(free.Id)!.Shape, new(-0.5, 10.5, 0), new(2.5, 12.5, 4));
    }

    // ------------------------------------------------------------------ Component Pattern

    [AvaloniaFact(DisplayName = "ASM-H106 | Component Pattern makes linear copies as free components of the same part")]
    [Trait("TestId", "ASM-H106")]
    public void ComponentPatternLinear()
    {
        var (session, _, free) = TwoParts();
        var command = Begin(session, () => new ComponentPatternCommand());
        Pick(command, "Component to repeat", free);
        SetNumber(command, "Number of copies", 2);
        SetNumber(command, "Spacing X", 0);
        SetNumber(command, "Spacing Y", 10);
        Assert.False(command.Params.Single(p => p.Label == "Angle between copies").IsVisible);
        var output = BuildOutput(command);
        Assert.Equal(2, output.Add.Count);
        AssertBounds(output.Add[0].Shape!, new(10, 10, 0), new(12, 13, 4));
        AssertBounds(output.Add[1].Shape!, new(10, 20, 0), new(12, 23, 4));
        var info = Assert.IsType<ComponentInfo>(output.Add[0].Asm);
        Assert.Equal(free.Component!.PartName, info.PartName);
        Assert.False(info.Grounded);
        Assert.Equal("2", Result(command, "Copies"));
    }

    [AvaloniaFact(DisplayName = "ASM-H107 | Component Pattern circular copies turn about the chosen axis and point")]
    [Trait("TestId", "ASM-H107")]
    public void ComponentPatternCircular()
    {
        var (session, _, free) = TwoParts();
        var command = Begin(session, () => new ComponentPatternCommand());
        Pick(command, "Component to repeat", free);
        SetChoice(command, "Pattern", "Circular");
        Assert.True(command.Params.Single(p => p.Label == "Angle between copies").IsVisible);
        SetNumber(command, "Number of copies", 3);
        SetNumber(command, "Angle between copies", 90);
        var output = BuildOutput(command);
        Assert.Equal(3, output.Add.Count);
        AssertBounds(output.Add[1].Shape!, new(-12, -3, 0), new(-10, 0, 4));
    }

    // ------------------------------------------------------------------ Mate

    [AvaloniaTheory(DisplayName = "ASM-H108 | Mate puts the moving part's face against, flush with or a distance from the fixed face")]
    [InlineData("Face to face (touching)", 0, 4, 8)]
    [InlineData("Face to face (touching)", 2, 6, 10)]
    [InlineData("Flush (same direction)", 0, 0, 4)]
    [Trait("TestId", "ASM-H108")]
    public void MateFaces(string mate, double distance, double minZ, double maxZ)
    {
        var (session, fixedPart, free) = TwoParts();
        var command = Begin(session, () => new MateCommand());
        Pick(command, "Face on the part that stays", FaceFacing(fixedPart, Vec3.UnitZ));
        Pick(command, mate.StartsWith("Flush") ? "Face on the part that moves" : "Face on the part that moves",
            FaceFacing(free, mate.StartsWith("Flush") ? Vec3.UnitZ : -Vec3.UnitZ));
        SetChoice(command, "Mate", mate);
        SetNumber(command, "Distance between the faces", distance);
        BuildOutput(command);
        Assert.Equal(free.Name, Result(command, "Moves"));
        Commit(session);
        var moved = session.Find(free.Id)!.Shape.Props();
        AssertClose(minZ, moved.Min.Z, 1e-6, "Moved part bottom (mm)");
        AssertClose(maxZ, moved.Max.Z, 1e-6, "Moved part top (mm)");
    }

    [AvaloniaFact(DisplayName = "ASM-H109 | Mate refuses a grounded moving part and two faces on one part")]
    [Trait("TestId", "ASM-H109")]
    public void MateRefusals()
    {
        var (session, fixedPart, free) = TwoParts();
        var command = Begin(session, () => new MateCommand());
        Pick(command, "Face on the part that stays", FaceFacing(free, Vec3.UnitZ));
        Pick(command, "Face on the part that moves", FaceFacing(fixedPart, -Vec3.UnitZ));
        AssertBuildFails(command, "grounded");
        Pick(command, "Face on the part that stays", FaceFacing(free, Vec3.UnitZ));
        Pick(command, "Face on the part that moves", FaceFacing(free, -Vec3.UnitZ));
        AssertBuildFails(command, "two different components");
    }

    // ------------------------------------------------------------------ Drive Joints

    [AvaloniaFact(DisplayName = "ASM-H110 | Drive Joints slides the jointed part by the typed value and keeps it on Apply")]
    [Trait("TestId", "ASM-H110")]
    public void DriveSliderJoint()
    {
        var (session, fixedPart, free) = TwoParts();
        var joint = Begin(session, () => new JointCommand(JointType.Slider));
        Pick(joint, "On the first part (face, circle edge or round face)", FaceFacing(fixedPart, Vec3.UnitZ));
        Pick(joint, "On the part that moves", FaceFacing(free, -Vec3.UnitZ));
        Commit(session);
        var command = Begin(session, () => new DriveJointsCommand());
        var value = Assert.Single(command.Params.OfType<NumberParam>());
        value.Value = 5;
        BuildOutput(command);
        Assert.Equal("1", Result(command, "Moved parts"));
        Commit(session);
        AssertBounds(session.Find(free.Id)!.Shape, new(0, 0, 9), new(2, 3, 13));
        Assert.Equal(5, Assert.Single(session.JointObjects).joint.Value);
    }

    [AvaloniaFact(DisplayName = "ASM-H111 | Drive Joints explains when there are no joints")]
    [Trait("TestId", "ASM-H111")]
    public void DriveJointsWithoutJoints()
    {
        var (session, _, _) = TwoParts();
        var command = Begin(session, () => new DriveJointsCommand());
        Assert.Null(command.TryBuild());
        Assert.Equal("none yet. Create joints first (Joints tab).", Result(command, "Joints"));
    }

    // ------------------------------------------------------------------ Parts List / Explode / Create Part

    [AvaloniaFact(DisplayName = "ASM-H112 | Parts List counts copies of one part as a single line")]
    [Trait("TestId", "ASM-H112")]
    public void PartsListCountsCopies()
    {
        var (session, _, free) = TwoParts();
        var pattern = Begin(session, () => new ComponentPatternCommand());
        Pick(pattern, "Component to repeat", free);
        SetNumber(pattern, "Number of copies", 2);
        Commit(session);
        var command = Begin(session, () => new PartsListCommand());
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("2", Result(command, "Distinct parts"));
        Assert.Equal("4", Result(command, "Total components"));
        Assert.Contains(command.Results, r => r.Label.StartsWith("3 ×  "));
    }

    [AvaloniaFact(DisplayName = "ASM-H113 | Parts List asks for components in an empty assembly")]
    [Trait("TestId", "ASM-H113")]
    public void PartsListEmpty()
    {
        var session = new CadSession();
        var command = Begin(session, () => new PartsListCommand());
        BuildOutput(command);
        Assert.Equal("none yet. Insert components first (Assemble tab).", Result(command, "Parts"));
    }

    [AvaloniaFact(DisplayName = "ASM-H114 | Exploded View sets the explode factor and Cancel restores it")]
    [Trait("TestId", "ASM-H114")]
    public void ExplodedView()
    {
        var (session, _, _) = TwoParts();
        var command = Begin(session, () => new ExplodeCommand());
        SetNumber(command, "Explode", 0.8);
        BuildOutput(command);
        Assert.Equal(0.8, session.ExplodeFactor);
        Assert.Equal("2", Result(command, "Parts"));
        session.CancelCommand();
        Assert.Equal(0, session.ExplodeFactor);
    }

    [AvaloniaFact(DisplayName = "ASM-H115 | Create Part names the new part and its starting workspace")]
    [Trait("TestId", "ASM-H115")]
    public void CreatePartReport()
    {
        var (session, _, _) = TwoParts();
        var command = Begin(session, () => new CreatePartCommand());
        Assert.Equal("Part 3", command.Params.OfType<TextParam>().Single(p => p.Label == "Part name").Value);
        SetChoice(command, "What kind of part", "Sheet metal");
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("Part 3", Result(command, "Part"));
        Assert.Equal("Sheet metal", Result(command, "Starts in"));
    }
}
