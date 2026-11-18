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
    [InlineData("Stock lower-left corner", "Top of stock", 8, 18, 39)]
    [InlineData("Model origin", "Model origin", 0, 0, 0)]
    [InlineData("Stock center", "Bottom of stock", 15, 24, 29)]
    [InlineData("Stock upper-right", "Top of stock", 22, 30, 39)]
    [Trait("TestId", "CAM-H001")]
    public void SetupCommitsStockAndWorkZero(string mode, string height, int x, int y, int z)
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
            SetChoice(command, "Work zero height (Z)", height);
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

    [AvaloniaFact(DisplayName = "CAM-H004 | Subtractive ends with Export and Post G-code writes to the chosen path")]
    [Trait("TestId", "CAM-H004")]
    public async Task PostWritesToFolderOrNewFile()
    {
        var vm = new MainViewModel();
        var tab = vm.Tabs.Single(tab => tab.Title == "Subtractive");
        Assert.Equal(new[] { "Setup", "Toolpaths", "Review", "Export" }, tab.Groups.Select(group => group.Title));
        var menu = Assert.IsType<Prism.Ribbon.RibbonButton>(Assert.Single(tab.Groups[1].Items));
        Assert.Equal(new[] { "Profile", "Pocket", "Adaptive", "Turning", "Drill", "Face", "Image Carve", "3D Rough", "3D Finish", "Constant-Z Finish" },
            menu.Menu!.Select(tool => tool.Label.Replace('\n', ' ')));
        Assert.Equal("Post G-code", Assert.IsType<Prism.Ribbon.RibbonButton>(Assert.Single(tab.Groups[3].Items)).Label.Replace('\n', ' '));
        var api = new AutomationApi(vm, null);
        async Task<JsonNode> Call(string tool, string json) => (await api.CallAsync(tool, (JsonObject)JsonNode.Parse(json)!))!;
        await Call("run_command", """{"name":"box","params":{"Width":40,"Depth":30,"Height":10}}""");
        string body = (string)(await Call("get_state", "{}"))["objects"]![0]!["name"]!;
        await Call("run_command", "{\"name\":\"cam_setup\",\"slots\":{\"Model body\":\"" + body + "\"}}");
        await Call("run_command", """{"name":"cnc_face","params":{"X from":0,"X to":40,"Y from":0,"Y to":30,"Top of the material (Z)":11,"Total cutting depth":1,"Depth per pass":1}}""");
        var root = Path.Combine(Path.GetTempPath(), "prism-post-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await Call("ribbon_click", """{"tab":"Subtractive","label":"Post G-code"}""");
            Assert.IsType<CncPostCommand>(vm.Session.ActiveCommand);
            await Call("command_set", "{\"params\":{\"Save to\":\"" + root.Replace("\\", "/") + "\",\"Save G-code\":true}}");
            Assert.Single(Directory.GetFiles(root, "program_*.nc"));
            var file = Path.Combine(root, "new", "deep", "part.nc");
            await Call("command_set", "{\"params\":{\"Save to\":\"" + file.Replace("\\", "/") + "\",\"Save G-code\":true}}");
            Assert.Contains("G21", File.ReadAllText(file));
            await Call("command_cancel", "{}");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTheory(DisplayName = "CAM-H006 | Every CNC operation on the ribbon can be started through the automation API")]
    [InlineData("cnc_profile")]
    [InlineData("cnc_pocket")]
    [InlineData("cnc_drill")]
    [InlineData("cnc_face")]
    [InlineData("cnc_adaptive")]
    [InlineData("cnc_rough")]
    [InlineData("cnc_finish")]
    [InlineData("cnc_constant_z")]
    [InlineData("cnc_turn")]
    [InlineData("cnc_image_relief")]
    [InlineData("cnc_post")]
    [Trait("TestId", "CAM-H006")]
    public void EveryCncOperationIsRegistered(string name) => Assert.NotNull(AutomationApi.CreateCommand(name));

    [AvaloniaFact(DisplayName = "CAM-H007 | An Adaptive Clearing toolpath with the wall finish pass can be posted (no sideways rapid below the clearance plane)")]
    [Trait("TestId", "CAM-H007")]
    public async Task AdaptiveClearingPostsWithItsWallFinish()
    {
        var api = new AutomationApi(new MainViewModel(), null);
        async Task<JsonNode> Call(string tool, string json) => (await api.CallAsync(tool, (JsonObject)JsonNode.Parse(json)!))!;
        await Call("sketch_begin", "{\"plane\":\"XY\"}");
        await Call("sketch_add", "{\"entities\":[{\"type\":\"rect\",\"corner1\":[0,0],\"corner2\":[40,30]}]}");
        string sketch = (string)(await Call("sketch_finish", "{}"))["sketch"]!;
        await Call("run_command", "{\"name\":\"cnc_adaptive\",\"slots\":{\"Outlines of the pocket\":\"" + sketch + "\"},\"params\":{\"Tool diameter\":6.0,\"Depth per pass\":6.0,\"Total cutting depth\":12.0,\"Finish the walls with one pass\":true}}");
        string file = Path.Combine(Path.GetTempPath(), "prism-adaptive-" + Guid.NewGuid().ToString("N") + ".nc");
        try
        {
            await Call("run_command", "{\"name\":\"cnc_post\",\"commit\":false,\"params\":{\"Work zero (G54)\":\"The model's own origin\"}}");
            await Call("command_set", "{\"params\":{\"Save to\":\"" + file.Replace("\\", "/") + "\",\"Save G-code\":true}}");
            Assert.True(File.Exists(file), "the program was not written");
            Assert.Contains("G1", File.ReadAllText(file));
            await Call("command_cancel", "{}");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    static CamSetupCommand StartSetup(CadSession session, SceneObject body, string mode, string height)
    {
        var command = StartCommand(session, body, () => new CamSetupCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Model body", body);
            SetNumber(command, "Stock allowance XY", 2);
            SetNumber(command, "Top allowance", 3);
            SetNumber(command, "Bottom allowance", 1);
            SetChoice(command, "Work zero (G54)", mode);
            SetChoice(command, "Work zero height (Z)", height);
        });
        return command;
    }

    [AvaloniaFact(DisplayName = "CAM-H014 | A picked work zero uses the clicked point and the chosen height")]
    [Trait("TestId", "CAM-H014")]
    public void PickedWorkZeroUsesClickedPoint()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new(10, 20, 30), 10, 8, 6);
        var command = StartSetup(session, body, "Picked point", "Bottom of stock");
        var face = FaceFacing(body, Vec3.UnitZ);
        Assert.True(command.PickPoint(new PickResult(face, new Vec3(12, 22, 36), Vec3.UnitZ, 0), false));
        session.CommitCommand();
        Assert.Equal(new double[] { 12, 22, 29 }, session.CamSetup!.WorkZero);
    }

    [AvaloniaFact(DisplayName = "CAM-H015 | Editing a setup shows the work zero choices it was saved with")]
    [Trait("TestId", "CAM-H015")]
    public void ReopenedSetupKeepsWorkZeroChoices()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new(10, 20, 30), 10, 8, 6);
        StartSetup(session, body, "Stock center", "Bottom of stock");
        session.CommitCommand();
        var again = StartCommand(session, body, () => new CamSetupCommand());
        var xy = again.Params.OfType<ChoiceParam>().Single(p => p.Label == "Work zero (G54)");
        Assert.Equal("Stock center", xy.Options[xy.Index]);
        var z = again.Params.OfType<ChoiceParam>().Single(p => p.Label == "Work zero height (Z)");
        Assert.Equal("Bottom of stock", z.Options[z.Index]);
    }

    [AvaloniaFact(DisplayName = "CAM-H016 | Moving the work zero keeps paths valid, and paths made before it still match")]
    [Trait("TestId", "CAM-H016")]
    public void WorkZeroChangeDoesNotInvalidatePaths()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new(10, 20, 30), 10, 8, 6);
        StartSetup(session, body, "Stock lower-left corner", "Top of stock");
        session.CommitCommand();
        var first = session.CamSetup!;
        var moved = first with { WorkZero = [15, 24, 29], ZeroXY = 5, ZeroZ = 1 };
        Assert.Equal(first.Signature(), moved.Signature());
        Assert.True(moved.Matches(first.Signature()));
        Assert.False(moved.Matches(first.LegacySignature()), "A path signed with the old rule needs its own work zero to match.");
        Assert.True(first.Matches(first.LegacySignature()), "Paths made before this change still match their setup.");
        Assert.NotEqual(first.LegacySignature(), moved.LegacySignature());
        Assert.NotEqual(first.Signature(), (first with { Feed = 123 }).Signature());
    }

    [AvaloniaFact(DisplayName = "CAM-H017 | The open setup previews the stock, nine work zero dots and the zero, and a click on a dot chooses it")]
    [Trait("TestId", "CAM-H017")]
    public void SetupPreviewShowsDotsAndClickChoosesOne()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new(10, 20, 30), 10, 8, 6);
        var command = StartSetup(session, body, "Stock lower-left corner", "Top of stock");
        var overlay = new Overlay();
        command.BuildOverlay(session, overlay);
        Assert.Equal(12, overlay.Lines.Count(l => l.Width == 1.5f));           // the stock box
        Assert.Equal(9 + 1, overlay.Dots.Count);                                // nine stock points and the zero
        Assert.Contains(overlay.Labels, l => l.Text.Contains("X 8"));          // lower-left zero at X 8

        // the stock center dot, projected to the screen, is what a click there must choose
        session.Camera.SetView(30, 30);
        var center = new Vec3(15, 24, 39);
        var at = session.Camera.Project(center);
        Assert.NotNull(at);
        Assert.True(command.ClickScreen(session, at.Value.x, at.Value.y));
        var xy = command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Work zero (G54)");
        Assert.Equal("Stock center", xy.Options[xy.Index]);
        Assert.False(command.ClickScreen(session, at.Value.x + 400, at.Value.y + 400), "A click far from every dot leaves the choice alone.");
    }

    [AvaloniaFact(DisplayName = "CAM-H018 | The machine X direction turns posted coordinates and is kept with the setup")]
    [Trait("TestId", "CAM-H018")]
    public void MachineAxisTurnRotatesPostedCoordinates()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new(10, 20, 30), 10, 8, 6);
        var command = StartSetup(session, body, "Stock lower-left corner", "Top of stock");
        SetChoice(command, "Machine X axis points", "Along model +Y");
        session.CommitCommand();
        Assert.Equal(1, session.CamSetup!.ZeroTurns);
        var again = StartCommand(session, body, () => new CamSetupCommand());
        var turns = again.Params.OfType<ChoiceParam>().Single(p => p.Label == "Machine X axis points");
        Assert.Equal("Along model +Y", turns.Options[turns.Index]);

        var path = new ToolpathInfo("Test path", "6 mm end mill", 6, 60, 30, 12000, 26, [0, 0, 26, 0, 10, 0, 26, 0, 10, 0, 20, 2, 0, 0, 20, 1], [0], false, false);
        string straight = Post.Cnc([path], Controller.GRBL, Vec3.Zero, "t");
        string turned = Post.Cnc([path], Controller.GRBL, Vec3.Zero, "t", 1);
        Assert.Contains("X10", straight);
        Assert.DoesNotContain("Y-10", straight);
        Assert.Contains("Y-10", turned);          // work X is along model +Y, so model +X becomes work -Y
        Assert.DoesNotContain("X10", turned);
        Assert.Equal(1, session.CamSetup.Signature() == (session.CamSetup with { ZeroTurns = 3 }).Signature() ? 1 : 0);
    }
}
