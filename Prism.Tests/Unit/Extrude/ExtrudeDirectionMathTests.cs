using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Extrude;

/// <summary>Managed plane/vector math used to position extrusion profiles. No OCCT or Avalonia.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/tabs/solid/extrude.md">Living Solid Extrude specification.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/tabs/surface-and-nurbs/extrude.md">Living Surface/NURBS Extrude specification; shared math does not verify those workflows.</seealso>
[Trait("Layer", "Unit")]
[Trait("Feature", "Extrude")]
public class ExtrudeDirectionMathTests
{
    [Fact(DisplayName = "EXT-U001 | XY profile normal points along positive Z")]
    [Trait("TestId", "EXT-U001")]
    public void XyProfile_Normal_PointsAlongPositiveZ()
    {
        Assert.Equal(Vec3.UnitZ, Plane3.XY.Normal);
    }

    [Fact(DisplayName = "EXT-U002 | Scaled plane axes still produce a unit extrusion normal")]
    [Trait("TestId", "EXT-U002")]
    public void ScaledPlaneAxes_Normal_HasUnitLength()
    {
        var plane = new Plane3(Vec3.Zero, new Vec3(4, 0, 0), new Vec3(0, 3, 0));

        Assert.Equal(Vec3.UnitZ, plane.Normal);
        Assert.Equal(1, plane.Normal.Length);
    }

    [Fact(DisplayName = "EXT-U003 | Reversing the plane winding reverses its extrusion normal")]
    [Trait("TestId", "EXT-U003")]
    public void ReversedPlaneWinding_Normal_PointsAlongNegativeZ()
    {
        var plane = new Plane3(Vec3.Zero, Vec3.UnitY, Vec3.UnitX);

        Assert.Equal(-Vec3.UnitZ, plane.Normal);
    }

    [Fact(DisplayName = "EXT-U004 | Offset XZ profile coordinates map into world space")]
    [Trait("TestId", "EXT-U004")]
    public void OffsetXzPlane_ProfilePoint_MapsToWorldCoordinates()
    {
        var plane = new Plane3(new Vec3(10, 20, 30), Vec3.UnitX, Vec3.UnitZ);

        var worldPoint = plane.PointAt(u: 2, v: 8);

        Assert.Equal(new Vec3(12, 20, 38), worldPoint);
        Assert.Equal((2d, 8d), plane.Project(worldPoint));
    }
}
