using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Prism.Automation;
using Prism.Core;
using Prism.ViewModels;

Native.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); checks++; }

var vm = new MainViewModel();
var api = new AutomationApi(vm, null);
JsonNode Call(string tool, string json)
{
    var t = api.CallAsync(tool, (JsonObject)JsonNode.Parse(json)!);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (!t.IsCompleted && sw.Elapsed.TotalSeconds < 60) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    if (!t.IsCompleted) throw new TimeoutException(tool);
    return t.GetAwaiter().GetResult()!;
}

var box = Call("run_command", """{"name":"box","params":{"Width":40,"Depth":30,"Height":20}}""");
var name = ((JsonObject)box["created"]![0]!)["name"]!.ToString();
Check(((JsonObject)box["created"]![0]!)["faces"]!.GetValue<int>() == 6, "box has six faces: " + name);

var top = Call("solid_find", $$"""{"object":"{{name}}","facing":[0,0,1]}""");
Check(top["total"]!.GetValue<int>() == 1 && top["hits"]![0]!["type"]!.ToString() == "Plane", "facing +Z finds exactly the top face");
int topFace = top["hits"]![0]!["face"]!.GetValue<int>();
var area = top["hits"]![0]!["area"]!.GetValue<double>();
Check(Math.Abs(area - 1200) < 1, $"top face area is length x width ({area})");

var sides = Call("solid_find", $$"""{"object":"{{name}}","kind":"edges","of_face":{{topFace}}}""");
Check(sides["total"]!.GetValue<int>() == 4, "a box top face is bounded by four edges");
var vertical = Call("solid_find", $$"""{"object":"{{name}}","kind":"edges","direction":[0,0,1],"summary":true}""");
Check(vertical["total"]!.GetValue<int>() == 4 && vertical["shown"] == null, "four vertical edges, summary lists no hits");
var longest = Call("solid_find", $$"""{"object":"{{name}}","kind":"edges","sort":"-size","limit":1}""");
Check(Math.Abs(longest["hits"]![0]!["length"]!.GetValue<double>() - 40) < 1e-6, "sort by -size puts the 40 mm edge first");

var sel = Call("solid_find", $$"""{"object":"{{name}}","kind":"edges","of_face":{{topFace}},"select":true,"summary":true}""");
Check(sel["selected"]!.GetValue<int>() == 4 && vm.Session.Selection.Count == 4, "select:true selects the hits");

var edge = Call("select", $$"""{"items":[{"object":"{{name}}","edge_at":[0,0,20]}]}""");
Check(edge.ToJsonString().Contains("Edge") || edge.ToJsonString().Contains("edge"), "edge_at selects an edge near a point: " + edge.ToJsonString());

var fil = Call("run_command", $$$"""{"name":"fillet","slots":{"Edges":[{"object":"{{{name}}}","edge_at":[20,0,20]}]},"params":{"Radius":3}}""");
var ch = (JsonObject)fil["changed"]![0]!;
Check(ch["volumeDelta"]!.GetValue<double>() < 0 && ch["valid"]!.GetValue<bool>(), "fillet result reports a volume decrease and validity");
Check(ch["faces"]!.ToString() == "6->7", "fillet result reports face count change " + ch["faces"]);

var cyl = Call("solid_find", $$"""{"object":"{{name}}","type":"Cylinder"}""");
Check(cyl["total"]!.GetValue<int>() == 1 && Math.Abs(cyl["hits"]![0]!["radius"]!.GetValue<double>() - 3) < 1e-6, "type filter finds the fillet face with radius 3");

var chk = Call("solid_check", $$"""{"object":"{{name}}"}""");
Check(chk["valid"]!.GetValue<bool>() && chk["volume"]!.GetValue<double>() > 0, "solid_check reports validity and volume");
var dist = Call("solid_check", $$$"""{"object":"{{{name}}}","a":{"object":"{{{name}}}","face":{{{topFace}}}},"b":{"object":"{{{name}}}","face":1}}""");
Check(dist["distance"] != null, "solid_check measures a distance between two faces");

try { Call("run_command", $$$"""{"name":"fillet","slots":{"Edges":[{"object":"{{{name}}}","edge":1}]},"params":{"Radius":500}}"""); throw new Exception("FAIL empty fillet must fail"); }
catch (Exception e) when (!e.Message.StartsWith("FAIL")) { Check(e.Message.Length > 0, "failure message is explanatory: " + e.Message[..Math.Min(120, e.Message.Length)]); }

// a revolved cylinder: its two caps must report opposite outward normals (a left-handed cap plane once reported both as +Z)
using var halfSection = Prism.Kernel.Occ.Polyline([new(0, 0, 0), new(20, 0, 0), new(20, 0, 30), new(0, 0, 30)], true);
using var revolved = Prism.Kernel.Occ.Revolve(halfSection, new(0, 0, 0), new(0, 0, 1), 360);
var spun = vm.Session.AddObject(revolved, "Spun", Prism.Core.ObjKind.Solid);
var capsUp = Call("solid_find", "{\"object\":\"Spun 1\",\"facing\":[0,0,1]}");
var capsDown = Call("solid_find", "{\"object\":\"Spun 1\",\"facing\":[0,0,-1]}");
Check(capsUp["total"]!.GetValue<int>() == 1 && Math.Abs(capsUp["hits"]![0]!["center"]![2]!.GetValue<double>() - 30) < 1e-6, "a revolved cylinder has exactly one cap facing up, at the top");
Check(capsDown["total"]!.GetValue<int>() == 1 && Math.Abs(capsDown["hits"]![0]!["center"]![2]!.GetValue<double>()) < 1e-6, "a revolved cylinder has exactly one cap facing down, at the bottom");

Console.WriteLine($"SUCCESS: {checks} checks");

sealed class TestApp : Application { public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") }); } }
static class Native { [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path); public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin")); }
