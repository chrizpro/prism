using Prism.Core.Form;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Freeform;

/// <summary>Fill holes at the selection only, and sharpening feature edges. Owner reports and requests 2026-11-16.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/freeform/README.md">Freeform guide.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Unit")]
public class FormHoleTests
{
    /// <summary>A 60 x 60 mm plane of 6 x 6 faces (10 mm cells) with two inner faces removed, 42 mm apart: two closed holes (a face at the plate's edge would only notch it).</summary>
    static (SubdMesh mesh, Vec3 holeA, Vec3 holeB) PlateWithTwoHoles()
    {
        var m = FormOps.Plane(60, 60, 6, 6);
        int Nearest(Vec3 p) => Enumerable.Range(0, m.F.Count).MinBy(i => m.FaceCenter(i).DistanceTo(new Vec3(p.X, p.Y, m.FaceCenter(i).Z)));
        int a = Nearest(new Vec3(-15, -15, 0)), b = Nearest(new Vec3(15, 15, 0));
        var holeA = m.FaceCenter(a); var holeB = m.FaceCenter(b);
        FormOps.DeleteFaces(m, new HashSet<int> { a, b });
        return (m, holeA, holeB);
    }

    static HashSet<int> PointsNear(SubdMesh m, Vec3 centre, double radius) =>
        Enumerable.Range(0, m.V.Count).Where(i => m.V[i].DistanceTo(centre) < radius).ToHashSet();

    [Fact(DisplayName = "FRM-U050 | Fill holes with a selection fills only the hole at it and leaves the far one open")]
    [Trait("TestId", "FRM-U050")]
    public void FillsOnlyTheHoleAtTheSelection()
    {
        var (m, a, b) = PlateWithTwoHoles();
        int before = m.F.Count;                                     // 34 faces: two holes
        int filled = FormOps.FillHoles(m, PointsNear(m, a, 10), out int found);      // a 10 mm plane cell has corners 7 mm from its centre
        Assert.Equal(3, found);                                     // the two holes, and the sheet's own outer edge, which is an open border too
        Assert.Equal(1, filled);
        Assert.Equal(before + 1, m.F.Count);
        // the far hole is still there (and so is the outer edge): asking about it finds two open borders and fills the one at the selection
        int farFilled = FormOps.FillHoles(m, PointsNear(m, b, 10), out int stillOpen);
        Assert.Equal(2, stillOpen);
        Assert.Equal(1, farFilled);
        Assert.Equal(before + 2, m.F.Count);
    }

    [Fact(DisplayName = "FRM-U051 | A selection with no hole at it fills nothing but reports the holes elsewhere; no selection fills every hole")]
    [Trait("TestId", "FRM-U051")]
    public void NothingNearAndEverything()
    {
        var (m, _, _) = PlateWithTwoHoles();
        int before = m.F.Count;
        int filled = FormOps.FillHoles(m, PointsNear(m, new Vec3(0, 0, 0), 4), out int found);        // the middle of the plate: no hole there
        Assert.Equal(0, filled);
        Assert.Equal(3, found);
        Assert.Equal(before, m.F.Count);                            // the old behaviour would have capped everything, wherever you had selected

        Assert.Equal(3, FormOps.FillHoles(m, null, out _));         // nothing selected: every open border, as before
        Assert.Equal(before + 3, m.F.Count);
        Assert.Equal(0, FormOps.FillHoles(m));                      // and the form has none left
    }

    [Fact(DisplayName = "FRM-U052 | Sharpening feature edges creases a box's 12 edges and a cylinder's two rims, but not a round cylinder's sides or a flat plane")]
    [Trait("TestId", "FRM-U052")]
    public void FeatureEdges()
    {
        var box = FormOps.Box(60, 60, 60, 1, 1, 1);
        Assert.Equal(12, FormOps.CreaseFeatureEdges(box));
        Assert.All(box.Crease.Values, s => Assert.Equal(5, s));

        var split = FormOps.Box(60, 60, 60, 2, 2, 2);               // each box edge is two edges long now, and the flat faces between stay smooth
        Assert.Equal(24, FormOps.CreaseFeatureEdges(split));

        var cylinder = FormOps.Cylinder(25, 60, 8, 3, true);        // 45 degrees between neighbouring sides: gentle, so smooth; the cap rims are 90 degrees
        Assert.Equal(16, FormOps.CreaseFeatureEdges(cylinder));

        Assert.Equal(0, FormOps.CreaseFeatureEdges(FormOps.Plane(80, 80, 4, 4)));
        Assert.Equal(0, FormOps.CreaseFeatureEdges(FormOps.Sphere(30, 3)));
    }
}
