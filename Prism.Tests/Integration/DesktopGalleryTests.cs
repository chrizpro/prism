using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Prism.IntegrationTests;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/gallery/README.md">Startup model gallery.</seealso>
[Trait("Feature", "Gallery")]
[Trait("Layer", "Integration")]
public class DesktopGalleryTests
{
    [Fact(DisplayName = "GAL-I006 | Fold stand vents and cable hole pass through the sheet and appear in the flat pattern")]
    [Trait("TestId", "GAL-I006")]
    public async Task SheetMetalStandHolesAndFlatPattern()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        await using var mcp = new DesktopMcp("GAL-I006", "Light");
        await mcp.Initialize();
        await mcp.Call("file", new { action = "open", path = Path.Combine(root.FullName, "gallery/models/fold-stand.prism") });
        var stand = await mcp.Call("object_info", new { @object = "Fold stand" });
        Assert.True((bool?)stand["valid"]);
        // 2 mm sheet: a hole that goes through has a wall 2 mm deep. A cut that points away from the metal leaves zero-depth discs.
        var vents = await mcp.Call("solid_find", new { @object = "Fold stand", kind = "faces", type = "Cylinder", radius = 3.5, limit = 40 });
        Assert.Equal(30, (int)vents["total"]!);
        foreach (var hit in vents["hits"]!.AsArray())
            Assert.InRange((double)hit!["area"]!, 2 * Math.PI * 3.5 * 2 - .5, 2 * Math.PI * 3.5 * 2 + .5);
        var cable = await mcp.Call("solid_find", new { @object = "Fold stand", kind = "faces", type = "Cylinder", radius = 11, limit = 5 });
        Assert.Equal(1, (int)cable["total"]!);
        Assert.InRange((double)cable["hits"]![0]!["area"]!, 2 * Math.PI * 11 * 2 - .5, 2 * Math.PI * 11 * 2 + .5);
        // The unfolded sheet keeps every hole: one outline per face plus 30 vents and the cable hole.
        var flat = await mcp.Call("object_info", new { @object = "Flat pattern" });
        Assert.Equal((int)flat["faces"]! + 31, (int)flat["wires"]!);
        Assert.InRange((double)flat["size"]![1]!, 139.99, 140.01);
        // Characterization of the K = 0.44 development (206.18 mm) recorded on 2026-11-08, not an independent calculation.
        Assert.InRange((double)flat["size"]![0]!, 206.0, 206.4);
        var tablet = await mcp.Call("object_info", new { @object = "Tablet (placeholder)" });
        Assert.InRange((double)tablet["min"]![2]!, .9, 1.1);
        var check = await mcp.Call("run_command", new { name = "interference", commit = false,
                                                        slots = new Dictionary<string, object> { ["Bodies to check (none = all solids)"] = new[] { "Fold stand", "Tablet (placeholder)" } } });
        string result = check["activeCommand"]!["results"]!.ToJsonString();
        await mcp.Call("command_cancel");
        Assert.Contains("No interference", result);
        foreach (var angle in new[] { ("rear-quarter", 40), ("front-quarter", -35), ("side", 145) })
        {
            await mcp.Call("view", new { yaw = angle.Item2, pitch = 24, fit = true, grid = false, display = "shaded" });
            await mcp.Capture("fold-stand-" + angle.Item1);
        }
    }

    [Fact(DisplayName = "GAL-I005 | Meridian winding knob drives every orbit at its modeled tooth ratio with clear meshes")]
    [Trait("TestId", "GAL-I005")]
    public async Task OrreryGearTrainRatiosAndMeshes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        await using var mcp = new DesktopMcp("GAL-I005", "Light");
        await mcp.Initialize();
        await mcp.Call("file", new { action = "open", path = Path.Combine(root.FullName, "gallery/models/meridian-orrery.prism") });
        // Layshaft turn -> planet turn, from the modeled tooth counts (Saturn through the 42:84 and 56:70 idler stages).
        var ratios = new (string planet, double ratio)[]
        {
            ("Mercury", 80.0 / 20), ("Venus", 62.0 / 38), ("Earth", 1), ("Mars", 35.0 / 65),
            ("Jupiter", 12.0 / 138), ("Saturn", 12.0 / 138 * 42 / 84 * 56 / 70),
        };
        async Task<Dictionary<string, double[]>> Centres()
        {
            var result = new Dictionary<string, double[]>();
            foreach (string name in ratios.Select(r => r.planet).Append("Moon"))
            {
                var info = await mcp.Call("object_info", new { @object = name });
                result[name] = info["center"]!.AsArray().Select(v => (double)v!).ToArray();
            }
            return result;
        }
        static double Turn(double[] from, double[] to, double[]? about = null)
        {
            about ??= [0, 0];
            double a = Math.Atan2(from[1] - about[1], from[0] - about[0]), b = Math.Atan2(to[1] - about[1], to[0] - about[0]);
            return Math.IEEERemainder((b - a) * 180 / Math.PI, 360);
        }
        var before = await Centres();
        foreach (var centre in before.Values) Assert.InRange(centre[2], before["Earth"][2] - .001, before["Earth"][2] + .001);
        var drive = await mcp.Call("run_command", new { name = "drive_joints", commit = false });
        string knob = (string)drive["activeCommand"]!["params"]![0]!["label"]!;
        Assert.StartsWith("Revolute 1 ", knob);
        await mcp.Call("command_set", new { @params = new Dictionary<string, object> { [knob] = 36 } });
        await mcp.Call("command_commit");
        var after = await Centres();
        foreach (var (planet, ratio) in ratios)
            Assert.InRange(Turn(before[planet], after[planet]), -36 * ratio - .01, -36 * ratio + .01);
        // The Moon carrier follows Earth's orbit joint at 37:3, so it turns 13 1/3 times Earth's turn (480 deg = -120 deg mod 360).
        double moon = Turn(new[] { before["Moon"][0] - before["Earth"][0], before["Moon"][1] - before["Earth"][1] },
                           new[] { after["Moon"][0] - after["Earth"][0], after["Moon"][1] - after["Earth"][1] });
        Assert.InRange(Math.IEEERemainder(moon - -36 * (1 + 37.0 / 3), 360), -.01, .01);
        await mcp.Call("history", new { action = "undo" });

        foreach (var group in new[]
                 {
                     new[] { "Layshaft", "Mercury carrier", "Venus carrier", "Earth carrier", "Mars carrier", "Jupiter carrier" },
                     new[] { "Saturn idler", "Jupiter carrier", "Saturn carrier" },
                 })
        {
            var check = await mcp.Call("run_command", new { name = "interference", commit = false,
                                                            slots = new Dictionary<string, object> { ["Bodies to check (none = all solids)"] = group } });
            string result = check["activeCommand"]!["results"]!.ToJsonString();
            await mcp.Call("command_cancel");
            Assert.Contains("No interference", result);
        }
        foreach (var angle in new[] { ("front-quarter", -38, 20), ("side", 0, 8), ("top", -38, 89) })
        {
            await mcp.Call("view", new { yaw = angle.Item2, pitch = angle.Item3, fit = true, grid = false, display = "shaded" });
            await mcp.Capture("orrery-" + angle.Item1);
        }
    }

    [Fact(DisplayName = "GAL-I004 | Comet wheels are symmetric and grounded with clear front and rear wings")]
    [Trait("TestId", "GAL-I004")]
    public async Task RacerLayoutAndWingClearances()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        await using var mcp = new DesktopMcp("GAL-I004", "Light");
        await mcp.Initialize();
        await mcp.Call("file", new { action = "open", path = Path.Combine(root.FullName, "gallery/models/comet-racer.prism") });
        var frontWing = await mcp.Call("object_info", new { @object = "Blue front wing" });
        var rearWing = await mcp.Call("object_info", new { @object = "Blue rear wing" });
        foreach (string axle in new[] { "Front", "Rear" })
        {
            var left = await mcp.Call("object_info", new { @object = axle + " left tire" });
            var right = await mcp.Call("object_info", new { @object = axle + " right tire" });
            Assert.True(Math.Abs((double)left["volume"]! - (double)right["volume"]!) < .01);
            Assert.True(Math.Abs((double)left["min"]![1]! + (double)right["max"]![1]!) < .001);
            foreach (var tire in new[] { left, right })
            {
                Assert.InRange((double)tire["min"]![2]!, -.001, .001);
                if (axle == "Front") Assert.True((double)tire["min"]![0]! - (double)frontWing["max"]![0]! >= 100);
                else Assert.True((double)rearWing["min"]![2]! - (double)tire["max"]![2]! >= 200);
            }
        }
        var sidepod = await mcp.Call("object_info", new { @object = "Red sidepod 1" });
        var rearTire = await mcp.Call("object_info", new { @object = "Rear right tire" });
        Assert.True((double)rearTire["min"]![1]! - (double)sidepod["max"]![1]! >= 80);
        var seat = await mcp.Call("object_info", new { @object = "Reclined seat back" });
        foreach (string name in new[] { "Red roll fairing", "Rear engine cowling" })
        {
            var cover = await mcp.Call("object_info", new { @object = name });
            Assert.True((double)cover["min"]![0]! - (double)seat["max"]![0]! >= 5);
        }
        foreach (var angle in new[] { ("front-quarter", -125), ("side", 180), ("rear-quarter", 55) })
        {
            await mcp.Call("view", new { yaw = angle.Item2, pitch = 18, fit = true, grid = false, display = "shaded" });
            await mcp.Call("view", new { distance = 3300 });
            await mcp.Capture("racer-" + angle.Item1);
        }
    }

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
