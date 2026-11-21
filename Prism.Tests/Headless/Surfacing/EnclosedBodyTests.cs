using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <summary>
/// Enclosed Body in two steps: pick the surfaces, then pick the spaces to keep in the viewport, each with its own colour and number. Owner request 2026-11-16.
/// A 10 mm cube cut by a sheet at Z = 3 gives two spaces of 300 and 700 mm³; which one is "Space 1" is not assumed.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/surface-and-nurbs/README.md">Surface and NURBS tabs.</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class EnclosedBodyTests
{
    const string Surfaces = "Surfaces, planes and bodies that enclose the space", Keep = "Spaces to keep (blank = all)", Working = "Working on";
    const string StepTwo = "2  Pick the spaces to keep";

    static (CadSession session, BoundaryFillCommand command) CubeCutAtThree()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        using var loop = Occ.Polyline([new(-5, -5, 3), new(15, -5, 3), new(15, 15, 3), new(-5, 15, 3)], true);
        var sheet = session.AddObject(Occ.Profile([loop], true), "Divider", ObjKind.Surface);
        var command = Begin(session, () => new BoundaryFillCommand());
        Pick(command, Surfaces, box, sheet);
        return (session, command);
    }

    static PointerInfo Press(CadSession session, Vec3 world, bool alt = false)
    {
        var p = session.Camera.Project(world);
        Assert.NotNull(p);
        return new PointerInfo(p.Value.x, p.Value.y, true, false, false, alt, 1);
    }

    static double[] Volumes(CommandOutput output) => output.Add.Select(a => a.Shape!.Props().Volume).Order().ToArray();

    [AvaloniaFact(DisplayName = "SRF-H030 | Enclosed Body has two steps, starts on the surfaces, and gives every space its own colour")]
    [Trait("TestId", "SRF-H030")]
    public void StepsAndColours()
    {
        var (_, command) = CubeCutAtThree();
        var step = command.Params.OfType<ChoiceParam>().Single(p => p.Label == Working);
        Assert.True(step.IsIllustrated);
        Assert.Equal(2, step.Options.Count);
        Assert.Equal(0, step.Index);
        var output = command.TryBuild();
        Assert.NotNull(output);
        Assert.Equal(2, output.Add.Count);
        Assert.Equal(2, output.Add.Select(a => a.Color).Distinct().Count());
        Assert.All(output.Add, a => Assert.NotNull(a.Color));
    }

    [AvaloniaFact(DisplayName = "SRF-H031 | In step 1 a click in the viewport does not pick a space; in step 2 it keeps only that space")]
    [Trait("TestId", "SRF-H031")]
    public void ClickKeepsASpace()
    {
        var (session, command) = CubeCutAtThree();
        Assert.NotNull(command.TryBuild());
        var onTop = Press(session, new Vec3(5, 5, 10));

        Assert.False(command.Tool!.PointerDown(session, onTop));                   // step 1: the click is left for picking surfaces
        Assert.Equal("", command.Params.OfType<TextParam>().Single(p => p.Label == Keep).Value);

        command.ConfigureInputs(() => SetChoice(command, Working, StepTwo));
        Assert.True(command.Tool.PointerDown(session, onTop));                      // step 2: the top of the cube belongs to the 700 mm³ space above the sheet
        var output = command.TryBuild();
        Assert.NotNull(output);
        AssertNear(700, Assert.Single(output.Add).Shape!.Props().Volume, "The one space kept (mm³)");
        Assert.Contains("kept", command.Results.Single(r => r.Value.StartsWith("700")).Value);
        Assert.Contains("left out", command.Results.Single(r => r.Value.StartsWith("300")).Value);
    }

    [AvaloniaFact(DisplayName = "SRF-H032 | A second click adds or removes a space, Alt-click reaches the space behind, and removing the last one keeps everything")]
    [Trait("TestId", "SRF-H032")]
    public void ToggleAndReachBehind()
    {
        var (session, command) = CubeCutAtThree();
        session.Camera.SetView(0, 90);                                              // looking straight down, so one ray passes through both spaces
        command.ConfigureInputs(() => SetChoice(command, Working, StepTwo));
        Assert.NotNull(command.TryBuild());
        var onTop = Press(session, new Vec3(5, 5, 10));
        var text = command.Params.OfType<TextParam>().Single(p => p.Label == Keep);

        Assert.True(command.Tool!.PointerDown(session, onTop));                      // keep the space above the sheet
        Assert.Single(text.Value.Split(','));
        Assert.True(command.Tool.PointerDown(session, Press(session, new Vec3(5, 5, 10), alt: true)));      // Alt-click in the same spot reaches the space behind it: add it
        Assert.Equal(2, text.Value.Split(',').Length);
        Assert.Equal([300.0, 700.0], Volumes(command.TryBuild()!).Select(v => Math.Round(v)).ToArray());

        Assert.True(command.Tool.PointerDown(session, onTop));                      // a plain click on the near space drops it again
        AssertNear(300, Assert.Single(command.TryBuild()!.Add).Shape!.Props().Volume, "Only the lower space remains (mm³)");
        Assert.True(command.Tool.PointerDown(session, Press(session, new Vec3(5, 5, 10), alt: true)));
        Assert.True(command.Tool.PointerDown(session, Press(session, new Vec3(5, 5, 10), alt: true)));      // keep stepping: the list never ends up empty-but-meaning-nothing
        Assert.NotNull(command.TryBuild());
    }

    [AvaloniaFact(DisplayName = "SRF-H033 | The viewport shows a number on every space, ghosts the ones left out, and names the one under the pointer")]
    [Trait("TestId", "SRF-H033")]
    public void NumbersGhostsAndHover()
    {
        var (session, command) = CubeCutAtThree();
        command.ConfigureInputs(() => SetChoice(command, Working, StepTwo));
        Assert.NotNull(command.TryBuild());

        var all = new Overlay(); command.BuildOverlay(session, all);
        Assert.Contains(all.Labels, l => l.Text == "1");
        Assert.Contains(all.Labels, l => l.Text == "2");
        Assert.Empty(all.Tris);                                                      // nothing left out yet, so no ghosts
        Assert.NotEmpty(all.Lines);                                                  // the lines where the surfaces meet, in each space's colour

        command.Tool!.PointerDown(session, Press(session, new Vec3(5, 5, 10)));      // keep only the upper space
        Assert.NotNull(command.TryBuild());
        var kept = new Overlay(); command.BuildOverlay(session, kept);
        Assert.NotEmpty(kept.Tris);                                                  // the other space is a faint ghost
        Assert.Contains(kept.Labels, l => l.Text.Contains('✕'));

        var p = Press(session, new Vec3(5, 5, 10));
        command.Tool.PointerMove(session, p);
        var hover = new Overlay(); command.BuildOverlay(session, hover);
        Assert.Contains(hover.Labels, l => l.Text.StartsWith("Space ") && l.Text.Contains("700") && l.Text.Contains("kept"));
    }

    const string DiscardLabel = "Delete the surfaces and bodies used afterward";

    [AvaloniaFact(DisplayName = "SRF-H035 | The surfaces and bodies that enclosed the spaces are deleted afterward by default, and kept when the option is off")]
    [Trait("TestId", "SRF-H035")]
    public void PickedToolsAreDeleted()
    {
        var (session, command) = CubeCutAtThree();
        var output = command.TryBuild();
        Assert.NotNull(output);
        Assert.Equal(2, output.Remove.Count);                                          // the cube and the dividing sheet
        Assert.Contains("Deleted", command.Results.Select(r => r.Label));

        command.ConfigureInputs(() => SetToggle(command, DiscardLabel, false));
        Assert.Empty(command.TryBuild()!.Remove);

        // committing really removes them: the two new solids are all that is left
        command.ConfigureInputs(() => SetToggle(command, DiscardLabel, true));
        session.CommitCommand();
        Assert.Equal(2, session.Objects.Count(o => o.Kind == ObjKind.Solid));
        Assert.DoesNotContain(session.Objects, o => o.Name.StartsWith("Divider"));
        session.Undo();                                                                  // one undo brings the cube and the sheet back
        Assert.Contains(session.Objects, o => o.Name.StartsWith("Divider"));
    }

    [AvaloniaFact(DisplayName = "SRF-H036 | A plane or a single face used to enclose is never deleted, and the option is hidden when nothing could be deleted")]
    [Trait("TestId", "SRF-H036")]
    public void PlanesAndFacesAreReferences()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = Begin(session, () => new BoundaryFillCommand());
        int top = Enumerable.Range(0, box.Shape.Count(SubKind.Face)).First(i => Occ.FaceInfoOf(box.Shape.Sub(SubKind.Face, i)).Normal.Z > 0.99);
        Pick(command, Surfaces, new SelItem(box.Id, SelKind.Face, top));                // a single face of the body, not the body
        var option = command.Params.OfType<BoolParam>().Single(p => p.Label == DiscardLabel);
        Assert.False(option.IsVisible);
        _ = command.TryBuild();                                                            // a lone face encloses nothing; the point is that nothing would be deleted
        Assert.Empty(command.TryBuild()?.Remove ?? []);
        Assert.Contains(session.Objects, o => o.Id == box.Id);
    }

    [AvaloniaFact(DisplayName = "SRF-H034 | Keep every space and Swap kept and left-out spaces work from the panel")]
    [Trait("TestId", "SRF-H034")]
    public void KeepAllAndSwap()
    {
        var (session, command) = CubeCutAtThree();
        Assert.NotNull(command.TryBuild());
        var text = command.Params.OfType<TextParam>().Single(p => p.Label == Keep);
        void Run(string label) => command.Params.OfType<ActionParam>().Single(a => a.Label == label).Run();

        text.Value = "1";
        var kept = Assert.Single(command.TryBuild()!.Add).Shape!.Props().Volume;
        Run("Swap kept and left-out spaces");
        Assert.Equal("2", text.Value);
        Assert.NotEqual(Math.Round(kept), Math.Round(Assert.Single(command.TryBuild()!.Add).Shape!.Props().Volume));       // the other space now

        Run("Keep every space");
        Assert.Equal("", text.Value);
        Assert.Equal(2, command.TryBuild()!.Add.Count);
    }
}
