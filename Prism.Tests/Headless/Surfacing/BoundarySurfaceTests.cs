using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/boundary-surface/README.md">Boundary Surface, Patch and Ruled Surface.</seealso>
[Trait("Feature", "BoundarySurface")]
[Trait("Layer", "Headless")]
public class BoundarySurfaceTests
{
    [AvaloniaTheory(DisplayName = "BND-H001 | Two skew-height parallel rails span an analytic ruled sheet")]
    [InlineData("Surface")]
    [InlineData("NURBS")]
    [InlineData("Ruled Surface")]
    [Trait("TestId", "BND-H001")]
    public void ParallelRailsProduceExpectedArea(string entry)
    {
        var vm = new MainViewModel();
        var first = CreateCurve(vm.Session, [Vec3.Zero, new(30, 0, 0)]);
        var second = CreateCurve(vm.Session, [new(0, 20, 10), new(30, 20, 10)]);
        Command command;
        if (entry == "Ruled Surface")
        {
            var ruled = StartCommand(vm.Session, first, () => new RuledSurfaceCommand());
            ruled.ConfigureInputs(() =>
            {
                SetSelection(ruled, "First curve", first);
                SetSelection(ruled, "Second curve", second);
            });
            command = ruled;
        }
        else
        {
            var boundary = StartBoundary(vm, entry);
            boundary.ConfigureInputs(() => SetSelection(boundary, "Boundary", first, second));
            command = boundary;
        }
        using var sheet = BuildSingleAddedShape(command);
        AssertSheet(sheet);
        AssertNear(30 * Math.Sqrt(500), sheet.Props().Area, "Ruled area (mm²)");
        AssertBounds(sheet, Vec3.Zero, new Vec3(30, 20, 10));
    }

    [AvaloniaTheory(DisplayName = "BND-H002 | Automatic and Flat keep an inner loop as a hole")]
    [InlineData("Surface", "Automatic (flat if the loop is flat)")]
    [InlineData("Surface", "Flat surface")]
    [InlineData("NURBS", "Automatic (flat if the loop is flat)")]
    [InlineData("NURBS", "Flat surface")]
    [InlineData("Patch", "Automatic (flat if the loop is flat)")]
    [InlineData("Patch", "Flat surface")]
    [Trait("TestId", "BND-H002")]
    public void NestedLoopsCreatePlanarHole(string entry, string fill)
    {
        var vm = new MainViewModel();
        var outer = Rectangle(vm.Session);
        var inner = vm.Session.AddObject(Occ.Circle(new(20, 20, 0), Vec3.UnitZ, 5), "Inner loop", ObjKind.Curve);
        var command = StartFill(vm, entry, outer);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, command.Slots[0].Label, outer, inner);
            SetChoice(command, "Fill with", fill);
        });
        using var sheet = BuildSingleAddedShape(command);
        AssertSheet(sheet);
        Assert.Equal(2, sheet.Count(SubKind.Wire));
        AssertNear(1600 - Math.PI * 25, sheet.Props().Area, "Perforated area (mm²)");
    }

    [AvaloniaTheory(DisplayName = "BND-H003 | Bent boundaries distinguish Automatic, Smooth and forced Flat")]
    [InlineData("Surface", "Automatic (flat if the loop is flat)", true)]
    [InlineData("Surface", "Smooth surface", true)]
    [InlineData("Surface", "Flat surface", false)]
    [InlineData("NURBS", "Automatic (flat if the loop is flat)", true)]
    [InlineData("NURBS", "Smooth surface", true)]
    [InlineData("NURBS", "Flat surface", false)]
    [InlineData("Patch", "Automatic (flat if the loop is flat)", false)]
    [InlineData("Patch", "Smooth patch", false)]
    [InlineData("Patch", "Flat surface", false)]
    [Trait("TestId", "BND-H003")]
    public void BentLoopRespectsFillChoice(string entry, string fill, bool succeeds)
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var loop = session.AddObject(Occ.Spline(
            [Vec3.Zero, new(20, 0, 8), new(40, 0, 0), new(40, 30, 0), new(20, 30, 8), new(0, 30, 0)], true),
            "Bent loop", ObjKind.Curve);
        var history = session.Current;
        var command = StartFill(vm, entry, loop);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, command.Slots[0].Label, loop);
            SetChoice(command, "Fill with", fill);
        });
        if (succeeds)
        {
            using var sheet = BuildSingleAddedShape(command);
            AssertSheet(sheet);
            Assert.True(sheet.Props().Size.Z > 4);
        }
        else
        {
            Assert.Null(command.TryBuild());
            Assert.False(string.IsNullOrWhiteSpace(command.Error));
            if (entry == "Patch" && fill != "Flat surface")
                Assert.Contains("bulges far outside", command.Error); // Issue #51
            Assert.Equal(loop.Id, Assert.Single(session.Objects).Id);
            Assert.Same(history, session.Current);
        }
    }

    [AvaloniaTheory(DisplayName = "BND-H004 | Both inside-curve modes lift the patch above its planar boundary")]
    [InlineData("Surface", false)]
    [InlineData("Surface", true)]
    [InlineData("NURBS", false)]
    [InlineData("NURBS", true)]
    [Trait("TestId", "BND-H004")]
    public void GuideCurveModesBuildLiftedPatch(string tab, bool fit)
    {
        var vm = new MainViewModel();
        var boundary = Rectangle(vm.Session);
        var guide = vm.Session.AddObject(Occ.Spline([new(0, 20, 0), new(20, 20, 6), new(40, 20, 0)], false),
            "Raised guide", ObjKind.Curve);
        var command = StartBoundary(vm, tab);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Boundary", boundary);
            SetSelection(command, "Guide curves", guide);
            SetChoice(command, "Inside curves", fit ? "Fit through points on them" : "Follow them exactly");
        });
        command.OnChanged();
        Assert.Equal(fit, command.Params.Single(p => p.Label == "Surface degree").IsVisible);
        using var sheet = BuildSingleAddedShape(command);
        AssertSheet(sheet);
        Assert.InRange(sheet.Props().Max.Z, 4, 12);
        if (fit)
            Assert.Contains(command.Results, row => row.Label == "Overshoot past the curves");
    }

    [AvaloniaTheory(DisplayName = "BND-H005 | Opening fill can add a separate patch or close the source into a solid")]
    [InlineData("Surface", false)]
    [InlineData("Surface", true)]
    [InlineData("NURBS", false)]
    [InlineData("NURBS", true)]
    [Trait("TestId", "BND-H005")]
    public void OpeningFillHonorsJoinChoice(string tab, bool join)
    {
        var vm = new MainViewModel();
        using var box = Occ.Box(10, 8, 10);
        var faces = new List<Shape>();
        try
        {
            for (int index = 0; index < box.Count(SubKind.Face); index++)
            {
                var face = box.Sub(SubKind.Face, index);
                if (Occ.FaceInfoOf(face).Normal.Z > 0.99)
                    face.Dispose();
                else
                    faces.Add(face);
            }
            var open = vm.Session.AddObject(Occ.Sew(faces, 0.01, false), "Open box", ObjKind.Surface);
            Assert.Equal(4, Occ.FreeEdges(open.Shape).Length);
            var command = StartBoundary(vm, tab);
            command.ConfigureInputs(() =>
            {
                SetSelection(command, "Boundary", open);
                command.Params.OfType<TextParam>().Single(p => p.Label == "Edge continuity").Value = "0:0";
                SetToggle(command, "Join the patch to the boundary surfaces (closes into a solid when it can)", join);
                SetNumber(command, "Joint tolerance", 0.01);
            });
            var output = command.TryBuild();
            Assert.True(output != null, command.Error);
            Assert.Empty(output.Remove);
            if (join)
            {
                Assert.Empty(output.Add);
                var replacement = Assert.Single(output.Replace);
                Assert.Equal(open.Id, replacement.Old.Id);
                using var solid = replacement.New;
                AssertValidSolid(solid);
                AssertNear(800, solid.Props().Volume, "Closed volume (mm³)");
            }
            else
            {
                Assert.Empty(output.Replace);
                using var patch = Assert.Single(output.Add).Shape!;
                AssertSheet(patch);
                AssertNear(80, patch.Props().Area, "Opening area (mm²)");
            }
        }
        finally
        {
            foreach (var face in faces)
                face.Dispose();
        }
    }

    static SceneObject Rectangle(CadSession session) =>
        CreateCurve(session, [Vec3.Zero, new(40, 0, 0), new(40, 40, 0), new(0, 40, 0)], closed: true);

    static void AssertSheet(Shape sheet)
    {
        Assert.True(sheet.IsValid);
        Assert.Equal(1, sheet.Count(SubKind.Face));
        Assert.Equal(0, sheet.Count(SubKind.Solid));
    }

    static Command StartFill(MainViewModel vm, string entry, SceneObject profile) =>
        entry == "Patch" ? StartCommand(vm.Session, profile, () => new PatchCommand()) : StartBoundary(vm, entry);

    static BoundarySurfaceCommand StartBoundary(MainViewModel vm, string tab)
    {
        var button = vm.Tabs.Single(t => t.Title == tab).Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct()
            .Single(b => b.Label.Replace('\n', ' ') == "Boundary Surface");
        button.Invoke();
        return Assert.IsType<BoundarySurfaceCommand>(vm.Session.ActiveCommand);
    }
}
