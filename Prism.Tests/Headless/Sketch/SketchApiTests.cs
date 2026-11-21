using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.Core;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <summary>The automation API's sketch_add takes text, so scripts and assistants can make lettering without driving the pointer (owner request 2026-11-15).</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/MCP.md">Automation API.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SketchApiTests
{
    static async Task<JsonNode> Call(AutomationApi api, string tool, string json) => (await api.CallAsync(tool, (JsonObject)JsonNode.Parse(json)!))!;

    [AvaloniaFact(DisplayName = "SKT-H065 | sketch_add places text at the given spot and height as curves in the sketch")]
    [Trait("TestId", "SKT-H065")]
    public async Task TextIsPlacedAsCurves()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        await Call(api, "sketch_begin", "{\"plane\":\"XY\"}");
        var added = await Call(api, "sketch_add", "{\"entities\":[{\"type\":\"text\",\"text\":\"HI\",\"at\":[10,20],\"height\":12}]}");
        Assert.True((int)added["added"]! > 4, "the letters become several lines and splines");
        string name = (string)(await Call(api, "sketch_finish", "{}"))["sketch"]!;
        var sketch = vm.Session.Objects.Single(o => o.Name == name);
        var box = sketch.Mesh;
        Assert.InRange(box.Min.X, 9, 12);                       // starts at the given spot (a letter's side bearing may shift it a little)
        Assert.InRange(box.Min.Y, 19, 21);
        Assert.InRange(box.Max.Y - box.Min.Y, 8, 13);           // capital height: close to, and not above, the letter height
    }

    [AvaloniaFact(DisplayName = "SKT-H066 | sketch_add text refuses empty text with a message, and line breaks make a second line")]
    [Trait("TestId", "SKT-H066")]
    public async Task EmptyTextRefusedAndLinesStack()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        await Call(api, "sketch_begin", "{\"plane\":\"XY\"}");
        await Assert.ThrowsAnyAsync<Exception>(() => Call(api, "sketch_add", "{\"entities\":[{\"type\":\"text\",\"text\":\"  \"}]}"));
        await Call(api, "sketch_add", "{\"entities\":[{\"type\":\"text\",\"text\":\"A\\nB\",\"height\":10}]}");
        string name = (string)(await Call(api, "sketch_finish", "{}"))["sketch"]!;
        var box = vm.Session.Objects.Single(o => o.Name == name).Mesh;
        Assert.True(box.Max.Y - box.Min.Y > 15, "two lines are taller than one letter");
    }
}
