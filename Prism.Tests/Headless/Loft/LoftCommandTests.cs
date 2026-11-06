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
public class LoftCommandTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [AvaloniaTheory(DisplayName = "LFT-H009 | Straight matching rails retain the analytic square frustum")]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [Trait("TestId", "LFT-H009")]
    public void StraightMatchingRails_RetainFrustum(int railCount, bool reverseRail)
    {
        var session = new CadSession();
        var lower = RectangleSection(session, 0, 10, 10);
        var upper = RectangleSection(session, 20, 12, 12);
        var first = CreateCurve(session, reverseRail ? [new(0, 0, 20), Vec3.Zero] : [Vec3.Zero, new(0, 0, 20)]);
        var second = CreateCurve(session, [new(10, 10, 0), new(12, 12, 20)]);
        var loft = Begin(session, () => new LoftCommand());
        loft.ConfigureInputs(() =>
        {
            SetSelection(loft, "Sections (in order)", lower, upper);
            SetSelection(loft, "Guide rails (optional, one or two)", railCount == 1 ? [first] : [first, second]);
        });
        var solid = BuildSingleAddedShape(loft);
        AssertValidSolid(solid);
        AssertNear(20.0 / 3 * (100 + 120 + 144), solid.Props().Volume, "Guided square frustum volume (mm³)");
        AssertBounds(solid, Vec3.Zero, new(12, 12, 20));
    }

    [AvaloniaTheory(DisplayName = "LFT-H008 | Curved two-rail ellipse loft retains profiles and follows both rails without a dense display mesh")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("TestId", "LFT-H008")]
    public void CurvedRails_EllipticalSections_FollowRailsInEitherProfileOrder(bool reverse, bool swapRails)
    {
        var session = new CadSession();
        var lower = session.AddObject(Occ.Ellipse(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, 71.67792467182227, 39.178793951444064), "Lower", ObjKind.Curve);
        var upper = session.AddObject(Occ.Ellipse(new(0, 0, 120), Vec3.UnitZ, Vec3.UnitX, 31.59994528542702, 27.232473172807023), "Upper", ObjKind.Curve);
        var right = session.AddObject(Occ.Spline([new(31.59994528542702, 0, 120), new(31.59994528542702, 0, 77.1344648707616), new(71.67792467182227, 0, 0)]), "Right rail", ObjKind.Curve);
        var left = session.AddObject(Occ.Spline([new(-31.59994528542702, 0, 120), new(-22.202528792560337, 0, 83.04166993725521), new(-71.67792467182227, 0, 0)]), "Left rail", ObjKind.Curve);
        var loft = Begin(session, () => new LoftCommand(surface: true));
        loft.ConfigureInputs(() =>
        {
            SetSelection(loft, "Sections (in order)", reverse ? lower : upper, reverse ? upper : lower);
            SetSelection(loft, "Guide rails (optional, one or two)", swapRails ? left : right, swapRails ? right : left);
        });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var surface = BuildSingleAddedShape(loft);
        output.WriteLine($"Build: {clock.Elapsed.TotalSeconds:F3} s");
        Assert.True(surface.IsValid);
        Assert.Equal(0, surface.Count(SubKind.Solid));
        foreach (var source in new[] { lower, upper, right, left })
        {
            double maximumGap = 0;
            foreach (var sample in Occ.CurveSamples(source.Shape, 49, 1)[0])
            {
                using var vertex = Occ.Vertex(sample.p);
                maximumGap = Math.Max(maximumGap, Occ.Distance(vertex, surface).Distance);
            }
            output.WriteLine($"{source.Name}: maximum of 49 sampled gaps = {maximumGap:F6} mm");
            // End profiles remain exact; the sampled rail fit interpolates only
            // twelve section planes and does not promise exact rail coincidence between them.
            Assert.InRange(maximumGap, 0, source == lower || source == upper ? 1e-5 : 0.05);
        }
        // Bound representation complexity rather than relying on workstation timing.
        clock.Restart();
        int triangles = surface.Tessellate().Indices.Length / 3;
        output.WriteLine($"Display mesh: {clock.Elapsed.TotalSeconds:F3} s, {triangles} triangles");
        Assert.InRange(triangles, 1, 30000);
        Assert.Equal(4, session.Objects.Count);
    }

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
