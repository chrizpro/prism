using System;
using System.IO;
using System.Linq;
using Prism.Core.Export;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Further file formats: BREP and IGES (exact geometry), glTF (for viewers and games), DXF (2D drawings and laser/CNC profiles).</summary>
public sealed partial class CadSession
{
    /// <summary>Every format Import File understands, as file-picker patterns.</summary>
    public static readonly string[] ImportPatterns = ["*.step", "*.stp", "*.iges", "*.igs", "*.brep", "*.dxf", "*.stl", "*.obj", "*.ply", "*.3mf", "*.xyz", "*.pts", "*.xyzn", "*.csv"];

    /// <summary>Import by file extension: STEP, IGES, BREP, DXF (as a sketch) and the mesh formats.</summary>
    public void ImportFile(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".step" or ".stp": ImportStep(path); break;
            case ".iges" or ".igs": ImportIges(path); break;
            case ".brep": ImportBrep(path); break;
            case ".dxf": ImportDxf(path); break;
            case ".xyz" or ".pts" or ".xyzn" or ".csv": Commands.PointCloudCommands.TryBegin(this, path); break;
            case ".stl" or ".obj" or ".ply" or ".3mf": if (!Commands.PointCloudCommands.TryBegin(this, path)) ImportMesh(path); break;
            default: throw new NotSupportedException($"{Path.GetExtension(path)} files cannot be imported. Supported: STEP, IGES, BREP, DXF, STL, OBJ, PLY, 3MF.");
        }
    }

    public void ImportIges(string path) => AddImported(Occ.ImportIges(path), path);

    public void ImportBrep(string path) => AddImported(Occ.FromBrep(File.ReadAllText(path)), path);

    public void ExportBrep(string path)
    {
        var shapes = Objects.Where(o => o.Visible && o.HasBrep && !o.IsAnnotation && o.Kind != ObjKind.Sketch).Select(o => o.Shape).ToList();
        if (shapes.Count == 0) throw new InvalidOperationException("Nothing to export.");
        File.WriteAllText(path, (shapes.Count == 1 ? shapes[0] : Occ.Compound(shapes)).ToBrep());
        Say($"Exported {Path.GetFileName(path)}");
    }

    public void ExportIges(string path)
    {
        var shapes = Objects.Where(o => o.Visible && o.HasBrep && !o.IsAnnotation && o.Kind != ObjKind.Sketch).Select(o => o.Shape).ToList();
        if (shapes.Count == 0) throw new InvalidOperationException("Nothing to export (meshes can be exported as STL / 3MF).");
        Occ.ExportIges(shapes, path); Say($"Exported {Path.GetFileName(path)}");
    }

    /// <summary>Binary glTF of the selection (or every visible body): Y-up, metres, one flat-colour material per body.</summary>
    public void ExportGlb(string path)
    {
        var src = SelectedObjects.Any() ? SelectedObjects.ToList() : Objects.Where(o => o.Visible).ToList();
        var parts = src.Where(o => !o.IsAnnotation).Select(o => (o, m: PrintMesh(o))).Where(x => x.m != null && x.m.TriCount > 0).ToList();
        if (parts.Count == 0) throw new InvalidOperationException("Nothing to export.");
        Gltf.SaveGlb(path, parts.Select(p => (p.o.Name, p.m!, p.o.Color)).ToList());
        Say($"Exported {parts.Count} object(s) to {Path.GetFileName(path)}");
    }

    /// <summary>The selected sketches (or every visible one) as DXF LINE / CIRCLE / ARC entities in millimetres, each drawing plane on its own layer.</summary>
    public void ExportDxf(string path)
    {
        var src = SelectedObjects.Where(o => o.SketchData != null).ToList();
        if (src.Count == 0) src = Objects.Where(o => o.Visible && o.SketchData != null).ToList();
        if (src.Count == 0) throw new InvalidOperationException("DXF export needs a sketch. Draw one (or select it) first.");
        var layers = new System.Collections.Generic.List<(string, SketchModel)>();
        foreach (var o in src)
        {
            int plane = 0;
            foreach (var (model, _) in o.SketchData!.DrawingModels(o.SketchPlane ?? Plane3.XY))
                layers.Add((plane++ == 0 ? o.Name : $"{o.Name}_{plane}", model));
        }
        Dxf.SaveSketches(path, layers);
        Say($"Exported {layers.Count} drawing plane(s) to {Path.GetFileName(path)}");
    }

    /// <summary>Bring a DXF in as a sketch on the XY plane (lines, circles, arcs and polylines).</summary>
    public void ImportDxf(string path)
    {
        var model = new SketchModel();
        int n = Dxf.LoadSketch(path, model, out string note);
        if (n == 0) throw new InvalidDataException("That DXF has no lines, circles, arcs or polylines to import." + (note.Length > 0 ? " " + note : ""));
        var shapes = model.ToShapes(Plane3.XY);
        var compound = Occ.Profile(shapes, faces: false);
        var obj = AddObject(compound, Path.GetFileNameWithoutExtension(path) is { Length: > 0 } name ? name : "Sketch", ObjKind.Sketch, plane: Plane3.XY, sketch: model);
        _selection.Clear(); _selection.Add(SelItem.Of(obj)); SelectionChanged?.Invoke(); ZoomExtents();
        Say($"Imported {n} curve(s) from {Path.GetFileName(path)} as a sketch." + (note.Length > 0 ? " " + note : ""));
    }
}
