using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Revolve;

/// <summary>Revolve a rectangle spanning radius 2..5 mm and height 0..8 mm about world Z.
/// Full volume is π × (5² − 2²) × 8 = 168π mm³.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/tabs/revolve/README.md">Canonical living Solid/Surface Revolve specification and unverified variant boundaries.</seealso>
[Trait("Layer", "Headless")]
[Trait("Feature", "Revolve")]
public class SolidRevolveTests
{
    [AvaloniaFact(DisplayName = "REV-H001 | Full revolve makes a valid annular cylinder of volume 168π mm³")]
    [Trait("TestId", "REV-H001")]
    public void FullRevolve_AnnularProfile_HasAnalyticVolumeAndBounds()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XZ, width: 3, height: 8, startU: 2);
        var revolve = StartCommand(session, profile, () => new RevolveCommand());

        SetChoice(revolve, "Extent", "Full 360°");
        var solid = BuildSingleAddedShape(revolve);

        AssertValidSolid(solid);
        AssertNear(168 * Math.PI, solid.Props().Volume, "Volume (mm³)");
        AssertBounds(solid, new Vec3(-5, -5, 0), new Vec3(5, 5, 8));
        Assert.Equal(4, solid.Count(SubKind.Face));
    }

    [AvaloniaFact(DisplayName = "REV-H002 | A 90 degree revolve occupies positive X/Y with volume 42π mm³")]
    [Trait("TestId", "REV-H002")]
    public void QuarterRevolve_HasQuarterVolumeAndPositiveQuadrantBounds()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XZ, width: 3, height: 8, startU: 2);
        var revolve = StartCommand(session, profile, () => new RevolveCommand());

        SetNumber(revolve, "Angle", 90);
        var solid = BuildSingleAddedShape(revolve);

        AssertValidSolid(solid);
        AssertNear(42 * Math.PI, solid.Props().Volume, "Quarter volume (mm³)");
        AssertBounds(solid, Vec3.Zero, new Vec3(5, 5, 8));
        Assert.Equal(6, solid.Count(SubKind.Face));
    }

    [AvaloniaFact(DisplayName = "REV-H003 | Reversing 90 degrees moves the quarter revolve into negative Y")]
    [Trait("TestId", "REV-H003")]
    public void ReversedQuarterRevolve_PreservesVolumeAndChangesQuadrant()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XZ, width: 3, height: 8, startU: 2);
        var revolve = StartCommand(session, profile, () => new RevolveCommand());

        revolve.ConfigureInputs(() =>
        {
            SetNumber(revolve, "Angle", 90);
            SetToggle(revolve, "Reverse direction", true);
        });
        var solid = BuildSingleAddedShape(revolve);

        AssertValidSolid(solid);
        AssertNear(42 * Math.PI, solid.Props().Volume, "Reversed quarter volume (mm³)");
        AssertBounds(solid, new Vec3(0, -5, 0), new Vec3(5, 0, 8));
    }

    [AvaloniaFact(DisplayName = "REV-H004 | Symmetric 180 degrees spans both sides of the profile with volume 84π")]
    [Trait("TestId", "REV-H004")]
    public void Symmetric180Degrees_Extends90DegreesOnEachSide()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XZ, width: 3, height: 8, startU: 2);
        var revolve = StartCommand(session, profile, () => new RevolveCommand());

        revolve.ConfigureInputs(() =>
        {
            SetChoice(revolve, "Extent", "Symmetric");
            SetNumber(revolve, "Angle", 180);
        });
        var solid = BuildSingleAddedShape(revolve);

        AssertValidSolid(solid);
        AssertNear(84 * Math.PI, solid.Props().Volume, "Half volume (mm³)");
        AssertBounds(solid, new Vec3(0, -5, 0), new Vec3(5, 5, 8));
    }

    [AvaloniaFact(DisplayName = "REV-H005 | Picking the origin Z axis produces the same full annular cylinder")]
    [Trait("TestId", "REV-H005")]
    public void PickedOriginZAxis_ProducesExpectedFullRevolve()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XZ, width: 3, height: 8, startU: 2);
        var revolve = StartCommand(session, profile, () => new RevolveCommand());

        revolve.ConfigureInputs(() =>
        {
            SetChoice(revolve, "Axis", "Pick a line");
            SetChoice(revolve, "Extent", "Full 360°");
            // CadSession origin indices: 3 = X axis, 4 = Y axis, 5 = Z axis.
            revolve.Slots.Single(slot => slot.Label == "Axis line")
                .TryAdd(new SelItem(Guid.Empty, SelKind.Origin, 5));
        });
        var solid = BuildSingleAddedShape(revolve);

        AssertValidSolid(solid);
        AssertNear(168 * Math.PI, solid.Props().Volume, "Picked-axis volume (mm³)");
        AssertBounds(solid, new Vec3(-5, -5, 0), new Vec3(5, 5, 8));
    }

    [AvaloniaFact(DisplayName = "REV-H006 | Zero symmetric angle produces no output or document changes")]
    [Trait("TestId", "REV-H006")]
    public void ZeroSymmetricAngle_LeavesGeometryAndHistoryUnchanged()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XZ, width: 3, height: 8, startU: 2);
        var originalHistoryNode = session.Current;
        var revolve = StartCommand(session, profile, () => new RevolveCommand());

        revolve.ConfigureInputs(() =>
        {
            SetChoice(revolve, "Extent", "Symmetric");
            SetNumber(revolve, "Angle", 0);
        });

        Assert.Null(revolve.TryBuild());
        Assert.Equal(profile.Id, Assert.Single(session.Objects).Id);
        Assert.Same(originalHistoryNode, session.Current);
    }

    [AvaloniaFact(DisplayName = "REV-H007 | Surface Revolve creates four faces of area 154π mm² without a solid")]
    [Trait("TestId", "REV-H007")]
    public void SurfaceRevolve_ClosedProfile_ProducesValidShellWithoutSolid()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XZ, width: 3, height: 8, startU: 2);
        var revolve = StartCommand(session, profile, () => new RevolveCommand(surface: true));

        SetChoice(revolve, "Extent", "Full 360°");
        var surface = BuildSingleAddedShape(revolve);

        // Cylinders: 2π × (5 + 2) × 8; annular ends: 2π × (25 − 4).
        Assert.True(surface.IsValid);
        Assert.Equal(0, surface.Count(SubKind.Solid));
        Assert.Equal(4, surface.Count(SubKind.Face));
        AssertNear(154 * Math.PI, surface.Props().Area, "Shell area (mm²)");
        AssertBounds(surface, new Vec3(-5, -5, 0), new Vec3(5, 5, 8));
    }

    [AvaloniaFact(DisplayName = "REV-H008 | Undo removes a committed revolve and redo restores its geometry")]
    [Trait("TestId", "REV-H008")]
    public void CommittedRevolve_UndoAndRedo_RestoreAnalyticVolume()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XZ, width: 3, height: 8, startU: 2);
        var revolve = StartCommand(session, profile, () => new RevolveCommand());
        SetChoice(revolve, "Extent", "Full 360°");
        session.CommitCommand();
        var originalBody = Assert.Single(session.Objects.Where(obj => obj.Kind == ObjKind.Solid));
        AssertNear(168 * Math.PI, originalBody.Shape.Props().Volume, "Committed volume (mm³)");

        session.Undo();
        Assert.DoesNotContain(session.Objects, obj => obj.Kind == ObjKind.Solid);

        session.Redo();
        var restoredBody = Assert.Single(session.Objects.Where(obj => obj.Kind == ObjKind.Solid));
        Assert.Equal(originalBody.Id, restoredBody.Id);
        AssertValidSolid(restoredBody.Shape);
        AssertNear(168 * Math.PI, restoredBody.Shape.Props().Volume, "Restored volume (mm³)");
        AssertBounds(restoredBody.Shape, new Vec3(-5, -5, 0), new Vec3(5, 5, 8));
    }
}
