using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>
/// Feasibility check for sculpting one face of a solid in place (owner direction 2026-11-16, "Option A"): the face becomes a NURBS surface,
/// its interior points move with the outline pinned, and it goes back into the body. The kernel risk is whether the result is a valid solid.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/direct-modeling/README.md">Direct modeling guide.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public class SculptFaceFeasibilityTests
{
    static (Shape Solid, double Bump) SculptTopFace(Shape box, double height, int refinements)
    {
        int top = Enumerable.Range(0, box.Count(SubKind.Face)).First(i => Occ.FaceInfoOf(box.Sub(SubKind.Face, i)).Normal.Z > 0.99);
        var face = Occ.ToNurbs(box.Sub(SubKind.Face, top));
        for (int i = 0; i < refinements; i++) face = Occ.RefineFace(face);
        var net = Occ.FaceNet(face);
        var points = net.Points.ToArray();
        // a smooth bump in the middle, zero at the outline (the outer ring of points is never touched)
        for (int r = 1; r < net.Rows - 1; r++)
            for (int c = 1; c < net.Cols - 1; c++)
            {
                double u = (double)r / (net.Rows - 1) * 2 - 1, v = (double)c / (net.Cols - 1) * 2 - 1;
                double w = Math.Max(0, 1 - (u * u + v * v)); w *= w;
                points[r * net.Cols + c] += new Vec3(0, 0, height * w);
            }
        var sculpted = Occ.SetFaceNet(face, points);
        var pieces = new List<Shape> { sculpted };
        for (int i = 0; i < box.Count(SubKind.Face); i++) if (i != top) pieces.Add(box.Sub(SubKind.Face, i));
        return (Occ.Sew(pieces, 0.01, true), 0);
    }

    [AvaloniaFact(DisplayName = "DIR-H070 | A face raised into a smooth bump with its outline fixed goes back into the box as a valid, larger solid")]
    [Trait("TestId", "DIR-H070")]
    public void BumpedFaceGivesValidSolid()
    {
        using var box = Occ.Box(40, 40, 20);
        var (solid, _) = SculptTopFace(box, 4, 2);
        Assert.True(solid.HasSolids, "the sewn result must be a solid");
        AssertValidSolid(solid);
        double volume = solid.Props().Volume;
        Assert.InRange(volume, 32000 + 1, 32000 + 40 * 40 * 4);          // more than the box, less than a full 4 mm slab on top
        Assert.InRange(solid.Props().Max.Z, 20 + 1, 20 + 4 + 0.5);
    }

    [AvaloniaFact(DisplayName = "DIR-H071 | A face pushed in (negative height) also gives a valid solid with less volume")]
    [Trait("TestId", "DIR-H071")]
    public void DentedFaceGivesValidSolid()
    {
        using var box = Occ.Box(40, 40, 20);
        var (solid, _) = SculptTopFace(box, -4, 2);
        Assert.True(solid.HasSolids);
        AssertValidSolid(solid);
        Assert.InRange(solid.Props().Volume, 32000 - 40 * 40 * 4, 32000 - 1);
    }
}
