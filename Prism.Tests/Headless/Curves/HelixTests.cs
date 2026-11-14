using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Curves;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#7-sweep-modes-variable-section-and-helix">0.4 modeling gaps, item 7 (SWP-P2).</seealso>
[Trait("Feature", "Curves")]
[Trait("Layer", "Headless")]
public class HelixTests
{
    static Shape Helix(Action<EquationCurveCommand>? set = null)
    {
        var session = new CadSession();
        session.StartCommand(() => new EquationCurveCommand(false));
        var command = Assert.IsType<EquationCurveCommand>(session.ActiveCommand);
        SetChoice(command, "Points from", "Helix");
        set?.Invoke(command);
        return BuildSingleAddedShape(command);
    }

    [AvaloniaFact(DisplayName = "CRV-H041 | A helix of radius 10, pitch 5 and 5 turns is 25 mm along its axis and the expected length")]
    [Trait("TestId", "CRV-H041")]
    public void HelixHasTheRequestedSize()
    {
        using var helix = Helix(c => { SetNumber(c, "Helix radius", 10); SetNumber(c, "Pitch (rise per turn)", 5); SetNumber(c, "Turns", 5); });
        var box = helix.Props();
        AssertClose(25, box.Size.Z, 1e-3, "Rise along the axis (mm)");
        AssertClose(20, box.Size.X, 0.2, "Width across the coils (mm)");
        double expected = 5 * Math.Sqrt(Math.Pow(2 * Math.PI * 10, 2) + 25);
        AssertClose(expected, Occ.CurveLength(helix), 1.0, "Helix length (mm)");
    }

    [AvaloniaFact(DisplayName = "CRV-H042 | Axis turns the helix onto X or Y, left-handed reverses its twist, and an end radius tapers it")]
    [Trait("TestId", "CRV-H042")]
    public void HelixAxisHandAndTaper()
    {
        using var alongX = Helix(c => { SetChoice(c, "Axis", "X"); SetNumber(c, "Pitch (rise per turn)", 4); SetNumber(c, "Turns", 3); });
        AssertClose(12, alongX.Props().Size.X, 1e-3, "Rise along X (mm)");

        Vec3 AfterQuarterTurn(string hand)
        {
            using var h = Helix(c => { SetChoice(c, "Handedness", hand); SetNumber(c, "Turns", 1); });
            var pts = Occ.CurveDivide(h, 4).points;
            return pts[1];
        }
        Assert.True(AfterQuarterTurn("Right-handed").Y * AfterQuarterTurn("Left-handed").Y < 0, "the two hands wind opposite ways");

        using var taper = Helix(c => { SetNumber(c, "Helix radius", 10); SetNumber(c, "End radius (0 = same)", 4); SetNumber(c, "Turns", 4); });
        var ends = Ends(taper);
        Assert.True(Math.Abs(ends.End.X * ends.End.X + ends.End.Y * ends.End.Y - 16) < 0.5 && ends.Start.DistanceTo(new Vec3(10, 0, 0)) < 1e-3, "starts at the radius and ends at the end radius");
    }
}
