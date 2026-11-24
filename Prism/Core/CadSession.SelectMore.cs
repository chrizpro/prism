using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>A named selection the user saved; stored with the document.</summary>
public sealed record SelectionSet(string Name, List<SelItem> Items);

/// <summary>
/// The Select group's More tools (owner approved 2026-11-24): grow and shrink, similar faces, convert between levels, linked,
/// shortest path, reselect last and saved selection sets. See docs/interface/selection/README.md, "More selection tools".
/// </summary>
public sealed partial class CadSession
{
    public enum MoreSelect { Grow, Shrink, Tangent, Feature, SameFeature, SameRadius, FilletsUpTo, Coplanar, Coaxial, Linked, ToFaces, ToEdges, ToVertices }

    /// <summary>True when the More tools can act: something selected, no tool asking for input and no sketch open (Edit Freeform has its own grow and shrink).</summary>
    public bool CanSelectMore => Sketch == null && (ActiveCommand is Commands.EditFormCommand || ActiveCommand == null && _selection.Count > 0);

    /// <summary>Run one of the More tools on the current selection. Returns how many items were added (negative when items were dropped).</summary>
    public int SelectMore(MoreSelect what)
    {
        if (ActiveCommand is Commands.EditFormCommand form)
        {
            if (what is MoreSelect.Grow or MoreSelect.Shrink) { form.SelectOp(what == MoreSelect.Grow ? "grow" : "shrink"); return 0; }
            Say("In Edit Freeform use the cage's own selection tools in its panel.", true); return 0;
        }
        if (ActiveCommand != null) { Say("Finish or cancel the open tool first: More acts on the selection, not on a tool's picks.", true); return 0; }
        if (_selection.Count == 0) { Say("Select something first.", true); return 0; }
        try
        {
            return what switch
            {
                MoreSelect.ToFaces => ConvertSelection(SelMode.Face),
                MoreSelect.ToEdges => ConvertSelection(SelMode.Edge),
                MoreSelect.ToVertices => ConvertSelection(SelMode.Vertex),
                MoreSelect.Shrink => Shrink(),
                _ => Add(what, Expand(what)),
            };
        }
        catch (KernelException e) { Say(e.Message, true); return 0; }
    }

    int Add(MoreSelect what, List<SelItem>? found)
    {
        if (found == null) return 0;
        var all = _selection.Concat(found).Distinct().ToList();
        int gained = all.Count - _selection.Count;
        SelectItems(all);
        Say(gained == 0 ? $"{Label(what)}: nothing more to add." : $"{Label(what)}: added {Count(gained, KindWord(found[0].Kind))}.");
        return gained;
    }

    static string Label(MoreSelect m) => m switch
    {
        MoreSelect.Tangent => "Tangent Chain", MoreSelect.SameFeature => "Same Feature", MoreSelect.SameRadius => "Same Radius", MoreSelect.FilletsUpTo => "Fillets Up To", MoreSelect.Feature => "Whole Feature",
        _ => m.ToString(),
    };
    static string KindWord(SelKind k) => k switch { SelKind.Face => "face", SelKind.Edge => "edge", SelKind.Vertex => "corner", SelKind.MeshFace => "triangle", _ => "object" };
    static string Count(int n, string word) => $"{n:#,0} {word}{(n == 1 ? "" : "s")}";

    IEnumerable<SelItem> Picked(SelKind kind) => _selection.Where(i => i.Kind == kind && Find(i.Obj) != null);

    /// <summary>Whether a face of a solid or surface may be added (visible, not hidden by style or by Isolate).</summary>
    bool Usable(SceneObject o, int face) => !o.IsFaceHidden(face) && ShownByIsolation(new SelItem(o.Id, SelKind.Face, face));

    List<SceneObject> VisibleBodies() => Objects.Where(o => o.Visible && !HiddenIds.Contains(o.Id) && !o.IsAnnotation && o.HasBrep && o.Tri == null && o.Kind is ObjKind.Solid or ObjKind.Surface).ToList();

    List<SelItem>? Expand(MoreSelect what)
    {
        var faces = Picked(SelKind.Face).ToList(); var edges = Picked(SelKind.Edge).ToList();
        var verts = Picked(SelKind.Vertex).ToList(); var tris = Picked(SelKind.MeshFace).ToList();
        var add = new List<SelItem>();
        string? needs = null;
        switch (what)
        {
            case MoreSelect.Grow:
                foreach (var g in faces.GroupBy(i => i.Obj)) { var o = Find(g.Key)!; foreach (int f in FaceGraph(o).Neighbours(g.Select(i => i.Index))) if (Usable(o, f)) add.Add(new(o.Id, SelKind.Face, f)); }
                foreach (var g in edges.GroupBy(i => i.Obj)) { var t = Topo(Find(g.Key)!); foreach (int e in g.Select(i => i.Index).SelectMany(t.EdgesTouching)) add.Add(new(g.Key, SelKind.Edge, e)); }
                foreach (var g in verts.GroupBy(i => i.Obj)) { var t = Topo(Find(g.Key)!); foreach (int v in g.Select(i => i.Index).SelectMany(t.VertexNeighbours)) add.Add(new(g.Key, SelKind.Vertex, v)); }
                foreach (var g in tris.GroupBy(i => i.Obj)) add.AddRange(MeshRing(g.Key, g.Select(i => i.Index), grow: true));
                break;
            case MoreSelect.Tangent:
                foreach (var f in faces) add.AddRange(FeatureFinder.Expand(Find(f.Obj)!, f.Index, PickIntent.Tangent).Where(x => Usable(Find(f.Obj)!, x)).Select(x => new SelItem(f.Obj, SelKind.Face, x)));
                foreach (var e in edges) add.AddRange(Commands.SelectionChains.Expand(this, e, tangent: true));
                needs = faces.Count + edges.Count == 0 ? "faces or edges" : null;
                break;
            case MoreSelect.Feature:
            case MoreSelect.SameFeature:
                foreach (var f in faces)
                {
                    var body = Find(f.Obj)!; if (body.Kind != ObjKind.Solid) continue;
                    var intent = what == MoreSelect.Feature ? PickIntent.Feature : PickIntent.SameSize;
                    add.AddRange(FeatureFinder.Expand(body, f.Index, intent).Where(x => Usable(body, x)).Select(x => new SelItem(f.Obj, SelKind.Face, x)));
                }
                needs = faces.Count == 0 ? "faces of a solid" : null;
                break;
            case MoreSelect.SameRadius:
            {
                var radii = faces.Select(f => RadiusOf(Find(f.Obj)!, f.Index)).OfType<double>().ToList();
                foreach (var g in faces.GroupBy(i => i.Obj))
                {
                    var o = Find(g.Key)!;
                    for (int f = 0; f < o.Mesh.FaceCount; f++) if (Usable(o, f) && RadiusOf(o, f) is { } r && radii.Any(x => Math.Abs(x - r) < 1e-3)) add.Add(new(o.Id, SelKind.Face, f));
                }
                var edgeRadii = edges.Select(EdgeRadius).OfType<double>().ToList();
                foreach (var g in edges.GroupBy(i => i.Obj))
                {
                    var o = Find(g.Key)!;
                    for (int e = 0; e < o.Mesh.EdgeCount; e++) if (EdgeRadius(new SelItem(o.Id, SelKind.Edge, e)) is { } r && edgeRadii.Any(x => Math.Abs(x - r) < 1e-3)) add.Add(new(o.Id, SelKind.Edge, e));
                }
                if (radii.Count + edgeRadii.Count == 0) { Say("Same Radius needs a round face (cylinder, cone, sphere or torus) or a circular edge.", true); return null; }
                break;
            }
            case MoreSelect.FilletsUpTo:
            {
                var limit = faces.Select(f => RadiusOf(Find(f.Obj)!, f.Index)).OfType<double>().DefaultIfEmpty(-1).Max();
                if (limit < 0) { Say("Fillets Up To needs a round face, such as a fillet, to set the largest radius.", true); return null; }
                foreach (var id in faces.Select(f => f.Obj).Distinct())
                {
                    var o = Find(id)!; if (o.Kind != ObjKind.Solid) continue;
                    foreach (var feat in FeatureFinder.Features(o).Where(x => x.Kind == FeatureKind.Fillet))
                        foreach (int f in feat.Faces) if (Usable(o, f) && RadiusOf(o, f) is { } r && r <= limit + 1e-6) add.Add(new(id, SelKind.Face, f));
                }
                break;
            }
            case MoreSelect.Coplanar:
            {
                var planes = faces.Select(f => PlaneOfFace(Find(f.Obj)!, f.Index)).OfType<(Vec3 c, Vec3 n)>().ToList();
                if (planes.Count == 0) { Say("Coplanar needs a flat face.", true); return null; }
                foreach (var o in VisibleBodies())
                    for (int f = 0; f < o.Mesh.FaceCount; f++)
                        if (Usable(o, f) && PlaneOfFace(o, f) is { } p && planes.Any(q => Vec3.Dot(q.n, p.n) > 0.99995 && Math.Abs(Vec3.Dot(p.c - q.c, q.n)) < Tol(o))) add.Add(new(o.Id, SelKind.Face, f));
                break;
            }
            case MoreSelect.Coaxial:
            {
                var axes = faces.Select(f => AxisOfRound(Find(f.Obj)!, f.Index)).OfType<(Vec3 o, Vec3 d)>().ToList();
                if (axes.Count == 0) { Say("Coaxial needs a round face (cylinder, cone or torus).", true); return null; }
                foreach (var o in VisibleBodies())
                    for (int f = 0; f < o.Mesh.FaceCount; f++)
                        if (Usable(o, f) && RadiusOf(o, f) != null && AxisOfRound(o, f) is { } ax && axes.Any(q => Math.Abs(Vec3.Dot(q.d, ax.d)) > 0.99995 && Vec3.Cross(ax.o - q.o, q.d).Length < Tol(o))) add.Add(new(o.Id, SelKind.Face, f));
                break;
            }
            case MoreSelect.Linked:
                foreach (var id in faces.Select(f => f.Obj).Distinct()) { var o = Find(id)!; for (int f = 0; f < o.Mesh.FaceCount; f++) if (Usable(o, f)) add.Add(new(id, SelKind.Face, f)); }
                foreach (var id in edges.Select(f => f.Obj).Distinct()) { var o = Find(id)!; for (int e = 0; e < o.Mesh.EdgeCount; e++) add.Add(new(id, SelKind.Edge, e)); }
                foreach (var id in verts.Select(f => f.Obj).Distinct()) { var o = Find(id)!; for (int v = 0; v < VertexPoints(o).Count; v++) add.Add(new(id, SelKind.Vertex, v)); }
                foreach (var g in tris.GroupBy(i => i.Obj)) add.AddRange(MeshLinked(g.Key, g.Select(i => i.Index)));
                break;
        }
        if (needs != null) { Say($"{Label(what)} works on {needs}.", true); return null; }
        if (add.Count == 0 && faces.Count + edges.Count + verts.Count + tris.Count == 0) { Say($"{Label(what)} works on faces, edges, corners or mesh triangles, not whole objects.", true); return null; }
        return add.Count == 0 ? [.. _selection.Take(1)] : add;
    }

    static double Tol(SceneObject o) => Math.Max((o.Mesh.Max - o.Mesh.Min).Length * 1e-6, 1e-4);

    /// <summary>The radius of a round face: a cylinder's, a cone's reference radius, a sphere's, or a torus's tube (the radius of a rolled fillet).</summary>
    static double? RadiusOf(SceneObject o, int face)
    {
        if (face < 0 || face >= o.Mesh.FaceCount) return null;
        var info = FeatureFinder.InfoOf(o, face);
        return info.Type switch { "Cylinder" or "Cone" or "Sphere" => info.R1, "Torus" => info.R2, _ => null };
    }

    double? EdgeRadius(SelItem e)
    {
        if (ShapeOf(e) is not { } sh) return null;
        using (sh) { var info = Occ.EdgeInfoOf(sh); return info.Type == "Circle" ? info.Radius : null; }
    }

    static (Vec3 c, Vec3 n)? PlaneOfFace(SceneObject o, int face)
    {
        if (face < 0 || face >= o.Mesh.FaceCount || FeatureFinder.InfoOf(o, face).Type != "Plane") return null;
        var (c, n) = FeatureFinder.PlaceOf(o, face);
        return (c, n.Normalized());
    }

    static (Vec3 o, Vec3 d)? AxisOfRound(SceneObject o, int face)
    {
        if (face < 0 || face >= o.Mesh.FaceCount) return null;
        if (FeatureFinder.AxisOfFace(o, face) is { } a) return (a.Origin, a.Dir.Normalized());
        if (FeatureFinder.InfoOf(o, face).Type is not ("Torus" or "Sphere")) return null;
        using var sh = o.Shape.Sub(SubKind.Face, face);
        return Occ.AxisOf(sh) is { } b ? (b.Origin, b.Dir.Normalized()) : null;
    }

    // ------------------------------------------------------------ shrink
    int Shrink()
    {
        var drop = new HashSet<SelItem>();
        foreach (var g in Picked(SelKind.Face).GroupBy(i => i.Obj))
        {
            var o = Find(g.Key)!; var set = g.Select(i => i.Index).ToHashSet(); var graph = FaceGraph(o);
            foreach (int f in set) if (graph.Adjacent(f).Any(n => !set.Contains(n))) drop.Add(new(o.Id, SelKind.Face, f));
        }
        foreach (var g in Picked(SelKind.Edge).GroupBy(i => i.Obj))
        {
            var t = Topo(Find(g.Key)!); var set = g.Select(i => i.Index).ToHashSet();
            foreach (int e in set)
                foreach (int v in t.EdgeEnds(e))
                    if (!t.EdgesAt(v).Any(x => x != e && set.Contains(x))) { drop.Add(new(g.Key, SelKind.Edge, e)); break; }
        }
        foreach (var g in Picked(SelKind.Vertex).GroupBy(i => i.Obj))
        {
            var t = Topo(Find(g.Key)!); var set = g.Select(i => i.Index).ToHashSet();
            foreach (int v in set) if (t.VertexNeighbours(v).Any(n => !set.Contains(n))) drop.Add(new(g.Key, SelKind.Vertex, v));
        }
        foreach (var g in Picked(SelKind.MeshFace).GroupBy(i => i.Obj))
        {
            var keep = MeshRing(g.Key, g.Select(i => i.Index), grow: false).ToHashSet();
            foreach (var i in g) if (!keep.Contains(i)) drop.Add(i);
        }
        if (drop.Count == 0) { Say("Shrink: nothing to drop (whole objects do not shrink, and a closed run of edges has no free ends)."); return 0; }
        SelectItems(_selection.Where(i => !drop.Contains(i)));
        Say($"Shrink: dropped {drop.Count:#,0}.");
        return -drop.Count;
    }

    // ------------------------------------------------------------ convert between levels
    int ConvertSelection(SelMode to)
    {
        var faces = Picked(SelKind.Face).ToList(); var edges = Picked(SelKind.Edge).ToList(); var verts = Picked(SelKind.Vertex).ToList();
        var result = new List<SelItem>(); string note = "";
        switch (to)
        {
            case SelMode.Face:
                foreach (var g in edges.GroupBy(i => i.Obj)) { var o = Find(g.Key)!; var graph = FaceGraph(o); foreach (var e in g) foreach (int f in graph.FacesOfEdge(e.Index)) if (Usable(o, f)) result.Add(new(o.Id, SelKind.Face, f)); }
                foreach (var g in verts.GroupBy(i => i.Obj))
                {
                    var o = Find(g.Key)!; if (o.Kind is not (ObjKind.Solid or ObjKind.Surface)) continue;
                    var t = Topo(o); var graph = FaceGraph(o);
                    foreach (var v in g) foreach (int e in t.EdgesAt(v.Index)) foreach (int f in graph.FacesOfEdge(e)) if (Usable(o, f)) result.Add(new(o.Id, SelKind.Face, f));
                }
                break;
            case SelMode.Edge:
                foreach (var g in faces.GroupBy(i => i.Obj))
                {
                    var graph = FaceGraph(Find(g.Key)!); var set = g.Select(i => i.Index).ToHashSet();
                    var all = set.SelectMany(graph.EdgesOfFace).Distinct().ToList();
                    var border = all.Where(e => graph.FacesOfEdge(e).Count(set.Contains) == 1).ToList();
                    if (border.Count == 0 && all.Count > 0) note = " The faces close the body, so every edge of them is taken.";
                    result.AddRange((border.Count > 0 ? border : all).Select(e => new SelItem(g.Key, SelKind.Edge, e)));
                }
                foreach (var g in verts.GroupBy(i => i.Obj)) { var t = Topo(Find(g.Key)!); foreach (var v in g) result.AddRange(t.EdgesAt(v.Index).Select(e => new SelItem(g.Key, SelKind.Edge, e))); }
                break;
            case SelMode.Vertex:
                foreach (var g in faces.GroupBy(i => i.Obj)) { var o = Find(g.Key)!; var graph = FaceGraph(o); var t = Topo(o); foreach (var f in g) foreach (int e in graph.EdgesOfFace(f.Index)) result.AddRange(t.EdgeEnds(e).Select(v => new SelItem(o.Id, SelKind.Vertex, v))); }
                foreach (var g in edges.GroupBy(i => i.Obj)) { var t = Topo(Find(g.Key)!); foreach (var e in g) result.AddRange(t.EdgeEnds(e.Index).Select(v => new SelItem(g.Key, SelKind.Vertex, v))); }
                break;
        }
        result = result.Distinct().ToList();
        if (result.Count == 0) { Say($"Nothing to convert: select {(to == SelMode.Face ? "edges or corners" : to == SelMode.Edge ? "faces or corners" : "faces or edges")} of a body first.", true); return 0; }
        _keepSelectionOnModeChange = true;
        try { Mode = to; } finally { _keepSelectionOnModeChange = false; }
        SelectItems(result);
        Say($"Selected {Count(result.Count, KindWord(result[0].Kind))}.{note}");
        return result.Count;
    }

    // ------------------------------------------------------------ meshes
    IEnumerable<SelItem> MeshRing(Guid id, IEnumerable<int> tris, bool grow)
    {
        if (Find(id) is not { Tri: { } tm }) return [];
        var sel = new Meshing.MeshSelection(id, tm); sel.Add(tris);
        if (grow) sel.Grow(); else sel.Shrink();
        return sel.Tris.Select(t => new SelItem(id, SelKind.MeshFace, t)).ToList();
    }

    /// <summary>Triangles connected to these without crossing an edge sharper than 30 degrees (Blender's Select Linked, delimited by sharp edges).</summary>
    IEnumerable<SelItem> MeshLinked(Guid id, IEnumerable<int> seeds)
    {
        if (Find(id) is not { Tri: { } tm }) return [];
        var nb = tm.Topology().Neighbors; double cos = Math.Cos(30 * Math.PI / 180);
        var seen = new HashSet<int>(); var queue = new Queue<int>();
        foreach (int s in seeds) if (s >= 0 && s < tm.TriCount && seen.Add(s)) queue.Enqueue(s);
        while (queue.TryDequeue(out int t))
            for (int k = 0; k < 3; k++)
            {
                int u = nb[3 * t + k];
                if (u < 0 || seen.Contains(u) || Vec3.Dot(tm.FaceNormal(t), tm.FaceNormal(u)) < cos) continue;
                seen.Add(u); queue.Enqueue(u);
            }
        return seen.Select(t => new SelItem(id, SelKind.MeshFace, t)).ToList();
    }

    // ------------------------------------------------------------ face and edge graphs of a body
    sealed class FaceEdgeGraph(HashSet<int>[] edgesOf, List<int>[] facesOf)
    {
        public IEnumerable<int> EdgesOfFace(int f) => f >= 0 && f < edgesOf.Length ? edgesOf[f] : [];
        public IEnumerable<int> FacesOfEdge(int e) => e >= 0 && e < facesOf.Length ? facesOf[e] : [];
        public IEnumerable<int> Adjacent(int f) => EdgesOfFace(f).SelectMany(FacesOfEdge).Where(x => x != f).Distinct();
        public IEnumerable<int> Neighbours(IEnumerable<int> faces) { var set = faces.ToHashSet(); return set.SelectMany(Adjacent).Where(x => !set.Contains(x)).Distinct(); }
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshData, FaceEdgeGraph> FaceGraphs = new();
    static FaceEdgeGraph FaceGraph(SceneObject o) => FaceGraphs.GetValue(o.Mesh, m =>
    {
        var edgesOf = FaceEdges(m);
        var facesOf = Enumerable.Range(0, m.EdgeCount).Select(_ => new List<int>()).ToArray();
        for (int f = 0; f < edgesOf.Length; f++) foreach (int e in edgesOf[f]) if (e >= 0 && e < facesOf.Length) facesOf[e].Add(f);
        return new FaceEdgeGraph(edgesOf, facesOf);
    });

    /// <summary>Which corners each edge runs between, worked out from the display edges and the kernel's corner points.</summary>
    sealed class EdgeTopo(int[][] ends, List<int>[] at, double[] length)
    {
        public IEnumerable<int> EdgeEnds(int e) => e >= 0 && e < ends.Length ? ends[e] : [];
        public IEnumerable<int> EdgesAt(int v) => v >= 0 && v < at.Length ? at[v] : [];
        public double Length(int e) => e >= 0 && e < length.Length ? length[e] : 0;
        public IEnumerable<int> EdgesTouching(int e) => EdgeEnds(e).SelectMany(EdgesAt).Where(x => x != e).Distinct();
        public IEnumerable<int> VertexNeighbours(int v) => EdgesAt(v).SelectMany(EdgeEnds).Where(x => x != v).Distinct();
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshData, EdgeTopo> EdgeTopos = new();
    EdgeTopo Topo(SceneObject o) => EdgeTopos.GetValue(o.Mesh, m =>
    {
        var corners = VertexPoints(o); double tol = Math.Max((m.Max - m.Min).Length * 1e-5, 1e-4);
        var ends = new int[m.EdgeCount][]; var length = new double[m.EdgeCount];
        var at = Enumerable.Range(0, corners.Count).Select(_ => new List<int>()).ToArray();
        int Nearest(Vec3 p)
        {
            int best = -1; double bd = tol;
            for (int i = 0; i < corners.Count; i++) { double d = (corners[i] - p).Length; if (d < bd) { bd = d; best = i; } }
            return best;
        }
        for (int e = 0; e < m.EdgeCount; e++)
        {
            var pts = EdgePoints(new SelItem(o.Id, SelKind.Edge, e));
            for (int k = 1; k < pts.Count; k++) length[e] += (pts[k] - pts[k - 1]).Length;
            ends[e] = pts.Count == 0 ? [] : new[] { Nearest(pts[0]), Nearest(pts[^1]) }.Where(v => v >= 0).Distinct().ToArray();
            foreach (int v in ends[e]) at[v].Add(e);
        }
        return new EdgeTopo(ends, at, length);
    });

    // ------------------------------------------------------------ double-click and shortest path
    /// <summary>Double-click: a face takes its smoothly joined faces; an edge its tangent chain, or its connected run where that is only itself.</summary>
    List<SelItem>? ChainAt(SelItem item)
    {
        if (Find(item.Obj) is not { } o || !o.HasBrep || o.Tri != null) return null;
        try
        {
            if (item.Kind == SelKind.Face && o.Kind is ObjKind.Solid or ObjKind.Surface)
                return FeatureFinder.Expand(o, item.Index, PickIntent.Tangent).Where(f => Usable(o, f)).Select(f => new SelItem(o.Id, SelKind.Face, f)).ToList();
            if (item.Kind == SelKind.Edge)
            {
                var chain = Commands.SelectionChains.Expand(this, item, tangent: true);
                if (chain.Count <= 1) chain = Commands.SelectionChains.Expand(this, item, tangent: false);
                return [.. chain];
            }
        }
        catch (KernelException e) { Say(e.Message, true); }
        return null;
    }

    void ApplyChain(List<SelItem> chain, PointerInfo p)
    {
        if (p.Ctrl) _selection.RemoveAll(chain.Contains);
        else
        {
            if (!p.Shift) _selection.Clear();
            foreach (var it in chain) if (!_selection.Contains(it)) _selection.Add(it);
        }
        ReleaseAdoptedRegion(); SelectionChanged?.Invoke(); Invalidate();
        Say($"{(p.Ctrl ? "Took away" : "Selected")} {Count(chain.Count, KindWord(chain[0].Kind))} joined smoothly.");
    }

    /// <summary>Ctrl+Shift-click: the shortest path from the last picked item to this one, on the same body and level.</summary>
    public List<SelItem>? ShortestPath(SelItem from, SelItem to)
    {
        if (from.Obj != to.Obj || from.Kind != to.Kind || Find(to.Obj) is not { } o) return null;
        if (from == to) return [to];
        Func<int, IEnumerable<(int next, double cost)>>? step = to.Kind switch
        {
            SelKind.Face when o.Tri == null => f => FaceGraph(o).Adjacent(f).Where(x => Usable(o, x)).Select(x => (x, 1.0)),
            SelKind.Edge when o.Tri == null => e => { var t = Topo(o); return t.EdgesTouching(e).Select(x => (x, t.Length(x))); },
            SelKind.Vertex => v => { var t = Topo(o); return t.EdgesAt(v).SelectMany(e => t.EdgeEnds(e).Where(x => x != v).Select(x => (x, t.Length(e)))); },
            SelKind.MeshFace when o.Tri is { } tm => MeshStep(tm),
            _ => null,
        };
        if (step == null) return null;
        // Dijkstra; the graphs are small (faces, edges, corners) or uniform (triangles), so a sorted set is enough
        var dist = new Dictionary<int, double> { [from.Index] = 0 }; var prev = new Dictionary<int, int>();
        var open = new SortedSet<(double d, int n)> { (0, from.Index) };
        int visited = 0;
        while (open.Count > 0 && visited++ < 2_000_000)
        {
            var (d, n) = open.Min; open.Remove(open.Min);
            if (n == to.Index) break;
            if (d > dist[n]) continue;
            foreach (var (next, cost) in step(n))
            {
                double nd = d + Math.Max(cost, 1e-9);
                if (dist.TryGetValue(next, out var old) && old <= nd) continue;
                if (dist.ContainsKey(next)) open.Remove((old, next));
                dist[next] = nd; prev[next] = n; open.Add((nd, next));
            }
        }
        if (!prev.ContainsKey(to.Index)) return null;
        var path = new List<SelItem>(); int cur = to.Index;
        while (true) { path.Add(new SelItem(o.Id, to.Kind, cur)); if (cur == from.Index) break; cur = prev[cur]; }
        path.Reverse();
        return path;
    }

    static Func<int, IEnumerable<(int, double)>> MeshStep(Meshing.TriMesh tm)
    {
        var nb = tm.Topology().Neighbors;
        return t => Enumerable.Range(0, 3).Select(k => nb[3 * t + k]).Where(u => u >= 0).Select(u => (u, 1.0));
    }

    bool PathClick(SelItem hit)
    {
        var anchor = _selection.LastOrDefault(i => i.Kind == hit.Kind && i.Obj == hit.Obj);
        if (anchor == default || anchor.Kind != hit.Kind)
        {
            SetSelection(hit, true);
            Say("Ctrl+Shift-click picks the shortest path from the last face, edge or corner you picked on the same body; pick a start first.");
            return true;
        }
        var path = ShortestPath(anchor, hit);
        if (path == null) { Say("No path joins those two on this body.", true); return true; }
        foreach (var it in path) if (!_selection.Contains(it)) _selection.Add(it);
        _selection.Remove(hit); _selection.Add(hit);          // the clicked item becomes the start of the next path
        ReleaseAdoptedRegion(); SelectionChanged?.Invoke(); Invalidate();
        Say($"Shortest path: {Count(path.Count, KindWord(hit.Kind))}.");
        return true;
    }

    // ------------------------------------------------------------ reselect last
    List<SelItem> _selectionNow = [], _selectionBefore = [];
    bool _restoringSelection, _keepSelectionOnModeChange;

    void TrackSelectionHistory()
    {
        if (_restoringSelection) { _selectionNow = [.. _selection]; return; }
        if (_selectionNow.Count > 0 && !_selectionNow.SequenceEqual(_selection)) _selectionBefore = _selectionNow;
        _selectionNow = [.. _selection];
    }

    public bool CanReselect => ActiveCommand == null && Sketch == null && _selectionBefore.Any(Exists);

    /// <summary>Put back the selection from before the last change; again swaps back.</summary>
    public bool ReselectLast()
    {
        var target = _selectionBefore.Where(Exists).ToList();
        if (target.Count == 0 || ActiveCommand != null || Sketch != null) { Say("No earlier selection to put back.", true); return false; }
        var current = _selection.ToList();
        if (target.Select(i => i.Kind).Any(k => k is SelKind.Face or SelKind.MeshFace) && Mode != SelMode.Face) SetModeKeeping(SelMode.Face);
        else if (target.All(i => i.Kind == SelKind.Edge) && Mode != SelMode.Edge) SetModeKeeping(SelMode.Edge);
        else if (target.All(i => i.Kind == SelKind.Vertex) && Mode != SelMode.Vertex) SetModeKeeping(SelMode.Vertex);
        _restoringSelection = true;
        try { SelectItems(target); } finally { _restoringSelection = false; }
        if (current.Count > 0) _selectionBefore = current;
        Say($"Reselected {target.Count:#,0}.");
        return true;
    }

    void SetModeKeeping(SelMode mode) { _keepSelectionOnModeChange = true; try { Mode = mode; } finally { _keepSelectionOnModeChange = false; } }

    /// <summary>Whether a remembered pick still points at something: its object exists and its number is in range.</summary>
    public bool Exists(SelItem i)
    {
        if (i.Kind == SelKind.Origin) return true;
        if (Find(i.Obj) is not { } o) return false;
        return i.Kind switch
        {
            SelKind.Object => true,
            SelKind.Face => o.Tri == null && o.HasBrep && i.Index >= 0 && i.Index < o.Mesh.FaceCount,
            SelKind.Edge => o.Tri == null && o.HasBrep && i.Index >= 0 && i.Index < o.Mesh.EdgeCount,
            SelKind.Vertex => i.Index >= 0 && i.Index < VertexPoints(o).Count,
            SelKind.MeshFace => o.Tri is { } tm && i.Index >= 0 && i.Index < tm.TriCount,
            _ => true,
        };
    }

    // ------------------------------------------------------------ selection sets
    public List<SelectionSet> SelectionSets { get; } = [];
    public event Action? SelectionSetsChanged;

    public bool CanSaveSelectionSet => ActiveCommand == null && Sketch == null && _selection.Any(i => i.Kind != SelKind.Origin);

    /// <summary>Save the selection as "Selection N" (or the given name). Returns the name, or null when nothing is selected.</summary>
    public string? SaveSelectionSet(string? name = null)
    {
        var items = _selection.Where(i => i.Kind != SelKind.Origin).ToList();
        if (items.Count == 0) { Say("Select something to save as a set.", true); return null; }
        if (string.IsNullOrWhiteSpace(name)) { int n = 1; while (SelectionSets.Any(s => s.Name == $"Selection {n}")) n++; name = $"Selection {n}"; }
        SelectionSets.RemoveAll(s => s.Name == name);
        SelectionSets.Add(new SelectionSet(name, items));
        Dirty = true; SelectionSetsChanged?.Invoke();
        Say($"Saved {Count(items.Count, "item")} as \"{name}\". It is listed under Selection Sets in the browser.");
        return name;
    }

    /// <summary>Select a saved set (added to the selection when <paramref name="additive"/>). Items that no longer exist are skipped.</summary>
    public int UseSelectionSet(string name, bool additive = false)
    {
        if (SelectionSets.FirstOrDefault(s => s.Name == name) is not { } set) return 0;
        var live = set.Items.Where(Exists).ToList();
        if (live.Count > 0 && live.All(i => i.Kind is SelKind.Face or SelKind.MeshFace) && Mode != SelMode.Face) SetModeKeeping(SelMode.Face);
        else if (live.Count > 0 && live.All(i => i.Kind == SelKind.Edge) && Mode != SelMode.Edge) SetModeKeeping(SelMode.Edge);
        else if (live.Count > 0 && live.All(i => i.Kind == SelKind.Vertex) && Mode != SelMode.Vertex) SetModeKeeping(SelMode.Vertex);
        SelectItems(additive ? _selection.Concat(live) : live);
        int gone = set.Items.Count - live.Count;
        Say($"\"{name}\": {Count(live.Count, "item")} selected" + (gone > 0 ? $"; {gone} no longer exist and were skipped." : "."));
        return live.Count;
    }

    public bool RenameSelectionSet(string from, string to)
    {
        to = to.Trim();
        int i = SelectionSets.FindIndex(s => s.Name == from);
        if (i < 0 || to.Length == 0 || to == from) return false;
        if (SelectionSets.Any(s => s.Name == to)) { Say($"A set called \"{to}\" already exists.", true); return false; }
        SelectionSets[i] = SelectionSets[i] with { Name = to };
        Dirty = true; SelectionSetsChanged?.Invoke();
        return true;
    }

    public bool DeleteSelectionSet(string name)
    {
        if (SelectionSets.RemoveAll(s => s.Name == name) == 0) return false;
        Dirty = true; SelectionSetsChanged?.Invoke();
        Say($"Deleted the selection set \"{name}\". Nothing in the model changed.");
        return true;
    }

    /// <summary>What is selected, for the status bar: "3 faces on 1 body", "4 objects".</summary>
    public string SelectionSummary
    {
        get
        {
            var items = _selection.Where(i => i.Kind != SelKind.Origin).ToList();
            if (items.Count == 0) return _selection.Count > 0 ? Count(_selection.Count, "reference") : "";
            var parts = items.GroupBy(i => i.Kind).OrderBy(g => g.Key switch { SelKind.Object => 0, SelKind.Face => 1, SelKind.Edge => 2, SelKind.Vertex => 3, SelKind.MeshFace => 4, _ => 5 })
                .Select(g => Count(g.Count(), g.Key switch { SelKind.Object => "object", SelKind.Datum => "datum", SelKind.Entity => "curve", _ => KindWord(g.Key) })).ToList();
            int bodies = items.Where(i => i.Kind is not (SelKind.Object or SelKind.Datum)).Select(i => i.Obj).Distinct().Count();
            return string.Join(", ", parts) + (bodies > 0 ? $" on {Count(bodies, "body").Replace("bodys", "bodies")}" : "");
        }
    }
}
