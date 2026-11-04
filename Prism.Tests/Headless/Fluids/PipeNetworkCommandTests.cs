using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Fluids;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cae/fluids/README.md">Pipe-network input units and analysis.</seealso>
[Trait("Feature", "Fluids")]
[Trait("Layer", "Headless")]
public class PipeNetworkCommandTests
{
    [AvaloniaTheory(DisplayName = "FLD-H001 | Typed mm and kPa inputs solve in SI or reject malformed rows")]
    [InlineData("A, B, 2000, 10", true)]
    [InlineData("A, B, broken, 10", false)]
    [Trait("TestId", "FLD-H001")]
    public void CommandConvertsUnitsAndPreservesDocument(string pipes, bool valid)
    {
        var session = new CadSession();
        var history = session.Current;
        session.StartCommand(() => new PipeNetworkCommand());
        var command = Assert.IsType<PipeNetworkCommand>(session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            command.Params.OfType<TextParam>().Single(p => p.Label == "Pipes").Value = pipes;
            command.Params.OfType<TextParam>().Single(p => p.Label == "Known pressures").Value = "A=0.1; B=0";
            SetNumber(command, "Fluid density", 1000);
            SetNumber(command, "Dynamic viscosity", 0.01);
            SetNumber(command, "Absolute roughness", 0);
        });
        var output = command.TryBuild();
        if (valid)
        {
            Assert.True(output?.NoOp);
            Assert.NotNull(command.Result);
            var pipe = Assert.Single(command.Result.Pipes);
            Assert.Equal(2, pipe.Pipe.Length);
            Assert.Equal(0.01, pipe.Pipe.Diameter);
            Assert.Equal(100, command.Result.Pressure["A"], 8);
            double expected = Math.PI * Math.Pow(0.01, 4) * 100 / (128 * 0.01 * 2);
            Assert.InRange(Math.Abs(pipe.Flow - expected), 0, 1e-10);
        }
        else
        {
            Assert.True(output?.NoOp);
            Assert.Null(command.Result);
            Assert.Contains(command.Results, row => row.Label == "Setup" && row.Value.Contains("not a number"));
        }
        Assert.Empty(session.Objects);
        Assert.Same(history, session.Current);
    }
}
