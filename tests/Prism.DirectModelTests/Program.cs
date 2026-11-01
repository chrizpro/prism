using System.Runtime.InteropServices;
using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
NativeSearch.Initialize();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
double[] identity = [1, 0, 0, 0, 1, 0, 0, 0, 1];

FeatureTests.Run(Check);
FaceTests.Run(Check);
PatternEditTests.Run(Check);
FaceOpsTests.Run(Check);
RepairMoveTests.Run(Check);
FaceOpsGapTests.Run(Check);

// a plate with a 10 x 5 grid of 6 mm through holes
var s = new CadSession();
Shape plate = Occ.Box(200, 150, 10); var lo = plate.Props().Min;
for (int i = 0; i < 50; i++)
{
    using var cyl = Occ.Cylinder(3, 12); using var at = Occ.Transform(cyl, identity, new Vec3(lo.X + 10 + (i % 10) * 20, lo.Y + 15 + (i / 10) * 24, lo.Z - 1));
    plate = Occ.Cut(plate, at);
}
var body = s.AddObject(plate, "Plate", ObjKind.Solid);
int faces = body.Shape.Count(SubKind.Face);
int firstHole = Enumerable.Range(0, faces).First(f => Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, f)).Type == "Cylinder");
Console.WriteLine($"plate has {faces} faces; first hole face {firstHole}");

double Time(string label, Func<CommandOutput?> run, out CommandOutput? o)
{
    var sw = Stopwatch.StartNew(); o = run(); sw.Stop();
    Console.WriteLine($"{label,-44} {sw.ElapsedMilliseconds,6} ms");
    return sw.Elapsed.TotalMilliseconds;
}
T Cmd<T>(T c, Action<T> set) where T : Command
{
    c.Begin(s); c.ConfigureInputs(() => { c.Slots[0].Items.Clear(); c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, firstHole)); set(c); }); return c;
}
void Scope(Command c, int i) => c.Params.OfType<ChoiceParam>().Single(p => p.Label == "Also edit").Index = i;
void Num(Command c, string l, double v) => c.Params.OfType<NumberParam>().Single(p => p.Label == l).Value = v;

double plateVol = body.Shape.Props().Volume, holeVol = Math.PI * 9 * 10;
Shape Result(Command c) => c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "No result");
int Holes(Shape sh) { int n = 0; for (int f = 0; f < sh.Count(SubKind.Face); f++) if (Occ.FaceInfoOf(sh.Sub(SubKind.Face, f)).Type == "Cylinder") n++; return n; }

foreach (int scope in new[] { 0, 0, 1, 2 })
{
    var mh = Cmd(new MoveHoleCommand(), c => { Scope(c, scope); Num(c, "Move Y", 2); });
    Time($"Move Hole  scope {scope}", () => mh.TryBuild(), out var o);
    var del = Cmd(new DeleteFaceCommand(), c => Scope(c, scope));
    Time($"Delete Face scope {scope}", () => del.TryBuild(), out var d);
    var pp = Cmd(new PushPullCommand(), c => { Scope(c, scope); Num(c, "Distance (positive pulls out, negative pushes in)", 1); });
    Time($"Push/Pull   scope {scope}", () => pp.TryBuild(), out var p);
    int n = scope == 0 ? 1 : 50;
    var ms = o!.Replace.Single().New; var ds = d!.Replace.Single().New; var ps = p!.Replace.Single().New;
    Check(ms.IsValid && Math.Abs(ms.Props().Volume - plateVol) < 1 && Holes(ms) == Holes(body.Shape), $"Move Hole scope {scope}: valid, same volume, same number of holes");
    Check(ds.IsValid && Math.Abs(ds.Props().Volume - (plateVol + n * holeVol)) < 1 && Holes(ds) == 50 - n, $"Delete Face scope {scope}: {n} hole(s) filled");
    Check(ps.IsValid && Math.Abs(ps.Props().Volume - (plateVol + n * Math.PI * 5 * 10)) < 1, $"Push/Pull scope {scope}: {n} hole(s) shrunk by 1 mm");
}
// the first hole's neighbours: the pattern scope must not touch a stray hole
var stray = new CadSession(); Shape sp = Occ.Box(200, 150, 10); var slo = sp.Props().Min;
foreach (var (x, y) in new[] { (20, 20), (40, 20), (60, 20), (90, 70) })
{
    using var cyl = Occ.Cylinder(3, 12); using var at = Occ.Transform(cyl, identity, new Vec3(slo.X + x, slo.Y + y, slo.Z - 1)); sp = Occ.Cut(sp, at);
}
var sb = stray.AddObject(sp, "Stray", ObjKind.Solid);
int sFirst = Enumerable.Range(0, sb.Shape.Count(SubKind.Face)).First(f => Occ.FaceInfoOf(sb.Shape.Sub(SubKind.Face, f)).Type == "Cylinder");
var cmd = new DeleteFaceCommand(); cmd.Begin(stray);
cmd.ConfigureInputs(() => { cmd.Slots[0].Items.Clear(); cmd.Slots[0].TryAdd(new SelItem(sb.Id, SelKind.Face, sFirst)); Scope(cmd, 2); });
var r = cmd.TryBuild()!.Replace.Single().New;
Check(Holes(r) == 1 || Holes(r) == 2, "pattern scope leaves a stray hole alone (" + Holes(r) + " holes remain)");

static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
