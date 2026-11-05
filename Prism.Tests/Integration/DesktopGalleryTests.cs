using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Prism.IntegrationTests;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/gallery/README.md">Startup model gallery.</seealso>
[Trait("Feature", "Gallery")]
[Trait("Layer", "Integration")]
public class DesktopGalleryTests
{
    [Fact(DisplayName = "GAL-I003 | Skylark has mirrored wings, grounded wheels and clear propeller blades")]
    [Trait("TestId", "GAL-I003")]
    public async Task AircraftLayoutAndClearances()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        await using var mcp = new DesktopMcp("GAL-I003", "Light");
        await mcp.Initialize();
        await mcp.Call("file", new { action = "open", path = Path.Combine(root.FullName, "gallery/models/skylark-aircraft.prism") });
        foreach (string part in new[] { "main wing", "teal wingtip", "tailplane" })
        {
            var port = await mcp.Call("object_info", new { @object = "Port " + part });
            var starboard = await mcp.Call("object_info", new { @object = "Starboard " + part });
            Assert.True(Math.Abs((double)port["volume"]! - (double)starboard["volume"]!) < .01);
            Assert.True(Math.Abs((double)port["min"]![1]! + (double)starboard["max"]![1]!) < .001);
        }
        foreach (string wheel in new[] { "Nose", "Port", "Starboard" })
        {
            var tire = await mcp.Call("object_info", new { @object = wheel + " tire" });
            Assert.InRange((double)tire["min"]![2]!, -.001, .001);
        }
        var fuselage = await mcp.Call("object_info", new { @object = "Pearl fuselage" });
        foreach (int side in new[] { -1, 1 })
        {
            var blade = await mcp.Call("object_info", new { @object = "Propeller blade " + side });
            Assert.True((double)blade["min"]![2]! >= 60);
            Assert.True((double)fuselage["min"]![0]! - (double)blade["max"]![0]! >= 17.9);
        }
        foreach (var angle in new[] { ("front-quarter", -125), ("side", 180), ("nose", -90) })
        {
            await mcp.Call("view", new { yaw = angle.Item2, pitch = 12, fit = true, grid = false, display = "shaded" });
            await mcp.Call("view", new { distance = 3600 });
            await mcp.Capture("aircraft-" + angle.Item1);
        }
    }

    [Fact(DisplayName = "GAL-I002 | Cove arm posts clear the complete back cushion cage by at least 8 mm")]
    [Trait("TestId", "GAL-I002")]
    public async Task ChairPostsStayOutsideBackCushion()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        await using var mcp = new DesktopMcp("GAL-I002", "Light");
        await mcp.Initialize();
        await mcp.Call("file", new { action = "open", path = Path.Combine(root.FullName, "gallery/models/cove-chair.prism") });
        var back = await mcp.Call("object_info", new { @object = "Sculpted back cushion" });
        // Subdivision stays inside its control-cage bounds. Disjoint X intervals establish clearance
        // independently of tessellation or a Boolean conversion of the Freeform body.
        foreach (int x in new[] { -307, 307 })
            foreach (int y in new[] { -145, 160 })
            {
                var post = await mcp.Call("object_info", new { @object = $"Arm support {x} {y}" });
                double gap = x < 0 ? (double)back["min"]![0]! - (double)post["max"]![0]!
                    : (double)post["min"]![0]! - (double)back["max"]![0]!;
                Assert.True(gap >= 8 - .001, $"Post {x}, {y} has only {gap} mm of back clearance.");
                var arm = await mcp.Call("object_info", new { @object = $"Soft arm {x}" });
                Assert.True((double)post["min"]![0]! > (double)arm["min"]![0]!);
                Assert.True((double)post["max"]![0]! < (double)arm["max"]![0]!);
            }
        await mcp.Call("view", new { yaw = 145, pitch = 15, fit = true, grid = false, display = "shaded" });
        await mcp.Capture("chair-rear");
        await mcp.Call("view", new { yaw = 90, pitch = 5, fit = true, grid = false, display = "shaded" });
        await mcp.Capture("chair-side");
    }

    [Fact(DisplayName = "GAL-I001 | Curated examples reopen through MCP with valid geometry, closed cages and saved appearance")]
    [Trait("TestId", "GAL-I001")]
    public async Task CuratedModelsOpenWithNativeGeometryAndAppearance()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var catalog = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root.FullName, "gallery", "catalog.json")))!;
        await using var mcp = new DesktopMcp("GAL-I001", "Light");
        await mcp.Initialize();
        foreach (var entry in catalog["Items"]!.AsArray())
        {
            string id = (string)entry!["Id"]!;
            string path = Path.Combine(root.FullName, "gallery", "models", id + ".prism");
            byte[] data = await File.ReadAllBytesAsync(path);
            Assert.Equal((long)entry["Bytes"]!, data.LongLength);
            Assert.Equal(((string)entry["Sha256"]!).ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(data)));
            var saved = JsonNode.Parse(data)!;
            await mcp.Call("document_new");
            var state = await mcp.Call("file", new { action = "open", path });
            Assert.Equal(saved["Objects"]!.AsArray().Count, (int)state["objects"]!);
            foreach (var obj in saved["Objects"]!.AsArray())
            {
                string name = (string)obj!["Name"]!;
                string kind = (string)obj["Kind"]!;
                if (kind is not ("Solid" or "Surface" or "Form")) continue;
                var info = await mcp.Call("object_info", new { @object = name });
                Assert.Equal((bool)obj["Visible"]!, (bool)info["visible"]!);
                uint color = (uint)obj["Color"]!;
                Assert.Equal($"#{color & 0xFFFFFF:X6}", (string?)info["color"]);
                if (kind is "Solid" or "Surface")
                {
                    Assert.True((bool?)info["valid"], id + ": " + name);
                    if (kind == "Solid") Assert.True((double?)info["volume"] > 0, id + ": " + name);
                }
                else
                {
                    var form = await mcp.Call("form_info", new { @object = name, summary = true });
                    Assert.True((bool?)form["closed"], id + ": " + name);
                    Assert.Equal(0, (int?)form["topology"]!["nonManifoldEdges"]);
                    Assert.Equal(0, (int?)form["topology"]!["windingConflicts"]);
                }
                if (obj["Material"] is { } material)
                    Assert.Equal(material.GetValue<string>(), (string?)info["appearance"]?["materialId"]);
            }
            await mcp.Call("view", new { yaw = -35, pitch = id == "halo-pendant" ? -12 : 20, fit = true, grid = false, display = "shaded" });
            await mcp.Capture(id);
        }
    }
}
