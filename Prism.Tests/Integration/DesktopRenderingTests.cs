using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Prism.IntegrationTests;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/rendering/README.md">Product rendering specification.</seealso>
[Trait("Feature", "Rendering")]
[Trait("Layer", "Integration")]
public class DesktopRenderingTests
{
    [Fact(DisplayName = "RND-I001 | Desktop imports an embedded HDR and renders glass, directional metal and coated paint with optional denoising")]
    [Trait("TestId", "RND-I001")]
    public async Task MaterialGalleryUnderHdrLighting()
    {
        await using var mcp = new DesktopMcp("RND-I001"); await mcp.Initialize(); await mcp.Call("document_new");
        async Task<string> Sphere(double x)
        {
            var result = await mcp.Call("run_command", new { name = "sphere", @params = new Dictionary<string, object> { ["Radius"] = 10, ["Position X"] = x, ["Position Z"] = 10, ["Operation"] = "New body" } });
            return (string)Assert.Single(result["created"]!.AsArray())!["name"]!;
        }
        string metal = await Sphere(-25), glass = await Sphere(0), paint = await Sphere(25);
        await mcp.Call("material_apply", new { @object = metal, material = "304", roughness = .28, surface = new { Anisotropy = .85, AnisotropyAngle = 20 } });
        await mcp.Call("material_apply", new { @object = glass, material = "glass", color = "#FFFFFF", roughness = 0, surface = new { Transmission = 1, Ior = 1.5, AbsorptionColor = 0xFFB7E6F3u, AbsorptionDistance = 15 } });
        await mcp.Call("material_apply", new { @object = paint, material = "abs", color = "#C62D18", roughness = .25, surface = new { Coat = .8, CoatRoughness = .1 } });
        await mcp.Call("select", new { items = Array.Empty<string>() }); await mcp.Call("view", new { preset = "iso", fit = true });
        await mcp.Capture("01-material-viewport");
        string path = Path.Combine(mcp.OutputDirectory, "gallery.prism"); await mcp.Call("file", new { action = "save_as", path });
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        saved["RenderSetup"] = new JsonObject { ["Env"] = 4, ["EnvironmentImage"] = StudioHdr(), ["EnvironmentRotation"] = 25, ["EnvironmentIntensity"] = 1, ["Background"] = 0, ["Ground"] = true, ["Denoise"] = false };
        await File.WriteAllTextAsync(path, saved.ToJsonString()); await mcp.Call("file", new { action = "open", path });
        await mcp.Call("view", new { preset = "iso", fit = true }); await mcp.Call("run_command", new { name = "render", commit = false });
        async Task Render(bool denoise, string name)
        {
            await mcp.Call("command_set", new { @params = new Dictionary<string, object> { ["Picture size"] = "Draft  960 × 540", ["Quality (samples per pixel)"] = 32, ["Light bounces"] = 8, ["Denoise final image"] = denoise, ["Start render"] = true } });
            DateTime deadline = DateTime.UtcNow.AddSeconds(120); JsonNode status;
            do { await Task.Delay(300); status = await mcp.Call("render_control", new { action = "status" }); } while ((bool?)status["running"] == true && DateTime.UtcNow < deadline);
            Assert.Null(status["error"]); Assert.True((bool?)status["finished"], status["status"]?.ToString());
            await mcp.Call("render_control", new { action = "save", path = Path.Combine(mcp.OutputDirectory, name + ".png") });
            await mcp.Capture(name + "-desktop");
        }
        await Render(false, "02-hdr-raw"); await Render(true, "03-hdr-denoised");
        Assert.NotEqual(await File.ReadAllBytesAsync(Path.Combine(mcp.OutputDirectory, "02-hdr-raw.png")), await File.ReadAllBytesAsync(Path.Combine(mcp.OutputDirectory, "03-hdr-denoised.png")));
        await mcp.Call("command_commit"); await mcp.Call("file", new { action = "save", path });
        var roundtrip = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.True((bool?)roundtrip["RenderSetup"]!["Denoise"]); Assert.Equal(4, (int?)roundtrip["RenderSetup"]!["Env"]);
        Assert.Equal(StudioHdr(), (string?)roundtrip["RenderSetup"]!["EnvironmentImage"]);
    }
    static string StudioHdr()
    {
        const int w = 128, h = 64; using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes($"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {h} +X {w}\n"));
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            float u = (x + .5f) / w, v = (y + .5f) / h; var color = v > .5f ? new Vector3(.09f, .1f, .12f) : new Vector3(.4f, .45f, .55f);
            if (u > .05f && u < .23f && v > .16f && v < .43f) color = new(14, 13, 11);
            if (u > .5f && u < .62f && v > .18f && v < .45f) color = new(7, 9, 12);
            if (u > .78f && u < .83f && v > .22f && v < .4f) color = new(5, 3, 1.5f);
            float max = Math.Max(color.X, Math.Max(color.Y, color.Z)); int e = MathF.ILogB(max) + 1; float scale = MathF.ScaleB(256, -e);
            stream.Write([(byte)(color.X * scale), (byte)(color.Y * scale), (byte)(color.Z * scale), (byte)(e + 128)]);
        }
        return Convert.ToBase64String(stream.ToArray());
    }
}
