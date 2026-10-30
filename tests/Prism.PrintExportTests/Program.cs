using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Prism.Core;
using Prism.Core.Export;
using Prism.Kernel;
using Prism.Meshing;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
NativeSearch.Initialize();
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); checks++; }

var folder = Path.Combine(Path.GetTempPath(), "prism-print-export-" + Guid.NewGuid().ToString("N"));
try
{
    var s = new CadSession();
    s.AddObject(Occ.Box(20, 20, 10), "Base plate", ObjKind.Solid);
    s.AddObject(Occ.Box(5, 5, 5), "Base/plate", ObjKind.Solid);

    var one = PrintExport.Export(s, new PrintExportOptions(folder, "assembly", PrintFormat.ThreeMf, false, false, 1));
    Check(one.Count == 1 && File.Exists(one[0]) && one[0].EndsWith("assembly.3mf"), "one 3MF holds every visible body");
    Check(Directory.GetFiles(folder, "*.tmp*").Length == 0, "no temporary file is left behind");
    Check(MeshIO.Load(one[0]).TriCount > 0, "the 3MF reads back as a mesh");

    var again = PrintExport.Export(s, new PrintExportOptions(folder, "assembly", PrintFormat.ThreeMf, false, false, 1));
    Check(again[0] == one[0], "exporting again rewrites the same file, so a slicer can reload from disk");

    var many = PrintExport.Export(s, new PrintExportOptions(folder, "x", PrintFormat.Stl, false, true, 1));
    Check(many.Count == 2 && many.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2 && many.All(f => f.EndsWith(".stl") && File.Exists(f)), "one STL per body with unique safe names");

    var coarse = PrintExport.Export(s, new PrintExportOptions(folder, "coarse", PrintFormat.Obj, false, false, 5));
    var fine = PrintExport.Export(s, new PrintExportOptions(folder, "fine", PrintFormat.Ply, false, false, .15));
    Check(File.Exists(coarse[0]) && File.Exists(fine[0]), "OBJ and PLY write");

    bool threw = false; try { PrintExport.Export(new CadSession(), new PrintExportOptions(folder, "none", PrintFormat.Stl, false, false, 1)); } catch (InvalidOperationException) { threw = true; }
    Check(threw, "an empty document reports nothing to export");
    Check(PrintExport.FindSlicer(4, Path.Combine(folder, "missing.exe")) == null, "a missing custom slicer is reported, not launched");
}
finally { try { Directory.Delete(folder, true); } catch { } }
Console.WriteLine($"SUCCESS: {checks} checks");
static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
