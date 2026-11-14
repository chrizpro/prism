using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sweep;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#7-sweep-modes-variable-section-and-helix">0.4 modeling gaps, item 7 (SWP-P1 twist).</seealso>
[Trait("Layer", "Headless")]
[Trait("Feature", "Sweep")]
public class SweepTwistTests
{
    /// <summary>A 10 x 10 square centred on the path start, swept 20 mm straight up.</summary>
    static Shape Sweep(Action<Command> set)
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 10, startU: -5, startV: -5);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 20)]);
        var sweep = StartCommand(session, profile, () => new SweepCommand());
        sweep.ConfigureInputs(() => { SetSelection(sweep, "Path", path); set(sweep); });
        var solid = BuildSingleAddedShape(sweep);
        AssertValidSolid(solid);
        return solid;
    }

    [AvaloniaFact(DisplayName = "SWP-H013 | A 90 degree twist keeps the volume of the straight sweep and swells the outline mid way")]
    [Trait("TestId", "SWP-H013")]
    public void TwistKeepsVolume()
    {
        var plain = Sweep(_ => { });
        var twisted = Sweep(c => SetNumber(c, "Twist along the path", 90));
        AssertClose(2000, plain.Props().Volume, 1e-3, "Straight sweep volume (mm³)");
        // equal cross-sections at every height have equal volume however they turn
        AssertClose(2000, twisted.Props().Volume, 10, "Twisted volume (mm³)");
        AssertClose(10, plain.Props().Size.X, 1e-6, "Straight width (mm)");
        // half way the square has turned 45 degrees, so its corners reach 5 * sqrt(2) from the axis
        AssertClose(2 * 5 * Math.Sqrt(2), twisted.Props().Size.X, 0.15, "Twisted width (mm)");
        AssertClose(20, twisted.Props().Size.Z, 1e-3, "Height (mm)");
    }

    [AvaloniaFact(DisplayName = "SWP-H014 | Twist and a scale list work together")]
    [Trait("TestId", "SWP-H014")]
    public void TwistWithScale()
    {
        // area 100 * s(t)^2 with s from 1 to 0.5 over 20 mm: 2000 * (1 - 0.5 + 0.25/3)
        var solid = Sweep(c => { SetNumber(c, "Twist along the path", 180); SetText(c, "Scale along the path (optional)", "0:1, 1:0.5"); });
        AssertClose(2000 * (1 - 0.5 + 0.25 / 3), solid.Props().Volume, 10, "Tapered twisted volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SWP-H015 | Twist is refused when the profile is not set to follow the path")]
    [Trait("TestId", "SWP-H015")]
    public void TwistNeedsFollowing()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 10, startU: -5, startV: -5);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 20)]);
        var sweep = StartCommand(session, profile, () => new SweepCommand());
        sweep.ConfigureInputs(() => { SetSelection(sweep, "Path", path); SetNumber(sweep, "Twist along the path", 45); SetChoice(sweep, "Profile orientation", "Keep parallel (no turning)"); });
        AssertBuildFails(sweep, "follow the path");
    }

    const string GuideSlot = "Guide curve: the profile grows and shrinks with its distance (optional)";

    static (CadSession session, Command sweep) GuidedSweep(Vec3 guideStart, Vec3 guideEnd)
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 10, startU: -5, startV: -5);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 20)]);
        var guide = CreateCurve(session, [guideStart, guideEnd]);
        var sweep = StartCommand(session, profile, () => new SweepCommand());
        sweep.ConfigureInputs(() => { SetSelection(sweep, "Path", path); SetSelection(sweep, GuideSlot, guide); });
        return (session, sweep);
    }

    [AvaloniaFact(DisplayName = "SWP-H016 | A guide curve closing in on the path shrinks the profile; a parallel guide leaves it alone")]
    [Trait("TestId", "SWP-H016")]
    public void GuideDrivesTheSize()
    {
        var (_, parallel) = GuidedSweep(new(10, 0, 0), new(10, 0, 20));
        AssertClose(2000, BuildSingleAddedShape(parallel).Props().Volume, 10, "Parallel guide volume (mm³)");

        var (_, closing) = GuidedSweep(new(10, 0, 0), new(4, 0, 20));
        var solid = BuildSingleAddedShape(closing);
        AssertValidSolid(solid);
        // the profile ends at 4 / 9.57 of its size: the volume sits between that frustum's lower bound and the straight sweep
        Assert.InRange(solid.Props().Volume, 349, 1500);
        AssertClose(10, solid.Props().Size.X, 0.01, "Widest section is the start (mm)");
    }

    [AvaloniaFact(DisplayName = "SWP-H017 | A guide curve through the path start is refused with a message")]
    [Trait("TestId", "SWP-H017")]
    public void GuideTouchingThePathIsRefused()
    {
        var (_, sweep) = GuidedSweep(new(0, 0, 0), new(5, 0, 20));
        AssertBuildFails(sweep, "touches the path");
    }

    const string SecondGuideSlot = "Second guide curve (optional)";

    static Command TwoGuides(Vec3 a0, Vec3 a1, Vec3 b0, Vec3 b1, string mode)
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 10, startU: -5, startV: -5);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 20)]);
        var first = CreateCurve(session, [a0, a1]);
        var second = CreateCurve(session, [b0, b1]);
        var sweep = StartCommand(session, profile, () => new SweepCommand());
        sweep.ConfigureInputs(() => { SetSelection(sweep, "Path", path); SetSelection(sweep, GuideSlot, first); SetSelection(sweep, SecondGuideSlot, second); SetChoice(sweep, "Between two guides", mode); });
        return sweep;
    }

    [AvaloniaFact(DisplayName = "SWP-H018 | Two guides spreading apart resize the profile evenly, to the analytic volume")]
    [Trait("TestId", "SWP-H018")]
    public void TwoGuidesResizeEvenly()
    {
        // the gap between the guides doubles from 12 to 24 mm, so the whole section doubles: 100 mm² * 20 mm * the mean of (1 + t)^2
        var solid = BuildSingleAddedShape(TwoGuides(new(6, 0, 0), new(12, 0, 20), new(-6, 0, 0), new(-12, 0, 20), "Resize evenly"));
        AssertValidSolid(solid);
        AssertClose(2000 * 7.0 / 3, solid.Props().Volume, 2000 * 7.0 / 3 * 0.01, "Evenly resized volume (mm³)");
        AssertClose(20, solid.Props().Size.X, 0.15, "Width at the far end (mm)");
        AssertClose(20, solid.Props().Size.Y, 0.15, "Depth at the far end (mm)");
    }

    [AvaloniaFact(DisplayName = "SWP-H019 | Two guides spreading apart stretch only between them, to the analytic volume")]
    [Trait("TestId", "SWP-H019")]
    public void TwoGuidesStretchBetweenThem()
    {
        var solid = BuildSingleAddedShape(TwoGuides(new(6, 0, 0), new(12, 0, 20), new(-6, 0, 0), new(-12, 0, 20), "Stretch between the guides"));
        AssertValidSolid(solid);
        // only the width between the guides grows: 100 mm² * 20 mm * the mean of (1 + t)
        AssertClose(3000, solid.Props().Volume, 30, "Stretched volume (mm³)");
        AssertClose(20, solid.Props().Size.X, 0.15, "Width at the far end (mm)");
        AssertClose(10, solid.Props().Size.Y, 0.15, "Depth stays (mm)");
    }

    [AvaloniaFact(DisplayName = "SWP-H020 | Two guides refuse a twist or scale list that would fight them")]
    [Trait("TestId", "SWP-H020")]
    public void TwoGuidesRefuseATwist()
    {
        var sweep = TwoGuides(new(6, 0, 0), new(6, 0, 20), new(-6, 0, 0), new(-6, 0, 20), "Resize evenly");
        SetNumber(sweep, "Twist along the path", 30);
        AssertBuildFails(sweep, "Clear the twist");
    }
}