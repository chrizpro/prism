using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>Select triangles on a mesh (patch, brush, rectangle, lasso). The selection stays after the command so other tools can use it.</summary>
public sealed class MeshRegionCommand : MeshCommand
{
    public readonly ChoiceParam Mode;
    readonly NumberParam _angle, _radius, _tolerance;
    readonly BoolParam _front, _through;
    readonly ChoiceParam _patch;

    public MeshRegionCommand() : base("Mesh (click it)")
    {
        Mode = Choice("Select with", 0, "Smart patch: click a smooth area", "Brush: paint triangles", "Rectangle: drag a box", "Lasso: draw around");
        _tolerance = Num("Surface tolerance", 0.2, 0.001, 50, 0.05);
        _angle = Num("Edge angle (organic surfaces)", 20, 2, 80, 2, "°");
        _radius = Num("Brush radius", 5, 0.1, 500, 0.5);
        _front = Check("Front-facing triangles only", true);
        _through = Check("Also select hidden triangles (see-through)", false);
        Add(new ActionParam { Label = "Grow by one ring", Run = () => Edit(s => s.Grow()) });
        Add(new ActionParam { Label = "Shrink by one ring", Run = () => Edit(s => s.Shrink()) });
        Add(new ActionParam { Label = "Invert selection", Run = () => Edit(s => s.Invert()) });
        Add(new ActionParam { Label = "Select everything", Run = () => Edit(s => s.All()) });
        Add(new ActionParam { Label = "Clear selection", Run = () => Edit(s => s.Clear()) });
        Add(new ActionParam { Label = "Extract selection as a new mesh", Run = ExtractSel });
        Add(new ActionParam { Label = "Delete selected triangles", Run = DeleteSel });
        _patch = Choice("Patch", 0, "Smooth (follows the surface around it)", "Flat");
        Add(new ActionParam { Label = "Delete and patch", Run = DeleteAndPatch });
        Tool = new RegionTool(this);
    }

    public override string Title => "Select Region";
    public override string Hint => "Pick triangles on a scan. Smart patch grows over a smooth surface from the point you click; Brush paints; Rectangle and Lasso pick everything inside. Shift adds, Ctrl removes. The selection stays when you leave this tool.";
    public override string Icon => "mesh-select";
    public override string OkText => "Done";

    public double Angle => _angle.Value;
    public double Tolerance => _tolerance.Value;
    public double Radius => _radius.Value;
    public bool FrontOnly => _front.Value;
    public bool SeeThrough => _through.Value;
    public int ModeIndex => Mode.Index;

    protected override void OnBegin()
    {
        base.OnBegin();
        if (Meshes().FirstOrDefault() is { } m) { _radius.Value = Math.Round(Math.Max(m.Tri!.Size.Length / 30, 0.2), 1); _tolerance.Value = Math.Round(Math.Max(m.Tri.Size.Length * 0.003, 0.01), 3); }
    }

    protected override void UpdateVisibility()
    {
        _angle.IsVisible = _tolerance.IsVisible = Mode.Index == 0; _radius.IsVisible = Mode.Index == 1;
        _through.IsVisible = Mode.Index >= 2;
    }

    static CommandOutput Done() => new() { NoOp = true };

    public SceneObject? Target => Meshes().FirstOrDefault();

    public MeshSelection? Sel()
    {
        var o = Target; if (o == null) return null;
        return S.RegionFor(o);
    }

    public void SetTarget(SceneObject o)
    {
        if (Target?.Id == o.Id) return;
        Src.Items.Clear(); Src.Items.Add(SelItem.Of(o));
        S.DropRegion();
    }

    void Edit(Action<MeshSelection> f)
    {
        var s = Sel(); if (s == null) { S.Say("Click the mesh first.", true); return; }
        f(s); S.RefreshRegion(); OnChanged();
    }

    void ExtractSel()
    {
        var o = Target; var s = Sel();
        if (o == null || s == null || s.IsEmpty) { S.Say("Select some triangles first.", true); return; }
        var part = s.Extract();
        var res = new CommandOutput { Label = "Extract region", Icon = "mesh-select" };
        res.Add.Add(new(null, o.Name + " part", ObjKind.Mesh, o.Color, null, null, o.Look, part));
        S.Apply(res); S.Say($"Extracted {part.TriCount:#,0} triangles as a new mesh.");
        S.DropRegion(); OnChanged();
    }

    void DeleteSel()
    {
        var o = Target; var s = Sel();
        if (o == null || s == null || s.IsEmpty) { S.Say("Select some triangles first.", true); return; }
        var rest = s.Without();
        var res = new CommandOutput { Label = "Delete region", Icon = "mesh-select" };
        res.ReplaceMeshes.Add((o, rest));
        S.Apply(res); S.Say("Deleted the selected triangles. Use Fill Holes to close the opening.");
        S.DropRegion(); OnChanged();
    }

    /// <summary>Remove the selection and close only the openings that leaves; holes elsewhere in the mesh stay as they are.</summary>
    void DeleteAndPatch()
    {
        var o = Target; var s = Sel();
        if (o == null || s == null || s.IsEmpty) { S.Say("Select some triangles first.", true); return; }
        if (s.Tris.Count == s.Mesh.TriCount) { S.Say("That is the whole mesh, so there is nothing to patch it to.", true); return; }
        var patched = MeshPatch.DeleteAndPatch(s.Mesh, s.Tris, _patch.Index == 0);
        var res = new CommandOutput { Label = "Delete and patch", Icon = "mesh-select" };
        res.ReplaceMeshes.Add((o, patched.Mesh));
        S.Apply(res);
        S.Say(patched.Openings == 0 ? "Deleted the triangles. They left no opening to patch." : $"Deleted the selection and patched {patched.Openings} opening{(patched.Openings == 1 ? "" : "s")} with {Count(patched.NewTriangles)} triangles.");
        S.DropRegion(); OnChanged();
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var sel = S.RegionSel;
        if (Target == null) { Row("Click the mesh", "to start selecting", 2); return Done(); }
        if (sel == null || sel.IsEmpty) { Row("Nothing selected", "click, paint or drag on the mesh"); return Done(); }
        Row("Selected", $"{Count(sel.Tris.Count)} of {Count(sel.Mesh.TriCount)} triangles", 1);
        Row("Area", N(sel.Area(), "mm²"));
        var sub = sel.Extract();
        Row("Size", $"{sub.Size.X:0.##} × {sub.Size.Y:0.##} × {sub.Size.Z:0.##} mm");
        return Done();
    }
}

public sealed class RegionTool(MeshRegionCommand cmd) : IViewportTool
{
    bool _down; Vec3? _hover; double _x0, _y0, _x1, _y1; readonly List<(double x, double y)> _lasso = [];
    public string Prompt => "Click or drag on the mesh. Shift adds to the selection, Ctrl removes.";

    (SceneObject obj, int tri, Vec3 point)? Hit(CadSession s, double x, double y)
    {
        var (o, d) = s.Camera.Ray(x, y);
        (SceneObject, int, Vec3)? best = null; double bt = double.MaxValue;
        foreach (var obj in s.Objects)
        {
            if (obj.Tri == null || !obj.Visible || s.HiddenIds.Contains(obj.Id)) continue;
            if (obj.Tri.Bvh.Raycast(o, d, bt) is { } h && h.t < bt) { bt = h.t; best = (obj, h.tri, o + d * h.t); }
        }
        return best;
    }

    void Apply(CadSession s, MeshSelection sel, IEnumerable<int> tris, PointerInfo p, bool replaceByDefault)
    {
        if (p.Ctrl) sel.Remove(tris);
        else { if (replaceByDefault && !p.Shift) sel.Clear(); sel.Add(tris); }
        s.RefreshRegion(); cmd.OnChanged();
    }

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var hit = Hit(s, p.X, p.Y);
        _down = true; _x0 = _x1 = p.X; _y0 = _y1 = p.Y; _lasso.Clear(); _lasso.Add((p.X, p.Y));
        if (cmd.ModeIndex >= 2) return true;                              // rectangle / lasso resolve on release
        if (hit == null) { if (!p.Shift && !p.Ctrl && s.RegionSel is { } cur) { cur.Clear(); s.RefreshRegion(); cmd.OnChanged(); } return true; }
        cmd.SetTarget(hit.Value.obj);
        var sel = cmd.Sel()!;
        if (cmd.ModeIndex == 0) { var tris = sel.SmartFit(hit.Value.tri, cmd.Tolerance, cmd.Angle, out var what); Apply(s, sel, tris, p, true); s.Say($"Selected {tris.Count:#,0} triangles: {what}"); }
        else Paint(s, sel, hit.Value, p);
        return true;
    }

    void Paint(CadSession s, MeshSelection sel, (SceneObject obj, int tri, Vec3 point) hit, PointerInfo p)
    {
        Vec3? facing = cmd.FrontOnly ? s.Camera.EyeDirection : null;
        Apply(s, sel, sel.Brush(hit.point, cmd.Radius, facing).ToList(), p, false);
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        var hit = Hit(s, p.X, p.Y);
        _hover = hit?.point;
        if (_down)
        {
            _x1 = p.X; _y1 = p.Y;
            if (cmd.ModeIndex == 3) _lasso.Add((p.X, p.Y));
            if (cmd.ModeIndex == 1 && hit != null && cmd.Sel() is { } sel && hit.Value.obj.Id == cmd.Target?.Id) Paint(s, sel, hit.Value, p);
        }
        s.Invalidate();
    }

    public void PointerUp(CadSession s, PointerInfo p)
    {
        if (!_down) return;
        _down = false;
        if (cmd.ModeIndex >= 2)
        {
            var obj = cmd.Target ?? s.Objects.FirstOrDefault(o => o.Tri != null && o.Visible);
            if (obj?.Tri != null)
            {
                cmd.SetTarget(obj);
                var sel = cmd.Sel()!;
                var found = Inside(s, obj.Tri, p);
                if (found.Count > 0 || (!p.Shift && !p.Ctrl)) Apply(s, sel, found, p, true);
            }
        }
        _lasso.Clear(); s.Invalidate();
    }

    List<int> Inside(CadSession s, TriMesh m, PointerInfo p)
    {
        Func<double, double, bool> inside;
        if (cmd.ModeIndex == 2)
        {
            double a = Math.Min(_x0, _x1), b = Math.Max(_x0, _x1), c = Math.Min(_y0, _y1), d = Math.Max(_y0, _y1);
            if (b - a < 3 && d - c < 3) return [];
            inside = (x, y) => x >= a && x <= b && y >= c && y <= d;
        }
        else
        {
            if (_lasso.Count < 3) return [];
            var poly = _lasso.ToArray();
            inside = (x, y) =>
            {
                bool r = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    if ((poly[i].y > y) != (poly[j].y > y) && x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) r = !r;
                return r;
            };
        }
        var cam = s.Camera; var eye = cam.Eye; var vp = cam.ViewProjection;
        var bag = new System.Collections.Concurrent.ConcurrentBag<int>();
        bool front = cmd.FrontOnly, thru = cmd.SeeThrough;
        System.Threading.Tasks.Parallel.For(0, m.TriCount, t =>
        {
            var c = (m.P(m.T[3 * t]) + m.P(m.T[3 * t + 1]) + m.P(m.T[3 * t + 2])) / 3;
            var q = cam.Project(c, vp); if (q == null || !inside(q.Value.x, q.Value.y)) return;
            if (front && Vec3.Dot(m.FaceNormal(t), eye - c) <= 0) return;
            if (!thru)
            {
                var (o, d) = cam.Ray(q.Value.x, q.Value.y);
                double dist = (c - o).Length;
                if (m.Bvh.Raycast(o, d, double.MaxValue, 1e-6) is { } h && h.t < dist - Math.Max(dist * 0.01, m.Size.Length * 0.0005) && h.tri != t) return;
            }
            bag.Add(t);
        });
        return [.. bag];
    }

    public bool Key(CadSession s, string key)
    {
        if (key == "Escape" && s.RegionSel is { IsEmpty: false } sel) { sel.Clear(); s.RefreshRegion(); cmd.OnChanged(); return true; }
        return false;
    }

    public void BuildOverlay(CadSession s, Overlay o)
    {
        if (cmd.ModeIndex == 1 && _hover is { } h)
        {
            var cam = s.Camera; var pts = new List<Vec3>();
            for (int i = 0; i <= 48; i++) { double a = i * 2 * Math.PI / 48; pts.Add(h + (cam.Right * Math.Cos(a) + cam.Up * Math.Sin(a)) * cmd.Radius); }
            o.AddPolyline(pts, 0xFFFFC53D, 2f);
        }
        if (_down && cmd.ModeIndex == 2) o.AddRect(_x0, _y0, _x1, _y1);
        if (_down && cmd.ModeIndex == 3 && _lasso.Count > 1)
        {
            var cam = s.Camera; double dist = cam.Distance * 0.9; var pts = new List<Vec3>();
            foreach (var (x, y) in _lasso) { var (ro, rd) = cam.Ray(x, y); pts.Add(ro + rd * dist); }
            o.AddPolyline(pts, 0xFFFFC53D, 2f);
        }
    }

    public void Deactivate(CadSession s) { _down = false; _lasso.Clear(); }
}
