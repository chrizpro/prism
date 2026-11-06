using System.Text.Json.Nodes;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Prism.IntegrationTests;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/Prism.Tests/Integration/README.md">Desktop sketch and surface evidence.</seealso>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/loft/README.md">Living Loft specification.</seealso>
[Trait("Layer", "Integration")]
public class SketchSurfaceTests
{
    [Fact(DisplayName = "LFT-I003 | Curved rails guide elliptical Surface Loft without the reported fold")]
    [Trait("Feature", "Loft")]
    [Trait("TestId", "LFT-I003")]
    public async Task CurvedRailsGuideEllipticalLoft()
    {
        await using var mcp = new DesktopMcp("LFT-I003");
        await mcp.Initialize();
        async Task<string> Ellipse(double z, double major, double minor)
        {
            await mcp.Call("sketch_begin", new { plane = "XY", offset = z });
            await mcp.Call("sketch_add", new { entities = new[] { new { type = "ellipse", center = new[] { 0.0, 0 }, major = new[] { major, 0 }, minor } } });
            return (string)(await mcp.Call("sketch_finish"))["sketch"]!;
        }
        string lower = await Ellipse(0, 71.67792467182227, 39.178793951444064);
        string upper = await Ellipse(120, 31.59994528542702, 27.232473172807023);
        await mcp.Call("sketch_begin", new { plane = "XZ" });
        await mcp.Call("sketch_add", new { entities = new[] {
            new { type = "spline", points = new[] { new[] { 31.59994528542702, 120 }, new[] { 31.59994528542702, 77.1344648707616 }, new[] { 71.67792467182227, 0 } } },
            new { type = "spline", points = new[] { new[] { -31.59994528542702, 120 }, new[] { -22.202528792560337, 83.04166993725521 }, new[] { -71.67792467182227, 0 } } }
        } });
        string rails = (string)(await mcp.Call("sketch_finish"))["sketch"]!;
        await mcp.Call("view", new { preset = "front" });
        await mcp.Call("wait_idle", new { timeout_ms = 5000 });
        await mcp.Call("view", new { fit = true });
        var preview = await mcp.Call("run_command", new {
            name = "loft_surface", commit = false,
            slots = new Dictionary<string, object> {
                ["Sections (in order)"] = new[] { new { @object = upper, edge = 1 }, new { @object = lower, edge = 1 } },
                ["Guide rails (optional, one or two)"] = new[] { new { @object = rails, edge = 1 }, new { @object = rails, edge = 2 } }
            }
        });
        Assert.NotNull(preview["activeCommand"]);
        await mcp.Capture("01-front-preview");
        await mcp.Call("view", new { preset = "iso" });
        await mcp.Call("wait_idle", new { timeout_ms = 5000 });
        await mcp.Call("view", new { fit = true });
        await mcp.Capture("02-iso-preview");
        string surface = Created(await mcp.Call("command_commit"));
        var check = await mcp.Call("solid_check", new { @object = surface });
        Assert.True((bool?)check["valid"]);
        Assert.Equal(0, (int)check["solids"]!);
        Assert.True((double)check["area"]! > 10000);
        await mcp.Capture("03-committed");
        await mcp.Call("file", new { action = "save_as", path = Path.Combine(mcp.OutputDirectory, "corrected-ellipse-loft.prism") });
    }

    [Fact(DisplayName = "LFT-I002 | Front-view sketch guide rail is visible over the Surface Loft preview")]
    [Trait("Feature", "Loft")]
    [Trait("TestId", "LFT-I002")]
    public async Task SketchRailOverLoftPreview()
    {
        await using var mcp = new DesktopMcp("LFT-I002");
        await mcp.Initialize();
        string lower = await Curve(mcp, [0, 0, 0], [10, 0, 0]);
        string upper = await Curve(mcp, [0, 0, 12], [10, 0, 12]);
        await mcp.Call("sketch_begin", new { plane = "XZ" });
        await mcp.Call("sketch_add", new { entities = new[] { new { type = "line", points = new[] { new[] { 4, 0 }, new[] { 4, 12 } } } } });
        string rail = (string)(await mcp.Call("sketch_finish"))["sketch"]!;
        await mcp.Call("object_update", new { @object = rail, color = "#E06CDE" });
        await mcp.Call("view", new { preset = "front", fit = true });
        var state = await mcp.Call("run_command", new
        {
            name = "loft_surface", commit = false,
            slots = new Dictionary<string, string[]> { ["Sections (in order)"] = [lower, upper] },
        });
        Assert.NotNull(state["activeCommand"]);
        Assert.Equal(3, (int)state["objectCount"]!);
        await mcp.Capture("01-sketch-rail-over-preview");
    }

    [Theory(DisplayName = "SKT-I001 | Applied constraint and driving dimension solve the sketch")]
    [InlineData("horizontal", "Horizontal", "v", "u")]
    [InlineData("vertical", "Vertical", "u", "v")]
    [Trait("Feature", "Sketch")]
    [Trait("TestId", "SKT-I001")]
    public async Task ConstraintAndDimensionChangeLine(string option, string type, string fixedCoordinate, string lengthCoordinate)
    {
        await using var mcp = new DesktopMcp("SKT-I001-" + option);
        await mcp.Initialize();
        await mcp.Call("sketch_begin", new { plane = "XY" });
        await mcp.Call("sketch_add", new { entities = new[] { new { type = "line", points = new[] { new[] { -20, -10 }, new[] { 20, 15 } } } } });
        var original = await mcp.Call("sketch_state");
        var line = Assert.Single(original["entities"]!.AsArray())!;
        string id = (string)line["id"]!;
        string[] refs = [(string)line["points"]![0]!, (string)line["points"]![1]!];
        await mcp.Capture("01-unconstrained");

        var constrained = await mcp.Call("sketch_constrain", new { type = option, refs = new[] { id } });
        Assert.True((bool?)constrained["converged"]);
        Assert.Contains(constrained["constraints"]!.AsArray(), row => (string?)row?["type"] == type &&
            (string?)row?["refs"]?[0] == id);
        Assert.InRange(Delta(constrained, refs, fixedCoordinate), 0, 0.0001);
        await mcp.Capture("02-constraint");

        var sized = await mcp.Call("sketch_dimension", new { type = "length", refs = new[] { id }, value = 30 });
        Assert.True((bool?)sized["converged"]);
        Assert.Contains(sized["constraints"]!.AsArray(), row => (string?)row?["type"] == "LineLength" &&
            (double?)row?["value"] == 30);
        Assert.InRange(Math.Abs(Delta(sized, refs, lengthCoordinate) - 30), 0, 0.0001);
        Assert.InRange(Delta(sized, refs, fixedCoordinate), 0, 0.0001);
        await mcp.Capture("03-constraint-and-dimension");
        var finished = await mcp.Call("sketch_finish");
        Assert.NotNull((string?)finished["sketch"]);
    }

    [Theory(DisplayName = "LFT-I001 | Surface Loft previews and commits an analytic sheet")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "Loft")]
    [Trait("TestId", "LFT-I001")]
    public async Task LoftMatchesAnalyticSheet(bool ruled)
    {
        await using var mcp = new DesktopMcp(ruled ? "LFT-I001-ruled" : "LFT-I001-smooth");
        await mcp.Initialize();
        string lower = await Curve(mcp, [0, 0, 0], [10, 0, 0]);
        string upper = await Curve(mcp, [0, 0, 12], [10, 0, 12]);
        await mcp.Call("view", new { preset = "iso", fit = true });
        await mcp.Capture("01-sections");
        var preview = await mcp.Call("run_command", new
        {
            name = "loft_surface", commit = false,
            slots = new Dictionary<string, string[]> { ["Sections (in order)"] = [lower, upper] },
            @params = new Dictionary<string, bool> { ["Straight (ruled) transitions"] = ruled },
        });
        Assert.NotNull(preview["activeCommand"]);
        Assert.Equal(2, (int)preview["objectCount"]!);
        await mcp.Capture("02-preview");
        await CheckSheet(mcp, Created(await mcp.Call("command_commit")), 120, [10, 0, 12]);
        await mcp.Capture("03-committed");
    }

    [Theory(DisplayName = "SWP-I001 | Surface Sweep previews and commits a straight-path sheet")]
    [InlineData("Follow the path", "follow")]
    [InlineData("Keep parallel (no turning)", "parallel")]
    [Trait("Feature", "Sweep")]
    [Trait("TestId", "SWP-I001")]
    public async Task SweepOrientationMatchesStraightPath(string orientation, string suffix)
    {
        await using var mcp = new DesktopMcp("SWP-I001-" + suffix);
        await mcp.Initialize();
        await mcp.Call("sketch_begin", new { plane = "XY" });
        await mcp.Call("sketch_add", new
        {
            entities = new[] { new { type = "polyline", points = new[] { new[] { 0, 0 }, new[] { 10, 0 }, new[] { 10, 6 }, new[] { 0, 6 } } } },
        });
        string profile = (string)(await mcp.Call("sketch_finish"))["sketch"]!;
        string path = await Curve(mcp, [0, 0, 0], [0, 0, 12]);
        await mcp.Call("view", new { preset = "iso", fit = true });
        await mcp.Capture("01-profile-and-path");
        var preview = await mcp.Call("run_command", new
        {
            name = "sweep_surface", commit = false, slots = new { Profile = profile, Path = path },
            @params = new Dictionary<string, string> { ["Profile orientation"] = orientation },
        });
        Assert.NotNull(preview["activeCommand"]);
        Assert.Equal(2, (int)preview["objectCount"]!);
        await mcp.Capture("02-preview");
        await CheckSheet(mcp, Created(await mcp.Call("command_commit")), 312, [10, 6, 12]);
        await mcp.Capture("03-committed");
    }

    static double Delta(JsonNode state, string[] refs, string coordinate)
    {
        var points = state["points"]!.AsArray();
        double Value(string id) => (double)points.Single(point => (string?)point?["id"] == id)![coordinate]!;
        return Math.Abs(Value(refs[0]) - Value(refs[1]));
    }

    static async Task<string> Curve(DesktopMcp mcp, int[] first, int[] last) =>
        Created(await mcp.Call("run_command", new { name = "curve_through", points = new[] { first, last } }));

    static string Created(JsonNode result) => (string)Assert.Single(result["created"]!.AsArray())!["name"]!;

    static async Task CheckSheet(DesktopMcp mcp, string name, double area, double[] size)
    {
        var result = await mcp.Call("solid_check", new Dictionary<string, string> { ["object"] = name });
        Assert.True((bool?)result["valid"]);
        Assert.Equal(0, (int)result["solids"]!);
        Assert.InRange(Math.Abs(area - (double)result["area"]!), 0, 0.0001);
        for (int axis = 0; axis < 3; axis++)
            Assert.InRange(Math.Abs(size[axis] - (double)result["size"]![axis]!), 0, 0.0001);
    }
}
