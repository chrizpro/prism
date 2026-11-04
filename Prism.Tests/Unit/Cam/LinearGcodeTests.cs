using Prism.Core.Cam;
using Xunit;

namespace Prism.UnitTests.Cam;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cam/README.md">CAM preview scope.</seealso>
[Trait("Feature", "CAM")]
[Trait("Layer", "Unit")]
public class LinearGcodeTests
{
    [Fact(DisplayName = "CAM-U001 | Inch, incremental and work reset moves resolve to millimeters")]
    [Trait("TestId", "CAM-U001")]
    public void ModalCoordinatesResolveToPhysicalPositions()
    {
        var preview = LinearGcode.Parse("G20\nG90\nG1 X1 F60\nG91\nG1 X.5 Y1\nG90\nG92 X0\nG1 X1");
        double[] moves = preview.Path.Moves;
        Assert.Equal(16, moves.Length);
        Assert.Equal(25.4, moves[4], 8);
        Assert.Equal(38.1, moves[8], 8);
        Assert.Equal(25.4, moves[9], 8);
        Assert.Equal(63.5, moves[12], 8);
        Assert.Equal(25.4, moves[13], 8);
        Assert.Equal(1524, preview.Path.Feeds![3], 8);
    }

    [Fact(DisplayName = "CAM-U002 | Relative extrusion marks printed moves and separates layers")]
    [Trait("TestId", "CAM-U002")]
    public void ExtrusionAndLayerChangesArePreserved()
    {
        var path = LinearGcode.Parse("G21\nG90\nM83\nG0 Z.2\nG1 X10 E1\nG1 X15 E-1\nG0 Z.4\nG1 X20 E1").Path;
        Assert.Equal((double)MoveKind.OuterWall, path.Moves[11]);
        Assert.Equal((double)MoveKind.Rapid, path.Moves[15]);
        Assert.Equal((double)MoveKind.OuterWall, path.Moves[23]);
        Assert.Equal(new[] { 0, 5 }, path.LayerStarts);
    }

    [Theory(DisplayName = "CAM-U003 | Unsupported or unsafe input is rejected with a diagnostic")]
    [InlineData("G2 X10 Y10", "unsupported G2")]
    [InlineData("G1 X10 F0", "feed must be positive")]
    [InlineData("G1 X1000001", "coordinate exceeds")]
    [InlineData("G1 X2\nG28", "homing after motion")]
    [InlineData("G1 X2 M104", "mixed motion")]
    [InlineData("T1", "multiple-nozzle")]
    [InlineData("G21\nG90", "No linear moves")]
    [Trait("TestId", "CAM-U003")]
    public void UnsafeOrUnsupportedInputIsRejected(string program, string diagnostic)
    {
        var error = Assert.Throws<InvalidOperationException>(() => LinearGcode.Parse(program));
        Assert.Contains(diagnostic, error.Message);
    }
}
