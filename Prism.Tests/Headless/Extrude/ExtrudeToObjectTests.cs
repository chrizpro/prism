using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Extrude;

/// <summary>Characterize To object against a planar-sided reference body.
/// Refs Story #6 and docs/tabs/solid/extrude.md. Curved-target semantics remain under review.</summary>
[Trait("Layer", "Headless")]
[Trait("Feature", "Extrude")]
public class ExtrudeToObjectTests
{
    [AvaloniaFact(DisplayName = "EXT-H009 | To object stops at the target body's near side, 20 mm away")]
    [Trait("TestId", "EXT-H009")]
    public void ToBodyNearSide_Commits1200CubicMillimetersAndPreservesReferenceBody()
    {
        // Arrange: 10 × 6 mm profile at Z=0; a second solid occupies Z=20..24.
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var target = CreateBoxBody(session, minimum: new Vec3(-5, -5, 20),
            width: 20, depth: 16, height: 4);
        var originalTargetShape = target.Shape;
        var extrude = StartCommand(session, profile, () => new ExtrudeCommand());

        // Act: select the target explicitly and stop at its near side, using New body.
        extrude.ConfigureInputs(() =>
        {
            SetChoice(extrude, "Extent", "To object");
            SetChoice(extrude, "Stop at", "Near side");
            Assert.True(extrude.Slots.Single(slot => slot.Label == "Extend to (face, plane or body)")
                .TryAdd(SelItem.Of(target)));
        });
        session.CommitCommand();

        // Assert: 10 × 6 × 20 = 1200 mm³. The reference remains a separate, unchanged body.
        Assert.Null(session.ActiveCommand);
        Assert.False(session.StatusIsError, session.Status);
        Assert.Equal(2, session.Objects.Count(obj => obj.Kind == ObjKind.Solid));
        var result = Assert.Single(session.Objects.Where(obj => obj.Kind == ObjKind.Solid && obj.Id != target.Id));
        AssertValidSolid(result.Shape);
        AssertNear(1200, result.Shape.Props().Volume, "Extruded volume (mm³)");
        AssertBounds(result.Shape, Vec3.Zero, new Vec3(10, 6, 20));
        Assert.Same(originalTargetShape, session.Find(target.Id)!.Shape);
        AssertNear(1280, target.Shape.Props().Volume, "Unchanged reference volume (mm³)");
        AssertBounds(target.Shape, new Vec3(-5, -5, 20), new Vec3(15, 11, 24));
    }

    [AvaloniaFact(DisplayName = "EXT-H010 | To object far side reaches through the reference body's 4 mm thickness")]
    [Trait("TestId", "EXT-H010")]
    public void ToBodyFarSide_ExtrudesTo24mmInsteadOf20mm()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var target = CreateBoxBody(session, minimum: new Vec3(-5, -5, 20),
            width: 20, depth: 16, height: 4);
        var extrude = StartCommand(session, profile, () => new ExtrudeCommand());

        extrude.ConfigureInputs(() =>
        {
            SetChoice(extrude, "Extent", "To object");
            SetChoice(extrude, "Stop at", "Far side");
            Assert.True(extrude.Slots.Single(slot => slot.Label == "Extend to (face, plane or body)")
                .TryAdd(SelItem.Of(target)));
        });
        var solid = BuildSingleAddedShape(extrude);

        AssertValidSolid(solid);
        AssertNear(1440, solid.Props().Volume, "Volume at target far side (mm³)");
        AssertBounds(solid, Vec3.Zero, new Vec3(10, 6, 24));
    }

    [AvaloniaFact(DisplayName = "EXT-H011 | To object offset minus 2 mm leaves a 2 mm gap before the target")]
    [Trait("TestId", "EXT-H011")]
    public void ToBodyNearSide_WithNegativeOffset_StopsTwoMillimetersShort()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var target = CreateBoxBody(session, minimum: new Vec3(-5, -5, 20),
            width: 20, depth: 16, height: 4);
        var extrude = StartCommand(session, profile, () => new ExtrudeCommand());

        extrude.ConfigureInputs(() =>
        {
            SetChoice(extrude, "Extent", "To object");
            SetChoice(extrude, "Stop at", "Near side");
            SetNumber(extrude, "Offset from the target", -2);
            Assert.True(extrude.Slots.Single(slot => slot.Label == "Extend to (face, plane or body)")
                .TryAdd(SelItem.Of(target)));
        });
        var solid = BuildSingleAddedShape(extrude);

        AssertValidSolid(solid);
        AssertNear(1080, solid.Props().Volume, "Volume at 18 mm (mm³)");
        AssertBounds(solid, Vec3.Zero, new Vec3(10, 6, 18));
        AssertNear(2, Occ.Distance(solid, target.Shape).Distance, "Gap to reference body (mm)");
    }

    [AvaloniaFact(DisplayName = "EXT-H012 | To object waits for an explicit target even when another solid exists")]
    [Trait("TestId", "EXT-H012")]
    public void ToObjectWithoutSelectedTarget_ProducesNoOutputAndPreservesDocument()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var target = CreateBoxBody(session, minimum: new Vec3(-5, -5, 20),
            width: 20, depth: 16, height: 4);
        var originalHistory = session.Current;
        var extrude = StartCommand(session, profile, () => new ExtrudeCommand());

        SetChoice(extrude, "Extent", "To object");

        Assert.Null(extrude.TryBuild());
        Assert.Equal(2, session.Objects.Count);
        Assert.Equal(profile.Id, session.Objects.Single(obj => obj.Kind == ObjKind.Sketch).Id);
        Assert.Same(target.Shape, session.Find(target.Id)!.Shape);
        Assert.Same(originalHistory, session.Current);
    }

    [AvaloniaFact(DisplayName = "EXT-H013 | To object rejects a body behind the profile without changing the document")]
    [Trait("TestId", "EXT-H013")]
    public void ToBodyBehindProfile_ReportsUnreachableTargetAndPreservesDocument()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var target = CreateBoxBody(session, minimum: new Vec3(-5, -5, -24),
            width: 20, depth: 16, height: 4);
        var originalHistory = session.Current;
        var extrude = StartCommand(session, profile, () => new ExtrudeCommand());

        extrude.ConfigureInputs(() =>
        {
            SetChoice(extrude, "Extent", "To object");
            Assert.True(extrude.Slots.Single(slot => slot.Label == "Extend to (face, plane or body)")
                .TryAdd(SelItem.Of(target)));
        });

        Assert.Null(extrude.TryBuild());
        Assert.Contains("behind", extrude.Error ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, session.Objects.Count);
        Assert.Same(target.Shape, session.Find(target.Id)!.Shape);
        Assert.Same(originalHistory, session.Current);
    }

    [AvaloniaFact(DisplayName = "EXT-H014 | Reverse direction reaches the target's near side at Z = minus 20 mm")]
    [Trait("TestId", "EXT-H014")]
    public void ToBodyBehindProfile_WithReverseDirection_ReachesNegative20mm()
    {
        var session = new CadSession();
        var profile = CreateRectangleSketch(session, Plane3.XY, width: 10, height: 6);
        var target = CreateBoxBody(session, minimum: new Vec3(-5, -5, -24),
            width: 20, depth: 16, height: 4);
        var extrude = StartCommand(session, profile, () => new ExtrudeCommand());

        extrude.ConfigureInputs(() =>
        {
            SetChoice(extrude, "Extent", "To object");
            SetChoice(extrude, "Stop at", "Near side");
            SetToggle(extrude, "Reverse direction", true);
            Assert.True(extrude.Slots.Single(slot => slot.Label == "Extend to (face, plane or body)")
                .TryAdd(SelItem.Of(target)));
        });
        var solid = BuildSingleAddedShape(extrude);

        AssertValidSolid(solid);
        AssertNear(1200, solid.Props().Volume, "Reversed To object volume (mm³)");
        AssertBounds(solid, new Vec3(0, 0, -20), new Vec3(10, 6, 0));
    }
}
