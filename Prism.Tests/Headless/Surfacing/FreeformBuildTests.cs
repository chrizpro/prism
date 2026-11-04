using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/extrude/README.md#freeform-extrusion">Freeform Extrude Profile options.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/revolve/README.md#freeform-revolve">Freeform Revolve options.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Headless")]
public class FreeformBuildTests
{
    [AvaloniaTheory(DisplayName = "FRM-H001 | Extrude Profile honors direction, signed distance and cage settings")]
    [InlineData("Profile normal", 12, true, 1, 2)]
    [InlineData("X", -12, false, 2, 1)]
    [InlineData("Y", 12, true, 2, 4)]
    [InlineData("Z", -12, true, 1, 3)]
    [Trait("TestId", "FRM-H001")]
    public void ExtrudeProfileOptions(string direction, int distance, bool caps, int steps, int level)
    {
        var vm = new MainViewModel();
        var (plane, axis) = direction switch
        {
            "X" => (Plane3.YZ, Vec3.UnitX),
            "Y" => (Plane3.XZ, Vec3.UnitY),
            _ => (Plane3.XY, Vec3.UnitZ),
        };
        var profile = CreateRectangleSketch(vm.Session, plane, 10, 6);
        var command = Start<FormExtrudeCommand>(vm, "Extrude Profile");
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Profile (curve or sketch)", profile);
            SetChoice(command, "Direction", direction);
            SetNumber(command, "Distance", distance);
            SetNumber(command, "Scale at the far end", 0.5);
            SetToggle(command, "Close the ends (closed profile)", caps);
            SetInt(command, "Steps along the way", steps);
            SetInt(command, "Points on the profile", 8);
            SetInt(command, "Smoothness (levels)", level);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var cage = Assert.Single(output.Add).Form!;
        Assert.Equal(8 * (steps + 1), cage.V.Count);
        Assert.Equal(8 * steps + (caps ? 2 : 0), cage.F.Count);
        Assert.Equal(caps, cage.IsClosed());
        Assert.Equal(level, cage.Level);
        var first = cage.V.Take(8).ToList();
        var last = cage.V.TakeLast(8).ToList();
        AssertNear(distance, Vec3.Dot(last[0] - first[0], axis), "Signed extent (mm)");
        AssertNear(first[0].DistanceTo(first[1]) * 0.5, last[0].DistanceTo(last[1]), "Tapered edge (mm)");
    }

    [AvaloniaTheory(DisplayName = "FRM-H002 | Revolve closes the seam only for a full turn and honors cap choice")]
    [InlineData("X", 360, true, 32, 26)]
    [InlineData("Y", 360, false, 32, 24)]
    [InlineData("Z", 180, true, 36, 24)]
    [Trait("TestId", "FRM-H002")]
    public void RevolveAxisAngleAndCaps(string axis, int angle, bool caps, int vertices, int faces)
    {
        var vm = new MainViewModel();
        var (a, b) = axis switch
        {
            "X" => (new Vec3(0, 5, 0), new Vec3(10, 5, 0)),
            "Y" => (new Vec3(5, 0, 0), new Vec3(5, 10, 0)),
            _ => (new Vec3(5, 0, 0), new Vec3(5, 0, 10)),
        };
        var profile = CreateCurve(vm.Session, [a, b]);
        var command = Start<FormRevolveCommand>(vm, "Revolve");
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Profile (curve or sketch)", profile);
            SetChoice(command, "Axis through the origin", axis);
            SetNumber(command, "Angle (degrees)", angle);
            SetToggle(command, "Close the open ends (360° only)", caps);
            SetInt(command, "Segments around", 8);
            SetInt(command, "Points on the profile", 4);
            SetInt(command, "Smoothness (levels)", 2);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var cage = Assert.Single(output.Add).Form!;
        Assert.Equal(vertices, cage.V.Count);
        Assert.Equal(faces, cage.F.Count);
        Assert.Equal(angle == 360 && caps, cage.IsClosed());
        Assert.Equal(2, cage.Level);
    }

    static void SetInt(Command command, string label, int value) =>
        command.Params.OfType<IntParam>().Single(param => param.Label == label).Value = value;

    static T Start<T>(MainViewModel vm, string label) where T : Command
    {
        var button = vm.Tabs.Single(t => t.Title == "Freeform").Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct().Single(b => b.Label.Replace('\n', ' ') == label);
        button.Invoke();
        return Assert.IsType<T>(vm.Session.ActiveCommand);
    }
}
