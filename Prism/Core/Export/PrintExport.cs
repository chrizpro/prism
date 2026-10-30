using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Prism.Meshing;

namespace Prism.Core.Export;

public enum PrintFormat { ThreeMf, Stl, Obj, Ply }

public sealed record PrintExportOptions(string Folder, string Name, PrintFormat Format, bool OnlySelection, bool PerBody, double Tolerance);

/// <summary>Writes bodies to a folder for a slicer and finds or launches the slicer. Prism does no slicing itself.</summary>
public static class PrintExport
{
    public static readonly string[] FormatNames = ["3MF", "STL", "OBJ", "PLY"];
    public static readonly string[] QualityNames = ["Draft", "Standard", "Fine", "Very fine"];
    /// <summary>Tessellation tolerance relative to the standard print tolerance (smaller is finer).</summary>
    public static readonly double[] QualityScale = [5, 1, .4, .15];
    public static readonly string[] SlicerNames = ["Save only", "Bambu Studio", "OrcaSlicer", "PrusaSlicer", "Other program"];

    public static string Extension(PrintFormat f) => f switch { PrintFormat.Stl => ".stl", PrintFormat.Obj => ".obj", PrintFormat.Ply => ".ply", _ => ".3mf" };

    /// <summary>Export and return the files written. Throws when there is nothing to export.</summary>
    public static List<string> Export(CadSession s, PrintExportOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Folder)) throw new InvalidOperationException("Choose a folder to export to.");
        var src = o.OnlySelection && s.SelectedObjects.Any() ? s.SelectedObjects.ToList() : s.Objects.Where(x => x.Visible).ToList();
        var parts = src.Select(x => (name: x.Name, mesh: s.PrintMesh(x, o.Tolerance))).Where(p => p.mesh != null && p.mesh.TriCount > 0).ToList();
        if (parts.Count == 0) throw new InvalidOperationException("Nothing to export.");
        Directory.CreateDirectory(o.Folder);
        var ext = Extension(o.Format);
        var baseName = Safe(string.IsNullOrWhiteSpace(o.Name) ? "part" : o.Name);
        var written = new List<string>();
        if (o.PerBody)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parts)
            {
                var stem = Safe(p.name); var unique = stem; int n = 2;
                while (!used.Add(unique)) unique = stem + "-" + n++;
                var path = Path.Combine(o.Folder, unique + ext);
                Write([p.mesh!], [p.name], path); written.Add(path);
            }
        }
        else
        {
            var path = Path.Combine(o.Folder, baseName + ext);
            Write(parts.Select(p => p.mesh!).ToList(), parts.Select(p => p.name).ToList(), path); written.Add(path);
        }
        return written;
    }

    static void Write(IReadOnlyList<TriMesh> meshes, IReadOnlyList<string> names, string path)
    {
        // Write to a temporary file first so a slicer reloading from disk never reads a half-written file.
        var tmp = path + ".tmp" + Path.GetExtension(path);
        if (Path.GetExtension(path).Equals(".3mf", StringComparison.OrdinalIgnoreCase)) MeshIO.Save3mf(meshes, tmp, names);
        else MeshIO.Save(meshes.Count == 1 ? meshes[0] : TriMesh.Merge(meshes), tmp);
        File.Move(tmp, path, true);
    }

    static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var t = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return t.Length == 0 ? "part" : t;
    }

    /// <summary>The slicer's executable if it is in a usual install place (or the user's own path), else null.</summary>
    public static string? FindSlicer(int choice, string custom)
    {
        if (choice == 4) return File.Exists(custom) ? custom : null;
        string[] candidates = choice switch
        {
            1 => ["Bambu Studio\\bambu-studio.exe"],
            2 => ["OrcaSlicer\\orca-slicer.exe"],
            3 => ["Prusa3D\\PrusaSlicer\\prusa-slicer.exe"],
            _ => [],
        };
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") };
        return candidates.SelectMany(c => roots.Select(r => Path.Combine(r, c))).FirstOrDefault(File.Exists);
    }

    public static bool IsRunning(string exe) => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Length > 0;

    /// <summary>Open the files in the slicer. A slicer that is already open takes them as new objects or a new window, never in place: the user reloads from disk.</summary>
    public static void Launch(string exe, IEnumerable<string> files)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var f in files) info.ArgumentList.Add(f);
        Process.Start(info);
    }
}
