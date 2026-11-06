using System.Runtime.InteropServices;
using Prism.Kernel;

// Run from the repository root, after building the native Release libraries.
NativeSearch.Initialize();
NativeLibrary.SetDllImportResolver(typeof(FemMesh).Assembly, (name, assembly, paths) =>
    NativeLibrary.Load(Path.GetFullPath($"native/build/Release/{name}.dll")));
int checks = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
void Reject<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, name); return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + name);
}

Reject<ArgumentException>(() => FemMesh.FromSurface(new float[10], 1), "partial triangles are rejected before P/Invoke");
using var shape = Occ.Box(2, 3, 4);
var display = shape.Tessellate();
var soup = display.Indices.SelectMany(i => display.Positions.Skip(3 * i).Take(3)).ToArray();
using var mesh = FemMesh.FromSurface(soup, 1);
Check(mesh.NodeCount > mesh.CornerCount && mesh.TetCount > 0, "Netgen produces a quadratic volume mesh");
Reject<ArgumentException>(() => mesh.SolveStatic(1000, .3, 1e-9, [0], [], null, [], [], [], [], null), "short fixture masks are rejected");
Reject<ArgumentException>(() => mesh.SolveStatic(1000, .3, 1e-9, [0], [7], null, [1], [1], [], [], null), "short force vectors are rejected");
Reject<ArgumentException>(() => mesh.SolveStatic(1000, .3, 1e-9, [0], [7], [0], [], [], [], [], null), "short prescribed values are rejected");
Reject<ArgumentException>(() => mesh.StartTopology(1000, .3, 1e-9, [0], [7], [0], [1], null, .5, 3, 1, 0), "short traction vectors are rejected");
Reject<ArgumentException>(() => mesh.StartTopology(1000, .3, 1e-9, [0], [7], [], [], [1], .5, 3, 1, 0), "short gravity vectors are rejected");
using var merged = FemMesh.Merge([mesh]);
Reject<InvalidOperationException>(() => merged.StartTopology(1000, .3, 1e-9, [0], [7], [], [], null, .5, 3, 1, 0), "merged mesh cannot dereference a null native handle");
using var topology = mesh.StartTopology(1000, .3, 1e-9, Enumerable.Range(0, mesh.CornerCount).ToArray(),
    Enumerable.Repeat(7, mesh.CornerCount).ToArray(), [], [], null, .5, 3, 1, 0);
topology.Step();
Check(double.IsFinite(topology.Compliance), "fully constrained topology produces finite results");
Parallel.For(0, 16, _ => topology.Dispose());
Check(true, "concurrent topology disposal completes safely");
Reject<ObjectDisposedException>(topology.Step, "disposed topology cannot reach native code");
mesh.Dispose();
Reject<InvalidOperationException>(() => mesh.StartTopology(1000, .3, 1e-9, [0], [7], [], [], null, .5, 3, 1, 0), "disposed mesh cannot start topology");
Console.WriteLine($"SUCCESS {checks} native boundary checks");

static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
