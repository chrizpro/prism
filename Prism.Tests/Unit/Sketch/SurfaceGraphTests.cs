using Prism.Core;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Sketch;

/// <summary>Laying a smooth curve on welded triangle surfaces, the engine behind Draw on Surface. Needs no kernel: the meshes are written by hand.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sketches/README.md#drawing-directly-on-a-surface">Drawing directly on a surface.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Unit")]
public class SurfaceGraphTests
{
    // The floor (z = 0, x 0..10) and the wall (x = 10, z 0..10) of a box corner, as two separate parts that share the edge x = 10, z = 0.
    static (float[], int[]) Floor() => ([0, 0, 0, 10, 0, 0, 10, 10, 0, 0, 10, 0], [0, 1, 2, 0, 2, 3]);
    static (float[], int[]) Wall() => ([10, 0, 0, 10, 10, 0, 10, 10, 10, 10, 0, 10], [0, 1, 2, 0, 2, 3]);
    static (float[], int[]) Far() => ([100, 0, 0, 110, 0, 0, 110, 10, 0, 100, 10, 0], [0, 1, 2, 0, 2, 3]);

    // A faceted cylinder (radius 10, 24 sides, z 0..20) standing for a curved face.
    static (float[], int[]) Cylinder()
    {
        const int sides = 24, rings = 10; var pos = new List<float>(); var idx = new List<int>();
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s < sides; s++) { double a = 2 * Math.PI * s / sides; pos.AddRange([(float)(10 * Math.Cos(a)), (float)(10 * Math.Sin(a)), 20f * r / rings]); }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = r * sides + s, b = r * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                idx.AddRange([a, b, d, a, d, c]);
            }
        return (pos.ToArray(), idx.ToArray());
    }

    static (List<Vec3> Points, List<bool> Pinned)? Lay(SurfaceGraph g, int[] parts, params Vec3[] picks)
    {
        var on = new Vec3[picks.Length]; var tris = new int[picks.Length];
        for (int i = 0; i < picks.Length; i++) tris[i] = g.FindTriangle(picks[i], parts[i], out on[i]);
        return g.Trace(on, tris);
    }

    [Fact(DisplayName = "SKT-SF001 | A point off the surface is moved onto the nearest triangle of the chosen part")]
    [Trait("TestId", "SKT-SF001")]
    public void PointsAreMovedOntoTheSurface()
    {
        var g = new SurfaceGraph([Floor(), Wall()]);
        int tri = g.FindTriangle(new Vec3(4, 3, 7), 0, out var on);
        Assert.True(tri >= 0);
        Assert.Equal(0, on.Z, 6);
        Assert.Equal(4, on.X, 6);
        Assert.Equal(3, on.Y, 6);
    }

    [Fact(DisplayName = "SKT-SF002 | A curve over the edge between two faces stays on both faces and keeps the clicked points")]
    [Trait("TestId", "SKT-SF002")]
    public void CurveCrossesTheSharedEdgeOfTwoFaces()
    {
        var g = new SurfaceGraph([Floor(), Wall()]);
        var a = new Vec3(2, 5, 0); var b = new Vec3(10, 5, 6);
        var curve = Lay(g, [0, 1], a, b);
        Assert.NotNull(curve);
        var (points, pinned) = curve!.Value;
        Assert.Equal(a, points[0]); Assert.Equal(b, points[^1]);
        Assert.Equal(2, pinned.Count(p => p));
        foreach (var p in points)
            Assert.True(Math.Abs(p.Z) < 1e-6 && p.X <= 10 + 1e-6 || Math.Abs(p.X - 10) < 1e-6 && p.Z >= -1e-6, $"{p} left the surface");
        Assert.Contains(points, p => Math.Abs(p.X - 10) < 1e-6);        // it reached the wall
        Assert.Contains(points, p => Math.Abs(p.Z) < 1e-6 && p.X < 9);  // and ran on the floor
    }

    [Fact(DisplayName = "SKT-SF003 | Surfaces that do not touch are not joined: a curve never crosses empty space")]
    [Trait("TestId", "SKT-SF003")]
    public void SeparateSurfacesAreNotJoined()
    {
        var g = new SurfaceGraph([Floor(), Far()]);
        Assert.Null(Lay(g, [0, 1], new Vec3(2, 5, 0), new Vec3(105, 5, 0)));
    }

    [Fact(DisplayName = "SKT-SF004 | A curve on a curved face stays on it, passes the clicked points and has no sharp turns")]
    [Trait("TestId", "SKT-SF004")]
    public void CurveOnACurvedFaceIsSmooth()
    {
        var g = new SurfaceGraph([Cylinder()]);
        var picks = new[] { new Vec3(10, 0, 3), new Vec3(0, 10, 9), new Vec3(-10, 0, 15) };
        var curve = Lay(g, [0, 0, 0], picks);
        Assert.NotNull(curve);
        var (points, pinned) = curve!.Value;
        Assert.Equal(3, pinned.Count(p => p));
        Assert.True(points.Count > 40, "the curve is sampled finely");
        double lowest = 10 * Math.Cos(Math.PI / 24) - 1e-6;      // a facet of the 24-sided cylinder lies between this radius and 10
        foreach (var p in points) Assert.InRange(Math.Sqrt(p.X * p.X + p.Y * p.Y), lowest, 10 + 1e-6);
        for (int i = 1; i + 1 < points.Count; i++)
        {
            var d1 = (points[i] - points[i - 1]).Normalized(); var d2 = (points[i + 1] - points[i]).Normalized();
            Assert.True(Vec3.Dot(d1, d2) > Math.Cos(40 * Math.PI / 180), $"sharp turn at sample {i}");
        }
    }

    [Fact(DisplayName = "SKT-SF005 | Moving a point changes the curve but keeps the other clicked points exactly")]
    [Trait("TestId", "SKT-SF005")]
    public void MovingAPointKeepsTheOthers()
    {
        var g = new SurfaceGraph([Cylinder()]);
        var before = Lay(g, [0, 0, 0], new Vec3(10, 0, 3), new Vec3(0, 10, 9), new Vec3(-10, 0, 15))!.Value;
        var after = Lay(g, [0, 0, 0], new Vec3(10, 0, 3), new Vec3(0, 10, 4), new Vec3(-10, 0, 15))!.Value;
        Assert.Equal(before.Points[0], after.Points[0]);
        Assert.Equal(before.Points[^1], after.Points[^1]);
        Assert.NotEqual(before.Points[before.Points.Count / 2], after.Points[after.Points.Count / 2]);
    }
}
