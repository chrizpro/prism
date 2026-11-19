using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using Edge = Prism.Meshing.MeshLocalEdit.Edge;
using Corner = Prism.Meshing.MeshLocalEdit.Corner;

namespace Prism.UnitTests.Mesh;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Local mesh editing guide.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Unit")]
public sealed class MeshLocalEditTests
{
    [Fact(DisplayName = "MSH-U005 | Drawing to an edge midpoint splits the retained rim and preserves a second opening")]
    [Trait("TestId", "MSH-U005")]
    public void SplitsSnappedRimWithoutClosingOtherOpening()
    {
        var open = new TriMesh(
            [0,0,0, 10,0,0, 10,10,0, 0,10,0, 0,0,10, 10,0,10, 10,10,10, 0,10,10],
            [0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7],
            faceColors: Enumerable.Repeat(0xFF123456u, 8).ToArray());
        Corner V(int i) => new(open.P(i), i);
        var midpoint = new Corner(new Vec3(5, 0, 10), 4, 5, .5);
        var result = MeshLocalEdit.Draw(open, null, MeshLocalEdit.Boundary(open),
            [[V(4), midpoint, V(7)], [midpoint, V(6), V(7)], [midpoint, V(5), V(6)]]);
        Assert.Null(result.Error);
        Assert.Equal(5, result.Stitched.Length);
        Assert.Equal(9, result.FirstNewTriangle);
        Assert.Equal(12, result.Mesh.TriCount);
        Assert.All(result.Mesh.FaceColors!.Take(9), c => Assert.Equal(0xFF123456u, c));
        var rim = Assert.Single(result.Mesh.Topology().HoleLoops);
        Assert.All(rim, v => Assert.Equal(0, result.Mesh.P(v).Z));
        Assert.Equal(0, result.Mesh.Topology().NonManifoldEdges);
    }

    [Fact(DisplayName = "MSH-U006 | A selected region cannot be replaced while its cut boundary is open")]
    [Trait("TestId", "MSH-U006")]
    public void IncompleteReplacementStaysInDraft()
    {
        var mesh = new TriMesh([0,0,0, 1,0,0, 1,1,0, 0,1,0, .5f,.5f,0, 0,-1,0],
            [0,1,4, 1,2,4, 2,3,4, 3,0,4, 1,0,5]);
        var region = new HashSet<int> { 0, 1 };
        Corner V(int i) => new(mesh.P(i), i);
        var draft = MeshLocalEdit.Draw(mesh, region, MeshLocalEdit.Boundary(mesh, region),
            [[V(0), V(1), V(4)]]);
        Assert.Contains("entire cut boundary", draft.Error);
        Assert.True(draft.Remaining > 0);
    }

    [Fact(DisplayName = "MSH-U008 | Replacing two faces keeps a sharp corner and leaves the intentional rim")]
    [Trait("TestId", "MSH-U008")]
    public void ReplacesTwoFacesAcrossSharpCorner()
    {
        var mesh = new TriMesh(
            [0,0,0, 10,0,0, 10,10,0, 0,10,0, 0,0,10, 10,0,10, 10,10,10, 0,10,10],
            [4,5,6, 4,6,7, 0,1,5, 0,5,4, 2,3,7, 2,7,6, 3,0,4, 3,4,7, 1,2,6, 1,6,5]);
        var removed = new HashSet<int> { 0, 1, 8, 9 };
        Corner V(int i) => new(mesh.P(i), i);
        var result = MeshLocalEdit.Draw(mesh, removed, MeshLocalEdit.Boundary(mesh, removed),
            [[V(4), V(5), V(6), V(7)], [V(1), V(2), V(6), V(5)]]);
        Assert.Null(result.Error);
        Assert.Equal(0, result.Remaining);
        Assert.Equal(mesh.T[6..24], result.Mesh.T[..18]);
        Assert.Equal(2, Enumerable.Range(result.FirstNewTriangle, 4).Count(i => result.Mesh.FaceNormal(i).Z > .99));
        Assert.Equal(2, Enumerable.Range(result.FirstNewTriangle, 4).Count(i => result.Mesh.FaceNormal(i).X > .99));
        var rim = Assert.Single(result.Mesh.Topology().HoleLoops);
        Assert.All(rim, i => Assert.Equal(0, result.Mesh.P(i).Z));
    }

    [Fact(DisplayName = "MSH-U007 | Stitch moves only the chosen rim endpoints and honors the maximum gap")]
    [Trait("TestId", "MSH-U007")]
    public void StitchWeldsExplicitPair()
    {
        var mesh = new TriMesh([0,0,0, 1,0,0, 1,1,0, 0,1,0, 1.02f,0,0, 2,0,0, 2,1,0, 1.02f,1,0],
            [0,1,2, 0,2,3, 4,5,6, 4,6,7]);
        var left = new Edge(1, 2);
        var right = new Edge(7, 4);
        Assert.Throws<InvalidOperationException>(() => MeshLocalEdit.Stitch(mesh, left, right, .01));
        var joined = MeshLocalEdit.Stitch(mesh, left, right, .03);
        Assert.Equal(4, joined.TriCount);
        Assert.Equal(6, joined.VertexCount);
        Assert.Equal(6, joined.Topology().BoundaryEdges);
        Assert.Equal(0, joined.Topology().InconsistentEdges);
        Assert.Equal(2, joined.Max.X);
        Assert.Equal(1, joined.Max.Y);
    }
}
