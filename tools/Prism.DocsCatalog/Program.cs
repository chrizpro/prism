using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Prism.Core;
using Prism.Ribbon;
using Prism.ViewModels;

string root = Path.GetFullPath(args.FirstOrDefault(a => a != "--check") ?? ".");
string[] inputs = ["Prism/ViewModels/MainViewModel.cs", "Prism/ViewModels/MainViewModel.RibbonCustom.cs", "Prism/ViewModels/MainViewModel.Workspaces.cs", "Prism/Ribbon/RibbonLayoutPolicy.cs"];
string sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", inputs.Select(p => File.ReadAllText(Path.Combine(root, p)).Replace("\r\n", "\n"))))));
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
AppSettings.Current.Ribbon = new RibbonLayout();
AppSettings.Current.LastPicks.Clear();
var vm = new MainViewModel();
object Tool(RibbonButton b) => new { id = b.Id, label = b.Label.Replace("\n", " "), description = b.Description, shortcut = b.Shortcut };
var catalog = new
{
    sourceHash,
    workspaces = MainViewModel.WorkspaceDefs.Where(w => w.Id != "all").Select(w => new { w.Id, w.Name, w.Tip, w.Tabs }),
    tabs = vm.Tabs.Select(t => new { title = t.Title, contextual = t.IsContextual, groups = t.Groups.Select(g => new { title = g.Title,
        tools = g.Items.OfType<RibbonButton>().SelectMany(b => b.Menu == null ? new[] { Tool(b) } : b.Menu.Select(Tool)) }) })
};
string json = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
string target = Path.Combine(root, "site/public/ribbon-catalog.json");
if (args.Contains("--check"))
{
    if (!File.Exists(target) || File.ReadAllText(target).Replace("\r\n", "\n") != json)
        throw new InvalidOperationException("The website tool catalog is stale. Run: dotnet run --project tools/Prism.DocsCatalog -- .");
    Console.WriteLine("Website catalog matches the shipped ribbons.");
}
else
{
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllText(target, json);
    Console.WriteLine("Generated " + target);
}
