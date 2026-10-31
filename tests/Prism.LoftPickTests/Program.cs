using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
NativeSearch.Initialize();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }

Shape Rect(double z, double w) => Occ.Polyline([new Vec3(0, 0, z), new Vec3(w, 0, z), new Vec3(w, w, z), new Vec3(0, w, z)], true);
Shape Rails() => Occ.Compound([Occ.Line(new Vec3(0, 0, 0), new Vec3(0, 0, 20)), Occ.Line(new Vec3(10, 10, 0), new Vec3(12, 12, 20))]);

LoftCommand Start(CadSession s) { var c = new LoftCommand { Factory = () => new LoftCommand() }; c.Begin(s); return c; }
PickResult Hit(SceneObject o, int edge) => new(new SelItem(o.Id, SelKind.Edge, edge), Vec3.Zero, Vec3.UnitZ, 0);
void Pick(LoftCommand c, int slot, SceneObject o, int edge, bool single = false)
{
    foreach (var sl in c.Slots) sl.IsActive = false;
    c.Slots[slot].IsActive = true;
    c.PickPoint(Hit(o, edge), single);
}

// separate objects: one chain click per profile
var s = new CadSession();
var a = s.AddObject(Rect(0, 10), "A", ObjKind.Curve); var b = s.AddObject(Rect(20, 12), "B", ObjKind.Curve);
var c1 = Start(s);
Pick(c1, 0, a, 0); Pick(c1, 0, b, 0);
Check(c1.Slots[0].Items.Count == 8, "one click on each rectangle takes all four edges (chain)");
var out1 = c1.TryBuild();
Check(out1 != null, "loft of two chained profiles builds: " + c1.Error);

// guide rails in a separate object
var r = s.AddObject(Rails(), "Rails", ObjKind.Curve);
Pick(c1, 1, r, 0); Pick(c1, 1, r, 1);
var out2 = c1.TryBuild();
Check(out2 != null || c1.Error != null, "loft with rails either builds or reports an error (no crash): " + c1.Error);

// everything in ONE object, rails touching the profile corners
var one = s.AddObject(Occ.Compound([Rect(0, 10), Rect(20, 12), Occ.Line(new Vec3(0, 0, 0), new Vec3(0, 0, 20)), Occ.Line(new Vec3(10, 10, 0), new Vec3(12, 12, 20))]), "Sketch", ObjKind.Curve);
int edges = one.Shape.Count(SubKind.Edge);
Console.WriteLine("edges in one object: " + edges);
var c2 = Start(s);
Pick(c2, 0, one, 0); Pick(c2, 0, one, 4);
Console.WriteLine("section items: " + c2.Slots[0].Items.Count);
var out3 = c2.TryBuild();
Console.WriteLine("one-object loft: " + (out3 != null ? "built" : c2.Error));
Check(true, "single-object picking did not throw");
Console.WriteLine("done");

static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
