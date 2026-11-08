using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;
using Prism.ViewModels;
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

    [AvaloniaFact(DisplayName = "CAM-H003 | A command's Run checks action works through the automation API instead of silently doing nothing")]
    [Trait("TestId", "CAM-H003")]
    public async Task ActionsRunThroughTheApi()
    {
        var api = new AutomationApi(new MainViewModel(), null);
        async Task<JsonNode> Call(string tool, string json) => (await api.CallAsync(tool, (JsonObject)JsonNode.Parse(json)!))!;
        await Call("run_command", """{"name":"box","params":{"Width":40,"Depth":30,"Height":10}}""");
        string body = (string)(await Call("get_state", "{}"))["objects"]![0]!["name"]!;
        await Call("run_command", "{\"name\":\"cam_setup\",\"slots\":{\"Model body\":\"" + body + "\"}}");
        await Call("run_command", """{"name":"cnc_face","params":{"X from":0,"X to":40,"Y from":0,"Y to":30,"Top of the material (Z)":11,"Total cutting depth":1,"Depth per pass":1}}""");

        // open the stock review without running it, then press Run checks the way a button click does
        await Call("run_command", "{\"name\":\"cnc_stock_review\",\"slots\":{\"Model\":\"" + body + "\"},\"commit\":false}");
        var set = await Call("command_set", """{"params":{"Run checks":true}}""");
        var rows = set["results"]!.AsArray().Select(r => r!.ToString()).ToList();
        Assert.DoesNotContain(rows, r => r.Contains("Choose the model and settings"));
        Assert.Contains(rows, r => r.StartsWith("Removed stock"));
        Assert.Contains(rows, r => r.StartsWith("Remaining material"));
        await Call("command_cancel", "{}");
    }

    [AvaloniaFact(DisplayName = "CAM-H004 | Post G-code accepts a folder or a new file path and reports the outcome")]
    [Trait("TestId", "CAM-H004")]
    public async Task PostWritesToFolderOrNewFile()
    {
        var api = new AutomationApi(new MainViewModel(), null);
        async Task<JsonNode> Call(string tool, string json) => (await api.CallAsync(tool, (JsonObject)JsonNode.Parse(json)!))!;
        await Call("run_command", """{"name":"box","params":{"Width":40,"Depth":30,"Height":10}}""");
        string body = (string)(await Call("get_state", "{}"))["objects"]![0]!["name"]!;
        await Call("run_command", "{\"name\":\"cam_setup\",\"slots\":{\"Model body\":\"" + body + "\"}}");
        await Call("run_command", """{"name":"cnc_face","params":{"X from":0,"X to":40,"Y from":0,"Y to":30,"Top of the material (Z)":11,"Total cutting depth":1,"Depth per pass":1}}""");
        var root = Path.Combine(Path.GetTempPath(), "prism-post-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await Call("run_command", "{\"name\":\"cnc_post\",\"commit\":false}");
            await Call("command_set", "{\"params\":{\"Save to\":\"" + root.Replace("\\", "/") + "\",\"Save G-code\":true}}");
            Assert.Single(Directory.GetFiles(root, "program_*.nc"));
            var file = Path.Combine(root, "new", "deep", "part.nc");
            await Call("command_set", "{\"params\":{\"Save to\":\"" + file.Replace("\\", "/") + "\",\"Save G-code\":true}}");
            Assert.Contains("G21", File.ReadAllText(file));
            await Call("command_cancel", "{}");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
