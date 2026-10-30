using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

public sealed partial class ActionParam : Param
{
    public Action Run { get; init; } = () => { };
    public string Text => Label;
}

public enum Falloff { Smooth, Linear, Sharp }

public static class FalloffMath
{
    public static double Weight(Falloff f, double d, double r)
    {
        if (r <= 1e-9) return d < 1e-9 ? 1 : 0;
        if (d >= r) return 0;
        double t = d / r;
        return f switch
        {
            Falloff.Linear => 1 - t,
            Falloff.Sharp => Math.Pow(1 - t, 3),
            _ => 0.5 * (1 + Math.Cos(Math.PI * t)),
        };
    }
}

/// <summary>Editable NURBS control net for a curve or single-patch surface.</summary>
public sealed class NetModel
{
    public bool IsSurface { get; }
    public Shape Source { get; private set; }
    public ControlNet Net { get; private set; }
    public Vec3[] Points => Net.Points;

    NetModel(Shape src, bool surface, ControlNet net) { Source = src; IsSurface = surface; Net = net; }

    public static NetModel From(SceneObject o)
    {
        if (o.Kind == ObjKind.Surface)
        {
            if (o.Shape.Count(SubKind.Face) != 1) throw new KernelException("Only single-patch surfaces can be edited. Use Convert to NURBS on simple faces.");
            var face = o.Shape.Kind == ShapeKind.Face ? o.Shape : o.Shape.Sub(SubKind.Face, 0);
            return new(face, true, Occ.FaceNet(face));
        }
        if (o.Kind == ObjKind.Curve)
        {
            if (o.Shape.Count(SubKind.Edge) != 1) throw new KernelException("Only single curves can be edited.");
            var e = o.Shape.Kind == ShapeKind.Edge ? o.Shape : o.Shape.Sub(SubKind.Edge, 0);
            return new(e, false, Occ.CurveNet(e));
        }
        throw new KernelException("Pick a curve or a surface.");
    }

    public Shape With(Vec3[] pts)
    {
        var shape = IsSurface ? Occ.SetFaceNet(Source, pts) : Occ.SetCurveNet(Source, pts);
        return shape;
    }

    public void Commit(Vec3[] pts, Shape shape)
    {
        Source = shape;
        Net = Net with { Points = pts };
    }

    public void BuildNetOverlay(Overlay o, uint color = 0xFF6F86A6)
    {
        if (!IsSurface)
        {
            o.AddPolyline(Net.Points, color, 1.5f);
            return;
        }
        int R = Net.Rows, C = Net.Cols;
        for (int r = 0; r < R; r++) for (int c = 0; c + 1 < C; c++) o.AddLine(Net[r, c], Net[r, c + 1], color, 1.5f);
        for (int c = 0; c < C; c++) for (int r = 0; r + 1 < R; r++) o.AddLine(Net[r, c], Net[r + 1, c], color, 1.5f);
    }
}

// ---------------------------------------------------------------- create
public sealed class NurbsPatchCommand : Command
{
    readonly ChoiceParam _plane;
    readonly NumberParam _w, _h, _off, _relief;
    readonly IntParam _rows, _cols, _deg;
    public NurbsPatchCommand()
    {
        _plane = Choice("Plane", 0, "XY (ground)", "XZ (front)", "YZ (side)");
        _w = Num("Width", 80, 0.01);
        _h = Num("Height", 60, 0.01);
        _cols = Add(new IntParam { Label = "Points across", Value = 6, Min = 2, Max = 40 });
        _rows = Add(new IntParam { Label = "Points up", Value = 5, Min = 2, Max = 40 });
        _deg = Add(new IntParam { Label = "Degree", Value = 3, Min = 1, Max = 5 });
        _relief = Num("Initial relief", 12);
        _off = Num("Plane offset", 0);
    }
    public override string Title => "NURBS Patch";
    public override string Hint => "Creates an editable grid of control points. Shape it with Edit Points or Sculpt.";

    protected override CommandOutput? Build()
    {
        var pl = _plane.Index == 0 ? Plane3.XY : _plane.Index == 1 ? Plane3.XZ : Plane3.YZ;
        int R = _rows.Value, C = _cols.Value;
        var n = pl.Normal;
        var grid = new Vec3[R * C];
        for (int r = 0; r < R; r++)
            for (int c = 0; c < C; c++)
            {
                double u = (double)c / (C - 1), v = (double)r / (R - 1);
                double bump = Math.Sin(Math.PI * u) * Math.Sin(Math.PI * v);
                grid[r * C + c] = pl.Origin + pl.U * ((u - 0.5) * _w.Value) + pl.V * ((v - 0.5) * _h.Value) + n * (_off.Value + _relief.Value * bump);
            }
        var shape = Occ.NurbsSurface(grid, R, C, _deg.Value, _deg.Value);
        var o = new CommandOutput();
        o.Add.Add(new(shape, "Patch", ObjKind.Surface));
        return o;
    }
}

public sealed class CurveByPointsCommand : Command
{
    readonly bool _through;
    readonly ChoiceParam _plane;
    readonly NumberParam _off;
    readonly IntParam? _deg;
    readonly BoolParam? _closed;
    readonly PlacePointsTool _tool;
    readonly bool _3d;
    /// <summary>3D sketch: the drawing plane follows the last point, Tab turns it, and points snap to existing edges, so a path can climb through space.</summary>
    public bool Is3D => _3d;
    public CurveByPointsCommand(bool through, bool sketch3D = false)
    {
        _through = through; _3d = sketch3D;
        _plane = Choice(sketch3D ? "Draw on plane (Tab turns it)" : "Draw on plane", 0, "XY (ground)", "XZ (front)", "YZ (side)");
        _off = Num("Plane offset", 0);
        if (sketch3D) _off.IsVisible = false;
        if (through) _closed = Check("Closed", false); else _deg = Add(new IntParam { Label = "Degree", Value = 3, Min = 1, Max = 5 });
        Tool = _tool = new PlacePointsTool(this);
    }
    public override string Title => _3d ? "3D Sketch" : _through ? "Curve Through Points" : "Control-Point Curve";
    public override string Hint => _3d ? "3D sketch: click points in space. The drawing plane passes through your last point, so a click keeps its height; press Tab to turn the plane (ground, front, side), or snap to the ends of existing edges. Right-click or Create to finish." : _through ? "Click points the curve must pass through. Right-click or OK to finish." : "Click to place control points. Right-click or OK to finish.";
    public override string OkText => "Create";

    public Plane3 Plane
    {
        get
        {
            var p = _plane.Index == 0 ? Plane3.XY : _plane.Index == 1 ? Plane3.XZ : Plane3.YZ;
            if (_3d) return new(Points.Count > 0 ? Points[^1] : Vec3.Zero, p.U, p.V);
            return new(p.Origin + p.Normal * _off.Value, p.U, p.V);
        }
    }
    public override Plane3? WorkPlane => Plane;
    public List<Vec3> Points => _tool.Points;
    public bool Through => _through;
    public void Changed() => OnChanged();
    public void NextPlane() { _plane.Index = (_plane.Index + 1) % 3; OnChanged(); }
    public void SetPoints(IEnumerable<Vec3> pts) { Points.Clear(); Points.AddRange(pts); OnChanged(); }

    protected override CommandOutput? Build()
    {
        if (Points.Count < 2) return null;
        var shape = _through ? Occ.Spline(Points, _closed!.Value && Points.Count > 2) : Occ.NurbsCurve(Points, _deg!.Value);
        var o = new CommandOutput();
        o.Add.Add(new(shape, "Curve", ObjKind.Curve));
        return o;
    }
}

// ---------------------------------------------------------------- edit control points
public sealed class EditPointsCommand : Command
{
    readonly SelectionSlot _src;
    public readonly NumberParam Radius;
    public readonly ChoiceParam FalloffMode, TransformMode;
    readonly NumberParam _px, _py, _pz;
    readonly EditPointsTool _tool;
    bool _syncing;

    public NetModel? Model { get; private set; }
    public SceneObject? Source { get; private set; }
    public Shape? Work { get; private set; }

    public EditPointsCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Curve or surface", Filter = SelFilter.SurfaceObj | SelFilter.CurveObj });
        Radius = Num("Influence radius", 0, 0, 1e6, 1);
        FalloffMode = Choice("Falloff", 0, "Smooth", "Linear", "Sharp");
        TransformMode = Choice("Gizmo (W move, E rotate, R scale)", 0, "Move", "Rotate", "Scale");
        _px = Num("Point X", 0); _py = Num("Point Y", 0); _pz = Num("Point Z", 0);
        _px.IsVisible = _py.IsVisible = _pz.IsVisible = false;
        _px.Changed = _py.Changed = _pz.Changed = OnPoleField;
        Add(new ActionParam { Label = "Add detail (more points)", Run = () => Restructure(Occ.RefineFace, Occ.RefineCurve) });
        Add(new ActionParam { Label = "Raise degree", Run = () => Restructure(Occ.ElevateFace, null) });
        Tool = _tool = new EditPointsTool(this);
    }
    public override string Title => "Edit Control Points";
    public override string Hint => "Pick a curve or surface, then click control points (Shift adds). Drag a point, or use the gizmo to move, rotate (E) or scale (R) the selection. An influence radius makes the neighbours follow.";
    public Falloff FalloffKind => (Falloff)FalloffMode.Index;

    protected override void OnBegin() { }

    protected override void UpdateVisibility()
    {
        // The source slot decides what we are editing; reload when it changes.
        var item = _src.Items.Count > 0 ? S.Find(_src.Items[0].Obj) : null;
        if (item != Source)
        {
            Source = item; Model = null; Work = null; _tool.Reset();
            if (item != null)
            {
                try { Model = NetModel.From(item); Work = item.Shape; }
                catch (KernelException e) { Error = e.Message; S.Say(e.Message, true); _src.Items.Clear(); Source = null; }
            }
        }
        bool one = _tool.Selected.Count == 1 && Model != null;
        TransformMode.IsVisible=false;
        _px.IsVisible = _py.IsVisible = _pz.IsVisible = one;
        if (one) SyncFields();
    }

    void SyncFields()
    {
        _syncing = true;
        var p = Model!.Points[_tool.Selected.First()];
        _px.Value = Math.Round(p.X, 4); _py.Value = Math.Round(p.Y, 4); _pz.Value = Math.Round(p.Z, 4);
        _syncing = false;
    }

    public void SelectionChanged() { UpdateVisibility(); }

    protected override CommandOutput? Build() => Out();

    void OnPoleField()
    {
        if (_syncing || Model == null || _tool.Selected.Count != 1) return;
        int i = _tool.Selected.First();
        var np = new Vec3(_px.Value, _py.Value, _pz.Value);
        if (np.DistanceTo(Model.Points[i]) < 1e-9) return;
        var pts = (Vec3[])Model.Points.Clone(); pts[i] = np;
        var delta=np-Model.Points[i];var frame=_tool.Binding.Frame;
        ApplyPoints(pts);
        if(_tool.Binding.Bound&&frame is {} before)_tool.Binding.Transform(before,FormTransform.MoveBy(delta));
        OnChanged();
    }

    CommandOutput? Out()
    {
        if (Source == null || Work == null) return null;
        var o = new CommandOutput();
        o.Replace.Add((Source, Work));
        return o;
    }

    public void ApplyPoints(Vec3[] pts)
    {
        if (Model == null) return;
        var shape = Model.With(pts);
        Model.Commit(pts, shape);
        Work = shape;
    }

    public void Touch() { OnChanged(); }

    void Restructure(Func<Shape, Shape> surf, Func<Shape, Shape>? curve)
    {
        if (Model == null) { S.Say("Pick a curve or surface first.", true); return; }
        try
        {
            var next = Model.IsSurface ? surf(Model.Source) : curve != null ? curve(Model.Source) : throw new KernelException("Not available for curves.");
            Model = NetModel.From(Source!.WithShape(next));
            Work = next;
            _tool.Reset();
            OnChanged();
        }
        catch (KernelException e) { S.Say(e.Message, true); }
    }
}

// ---------------------------------------------------------------- sculpt
public sealed class SculptCommand : Command
{
    readonly SelectionSlot _src;
    public readonly NumberParam Radius, Strength;
    public readonly ChoiceParam Mode, FalloffMode;
    readonly SculptTool _tool;
    public NetModel? Model { get; private set; }
    public SceneObject? Source { get; private set; }
    public Shape? Work { get; private set; }

    public SculptCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Surface", Filter = SelFilter.SurfaceObj });
        Radius = Num("Brush radius", 20, 0.1);
        Mode = Choice("Brush", 0, "Pull / push (along normal)", "Move (follow cursor)", "Smooth");
        FalloffMode = Choice("Falloff", 0, "Smooth", "Linear", "Sharp");
        Strength = Num("Smooth strength", 0.5, 0.01, 1, 0.05, "");
        Add(new ActionParam { Label = "Add detail (more points)", Run = AddDetail });
        Tool = _tool = new SculptTool(this);
    }
    public override string Title => "Sculpt";
    public override string Hint => "Pick a surface, then drag on it to pull the surface. Add detail first for finer control.";
    public Falloff FalloffKind => (Falloff)FalloffMode.Index;

    protected override void UpdateVisibility()
    {
        var item = _src.Items.Count > 0 ? S.Find(_src.Items[0].Obj) : null;
        if (item != Source)
        {
            Source = item; Model = null; Work = null;
            if (item != null)
            {
                try
                {
                    Model = NetModel.From(item); Work = item.Shape;
                    var size = item.Shape.Props().Size;
                    Radius.Value = Math.Round(Math.Max(Math.Max(size.X, size.Y), size.Z) * 0.18, 1);
                }
                catch (KernelException e) { S.Say(e.Message, true); _src.Items.Clear(); Source = null; }
            }
        }
    }

    protected override CommandOutput? Build()
    {
        if (Source == null || Work == null) return null;
        var o = new CommandOutput();
        o.Replace.Add((Source, Work));
        return o;
    }

    public void ApplyPoints(Vec3[] pts)
    {
        if (Model == null) return;
        var shape = Model.With(pts);
        Model.Commit(pts, shape);
        Work = shape;
    }
    public void Touch() => OnChanged();

    void AddDetail()
    {
        if (Model == null || Source == null) return;
        try
        {
            var next = Occ.RefineFace(Model.Source);
            Model = NetModel.From(Source.WithShape(next));
            Work = next; OnChanged();
        }
        catch (KernelException e) { S.Say(e.Message, true); }
    }
}

// ---------------------------------------------------------------- one-shot freeform actions (ribbon buttons)
public static class FreeformActions
{
    static CommandOutput? Run(CadSession s, Func<SceneObject, Shape?> f, string what)
    {
        var o = new CommandOutput();
        foreach (var obj in s.SelectedObjects.ToList())
        {
            try { if (f(obj) is { } nu) o.Replace.Add((obj, nu)); }
            catch (KernelException e) { s.Say($"{obj.Name}: {e.Message}", true); }
        }
        if (o.Replace.Count == 0) { if (!s.StatusIsError) s.Say($"Select a curve or surface to {what}.", true); return null; }
        return o;
    }

    public static void AddDetail(CadSession s) => Apply(s, Run(s, o =>
    {
        if (o.Kind == ObjKind.Surface && o.Shape.Count(SubKind.Face) == 1) return Occ.RefineFace(o.Shape.Kind == ShapeKind.Face ? o.Shape : o.Shape.Sub(SubKind.Face, 0));
        if (o.Kind == ObjKind.Curve && o.Shape.Count(SubKind.Edge) == 1) return Occ.RefineCurve(o.Shape.Kind == ShapeKind.Edge ? o.Shape : o.Shape.Sub(SubKind.Edge, 0));
        throw new KernelException("Only single curves or surface patches can be refined.");
    }, "add detail to"), "Added detail");

    public static void RaiseDegree(CadSession s) => Apply(s, Run(s, o =>
    {
        if (o.Kind == ObjKind.Surface && o.Shape.Count(SubKind.Face) == 1) return Occ.ElevateFace(o.Shape.Kind == ShapeKind.Face ? o.Shape : o.Shape.Sub(SubKind.Face, 0));
        throw new KernelException("Only single surface patches support this.");
    }, "raise the degree of"), "Raised degree");

    public static void ConvertToNurbs(CadSession s, bool fewerFaces = false) => Apply(s, Run(s, o => fewerFaces && o.Form != null ? FormToBrep.ToShape(o.Form, ConvertDetail.Fewer) : Occ.ToNurbs(o.Shape), "convert"), fewerFaces ? "Converted to NURBS (fewer faces)" : "Converted to NURBS");

    static void Apply(CadSession s, CommandOutput? o, string msg)
    {
        if (o == null) return;
        s.Apply(o);
        s.SelectObjects(o.Replace.Select(r => r.Old.Id));
        s.Say(msg);
    }
}
