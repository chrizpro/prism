using Prism.Core;
using Xunit;

namespace Prism.UnitTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/interface/selection/README.md">Selection region behavior.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Unit")]
public class SelectionRegionTests
{
    [Fact(DisplayName = "SOL-U005 | Reversing a rectangle preserves its bounds and enables crossing")]
    [Trait("TestId", "SOL-U005")]
    public void ReverseRectangleChangesCrossingOnly()
    {
        var containment = RegionSelect.Region.Box(1, 2, 8, 9);
        var crossing = RegionSelect.Region.Box(8, 2, 1, 9);
        Assert.False(containment.Crossing);
        Assert.True(crossing.Crossing);
        Assert.Equal(containment.Points, crossing.Points);
        Assert.True(crossing.Contains(1, 2));
        Assert.False(crossing.Contains(0, 2));
    }

    [Fact(DisplayName = "SOL-U006 | A concave lasso excludes its notch")]
    [Trait("TestId", "SOL-U006")]
    public void LassoRespectsConcaveBoundary()
    {
        var region = new RegionSelect.Region
        {
            Points = { (0, 0), (8, 0), (8, 3), (3, 3), (3, 8), (0, 8) },
        };
        Assert.True(region.Contains(1, 6));
        Assert.True(region.Contains(6, 1));
        Assert.False(region.Contains(6, 6));
    }
}
