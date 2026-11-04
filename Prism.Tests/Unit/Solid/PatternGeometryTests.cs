using Prism.Commands;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/patterns/README.md">Living pattern guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Unit")]
public class PatternGeometryTests
{
    [Fact(DisplayName = "SOL-U001 | Arc-length sampling skips a repeated vertex and follows the next leg")]
    [Trait("TestId", "SOL-U001")]
    public void SampleFollowsArcLengthAcrossCorner()
    {
        Vec3[] path = [Vec3.Zero, new(3, 0, 0), new(3, 0, 0), new(3, 4, 0)];
        Assert.Equal(7, PatternGeometry.Length(path));
        var (point, tangent) = PatternGeometry.Sample(path, 5);
        Assert.Equal(new Vec3(3, 2, 0), point);
        Assert.Equal(Vec3.UnitY, tangent);
    }

    [Fact(DisplayName = "SOL-U002 | A reversed path tangent gets a valid half-turn around its pivot")]
    [Trait("TestId", "SOL-U002")]
    public void AlignHandlesOppositeDirections()
    {
        var pivot = new Vec3(2, 3, 4);
        var rotation = PatternGeometry.Align(pivot, Vec3.UnitX, -Vec3.UnitX);
        Assert.Equal(180, rotation.Deg);
        var point = PatternGeometry.RotatePoint(pivot + Vec3.UnitX, rotation.Pivot, rotation.Axis, rotation.Deg);
        Assert.InRange(point.DistanceTo(pivot - Vec3.UnitX), 0, 1e-12);
    }

    [Fact(DisplayName = "SOL-U003 | A hole excludes fill centers and its edge counts toward the margin")]
    [Trait("TestId", "SOL-U003")]
    public void FillRegionRespectsInnerLoop()
    {
        List<(double u, double v)>[] loops =
        [
            [(0, 0), (10, 0), (10, 10), (0, 10)],
            [(4, 4), (6, 4), (6, 6), (4, 6)],
        ];
        Assert.False(PatternGeometry.Inside(loops, 5, 5));
        Assert.True(PatternGeometry.Inside(loops, 3, 5));
        Assert.Equal(1, PatternGeometry.DistanceToLoops(loops, 3, 5));
    }
}
