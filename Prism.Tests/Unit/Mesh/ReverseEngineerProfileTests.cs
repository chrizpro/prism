using Prism.Meshing;
using Xunit;

namespace Prism.UnitTests.Mesh;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Scan section fitting.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Unit")]
public class ReverseEngineerProfileTests
{
    [Fact(DisplayName = "REV-U001 | Sparse rectangular sections retain straight sides instead of becoming a corner-fitted arc")]
    [Trait("TestId", "REV-U001")]
    public void SparseRectangleStaysRectangular()
    {
        var outline = new List<(double x, double y)> { (2, 0), (5, 0), (5, 8), (2, 8) };
        var fitted = LineArcFit.Fit(outline, true, .01);
        Assert.Equal(4, fitted.Count);
        Assert.All(fitted, segment => Assert.False(segment.IsArc));
        var midpoints = outline.Select((p, i) => ((p.x + outline[(i + 1) % 4].x) * .5, (p.y + outline[(i + 1) % 4].y) * .5)).ToList();
        Assert.InRange(LineArcFit.MaxDeviation(midpoints, fitted), 0, .01);
    }

    [Fact(DisplayName = "REV-U002 | Dense circular sections still recover the correct circle after checking between samples")]
    [Trait("TestId", "REV-U002")]
    public void CircularSectionKeepsItsRadius()
    {
        var outline = Enumerable.Range(0, 180).Select(i => (x: 5 * Math.Cos(i * Math.PI / 90), y: 5 * Math.Sin(i * Math.PI / 90))).ToList();
        var fitted = LineArcFit.Fit(outline, true, .01);
        Assert.Equal(2, fitted.Count);
        Assert.All(fitted, segment => { Assert.True(segment.IsArc); Assert.Equal(5, segment.Radius, 6); });
        Assert.Equal(360, fitted.Sum(segment => Math.Abs(segment.SweepDeg)), 6);
    }
}
