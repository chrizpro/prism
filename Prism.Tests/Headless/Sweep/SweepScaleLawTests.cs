using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sweep;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#7-sweep-modes-variable-section-and-helix">0.4 modeling gaps, item 7 (SWP-P1).</seealso>
[Trait("Layer", "Headless")]
[Trait("Feature", "Sweep")]
public class SweepScaleLawTests
{
    /// <summary>A 10 x 6 rectangle (60 mm²) swept 12 mm straight up; the scale settings are applied by the test.</summary>
    static double Volume(Action<Command> set)
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 12)]);
        var sweep = StartCommand(session, profile, () => new SweepCommand());
        sweep.ConfigureInputs(() => { SetSelection(sweep, "Path", path); set(sweep); });
        var solid = BuildSingleAddedShape(sweep);
        AssertValidSolid(solid);
        return solid.Props().Volume;
    }

    // a rectangle scaled from a to b over height h has volume h/3 * (A + sqrt(A*B) + B), A = 60a², B = 60b²
    static double Frustum(double a, double b) => 12.0 / 3 * (60 * a * a + 60 * a * b + 60 * b * b);

    [AvaloniaTheory(DisplayName = "SWP-H010 | A start scale and an end scale give the analytic frustum volume")]
    [InlineData(2.0, 1.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(0.5, 2.0)]
    [Trait("TestId", "SWP-H010")]
    public void StartAndEndScale(double start, double end) =>
        AssertNear(Frustum(start, end), Volume(c => { SetNumber(c, "Scale at the start", start); SetNumber(c, "Scale at the end", end); }), "Frustum volume (mm³)");

    [AvaloniaFact(DisplayName = "SWP-H011 | A scale list replaces the start and end scale, and a flat list matches a straight sweep")]
    [Trait("TestId", "SWP-H011")]
    public void ScaleListFollowsTheLaw()
    {
        AssertNear(Frustum(1, 0.5), Volume(c => SetText(c, "Scale along the path (optional)", "0:1, 1:0.5")), "Linear list (mm³)");
        AssertNear(720, Volume(c => SetText(c, "Scale along the path (optional)", "0:1, 0.5:1, 1:1")), "Constant list (mm³)");
        // a belly in the middle holds more material than the straight taper between the same ends
        Assert.True(Volume(c => SetText(c, "Scale along the path (optional)", "0:1, 0.5:2, 1:1")) > 720 + 1, "a swelling profile is bigger than a constant one");
    }

    [AvaloniaFact(DisplayName = "SWP-H012 | A scale list that is not positions and scales is refused with a message")]
    [Trait("TestId", "SWP-H012")]
    public void ScaleListRejectsText()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 12)]);
        var sweep = StartCommand(session, profile, () => new SweepCommand());
        sweep.ConfigureInputs(() => { SetSelection(sweep, "Path", path); SetText(sweep, "Scale along the path (optional)", "big at the top"); });
        AssertBuildFails(sweep, "position and scale");
    }
}
