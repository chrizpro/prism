using System.Globalization;
using System.Text.Json.Nodes;
using Xunit;

namespace Prism.IntegrationTests;

/// <summary>Owner-approved #131 wood CAM and patterned wheel native fixtures.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/codex/v0.4-illustrated-docs/docs/interface/gallery/README.md"/>
[Trait("Feature", "Gallery")]
[Trait("Layer", "Integration")]
public class DesktopCncPatternGalleryTests
{
    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Prism.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException();
    }

    [Fact(DisplayName = "GAL-I014 | Walnut tray reopens with its bitmap and fresh ordered CAM paths, and stock decreases without sampled contacts")]
    [Trait("TestId", "GAL-I014")]
    public async Task WalnutTrayRetainsTextureAndStockRemoval()
    {
        string path = Path.Combine(Root(), "gallery/models/walnut-valet.prism");
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal("Subtractive", (string?)saved["Tab"]);
        Assert.Equal("cnc_stock_review", (string?)saved["OpenCommand"]);
        var objects = saved["Objects"]!.AsArray();
        var body = Assert.Single(objects, o => (string?)o!["Kind"] == "Solid")!;
        Assert.NotNull(body["Surface"]?["ColorMap"]);
        var paths = objects.Where(o => (string?)o!["Kind"] == "Toolpath").ToArray();
        Assert.Equal(4, paths.Length);
        Assert.Equal(new[] { 0, 1, 2, 3 }, paths.Select(o => (int)o!["Asm"]!["ProgramOrder"]!));
        await using var mcp = new DesktopMcp("GAL-I014", "Light");
        await mcp.Initialize();
        await mcp.Call("file", new { action = "open", path });
        var info = await mcp.Call("object_info", new { @object = "Walnut valet tray" });
        Assert.True((bool)info["valid"]!);
        Assert.InRange((double)info["volume"]!, 1_200_000, 1_600_000);
        Assert.Equal(new[] { 300.0, 210, 32 }, info["size"]!.AsArray().Select(v => (double)v!));
        await mcp.Call("command_cancel");
        await mcp.Call("view", new { yaw = -25, pitch = 52, fit = true, grid = false });
        double previous = -1;
        foreach (int position in new[] { 0, 50, 100 })
        {
            await mcp.Call("run_command", new { name = "cnc_stock_review", commit = false,
                slots = new Dictionary<string, object> { ["Model"] = "Walnut valet tray" },
                @params = new Dictionary<string, object> { ["Program position"] = position, ["Grid size"] = 1.8, ["Holder diameter"] = 8, ["Flute / holder clearance length"] = 40, ["Comparison tolerance"] = .3 } });
            var result = await mcp.Call("command_set", new { @params = new Dictionary<string, object> { ["Run checks"] = true } });
            var rows = result["results"]!.AsArray().Select(v => (string)v!).ToArray();
            string removed = Assert.Single(rows, r => r.StartsWith("Removed stock:"));
            double volume = double.Parse(removed.Split(':')[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
            Assert.True(volume > previous); previous = volume;
            if (position == 100) Assert.True(volume > 650);
            Assert.Contains(rows, r => r.StartsWith("Possible gouges: 0 moves"));
            Assert.Contains("Rapid contacts: 0 sampled moves", rows);
            Assert.Contains("Holder contacts: 0 sampled moves", rows);
            await mcp.Capture("walnut-stock-" + position);
            await mcp.Call("command_cancel");
        }
        await mcp.Call("run_command", new { name = "cnc_stock_review", commit = false,
            slots = new Dictionary<string, object> { ["Model"] = "Walnut valet tray" },
            @params = new Dictionary<string, object> { ["Grid size"] = 1.8, ["Holder diameter"] = 8, ["Flute / holder clearance length"] = 40, ["Comparison tolerance"] = .3 } });
        await mcp.Call("command_set", new { @params = new Dictionary<string, object> { ["Play removal"] = true } });
        await Task.Delay(3000);
        var paused = await mcp.Call("command_set", new { @params = new Dictionary<string, object> { ["Pause removal"] = true } });
        string sampling = Assert.Single(paused["results"]!.AsArray().Select(v => (string)v!), r => r.StartsWith("Sampling:"));
        double percent = double.Parse(sampling.Split(';')[1].Trim().Split('%')[0], CultureInfo.InvariantCulture);
        Assert.InRange(percent, 5, 100);
        await mcp.Capture("walnut-playing-removal");
        await mcp.Call("command_cancel");
        await mcp.Call("batch", new { calls = paths.Select(o => new { tool = "object_update", args = new { @object = (string)o!["Name"]!, visible = false } }).ToArray() });
        await mcp.Capture("walnut-texture");
        string roundtrip = Path.Combine(mcp.OutputDirectory, "walnut-roundtrip.prism");
        await mcp.Call("file", new { action = "save_as", path = roundtrip });
        var reopened = JsonNode.Parse(await File.ReadAllTextAsync(roundtrip))!["Objects"]!.AsArray().Single(o => (string?)o!["Kind"] == "Solid")!;
        Assert.Equal((string?)body["Surface"]!["ColorMap"], (string?)reopened["Surface"]!["ColorMap"]);
    }

    [Fact(DisplayName = "GAL-I015 | Ridge wheel reopens with five 32-sector tread rows, six spokes and valid native geometry")]
    [Trait("TestId", "GAL-I015")]
    public async Task RidgeWheelRetainsItsPatternedSolids()
    {
        string path = Path.Combine(Root(), "gallery/models/ridge-all-terrain.prism");
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var solids = saved["Objects"]!.AsArray().Where(o => (string?)o!["Kind"] == "Solid").ToArray();
        Assert.Equal(185, solids.Length);
        Assert.Equal(8, saved["History"]!["Steps"]!.AsArray().Count(n => ((string?)n?["Recipe"]?["Key"])?.Contains("CircularPatternCommand") == true));
        await using var mcp = new DesktopMcp("GAL-I015", "Light");
        await mcp.Initialize(); await mcp.Call("file", new { action = "open", path });
        var reports = (await mcp.Call("batch", new { calls = solids.Select(o => new { tool = "object_info", args = new { @object = (string)o!["Name"]! } }).ToArray() })).AsArray();
        var info = reports.Select(r => r!["result"]!).ToDictionary(r => (string)r["name"]!);
        foreach (var value in info.Values) { Assert.True((bool)value["valid"]!); Assert.True((double)value["volume"]! > 0); }
        foreach (string prefix in new[] { "Chevron tread", "Intermediate L", "Intermediate R", "Shoulder L", "Shoulder R", "Split spoke", "Recessed lug nut" })
        {
            var row = info.Where(kv => kv.Key.StartsWith(prefix + " ")).Select(kv => kv.Value).ToArray();
            Assert.Equal(prefix is "Split spoke" or "Recessed lug nut" ? 6 : 32, row.Length);
            double volume = (double)row[0]["volume"]!;
            Assert.All(row, r => Assert.InRange((double)r["volume"]!, volume - .02, volume + .02));
        }
        Assert.InRange(info.Values.Min(r => (double)r["min"]![2]!), -.01, .01);
        var bores = await mcp.Call("solid_find", new { @object = "Six-lug wheel hub", kind = "faces", type = "Cylinder", radius = 8.5, radius_tol = .01 });
        Assert.Equal(6, (int)bores["total"]!);
        await mcp.Call("view", new { yaw = 148, pitch = 16, fit = true, grid = false });
        await mcp.Capture("ridge-assembled");
        var tireNames = info.Keys.Where(n => n.StartsWith("Ridge tire") || n.Contains("tread") || n.StartsWith("Intermediate") || n.StartsWith("Shoulder") || n.StartsWith("Sidewall")).ToArray();
        await mcp.Call("batch", new { calls = tireNames.Select(n => new { tool = "object_update", args = new { @object = n, visible = false } }).ToArray() });
        await mcp.Call("view", new { yaw = 168, pitch = 15, fit = true });
        await mcp.Capture("ridge-six-split-spoke-wheel");
    }
}
