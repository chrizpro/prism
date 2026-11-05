using System.Text.Json.Nodes;
using Xunit;

namespace Prism.IntegrationTests;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/appearance-studio/README.md">Appearance studio.</seealso>
[Trait("Feature", "Appearance")]
[Trait("Layer", "Integration")]
public class DesktopAppearanceTests
{
    [Fact(DisplayName = "APP-I001 | Desktop shaders display gradients and weathering; embedded image layers survive reopen and render")]
    [Trait("TestId", "APP-I001")]
    public async Task SurfaceLayersDisplayPersistAndRender()
    {
        await using var mcp = new DesktopMcp("APP-I001"); await mcp.Initialize();
        await mcp.Call("document_new");
        var made = await mcp.Call("run_command", new { name = "sphere" });
        string body = (string)Assert.Single(made["created"]!.AsArray())!["name"]!;
        await mcp.Call("material_apply", new { @object = body, material = "6061" });
        await mcp.Call("view", new { preset = "iso", fit = true });
        await mcp.Call("select", new { items = Array.Empty<string>() });
        await mcp.Capture("01-default");
        var styled = await mcp.Call("material_apply", new
        {
            @object = body, color = "#EB5234", metal = 0, roughness = .35,
            surface = new { Gradient = 4, Color2 = 0xFF3478DBu, Color3 = 0xFFE9B26Bu, Color4 = 0xFFB777CBu, GradientSize = 30, Coat = .6, Bump = .08 }
        });
        Assert.Equal("#EB5234", (string?)styled["color"]);
        await mcp.Capture("02-freeform-coated");
        await mcp.Call("material_apply", new { @object = body, color = "#BBC3CC", metal = 1, roughness = .25, surface = new { Weathering = 2, Coverage = .55, PatchSize = 5, Bump = .7 } });
        await mcp.Capture("03-weathered-metal");
        var before = await mcp.Call("object_info", new { @object = body });
        string path = Path.Combine(Path.GetTempPath(), "prism-appearance-" + Guid.NewGuid() + ".prism");
        string png = Path.Combine(mcp.OutputDirectory, "06-render.png");
        try
        {
            await mcp.Call("file", new { action = "save_as", path });
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var obj = Assert.Single(saved["Objects"]!.AsArray())!;
            Assert.Equal(2, (int?)obj["Surface"]!["Weathering"]);
            Assert.Equal("6061", (string?)obj["Material"]);
            // Embedded opaque PNG is shared by color/roughness/bump and background to exercise decoding in both render paths.
            const string image = "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAIUlEQVR4nGNwa7r0H4Rf78sDY3Q+A0EFuCRgfMIKBoEbAKeLteHHtGDbAAAAAElFTkSuQmCC";
            obj["Surface"]!["ColorMap"] = image; obj["Surface"]!["RoughnessMap"] = image; obj["Surface"]!["BumpMap"] = image;
            saved["RenderSetup"] = new JsonObject { ["Background"] = 6, ["BackgroundImage"] = image, ["ImageFit"] = 1, ["Ground"] = false };
            await File.WriteAllTextAsync(path, saved.ToJsonString());
            await mcp.Call("file", new { action = "open", path });
            await mcp.Call("view", new { preset = "iso", fit = true });
            await mcp.Capture("04-embedded-maps-reopened");
            var after = await mcp.Call("object_info", new { @object = body });
            Assert.Equal(before["volume"]?.ToJsonString(), after["volume"]?.ToJsonString());
            await mcp.Call("run_command", new { name = "render", commit = false });
            await mcp.Call("command_set", new { @params = new Dictionary<string, object> { ["Picture size"] = "Draft  960 × 540", ["Quality (samples per pixel)"] = 16, ["Start render"] = true } });
            var deadline = DateTime.UtcNow.AddSeconds(45); JsonNode status;
            do { await Task.Delay(250); status = await mcp.Call("render_control", new { action = "status" }); } while ((bool?)status["running"] == true && DateTime.UtcNow < deadline);
            Assert.Null(status["error"]); Assert.True((bool?)status["finished"]);
            await mcp.Call("render_control", new { action = "save", path = png });
            Assert.True(new FileInfo(png).Length > 1000);
            await mcp.Capture("05-rendered-backplate");
        }
        finally { File.Delete(path); }
    }
}
