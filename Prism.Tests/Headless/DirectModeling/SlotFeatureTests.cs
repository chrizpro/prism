using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct feature recognition.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public class SlotFeatureTests
{
    static SceneObject SlottedPlate(CadSession session, double rimRadius)
    {
        using var plate = Occ.Box(60, 40, 5);
        using var bar = Occ.Translate(Occ.Box(12, 4, 12), new Vec3(0, 0, -3));
        using var left = Occ.Translate(Occ.Cylinder(2, 12), new Vec3(-6, 0, -3));
        using var right = Occ.Translate(Occ.Cylinder(2, 12), new Vec3(6, 0, -3));
        using var barAndLeft = Occ.Fuse(bar, left);
        using var cutter = Occ.Fuse(barAndLeft, right);
        var slotted = Occ.Cut(plate, cutter);
        if (rimRadius > 0)
        {
            var temp = session.AddObject(slotted, "Plate", ObjKind.Solid);
            var rim = EdgesWhere(temp, p => p.Min.Z > 4.99 && p.Max.Z < 5.01 && Math.Abs(p.Center.X) < 9 && Math.Abs(p.Center.Y) < 3).Select(e => e.Index).ToArray();
            Assert.NotEmpty(rim);
            slotted = Occ.Fillet(temp.Shape, rim, rimRadius);
        }
        return session.AddObject(slotted, "Slotted plate", ObjKind.Solid);
    }

    [AvaloniaTheory(DisplayName = "DIR-H090 | A slot is one feature, with its rounded rim when it has one")]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [Trait("TestId", "DIR-H090")]
    public void SlotIsOneFeatureWithItsRim(double rimRadius)
    {
        var session = new CadSession();
        var plate = SlottedPlate(session, rimRadius);
        var features = FeatureFinder.Features(plate);
        var slot = Assert.Single(features, f => f.Kind == FeatureKind.Slot);
        Assert.DoesNotContain(features, f => f.Kind == FeatureKind.Fillet);
        // two flat sides and two round ends, plus the rim rounds when there are any
        if (rimRadius > 0) Assert.True(slot.Faces.Count > 4, "The rim rounds belong to the slot."); else Assert.Equal(4, slot.Faces.Count);
        // clicking any face of the slot picks the whole slot
        foreach (int face in slot.Faces) Assert.Same(slot, FeatureFinder.FeatureOf(plate, face));
    }
}
