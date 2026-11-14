using Prism.Meshing;
using Xunit;

namespace Prism.UnitTests.Mesh;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Scoped mesh repair.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Unit")]
public class MeshRegionRepairTests
{
    [Fact(DisplayName = "MSH-U001 | Scoped welding joins selected split vertices without changing shared anchors")]
    [Trait("TestId", "MSH-U001")]
    public void WeldingPreservesUnselectedTriangle()
    {
        var mesh = new TriMesh([0,0,0, 10,0,0, 0,10,0, .0005f,0,0, 10,10,0, 0,10.0005f,0],
            [0,1,2, 3,1,2, 0,4,5]);
        var original = (int[])mesh.T.Clone();
        var result = MeshRegionRepair.Prepare(mesh, new HashSet<int> { 1, 2 }, new(true, .001, false, false, 0, false));
        Assert.Equal(new[] { 0, 1, 2 }, result.Mesh.T[..3]);
        Assert.Equal(0, result.Mesh.T[3]);
        Assert.Equal(2, result.Mesh.T[8]);
        Assert.Equal(mesh.V, result.Mesh.V);
        Assert.Equal(original, mesh.T);
    }

    [Fact(DisplayName = "MSH-U002 | Scoped cleanup removes selected junk but keeps unselected duplicates and degenerate faces")]
    [Trait("TestId", "MSH-U002")]
    public void CleanupPreservesOutsideDefectsAndColors()
    {
        var mesh = new TriMesh([0,0,0, 10,0,0, 0,10,0], [0,1,2, 0,1,2, 0,0,1, 1,1,2],
            faceColors: [1u, 2u, 3u, 4u]);
        var result = MeshRegionRepair.Prepare(mesh, new HashSet<int> { 0, 2 }, new(false, 0, true, false, 0, false));
        Assert.Equal(new[] { 0,1,2, 1,1,2 }, result.Mesh.T);
        Assert.Equal(new[] { 2u, 4u }, result.Mesh.FaceColors);
        Assert.Empty(result.Selected);
    }

    [Fact(DisplayName = "MSH-U003 | Scoped shell removal deletes only wholly selected small shells")]
    [Trait("TestId", "MSH-U003")]
    public void PartlySelectedShellIsProtected()
    {
        var mesh = new TriMesh([0,0,0, 1,0,0, 0,1,0, 3,0,0, 4,0,0, 3,1,0, 6,0,0, 7,0,0, 6,1,0, 7,1,0],
            [0,1,2, 3,4,5, 6,7,8, 7,9,8]);
        var result = MeshRegionRepair.Prepare(mesh, new HashSet<int> { 0, 2 }, new(false, 0, false, true, 5, false));
        Assert.Equal(mesh.T[3..], result.Mesh.T);
        Assert.Equal(new[] { 1 }, result.Selected);
    }

    [Fact(DisplayName = "MSH-U004 | Scoped normals follow the unselected neighboring face without flipping it")]
    [Trait("TestId", "MSH-U004")]
    public void OrientationUsesOutsideFaceAsAnchor()
    {
        var mesh = new TriMesh([0,0,0, 1,0,0, 1,1,0, 0,1,0], [0,1,2, 0,3,2]);
        var original = (int[])mesh.T.Clone();
        var result = MeshRegionRepair.Prepare(mesh, new HashSet<int> { 1 }, new(false, 0, false, false, 0, true));
        Assert.Equal(new[] { 0,1,2, 0,2,3 }, result.Mesh.T);
        Assert.Equal(original, mesh.T);
    }
}
