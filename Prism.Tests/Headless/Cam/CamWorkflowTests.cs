using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Cam;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cam/README.md">CAM setup and nozzle preview.</seealso>
[Trait("Feature", "CAM")]
[Trait("Layer", "Headless")]
public class CamWorkflowTests
{
    [AvaloniaTheory(DisplayName = "CAM-H001 | Setup allowances and G54 choice reach the committed setup")]
    [InlineData("Stock lower-left, top", 8, 18, 39)]
    [InlineData("Model origin", 0, 0, 0)]
    [Trait("TestId", "CAM-H001")]
    public void SetupCommitsStockAndWorkZero(string mode, int x, int y, int z)
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new(10, 20, 30), 10, 8, 6);
        var command = StartCommand(session, body, () => new CamSetupCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Model body", body);
            SetNumber(command, "Stock allowance XY", 2);
            SetNumber(command, "Top allowance", 3);
            SetNumber(command, "Bottom allowance", 1);
            SetChoice(command, "Work zero (G54)", mode);
        });
        session.CommitCommand();
        var setup = session.CamSetup;
        Assert.NotNull(setup);
        Assert.Equal(body.Id, setup.BodyId);
        Assert.Equal(new double[] { 8, 18, 29 }, setup.StockMin);
        Assert.Equal(new double[] { 22, 30, 39 }, setup.StockMax);
        Assert.Equal(new double[] { x, y, z }, setup.WorkZero);
    }

    [AvaloniaTheory(DisplayName = "CAM-H002 | Nozzle import commits linear motion and rejects arcs without changing history")]
    [InlineData("G21\nG1 X10 F600", true)]
    [InlineData("G21\nG2 X10 Y5", false)]
    [Trait("TestId", "CAM-H002")]
    public void NozzleImportValidatesFileBeforeCommit(string program, bool valid)
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, program);
            var session = new CadSession();
            var history = session.Current;
            session.StartCommand(() => new ImportGcodeCommand());
            var command = Assert.IsType<ImportGcodeCommand>(session.ActiveCommand);
            command.ConfigureInputs(() => command.Params.OfType<TextParam>().Single(p => p.Label == "G-code file").Value = file);
            session.CommitCommand();
            if (valid)
            {
                var path = Assert.IsType<ToolpathInfo>(Assert.Single(session.Objects).Asm);
                Assert.Equal(10, path.Move(1).x);
                Assert.Equal(600, path.FeedAt(1));
            }
            else
            {
                Assert.Empty(session.Objects);
                Assert.Same(history, session.Current);
                Assert.Contains("unsupported G2", command.Error);
            }
        }
        finally
        {
            File.Delete(file);
        }
    }
}
