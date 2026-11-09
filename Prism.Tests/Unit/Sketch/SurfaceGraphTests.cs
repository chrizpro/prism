using Prism.Core;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Sketch;

/// <summary>Shortest paths over welded triangle surfaces, the engine behind Draw on Surface. Needs no kernel: the meshes are written by hand.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sketches/README.md#drawing-directly-on-a-surface">Drawing directly on a surface.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Unit")]
public class SurfaceGraphTests
{
    // The floor (z = 0, x 0..10) and the wall (x = 10, z 0..10) of a box corner, as two separate parts that share the edge x = 10, z = 0.
    static (float[], int[]) Floor() => ([0, 0, 0, 10, 0, 0, 10, 10, 0, 0, 10, 0], [0, 1, 2, 0, 2, 3]);
    static (float[], int[]) Wall() => ([10, 0, 0, 10, 10, 0, 10, 10, 10, 10, 0, 10], [0, 1, 2, 0, 2, 3]);
    static (float[], int[]) Far() => ([100, 0, 0, 110, 0, 0, 110, 10, 0, 100, 10, 0], [0, 1, 2, 0, 2, 3]);

    static List<Vec3>? Path(SurfaceGraph g, Vec3 a, int pa, Vec3 b, int pb)
    {
        int ta = g.FindTriangle(a, pa, out var sa), tb = g.FindTriangle(b, pb, out var sb);
        return g.Geodesic(sa, ta, sb, tb);
    }

    [Fact(DisplayName = "SKT-SF001 | A path over the edge between two faces is the straight line of the unfolded faces")]
    [Trait("TestId", "SKT-SF001")]
    public void PathCrossesTheSharedEdgeOfTwoFaces()
    {
        var g = new SurfaceGraph([Floor(), Wall()]);
        var path = Path(g, new Vec3(2, 5, 0), 0, new Vec3(10, 5, 6), 1);
        Assert.NotNull(path);
        // unfolded, the wall continues the floor: 8 along the floor plus 6 up the wall
        double length = SurfaceGraph.Length(path!);
        Assert.InRange(length, 13.999, 14.5);
        foreach (var p in path!)
            Assert.True(Math.Abs(p.Z) < 1e-6 && p.X <= 10 + 1e-6 || Math.Abs(p.X - 10) < 1e-6 && p.Z >= -1e-6, $"{p} left the surface");
        Assert.Contains(path, p => Math.Abs(p.X - 10) < 1e-6 && Math.Abs(p.Z) < 1e-6);   // it really crossed the shared edge
    }

    [Fact(DisplayName = "SKT-SF002 | Surfaces that do not touch are not joined: a path never crosses empty space")]
    [Trait("TestId", "SKT-SF002")]
    public void SeparateSurfacesAreNotJoined()
    {
        var g = new SurfaceGraph([Floor(), Far()]);
        Assert.Null(Path(g, new Vec3(2, 5, 0), 0, new Vec3(105, 5, 0), 1));
    }

    [Fact(DisplayName = "SKT-SF003 | Two points inside one triangle are joined by a straight line")]
    [Trait("TestId", "SKT-SF003")]
    public void SameTriangleIsStraight()
    {
        var g = new SurfaceGraph([Floor()]);
        var path = Path(g, new Vec3(8, 1, 0), 0, new Vec3(9, 2, 0), 0);
        Assert.NotNull(path);
        Assert.Equal(2, path!.Count);
        Assert.InRange(SurfaceGraph.Length(path), Math.Sqrt(2) - 1e-9, Math.Sqrt(2) + 1e-9);
    }

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

    [Fact(DisplayName = "SKT-SF005 | A smoothed path keeps its end points exactly and stays on the surface")]
    [Trait("TestId", "SKT-SF005")]
    public void SmoothedPathStaysOnTheSurface()
    {
        var g = new SurfaceGraph([Cylinder()]);
        var a = new Vec3(10, 0, 3); var b = new Vec3(0, 10, 15);
        int ta = g.FindTriangle(a, 0, out var sa), tb = g.FindTriangle(b, 0, out var sb);
        var leg = g.Geodesic(sa, ta, sb, tb);
        Assert.NotNull(leg);
        var (points, pinned) = g.SmoothPath([leg!], [ta], SurfaceGraph.Length(leg!) / 40);
        Assert.True(points.Count > leg!.Count, "the path is resampled finely");
        Assert.Equal(points.Count, pinned.Count);
        Assert.Equal(sa, points[0]); Assert.Equal(sb, points[^1]);
        double lowest = 10 * Math.Cos(Math.PI / 24) - 1e-6;      // a facet of the 24-sided cylinder lies between this radius and 10
        foreach (var p in points) Assert.InRange(Math.Sqrt(p.X * p.X + p.Y * p.Y), lowest, 10 + 1e-6);
    }

    [Fact(DisplayName = "SKT-SF006 | Smoothing across several legs keeps every clicked point exactly")]
    [Trait("TestId", "SKT-SF006")]
    public void ClickedPointsAreKept()
    {
        var g = new SurfaceGraph([Cylinder()]);
        var spots = new[] { new Vec3(10, 0, 3), new Vec3(0, 10, 9), new Vec3(-10, 0, 15) };
        var tris = spots.Select(s => g.FindTriangle(s, 0, out _)).ToArray();
        var on = spots.Select(s => { g.FindTriangle(s, 0, out var q); return q; }).ToArray();
        var legs = new List<IReadOnlyList<Vec3>> { g.Geodesic(on[0], tris[0], on[1], tris[1])!, g.Geodesic(on[1], tris[1], on[2], tris[2])! };
        var (points, pinned) = g.SmoothPath(legs, [tris[0], tris[1]], 1.0);
        var fixedPoints = points.Where((_, i) => pinned[i]).ToList();
        Assert.Equal(3, fixedPoints.Count);
        for (int i = 0; i < 3; i++) Assert.Equal(on[i], fixedPoints[i]);
    }

    [Fact(DisplayName = "SKT-SF004 | A point off the surface is moved onto the nearest triangle of the chosen part")]
    [Trait("TestId", "SKT-SF004")]
    public void PointsAreMovedOntoTheSurface()
    {
        var g = new SurfaceGraph([Floor(), Wall()]);
        int tri = g.FindTriangle(new Vec3(4, 3, 7), 0, out var on);
        Assert.True(tri >= 0);
        Assert.Equal(0, on.Z, 6);
        Assert.Equal(4, on.X, 6);
        Assert.Equal(3, on.Y, 6);
    }
}
