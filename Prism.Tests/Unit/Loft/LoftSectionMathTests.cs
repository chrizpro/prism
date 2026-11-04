using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Loft;

/// <summary>Supporting managed interpolation/projection math, not a substitute for the Loft command.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/tabs/loft/README.md">Living Loft specification.</seealso>
[Trait("Layer", "Unit")]
[Trait("Feature", "Loft")]
public class LoftSectionMathTests
{
    [Fact(DisplayName = "LFT-U001 | Corresponding section points interpolate to their geometric midpoint")]
    [Trait("TestId", "LFT-U001")]
    public void SectionPoints_InterpolatedHalfway_ReturnTheirMidpoint()
    {
        var midpoint = Vec3.Lerp(new Vec3(0, 0, 0), new Vec3(10, 6, 12), 0.5);

        Assert.Equal(new Vec3(5, 3, 6), midpoint);
    }

    [Fact(DisplayName = "LFT-U002 | An offset section projects back into its own profile coordinates")]
    [Trait("TestId", "LFT-U002")]
    public void OffsetSectionPlane_ProjectsWorldPoint_ToLocalCoordinates()
    {
        var upperSection = new Plane3(new Vec3(2, 4, 12), Vec3.UnitX, Vec3.UnitY);

        Assert.Equal((3d, 2d), upperSection.Project(new Vec3(5, 6, 12)));
    }
}
