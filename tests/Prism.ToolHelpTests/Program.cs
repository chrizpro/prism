using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Commands;
using Prism.Controls;
using Prism.Help;
using Prism.Rendering;
using Prism.ViewModels;
using Prism.Views;

NativeSearch.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var vm = new MainViewModel { HomeOpen = false };
var session = vm.Session;
// Every tool on the CAE, CAM and Analyze tabs (including disabled ones) explains itself with a rendered before/after example.
var covered = new HashSet<string>();
foreach (var tab in vm.Tabs.Where(t => new[] { "Static", "Thermal", "Dynamics", "Fluids", "Subtractive", "3D FDM", "3D Resin", "Analyze" }.Contains(t.Title)))
    foreach (var button in tab.Buttons.Where(b => !b.HasMenu || b.IsSplit))
    {
        string? icon = button.ToolHelpIcon;
        Check(icon != null && ToolHelp.Find(icon) != null, tab.Title + ": " + button.Label.Replace((char)10, (char)32) + " has help");
        if (icon != null) covered.Add(icon);
    }
foreach (string label in new[] { "Orthographic", "Perspective", "X-ray", "Isolate", "Section View", "Zebra stripes", "Clear Section" })
{
    var button = vm.Tabs.Single(t => t.Title == "View").Buttons.First(b => b.Label.Replace((char)10, (char)32) == label);
    Check(button.ToolHelpIcon != null, "View: " + label + " has help");
    covered.Add(button.ToolHelpIcon!);
}
foreach (string icon in covered.Order())
{
    var topic = ToolHelp.Find(icon)!;
    var example = ToolHelp.Example(topic);
    if (Environment.GetEnvironmentVariable("HELP_PNG_DIR") is { Length: > 0 } pngDir)
    {
        Directory.CreateDirectory(pngDir);
        foreach (var (name, source) in new[] { ("before", example.Before), ("after", example.After) })
        { var pixels = StudioPreview.Render(source, true); PngWriter.Save(Path.Combine(pngDir, icon + "-" + name + ".png"), pixels.Bgra, pixels.Width, pixels.Height); }
    }
    Check(example.Before.Mesh.TriangleCount + example.Before.Mesh.SegmentCount > 0 && example.After.Mesh.TriangleCount + example.After.Mesh.SegmentCount > 0, icon + " example has geometry");
    foreach (bool light in new[] { false, true })
        Check(StudioPreview.Render(example.After, light).Bgra.Distinct().Count() > 12 && StudioPreview.Render(example.Before, light).Bgra.Distinct().Count() > 12, icon + " renders in " + (light ? "light" : "dark"));
}
var eye = session.Camera.EyeDirection;
bool dirty = session.Dirty, undo = session.CanUndo;
foreach (string icon in new[] { "extrude", "loft", "sweep", "extrude-surface", "loft-surface", "sweep-surface", "revolve", "revolve-surface", "fillet", "chamfer" })
{
    var example = ToolHelp.Example(ToolHelp.Find(icon)!);
    Check(example.Before.Mesh.SegmentCount > 0 && example.After.Mesh.TriangleCount > 0, icon + " has input curves and output faces");
    if (icon is "fillet" or "chamfer")
        Check(StudioPreview.Render(example.Before, false).HighlightPixels > 0, icon + " shows its selected edge");
    foreach (bool light in new[] { false, true })
        Check(StudioPreview.Render(example.After, light).Bgra.Distinct().Count() > 30, icon + " renders in " + (light ? "light" : "dark"));
}
Check(session.Objects.Count == 0 && session.Dirty == dirty && session.CanUndo == undo && session.Camera.EyeDirection == eye, "examples preserve document, history and camera");
Check(ToolHelp.ForCommand(new SweepTwoRailsCommand()) == null, "two-rail sweep does not inherit one-path instructions");
foreach (string icon in new[] { "union", "subtract", "intersect", "shell", "hole", "mirror", "pattern-linear", "pattern-circular" })
{
    var example = ToolHelp.Example(ToolHelp.Find(icon)!);
    Check(example.Before.Mesh.TriangleCount > 0 && example.After.Mesh.TriangleCount > 0, icon + " has input and result geometry");
    foreach (bool light in new[] { false, true })
        Check(StudioPreview.Render(example.After, light).Bgra.Distinct().Count() > 30, icon + " renders in " + (light ? "light" : "dark"));
}
foreach (var (cmd, icon) in new (Command, string)[] { (new BooleanCommand(BooleanCommand.Kind.Union), "union"), (new BooleanCommand(BooleanCommand.Kind.Subtract), "subtract"), (new BooleanCommand(BooleanCommand.Kind.Intersect), "intersect"), (new ShellCommand(), "shell"), (new HoleCommand(), "hole"), (new TransformCommand(TransformCommand.Kind.Mirror), "mirror"), (new BoxCommand(), "box") })
    Check(ToolHelp.ForCommand(cmd)?.Icon == icon, cmd.Title + " maps to its own help topic");
foreach (string icon in new[] { "cnc-profile", "cnc-pocket", "sim-fixed", "sim-force", "sim-pressure" })
{
    var example = ToolHelp.Example(ToolHelp.Find(icon)!);
    Check(StudioPreview.Render(example.After, false).HighlightPixels > 0, icon + " displays the generated cutting moves or selected face");
}
Check(vm.Tabs.Single(t => t.Title == "Static").Buttons.Any(b => b.ExampleIcon == "sim-fixed") && vm.Tabs.Single(t => t.Title == "Static").Buttons.Any(b => b.ExampleIcon == "sim-force"), "CAE ribbon exposes the correct examples");
Check(vm.Tabs.Single(t => t.Title == "Subtractive").Buttons.Any(b => b.ExampleIcon == "cnc-profile") && vm.Tabs.Single(t => t.Title == "Subtractive").Buttons.Any(b => b.ExampleIcon == "cnc-pocket"), "CAM ribbon distinguishes Profile and Pocket");
var view = new MainView { DataContext = vm };
var window = new Window { Width = 1280, Height = 800, Content = view };
window.Show();
Dispatcher.UIThread.RunJobs();
void Pump(int milliseconds)
{
    using var stop = new CancellationTokenSource(milliseconds);
    Dispatcher.UIThread.MainLoop(stop.Token);
    Dispatcher.UIThread.RunJobs();
}
vm.SelectTab(vm.Tabs.Single(t => t.Title == "Surface"));
Dispatcher.UIThread.RunJobs();
var ribbon = view.GetVisualDescendants().OfType<Prism.Ribbon.RibbonView>().Single();
var popup = ribbon.FindControl<Popup>("ToolHoverPopup")!;
popup.ShouldUseOverlayLayer = true;
var output = Path.GetFullPath("artifacts/tool-help-review");
Directory.CreateDirectory(output);
Button ToolButton(string icon) => ribbon.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is Prism.Ribbon.RibbonButton rb && rb.Icon == icon && !rb.HasMenu);
Point Center(Control button) => button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
var disabledLoft = ToolButton("loft-surface");
var disabledModel = (Prism.Ribbon.RibbonButton)disabledLoft.DataContext!;
disabledModel.IsEnabled = false;
window.MouseMove(Center(disabledLoft)); Pump(650);
Check(popup.IsOpen && !disabledLoft.IsEnabled && session.ActiveCommand == null, "disabled ribbon tool shows help without starting a command");
window.MouseDown(Center(disabledLoft), MouseButton.Left); window.MouseUp(Center(disabledLoft), MouseButton.Left); Pump(50);
Check(session.ActiveCommand == null, "clicking the disabled ribbon tool does not start it");
window.MouseMove(new Point(300, 690)); Pump(800);
disabledModel.IsEnabled = true;
int invokedDisabled = 0;
var menuVariant = new Prism.Ribbon.RibbonButton { Label = "Unavailable option", Icon = "revolve-surface", Description = "A disabled menu option.", CanExecute = () => false, Execute = () => invokedDisabled++ };
var menuButton = new Prism.Ribbon.RibbonButton { Label = "Options", Icon = "revolve-surface", Menu = [menuVariant] };
var menuTab = new Prism.Ribbon.RibbonTab { Title = "Help test", Groups = [new Prism.Ribbon.RibbonGroup { Title = "Tools", Items = [menuButton] }] };
vm.Tabs.Add(menuTab); vm.SelectTab(menuTab); Pump(100);
var menuAnchor = ribbon.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.DataContext, menuButton));
menuAnchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(100);
var flyout = (MenuFlyout)typeof(Prism.Ribbon.RibbonView).GetField("_activeToolMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ribbon)!;
var disabledItem = ((System.Collections.IEnumerable)flyout.ItemsSource!).Cast<MenuItem>().Single();
var menuRoot = TopLevel.GetTopLevel(disabledItem)!;
var menuPoint = disabledItem.TranslatePoint(new Point(disabledItem.Bounds.Width / 2, disabledItem.Bounds.Height / 2), menuRoot)!.Value;
menuRoot.MouseMove(menuPoint); Pump(650);
Check(popup.IsOpen && !menuVariant.IsEnabled && invokedDisabled == 0, "disabled dropdown option shows help without becoming executable");
disabledItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
Check(invokedDisabled == 0, "activating an unavailable dropdown option does not execute it");
flyout.Hide(); Pump(100);
vm.Tabs.Remove(menuTab); vm.SelectTab(vm.Tabs.Single(t => t.Title == "Surface")); Pump(100);
void WaitForImages()
{
    var previews = popup.Child!.GetVisualDescendants().OfType<GeometryPreview>().ToArray();
    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (previews.Any(p => !p.HasRenderedImage) && DateTime.UtcNow < deadline) Pump(25);
    Check(previews.Length == 2 && previews.All(p => p.HasRenderedImage), "tooltip displays both rendered example images");
}
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
{
    Application.Current!.RequestedThemeVariant = theme;
    window.MouseMove(new Point(300, 690)); Pump(800);
    var point = Center(ToolButton("loft-surface"));
    window.MouseMove(point); Pump(650);
    Check(popup.IsOpen && !vm.HasActiveAssistance && session.ActiveCommand == null, "hover opens local tooltip without viewport assistance or activating a tool");
    Check(popup.Placement == PlacementMode.Pointer, "hover help uses pointer placement");
    WaitForImages();
    var position = popup.Child!.TranslatePoint(new Point(), window)!.Value;
    Check(Math.Abs(position.X - point.X) < 50 && Math.Abs(position.Y - point.Y) < 60, "rich tooltip appears beside the hovered pointer");
    using var shot = window.CaptureRenderedFrame();
    shot?.Save(Path.Combine(output, "loft-tooltip-" + theme.ToString().ToLowerInvariant() + ".png"));
}
var learnMore = popup.Child!.GetVisualDescendants().OfType<Button>().Single();
string? savedDocsRoot = Prism.Core.AppSettings.Current.ToolDocumentationBaseUrl;
Prism.Core.AppSettings.Current.ToolDocumentationBaseUrl = null;
// Learn More opens the published tool reference at the tool's own row; a button with no ribbon cannot be placed there.
Check(ToolDocumentation.LinkFor("x|Fillet", "Solid", "Fillet")?.AbsoluteUri == Prism.Core.AppInfo.DocsUrl + "tools/solid#tool-fillet", "Learn More links to the tool's row on its ribbon page");
Check(ToolDocumentation.LinkFor("x|Fillet") == null, "no ribbon, no link");
window.MouseMove(new Point(300, 690)); Pump(800);
Check(!popup.IsOpen, "tooltip dismisses on leaving");
window.MouseMove(Center(ToolButton("to-nurbs"))); Pump(650);
Check(popup.IsOpen && !popup.Child!.GetVisualDescendants().OfType<GeometryPreview>().Any() && popup.Child!.GetVisualDescendants().OfType<Button>().Any(), "tools without examples retain a local tooltip and Learn More");
Prism.Core.AppSettings.Current.ToolDocumentationBaseUrl = "https://example.test/prism/tools";
Check(ToolDocumentation.LinkFor("sweep|Sweep 2 rails")?.AbsoluteUri == "https://example.test/prism/tools/sweep%7CSweep%202%20rails", "online links preserve tool identity");
Prism.Core.AppSettings.Current.ToolDocumentationBaseUrl = savedDocsRoot;
ribbon.DismissExample();
window.MouseMove(new Point(300, 690)); Pump(800);
var profile = session.AddObject(Prism.Kernel.Occ.Circle(new(0, 0, 0), new(0, 0, 1), 4), "Profile", Prism.Core.ObjKind.Curve);
var path = session.AddObject(Prism.Kernel.Occ.Line(new(0, 0, 0), new(0, 0, 24)), "Path", Prism.Core.ObjKind.Curve);
session.StartCommand(() => new SweepCommand(true));
var command = session.ActiveCommand!;
Check(vm.ActiveToolInstruction.Contains("outline"), "live assistance initially requests the sweep outline");
command.Pick(Prism.Core.SelItem.Of(profile), false);
Check(vm.ActiveToolInstruction.Contains("path curve"), "live assistance advances to the path after choosing an outline");
foreach (var slot in command.Slots) slot.IsActive = slot == command.Slots[1];
command.Pick(Prism.Core.SelItem.Of(path), false);
Check(session.Preview.Count > 0 && vm.ActiveToolInstruction.Contains("Review the preview"), "live assistance advances to reviewing the completed sweep");
var liveText = vm.ActiveToolInstruction;
var context = view.FindControl<Border>("ViewportContext")!;
Check(context.IsVisible && context.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right && context.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Top, "active assistance lives in the viewport top-right corner");
window.MouseMove(Center(ToolButton("loft-surface"))); Pump(650);
Check(popup.IsOpen && vm.ActiveToolInstruction == liveText && ReferenceEquals(session.ActiveCommand, command), "hovering another tool does not replace live instructions or the current command");
WaitForImages();
using (var shot = window.CaptureRenderedFrame()) shot?.Save(Path.Combine(output, "hover-with-active-tool.png"));
view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
Check(!popup.IsOpen && ReferenceEquals(session.ActiveCommand, command), "Escape dismisses tooltip before canceling the tool");
window.MouseMove(new Point(300, 690)); Pump(800);
vm.HoveredToolHelpIcon = null;
view.GetVisualDescendants().OfType<CommandPanel>().Single().GetVisualDescendants().OfType<Button>()
    .Single(b => Equals(b.Content, "Show tool example")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(popup.IsOpen && ReferenceEquals(session.ActiveCommand, command), "command panel example uses the tooltip without replacing the active tool");
ribbon.DismissExample();
session.CancelCommandPanel();
Check(!vm.HasActiveAssistance, "ending the tool clears live instructions");
window.Close();
Console.WriteLine($"SUCCESS: {checks} checks. Screenshots: {output}");

class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") });
        RequestedThemeVariant = ThemeVariant.Dark;
    }
}
static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
