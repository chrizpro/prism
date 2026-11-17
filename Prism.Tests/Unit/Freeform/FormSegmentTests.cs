using Prism.Core.Form;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Freeform;

/// <summary>More segment controls on Freeform primitives (owner request 2026-11-16): divided cylinder caps and a sphere with its own count in each direction.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/freeform/README.md">Freeform guide.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Unit")]
public class FormSegmentTests
{
    static double Volume(SubdMesh m)
    {
        double sum = 0;
        foreach (var f in m.F)
            for (int i = 1; i + 1 < f.Length; i++) sum += Vec3.Dot(m.V[f[0]], Vec3.Cross(m.V[f[i]], m.V[f[i + 1]])) / 6;
        return sum;
    }

    static int Euler(SubdMesh m) => m.V.Count - m.Edges().Count() + m.F.Count;

    [Theory(DisplayName = "FRM-U060 | Dividing the cylinder caps adds the expected rings of faces, stays a closed solid cage and keeps the volume")]
    [InlineData(8, 3, 0, 8 * 3 + 2)]                     // as before: the sides plus one face per cap
    [InlineData(8, 3, 1, 8 * 3 + 2 * (8 + 1))]           // one ring of 8 quads and a middle face, on each cap
    [InlineData(8, 3, 2, 8 * 3 + 2 * (2 * 8 + 1))]
    [InlineData(12, 2, 3, 12 * 2 + 2 * (3 * 12 + 1))]
    [Trait("TestId", "FRM-U060")]
    public void CapRings(int sides, int rings, int capRings, int expectedFaces)
    {
        var m = FormOps.Cylinder(25, 60, sides, rings, true, capRings);
        Assert.Equal(expectedFaces, m.F.Count);
        Assert.True(m.IsClosed(), "the cage must stay closed");
        Assert.Equal(2, Euler(m));                          // a closed cage in one piece with no handles
        Assert.Equal(m.V.Count, m.F.SelectMany(f => f).Distinct().Count());
        double polygon = 0.5 * sides * 25 * 25 * Math.Sin(2 * Math.PI / sides);          // area of the regular polygon, times the height
        Assert.Equal(polygon * 60, Volume(m), 6);           // positive: every face runs counter-clockwise seen from outside, including the divided caps
        Assert.Equal(Volume(FormOps.Cylinder(25, 60, sides, rings, true, 0)), Volume(m), 6);
    }

    [Fact(DisplayName = "FRM-U061 | Cap rings with no caps, or zero rings, leave the cylinder as it was")]
    [Trait("TestId", "FRM-U061")]
    public void CapRingsAreOptional()
    {
        var plain = FormOps.Cylinder(25, 60, 8, 3, true);
        var same = FormOps.Cylinder(25, 60, 8, 3, true, 0);
        Assert.Equal(plain.F.Count, same.F.Count); Assert.Equal(plain.V.Count, same.V.Count);
        Assert.Equal(8 * 3, FormOps.Cylinder(25, 60, 8, 3, false, 4).F.Count);               // no caps: nothing to divide
    }

    [Theory(DisplayName = "FRM-U062 | A sphere with its own count in each direction has 2(xy + yz + zx) faces, is closed and lies on the sphere")]
    [InlineData(3, 3, 3)]
    [InlineData(8, 4, 2)]
    [InlineData(1, 6, 10)]
    [Trait("TestId", "FRM-U062")]
    public void SphereWithOwnCounts(int nx, int ny, int nz)
    {
        var m = FormOps.Sphere(30, nx, ny, nz);
        Assert.Equal(2 * (nx * ny + ny * nz + nz * nx), m.F.Count);
        Assert.True(m.IsClosed());
        Assert.Equal(2, Euler(m));
        Assert.All(m.V, p => Assert.Equal(30, p.Length, 6));
        Assert.True(Volume(m) > 0);
        Assert.Equal(FormOps.Sphere(30, 3).F.Count, FormOps.Sphere(30, 3, 3, 3).F.Count);    // the single count is the same as three equal ones
    }
}
