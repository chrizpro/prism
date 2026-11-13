using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Isolate faces: show only chosen faces of bodies and hide the rest of those bodies and every other body. It is a way of
/// looking, not an edit: nothing in the document changes, commands still work on the whole body, and it is not saved or undone.
/// </summary>
public sealed partial class CadSession
{
    sealed record IsolatedBody(MeshData Mesh, HashSet<int> Faces, HashSet<int> Edges);
    Dictionary<Guid, IsolatedBody>? _isolated;

    /// <summary>True while only chosen faces are shown.</summary>
    public bool FacesIsolated => _isolated != null;

    /// <summary>Show only these faces. Whole objects in the list keep all their faces. Curves, sketches and planes stay visible.</summary>
    public bool IsolateFaces(IEnumerable<SelItem> items)
    {
        var next = new Dictionary<Guid, IsolatedBody>();
        foreach (var group in items.Where(i => i.Kind is SelKind.Face or SelKind.Object).GroupBy(i => i.Obj))
        {
            if (Find(group.Key) is not { } obj || !HasFaces(obj)) continue;
            var mesh = obj.Mesh;
            var faces = group.Any(i => i.Kind == SelKind.Object) ? Enumerable.Range(0, mesh.FaceCount).ToHashSet()
                : group.Select(i => i.Index).Where(f => f >= 0 && f < mesh.FaceCount).ToHashSet();
            if (faces.Count > 0) next[obj.Id] = new(mesh, faces, EdgesOf(mesh, faces));
        }
        if (next.Count == 0) return false;
        _isolated = next;
        int count = next.Values.Sum(b => b.Faces.Count);
        Say($"Showing {count} face{(count == 1 ? "" : "s")} only. Show All brings the rest back.");
        Invalidate();
        return true;
    }

    public void ClearFaceIsolation()
    {
        if (_isolated == null) return;
        _isolated = null; Say("All faces are shown again."); Invalidate();
    }

    static bool HasFaces(SceneObject o) => !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form or ObjKind.Mesh;

    /// <summary>The isolation entry of a body, or null when it has none or the body has changed shape since (it is then shown whole).</summary>
    IsolatedBody? Isolation(SceneObject o) =>
        _isolated != null && _isolated.TryGetValue(o.Id, out var body) && ReferenceEquals(body.Mesh, o.Mesh) ? body : null;

    /// <summary>False for a body that face isolation hides completely.</summary>
    public bool ShownByIsolation(SceneObject o) => _isolated == null || !HasFaces(o) || _isolated.ContainsKey(o.Id);

    /// <summary>The faces of this body that are shown, or null when all of them are.</summary>
    public IReadOnlySet<int>? ShownFaces(SceneObject o) => Isolation(o)?.Faces;

    /// <summary>The edges of this body that border a shown face, or null when all edges are shown.</summary>
    public IReadOnlySet<int>? ShownEdges(SceneObject o) => Isolation(o)?.Edges;

    /// <summary>True when isolation leaves this face, edge or object on screen.</summary>
    public bool ShownByIsolation(SelItem item)
    {
        if (_isolated == null || Find(item.Obj) is not { } o) return true;
        if (!ShownByIsolation(o)) return false;
        if (Isolation(o) is not { } body) return true;
        return item.Kind switch { SelKind.Face => body.Faces.Contains(item.Index), SelKind.Edge => body.Edges.Contains(item.Index), _ => true };
    }

    /// <summary>
    /// The edges that border any of the faces. An edge's polyline runs along the triangles of the faces it borders, so its
    /// points are corners of those triangles; an edge whose first segment has both ends among a face's corners belongs to it.
    /// </summary>
    static HashSet<int> EdgesOf(MeshData mesh, HashSet<int> faces)
    {
        var edges = new HashSet<int>();
        if (mesh.SegEdge.Length == 0 || mesh.FaceTriStart.Length < 2) return edges;
        double size = (mesh.Max - mesh.Min).Length; double cell = Math.Max(size * 1e-6, 1e-9);
        (long, long, long) Key(float x, float y, float z) => ((long)Math.Round(x / cell), (long)Math.Round(y / cell), (long)Math.Round(z / cell));
        var corners = new HashSet<(long, long, long)>();
        var P = mesh.Positions; var I = mesh.Indices;
        foreach (int f in faces)
        {
            if (f + 1 >= mesh.FaceTriStart.Length) continue;
            for (int t = mesh.FaceTriStart[f]; t < mesh.FaceTriStart[f + 1]; t++)
                for (int k = 0; k < 3; k++) { int v = I[3 * t + k] * 3; corners.Add(Key(P[v], P[v + 1], P[v + 2])); }
        }
        var E = mesh.EdgePoints;
        for (int e = 0; e + 1 < mesh.EdgeSegStart.Length; e++)
        {
            int first = mesh.EdgeSegStart[e], last = mesh.EdgeSegStart[e + 1];
            if (last <= first) continue;
            bool all = true;
            for (int s = first; s < last && all; s++)
                all = corners.Contains(Key(E[6 * s], E[6 * s + 1], E[6 * s + 2])) && corners.Contains(Key(E[6 * s + 3], E[6 * s + 4], E[6 * s + 5]));
            if (all) edges.Add(e);
        }
        return edges;
    }
}
