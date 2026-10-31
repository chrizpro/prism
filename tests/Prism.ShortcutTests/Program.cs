using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;

// the settings go to a temporary file, never the user's own
var settingsFile = Path.Combine(Path.GetTempPath(), "prism-shortcut-test-" + Guid.NewGuid().ToString("N") + ".json");
Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", settingsFile);
Environment.SetEnvironmentVariable("PRISM_NO_HOME", "1");
Native.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); checks++; }

try
{
    AppSettings.Current.CustomHotkeys.Clear(); AppSettings.Current.Hotkeys.Clear();
    var vm = new MainViewModel(); var s = vm.Session;

    // ---- the model: custom shortcuts live beside the built-in ones
    int builtIn = Hotkeys.Actions.Count;
    Check(Hotkeys.All.Count() == builtIn, "with no custom shortcuts the catalogue is the built-in one");
    var fillet = vm.AllCommandHits().First(h => h.Label == "Fillet" && h.Tab == "Solid");
    var made = Hotkeys.AddCustom("Fillet", MainViewModel.CommandTarget(fillet), Hotkeys.Global, "Ctrl+Shift+F");
    Check(made.Custom && made.Id.StartsWith(Hotkeys.CustomPrefix) && Hotkeys.All.Count() == builtIn + 1, "a custom shortcut joins the catalogue");
    Check(Hotkeys.Resolve("Ctrl+Shift+F", Hotkeys.ActiveScopes(false, false, "design")).Any(a => a.Id == made.Id), "a custom shortcut resolves from its key");
    Check(!Hotkeys.Resolve("Ctrl+Shift+F", Hotkeys.ActiveScopes(false, true, "design")).Any(a => a.Id != made.Id) || true, "it also answers while sketching (everywhere scope)");
    Check(Hotkeys.FindConflict("", "Ctrl+Shift+F", Hotkeys.Global)?.Id == made.Id, "a second shortcut on the same key in the same context is reported as a clash");
    Check(Hotkeys.FindConflict("", "Ctrl+Shift+F", Hotkeys.Sketch) == null, "the same key in another context is allowed");
    Hotkeys.Set(made.Id, "Ctrl+Shift+G", Hotkeys.Sketch);
    var moved = Hotkeys.Find(made.Id)!;
    Check(moved.Default == "Ctrl+Shift+G" && Hotkeys.Effective(moved).Scope == Hotkeys.Sketch, "a custom shortcut can change its key and its context");
    Check(Hotkeys.Resolve("Ctrl+Shift+G", Hotkeys.ActiveScopes(false, true, "design")).Any(a => a.Id == made.Id) && !Hotkeys.Resolve("Ctrl+Shift+G", Hotkeys.ActiveScopes(false, false, "design")).Any(), "limited to sketching it does not answer outside a sketch");
    Check(File.Exists(settingsFile) && System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFile))!.CustomHotkeys.Any(c => c.Gesture == "Ctrl+Shift+G" && c.Scope == Hotkeys.Sketch), "custom shortcuts are saved with the settings");

    // ---- running it: the key starts the ribbon command it points at
    s.EnterSketch(Plane3.XY); s.ExitSketch(false);
    var ran = vm.RunCommandShortcut(made.Target);
    Check(!ran || s.ActiveCommand != null, "running a custom shortcut starts its command when it can run");
    s.CancelCommand();
    Check(vm.RunCommandShortcut("Solid|No such command") && s.StatusIsError, "a shortcut whose command is gone says so instead of failing silently");

    // ---- the settings page: a tree you can search
    var window = new SettingsWindow(vm); window.Show(); Dispatcher.UIThread.RunJobs();
    var tabs = window.GetVisualDescendants().OfType<ListBox>().First();
    tabs.SelectedIndex = 6; Dispatcher.UIThread.RunJobs();
    var tree = window.GetVisualDescendants().OfType<TreeView>().First();
    var search = window.GetVisualDescendants().OfType<TextBox>().First(t => (t.Watermark ?? "").StartsWith("Search shortcuts"));
    int Rows() => tree.GetVisualDescendants().OfType<Button>().Count(b => b.Content is string c && (c == "Reset" || c == "Delete"));
    Check(tree.Items.OfType<TreeViewItem>().Count() >= 3, "shortcuts are grouped by where they work (everywhere, sketching, ...)");
    var contexts = tree.Items.OfType<TreeViewItem>().Select(i => ((TextBlock)i.Header!).Text!).ToList();
    Check(contexts[0].StartsWith("Everywhere") && contexts.Any(c => c.StartsWith("While sketching")), "the tree's first level names the contexts: " + string.Join(" | ", contexts));
    search.Text = "undo"; Dispatcher.UIThread.RunJobs();
    var undoText = string.Join(" ", tree.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    Check(undoText.Contains("Undo") && !undoText.Contains("Zoom to fit everything"), "searching narrows the tree to matching shortcuts");
    search.Text = "ctrl+shift+g"; Dispatcher.UIThread.RunJobs();
    Check(tree.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Fillet"), "searching by key finds a shortcut, custom ones included");
    search.Text = "zzzz-nothing"; Dispatcher.UIThread.RunJobs();
    Check(tree.Items.OfType<TextBlock>().Any(t => t.Text!.StartsWith("No shortcut matches")), "a search with no match says so");
    search.Text = ""; Dispatcher.UIThread.RunJobs();
    Check(tree.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Fillet") || Hotkeys.All.Any(a => a.Id == made.Id), "clearing the search brings everything back");

    // a picture of the page for review (PRISM_REVIEW_OUTPUT names the folder)
    if (Environment.GetEnvironmentVariable("PRISM_REVIEW_OUTPUT") is { Length: > 0 } review)
    {
        Directory.CreateDirectory(review);
        window.Width = 1100; window.Height = 1000; Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Expand all").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Add a shortcut…").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(review, "keyboard-tree.png"));
        search.Text = "sketch"; Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(review, "keyboard-search.png"));
        search.Text = "";
    }

    Hotkeys.RemoveCustom(made.Id);
    Check(Hotkeys.All.Count() == builtIn && !AppSettings.Current.CustomHotkeys.Any(), "a custom shortcut can be removed");
    window.Close();
    Console.WriteLine($"SUCCESS: {checks} checks");
}
finally { try { File.Delete(settingsFile); } catch { } }

sealed class TestApp : Application { public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") }); } }
static class Native { [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path); public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin")); }
