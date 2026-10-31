using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;

NativeSearch.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool valid, string description) { if (!valid) throw new Exception(description); checks++; Console.WriteLine("PASS " + description); }
string output = Path.GetFullPath("docs/sketch-panel-workflows"); Directory.CreateDirectory(output);
var vm = new MainViewModel(); var s = vm.Session; s.EnterSketch(Plane3.XY, alignView: false);
var sketch = s.Sketch!;
var panel = new SketchPanel { DataContext = vm }; var host = new Window { Width = 394, Height = 960, Content = panel };
host.Show(); Dispatcher.UIThread.RunJobs();
void SetTool(SketchTool tool) { sketch.SetTool(s, tool); Dispatcher.UIThread.RunJobs(); }
void Move(SketchTool tool, double u, double v)
{
    var point = s.Camera.Project(sketch.Plane.PointAt(u, v))!.Value;
    tool.PointerMove(s, new PointerInfo(point.x, point.y, false, false, false, false));
    s.RaiseState(); Dispatcher.UIThread.RunJobs();
}
void Capture(string name)
{
    foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
    {
        Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize(394, 960)); bitmap.Render(panel);
        bitmap.Save(Path.Combine(output, name + "-" + theme.Key.ToString()!.ToLowerInvariant() + ".png"));
    }
}
var inventory = new List<object>(); var gallery = new List<string>(); int variants = 0;
var types = typeof(SketchTool).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(SketchTool).IsAssignableFrom(t)).OrderBy(t => t.Name).ToList();
foreach (var type in types)
{
    var constructor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
    var parameters = constructor.GetParameters();
    CType[] constraintKinds = [CType.Horizontal, CType.Vertical, CType.Coincident, CType.Parallel, CType.Perpendicular, CType.Collinear, CType.Equal, CType.Tangent, CType.Concentric, CType.Midpoint, CType.Symmetric, CType.Fixed];
    int seeds = type == typeof(ConstraintTool) ? constraintKinds.Length : parameters.Any(p => p.ParameterType == typeof(bool)) ? 2 : 1;
    for (int seed = 0; seed < seeds; seed++)
    {
        var toolArguments = parameters.Select(p => p.ParameterType == typeof(bool) ? (object)(seed == 1) : p.ParameterType == typeof(int) ? 0 : p.ParameterType == typeof(CType) ? constraintKinds[seed] : p.ParameterType.IsEnum ? Enum.GetValues(p.ParameterType).GetValue(0)! : p.HasDefaultValue ? p.DefaultValue : throw new Exception("Add an explicit factory for " + type.Name)).ToArray();
        var tool = (SketchTool)constructor.Invoke(toolArguments);
        SetTool(tool);
        int count = Math.Max(1, tool.Options.Count);
        for (int mode = 0; mode < count; mode++)
        {
            if (tool.Options.Count > 0) { tool.SetOption(s, mode); Dispatcher.UIThread.RunJobs(); }
            string label = type.Name + "/" + seed + "/" + mode;
            Check(vm.HasToolPanel && vm.ToolSections.Count > 0 && vm.ToolPanelTitle.Length > 0, label + " has a visible workflow");
            Check(vm.ToolSections.Select(g => g.Number).SequenceEqual(Enumerable.Range(1, vm.ToolSections.Count).Select(n => n.ToString())), label + " has consecutive steps");
            Check(vm.ToolSections.SelectMany(g => g.Controls).Count() == vm.ToolPanel.Count && vm.ToolSections.SelectMany(g => g.Controls).Distinct().Count() == vm.ToolPanel.Count, label + " places every control exactly once");
            Check(vm.ToolSections.SelectMany(g => g.Options).Count() == tool.Options.Count, label + " retains every method");
            // Select's own Geometry step is the sketch panel's Geometry block (3D sketch, Edit Geometry, Gizmo); its instructions are that block's tooltip
            if (tool is not SelectTool) Check(vm.ToolSections.Single(g => g.Title == tool.PanelInputTitle).Guidance == tool.Prompt, label + " shows the current geometry instructions");
            else Check(vm.ToolSections.Single().Title == "Sketch tool", label + " shows only its Sketch tool step; Geometry lives in the sketch panel");
            string file = Regex.Replace(type.Name + "-" + seed + "-" + mode, "[^a-zA-Z0-9-]", "").ToLowerInvariant();
            Capture(file);
            inventory.Add(new { Tool = type.Name, tool.Name, tool.Mode, Seed = seed, Option = mode, Sections = vm.ToolSections.Select(g => g.Title).ToArray(), Controls = vm.ToolPanel.Select(r => r.Id).ToArray(), Screenshot = file });
            gallery.Add($"| {tool.PanelTitle} | {tool.Mode} | [Dark]({file}-dark.png) | [Light]({file}-light.png) |");
            variants++;
        }
    }
}
File.WriteAllText(Path.Combine(output, "inventory.json"), JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(Path.Combine(output, "gallery.md"), "# All sketch-tool panels\n\n| Tool | Mode | Dark | Light |\n| --- | --- | --- | --- |\n" + string.Join("\n", gallery) + "\n");

var rectangle = new RectTool(RectMode.TwoCorner); SetTool(rectangle); rectangle.Feed(s, 10, 10); Move(rectangle, 50, 35); s.RaiseState(); Dispatcher.UIThread.RunJobs();
Check(vm.ToolSections.Select(g => g.Title).SequenceEqual(new[] { "Method", "Placement and dimensions", "Settings", "Place geometry" }), "rectangle follows method, placement, settings, then placement action");
Check(vm.ToolPanel.Any(r => r.Label == "Width") && vm.ToolPanel.Any(r => r.Label == "Height"), "rectangle exposes real width and height fields after its anchor");
var width = vm.ToolPanel.Single(r => r.Label == "Width"); var height = vm.ToolPanel.Single(r => r.Label == "Height");
width.IsEditing = true; width.NumberText = "40.25"; var group = vm.ToolSections.First(); group.IsExpanded = false;
s.RaiseState(); Dispatcher.UIThread.RunJobs();
Check(ReferenceEquals(width, vm.ToolPanel.Single(r => r.Label == "Width")) && width.NumberText == "40.25", "live refresh preserves field identity and in-progress text");
Check(!group.IsExpanded && ReferenceEquals(group, vm.ToolSections.First()), "tool refresh preserves collapsed sections"); group.IsExpanded = true;
width.NumberText = "NaN"; Check(!vm.CommitToolNumber(width) && width.HasError, "non-finite input gives inline feedback");
vm.SetToolControl("place", null);
Check(!vm.ToolPanel.Single(r => r.Id == "place").CanRun && sketch.Model.Entities.Count == 0, "an invalid draft disables placement and cannot create geometry from stale values");
width.NumberText = "40"; Check(vm.CommitToolNumber(width) && !width.HasError, "valid input clears feedback"); width.IsEditing = false;
height.NumberText = "25"; Check(vm.CommitToolNumber(height), "rectangle height commits through the tool");
Capture("rectangle-dimensions");
vm.SetToolControl("place", null);
Check(sketch.Model.Entities.Count(e => e.Type == EntType.Line) == 4, "shared placement action creates an actual rectangle");

var ellipse = new EllipseTool(EllipseMode.Center); SetTool(ellipse); ellipse.Feed(s, 80, 40); Move(ellipse, 110, 40); s.RaiseState(); Dispatcher.UIThread.RunJobs();
Check(vm.ToolPanel.Any(r => r.Label == "Major radius") && vm.ToolPanel.Any(r => r.Label == "Angle"), "ellipse exposes major radius and angle");
var lockRow = vm.ToolPanel.Single(r => r.Id == "circle"); lockRow.On = true; Dispatcher.UIThread.RunJobs();
Check(ellipse.CircleLocked && vm.ToolPanel.Any(r => r.Label == "Radius"), "ellipse lock control updates geometry mode and labels");
lockRow.On = false; Dispatcher.UIThread.RunJobs(); Capture("ellipse-dimensions");
var stale = width; string before = stale.NumberText; stale.NumberText = "99";
Check(!vm.CommitToolNumber(stale), "a stale field cannot change a different active tool");

sketch.SelEntities.Clear(); foreach (var e in sketch.Model.Entities.Where(e => e.Type == EntType.Line)) sketch.SelEntities.Add(e.Id);
var offset = new OffsetTool(); SetTool(offset);
Check(vm.ToolSections.Select(g => g.Title).SequenceEqual(new[] { "Select curves", "Distance and side", "Apply offset" }), "offset follows selection, distance/side, then apply");
vm.SetToolControl("selection", null); vm.SetToolControl("dist", 3.0); Dispatcher.UIThread.RunJobs();
Check(vm.ToolPanel.Single(r => r.Id == "apply").Enabled, "offset action enables only after selecting curves and entering a distance");
Capture("offset-selected"); int original = sketch.Model.Entities.Count; vm.SetToolControl("apply", null);
Check(sketch.Model.Entities.Count > original, "shared offset action creates real offset geometry");

host.Width = 312; Dispatcher.UIThread.RunJobs();
foreach (SketchTool tool in new SketchTool[] { new RectTool(RectMode.TwoCorner), new EllipseTool(EllipseMode.Center), new OffsetTool(), new SketchPatternTool(false), new ProjectTool(), new DimensionTool(DimMode.Smart) })
{
    SetTool(tool);
    Check(panel.GetVisualDescendants().OfType<TextBox>().Where(x => x.IsEffectivelyVisible).All(x => x.TranslatePoint(default, panel) is { } p && p.X >= 0 && p.X + x.Bounds.Width <= panel.Bounds.Width + 1), tool.Name + " fields fit a 312px dock");
}
host.Close();
Console.WriteLine($"SUCCESS: {checks} checks; {types.Count} tool types; {variants} variants; screenshots in {output}");
sealed class TestApp : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") }); }
}
static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
