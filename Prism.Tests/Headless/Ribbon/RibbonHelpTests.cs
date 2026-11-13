using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Help;
using Prism.Rendering;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Ribbon;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/tool-examples.md">Tool assistance: hover help and its pictures.</seealso>
[Trait("Feature", "Ribbon")]
[Trait("Layer", "Headless")]
public class RibbonHelpTests
{
    /// <summary>Buttons whose label and help title differ on purpose: the help names the same tool in other words.</summary>
    static readonly HashSet<(string Label, string Title)> Aliases =
    [
        ("Through", "X-ray"), ("Orient part", "Rotate"), ("Tilt part", "Rotate"), ("Hollow body", "Shell"),
        ("Export for resin", "Export for Printing"), ("Clear Analysis", "Clear Results"), ("Clear Section", "Clear Results"),
        ("Displace- ment", "Displacement"), ("Recall faces", "Recall Face Selection"), ("Section View", "Section Analysis"),
    ];

    [AvaloniaFact(DisplayName = "HLP-H001 | Every ribbon button's hover card describes that button's own tool")]
    [Trait("TestId", "HLP-H001")]
    public void HoverCardsNameTheirTool()
    {
        var vm = new MainViewModel();
        var wrong = new List<string>();
        foreach (var tab in vm.Tabs)
            foreach (var button in tab.Buttons.Where(b => b.Menu == null).Distinct())
            {
                string label = button.Label.Replace('\n', ' ');
                var key = button.ExampleIcon;
                var topic = ToolHelp.Find(key);
                if (topic == null) { wrong.Add($"{tab.Title}/{label}: no topic"); continue; }
                // Topics written for one button are keyed "icon|Label"; the rest must name the same tool.
                if (key!.Contains('|') || SameWords(topic.Title, label) || Aliases.Contains((label, topic.Title))) continue;
                wrong.Add($"{tab.Title}/{label}: shows '{topic.Title}'");
            }
        Assert.True(wrong.Count == 0, "Hover cards for the wrong tool:\n" + string.Join("\n", wrong.Distinct()));
    }

    [AvaloniaFact(DisplayName = "HLP-H002 | Every hover card with a picture renders a before and an after image")]
    [Trait("TestId", "HLP-H002")]
    public void HoverPicturesRender()
    {
        var vm = new MainViewModel();
        var failures = new List<string>();
        var keys = vm.Tabs.SelectMany(tab => tab.Buttons).Select(b => b.ExampleIcon).OfType<string>().Distinct().ToList();
        foreach (var key in keys)
        {
            var topic = ToolHelp.Find(key)!;
            if (topic.NoPreview) continue;
            try
            {
                var example = ToolHelp.Example(topic);
                foreach (var source in new[] { example.Before, example.After })
                    if (source.Mesh.TriangleCount + source.Mesh.SegmentCount == 0 || StudioPreview.Render(source, true).Bgra.Distinct().Count() <= 12)
                        failures.Add(key + ": empty picture");
            }
            catch (Exception ex) { failures.Add(key + ": " + ex.Message); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(keys.Count(k => !ToolHelp.Find(k)!.NoPreview) > 200, "most tools carry a picture");
    }

    [AvaloniaFact(DisplayName = "HLP-H003 | Tools that share an icon keep their own hover help")]
    [Trait("TestId", "HLP-H003")]
    public void SharedIconsKeepTheirOwnHelp()
    {
        var vm = new MainViewModel();
        string Title(string tab, string label) =>
            ToolHelp.Find(vm.Tabs.Single(t => t.Title == tab).Buttons.First(b => b.Label.Replace('\n', ' ') == label && b.Menu == null).ExampleIcon)!.Title;

        Assert.Equal("Trim", Title("Sketch", "Trim"));
        Assert.Equal("Trim Surface", Title("Surface", "Trim"));
        Assert.Equal("Untrim", Title("Surface", "Untrim"));
        Assert.Equal("Thicken Form", Title("Freeform", "Thicken"));
        Assert.Equal("Freeform Cylinder", Title("Freeform", "Cylinder"));
        Assert.Equal("Isocurves", ToolHelp.Find(vm.Tabs.Single(t => t.Title == "Surface").Buttons.Single(b => b.Id == "menu:curves").Menu!.First(b => b.Label == "Isocurves").ExampleIcon)!.Title);   // inside the Curves dropdown since 2026-11-13
        Assert.Equal("Mesh Refinement", Title("Static", "Mesh refinement"));
        // Sweep 2 rails shares the sweep icon but not its one-path instructions.
        Assert.Null(ToolHelp.ForCommand(new SweepTwoRailsCommand()));
    }

    [AvaloniaFact(DisplayName = "HLP-H004 | Flat sketch pictures face the preview camera")]
    [Trait("TestId", "HLP-H004")]
    public void SketchPicturesFaceTheCamera()
    {
        var after = ToolHelp.Example(ToolHelp.Find("c-horizontal")!).After.Mesh;
        var up = new Core.Camera().Up;
        var heights = Enumerable.Range(0, after.EdgePoints.Length / 3)
            .Select(i => after.EdgePoints[i * 3] * up.X + after.EdgePoints[i * 3 + 1] * up.Y + after.EdgePoints[i * 3 + 2] * up.Z).ToList();
        // A horizontal line reads level on screen: every point sits at the same screen height.
        Assert.True(heights.Max() - heights.Min() < 1e-3, $"screen heights vary by {heights.Max() - heights.Min()}");
    }

    static bool SameWords(string title, string label)
    {
        static HashSet<string> Words(string text) => text.ToLowerInvariant().Split([' ', '/', '-', '&', '(', ')', '.'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var a = Words(title); var b = Words(label);
        return a.IsSupersetOf(b) || b.IsSupersetOf(a);
    }
}
