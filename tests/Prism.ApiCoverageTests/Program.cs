using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Prism.Automation;
using Prism.Commands;
using Prism.Core;
using Prism.ViewModels;

Native.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();

// Every command the ribbon can start must also be startable by name through run_command.
var vm = new MainViewModel();
var ribbon = CommandCatalog.Keys().ToHashSet();           // taken before the automation layer registers its own
var api = AutomationApi.CommandKeys().ToHashSet();
var missing = ribbon.Where(k => !api.Contains(k)).Order().ToList();
Console.WriteLine($"ribbon commands: {ribbon.Count}, reachable by name: {ribbon.Count - missing.Count}");
foreach (var m in missing) Console.WriteLine("NOT REACHABLE " + m);
foreach (var tab in new[] { "Solid", "Sketch", "Sheet Metal", "Direct Modeling", "Subtractive", "Additive" })
    if (vm.Tabs.FirstOrDefault(t => t.Title == tab) is { } t) Console.WriteLine(tab + ": " + string.Join(" | ", t.Buttons.Select(b => b.Label.Replace("\n", " "))));
if (missing.Count > 0) { Console.WriteLine($"FAIL: {missing.Count} ribbon commands cannot be started through the API"); return 1; }
Console.WriteLine("SUCCESS");
return 0;

sealed class TestApp : Application { public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") }); } }
static class Native { [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path); public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin")); }
