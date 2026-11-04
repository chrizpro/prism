using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sweep;

/// <summary>Actual Sweep commands compared with simple independent straight-path geometry.</summary>
/// <remarks>Solid-output cases cover the command used by saved feature recipes and automation.</remarks>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sweep/README.md">Living Sweep specification and variant boundaries.</seealso>
[Trait("Layer", "Headless")]
[Trait("Feature", "Sweep")]
public class SweepCommandTests
{
    [AvaloniaFact(DisplayName = "SWP-H001 | A 10 × 6 profile swept along 12 mm makes a 720 mm³ solid")]
    [Trait("TestId", "SWP-H001")]
    public void StraightPath_DefaultOrientation_HasAnalyticVolumeAndBounds()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 12)]);
        var sweep = StartCommand(session, profile, () => new SweepCommand());

        sweep.ConfigureInputs(() => SetSelection(sweep, "Path", path));
        var solid = BuildSingleAddedShape(sweep);

        AssertValidSolid(solid);
        AssertNear(720, solid.Props().Volume, "Sweep volume (mm³)");
        AssertBounds(solid, Vec3.Zero, new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "SWP-H002 | Keep parallel preserves a straight sweep's dimensions")]
    [Trait("TestId", "SWP-H002")]
    public void StraightPath_KeepParallel_PreservesRectangularCrossSection()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 12)]);
        var sweep = StartCommand(session, profile, () => new SweepCommand());

        sweep.ConfigureInputs(() =>
        {
            SetSelection(sweep, "Path", path);
            SetChoice(sweep, "Profile orientation", "Keep parallel (no turning)");
        });
        var solid = BuildSingleAddedShape(sweep);

        AssertValidSolid(solid);
        AssertNear(720, solid.Props().Volume, "Parallel sweep volume (mm³)");
        AssertBounds(solid, Vec3.Zero, new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "SWP-H003 | Half-scale sweep end produces the analytic 420 mm³ frustum volume")]
    [Trait("TestId", "SWP-H003")]
    public void StraightPath_HalfEndScale_HasFrustumVolume()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 12)]);
        var sweep = StartCommand(session, profile, () => new SweepCommand());

        sweep.ConfigureInputs(() =>
        {
            SetSelection(sweep, "Path", path);
            SetNumber(sweep, "Scale at the end", 0.5);
        });
        var solid = BuildSingleAddedShape(sweep);

        // A=60, L=12, k=0.5: A*L*(1+k+k²)/3 = 420 mm³.
        AssertValidSolid(solid);
        AssertNear(420, solid.Props().Volume, "Tapered sweep volume (mm³)");
        AssertNear(12, solid.Props().Size.Z, "Path extent (mm)");
    }

    [AvaloniaFact(DisplayName = "SWP-H004 | Sweeping three open edges creates 312 mm² of uncapped surface")]
    [Trait("TestId", "SWP-H004")]
    public void SurfaceSweep_OpenProfile_HasExpectedAreaAndNoSolid()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6, closed: false);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 12)]);
        var sweep = StartCommand(session, profile, () => new SweepCommand(surface: true));

        sweep.ConfigureInputs(() => SetSelection(sweep, "Path", path));
        var surface = BuildSingleAddedShape(sweep);

        Assert.True(surface.IsValid);
        Assert.Equal(0, surface.Count(SubKind.Solid));
        Assert.Equal(3, surface.Count(SubKind.Face));
        AssertNear(312, surface.Props().Area, "Surface area (mm²)");
        AssertBounds(surface, Vec3.Zero, new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "SWP-H005 | Missing path produces no sweep or document changes")]
    [Trait("TestId", "SWP-H005")]
    public void MissingPath_LeavesProfileAndHistoryUnchanged()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var history = session.Current;
        var sweep = StartCommand(session, profile, () => new SweepCommand());

        Assert.Null(sweep.TryBuild());
        Assert.Equal(profile.Id, Assert.Single(session.Objects).Id);
        Assert.Same(history, session.Current);
    }

    [AvaloniaFact(DisplayName = "SWP-H006 | Freeform straight sweep creates a closed 32-point, 26-face cage")]
    [Trait("TestId", "SWP-H006")]
    public void FreeformSweep_CappedStraightPath_HasExpectedCageTopology()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(0, 0, 12)]);
        var sweep = StartCommand(session, profile, () => new FormSweepCommand());

        sweep.ConfigureInputs(() =>
        {
            SetSelection(sweep, "Path (curve or edge)", path);
            sweep.Params.OfType<IntParam>().Single(p => p.Label == "Points on the profile").Value = 8;
            sweep.Params.OfType<IntParam>().Single(p => p.Label == "Steps along the path").Value = 3;
        });
        var output = sweep.TryBuild();

        Assert.True(output != null, sweep.Error);
        var cage = Assert.Single(output.Add).Form!;
        Assert.Equal(32, cage.V.Count);
        Assert.Equal(26, cage.F.Count);
        Assert.True(cage.IsClosed());
        AssertNear(0, cage.V.Min(point => point.Z), "Cage start Z (mm)");
        AssertNear(12, cage.V.Max(point => point.Z), "Cage end Z (mm)");
    }
}
