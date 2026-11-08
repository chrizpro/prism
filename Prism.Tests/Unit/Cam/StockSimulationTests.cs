using Prism.Core.Cam;
using Prism.Meshing;
using Xunit;

namespace Prism.UnitTests.Cam;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cam/README.md">CAM stock review.</seealso>
[Trait("Feature", "CAM")]
[Trait("Layer", "Unit")]
public class StockSimulationTests
{
    static TriMesh Box(float x, float y, float z)
    {
        float[] v = [0, 0, 0, x, 0, 0, x, y, 0, 0, y, 0, 0, 0, z, x, 0, z, x, y, z, 0, y, z];
        int[] t = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7];
        return new TriMesh(v, t);
    }

    [Fact(DisplayName = "CAM-U003 | Drilling through into the spoilboard and retracting is not reported as a rapid contact")]
    [Trait("TestId", "CAM-U003")]
    public void RetractingFromAThroughHoleIsNotACollision()
    {
        var setup = new CamSetup("test", Guid.NewGuid(), "", [0, 0, 0], [40, 30, 10], [0, 0, 10]);
        // (the solid test box has no hole, so the plunge counts as a gouge; only the rapid retract is checked here)
        // plunge 0.5 mm below the stock bottom, then retract at rapid speed
        double R = (double)MoveKind.Rapid, C = (double)MoveKind.Cut, P = (double)MoveKind.Plunge;
        double[] moves = [20, 15, 20, R, 20, 15, 11, R, 20, 15, -.5, P, 20, 15, 20, R];
        var drill = new ToolpathInfo("Drill", "5 mm end mill", 5, 500, 200, 12000, 20, moves, [0], false, false);
        var result = StockSimulation.Run(setup, Box(40, 30, 10), [drill], 0.5, 25, 20, 0.1);
        Assert.Equal(0, result.RapidContacts);
        Assert.True(result.RemovedVolume > 0.9 * Math.PI * 2.5 * 2.5 * 10);
    }
}
