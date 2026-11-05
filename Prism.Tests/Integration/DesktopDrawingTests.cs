using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Prism.IntegrationTests;

/// <summary>Real desktop drawing document through MCP; canonical guide: docs/drawing/README.md.</summary>
/// <seealso href="../../docs/drawing/README.md"/>
[Trait("Feature", "Drawing"), Trait("Layer", "Integration")]
public class DesktopDrawingTests
{
    [Fact(DisplayName = "DRW-I001 | Desktop two-sheet drawing, optional dimensions and export roundtrip in both themes")]
    [Trait("TestId", "DRW-I001")]
    public async Task DrawingDesktopRoundtrip()
    {
        foreach (string theme in new[] { "Light", "Dark" })
        {
            await using var mcp = new DesktopMcp("DRW-I001-" + theme, theme);
            await mcp.Initialize(); await mcp.Call("file", new { action = "new" });
            await mcp.Call("workspace", new { id = "drawing" });
            await mcp.Capture("01-empty", viewport: false);
            await mcp.Call("workspace", new { id = "design" });
            await mcp.Call("sketch_begin", new { plane = "XY" });
            var entities = new List<object> { new { type = "rect", corner1 = new[] { 0, 0 }, corner2 = new[] { 100, 60 } } };
            for (int i = 0; i < 8; i++) entities.Add(new { type = "circle", center = new[] { 8 + i * 12, 12 + i % 2 * 32 }, radius = 3 });
            await mcp.Call("sketch_add", new { entities });
            var finished = await mcp.Call("sketch_finish"); string sketch = (string)finished["sketch"]!;
            var result = await mcp.Call("run_command", new { name = "extrude", slots = new { Profile = sketch }, @params = new { Distance = 8, Operation = "New body" } });
            string name = (string)Assert.Single(result["created"]!.AsArray())!["name"]!;
            var state = await mcp.Call("get_state"); string bodyId = (string)state["objects"]!.AsArray().Single(o => (string?)o?["name"] == name)!["id"]!;
            var drawing = await mcp.Call("drawing_document", new { action = "new_sheet" });
            string sheet = (string)drawing["Sheets"]![0]!["Id"]!;
            await mcp.Call("drawing_document", new { action = "sheet_settings", sheet_id = sheet, settings = new { title = "Mounting plate", number = "DRW-I001", notes = "Review drawing before manufacture", format = "A3", scale = 1 } });
            drawing = await mcp.Call("drawing_document", new { action = "add_view", sheet_id = sheet, sources = new[] { bodyId }, orientation = "top", x = 110, y = 100 });
            string top = (string)drawing["Sheets"]![0]!["Views"]![0]!["Id"]!;
            Assert.Empty(drawing["Sheets"]![0]!["Annotations"]!.AsArray());
            await mcp.Capture("02-views-only", viewport: false);
            var suggestions = await mcp.Call("drawing_document", new { action = "suggest", sheet_id = sheet, view_id = top, category = "Overall size" });
            Assert.Equal(2, suggestions.AsArray().Count);
            string widthId = (string)suggestions[0]!["Id"]!;
            drawing = await mcp.Call("drawing_document", new { action = "accept", sheet_id = sheet });
            Assert.Equal(2, drawing["Sheets"]![0]!["Annotations"]!.AsArray().Count);
            await mcp.Call("drawing_document", new { action = "move", sheet_id = sheet, annotation_id = widthId, x = 0, y = 5 });
            await mcp.Call("drawing_document", new { action = "suggest", sheet_id = sheet, view_id = top, category = "Center marks" });
            await mcp.Call("drawing_document", new { action = "accept", sheet_id = sheet });
            var positions = await mcp.Call("drawing_document", new { action = "suggest", sheet_id = sheet, view_id = top, category = "Hole positions" });
            Assert.Equal(16, positions.AsArray().Count);
            // Keep a selected subset so the sheet stays deliberate; no location was silently discarded.
            await mcp.Call("drawing_document", new { action = "accept", sheet_id = sheet, suggestions = positions.AsArray().Take(4).Select(p => (string)p!["Id"]!).ToArray() });
            await mcp.Call("drawing_document", new { action = "add_view", sheet_id = sheet, parent_id = top, orientation = "projected:right", x = 260, y = 100 });
            await mcp.Call("drawing_document", new { action = "add_view", sheet_id = sheet, sources = new[] { bodyId }, orientation = "iso", x = 270, y = 175, scale = .75 });
            await mcp.Call("drawing_document", new { action = "parts", sheet_id = sheet });
            await mcp.Capture("03-annotated", viewport: false);
            await mcp.Call("drawing_document", new { action = "new_sheet" });
            drawing = await mcp.Call("drawing_document", new { action = "state" });
            string second = (string)drawing["Sheets"]![1]!["Id"]!;
            await mcp.Call("drawing_document", new { action = "add_view", sheet_id = second, sources = new[] { bodyId }, orientation = "front", x = 105, y = 90 });
            drawing = await mcp.Call("drawing_document", new { action = "state" }); string front = (string)drawing["Sheets"]![1]!["Views"]![0]!["Id"]!;
            await mcp.Call("drawing_document", new { action = "add_view", sheet_id = second, parent_id = front, orientation = "front", kind = "section", vertical = true, cut = 50, x = 220, y = 90 });
            string save = Path.Combine(mcp.OutputDirectory, "mounting-plate.prism");
            await mcp.Call("file", new { action = "save_as", path = save });
            await mcp.Call("file", new { action = "new" }); await mcp.Call("file", new { action = "open", path = save });
            drawing = await mcp.Call("drawing_document", new { action = "state" });
            Assert.Equal(2, drawing["Sheets"]!.AsArray().Count); Assert.Equal(3, drawing["Sheets"]![0]!["Views"]!.AsArray().Count);
            Assert.Contains(drawing["Sheets"]![0]!["Annotations"]!.AsArray(), a => (string?)a?["Id"] == widthId);
            var again = await mcp.Call("drawing_document", new { action = "suggest", sheet_id = sheet, view_id = top, category = "Overall size" }); Assert.Empty(again.AsArray());
            string pdf = Path.Combine(mcp.OutputDirectory, "drawing.pdf"), svg = Path.Combine(mcp.OutputDirectory, "drawing.svg"), dxf = Path.Combine(mcp.OutputDirectory, "drawing.dxf");
            await mcp.Call("drawing_document", new { action = "export", sheet_id = sheet, path = pdf, all = true });
            await mcp.Call("drawing_document", new { action = "export", sheet_id = sheet, path = svg });
            await mcp.Call("drawing_document", new { action = "export", sheet_id = sheet, path = dxf });
            Assert.Contains("/Count 2", Encoding.Latin1.GetString(await File.ReadAllBytesAsync(pdf)));
            Assert.Contains("Mounting plate", await File.ReadAllTextAsync(svg)); Assert.Contains("Mounting plate", await File.ReadAllTextAsync(dxf));
            await mcp.Capture("04-reopened", viewport: false);
        }
    }
}
