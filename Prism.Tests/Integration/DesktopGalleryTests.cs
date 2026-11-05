using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Prism.IntegrationTests;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/gallery/README.md">Startup model gallery.</seealso>
[Trait("Feature", "Gallery")]
[Trait("Layer", "Integration")]
public class DesktopGalleryTests
{
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
