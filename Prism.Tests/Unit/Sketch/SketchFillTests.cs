using Prism.Core.Sketching;
using Xunit;
using P = Prism.Core.Sketching.SketchFill.P2;

namespace Prism.UnitTests.Sketch;

/// <summary>The tint over closed profiles: overlapping shapes are each filled, and only a shape wholly inside another is a hole.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sketches/README.md">Sketches.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Unit")]
public class SketchFillTests
{
    static List<P> Square(double x, double y, double size) => [new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)];

    static double Area(List<(P a, P b, P c)> tris) => tris.Sum(t => Math.Abs((t.b.X - t.a.X) * (t.c.Y - t.a.Y) - (t.c.X - t.a.X) * (t.b.Y - t.a.Y)) / 2);

    [Fact(DisplayName = "SKT-H060 | Two overlapping squares are both filled whole, not bridged into each other")]
    [Trait("TestId", "SKT-H060")]
    public void OverlappingSquaresAreBothFilled()
    {
        var tris = SketchFill.Triangulate([Square(0, 0, 10), Square(6, 4, 10)]);
        Assert.Equal(200, Area(tris), 6);       // 100 + 100, each square once
    }

    [Fact(DisplayName = "SKT-H061 | A square wholly inside another is a hole")]
    [Trait("TestId", "SKT-H061")]
    public void NestedSquareIsAHole()
    {
        var tris = SketchFill.Triangulate([Square(0, 0, 10), Square(3, 3, 4)]);
        Assert.Equal(100 - 16, Area(tris), 6);
    }
}
