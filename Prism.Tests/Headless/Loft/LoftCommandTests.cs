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
    [AvaloniaFact(DisplayName = "LFT-H007 | Front-view sketch rail stays visible and can be picked and removed with a loft preview")]
    [Trait("TestId", "LFT-H007")]
    public void SketchRail_WithPreview_IsOverlaidAndPickable()
    {
        var session = new CadSession();
        var lower = CreateCurve(session, [new(0, 0, 0), new(10, 0, 0)]);
        var upper = CreateCurve(session, [new(0, 0, 12), new(10, 0, 12)]);
        var sketch = new Prism.Core.Sketching.SketchModel();
        sketch.AddLine(sketch.AddPoint(5, 0), sketch.AddPoint(5, 12));
        session.EnterSketch(new Plane3(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ), sketch);
        session.ExitSketch(keep: true);
        var rail = session.Objects.Last(o => o.Kind == ObjKind.Sketch);
        var loft = StartCommand(session, lower, () => new LoftCommand(surface: true));
        loft.ConfigureInputs(() => SetSelection(loft, "Sections (in order)", lower, upper));
        session.RefreshPreview(loft);
        Assert.NotEmpty(session.Preview);
        var rails = loft.Slots.Single(slot => slot.Caption == "Guide rails");
        foreach (var slot in loft.Slots) slot.IsActive = slot == rails;
        session.Camera = new Camera { Yaw = 0, Pitch = 0, Orthographic = true, Target = new(5, 0, 6) };
        var overlay = new Overlay();
        loft.BuildOverlay(session, overlay);
        Assert.Contains(overlay.Lines, line => (line.A - new Vec3(5, 0, 0)).Length < 1e-6 &&
            (line.B - new Vec3(5, 0, 12)).Length < 1e-6 && line.Color == rail.Color);
        var pixel = session.Camera.Project(new(5, 0, 6))!.Value;
        var hit = session.Pick(pixel.x, pixel.y, rails.PickFilter)!.Value;
        Assert.Equal(rail.Id, hit.Item.Obj);
        Assert.True(loft.PickPoint(hit, false));
        Assert.Equal(hit.Item, Assert.Single(rails.Items));
        overlay.Clear();
        loft.BuildOverlay(session, overlay);
        Assert.Contains(overlay.Lines, line => line.Color == 0xFFFF8A1F);
        Assert.True(loft.PickPoint(hit, false));
        Assert.Empty(rails.Items);

        session.Update(rail.Id, obj => obj.WithVisible(false), checkpoint: false);
        overlay.Clear();
        loft.BuildOverlay(session, overlay);
        Assert.DoesNotContain(overlay.Lines, line => Math.Abs(line.A.X - 5) < 1e-6 && Math.Abs(line.B.X - 5) < 1e-6);
        session.Update(rail.Id, obj => obj.WithVisible(true), checkpoint: false);
        session.HiddenIds.Add(rail.Id);
        overlay.Clear();
        loft.BuildOverlay(session, overlay);
        Assert.DoesNotContain(overlay.Lines, line => Math.Abs(line.A.X - 5) < 1e-6 && Math.Abs(line.B.X - 5) < 1e-6);
    }

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
