using Prism.Simulation;
using Xunit;

namespace Prism.UnitTests.Fluids;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cae/fluids/README.md">Pipe-network solver.</seealso>
[Trait("Feature", "Fluids")]
[Trait("Layer", "Unit")]
public class PipeNetworkTests
{
    [Theory(DisplayName = "FLD-U001 | A straight laminar pipe matches Hagen–Poiseuille in both directions")]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [Trait("TestId", "FLD-U001")]
    public void LaminarFlowMatchesPressureGradient(int inlet, int outlet)
    {
        const double diameter = 0.01, length = 2, viscosity = 0.01;
        var result = PipeNetwork.Solve([new("A", "B", length, diameter)], [], new(1000, viscosity),
            new Dictionary<string, double> { ["A"] = inlet, ["B"] = outlet });
        var pipe = Assert.Single(result.Pipes);
        double expected = Math.PI * Math.Pow(diameter, 4) * (inlet - outlet) / (128 * viscosity * length);
        Assert.InRange(Math.Abs(pipe.Flow - expected), 0, 1e-10);
        Assert.InRange(pipe.Reynolds, 0, 2000);
    }

    [Fact(DisplayName = "FLD-U002 | A disconnected branch needs its own known pressure")]
    [Trait("TestId", "FLD-U002")]
    public void DisconnectedBranchIsRejected()
    {
        var error = Assert.Throws<ArgumentException>(() => PipeNetwork.Solve(
            [new("A", "B", 2, 0.01), new("C", "D", 2, 0.01)], [], new(1000, 0.01),
            new Dictionary<string, double> { ["A"] = 100 }));
        Assert.Contains("not connected", error.Message);
    }
}
