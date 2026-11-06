using System.Text.Json.Nodes;
using Xunit;

namespace Prism.IntegrationTests;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/document-safety/README.md">Document safety.</seealso>
[Trait("Feature", "DocumentSafety")]
[Trait("Layer", "Integration")]
public class DesktopDocumentSafetyTests
{
    [Fact(DisplayName = "SAV-I001 | Actual desktop timer writes a separate recoverable copy while preserving original bytes and dirty state")]
    [Trait("TestId", "SAV-I001")]
    public async Task DesktopTimerWritesNativeRecoveryWithoutSavingOriginal()
    {
        await using var mcp = new DesktopMcp("SAV-I001", "Light", recoveryIntervalMinutes: 1);
        await mcp.Initialize();
        await mcp.Call("document_new");
        var made = await mcp.Call("run_command", new { name = "box" });
        string body = (string)Assert.Single(made["created"]!.AsArray())!["name"]!;
        string original = Path.Combine(mcp.OutputDirectory, "original.prism");
        await mcp.Call("file", new { action = "save_as", path = original });
        byte[] bytes = await File.ReadAllBytesAsync(original);
        await mcp.Call("material_apply", new { @object = body, color = "#D93F2D", roughness = .35 });
        await mcp.Call("view", new { preset = "iso", fit = true, grid = false });
        await mcp.Capture("01-changed-original");
        string recoveryFolder = Path.Combine(mcp.OutputDirectory, "recovery");
        string[] copies = [];
        var deadline = DateTime.UtcNow.AddSeconds(95);
        do
        {
            await Task.Delay(1000);
            if (Directory.Exists(recoveryFolder)) copies = Directory.GetFiles(recoveryFolder, "*.prism");
        } while (copies.Length == 0 && DateTime.UtcNow < deadline);
        string copy = Assert.Single(copies);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
        var state = await mcp.Call("get_state");
        Assert.Equal(original, (string?)state["document"]); Assert.True((bool?)state["dirty"]);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(copy))!;
        Assert.Equal(0xFFD93F2Du, (uint)saved["Objects"]![0]!["Color"]!);
        await mcp.Call("document_new");
        await mcp.Call("file", new { action = "open", path = copy });
        var info = await mcp.Call("object_info", new { @object = body });
        Assert.True((bool?)info["valid"]); Assert.Equal("#D93F2D", (string?)info["color"]);
        Assert.True((double?)info["volume"] > 0);
        await mcp.Call("view", new { preset = "iso", fit = true, grid = false });
        await mcp.Capture("02-recovery-reopened");
    }
}
