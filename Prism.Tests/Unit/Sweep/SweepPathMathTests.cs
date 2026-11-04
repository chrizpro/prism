using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Sweep;

/// <summary>Production vector/plane operations used when reasoning about sweep paths.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/tabs/sweep/README.md">Living Sweep specification.</seealso>
[Trait("Layer", "Unit")]
[Trait("Feature", "Sweep")]
public class SweepPathMathTests
{
    [Fact(DisplayName = "SWP-U001 | An oblique path tangent normalizes without changing direction")]
    [Trait("TestId", "SWP-U001")]
    public void ObliquePathVector_Normalized_HasExpectedUnitComponents()
    {
        var tangent = new Vec3(0, 3, 4).Normalized();

        Assert.Equal(new Vec3(0, 0.6, 0.8), tangent);
        Assert.Equal(1, tangent.Length, precision: 12);
    }

    [Fact(DisplayName = "SWP-U002 | A perpendicular path ray intersects the profile plane at the expected point")]
    [Trait("TestId", "SWP-U002")]
    public void PathRay_IntersectsXyProfilePlane_AtExpectedLocation()
    {
        var intersection = Plane3.XY.Intersect(new Vec3(5, 3, 12), -Vec3.UnitZ);

        Assert.Equal(new Vec3(5, 3, 0), intersection);
    }
}
