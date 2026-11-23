using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Interface;

/// <summary>
/// Repeat last tool (Ctrl+Shift+R) and the Apply button that keeps a tool open. Owner request and decisions 2026-11-23: the key is Ctrl+Shift+R, and
/// Trim Surface and Boundary Surface get Apply first.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/README.md#repeat-the-last-tool-and-keep-going-inside-a-tool-owner-request-2026-11-23-built">Interface guide: repeat and Apply.</seealso>
[Trait("Feature", "Interface")]
[Trait("Layer", "Headless")]
public class RepeatLastToolTests
{
    static SceneObject Sheet(CadSession session, double x0, double width, double z = 0) =>
        session.AddObject(Occ.NurbsSurface([new(x0, 0, z), new(x0 + width, 0, z), new(x0, 10, z), new(x0 + width, 10, z)], 2, 2, 1, 1), "Sheet", ObjKind.Surface);

    static SelItem EdgeAtX(SceneObject source, double x) =>
        Assert.Single(EdgesWhere(source, e => Math.Abs(e.Min.X - x) < 1e-6 && Math.Abs(e.Max.X - x) < 1e-6));

    /// <summary>Two sheets 10 mm apart in X and Z, with the facing edges picked for a bridge in Blend mode at fullness 2.</summary>
    static (CadSession session, BoundarySurfaceCommand bridge, SelItem a, SelItem b) Bridge()
    {
        var session = new CadSession();
        var low = Sheet(session, 0, 10);
        var high = Sheet(session, 20, 10, z: 10);
        var bridge = Begin(session, () => new BoundarySurfaceCommand());
        SetChoice(bridge, "Mode", "Blend: bridge two surface edges");
        bridge.Params.OfType<NumberParam>().Single(p => p.Label == "Fullness").Value = 2;
        KeepSeparate(bridge);
        var a = EdgeAtX(low, 10); var b = EdgeAtX(high, 20);
        PickAsClicked(bridge, a, b);
        return (session, bridge, a, b);
    }

    /// <summary>The scenario Pick fills the slot without the change a click raises; raise it, so the per-edge continuity rows exist as they do in the app.</summary>
    static void PickAsClicked(Command command, params SelItem[] items) { Pick(command, "Boundary", items); command.OnChanged(); }

    static void KeepSeparate(Command command) => command.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Join the patch")).Value = false;

    static void AssertSameOptionsNoPicks(Command command)
    {
        var boundary = Assert.IsType<BoundarySurfaceCommand>(command);
        var mode = boundary.Params.OfType<ChoiceParam>().Single(p => p.Label == "Mode");
        Assert.Equal("Blend: bridge two surface edges", mode.Options[mode.Index]);
        Assert.Equal(2, boundary.Params.OfType<NumberParam>().Single(p => p.Label == "Fullness").Value);
        Assert.False(boundary.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Join the patch")).Value);
        Assert.All(boundary.Slots, slot => Assert.Empty(slot.Items));
    }

    [AvaloniaFact(DisplayName = "REP-H001 | Repeat last tool reopens the finished Boundary Surface with the same options and no picks")]
    [Trait("TestId", "REP-H001")]
    public void RepeatReopensWithSameOptions()
    {
        var (session, _, _, _) = Bridge();
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        Assert.Equal("Boundary Surface", session.LastTool?.Title);
        session.ClearSelection();
        Assert.True(session.RepeatLastTool());
        AssertSameOptionsNoPicks(session.ActiveCommand!);
        Assert.Contains("Repeating Boundary Surface", session.Status);
    }

    [AvaloniaFact(DisplayName = "REP-H002 | A cancelled tool is not the last tool, nothing to repeat says so, and the key never starts a tool on top of an open one")]
    [Trait("TestId", "REP-H002")]
    public void CancelledToolsAndOpenToolsAreLeftAlone()
    {
        var empty = new CadSession();
        Assert.True(empty.RepeatLastTool());
        Assert.Null(empty.ActiveCommand);
        Assert.Contains("no tool to repeat", empty.Status);

        var (session, _, _, _) = Bridge();
        session.CommitCommand();
        Begin(session, () => new TrimSurfaceCommand());
        session.CancelCommand();
        Assert.Equal("Boundary Surface", session.LastTool?.Title);

        var open = Begin(session, () => new TrimSurfaceCommand());
        Assert.False(session.RepeatLastTool());
        Assert.Same(open, session.ActiveCommand);
    }

    [AvaloniaFact(DisplayName = "REP-H003 | Three Applies and a Create in Boundary Surface leave four steps that undo one at a time")]
    [Trait("TestId", "REP-H003")]
    public void ApplyKeepsBoundarySurfaceOpen()
    {
        var (session, bridge, a, b) = Bridge();
        int before = session.Objects.Count;
        Assert.Equal("Apply", bridge.RepeatApply?.Label);
        for (int i = 1; i <= 3; i++)
        {
            var current = session.ActiveCommand!;
            current.RepeatApply!.Run();
            Assert.Equal(before + i, session.Objects.Count);
            var next = session.ActiveCommand!;
            Assert.NotSame(current, next);
            AssertSameOptionsNoPicks(next);
            PickAsClicked(next, a, b);
        }
        session.AcceptCommandPanel();
        Assert.Null(session.ActiveCommand);
        Assert.Equal(before + 4, session.Objects.Count);
        for (int left = 3; left >= 0; left--)
        {
            session.Undo();
            Assert.Equal(before + left, session.Objects.Count);
        }
    }

    [AvaloniaFact(DisplayName = "REP-H004 | Apply in Boundary Surface that cannot be built keeps the tool and its picks")]
    [Trait("TestId", "REP-H004")]
    public void FailedApplyKeepsPicks()
    {
        var session = new CadSession();
        var line = session.AddObject(Occ.Line(Vec3.Zero, new(10, 0, 0)), "Line", ObjKind.Curve);
        var command = Begin(session, () => new BoundarySurfaceCommand());
        SetChoice(command, "Mode", "Edge: span two to four curves");
        Pick(command, "Boundary", line);
        command.RepeatApply!.Run();
        Assert.Same(command, session.ActiveCommand);
        Assert.Single(command.Slots[0].Items);
        Assert.Null(session.LastTool);
    }

    [AvaloniaFact(DisplayName = "REP-H005 | Trim Surface Apply trims and reopens with the side to keep unchanged")]
    [Trait("TestId", "REP-H005")]
    public void ApplyKeepsTrimSurfaceOpen()
    {
        var session = new CadSession();
        var sheet = Sheet(session, 0, 10);
        var cutter = session.AddObject(Occ.NurbsSurface([new(5, -5, -5), new(5, 15, -5), new(5, -5, 5), new(5, 15, 5)], 2, 2, 1, 1), "Cutter", ObjKind.Surface);
        var trim = Begin(session, () => new TrimSurfaceCommand());
        Pick(trim, "Surface to trim", sheet);
        Pick(trim, "Cutting tool (plane, surface, face or solid)", cutter);
        SetChoice(trim, "Keep", "The back side (the other side, or inside a solid)");
        trim.RepeatApply!.Run();
        var next = Assert.IsType<TrimSurfaceCommand>(session.ActiveCommand);
        Assert.NotSame(trim, next);
        Assert.Equal(1, next.Params.OfType<ChoiceParam>().Single(p => p.Label == "Keep").Index);
        Assert.All(next.Slots, slot => Assert.Empty(slot.Items));
        var trimmed = Assert.Single(session.Objects, o => o.Kind == ObjKind.Surface && o.Name != "Cutter");
        AssertClose(50, trimmed.Shape.Props().Area, 1e-3, "Trimmed half (mm²)");
    }

    [AvaloniaFact(DisplayName = "REP-H006 | Ctrl+Shift+R is the Repeat last tool key, the right-click menu offers it first, and each document tab keeps its own")]
    [Trait("TestId", "REP-H006")]
    public void KeyMenuAndTabs()
    {
        var action = Assert.Single(Hotkeys.Actions, h => h.Id == "edit.repeat");
        Assert.Equal("Ctrl+Shift+R", action.Default);
        Assert.Equal(Hotkeys.Global, action.Scope);

        var vm = new MainViewModel();
        var first = vm.Session;
        var line = first.AddObject(Occ.Line(Vec3.Zero, new(10, 0, 0)), "Line", ObjKind.Curve);
        var next = first.AddObject(Occ.Line(new(10, 0, 0), new(10, 10, 0)), "Line", ObjKind.Curve);
        var join = Begin(first, () => new JoinCurvesCommand());
        Pick(join, "Curves to join", line, next);
        first.CommitCommand();
        var menu = vm.BuildTraditionalMenu();
        Assert.Equal("Repeat Join Curves", menu[0].Label);
        Assert.Equal("Ctrl+Shift+R", menu[0].Gesture);

        vm.NewDocumentTab();
        Assert.NotSame(first, vm.Session);
        Assert.Null(vm.Session.LastTool);
        Assert.DoesNotContain(vm.BuildTraditionalMenu(), m => m.Label.StartsWith("Repeat"));
        Assert.Equal("Join Curves", first.LastTool?.Title);
    }

    [AvaloniaFact(DisplayName = "REP-H007 | Apply is offered by Patch, Ruled Surface, Fill Opening, Fillet, Chamfer, Fill Hole, Stitch Seam, Split Body and Split Face, not by other tools or while editing a step")]
    [Trait("TestId", "REP-H007")]
    public void ApplyIsOfferedWhereAsked()
    {
        var session = new CadSession();
        Func<Command>[] offered =
        [
            () => new BoundarySurfaceCommand(), () => new TrimSurfaceCommand(), () => new PatchCommand(), () => new RuledSurfaceCommand(),
            () => new FillOpeningCommand(), () => new FilletCommand(), () => new ChamferCommand(), () => new MeshBoundaryCommand(), () => new MeshBoundaryCommand(stitch: true), () => new SplitBodyCommand(), () => new SplitFaceCommand(),
        ];
        foreach (var make in offered)
        {
            session.StartCommand(make);
            Assert.True(session.ActiveCommand!.RepeatApply?.Label == "Apply", session.ActiveCommand.Title);
            session.CancelCommand();
        }
        session.StartCommand(() => new JoinCurvesCommand());
        Assert.Null(session.ActiveCommand!.RepeatApply);
        session.CancelCommand();
        Assert.Null(new FilletCommand { EditMode = true }.RepeatApply);
    }

    [AvaloniaFact(DisplayName = "REP-H008 | Fillet Apply rounds one box edge and reopens with the same radius for the next")]
    [Trait("TestId", "REP-H008")]
    public void FilletApplyKeepsRadius()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        double volume = box.Shape.Props().Volume;
        var fillet = Begin(session, () => new FilletCommand());
        Pick(fillet, "Edges", new SelItem(box.Id, SelKind.Edge, 0));
        fillet.Params.OfType<NumberParam>().Single(p => p.Label == "Radius").Value = 3;
        fillet.RepeatApply!.Run();
        var next = Assert.IsType<FilletCommand>(session.ActiveCommand);
        Assert.NotSame(fillet, next);
        Assert.Equal(3, next.Params.OfType<NumberParam>().Single(p => p.Label == "Radius").Value);
        Assert.Empty(next.Slots[0].Items);
        Assert.True(session.Find(box.Id)!.Shape.Props().Volume < volume - 1);
    }

    [AvaloniaFact(DisplayName = "REP-H009 | Command search offers Repeat <last tool> first and running it reopens the tool")]
    [Trait("TestId", "REP-H009")]
    public void SearchOffersRepeat()
    {
        var vm = new MainViewModel();
        var s = vm.Session;
        var join = Begin(s, () => new JoinCurvesCommand());
        Pick(join, "Curves to join", s.AddObject(Occ.Line(Vec3.Zero, new(10, 0, 0)), "Line", ObjKind.Curve), s.AddObject(Occ.Line(new(10, 0, 0), new(10, 10, 0)), "Line", ObjKind.Curve));
        s.CommitCommand();
        vm.SearchText = "repeat";
        var hit = vm.SearchResults.First();
        Assert.Equal("Repeat Join Curves", hit.Label);
        Assert.Equal("Ctrl+Shift+R", hit.Shortcut);
        vm.SearchText = "join";
        Assert.Equal("Repeat Join Curves", vm.SearchResults.First().Label);
        Assert.Contains(vm.SearchResults, h => h.Label == "Join");
        vm.RunSearchHit(vm.SearchResults.First());
        Assert.IsType<JoinCurvesCommand>(s.ActiveCommand);
        vm.SearchText = "repeat";
        Assert.DoesNotContain(vm.SearchResults, h => h.Label.StartsWith("Repeat"));   // not while a tool is open
    }
}
