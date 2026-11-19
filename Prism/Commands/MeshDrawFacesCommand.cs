using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Edge = Prism.Meshing.MeshLocalEdit.Edge;
using Corner = Prism.Meshing.MeshLocalEdit.Corner;

namespace Prism.Commands;

public sealed class MeshDrawFacesCommand : MeshCommand
{
    readonly ChoiceParam _scope, _plane;
    readonly BoolParam _snap, _seam;
    readonly NumberParam _offset;
    readonly List<Corner[]> _faces = [];
    Guid _target;
    TriMesh? _mesh;
    HashSet<int>? _removed;
    Edge[] _boundary = [];
    Corner[] _rimCorners = [];
    MeshLocalEdit.Patch? _draft;

    public MeshDrawFacesCommand()
    {
        _scope = Choice("Patch boundary", 0, "Open rim", "Replace selected region");
        _snap = Check("Snap to mesh", true);
        _plane = Choice("Draw on plane", 0, "XY", "XZ", "YZ");
        _offset = Num("Plane offset", 0, -1000000, 1000000);
        _seam = Check("Show stitch preview", true);
        Add(new ActionParam { Label = "Undo last face", Run = () => TryUndo() });
        Tool = new DrawTool(this);
    }

    public override string Title => "Draw Faces";
    public override string Icon => "form-face";
    public override string OkText => "Stitch and apply";
    public override string Hint => "Click three or four corners and press Enter to finish a face. Shift-click a rim or draft edge to start along it, or Shift-drag it into a quad. Preview the stitch before applying.";

    public override Plane3? WorkPlane
    {
        get
        {
            var basis = _plane.Index switch { 1 => Plane3.XZ, 2 => Plane3.YZ, _ => Plane3.XY };
            return new Plane3(basis.Origin + basis.Normal * _offset.Value, basis.U, basis.V);
        }
    }

    protected override void OnBegin()
    {
        base.OnBegin();
        if (S.RegionSel is { IsEmpty: false } region && S.Find(region.ObjectId) is { } obj && ReferenceEquals(region.Mesh, obj.Tri))
        {
            Src.Items.Clear();
            Src.Items.Add(SelItem.Of(obj));
            _scope.Index = 1;
        }
    }

    protected override void UpdateVisibility()
    {
        var obj = Meshes().FirstOrDefault();
        var mesh = obj?.Tri;
        var region = S.RegionSel;
        var removed = _scope.Index == 1 && region != null && region.ObjectId == obj?.Id &&
            ReferenceEquals(region.Mesh, mesh) && !region.IsEmpty ? region.Tris : null;
        bool sameRegion = _removed == null ? removed == null : removed != null && _removed.SetEquals(removed);
        if (_target == (obj?.Id ?? Guid.Empty) && ReferenceEquals(mesh, _mesh) && sameRegion) return;

        _target = obj?.Id ?? Guid.Empty;
        _mesh = mesh;
        _removed = removed?.ToHashSet();
        _boundary = mesh == null ? [] : MeshLocalEdit.Boundary(mesh, _removed);
        _rimCorners = mesh == null ? [] : _boundary.SelectMany(e => new[] { e.A, e.B })
            .Distinct().Select(i => new Corner(mesh.P(i), i)).ToArray();
        _faces.Clear();
        _draft = null;
        ((DrawTool)Tool!).Reset();
    }

    public override bool TryUndo()
    {
        if (((DrawTool)Tool!).TakeBack()) return true;
        if (_faces.Count == 0) return false;
        _faces.RemoveAt(_faces.Count - 1);
        _draft = null;
        OnChanged();
        return true;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = S.Find(_target);
        if (_mesh == null || obj == null || !ReferenceEquals(obj.Tri, _mesh)) return null;
        if (_scope.Index == 1 && (_removed == null || _removed.Count >= _mesh.TriCount))
        {
            Row("Patch boundary", "Select a region with a remaining boundary in Edit Region.", 2);
            return null;
        }
        if (_boundary.Length == 0)
        {
            Row("Patch boundary", "Pick a mesh with an open rim or select faces to replace in Edit Region.", 2);
            return null;
        }
        if (Committing && ((DrawTool)Tool!).HasPending)
            throw new InvalidOperationException("Finish the current face or clear its corners before applying.");
        Row("Draft faces", Count(_faces.Count));
        if (_faces.Count == 0) return null;

        _draft ??= MeshLocalEdit.Draw(_mesh, _removed, _boundary, _faces);
        Row("Joined edges", Count(_draft.Stitched.Length), _draft.Stitched.Length == 0 ? 2 : 1);
        Row("Remaining open edges", Count(_draft.Mesh.Topology().BoundaryEdges));
        if (_draft.Error is { } error)
        {
            Row("Stitch preview", error, 3);
            if (Committing) throw new InvalidOperationException(error);
        }

        if (!Committing) return Report();
        var result = new CommandOutput { Label = Title, Icon = Icon };
        var m = _draft.Mesh;
        result.ReplaceMeshes.Add((obj, MeshOps.Compact(m.V, m.T, m.VertexColors, m.FaceColors)));
        return result;
    }

    public override void BuildOverlay(CadSession s, Overlay overlay)
    {
        if (_mesh == null) return;
        if (_removed != null)
            foreach (int t in _removed.Take(10000))
            {
                var (a, b, c) = _mesh.Tri(t);
                overlay.AddTri(_mesh.P(a), _mesh.P(b), _mesh.P(c), 0x60F69736);
            }
        if (_draft is { } draft)
            for (int t = draft.FirstNewTriangle; t < draft.Mesh.TriCount; t++)
            {
                var (a, b, c) = draft.Mesh.Tri(t);
                var m = draft.Mesh;
                overlay.AddTri(m.P(a), m.P(b), m.P(c), 0x9077B9FF);
                overlay.AddPolyline([m.P(a), m.P(b), m.P(c)], 0xFF358CE9, 2, true);
            }
        if (!_seam.Value) return;
        foreach (var e in _boundary.Take(10000)) overlay.AddLine(_mesh.P(e.A), _mesh.P(e.B), 0xFFF29B45, 2);
        if (_draft is { } merged)
            foreach (var e in merged.Stitched)
                overlay.AddLine(merged.Mesh.P(e.A), merged.Mesh.P(e.B), 0xFF36C978, 3);
    }

    sealed class DrawTool(MeshDrawFacesCommand command) : IViewportTool
    {
        readonly List<Corner> _pending = [];
        Corner? _hover;
        (double X, double Y)? _edgeStart;
        Corner[]? _pull;
        public string Prompt => command.Hint;
        public bool HasPending => _pending.Count != 0;

        public void Reset() { _pending.Clear(); _hover = null; _edgeStart = null; _pull = null; }

        public bool TakeBack()
        {
            if (_pending.Count == 0) return false;
            _pending.RemoveAt(_pending.Count - 1);
            command.S.Invalidate();
            return true;
        }

        Corner? Snap(CadSession s, double x, double y)
        {
            if (command._mesh is not { } mesh) return null;
            var (origin, direction) = s.Camera.Ray(x, y);
            var hit = mesh.Bvh.Raycast(origin, direction);
            double? depth = hit == null ? null : s.Camera.Project(origin + direction * hit.Value.t)?.depth;
            var draftCorners = command._faces.SelectMany(f => f).Concat(_pending);
            var corners = command._snap.Value
                ? draftCorners.Concat(command._rimCorners)
                : draftCorners;

            Corner? closest = null;
            double distance = 10;
            foreach (var corner in corners.Distinct())
            {
                if (s.Camera.Project(corner.Point) is not { } p || depth != null && p.depth > depth + 0.01) continue;
                double delta = Math.Sqrt(Math.Pow(x - p.x, 2) + Math.Pow(y - p.y, 2));
                if (delta >= distance) continue;
                closest = corner;
                distance = delta;
            }
            if (closest != null) return closest;

            if (command._snap.Value)
            {
                if (MeshBoundaryPicker.Nearest(s, mesh, command._boundary, x, y, depth) is { } edge)
                    return new Corner(edge.Point, edge.Edge.A, edge.Edge.B, edge.Fraction);
                if (hit is { } h) return new Corner(origin + direction * h.t);
            }
            return command.WorkPlane?.Intersect(origin, direction) is { } point ? new Corner(point) : null;
        }

        bool StartEdge(CadSession s, PointerInfo p)
        {
            if (_pending.Count != 0 || command._mesh is not { } mesh) return false;
            double best = 9;
            (Corner A, Corner B)? picked = null;
            foreach (var face in command._faces)
                for (int i = 0; i < face.Length; i++)
                {
                    var a = face[i];
                    var b = face[(i + 1) % face.Length];
                    if (s.Camera.Project(a.Point) is not { } u || s.Camera.Project(b.Point) is not { } v) continue;
                    double d = Picking.SegDist(p.X, p.Y, u.x, u.y, v.x, v.y).Item2;
                    if (d < best) { best = d; picked = (a, b); }
                }
            if (picked == null && MeshBoundaryPicker.Nearest(s, mesh, command._boundary, p.X, p.Y) is { } rim)
                picked = (new Corner(mesh.P(rim.Edge.A), rim.Edge.A), new Corner(mesh.P(rim.Edge.B), rim.Edge.B));
            if (picked is not { } choice) return false;
            _pending.Add(choice.A);
            _pending.Add(choice.B);
            s.Invalidate();
            return true;
        }

        public bool PointerDown(CadSession s, PointerInfo p)
        {
            if (!p.Left) return false;
            if (command._mesh == null)
            {
                if (s.Pick(p.X, p.Y, SelFilter.MeshObj) is { } pick)
                {
                    command.Src.Items.Clear();
                    command.Src.Items.Add(pick.Item);
                    command.OnChanged();
                }
                return true;
            }
            _edgeStart = null;
            _pull = null;
            if (p.Shift && StartEdge(s, p)) { _edgeStart = (p.X, p.Y); return true; }
            if (Snap(s, p.X, p.Y) is not { } corner) return true;
            if (_pending.Count >= 3 && (corner == _pending[0] || p.Clicks >= 2)) { Finish(); return true; }
            if (_pending.Count < 4 && !_pending.Any(existing => existing.Point.DistanceTo(corner.Point) < 1e-7))
                _pending.Add(corner);
            s.Invalidate();
            return true;
        }

        void Finish()
        {
            command._faces.Add([.. _pending]);
            Reset();
            command._draft = null;
            command.OnChanged();
        }

        public void PointerMove(CadSession s, PointerInfo p)
        {
            _hover = Snap(s, p.X, p.Y);
            UpdatePull(s, p);
            s.Invalidate();
        }

        void UpdatePull(CadSession s, PointerInfo p)
        {
            _pull = null;
            if (_edgeStart is not { } start || _pending.Count != 2) return;
            double dx = p.X - start.X, dy = p.Y - start.Y;
            if (dx * dx + dy * dy < 100) return;
            if (s.Camera.Project(_pending[0].Point) is { } a && s.Camera.Project(_pending[1].Point) is { } b &&
                Snap(s, a.x + dx, a.y + dy) is { } farA && Snap(s, b.x + dx, b.y + dy) is { } farB)
                _pull = [_pending[0], _pending[1], farB, farA];
        }

        public void PointerUp(CadSession s, PointerInfo p)
        {
            UpdatePull(s, p);
            if (_pull is { } face)
            {
                _pending.Clear();
                _pending.AddRange(face);
                Finish();
            }
            _edgeStart = null;
        }
        public bool Key(CadSession s, string key)
        {
            if (key == "Enter" && _pending.Count >= 3) { Finish(); return true; }
            if (key == "Escape" && _pending.Count > 0) { Reset(); s.Invalidate(); return true; }
            return key == "Back" && TakeBack();
        }

        public void BuildOverlay(CadSession s, Overlay overlay)
        {
            if (_pull is { } pulled)
            {
                overlay.AddPolyline(pulled.Select(c => c.Point).ToArray(), 0xFF70B7FF, 2.5f, true);
                return;
            }
            overlay.AddPolyline(_pending.Select(c => c.Point).ToArray(), 0xFF70B7FF, 2.5f);
            foreach (var c in _pending) overlay.AddDot(c.Point, 0xFF70B7FF, 7);
            if (_hover is not { } hot) return;
            if (_pending.Count > 0) overlay.AddLine(_pending[^1].Point, hot.Point, 0xFF70B7FF, 2);
            overlay.AddDot(hot.Point, hot.Vertex >= 0 ? 0xFF36C978 : 0xFF70B7FF, 8);
        }

        public void Deactivate(CadSession s) => Reset();
    }
}

static class MeshBoundaryPicker
{
    internal readonly record struct Hit(Edge Edge, Vec3 Point, double Fraction);
    internal static Hit? Nearest(CadSession session, TriMesh mesh, IReadOnlyList<Edge> edges, double x, double y, double? surfaceDepth = null, Edge? exclude = null)
    {
        Hit? best = null;
        double nearest = 9;
        foreach (var edge in edges)
        {
            if (edge == exclude) continue;
            var a = mesh.P(edge.A);
            var b = mesh.P(edge.B);
            if (session.Camera.Project(a) is not { } p || session.Camera.Project(b) is not { } q) continue;
            var (_, distance) = Picking.SegDist(x, y, p.x, p.y, q.x, q.y);
            if (distance >= nearest) continue;
            var dx = q.x - p.x;
            var dy = q.y - p.y;
            var fraction = Math.Clamp(((x - p.x) * dx + (y - p.y) * dy) / Math.Max(dx * dx + dy * dy, 1e-12), 1e-6, 1 - 1e-6);
            var point = a * (1 - fraction) + b * fraction;
            if (surfaceDepth != null && session.Camera.Project(point) is { } hit && hit.depth > surfaceDepth + 0.01) continue;
            nearest = distance;
            best = new Hit(edge, point, fraction);
        }
        return best;
    }
}
