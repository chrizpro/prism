using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;

Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", Path.Combine(Path.GetTempPath(), "prism-browser-test-" + Guid.NewGuid().ToString("N") + ".json"));
Environment.SetEnvironmentVariable("PRISM_NO_HOME", "1");
Native.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); checks++; }

var vm = new MainViewModel(); var s = vm.Session;
var made = s.AddObject(Occ.Box(10, 10, 10), "Bracket", ObjKind.Solid); string original = made.Name;
vm.RebuildBrowser(); Dispatcher.UIThread.RunJobs();
var nodes = vm.AllBrowserNodes().ToList();
var body = nodes.First(n => n.IsObject && n.Title == original);
Check(body.CanRename, "a body can be renamed");
Check(nodes.Where(n => n.Kind is NodeKind.Origin or NodeKind.Folder).All(n => !n.CanRename), "the Origin, its planes and axes, and the folders cannot be renamed");
body.BeginRename();
Check(body.IsEditing && body.EditText == original, "starting a rename opens the name for editing");
body.EditText = "Mounting bracket"; body.CommitRename(); Dispatcher.UIThread.RunJobs();
Check(s.Objects.Any(o => o.Name == "Mounting bracket") && vm.AllBrowserNodes().Any(n => n.Title == "Mounting bracket"), "committing a rename changes the object and the tree");
s.Undo(); Dispatcher.UIThread.RunJobs();
Check(s.Objects.Any(o => o.Name == original), "a rename can be undone");
var again = vm.AllBrowserNodes().First(n => n.IsObject && n.Title == original);
again.BeginRename(); again.EditText = "   "; again.CommitRename(); Dispatcher.UIThread.RunJobs();
Check(s.Objects.Any(o => o.Name == original), "an empty name is ignored");
Console.WriteLine($"SUCCESS: {checks} checks");

sealed class TestApp : Application { public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") }); } }
static class Native { [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path); public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin")); }
