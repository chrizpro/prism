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
