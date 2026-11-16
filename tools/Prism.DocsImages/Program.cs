using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Prism.Core;
using Prism.Help;
using Prism.Rendering;
using Prism.Ribbon;
using Prism.ViewModels;

// Export the application's existing explanatory examples, not fabricated command results.
// Input: site/tools/catalog-model.mjs toolPages, serialized as JSON by Node.
if (args.Length != 2) throw new ArgumentException("Usage: Prism.DocsImages <tool-pages.json> <output-directory>");
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
AppSettings.Current.Ribbon = new RibbonLayout();
AppSettings.Current.LastPicks.Clear();
var vm = new MainViewModel();
using var pages = JsonDocument.Parse(File.ReadAllText(args[0]));
var manifest = new List<object>();
var exported = new Dictionary<string, string>();
foreach (var page in pages.RootElement.EnumerateArray())
{
    string slug = page.GetProperty("slug").GetString()!;
    string id = page.GetProperty("tool").GetProperty("id").GetString()!;
    RibbonButton? button = null;
    foreach (var place in page.GetProperty("places").EnumerateArray())
    {
        string tab = place.GetProperty("tab").GetString()!, group = place.GetProperty("group").GetString()!;
        button = vm.Tabs.FirstOrDefault(t => t.Title == tab)?.Groups.FirstOrDefault(g => g.Title == group)?.Items.OfType<RibbonButton>()
            .SelectMany(b => b.Menu ?? new List<RibbonButton> { b }).FirstOrDefault(b => b.Id == id);
        if (button != null) break;
    }
    string? key = button?.ExampleIcon;
    var topic = ToolHelp.Find(key);
    string? picture = null, error = null;
    if (topic is { NoPreview: false })
    {
        try
        {
            if (!exported.TryGetValue(topic.Icon, out picture))
            {
                var example = ToolHelp.Example(topic);
                picture = slug;
                var before = StudioPreview.Render(example.Before with { WholeView = true }, light: false);
                var after = StudioPreview.Render(example.After with { WholeView = true }, light: false);
                File.WriteAllBytes(Path.Combine(output, picture + "-before.bgra"), before.Bgra);
                File.WriteAllBytes(Path.Combine(output, picture + "-after.bgra"), after.Bgra);
                exported[topic.Icon] = picture;
            }
        }
        catch (Exception e) { picture = null; error = e.Message; }
    }
    manifest.Add(new { slug, key, topic, picture, error });
    Console.WriteLine($"{slug}: {(picture != null ? "example" : error != null ? "ERROR " + error : "diagram needed")}");
}
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
var result = new { generatedUtc = DateTime.UtcNow, assemblyHash = Hash(typeof(ToolHelp).Assembly.Location),
    kernelAssemblyHash = Hash(typeof(Prism.Kernel.Occ).Assembly.Location), examples = exported.Count, pages = manifest };
File.WriteAllText(Path.Combine(output, "help-examples.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Exported {exported.Count} distinct before/after examples for {manifest.Count} pages.");
