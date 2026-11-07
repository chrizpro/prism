using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Pull a profile straight out into a form cage (a block, a tapered tower, a cushion you can keep sculpting).</summary>
public sealed class FormExtrudeCommand : Command
{
    readonly SelectionSlot _profile;
    readonly IntParam _pts, _steps, _level;
    readonly NumberParam _dist, _endScale;
    readonly ChoiceParam _dir;
    readonly BoolParam _cap;

    public FormExtrudeCommand()
    {
        _profile = Slot(new SelectionSlot { Label = "Profile (curve or sketch)", Filter = SelFilter.CurveObj | SelFilter.SketchObj | SelFilter.Edge });
        _dist = Num("Distance", 30, -1e5, 1e5, 5);
        _dir = Choice("Direction", 0, "Profile normal", "X", "Y", "Z");
        _endScale = Num("Scale at the far end", 1, 0.01, 20, 0.1, "");
        _steps = Add(new IntParam { Label = "Steps along the way", Min = 1, Max = 60, Value = 4 });
        _pts = Add(new IntParam { Label = "Points on the profile", Min = 4, Max = 96, Value = 16 });
        _cap = Check("Close the ends (closed profile)", true);
        _level = Add(new IntParam { Label = "Smoothness (levels)", Min = 1, Max = 4, Value = 3 });
    }

    public override string Title => "Freeform Extrude";
    public override string Icon => "form-extrude";
    public override string OkText => "Create form";
    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_profile.Items.Count == 0) return [];
            try
            {
                var (prof, closed) = FormCurves.Section(S, _profile.Items[0]);
                if (prof.Count < 2) return [];
                var c = prof.Aggregate(Vec3.Zero, (a, b) => a + b) / prof.Count;
                Vec3 d;
                if (_dir.Index == 0)
                {
                    var n = Vec3.Zero;
                    for (int i = 0; i < prof.Count; i++) { var a = prof[i] - c; var b = prof[(i + 1) % prof.Count] - c; n += Vec3.Cross(a, b); }
                    if (n.Length < 1e-9) return [new(c, Vec3.UnitZ, _dist)];
                    d = n.Normalized();
                }
                else d = _dir.Index == 1 ? Vec3.UnitX : _dir.Index == 2 ? Vec3.UnitY : Vec3.UnitZ;
                double r0 = Math.Max(prof.Max(p => p.DistanceTo(c)), 1e-9);
                return
                [
                    new(c, d, _dist),
                    new(c + d * _dist.Value, Meshing.SurfaceMath.Basis(d).u, _endScale, 1 / r0),
                ];
            }
            catch (Exception e) when (e is not OutOfMemoryException) { return []; }
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;
    public override string Hint => "Pick a closed or open profile and a distance. A form cage is pulled out from it, optionally shrinking or growing toward the far end, ready to sculpt.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_profile.Items.Count == 0) return null;
        var (prof, closed) = FormCurves.Section(S, _profile.Items[0]);
        var row = FormCurves.Resample(prof, closed, _pts.Value);
        var c = row.Aggregate(Vec3.Zero, (a, b) => a + b) / row.Count;
        Vec3 d;
        if (_dir.Index == 0)
        {
            var n = Vec3.Zero;
            for (int i = 0; i < row.Count; i++) { var a = row[i] - c; var b = row[(i + 1) % row.Count] - c; n += Vec3.Cross(a, b); }
            if (n.Length < 1e-9) throw new FormatException("The profile is a straight line, so it has no normal. Pick X, Y or Z for the direction.");
            d = n.Normalized();
        }
        else d = _dir.Index == 1 ? Vec3.UnitX : _dir.Index == 2 ? Vec3.UnitY : Vec3.UnitZ;
        var rows = new List<List<Vec3>>();
        for (int s = 0; s <= _steps.Value; s++)
        {
            double t = (double)s / _steps.Value, k = 1 + (_endScale.Value - 1) * t;
            rows.Add(row.Select(p => c + (p - c) * k + d * (_dist.Value * t)).ToList());
        }
        var m = FormCurves.Grid(rows, closed, _cap.Value, _level.Value);
        Results.Add(new ResultLine("Cage", $"{m.V.Count} points, {m.F.Count} faces"));
        var o = new CommandOutput { Label = "Freeform extrude", Icon = "form-extrude" };
        o.Add.Add(new(null, "Freeform", ObjKind.Form, Form: m));
        return o;
    }
}

/// <summary>Spin a profile around an axis into a form cage (vases, bottles, knobs, domes).</summary>
public sealed class FormRevolveCommand : Command
{
    readonly SelectionSlot _profile;
    readonly IntParam _pts, _seg, _level;
    readonly NumberParam _angle;
    readonly ChoiceParam _axis;

    public FormRevolveCommand()
    {
        _profile = Slot(new SelectionSlot { Label = "Profile (curve or sketch)", Filter = SelFilter.CurveObj | SelFilter.SketchObj | SelFilter.Edge });
        _axis = Choice("Axis through the origin", 2, "X", "Y", "Z");
        _angle = Num("Angle (degrees)", 360, 1, 360, 15, "°");
        _seg = Add(new IntParam { Label = "Segments around", Min = 3, Max = 64, Value = 8 });
        _pts = Add(new IntParam { Label = "Points on the profile", Min = 4, Max = 96, Value = 12 });
        _cap = Check("Close the open ends (360° only)", true);
        _level = Add(new IntParam { Label = "Smoothness (levels)", Min = 1, Max = 4, Value = 3 });
    }

    readonly BoolParam _cap;

    public override string Title => "Freeform Revolve";
    public override string Icon => "form-revolve";
    public override string OkText => "Create form";
    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_profile.Items.Count == 0) return [];
            try
            {
                var (prof, _) = FormCurves.Section(S, _profile.Items[0]);
                if (prof.Count == 0) return [];
                var ax = _axis.Index == 0 ? Vec3.UnitX : _axis.Index == 1 ? Vec3.UnitY : Vec3.UnitZ;
                var c = prof.Aggregate(Vec3.Zero, (a, b) => a + b) / prof.Count;
                var rel = c - ax * Vec3.Dot(c, ax);
                if (rel.Length < 1e-9) return [];
                return [new(ax * Vec3.Dot(c, ax), ax, _angle, 1, rel.Normalized(), rel.Length)];
            }
            catch (Exception e) when (e is not OutOfMemoryException) { return []; }
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;
    public override string Hint => "Pick a profile drawn beside an axis. It is spun around the axis (through the origin) into a form cage. 360° makes a closed shape; less leaves it open.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_profile.Items.Count == 0) return null;
        var (prof, pClosed) = FormCurves.Section(S, _profile.Items[0]);
        var row = FormCurves.Resample(prof, pClosed, _pts.Value);
        var ax = _axis.Index == 0 ? Vec3.UnitX : _axis.Index == 1 ? Vec3.UnitY : Vec3.UnitZ;
        Vec3 Rot(Vec3 v, double ang) { double cs = Math.Cos(ang), sn = Math.Sin(ang); return v * cs + Vec3.Cross(ax, v) * sn + ax * (Vec3.Dot(ax, v) * (1 - cs)); }
        bool full = _angle.Value >= 359.999;
        int segs = _seg.Value, nRows = full ? segs : segs + 1;
        var rows = new List<List<Vec3>>();
        for (int s = 0; s < nRows; s++) { double a = _angle.Value * Math.PI / 180 * s / segs; rows.Add(row.Select(p => Rot(p, a)).ToList()); }
        if (full) rows.Add(rows[0].ToList());
        var m = FormCurves.Grid(rows, pClosed, false, _level.Value);
        if (full) m.WeldByDistance(1e-6);
        if (m.F.Count > 0)
        {
            var fc = m.FaceCenter(0); var rad = fc - ax * Vec3.Dot(fc, ax);
            if (rad.Length > 1e-9 && Vec3.Dot(m.FaceNormal(0), rad) < 0) FormOps.FlipNormals(m);
        }
        // an open profile leaves a hole at each end, and smoothing would pinch it shut: cap it with a flat face (ends on the axis are already closed points)
        if (full && !pClosed && _cap.Value) FormOps.FillHoles(m);
        Results.Add(new ResultLine("Cage", $"{m.V.Count} points, {m.F.Count} faces"));
        var o = new CommandOutput { Label = "Freeform revolve", Icon = "form-revolve" };
        o.Add.Add(new(null, "Freeform", ObjKind.Form, Form: m));
        return o;
    }
}

/// <summary>A round tube along a path with its own radius (and a different radius at the end): limbs, cables, branches.</summary>
public sealed class FormPipeCommand : Command
{
    readonly SelectionSlot _path;
    readonly NumberParam _r0, _r1;
    readonly IntParam _sides, _steps, _level;
    readonly BoolParam _cap;

    public FormPipeCommand()
    {
        _path = Slot(new SelectionSlot { Label = "Path (curve or edge)", Filter = SelFilter.CurveObj | SelFilter.SketchObj | SelFilter.Edge });
        _r0 = Num("Radius at the start", 5, 0.01, 1e4, 1);
        _r1 = Num("Radius at the end", 5, 0.01, 1e4, 1);
        _sides = Add(new IntParam { Label = "Sides", Min = 4, Max = 48, Value = 8 });
        _steps = Add(new IntParam { Label = "Steps along the path", Min = 2, Max = 200, Value = 16 });
        _cap = Check("Close the ends", true);
        _level = Add(new IntParam { Label = "Smoothness (levels)", Min = 1, Max = 4, Value = 3 });
    }

    public override string Title => "Freeform Pipe";
    public override string Icon => "form-sweep";
    public override string OkText => "Create form";
    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_path.Items.Count == 0) return [];
            try
            {
                var (path, closedPath) = FormCurves.Section(S, _path.Items[0]);
                var pp = FormCurves.Resample(path, closedPath, 8); if (pp.Count < 2) return [];
                var t0 = (pp[1] - pp[0]).Normalized(); var t1 = (pp[^1] - pp[^2]).Normalized();
                var list = new List<HandleSpec> { new(pp[0], Meshing.SurfaceMath.Basis(t0).u, _r0) };
                if (!closedPath) list.Add(new(pp[^1], Meshing.SurfaceMath.Basis(t1).u, _r1));
                return list;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { return []; }
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;
    public override string Hint => "Pick a path. A round tube is built along it; give a different end radius to taper it (arms, tails, stems). Edit the cage afterwards to add muscle or bulges.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_path.Items.Count == 0) return null;
        var (path, closedPath) = FormCurves.Section(S, _path.Items[0]);
        var pp = FormCurves.Resample(path, closedPath, _steps.Value + (closedPath ? 0 : 1)); int n = pp.Count;
        Vec3 Tan(int i) { var a = pp[Math.Max(i - 1, 0)]; var b = pp[Math.Min(i + 1, n - 1)]; if (closedPath) { a = pp[(i + n - 1) % n]; b = pp[(i + 1) % n]; } return (b - a).Normalized(); }
        var t = Tan(0);
        var side = Math.Abs(t.Z) < 0.9 ? Vec3.Cross(t, Vec3.UnitZ).Normalized() : Vec3.Cross(t, Vec3.UnitX).Normalized();
        var up = Vec3.Cross(t, side).Normalized();
        Vec3 Rot(Vec3 v, Vec3 axis, double ang) { double cs = Math.Cos(ang), sn = Math.Sin(ang); return v * cs + Vec3.Cross(axis, v) * sn + axis * (Vec3.Dot(axis, v) * (1 - cs)); }
        var rows = new List<List<Vec3>>(); var prevT = t;
        for (int i = 0; i < n; i++)
        {
            var ti = Tan(i);
            var ax = Vec3.Cross(prevT, ti);
            if (i > 0 && ax.Length > 1e-9) { double ang = Math.Acos(Math.Clamp(Vec3.Dot(prevT, ti), -1, 1)); var axn = ax.Normalized(); side = Rot(side, axn, ang); up = Rot(up, axn, ang); }
            prevT = ti;
            double f = closedPath ? (double)i / n : (double)i / (n - 1), r = _r0.Value + (_r1.Value - _r0.Value) * f;
            rows.Add(Enumerable.Range(0, _sides.Value).Select(j => { double a = 2 * Math.PI * j / _sides.Value; return pp[i] + (side * Math.Cos(a) + up * Math.Sin(a)) * r; }).ToList());
        }
        if (closedPath) rows.Add(rows[0].ToList());
        var m = FormCurves.Grid(rows, true, _cap.Value && !closedPath, _level.Value);
        if (closedPath) m.WeldByDistance(1e-6);
        Results.Add(new ResultLine("Cage", $"{m.V.Count} points, {m.F.Count} faces"));
        var o = new CommandOutput { Label = "Freeform pipe", Icon = "form-sweep" };
        o.Add.Add(new(null, "Freeform", ObjKind.Form, Form: m));
        return o;
    }
}
