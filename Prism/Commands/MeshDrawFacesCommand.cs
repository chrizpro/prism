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
    readonly BoolParam _snap, _seam, _follow, _longer, _fit;
    readonly NumberParam _offset;
    const uint DraftFill = 0xC49736E6, DraftOutline = 0xFFDCACFF;
    readonly List<Corner[]> _faces = [];
    MeshSelection? _pickedFaces;
    Guid _target;
    TriMesh? _mesh;
    HashSet<int>? _removed;
    Edge[] _boundary = [];
    Corner[] _rimCorners = [];
    HashSet<int> _rimVertices = [];
    HashSet<(int, int)> _rimEdges = [];
    /// <summary>Open rims as ordered vertex loops, and where each rim vertex sits in them.</summary>
    List<int[]> _loops = [];
    Dictionary<int, (int Loop, int At)> _loopOf = [];
    MeshLocalEdit.Patch? _draft;
    string _draftKey = "";

    public MeshDrawFacesCommand()
    {
        _scope = Choice("Patch boundary", 0, "Open rim", "Replace selected region");
        _snap = Check("Snap to mesh", true);
        _plane = Choice("Draw on plane", 0, "XY", "XZ", "YZ");
        _offset = Num("Plane offset", 0, -1000000, 1000000);
        _seam = Check("Show stitch preview", true);
        _follow = Check("Follow the rim between corners", true);
        _longer = Check("Use the longer way around the rim", false);
        _fit = Check("Fit to surface", true);
        Add(new ActionParam { Label = "Undo last face", Run = () => TryUndo() });
        Tool = new DrawTool(this);
    }

    public override string Title => "Draw Faces";
    public override string Icon => "form-face";
    public override string OkText => "Stitch and apply";
    public override string Hint => "Hover to see the source triangle and snap target. Click three or four corners and press Enter to finish a face. Shift-click or Shift-drag a rim or draft edge to start from it.";

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
        var picked = S.Selection.Where(item => item.Kind == SelKind.MeshFace).ToArray();
        if (picked.Length > 0 && S.Find(picked[0].Obj) is { Tri: { } mesh } &&
            picked.All(item => item.Obj == picked[0].Obj && item.Index >= 0 && item.Index < mesh.TriCount))
        {
            _pickedFaces = new MeshSelection(picked[0].Obj, mesh);
            _pickedFaces.Add(picked.Select(item => item.Index));
        }

        var region = _pickedFaces ?? S.RegionSel;
        if (region is { IsEmpty: false } && S.Find(region.ObjectId) is { } obj && ReferenceEquals(region.Mesh, obj.Tri))
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
        var region = _pickedFaces is { } picked && picked.ObjectId == obj?.Id ? picked : S.RegionSel;
        var removed = _scope.Index == 1 && region != null && region.ObjectId == obj?.Id &&
            ReferenceEquals(region.Mesh, mesh) && !region.IsEmpty ? region.Tris : null;
        bool sameRegion = _removed == null ? removed == null : removed != null && _removed.SetEquals(removed);
        if (_target == (obj?.Id ?? Guid.Empty) && ReferenceEquals(mesh, _mesh) && sameRegion) return;

        _target = obj?.Id ?? Guid.Empty;
        _mesh = mesh;
        _removed = removed?.ToHashSet();
        _boundary = mesh == null ? [] : MeshLocalEdit.Boundary(mesh, _removed);
        _rimVertices = _boundary.SelectMany(e => new[] { e.A, e.B }).ToHashSet();
        _rimEdges = _boundary.Select(e => e.Key).ToHashSet();
        _rimCorners = mesh == null ? [] : _rimVertices.Select(i => new Corner(mesh.P(i), i)).ToArray();
        _loops = Loops(_boundary);
        _loopOf = [];
        for (int l = 0; l < _loops.Count; l++) for (int i = 0; i < _loops[l].Length; i++) _loopOf[_loops[l][i]] = (l, i);
        _faces.Clear();
        _draft = null;
        ((DrawTool)Tool!).Reset();
    }

    /// <summary>Chains boundary edges into closed loops of vertices.</summary>
    static List<int[]> Loops(Edge[] boundary)
    {
        var next = new Dictionary<int, int>();
        foreach (var e in boundary) next.TryAdd(e.A, e.B);
        var loops = new List<int[]>();
        var used = new HashSet<int>();
        foreach (int start in next.Keys)
        {
            if (used.Contains(start)) continue;
            var loop = new List<int>();
            int v = start;
            while (used.Add(v) && next.TryGetValue(v, out int w)) { loop.Add(v); v = w; }
            if (v == start && loop.Count >= 3) loops.Add([.. loop]);
        }
        return loops;
    }

    /// <summary>
    /// Follow the rim (MSH-P03): where two consecutive corners of a face are vertices of the same open rim, the side between them
    /// runs along the rim (the shorter way unless asked otherwise), so every rim edge it passes is stitched.
    /// </summary>
    Corner[] FollowRim(Corner[] face)
    {
        if (_mesh is not { } mesh) return face;
        var res = new List<Corner>();
        for (int i = 0; i < face.Length; i++)
        {
            var a = face[i]; var b = face[(i + 1) % face.Length];
            res.Add(a);
            if (a.Vertex < 0 || b.Vertex < 0 || !_loopOf.TryGetValue(a.Vertex, out var la) || !_loopOf.TryGetValue(b.Vertex, out var lb) || la.Loop != lb.Loop) continue;
            var loop = _loops[la.Loop];
            int n = loop.Length, forward = ((lb.At - la.At) % n + n) % n, backward = n - forward;
            if (forward <= 1 || backward <= 1) continue;                  // already neighbours along the rim
            bool goForward = (forward <= backward) != _longer.Value;
            int steps = goForward ? forward : backward;
            for (int k = 1; k < steps; k++)
            {
                int v = loop[((la.At + (goForward ? k : -k)) % n + n) % n];
                if (face.Any(c => c.Vertex == v) || res.Any(c => c.Vertex == v)) continue;
                res.Add(new Corner(mesh.P(v), v));
            }
        }
        return [.. res];
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
        Row("Draft faces", Count(_faces.Count));
        if (_scope.Index == 1 && (_removed == null || _removed.Count >= _mesh.TriCount))
        {
            Row("Patch boundary", "Select faces with Select â†’ Face or Edit Region before opening Draw Faces.", 2);
            return null;
        }
        if (_boundary.Length == 0)
        {
            Row("Patch boundary", "Stitching needs an open rim or faces selected for replacement.", 2);
            return null;
        }
        if (Committing && ((DrawTool)Tool!).HasPending)
            throw new InvalidOperationException("Finish the current face or clear its corners before applying.");
        if (_faces.Count == 0) return null;

        string key = $"{_follow.Value}|{_longer.Value}|{_fit.Value}";
        if (key != _draftKey) { _draft = null; _draftKey = key; }
        if (_draft == null)
        {
            var faces = _follow.Value ? _faces.Select(FollowRim).ToList() : _faces;
            _draft = MeshLocalEdit.Draw(_mesh, _removed, _boundary, faces);
            if (_fit.Value && _draft.Error == null)
            {
                // bend the new faces onto the surface around them; keep them flat if that would fold or overlap anything
                var fitted = MeshPatch.FitNewFaces(_draft.Mesh, _draft.FirstNewTriangle);
                var added = Enumerable.Range(_draft.FirstNewTriangle, fitted.TriCount - _draft.FirstNewTriangle).ToHashSet();
                if (MeshLocalEdit.Validate(fitted, added) is { } problem) Row("Fit to surface", "kept flat: " + problem, 2);
                else _draft = _draft with { Mesh = fitted };
            }
        }
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
        ((DrawTool)Tool!).DrawTarget(_mesh, overlay);
        if (_removed != null)
            foreach (int t in _removed.Take(10000))
            {
                var (a, b, c) = _mesh.Tri(t);
                overlay.AddTri(_mesh.P(a), _mesh.P(b), _mesh.P(c), 0x60F69736);
            }
        if (_draft is { } draft)
        {
            for (int t = draft.FirstNewTriangle; t < draft.Mesh.TriCount; t++)
            {
                var (a, b, c) = draft.Mesh.Tri(t);
                overlay.AddTri(draft.Mesh.P(a), draft.Mesh.P(b), draft.Mesh.P(c), DraftFill);
            }
            foreach (var face in _faces)
                overlay.AddPolyline(face.Select(c => c.Point).ToArray(), DraftOutline, 2.5f, closed: true);
        }
        else
        {
            foreach (var face in _faces)
                DrawDraft(overlay, face);
        }
        if (!_seam.Value) return;
        foreach (var e in _boundary.Take(10000)) overlay.AddLine(_mesh.P(e.A), _mesh.P(e.B), 0xFFF29B45, 2);
        if (_draft is { } merged)
        {
            foreach (var e in merged.Stitched)
                overlay.AddLine(merged.Mesh.P(e.A), merged.Mesh.P(e.B), 0xFF36C978, 3);
            // rim edges the draft does not reach stay open: show them in red (an edge split by a corner counts once both ends join)
            var joined = merged.Stitched.Select(e => e.Key).ToHashSet();
            var touched = merged.Stitched.SelectMany(e => new[] { e.A, e.B }).ToHashSet();
            foreach (var e in _boundary.Take(10000))
                if (!joined.Contains(e.Key) && !(touched.Contains(e.A) && touched.Contains(e.B)))
                    overlay.AddLine(_mesh.P(e.A), _mesh.P(e.B), 0xFFE5484D, 2.5f);
        }
    }

    static void DrawDraft(Overlay overlay, IReadOnlyList<Corner> corners)
    {
        if (corners.Count < 3) return;
        for (int i = 1; i + 1 < corners.Count; i++)
            overlay.AddTri(corners[0].Point, corners[i].Point, corners[i + 1].Point, DraftFill);
        overlay.AddPolyline(corners.Select(c => c.Point).ToArray(), DraftOutline, 2.5f, closed: true);
    }

    sealed class DrawTool(MeshDrawFacesCommand command) : IViewportTool
    {
        const uint TargetFill = 0x8045CCE1, TargetOutline = 0xFF50D8EB, EdgeColor = 0xFFFFC568;
        readonly record struct SnapTarget(Corner Corner, bool Vertex = false, Edge? Edge = null);
        readonly List<Corner> _pending = [];
        SnapTarget? _hover;
        int? _hoverTriangle;
        (double X, double Y)? _edgeStart;
        Corner[]? _pull;
        public string Prompt => command.Hint;
        public bool HasPending => _pending.Count != 0;

        public void Reset() { _pending.Clear(); _hover = null; _hoverTriangle = null; _edgeStart = null; _pull = null; }

        public bool TakeBack()
        {
            if (_pending.Count == 0) return false;
            _pending.RemoveAt(_pending.Count - 1);
            command.S.Invalidate();
            return true;
        }

        SnapTarget? Snap(CadSession s, double x, double y, out int? triangle)
        {
            triangle = null;
            if (command._mesh is not { } mesh) return null;
            var hit = MeshBoundaryPicker.SurfaceAt(s, x, y);
            if (hit is { Item: { Kind: SelKind.MeshFace } item } && item.Obj == command._target)
                triangle = item.Index;

            var corners = command._faces.SelectMany(f => f).Concat(_pending);
            Edge[] faceEdges = [];
            if (command._snap.Value)
            {
                corners = corners.Concat(command._rimCorners);
                if (triangle is int t)
                {
                    var (a, b, c) = mesh.Tri(t);
                    // Source geometry outside the repair boundary is a placement guide only.
                    corners = corners.Concat(new[] { a, b, c }.Select(i =>
                        new Corner(mesh.P(i), command._rimVertices.Contains(i) ? i : -1)));
                    faceEdges = [new(a, b), new(b, c), new(c, a)];
                }
            }

            Corner? closest = null;
            double distance = 100; // squared pixels
            foreach (var corner in corners.Distinct())
            {
                if (s.Camera.Project(corner.Point) is not { } p) continue;
                double dx = x - p.x, dy = y - p.y, squared = dx * dx + dy * dy;
                if (squared >= distance || !MeshBoundaryPicker.IsVisible(s, corner.Point)) continue;
                closest = corner;
                distance = squared;
            }
            if (closest is { } vertex) return new SnapTarget(vertex, Vertex: true);

            if (command._snap.Value)
            {
                var local = MeshBoundaryPicker.Nearest(s, mesh, faceEdges, x, y,
                    accept: edge => MeshBoundaryPicker.IsVisible(s, edge.Point));
                var rim = MeshBoundaryPicker.Nearest(s, mesh, command._boundary, x, y,
                    accept: edge => MeshBoundaryPicker.IsVisible(s, edge.Point));
                var nearest = local == null || rim is { } r && r.Distance < local.Value.Distance ? rim : local;
                if (nearest is { } edge)
                {
                    var corner = command._rimEdges.Contains(edge.Edge.Key)
                        ? new Corner(edge.Point, edge.Edge.A, edge.Edge.B, edge.Fraction)
                        : new Corner(edge.Point);
                    return new SnapTarget(corner, Edge: edge.Edge);
                }
                if (triangle != null && hit is { } h) return new SnapTarget(new Corner(h.Point));
            }
            var (origin, direction) = s.Camera.Ray(x, y);
            return command.WorkPlane?.Intersect(origin, direction) is { } point ? new SnapTarget(new Corner(point)) : null;
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
            if (picked == null && MeshBoundaryPicker.Nearest(s, mesh, command._boundary, p.X, p.Y,
                accept: edge => MeshBoundaryPicker.IsVisible(s, edge.Point)) is { } rim)
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
            _hover = Snap(s, p.X, p.Y, out _hoverTriangle);
            if (_hover is not { Corner: var corner }) { s.Invalidate(); return true; }
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
            _hover = Snap(s, p.X, p.Y, out _hoverTriangle);
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
                Snap(s, a.x + dx, a.y + dy, out _) is { Corner: var farA } &&
                Snap(s, b.x + dx, b.y + dy, out _) is { Corner: var farB })
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

        public void DrawTarget(TriMesh mesh, Overlay overlay)
        {
            if (_hoverTriangle is not int t) return;
            var (a, b, c) = mesh.Tri(t);
            var p = mesh.P(a); var q = mesh.P(b); var r = mesh.P(c);
            overlay.AddTri(p, q, r, TargetFill);
            overlay.AddPolyline([p, q, r], TargetOutline, 2, closed: true);
        }

        public void PointerExit(CadSession s)
        {
            _hover = null;
            _hoverTriangle = null;
            s.Invalidate();
        }

        public void BuildOverlay(CadSession s, Overlay overlay)
        {
            if (_pull is { } pulled)
            {
                DrawDraft(overlay, pulled);
                return;
            }
            if (_pending.Count >= 3) DrawDraft(overlay, _pending);
            else overlay.AddPolyline(_pending.Select(c => c.Point).ToArray(), DraftOutline, 2.5f);
            foreach (var c in _pending) overlay.AddDot(c.Point, DraftOutline, 7);
            if (_hover is not { } hot) return;
            var point = hot.Corner.Point;
            if (_pending.Count is > 0 and < 4) overlay.AddLine(_pending[^1].Point, point, DraftOutline, 2);
            if (hot.Edge is { } edge && command._mesh is { } mesh)
                overlay.AddLine(mesh.P(edge.A), mesh.P(edge.B), EdgeColor, 4);
            overlay.AddDot(point, hot.Vertex ? TargetOutline : hot.Edge != null ? EdgeColor : DraftOutline, hot.Vertex ? 12 : 8);
        }

        public void Deactivate(CadSession s) => Reset();
    }
}

static class MeshBoundaryPicker
{
    internal readonly record struct Hit(Edge Edge, Vec3 Point, double Fraction, double Distance);

    internal static PickResult? SurfaceAt(CadSession session, double x, double y) =>
        session.Pick(x, y, SelFilter.MeshFace | SelFilter.Face | SelFilter.Form);

    internal static bool IsVisible(CadSession session, Vec3 point)
    {
        var camera = session.Camera;
        if (camera.Project(point) is not { } p) return false;
        return SurfaceAt(session, p.x, p.y) is not { } surface ||
            Vec3.Dot(point - surface.Point, camera.Forward) <= Math.Max(camera.PixelSizeAt(point) * 0.1, 1e-6);
    }

    internal static Hit? Nearest(CadSession session, TriMesh mesh, IReadOnlyList<Edge> edges, double x, double y,
        double? surfaceDepth = null, Edge? exclude = null, Func<Hit, bool>? accept = null)
    {
        Hit? best = null;
        double nearest = 9;
        var camera = session.Camera;
        var projection = camera.ViewProjection;
        foreach (var edge in edges)
        {
            if (edge == exclude) continue;
            var a = mesh.P(edge.A);
            var b = mesh.P(edge.B);
            if (camera.Project(a, projection) is not { } p || camera.Project(b, projection) is not { } q) continue;
            var (fraction, distance) = Picking.SegDist(x, y, p.x, p.y, q.x, q.y);
            if (distance >= nearest) continue;
            if (!camera.Orthographic)
            {
                double depthA = Vec3.Dot(a - camera.Eye, camera.Forward);
                double depthB = Vec3.Dot(b - camera.Eye, camera.Forward);
                fraction = fraction * depthA / ((1 - fraction) * depthB + fraction * depthA);
            }
            fraction = Math.Clamp(fraction, 1e-6, 1 - 1e-6);
            var point = a * (1 - fraction) + b * fraction;
            if (surfaceDepth != null && session.Camera.Project(point) is { } hit && hit.depth > surfaceDepth + 0.01) continue;
            var candidate = new Hit(edge, point, fraction, distance);
            if (accept != null && !accept(candidate)) continue;
            nearest = distance;
            best = candidate;
        }
        return best;
    }
}
