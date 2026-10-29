using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;
using Prism.ViewModels;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
NativeSearch.Initialize();
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); checks++; }
void Number(HoleCommand c, string label, double value) => c.Params.OfType<NumberParam>().Single(p => p.Label == label).Value = value;
void Choice(HoleCommand c, string label, int index) => c.Params.OfType<ChoiceParam>().Single(p => p.Label == label).Index = index;
HoleCommand Hole(CadSession s, SelItem pick, Action<HoleCommand>? configure = null)
{
    var c = new HoleCommand { Factory = () => new HoleCommand() }; c.Begin(s);
    c.ConfigureInputs(() => { c.Slots[0].Items.Clear(); c.Slots[0].TryAdd(pick); configure?.Invoke(c); });
    return c;
}
Shape Result(HoleCommand c) => c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "No result");
var s = new CadSession(); var block = s.AddObject(Occ.Box(20, 20, 12), "Block", ObjKind.Solid);
double volume = block.Shape.Props().Volume;
var plain = Hole(s, SelItem.Of(block));
using var through = Result(plain);
Check(Math.Abs(volume - through.Props().Volume - Math.PI * 9 * 12) < .001, "plain through-hole removes a cylinder through the whole body");
var blind = Hole(s, SelItem.Of(block), c => { Choice(c, "Extent", 1); Number(c, "Hole depth", 5); });
using var blindShape = Result(blind);
Check(Math.Abs(volume - blindShape.Props().Volume - Math.PI * 9 * 5) < .001, "blind hole stops at its requested depth");
var offset = Hole(s, SelItem.Of(block), c => Number(c, "Center offset U", 4));
using var offsetShape = Result(offset);
Check(offsetShape.IsValid && offsetShape.Props().Volume < volume, "offset placement cuts valid geometry");
var miss = Hole(s, SelItem.Of(block), c => Number(c, "Center offset U", 100));
Check(miss.TryBuild() == null && miss.Error!.Contains("intersect"), "a hole that misses the body is rejected");
var reversed = Hole(s, SelItem.Of(block), c => { Choice(c, "Extent", 1); Number(c, "Hole depth", 3); c.Params.OfType<BoolParam>().Single(p => p.Label == "Reverse direction").Value = true; });
using var reversedShape = Result(reversed);
Check(Math.Abs(volume - reversedShape.Props().Volume - Math.PI * 9 * 3) < .001, "reversing a body selection drills from its opposite side");
var side = Hole(s, SelItem.Of(block), c => Choice(c, "Direction", 2));
using var sideShape = Result(side);
Check(Math.Abs(volume - sideShape.Props().Volume - Math.PI * 9 * 20) < .001, "world-X direction cuts across the body");
int topFace = Enumerable.Range(0, block.Shape.Count(SubKind.Face)).MaxBy(i => block.Shape.Sub(SubKind.Face, i).Props().Center.Z);
var fromFace = Hole(s, new(block.Id, SelKind.Face, topFace), c => { Choice(c, "Extent", 1); Number(c, "Hole depth", 4); });
using var faceShape = Result(fromFace);
Check(Math.Abs(volume - faceShape.Props().Volume - Math.PI * 9 * 4) < .001, "entry face normal points the hole into the body");
var tilted = s.AddObject(Occ.Rotate(block.Shape, Vec3.Zero, Vec3.UnitY, 37), "Tilted", ObjKind.Solid);
int tiltedFace = Enumerable.Range(0, tilted.Shape.Count(SubKind.Face)).MaxBy(i => Vec3.Dot(s.FaceFrame(new(tilted.Id, SelKind.Face, i))!.Value.normal, new Vec3(Math.Sin(37 * Math.PI / 180), 0, Math.Cos(37 * Math.PI / 180))));
var tiltHole = Hole(s, new(tilted.Id, SelKind.Face, tiltedFace), c => { Choice(c, "Extent", 1); Number(c, "Hole depth", 4); });
using var tiltShape = Result(tiltHole);
Check(Math.Abs(volume - tiltShape.Props().Volume - Math.PI * 9 * 4) < .01, "rotated entry faces use a correctly placed drilling frame");
var shallowTilt = s.AddObject(Occ.Rotate(block.Shape, Vec3.Zero, Vec3.UnitY, 15), "Shallow tilt", ObjKind.Solid);
int shallowFace = Enumerable.Range(0, shallowTilt.Shape.Count(SubKind.Face)).MaxBy(i => s.FaceFrame(new(shallowTilt.Id, SelKind.Face, i))!.Value.normal.Z);
var shallowHole = Hole(s, new(shallowTilt.Id, SelKind.Face, shallowFace), c => { Choice(c, "Extent", 1); Number(c, "Hole depth", 4); });
using var shallowShape = Result(shallowHole);
Check(Math.Abs(volume - shallowShape.Props().Volume - Math.PI * 9 * 4) < .01, "near-Z tilted faces retain an orthonormal drilling frame");
var threaded = Hole(s, SelItem.Of(block), c => { Choice(c, "Hole type", 1); Choice(c, "Extent", 1); Number(c, "Hole depth", 6); });
using var threadShape = Result(threaded);
double minor = 6 - 1.082532;
Check(threadShape.IsValid && threadShape.HasSolids, "modeled right-hand thread produces a valid solid");
Check(volume - threadShape.Props().Volume > Math.PI * minor * minor / 4 * 6 + .1, "helical grooves remove additional material beyond the minor-diameter bore");
Check(threadShape.Count(SubKind.Face) > blindShape.Count(SubKind.Face), "thread is physical face geometry");
var partial = Hole(s, SelItem.Of(block), c => { Choice(c, "Hole type", 1); Choice(c, "Extent", 1); Number(c, "Hole depth", 6); Number(c, "Thread depth (0 = full hole)", 3); });
using var partialShape = Result(partial);
Check(partialShape.Props().Volume > threadShape.Props().Volume, "partial thread depth leaves an unthreaded bore below it");
var left = Hole(s, SelItem.Of(block), c => { Choice(c, "Hole type", 1); Choice(c, "Extent", 1); Number(c, "Hole depth", 6); c.Params.OfType<BoolParam>().Single(p => p.Label == "Left-hand thread").Value = true; });
using var leftShape = Result(left);
Check(leftShape.IsValid && Math.Abs(leftShape.Props().Volume - threadShape.Props().Volume) < .05, "left-hand thread produces the matching opposite helix");
var invalid = Hole(s, SelItem.Of(block), c => { Choice(c, "Hole type", 1); Number(c, "Thread pitch", 10); });
Check(invalid.TryBuild() == null && invalid.Error!.Contains("pitch"), "impossible thread pitch is rejected");
var tooDeep = Hole(s, SelItem.Of(block), c => { Choice(c, "Hole type", 1); Choice(c, "Extent", 1); Number(c, "Hole depth", 3); Number(c, "Thread depth (0 = full hole)", 5); });
Check(tooDeep.TryBuild() == null, "thread depth cannot exceed hole depth");
var preset = Hole(s, SelItem.Of(block), c => { Choice(c, "Hole type", 1); Choice(c, "Metric size", 5); });
Check(preset.Params.OfType<NumberParam>().Single(p => p.Label == "Diameter").Value == 8 && preset.Params.OfType<NumberParam>().Single(p => p.Label == "Thread pitch").Value == 1.25, "M8 preset sets diameter and pitch together");
Check(preset.Params.OfType<ChoiceParam>().Single(p => p.Label == "Metric size").Index == 5, "preset remains selected while applying both dimensions");
preset.ConfigureInputs(() => Number(preset, "Diameter", 8.2));
Check(preset.Params.OfType<ChoiceParam>().Single(p => p.Label == "Metric size").Index == 0, "editing preset dimensions switches to Custom");
var thin = s.AddObject(Occ.Box(10,10,3), "Thin block", ObjKind.Solid);
var m3 = Hole(s, SelItem.Of(thin), c => { Choice(c, "Hole type", 1); Choice(c, "Metric size", 1); });
using var m3Shape = Result(m3);
Check(m3Shape.IsValid && m3Shape.HasSolids && m3Shape.Props().Volume < thin.Shape.Props().Volume, "M3 through-all threads cut a valid solid at a smaller scale");
var form = s.AddObject(null, "Closed form", ObjKind.Form, form: FormOps.Box(20,20,20,2,2,2));
var formHole = Hole(s, SelItem.Of(form)); using var formShape = Result(formHole);
Check(formShape.IsValid && formShape.HasSolids && formShape.Props().Volume < form.Shape.Props().Volume, "closed freeform objects accept holes through their exact geometry");
var surface = s.AddObject(Occ.Profile([Occ.Polyline([new(-10,-10,0), new(10,-10,0), new(10,10,0), new(-10,10,0)], true)], true), "Surface", ObjKind.Surface);
var surfaceHole = Hole(s, SelItem.Of(surface)); using var openSurface = Result(surfaceHole);
Check(openSurface.IsValid && Math.Abs(surface.Shape.Props().Area - openSurface.Props().Area - Math.PI * 9) < .001, "plain holes also trim exact surfaces");
var surfaceThread = Hole(s, SelItem.Of(surface), c => Choice(c, "Hole type", 1));
Check(surfaceThread.TryBuild() == null && surfaceThread.Error!.Contains("closed solid"), "threading an open surface gives an actionable error");
var replay = new HoleCommand(); replay.Begin(s); replay.Restore(threaded.Capture()!);
using var replayShape = Result(replay);
Check(Math.Abs(replayShape.Props().Volume - threadShape.Props().Volume) < .001, "thread settings and face/body references replay from a feature recipe");
s.StartCommand(() => new HoleCommand()); var commit = (HoleCommand)s.ActiveCommand!;
commit.ConfigureInputs(() => commit.Slots[0].TryAdd(SelItem.Of(block))); s.CommitCommand();
Check(s.Find(block.Id)!.Shape.Props().Volume < volume && s.Current.Recipe != null, "commit preserves body identity and records an editable feature");
s.Undo(); Check(Math.Abs(s.Find(block.Id)!.Shape.Props().Volume - volume) < .001, "undo restores the original body");
s.Redo(); Check(s.Find(block.Id)!.Shape.Props().Volume < volume, "redo restores the hole");
Check(s.EditFeature(s.Current), "committed hole opens in the feature editor");
var editing = (HoleCommand)s.ActiveCommand!; editing.ConfigureInputs(() => Number(editing, "Diameter", 4)); s.CommitCommand();
Check(Math.Abs(volume - s.Find(block.Id)!.Shape.Props().Volume - Math.PI * 4 * 12) < .001, "editing the hole rebuilds its diameter against the original body");
var folder = Path.Combine(Path.GetTempPath(), "Prism-hole-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
try
{
    string step = Path.Combine(folder, "thread.step"); Occ.ExportStep([threadShape], step);
    using var imported = Occ.ImportStep(step);
    Check(imported.IsValid && imported.Count(SubKind.Face) >= threadShape.Count(SubKind.Face) && Math.Abs(imported.Props().Volume - threadShape.Props().Volume) / volume < 5e-6, "modeled thread geometry survives STEP exchange within five parts per million");
    var save = new CadSession(); save.AddObject(threadShape, "Threaded", ObjKind.Solid); string file = Path.Combine(folder, "thread.prism"); save.Save(file);
    var opened = new CadSession(); opened.Open(file);
    Check(opened.Objects.Single().Shape.IsValid && Math.Abs(opened.Objects.Single().Shape.Props().Volume - threadShape.Props().Volume) < .001, "thread geometry survives document save/reopen");
}
finally { Directory.Delete(folder, true); }
var vm = new MainViewModel();
Check(vm.Tabs.Where(t => t.Title is "Home" or "Solid").All(t => t.Buttons.Any(b => b.Label == "Hole")), "Hole is available in both Home and Solid ribbons");
foreach (var c in new[] { plain, blind, offset, miss, reversed, side, fromFace, tiltHole, shallowHole, threaded, partial, left, invalid, tooDeep, preset, m3, formHole, surfaceHole, surfaceThread, replay }) c.OnEnd();
Console.WriteLine($"SUCCESS: {checks} checks");
static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
