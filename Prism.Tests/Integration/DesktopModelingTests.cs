using Xunit;
using System.Text.Json.Nodes;

namespace Prism.IntegrationTests;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/Prism.Tests/Integration/README.md">Desktop integration scenarios.</seealso>
[Trait("Layer", "Integration")]
public class DesktopModelingTests
{
    [Fact(DisplayName = "EXT-I001 | MCP Extrude previews, commits and supports undo/redo")]
    [Trait("Feature", "Extrude")]
    [Trait("TestId", "EXT-I001")]
    public async Task ExtrudePreviewCommitAndHistory()
    {
        await using var mcp = new DesktopMcp("EXT-I001");
        await mcp.Initialize();
        string profile = await SketchRectangle(mcp, "XY", [0, 0], [10, 6]);
        await mcp.VerifyAuthentication();
        await mcp.Capture("01-profile");
        var pending = await mcp.Call("run_command", new
        {
            name = "extrude", commit = false, slots = new { Profile = profile },
            @params = new { Operation = "New body", Distance = 8 },
        });
        Assert.NotNull(pending["activeCommand"]);
        var settings = await mcp.Call("command_set", new { @params = new { Distance = 12 } });
        Assert.Equal(12d, (double)settings["values"]!["Distance"]!);
        Assert.Single((await mcp.Call("get_state"))["objects"]!.AsArray());
        await mcp.Capture("02-preview");
        string body = Created(await mcp.Call("command_commit"));
        await CheckSolid(mcp, body, 720, [0, 0, 0], [10, 6, 12]);
        await mcp.Capture("03-committed");
        await mcp.Call("history", new { action = "undo" });
        Assert.Single((await mcp.Call("get_state"))["objects"]!.AsArray());
        await mcp.Capture("04-undo");
        await mcp.Call("history", new { action = "redo" });
        await CheckSolid(mcp, body, 720, [0, 0, 0], [10, 6, 12]);
        await mcp.Capture("05-redo");
    }

    [Fact(DisplayName = "REV-I001 | MCP Revolve previews and commits both directions")]
    [Trait("Feature", "Revolve")]
    [Trait("TestId", "REV-I001")]
    public async Task RevolveChangesQuadrantWhenReversed()
    {
        await using var mcp = new DesktopMcp("REV-I001");
        await mcp.Initialize();
        string profile = await SketchRectangle(mcp, "XZ", [2, 0], [5, 8]);
        await mcp.Capture("01-profile");
        var pending = await StartRevolve(mcp, profile);
        Assert.NotNull(pending["activeCommand"]);
        await mcp.Capture("02-preview");
        string body = Created(await mcp.Call("command_commit"));
        await CheckSolid(mcp, body, 42 * Math.PI, [0, 0, 0], [5, 5, 8]);
        await mcp.Capture("03-forward");
        await mcp.Call("history", new { action = "undo" });
        await StartRevolve(mcp, profile);
        var settings = await mcp.Call("command_set", new { @params = new Dictionary<string, object> { ["Reverse direction"] = true } });
        Assert.True((bool?)settings["values"]!["Reverse direction"]);
        string reversed = Created(await mcp.Call("command_commit"));
        await CheckSolid(mcp, reversed, 42 * Math.PI, [0, -5, 0], [5, 0, 8]);
        await mcp.Capture("04-reversed");
    }

    [Fact(DisplayName = "SFC-I001 | MCP Control Grid and Thicken survive document save and reopen")]
    [Trait("Feature", "Surfacing")]
    [Trait("TestId", "SFC-I001")]
    public async Task SurfaceToSolidRoundtrips()
    {
        await using var mcp = new DesktopMcp("SFC-I001");
        await mcp.Initialize();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mcp.Call("run_command", new { name = "not_a_prism_command" }));
        Assert.Contains("Unknown command", error.Message);
        Assert.Empty((await mcp.Call("get_state"))["objects"]!.AsArray());
        string patch = Created(await mcp.Call("run_command", new
        {
            name = "nurbs_patch",
            @params = new Dictionary<string, object>
            {
                ["Width"] = 10, ["Height"] = 6, ["Initial relief"] = 0,
            },
        }));
        var sheet = await mcp.Call("solid_check", new Dictionary<string, string> { ["object"] = patch });
        Assert.Equal(0, (int)sheet["solids"]!);
        Assert.Equal(60d, (double)sheet["area"]!, 4);
        await mcp.Call("view", new { preset = "iso", fit = true });
        await mcp.Capture("01-control-grid");
        string solid = Created(await mcp.Call("run_command", new
        {
            name = "thicken", slots = new { Surface = patch }, @params = new { Thickness = 2 },
        }));
        await CheckSolid(mcp, solid, 120, [-5, -3, -2], [5, 3, 0], volumeTolerance: 0.01);
        await mcp.Capture("02-thickened");
        string path = Path.Combine(Path.GetTempPath(), $"prism-integration-{Guid.NewGuid():N}.prism");
        try
        {
            await mcp.Call("file", new { action = "save_as", path });
            await mcp.Call("file", new { action = "new" });
            Assert.Empty((await mcp.Call("get_state"))["objects"]!.AsArray());
            await mcp.Call("file", new { action = "open", path });
            await CheckSolid(mcp, solid, 120, [-5, -3, -2], [5, 3, 0], volumeTolerance: 0.01);
            await mcp.Capture("03-reopened");
        }
        finally
        {
            File.Delete(path);
        }
    }

    static Task<JsonNode> StartRevolve(DesktopMcp mcp, string profile) =>
        mcp.Call("run_command", new
        {
            name = "revolve", commit = false, slots = new { Profile = profile },
            @params = new { Axis = "World Z", Angle = 90, Operation = "New body" },
        });

    static async Task<string> SketchRectangle(DesktopMcp mcp, string plane, int[] corner1, int[] corner2)
    {
        Assert.Empty((await mcp.Call("get_state"))["objects"]!.AsArray());
        await mcp.Call("sketch_begin", new { plane });
        await mcp.Call("sketch_add", new { entities = new[] { new { type = "rect", corner1, corner2 } } });
        var result = await mcp.Call("sketch_finish");
        await mcp.Call("view", new { preset = "iso", fit = true });
        return (string)result["sketch"]!;
    }

    static string Created(JsonNode result) => (string)Assert.Single(result["created"]!.AsArray())!["name"]!;

    static async Task CheckSolid(DesktopMcp mcp, string name, double volume, double[] min, double[] max, double volumeTolerance = 0.0001)
    {
        var result = await mcp.Call("solid_check", new Dictionary<string, string> { ["object"] = name });
        Assert.True((bool?)result["valid"]);
        Assert.Equal(1, (int)result["solids"]!);
        Assert.InRange(Math.Abs(volume - (double)result["volume"]!), 0, volumeTolerance);
        for (int i = 0; i < 3; i++)
        {
            Assert.InRange(Math.Abs(min[i] - (double)result["min"]![i]!), 0, 0.0001);
            Assert.InRange(Math.Abs(max[i] - (double)result["max"]![i]!), 0, 0.0001);
        }
    }
}
