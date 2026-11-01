using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Prism.Core;
using Prism.Kernel;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
NativeSearch.Initialize();
string path = args.Length > 0 ? args[0] : Generate();
string Generate()
{
    double[] id = [1, 0, 0, 0, 1, 0, 0, 0, 1]; var parts = new List<Shape>();
    Shape plate = Occ.Box(200, 150, 10); var lo = plate.Props().Min; var tools = new List<Shape>();
    for (int i = 0; i < 50; i++) { using var c = Occ.Cylinder(3, 12); tools.Add(Occ.Transform(c, id, new Vec3(lo.X + 10 + (i % 10) * 20, lo.Y + 15 + (i / 10) * 24, lo.Z - 1))); }
    plate = Occ.Cut(plate, Occ.Compound(tools));
    for (int p = 0; p < 60; p++) parts.Add(Occ.Translate(plate, new Vec3(0, 0, p * 20)));
    var f = Path.Combine(Path.GetTempPath(), "prism-import-stress.step"); Occ.ExportStep(parts, f); return f;
}
Console.WriteLine($"{Path.GetFileName(path)}  {new FileInfo(path).Length / 1048576.0:0.0} MB");
var sw = Stopwatch.StartNew();
using var shape = Occ.ImportStep(path);
Console.WriteLine($"kernel read + transfer      {sw.ElapsedMilliseconds,8} ms   {shape.Count(SubKind.Solid)} solids, {shape.Count(SubKind.Face)} faces");
sw.Restart();
var prep = CadSession.PrepareImport(path);
Console.WriteLine($"background read + parallel mesh {sw.ElapsedMilliseconds,8} ms   {prep.Parts.Count} parts, {prep.Parts.Sum(p => (long)(p.Mesh?.Indices.Length ?? 0) / 3):N0} triangles  ({Environment.ProcessorCount} cores)");
sw.Restart();
var s = new CadSession(); s.AddPrepared(prep, path);
Console.WriteLine($"add to scene (UI thread)        {sw.ElapsedMilliseconds,8} ms   {s.Objects.Count} objects");
static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
