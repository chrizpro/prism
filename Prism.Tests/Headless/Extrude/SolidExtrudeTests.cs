using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Extrude;

/// <summary>Existing Extrude behavior exercised through real commands and OCCT, without a viewport.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/extrude/README.md#solid-extrude">Living Solid Extrude specification.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/extrude/README.md#surface-and-nurbs-extrude">Living Surface/NURBS specification; the named Surface case does not verify the NURBS tab.</seealso>
[Trait("Layer", "Headless")]
[Trait("Feature", "Extrude")]
public class SolidExtrudeTests
{
    [AvaloniaFact(DisplayName = "EXT-H001 | A 10 × 6 rectangle extruded 12 mm makes a valid 720 mm³ solid")]
    [Trait("TestId", "EXT-H001")]
    public void Rectangle_Extruded12mm_HasExpectedVolumeAndBounds()
    {
        // Arrange: a closed 10 × 6 mm profile on the XY plane.
        var session = new CadSession();
        var rectangle = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var extrude = StartCommand(session, rectangle, () => new ExtrudeCommand());

        // Act: create a new body extending 12 mm in the profile-normal direction.
        SetNumber(extrude, "Distance", 12);
        var solid = BuildSingleAddedShape(extrude);

        // Assert: 10 × 6 × 12 = 720 mm³; every bounding coordinate is checked.
        AssertValidSolid(solid);
        AssertNear(expected: 720, actual: solid.Props().Volume, "Volume (mm³)");
        AssertBounds(solid, expectedMin: Vec3.Zero, expectedMax: new Vec3(10, 6, 12));
        Assert.Equal(6, solid.Count(SubKind.Face));
        for (int index = 0; index < 6; index++)
        {
            using var face = solid.Sub(SubKind.Face, index);
            Assert.Equal("Plane", Occ.FaceInfoOf(face).Type);
        }
    }

    [AvaloniaFact(DisplayName = "EXT-H002 | Reverse direction puts the 12 mm extrusion below the XY plane")]
    [Trait("TestId", "EXT-H002")]
    public void ReverseDirection_ExtrudesFromMinus12ToZero()
    {
        var session = new CadSession();
        var rectangle = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var extrude = StartCommand(session, rectangle, () => new ExtrudeCommand());

        extrude.ConfigureInputs(() =>
        {
            SetNumber(extrude, "Distance", 12);
            SetToggle(extrude, "Reverse direction", true);
        });
        var solid = BuildSingleAddedShape(extrude);

        AssertValidSolid(solid);
        AssertNear(720, solid.Props().Volume, "Volume (mm³)");
        AssertBounds(solid, new Vec3(0, 0, -12), new Vec3(10, 6, 0));
    }

    [AvaloniaFact(DisplayName = "EXT-H003 | Symmetric total length 12 mm extends 6 mm on each side")]
    [Trait("TestId", "EXT-H003")]
    public void Symmetric12mm_ExtendsSixMillimetersOnEachSide()
    {
        var session = new CadSession();
        var rectangle = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var extrude = StartCommand(session, rectangle, () => new ExtrudeCommand());

        extrude.ConfigureInputs(() =>
        {
            SetChoice(extrude, "Extent", "Symmetric");
            SetNumber(extrude, "Distance", 12);
        });
        var solid = BuildSingleAddedShape(extrude);

        AssertValidSolid(solid);
        AssertNear(720, solid.Props().Volume, "Volume (mm³)");
        AssertBounds(solid, new Vec3(0, 0, -6), new Vec3(10, 6, 6));
    }

    [AvaloniaFact(DisplayName = "EXT-H004 | Two sides of 12 and 4 mm produce a 16 mm, 960 mm³ solid")]
    [Trait("TestId", "EXT-H004")]
    public void TwoSides12And4mm_HaveIndependentForwardAndBackwardExtents()
    {
        var session = new CadSession();
        var rectangle = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var extrude = StartCommand(session, rectangle, () => new ExtrudeCommand());

        extrude.ConfigureInputs(() =>
        {
            SetChoice(extrude, "Extent", "Two sides");
            SetNumber(extrude, "Distance", 12);
            SetNumber(extrude, "Distance 2 (other side)", 4);
        });
        var solid = BuildSingleAddedShape(extrude);

        AssertValidSolid(solid);
        AssertNear(960, solid.Props().Volume, "Volume (mm³)");
        AssertBounds(solid, new Vec3(0, 0, -4), new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "EXT-H005 | Solid Extrude rejects an open profile and preserves the document")]
    [Trait("TestId", "EXT-H005")]
    public void OpenProfile_IsRejectedWithoutChangingGeometryOrHistory()
    {
        var session = new CadSession();
        var openProfile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6, closed: false);
        var originalHistoryNode = session.Current;
        var extrude = StartCommand(session, openProfile, () => new ExtrudeCommand());

        var output = extrude.TryBuild();

        Assert.Null(output);
        Assert.False(string.IsNullOrWhiteSpace(extrude.Error));
        Assert.Equal(openProfile.Id, Assert.Single(session.Objects).Id);
        Assert.Same(originalHistoryNode, session.Current);
    }

    [AvaloniaFact(DisplayName = "EXT-H006 | Surface Extrude turns three open edges into 312 mm² of surface")]
    [Trait("TestId", "EXT-H006")]
    public void SurfaceExtrude_OpenThreeEdgeProfile_ProducesThreeUncappedFaces()
    {
        var session = new CadSession();
        var openProfile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6, closed: false);
        var extrude = StartCommand(session, openProfile, () => new ExtrudeCommand(surface: true));

        SetNumber(extrude, "Distance", 12);
        var surface = BuildSingleAddedShape(extrude);

        // Open perimeter is 10 + 6 + 10 = 26 mm; area is 26 × 12 = 312 mm².
        Assert.True(surface.IsValid);
        Assert.Equal(0, surface.Count(SubKind.Solid));
        Assert.Equal(3, surface.Count(SubKind.Face));
        AssertNear(312, surface.Props().Area, "Surface area (mm²)");
        AssertBounds(surface, Vec3.Zero, new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "EXT-H007 | Undo removes the extrusion and redo restores its 720 mm³ solid")]
    [Trait("TestId", "EXT-H007")]
    public void CommittedExtrude_UndoAndRedo_RestoreExpectedGeometry()
    {
        var session = new CadSession();
        var rectangle = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var extrude = StartCommand(session, rectangle, () => new ExtrudeCommand());
        SetNumber(extrude, "Distance", 12);
        session.CommitCommand();
        var originalBody = Assert.Single(session.Objects.Where(obj => obj.Kind == ObjKind.Solid));
        AssertNear(720, originalBody.Shape.Props().Volume, "Committed volume (mm³)");

        session.Undo();
        Assert.DoesNotContain(session.Objects, obj => obj.Kind == ObjKind.Solid);
        Assert.Equal(rectangle.Id, Assert.Single(session.Objects).Id);

        session.Redo();
        var restoredBody = Assert.Single(session.Objects.Where(obj => obj.Kind == ObjKind.Solid));
        Assert.Equal(originalBody.Id, restoredBody.Id);
        AssertValidSolid(restoredBody.Shape);
        AssertNear(720, restoredBody.Shape.Props().Volume, "Restored volume (mm³)");
        AssertBounds(restoredBody.Shape, Vec3.Zero, new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "EXT-H008 | Editing distance to 18 mm survives save/reopen as a 1080 mm³ solid")]
    [Trait("TestId", "EXT-H008")]
    public void EditedExtrusion_SavedAndReopened_PreservesGeometryAndEditableRecipe()
    {
        var session = new CadSession();
        var rectangle = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var extrude = StartCommand(session, rectangle, () => new ExtrudeCommand());
        SetNumber(extrude, "Distance", 12);
        session.CommitCommand();

        Assert.True(session.EditFeature(session.Current));
        SetNumber(session.ActiveCommand!, "Distance", 18);
        session.CommitCommand();

        string path = Path.Combine(Path.GetTempPath(), $"Prism-EXT-H008-{Guid.NewGuid():N}.prism");
        try
        {
            session.Save(path);
            var reopened = new CadSession();
            reopened.Open(path);
            var body = Assert.Single(reopened.Objects.Where(obj => obj.Kind == ObjKind.Solid));

            AssertValidSolid(body.Shape);
            AssertNear(1080, body.Shape.Props().Volume, "Reopened volume (mm³)");
            AssertBounds(body.Shape, Vec3.Zero, new Vec3(10, 6, 18));
            Assert.True(reopened.EditFeature(reopened.Current), "Saved extrusion must remain editable.");
            Assert.Equal(18, reopened.ActiveCommand!.Params.OfType<NumberParam>()
                .Single(param => param.Label == "Distance").Value);
            reopened.CancelCommand();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
