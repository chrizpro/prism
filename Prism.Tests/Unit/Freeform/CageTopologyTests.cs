using Prism.Core.Form;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Freeform;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/freeform/README.md">Freeform cage topology.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Unit")]
public class CageTopologyTests
{
    [Theory(DisplayName = "FRM-U001 | Plane segments determine grid size without closing the cage")]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(4, 2)]
    [Trait("TestId", "FRM-U001")]
    public void PlaneSegmentsDetermineTopology(int columns, int rows)
    {
        var cage = FormOps.Plane(10, 6, columns, rows);
        Assert.Equal((columns + 1) * (rows + 1), cage.V.Count);
        Assert.Equal(columns * rows, cage.F.Count);
        Assert.False(cage.IsClosed());
        var (min, max) = cage.Bounds();
        Assert.Equal(new Vec3(10, 6, 0), max - min);
    }

    [Theory(DisplayName = "FRM-U002 | Cylinder caps add two faces and close the cage")]
    [InlineData(true, 18)]
    [InlineData(false, 16)]
    [Trait("TestId", "FRM-U002")]
    public void CylinderCapOptionControlsClosure(bool caps, int faces)
    {
        var cage = FormOps.Cylinder(3, 10, 8, 2, caps);
        Assert.Equal(faces, cage.F.Count);
        Assert.Equal(caps, cage.IsClosed());
    }
}
