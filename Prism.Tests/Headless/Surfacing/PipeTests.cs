using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/pipe/README.md">Pipe variants and verification.</seealso>
[Trait("Feature", "Pipe")]
[Trait("Layer", "Headless")]
public class PipeTests
{
    [AvaloniaTheory(DisplayName = "PIP-H001 | Straight exact Pipe matches cylinder and frustum geometry")]
    [InlineData("Surface", true, 4)]
    [InlineData("Surface", true, 2)]
    [InlineData("Surface", false, 4)]
    [InlineData("Surface", false, 2)]
    [InlineData("NURBS", true, 4)]
    [InlineData("NURBS", true, 2)]
    [InlineData("NURBS", false, 4)]
    [InlineData("NURBS", false, 2)]
    [Trait("TestId", "PIP-H001")]
    public void ExactPipeMatchesAnalyticGeometry(string tab, bool capped, int endRadius)
    {
        const int startRadius = 4;
        const int length = 12;
        var vm = new MainViewModel();
        var path = CreateCurve(vm.Session, [Vec3.Zero, new(0, 0, length)]);
        var command = Start<PipeCommand>(vm, tab);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Path curve", path);
            SetNumber(command, "Radius at the start", startRadius);
            SetNumber(command, "Radius at the end", endRadius);
            SetChoice(command, "Result", capped ? "Solid (capped)" : "Surface");
        });
        using var shape = BuildSingleAddedShape(command);
        Assert.True(shape.IsValid);
        Assert.Equal(capped ? 1 : 0, shape.Count(SubKind.Solid));
        if (capped)
        {
            double volume = Math.PI * length / 3 * (startRadius * startRadius + startRadius * endRadius + endRadius * endRadius);
            AssertNear(volume, shape.Props().Volume, "Pipe volume (mm³)");
        }
        else
        {
            double slant = Math.Sqrt(length * length + Math.Pow(startRadius - endRadius, 2));
            AssertNear(Math.PI * (startRadius + endRadius) * slant, shape.Props().Area, "Open pipe area (mm²)");
        }
        AssertNear(length, shape.Props().Size.Z, "Pipe length (mm)");
    }

    [AvaloniaTheory(DisplayName = "PIP-H002 | Freeform Pipe honors radii, sampling, caps and smoothness")]
    [InlineData(2, 6, 4, 2, false, 1)]
    [InlineData(5, 5, 8, 3, true, 2)]
    [InlineData(6, 2, 12, 4, true, 4)]
    [Trait("TestId", "PIP-H002")]
    public void CagePipeUsesAllControls(int startRadius, int endRadius, int sides, int steps, bool caps, int level)
    {
        var vm = new MainViewModel();
        var path = CreateCurve(vm.Session, [Vec3.Zero, new(0, 0, 12)]);
        var command = Start<FormPipeCommand>(vm, "Freeform");
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Path (curve or edge)", path);
            SetNumber(command, "Radius at the start", startRadius);
            SetNumber(command, "Radius at the end", endRadius);
            SetInt(command, "Sides", sides);
            SetInt(command, "Steps along the path", steps);
            SetInt(command, "Smoothness (levels)", level);
            SetToggle(command, "Close the ends", caps);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var cage = Assert.Single(output.Add).Form!;
        Assert.Equal(sides * (steps + 1), cage.V.Count);
        Assert.Equal(sides * steps + (caps ? 2 : 0), cage.F.Count);
        Assert.Equal(level, cage.Level);
        Assert.Equal(caps, cage.IsClosed());
        Assert.All(cage.V.Take(sides), point => AssertNear(startRadius, point.Length, "Start radius (mm)"));
        Assert.All(cage.V.TakeLast(sides), point =>
            AssertNear(endRadius, point.DistanceTo(new Vec3(0, 0, 12)), "End radius (mm)"));
    }

    [AvaloniaTheory(DisplayName = "PIP-H003 | Pipe without a path leaves the document unchanged")]
    [InlineData("Surface")]
    [InlineData("NURBS")]
    [InlineData("Freeform")]
    [Trait("TestId", "PIP-H003")]
    public void MissingPathDoesNotChangeDocument(string tab)
    {
        var vm = new MainViewModel();
        var history = vm.Session.Current;
        Command command = tab == "Freeform" ? Start<FormPipeCommand>(vm, tab) : Start<PipeCommand>(vm, tab);
        Assert.Null(command.TryBuild());
        Assert.Same(history, vm.Session.Current);
        Assert.Empty(vm.Session.Objects);
    }

    static void SetInt(Command command, string label, int value) =>
        command.Params.OfType<IntParam>().Single(p => p.Label == label).Value = value;

    static T Start<T>(MainViewModel vm, string tab) where T : Command
    {
        var button = vm.Tabs.Single(t => t.Title == tab).Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct().Single(b => b.Label == "Pipe");
        button.Invoke();
        return Assert.IsType<T>(vm.Session.ActiveCommand);
    }
}
