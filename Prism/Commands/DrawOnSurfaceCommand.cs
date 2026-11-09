using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Draw a curve directly on a surface. Click points on any face, surface, freeform form, NURBS surface or mesh; between two
/// clicks the curve follows the shortest path over the surface (a geodesic), runs on over shared edges onto neighbouring
/// faces, and may continue onto another body that touches the first. The result is an ordinary curve that stays where it was drawn.
/// </summary>
public sealed class DrawOnSurfaceCommand : Command
{
    public readonly ChoiceParam Style;
    readonly record struct Spot(SceneObject Obj, int Part, Vec3 Point, int Tri);
    readonly List<Spot> _picks = [];
    readonly List<List<Vec3>> _legs = [];       // _legs[i] runs from pick i to pick i + 1
    List<Vec3>? _preview;
    long _lastPreview;
    SurfaceGraph? _graph; List<SceneObject> _parts = [];
    IReadOnlyList<SceneObject>? _graphSource;

    public DrawOnSurfaceCommand()
    {
        Style = Choice("Curve", 0, "Smooth through the points", "Sharp corners at the points");
        Add(new ActionParam { Label = "Clear points", Run = () => { _picks.Clear(); _legs.Clear(); _preview = null; OnChanged(); } });
        Tool = new DrawOnSurfaceTool(this);
    }

    public override string Title => "Draw on Surface";
    public override string Icon => "curve-through";
    public override string Hint => "Click points on a surface, surface body, freeform form, NURBS surface or mesh. The curve follows the surface between clicks, even over the edge onto the next face. Enter or double-click to finish, Backspace removes the last point.";
    public override string OkText => "Make curve";

    public int PickCount => _picks.Count;

    /// <summary>The surface under the pointer, from any visible object that has a surface.</summary>
    public (SceneObject obj, Vec3 point)? Hit(CadSession s, double x, double y)
    {
        var vis = s.Objects.Where(o => o.Visible && !s.HiddenIds.Contains(o.Id) && !o.IsAnnotation && o.Kind is not (ObjKind.Curve or ObjKind.Sketch)).ToList();
        return Picking.RayFace(s.Camera, vis, x, y) is { } h ? (h.obj, h.point) : null;
    }

    /// <summary>Weld every visible surface into one graph, rebuilt when the scene changes.</summary>
    SurfaceGraph Graph(CadSession s)
    {
        if (_graph != null && ReferenceEquals(_graphSource, s.Objects)) return _graph;
        _parts = s.Objects.Where(o => o.Visible && !s.HiddenIds.Contains(o.Id) && !o.IsAnnotation && o.Kind is not (ObjKind.Curve or ObjKind.Sketch) && o.Mesh.TriangleCount > 0).ToList();
        _graph = new SurfaceGraph(_parts.Select(o => (o.Mesh.Positions, o.Mesh.Indices)).ToList());
        _graphSource = s.Objects;
        return _graph;
    }

    Spot? Locate(CadSession s, SceneObject obj, Vec3 point)
    {
        var graph = Graph(s);
        int part = _parts.FindIndex(o => o.Id == obj.Id);
        if (part < 0) return null;
        int tri = graph.FindTriangle(point, part, out var on);
        return tri < 0 ? null : new Spot(obj, part, on, tri);
    }

    public void AddPoint(CadSession s, SceneObject obj, Vec3 point)
    {
        if (Locate(s, obj, point) is not { } spot) { s.Say("There is no surface there.", true); return; }
        if (_picks.Count > 0)
        {
            var last = _picks[^1];
            var leg = Graph(s).Geodesic(last.Point, last.Tri, spot.Point, spot.Tri);
            if (leg == null) { s.Say("That spot is not joined to the last point by surface: the curve would have to cross empty space. Pick a spot on a connected face or body.", true); return; }
            _legs.Add(leg);
        }
        _picks.Add(spot); _preview = null;
        s.Say(_picks.Count == 1 ? "First point set. Click more points; Enter or double-click finishes." : $"{_picks.Count} points, {SurfaceGraph.Length(Path()):0.##} mm long.");
        OnChanged(); s.Invalidate();
    }

    public void RemoveLast(CadSession s)
    {
        if (_picks.Count == 0) return;
        _picks.RemoveAt(_picks.Count - 1);
        if (_legs.Count > 0) _legs.RemoveAt(_legs.Count - 1);
        _preview = null; OnChanged(); s.Invalidate();
    }

    public override bool TryUndo()
    {
        if (_picks.Count == 0) return false;
        RemoveLast(S);
        return true;
    }

    /// <summary>Show where the curve would go if the next click were at this point.</summary>
    public void Preview(CadSession s, SceneObject? obj, Vec3 point)
    {
        long now = Environment.TickCount64;                // finding the triangle walks the mesh, so do it a few times a second at most
        if (now - _lastPreview < 60) return;
        _lastPreview = now;
        List<Vec3>? next = null;
        if (obj != null && _picks.Count > 0 && Locate(s, obj, point) is { } spot)
        {
            var last = _picks[^1];
            next = Graph(s).Geodesic(last.Point, last.Tri, spot.Point, spot.Tri, 40_000);
        }
        _preview = next; s.Invalidate();
    }

    public void ClearPreview(CadSession s) { if (_preview != null) { _preview = null; s.Invalidate(); } }

    List<Vec3> Path()
    {
        var all = new List<Vec3>();
        foreach (var leg in _legs) foreach (var p in leg) if (all.Count == 0 || (p - all[^1]).Length > 1e-9) all.Add(p);
        return all;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var leg in _legs) o.AddPolyline(leg, 0xFF35B6FF, 3);
        if (_preview != null) o.AddPolyline(_preview, 0xAA35B6FF, 2);
        foreach (var p in _picks) o.AddDot(p.Point, 0xFFFFFFFF, 9);
    }

    public override void OnEnd() { _graph = null; _graphSource = null; }

    protected override CommandOutput? Build()
    {
        if (_picks.Count < 2 || _legs.Count != _picks.Count - 1) return null;
        var pieces = new List<Shape>();
        if (Style.Index == 1) foreach (var leg in _legs) pieces.Add(Piece(leg));
        else pieces.Add(Piece(Path()));

        // On an exact solid or surface body, pull the curve onto the true surface: the display mesh is only an approximation of it.
        var owners = _picks.Select(p => p.Obj.Id).Distinct().ToList();
        string note = "";
        if (owners.Count == 1 && _picks[0].Obj is { Form: null, Tri: null, HasBrep: true } body)
        {
            var snapped = pieces.Select(p => OntoSurface(p, body)).ToList();
            note = snapped.All(x => x != null) ? " (placed on the exact surface)" : " (follows the displayed surface; the exact-surface step was not possible)";
            for (int i = 0; i < pieces.Count; i++) if (snapped[i] != null) pieces[i] = snapped[i]!;
        }
        Results.Clear();
        Results.Add(new ResultLine("Points", _picks.Count.ToString()));
        Results.Add(new ResultLine("Length", $"{SurfaceGraph.Length(Path()):0.###} mm" + note));
        return NurbsUtil.Curves([pieces.Count == 1 ? pieces[0] : Occ.Compound(pieces)], "Curve on surface");
    }

    /// <summary>A curve through a path: a straight line for two points, otherwise a spline through points spaced evenly enough to stay stable.</summary>
    static Shape Piece(IReadOnlyList<Vec3> path)
    {
        double total = SurfaceGraph.Length(path), step = Math.Max(total / 150, 1e-3);
        var kept = new List<Vec3> { path[0] };
        for (int i = 1; i + 1 < path.Count; i++) if ((path[i] - kept[^1]).Length >= step) kept.Add(path[i]);
        if (kept.Count > 1 && (path[^1] - kept[^1]).Length < step) kept.RemoveAt(kept.Count - 1);
        kept.Add(path[^1]);
        if (kept.Count == 2) return Occ.Line(kept[0], kept[1]);
        return Occ.Spline(kept);
    }

    /// <summary>Pull a curve onto the nearest points of the body's exact surface; null when that fails or changes the curve too much.</summary>
    static Shape? OntoSurface(Shape curve, SceneObject body)
    {
        try
        {
            var projected = Occ.ProjectCurve(curve, body.Shape, Vec3.Zero, true);
            if (projected.Count(SubKind.Edge) == 0) return null;
            double before = SampledLength(curve), after = SampledLength(projected);
            return before > 1e-9 && Math.Abs(after - before) <= 0.03 * before ? projected : null;
        }
        catch (KernelException) { return null; }
    }

    static double SampledLength(Shape s)
    {
        double len = 0;
        foreach (var edge in Occ.CurveSamples(s))
            for (int i = 1; i < edge.Length; i++) len += (edge[i].p - edge[i - 1].p).Length;
        return len;
    }
}

/// <summary>Clicks place points on whatever surface is under the pointer.</summary>
public sealed class DrawOnSurfaceTool(DrawOnSurfaceCommand cmd) : IViewportTool
{
    public string Prompt => "Draw on Surface: click points on a surface. Enter or double-click finishes, Backspace removes the last point, Esc cancels.";

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        if (p.Clicks >= 2 && cmd.PickCount >= 2) { s.CommitCommand(); return true; }
        if (cmd.Hit(s, p.X, p.Y) is { } hit) cmd.AddPoint(s, hit.obj, hit.point);
        return true;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        if (cmd.PickCount == 0) return;
        if (cmd.Hit(s, p.X, p.Y) is { } hit) cmd.Preview(s, hit.obj, hit.point); else cmd.ClearPreview(s);
    }

    public void PointerUp(CadSession s, PointerInfo p) { }

    public bool Key(CadSession s, string key)
    {
        switch (key)
        {
            case "Enter": if (cmd.PickCount >= 2) s.CommitCommand(); else s.Say("Click at least two points on the surface first.", true); return true;
            case "Back": cmd.RemoveLast(s); return true;
            default: return false;
        }
    }

    public void BuildOverlay(CadSession s, Overlay o) { }
    public void Deactivate(CadSession s) { }
}
