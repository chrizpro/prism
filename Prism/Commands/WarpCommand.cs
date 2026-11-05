using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>
/// Warp: bend, twist, taper or stretch whole bodies, surfaces, curves, forms or meshes along an axis.
/// Solids and surfaces are reshaped through their NURBS control nets, so the result stays exact CAD geometry.
/// </summary>
public sealed class WarpCommand : Command
{
    enum Kind { Bend, Twist, Taper, Stretch }

    readonly SelectionSlot _src;
    readonly ChoiceParam _kind, _axis, _toward;
    readonly NumberParam _angle, _percent, _start, _end, _tol;
    readonly IntParam _detail;

    public WarpCommand()
    {
        _src = Slot(new SelectionSlot { Label = "What to warp", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.CurveObj | SelFilter.Form | SelFilter.MeshObj, Multi = true });
        _kind = Choice("Warp", 0, "Bend", "Twist", "Taper (narrow or widen toward one end)", "Stretch (lengthen or shorten)");
        _axis = Choice("Along the axis", 0, "X", "Y", "Z");
        _toward = Choice("Bend toward", 0, "+X", "-X", "+Y", "-Y", "+Z", "-Z");
        _toward.Index = 4;
        _angle = Num("Angle", 45, -360, 360, 5, "°");
        _percent = Num("Amount", 30, -95, 500, 5, "%");
        _start = Num("Warp starts at", 0, 0, 100, 5, "%");
        _end = Num("Warp ends at", 100, 0, 100, 5, "%");
        _detail = Add(new IntParam { Label = "Smoothness (solids and surfaces)", Min = 0, Max = 5, Value = 2 });
        _tol = Num("Joint tolerance (solids)", 0.05, 0.001, 5, 0.01);
        UpdateVisibility();
    }

    public override string Title => "Warp";
    public override string Icon => "warp";
    public override string OkText => "Apply warp";
    public override string Hint => "Pick bodies, surfaces, curves, forms or meshes, then choose Bend, Twist, Taper or Stretch along an axis. The part of the shape before the start stays put and the part beyond the end moves rigidly with it. Higher smoothness gives solids and surfaces more control points to follow a strong bend.";

    protected override void UpdateVisibility()
    {
        if (_kind == null || _angle == null) return;
        var k = (Kind)_kind.Index;
        _angle.IsVisible = k is Kind.Bend or Kind.Twist;
        _percent.IsVisible = k is Kind.Taper or Kind.Stretch;
        _toward.IsVisible = k == Kind.Bend;
    }

    static Vec3 AxisVec(int i) => i == 0 ? Vec3.UnitX : i == 1 ? Vec3.UnitY : Vec3.UnitZ;

    Func<Vec3, Vec3> Deformation(Vec3 min, Vec3 max)
    {
        var d = AxisVec(_axis.Index);
        var c = (min + max) * 0.5;
        double lo = Vec3.Dot(min, d), hi = Vec3.Dot(max, d);
        if (_axis.Index >= 0) { lo = new[] { min.X, min.Y, min.Z }[_axis.Index]; hi = new[] { max.X, max.Y, max.Z }[_axis.Index]; }
        double t0 = lo + (hi - lo) * Math.Min(_start.Value, _end.Value) / 100, t1 = lo + (hi - lo) * Math.Max(_start.Value, _end.Value) / 100;
        double len = Math.Max(t1 - t0, 1e-9);
        var kind = (Kind)_kind.Index;

        if (kind == Kind.Bend)
        {
            int ti = _toward.Index; var b = ti / 2 == 0 ? Vec3.UnitX : ti / 2 == 1 ? Vec3.UnitY : Vec3.UnitZ; if (ti % 2 == 1) b = -b;
            if (Math.Abs(Vec3.Dot(b, d)) > 0.5) throw new FormatException("Bend toward a direction at right angles to the axis (for example along X, bend toward Y or Z).");
            double phi = Math.Abs(_angle.Value) * Math.PI / 180;
            if (_angle.Value < 0) b = -b;
            if (phi < 1e-6) return p => p;
            double R = len / phi;
            return p =>
            {
                var rel = p - c; double t = Vec3.Dot(p, d), a = t - t0;
                if (a <= 0) return p;
                double v = Vec3.Dot(rel, b);
                var w = rel - d * Vec3.Dot(rel, d) - b * v;
                double theta = Math.Min(a, len) / R, r = R - v;
                double A = r * Math.Sin(theta), B = R - r * Math.Cos(theta);
                if (a > len) { A += (a - len) * Math.Cos(theta); B += (a - len) * Math.Sin(theta); }
                // c already carries its own coordinate along the axis; the bend starts at t0, so drop that component first.
                return c - d * Vec3.Dot(c, d) + d * (t0 + A) + b * B + w;
            };
        }
        if (kind == Kind.Twist)
        {
            double total = _angle.Value * Math.PI / 180;
            return p =>
            {
                double t = Vec3.Dot(p, d); double s = Math.Clamp((t - t0) / len, 0, 1);
                if (s <= 0) return p;
                var rel = p - c; var along = d * Vec3.Dot(rel, d); var perp = rel - along;
                double ang = total * s, ca = Math.Cos(ang), sa = Math.Sin(ang);
                var rot = perp * ca + Vec3.Cross(d, perp) * sa;
                return c + along + rot;
            };
        }
        if (kind == Kind.Taper)
        {
            double f1 = 1 + _percent.Value / 100;
            return p =>
            {
                double t = Vec3.Dot(p, d); double s = Math.Clamp((t - t0) / len, 0, 1);
                var rel = p - c; var along = d * Vec3.Dot(rel, d); var perp = rel - along;
                return c + along + perp * (1 + (f1 - 1) * s);
            };
        }
        double amt = _percent.Value / 100;
        return p =>
        {
            double t = Vec3.Dot(p, d);
            double nt = t <= t0 ? t : t >= t1 ? t + amt * len : t0 + (t - t0) * (1 + amt);
            return p + d * (nt - t);
        };
    }

    Shape WarpBrep(Shape s, Func<Vec3, Vec3> f)
    {
        bool solid = s.HasSolids;
        var n = Occ.ToNurbs(s);
        int faces = n.Count(SubKind.Face);
        if (faces == 0)
        {
            int edges = n.Count(SubKind.Edge); var list = new List<Shape>();
            for (int i = 0; i < edges; i++)
            {
                var e = n.Sub(SubKind.Edge, i);
                for (int r = 0; r < _detail.Value; r++) e = Occ.RefineCurve(e);
                var net = Occ.CurveNet(e);
                list.Add(Occ.SetCurveNet(e, net.Points.Select(f).ToArray()));
            }
            return list.Count == 1 ? list[0] : Occ.Compound(list);
        }
        var fl = new List<Shape>();
        for (int i = 0; i < faces; i++)
        {
            var face = n.Sub(SubKind.Face, i);
            for (int r = 0; r < _detail.Value; r++) face = Occ.RefineFace(face);
            var net = Occ.FaceNet(face);
            fl.Add(Occ.SetFaceNet(face, net.Points.Select(f).ToArray()));
        }
        var sewn = Occ.Sew(fl, _tol.Value, solid);
        if (solid && !sewn.HasSolids) throw new FormatException("The warped faces would not close into a solid. Try a higher smoothness, a larger joint tolerance, or warp it as a surface.");
        return sewn;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count == 0) return null;
        var objs = _src.Items.Select(i => S.Find(i.Obj)).Where(o => o != null).Cast<SceneObject>().Distinct().ToList();
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var max = -min;
        foreach (var o in objs) { var m = o.Mesh; min = new(Math.Min(min.X, m.Min.X), Math.Min(min.Y, m.Min.Y), Math.Min(min.Z, m.Min.Z)); max = new(Math.Max(max.X, m.Max.X), Math.Max(max.Y, m.Max.Y), Math.Max(max.Z, m.Max.Z)); }
        var f = Deformation(min, max);
        var o2 = new CommandOutput { Label = $"Warp ({_kind.Selected.Split(' ')[0]})", Icon = "warp" };
        foreach (var o in objs)
        {
            if (o.Form is { } form)
            {
                var nf = form.Clone();
                for (int i = 0; i < nf.V.Count; i++) nf.V[i] = f(nf.V[i]);
                o2.ReplaceForms.Add((o, nf));
            }
            else if (o.Tri is { } tm)
            {
                var v = new float[tm.V.Length];
                for (int i = 0; i + 2 < v.Length; i += 3) { var p = f(new Vec3(tm.V[i], tm.V[i + 1], tm.V[i + 2])); v[i] = (float)p.X; v[i + 1] = (float)p.Y; v[i + 2] = (float)p.Z; }
                o2.ReplaceMeshes.Add((o, new TriMesh(v, (int[])tm.T.Clone())));
            }
            else o2.Replace.Add((o, WarpBrep(o.Shape, f)));
        }
        Results.Add(new ResultLine("Objects", objs.Count.ToString()));
        Results.Add(new ResultLine("Length along the axis", $"{(new[] { max.X - min.X, max.Y - min.Y, max.Z - min.Z })[_axis.Index]:0.##} mm"));
        return o2;
    }
}
