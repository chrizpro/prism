using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Copy and paste of whole objects, and the copy that Alt-drag with the Move tool leaves behind.</summary>
public sealed partial class CadSession
{
    // shared by every open document, so a part can be pasted into another tab
    static List<SceneObject> _clipboard = [];

    static bool Copyable(SceneObject o) => !o.IsAnnotation && !o.IsReference && o.Asm == null;

    List<SceneObject> CopyableSelection() => _selection.Select(i => Find(i.Obj)).Where(o => o != null && Copyable(o!)).Select(o => o!).Distinct().ToList();

    /// <summary>True for an object Copy, Paste and Alt-drag can copy (not annotations, reference parts or assembly components).</summary>
    public bool IsCopyable(Guid id) => Find(id) is { } o && Copyable(o);

    // Pieces of a model (selected faces or edges) copied instead of whole objects: pasted as a new surface or curve (owner direction 2026-11-16)
    static List<(Shape Shape, string Name, ObjKind Kind)> _clipboardParts = [];
    // selected triangles of meshes, pasted as new meshes (one per source mesh)
    static List<(Meshing.TriMesh Mesh, string Name, uint Color)> _clipboardMeshes = [];

    List<(Meshing.TriMesh, string, uint)> CopyMeshFaces()
    {
        var res = new List<(Meshing.TriMesh, string, uint)>();
        foreach (var group in _selection.Where(i => i.Kind == SelKind.MeshFace).GroupBy(i => i.Obj))
        {
            if (Find(group.Key) is not { Tri: { } tri } source) continue;
            var tris = group.Select(i => i.Index).Where(t => t >= 0 && t < tri.TriCount).Distinct().ToList();
            if (tris.Count == 0) continue;
            var idx = new int[tris.Count * 3];
            for (int k = 0; k < tris.Count; k++) for (int c = 0; c < 3; c++) idx[3 * k + c] = tri.T[3 * tris[k] + c];
            res.Add((Meshing.MeshOps.Compact((float[])tri.V.Clone(), idx), $"{source.Name} piece", source.Color));
        }
        return res;
    }

    public bool CanCopy => ActiveCommand == null && (Sketch != null ? Sketch.SelEntities.Count > 0 : CopyableSelection().Count > 0 || _selection.Any(i => i.Kind is SelKind.Face or SelKind.Edge or SelKind.Vertex or SelKind.MeshFace));
    public bool CanPaste => ActiveCommand == null && (Sketch != null ? _sketchClip != null : _clipboard.Count > 0 || _clipboardParts.Count > 0 || _clipboardMeshes.Count > 0);

    /// <summary>
    /// The selected faces and edges of solids, surfaces and curves, one new object per source object: a face copy is a surface (the faces of one body stay
    /// sewn together where they touch), an edge copy is a curve. Freeform cages and meshes are not handled here.
    /// </summary>
    List<(Shape Shape, string Name, ObjKind Kind)> CopyComponents()
    {
        var parts = new List<(Shape, string, ObjKind)>();
        foreach (var group in _selection.Where(i => i.Kind is SelKind.Face or SelKind.Edge or SelKind.Vertex).GroupBy(i => i.Obj))
        {
            if (Find(group.Key) is not { Tri: null, Form: null, HasBrep: true } source) continue;
            foreach (var kind in new[] { SelKind.Face, SelKind.Edge, SelKind.Vertex })
            {
                var items = group.Where(i => i.Kind == kind).Distinct().ToList();
                if (items.Count == 0) continue;
                var shapes = items.Select(i => ShapeOf(i)).OfType<Shape>().ToList();
                if (shapes.Count == 0) continue;
                // vertices paste as points (a curve object holding points, as Points Along Curve makes)
                Shape joined = shapes.Count == 1 ? Occ.Copy(shapes[0]) : kind == SelKind.Face ? Occ.Sew(shapes, 0.01, false) : kind == SelKind.Vertex ? Occ.Compound(shapes) : Occ.Profile(shapes, false);
                parts.Add((joined, kind == SelKind.Face ? $"{source.Name} face" : kind == SelKind.Vertex ? $"{source.Name} points" : $"{source.Name} edge", kind == SelKind.Face ? ObjKind.Surface : ObjKind.Curve));
            }
        }
        return parts;
    }

    /// <summary>Remember the selected objects, or the selected faces and edges. They are copied now, so later edits or deleting the originals change nothing.</summary>
    public bool CopySelection()
    {
        if (Sketch != null) return CopySketchSelection();
        var parts = CopyComponents(); var meshes = CopyMeshFaces();
        if (meshes.Count > 0 && parts.Count == 0)
        {
            _clipboardMeshes = meshes; _clipboardParts = []; _clipboard = [];
            Say($"Copied {meshes.Sum(m => m.Item1.TriCount)} triangle(s). Paste with Ctrl+V to get them as a new mesh.");
            return true;
        }
        if (parts.Count > 0)
        {
            _clipboardParts = parts; _clipboard = []; _clipboardMeshes = [];
            Say($"Copied {parts.Count} piece(s) of the model. Paste with Ctrl+V to get them as new {(parts.All(p => p.Kind == ObjKind.Surface) ? "surfaces" : parts.All(p => p.Kind == ObjKind.Curve) ? "curves" : "surfaces and curves")}.");
            return true;
        }
        var objs = CopyableSelection();
        if (objs.Count == 0) { Say("Select an object, face or edge to copy.", true); return false; }
        _clipboard = objs.Select(o => CloneObject(o, o.Name)).ToList(); _clipboardParts = []; _clipboardMeshes = [];
        Say($"Copied {objs.Count} object(s). Paste with Ctrl+V.");
        return true;
    }

    /// <summary>Add copies of what was copied, in the same place, and select them so they can be moved away.</summary>
    public List<SceneObject> Paste()
    {
        if (Sketch != null) { if (_sketchClip != null) PasteSketch(); else Say("Copy sketch curves first.", true); return []; }
        if (_clipboard.Count == 0 && _clipboardParts.Count == 0 && _clipboardMeshes.Count == 0) { Say("Nothing has been copied.", true); return []; }
        if (_clipboardMeshes.Count > 0)
        {
            Checkpoint("Paste", "duplicate");
            var made = _clipboardMeshes.Select(m => new SceneObject(Guid.NewGuid(), NextName(m.Name), null, ObjKind.Mesh, m.Color, true, null, null, null, null, m.Mesh)).ToList();
            Objects = Objects.AddRange(made);
            _selection.Clear(); foreach (var c in made) _selection.Add(SelItem.Of(c));
            RaiseScene(); SelectionChanged?.Invoke();
            Say($"Pasted {made.Count} mesh piece(s) where they were copied from. Move them with the gizmo.");
            return made;
        }
        if (_clipboardParts.Count > 0)
        {
            Checkpoint("Paste", "duplicate");
            var pieces = _clipboardParts.Select(p => AddObject(Occ.Copy(p.Shape), p.Name, p.Kind, checkpoint: false)).ToList();
            _selection.Clear(); foreach (var c in pieces) _selection.Add(SelItem.Of(c));
            RaiseScene(); SelectionChanged?.Invoke();
            Say($"Pasted {pieces.Count} new object(s) where they were copied from. Move them with the gizmo.");
            return pieces;
        }
        Checkpoint("Paste", "duplicate");
        var copies = new List<SceneObject>();
        foreach (var o in _clipboard)
        {
            var c = CloneObject(o, NextName(StripNumber(o.Name)));
            Objects = Objects.Add(c); copies.Add(c);
        }
        _selection.Clear(); foreach (var c in copies) _selection.Add(SelItem.Of(c));
        RaiseScene(); SelectionChanged?.Invoke();
        Say($"Pasted {copies.Count} object(s) where they were copied from. Move them with the gizmo.");
        return copies;
    }

    /// <summary>Copies of whole objects left in place (the originals stay put while the copies are dragged away). Null when none can be copied.</summary>
    public List<SceneObject>? CopyForDrag(IEnumerable<Guid> ids)
    {
        var objs = ids.Select(Find).Where(o => o != null && Copyable(o!)).Select(o => o!).ToList();
        if (objs.Count == 0) return null;
        Checkpoint("Copy", "duplicate");
        var copies = new List<SceneObject>();
        foreach (var o in objs)
        {
            var c = CloneObject(o, NextName(StripNumber(o.Name)));
            Objects = Objects.Add(c); copies.Add(c);
        }
        RaiseScene();
        return copies;
    }
}
