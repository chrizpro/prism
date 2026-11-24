using Xunit;

namespace Prism.IntegrationTests;

/// <summary>Native surfacing through MCP. See docs/kingfisher-study/README.md and docs/MCP.md.</summary>
[Trait("Feature", "Surface")]
[Trait("Layer", "Integration")]
public class KingfisherStudyTests
{
    [Fact(DisplayName = "SFC-I002 | Kingfisher reopens with an elliptical deck opening and a valid solid coaming")]
    [Trait("TestId", "SFC-I002")]
    public async Task CockpitSurvivesNativeRoundTrip()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        string path = Path.Combine(root!.FullName, "docs", "kingfisher-study", "Kingfisher.prism");
        await using var mcp = new DesktopMcp("SFC-I002", "Light");
        await mcp.Initialize();
        await mcp.Call("file", new { action = "open", path });
        var deck = await mcp.Call("solid_check", new { @object = "Kingfisher deck" });
        Assert.True((bool)deck["valid"]!);
        Assert.Equal(4750, (double)deck["size"]![0]!, 1);
        var edges = await mcp.Call("solid_find", new { @object = "Kingfisher deck", kind = "edges", min_size = 2000, max_size = 2200 });
        var opening = Assert.Single(edges["hits"]!.AsArray())!;
        Assert.InRange((double)opening["length"]!, 2110, 2125);
        var rim = await mcp.Call("object_info", new { @object = "Cockpit coaming" });
        Assert.True((bool)rim["valid"]!);
        Assert.Equal("Solid", (string?)rim["kind"]);
        Assert.True((double)rim["volume"]! > 0);
        await mcp.Capture("kingfisher");
    }
}
