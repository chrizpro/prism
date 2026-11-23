using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Edge = Prism.Meshing.MeshLocalEdit.Edge;

namespace Prism.Commands;

/// <summary>Fills selected rims or stitches two explicitly picked boundary edges.</summary>
public sealed class MeshBoundaryCommand : MeshCommand
{
    readonly bool _stitch;
    readonly NumberParam _gap;
    readonly ChoiceParam _fill;
    Guid _target;
    TriMesh? _mesh;
    Edge[] _edges = [];
    Edge? _first, _second;
    IReadOnlyList<int[]> _rims = [];
    readonly SortedSet<int> _selectedRims = [];
    readonly Dictionary<(int, int), int> _rimByEdge = [];
    Edge[] _closedEdges = [];
    (TriMesh Mesh, int[] RimByTriangle)? _pickCaps;
    TriMesh? _result;
    bool _valid;

    public MeshBoundaryCommand(bool stitch = false)
    {
        _stitch = stitch;
        _gap = Num("Maximum gap", 0.05, 0.0001, 100, 0.01);
        _fill = Choice("Patch", 0, "Flat", "Smooth");
        _gap.IsVisible = stitch;
        _fill.IsVisible = !stitch;
        Add(new ActionParam { Label = "Clear picks", Run = Clear });
        Tool = new BoundaryTool(this);
    }

    /// <summary>Fill one set of holes or stitch one seam, then Apply and carry on with the same mesh.</summary>
    protected override bool OffersApply => true;
    public override bool ApplyKeepsSelection => true;
    public override string Title => _stitch ? "Stitch Seam" : "Fill Hole";
    public override string Icon => _stitch ? "mesh-repair" : "mesh-patch";
    public override string OkText => _stitch ? "Stitch" : "Fill";
    public override string Hint => _stitch
        ? "Pick the rim edge to keep, then the matching edge to weld to it. Preview which endpoints join."
        : "Click inside an opening or on its rim to select it. Click it again to remove it, then choose Fill.";

    protected override void UpdateVisibility()
    {
        var obj = Meshes().FirstOrDefault();
        if (_target == (obj?.Id ?? Guid.Empty) && ReferenceEquals(obj?.Tri, _mesh)) return;
        _target = obj?.Id ?? Guid.Empty;
        _mesh = obj?.Tri;
        _edges = _mesh == null ? [] : MeshLocalEdit.Boundary(_mesh);
        _first = _second = null;
        _selectedRims.Clear();
        _rimByEdge.Clear();
        _closedEdges = [];
        _pickCaps = null;
        _rims = !_stitch && _mesh != null ? _mesh.Topology().HoleLoops : [];
        for (int i = 0; i < _rims.Count; i++)
        {
            var rim = _rims[i];
            for (int k = 0; k < rim.Length; k++)
                _rimByEdge.TryAdd(new Edge(rim[k], rim[(k + 1) % rim.Length]).Key, i);
        }
        _closedEdges = _edges.Where(edge => _rimByEdge.ContainsKey(edge.Key)).ToArray();
    }

    void Clear()
    {
        _first = _second = null;
        _selectedRims.Clear();
        OnChanged();
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        _result = null;
        _valid = false;
        if (S.Find(_target) is not { } obj || _mesh == null || !ReferenceEquals(obj.Tri, _mesh)) return null;
        if (_stitch)
        {
            if (_first is not { } first || _second is not { } second)
            {
                Row("Boundary edges", _first == null ? "Pick the edge to keep fixed." : "Pick the matching edge to move.");
                return null;
            }
            double distance = Math.Max(_mesh.P(first.A).DistanceTo(_mesh.P(second.B)), _mesh.P(first.B).DistanceTo(_mesh.P(second.A)));
            Row("Maximum endpoint movement", N(distance, "mm"));
            _result = MeshLocalEdit.Stitch(_mesh, first, second, _gap.Value);
        }
        else
        {
            if (_selectedRims.Count == 0)
            {
                Row("Openings", _rims.Count == 0 ? "This mesh has no closed openings." : "Pick one or more openings.");
                return null;
            }
            Row("Selected openings", Count(_selectedRims.Count));
            _result = MeshPatch.FillBoundaries(_mesh, _selectedRims.Select(i => _rims[i]), _fill.Index == 1);
            var added = Enumerable.Range(_mesh.TriCount, _result.TriCount - _mesh.TriCount).ToHashSet();
            if (added.Count == 0) throw new InvalidOperationException("The selected rim could not be filled.");
            if (MeshLocalEdit.Validate(_result, added) is { } error) throw new InvalidOperationException(error);
        }
        _valid = true;
        Row("Open edges", $"{Count(_mesh.Topology().BoundaryEdges)} → {Count(_result.Topology().BoundaryEdges)}", 1);
        if (!Committing) return Report();
        var output = new CommandOutput { Label = Title, Icon = Icon };
        output.ReplaceMeshes.Add((obj, _result));
        return output;
    }

    public override void BuildOverlay(CadSession s, Overlay overlay)
    {
        if (_mesh == null) return;
        foreach (var e in _edges.Take(10000)) overlay.AddLine(_mesh.P(e.A), _mesh.P(e.B), 0xFFF29B45, 2);
        foreach (int rim in _selectedRims)
            overlay.AddPolyline(_rims[rim].Select(_mesh.P).ToArray(), _valid ? 0xFF36C978 : 0xFFFF5555, 3, true);
        if (_first is { } first) overlay.AddLine(_mesh.P(first.A), _mesh.P(first.B), 0xFF36C978, 4);
        if (_first is { } a && _second is { } b)
        {
            var color = _valid ? 0xFF36C978u : 0xFFFF5555u;
            overlay.AddLine(_mesh.P(b.A), _mesh.P(b.B), color, 4);
            overlay.AddLine(_mesh.P(a.A), _mesh.P(b.B), color);
            overlay.AddLine(_mesh.P(a.B), _mesh.P(b.A), color);
        }
        if (!_stitch && _valid && _result is { } fitted)
            for (int t = _mesh.TriCount; t < fitted.TriCount; t++)
            {
                var (x, y, z) = fitted.Tri(t);
                overlay.AddTri(fitted.P(x), fitted.P(y), fitted.P(z), 0x9036C978);
            }
    }

    int? PickRim(CadSession s, TriMesh mesh, double x, double y)
    {
        if (_rims.Count == 0) return null;
        if (MeshBoundaryPicker.Nearest(s, mesh, _closedEdges, x, y,
            accept: hit => MeshBoundaryPicker.IsVisible(s, hit.Point)) is { } edge)
            return _rimByEdge[edge.Edge.Key];

        _pickCaps ??= MeshPatch.BoundaryPickMesh(mesh, _rims);
        var (caps, rimByTriangle) = _pickCaps.Value;
        var (origin, direction) = s.Camera.Ray(x, y);
        if (caps.Bvh.Raycast(origin, direction) is not { } hit) return null;
        return MeshBoundaryPicker.IsVisible(s, origin + direction * hit.t) ? rimByTriangle[hit.tri] : null;
    }

    sealed class BoundaryTool(MeshBoundaryCommand command) : IViewportTool
    {
        public string Prompt => command.Hint;
        public bool PointerDown(CadSession s, PointerInfo p)
        {
            if (!p.Left) return false;
            if (command._mesh is not { } mesh)
            {
                if (s.Pick(p.X, p.Y, SelFilter.MeshObj) is { } picked)
                {
                    command.Src.Items.Clear();
                    command.Src.Items.Add(picked.Item);
                    command.OnChanged();
                }
                return true;
            }
            if (!command._stitch)
            {
                if (command.PickRim(s, mesh, p.X, p.Y) is { } rim)
                {
                    if (!command._selectedRims.Add(rim)) command._selectedRims.Remove(rim);
                    command.OnChanged();
                }
                return true;
            }
            var (origin, direction) = s.Camera.Ray(p.X, p.Y);
            var hit = mesh.Bvh.Raycast(origin, direction);
            double? depth = hit == null ? null : s.Camera.Project(origin + direction * hit.Value.t)?.depth;
            var exclude = command._second == null ? command._first : null;
            if (MeshBoundaryPicker.Nearest(s, mesh, command._edges, p.X, p.Y, depth, exclude) is not { } edge)
                return true;

            if (command._first == null || command._second != null)
            {
                command._first = edge.Edge;
                command._second = null;
            }
            else if (command._first.Value.Key != edge.Edge.Key) command._second = edge.Edge;
            command.OnChanged();
            return true;
        }

        public void PointerMove(CadSession s, PointerInfo p) { }
        public void PointerUp(CadSession s, PointerInfo p) { }
        public bool Key(CadSession s, string key) => false;
        public void BuildOverlay(CadSession s, Overlay overlay) { }
        public void Deactivate(CadSession s) { }
    }
}
