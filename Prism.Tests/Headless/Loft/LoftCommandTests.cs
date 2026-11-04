using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Loft;

/// <summary>Actual Loft commands checked against prism/frustum geometry and cage invariants.</summary>
/// <remarks>Solid-output cases cover the command used by saved feature recipes and automation.</remarks>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/loft/README.md">Living Loft specification and variant boundaries.</seealso>
[Trait("Layer", "Headless")]
[Trait("Feature", "Loft")]
public class LoftCommandTests
{
    static SceneObject RectangleSection(CadSession session, double z, double width = 10, double height = 6, bool closed = true) =>
        CreateCurve(session, [new Vec3(0, 0, z), new Vec3(width, 0, z), new Vec3(width, height, z), new Vec3(0, height, z)], closed);

    [AvaloniaFact(DisplayName = "LFT-H001 | Equal rectangular sections 12 mm apart loft to a 720 mm³ solid")]
    [Trait("TestId", "LFT-H001")]
    public void EqualSections_DefaultTransitions_HavePrismVolumeAndBounds()
    {
        var session = new CadSession();
        var lower = RectangleSection(session, z: 0);
        var upper = RectangleSection(session, z: 12);
        var loft = StartCommand(session, lower, () => new LoftCommand());

        loft.ConfigureInputs(() => SetSelection(loft, "Sections (in order)", lower, upper));
        var solid = BuildSingleAddedShape(loft);

        AssertValidSolid(solid);
        AssertNear(720, solid.Props().Volume, "Loft volume (mm³)");
        AssertBounds(solid, Vec3.Zero, new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "LFT-H002 | Ruled transitions retain the same 720 mm³ prism between equal sections")]
    [Trait("TestId", "LFT-H002")]
    public void EqualSections_RuledTransitions_HavePrismVolume()
    {
        var session = new CadSession();
        var lower = RectangleSection(session, z: 0);
        var upper = RectangleSection(session, z: 12);
        var loft = StartCommand(session, lower, () => new LoftCommand());

        loft.ConfigureInputs(() =>
        {
            SetSelection(loft, "Sections (in order)", lower, upper);
            SetToggle(loft, "Straight (ruled) transitions", true);
        });
        var solid = BuildSingleAddedShape(loft);

        AssertValidSolid(solid);
        AssertNear(720, solid.Props().Volume, "Ruled loft volume (mm³)");
        AssertBounds(solid, Vec3.Zero, new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "LFT-H003 | Half-scale final section creates the analytic 420 mm³ ruled frustum")]
    [Trait("TestId", "LFT-H003")]
    public void HalfScaleUpperSection_RuledLoft_HasFrustumVolume()
    {
        var session = new CadSession();
        var lower = RectangleSection(session, z: 0);
        var upper = RectangleSection(session, z: 12, width: 5, height: 3);
        var loft = StartCommand(session, lower, () => new LoftCommand());

        loft.ConfigureInputs(() =>
        {
            SetSelection(loft, "Sections (in order)", lower, upper);
            SetToggle(loft, "Straight (ruled) transitions", true);
        });
        var solid = BuildSingleAddedShape(loft);

        AssertValidSolid(solid);
        AssertNear(420, solid.Props().Volume, "Frustum volume (mm³)");
        AssertBounds(solid, Vec3.Zero, new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "LFT-H004 | Lofting matching open three-edge sections produces 312 mm² without solid caps")]
    [Trait("TestId", "LFT-H004")]
    public void SurfaceLoft_OpenSections_HasExpectedSheetArea()
    {
        var session = new CadSession();
        var lower = RectangleSection(session, z: 0, closed: false);
        var upper = RectangleSection(session, z: 12, closed: false);
        var loft = StartCommand(session, lower, () => new LoftCommand(surface: true));

        loft.ConfigureInputs(() => SetSelection(loft, "Sections (in order)", lower, upper));
        var surface = BuildSingleAddedShape(loft);

        Assert.True(surface.IsValid);
        Assert.Equal(0, surface.Count(SubKind.Solid));
        AssertNear(312, surface.Props().Area, "Open loft area (mm²)");
        AssertBounds(surface, Vec3.Zero, new Vec3(10, 6, 12));
    }

    [AvaloniaFact(DisplayName = "LFT-H005 | A loft with only one section leaves the document unchanged")]
    [Trait("TestId", "LFT-H005")]
    public void OneSectionWithoutEndpoint_ProducesNoLoft()
    {
        var session = new CadSession();
        var lower = RectangleSection(session, z: 0);
        var history = session.Current;
        var loft = StartCommand(session, lower, () => new LoftCommand());
        loft.ConfigureInputs(() => SetSelection(loft, "Sections (in order)", lower));

        Assert.Null(loft.TryBuild());
        Assert.Equal(lower.Id, Assert.Single(session.Objects).Id);
        Assert.Same(history, session.Current);
    }

    [AvaloniaFact(DisplayName = "LFT-H006 | Freeform loft between two closed sections creates a closed 16-point, 10-face cage")]
    [Trait("TestId", "LFT-H006")]
    public void FreeformLoft_TwoClosedSections_HasExpectedCageTopology()
    {
        var session = new CadSession();
        var lower = RectangleSection(session, z: 0);
        var upper = RectangleSection(session, z: 12);
        var loft = StartCommand(session, lower, () => new FormLoftCommand());

        loft.ConfigureInputs(() =>
        {
            SetSelection(loft, "Sections (curves or sketches, in order)", lower, upper);
            loft.Params.OfType<IntParam>().Single(p => p.Label == "Points per section").Value = 8;
        });
        var output = loft.TryBuild();

        Assert.True(output != null, loft.Error);
        var cage = Assert.Single(output.Add).Form!;
        Assert.Equal(16, cage.V.Count);
        Assert.Equal(10, cage.F.Count);
        Assert.True(cage.IsClosed());
        AssertNear(0, cage.V.Min(point => point.Z), "Cage lower Z (mm)");
        AssertNear(12, cage.V.Max(point => point.Z), "Cage upper Z (mm)");
    }
}
