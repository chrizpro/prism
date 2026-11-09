using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

static class FormCurves
{
    /// <summary>The main outline of a picked curve, sketch or edge as points.</summary>
    public static (List<Vec3> pts, bool closed) Section(CadSession s, SelItem it)
    {
        var sh = it.Kind == SelKind.Object ? s.Find(it.Obj)?.Shape : s.ShapeOf(it);
        if (sh == null) throw new FormatException("A picked curve is gone.");
        var loops = CamGeom.Loops(sh, 0.05);
        if (loops.Count == 0) throw new FormatException("Could not read one of the curves.");
        var best = loops.OrderByDescending(l => CamGeom.Length(l.pts, l.closed)).First();
        return (best.pts, best.closed);
    }

    /// <summary>n points spread evenly along a polyline by length (closed loops do not repeat the first point).</summary>
    public static List<Vec3> Resample(List<Vec3> pts, bool closed, int n)
    {
        var p = new List<Vec3>(pts); if (closed) p.Add(p[0]);
        var cum = new List<double> { 0 };
        for (int i = 1; i < p.Count; i++) cum.Add(cum[^1] + p[i].DistanceTo(p[i - 1]));
        double total = cum[^1]; var res = new List<Vec3>();
        int count = closed ? n : n; double step = closed ? total / n : total / Math.Max(n - 1, 1);
        for (int k = 0; k < count; k++)
        {
            double d = Math.Min(k * step, total); int i = cum.FindLastIndex(c => c <= d); i = Math.Clamp(i, 0, p.Count - 2);
            double seg = Math.Max(cum[i + 1] - cum[i], 1e-12), t = (d - cum[i]) / seg;
            res.Add(p[i] + (p[i + 1] - p[i]) * t);
        }
        return res;
    }

    /// <summary>Order and rotate a section to line up with the previous one (least total distance).</summary>
    public static List<Vec3> Align(List<Vec3> prev, List<Vec3> cur, bool closed)
    {
        int n = cur.Count; double best = double.MaxValue; List<Vec3> pick = cur;
        foreach (var dir in new[] { cur, cur.AsEnumerable().Reverse().ToList() })
            for (int r = 0; r < (closed ? n : 1); r++)
            {
                var cand = Enumerable.Range(0, n).Select(i => dir[(i + r) % n]).ToList();
                double sum = 0; for (int i = 0; i < n; i++) sum += prev[i].DistanceTo(cand[i]);
                if (sum < best) { best = sum; pick = cand; }
            }
        return pick;
    }

    /// <summary>A quad grid through rows of points: row i joins row i+1. Faces face outward from the middle for closed sections.</summary>
    public static SubdMesh Grid(List<List<Vec3>> rows, bool closed, bool cap, int level)
    {
        var m = new SubdMesh { Level = level }; int n = rows[0].Count, k = rows.Count;
        foreach (var r in rows) m.V.AddRange(r);
        int Idx(int i, int j) => i * n + (j % n);
        for (int i = 0; i + 1 < k; i++)
            for (int j = 0; j < (closed ? n : n - 1); j++)
                m.F.Add([Idx(i, j), Idx(i, j + 1), Idx(i + 1, j + 1), Idx(i + 1, j)]);
        if (m.F.Count == 0) throw new FormatException("Need at least two sections.");
        // closed tubes: make the faces point away from the axis
        if (closed)
        {
            // Use the local tube direction: a curved handle may return beside its start.
            var c0 = rows[0].Aggregate(Vec3.Zero, (a, b) => a + b) / n; var c1 = rows[1].Aggregate(Vec3.Zero, (a, b) => a + b) / n;
            var axisPt = (c0 + c1) * 0.5; var fn = m.FaceNormal(0); var fc = m.FaceCenter(0);
            var axisDir = (c1 - c0); var rad = fc - axisPt; if (axisDir.Length > 1e-9) rad -= axisDir.Normalized() * Vec3.Dot(rad, axisDir.Normalized());
            if (Vec3.Dot(fn, rad) < 0) for (int f = 0; f < m.F.Count; f++) Array.Reverse(m.F[f]);
            if (cap)
            {
                var first = Enumerable.Range(0, n).Select(j => Idx(0, j)).ToArray(); var last = Enumerable.Range(0, n).Select(j => Idx(k - 1, j)).ToArray();
                m.F.Add(first); m.F.Add(last);
                // Orient each cap against its adjacent side edge, independent of path curvature.
                void OrientCap(int faceIndex, int sideIndex)
                {
                    var face = m.F[faceIndex]; var side = m.F[sideIndex];
                    for (int i = 0; i < face.Length; i++)
                    {
                        int a = face[i], b = face[(i + 1) % face.Length];
                        int at = Array.IndexOf(side, a);
                        if (at < 0 || (side[(at + 1) % side.Length] != b && side[(at + side.Length - 1) % side.Length] != b)) continue;
                        if (side[(at + 1) % side.Length] == b) Array.Reverse(face);
                        return;
                    }
                    throw new InvalidOperationException("Could not orient the tube end cap against its side faces.");
                }
                OrientCap(m.F.Count - 2, 0);
                OrientCap(m.F.Count - 1, (k - 2) * n);
            }
        }
        return m;
    }
}

/// <summary>Blend a form through several curves (sections): a smooth cage you can keep sculpting.</summary>
public sealed class FormLoftCommand : Command
{
    /// <summary>Picks edges, curves or faces spread around the model, which is hard in an orthographic view: start in perspective at the home view.</summary>
    public override bool WantsHomeView => true;
    readonly SelectionSlot _src;
    readonly IntParam _pts, _level;
    readonly BoolParam _cap;

    public FormLoftCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Sections (curves or sketches, in order)", Caption = "Profiles", Filter = SelFilter.CurveObj | SelFilter.SketchObj | SelFilter.Edge, Multi = true, Ordered = true });
        _pts = Add(new IntParam { Label = "Points per section", Min = 4, Max = 96, Value = 16 });
        _cap = Check("Close the ends (closed sections)", true);
        _level = Add(new IntParam { Label = "Smoothness (levels)", Min = 1, Max = 4, Value = 3 });
    }

    public override string Title => "Freeform Loft";
    public override string Icon => "form-loft";
    public override string OkText => "Create form";
    public override string Hint => "Pick two or more curves or sketches in order along the shape. A form cage is built through them with the chosen number of points, ready to edit with the Form tools. Closed sections make a tube.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count < 2) return null;
        var secs = _src.Items.Select(i => FormCurves.Section(S, i)).ToList();
        bool closed = secs[0].closed;
        if (secs.Any(s => s.closed != closed)) throw new FormatException("Use either all closed or all open sections.");
        var rows = new List<List<Vec3>>();
        foreach (var (pts, _) in secs)
        {
            var r = FormCurves.Resample(pts, closed, _pts.Value);
            rows.Add(rows.Count == 0 ? r : FormCurves.Align(rows[^1], r, closed));
        }
        var m = FormCurves.Grid(rows, closed, _cap.Value, _level.Value);
        Results.Add(new ResultLine("Cage", $"{m.V.Count} points, {m.F.Count} faces"));
        var o = new CommandOutput { Label = "Freeform loft", Icon = "form-loft" };
        o.Add.Add(new(null, "Freeform", ObjKind.Form, Form: m));
        return o;
    }
}

/// <summary>Carry a profile along a path to build a form cage.</summary>
public sealed class FormSweepCommand : Command
{
    /// <summary>Picks edges, curves or faces spread around the model, which is hard in an orthographic view: start in perspective at the home view.</summary>
    public override bool WantsHomeView => true;
    readonly SelectionSlot _profile, _path;
    readonly IntParam _pts, _steps, _level;
    readonly BoolParam _cap;

    public FormSweepCommand()
    {
        _profile = Slot(new SelectionSlot { Label = "Profile (curve or sketch)", Filter = SelFilter.CurveObj | SelFilter.SketchObj | SelFilter.Edge });
        _path = Slot(new SelectionSlot { Label = "Path (curve or edge)", Filter = SelFilter.CurveObj | SelFilter.SketchObj | SelFilter.Edge });
        _pts = Add(new IntParam { Label = "Points on the profile", Min = 4, Max = 96, Value = 12 });
        _steps = Add(new IntParam { Label = "Steps along the path", Min = 2, Max = 200, Value = 16 });
        _cap = Check("Close the ends (closed profile)", true);
        _level = Add(new IntParam { Label = "Smoothness (levels)", Min = 1, Max = 4, Value = 3 });
    }

    public override string Title => "Freeform Sweep";
    public override string Icon => "form-sweep";
    public override string OkText => "Create form";
    public override string Hint => "Pick a profile and a path. The profile is carried along the path (turning with it, without twisting) to build a form cage: pipes, handles, limbs. The profile should be drawn across the path where the sweep starts.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_profile.Items.Count == 0 || _path.Items.Count == 0) return null;
        var (prof, pClosed) = FormCurves.Section(S, _profile.Items[0]); var (path, pathClosed) = FormCurves.Section(S, _path.Items[0]);
        var prow = FormCurves.Resample(prof, pClosed, _pts.Value);
        var pp = FormCurves.Resample(path, pathClosed, _steps.Value + (pathClosed ? 0 : 1));
        // the path starts at whichever end is nearer the profile
        var pc = prow.Aggregate(Vec3.Zero, (a, b) => a + b) / prow.Count;
        if (!pathClosed && pc.DistanceTo(pp[^1]) < pc.DistanceTo(pp[0])) pp.Reverse();
        int m = pp.Count; var rows = new List<List<Vec3>>(); var rel = prow.Select(q => q - pp[0]).ToList();
        Vec3 Tan(int i) { var a = pp[Math.Max(i - 1, 0)]; var b = pp[Math.Min(i + 1, m - 1)]; if (pathClosed) { a = pp[(i + m - 1) % m]; b = pp[(i + 1) % m]; } return (b - a).Normalized(); }
        Vec3 Rot(Vec3 v, Vec3 axis, double ang) { double c = Math.Cos(ang), s = Math.Sin(ang); return v * c + Vec3.Cross(axis, v) * s + axis * (Vec3.Dot(axis, v) * (1 - c)); }
        var t0 = Tan(0); var frame = rel.ToList(); var prevT = t0;
        for (int i = 0; i < m; i++)
        {
            var ti = Tan(i);
            if (i > 0)
            {
                var ax = Vec3.Cross(prevT, ti);
                if (ax.Length > 1e-9) { double ang = Math.Acos(Math.Clamp(Vec3.Dot(prevT, ti), -1, 1)); var axn = ax.Normalized(); frame = frame.Select(v => Rot(v, axn, ang)).ToList(); }
            }
            prevT = ti;
            rows.Add(frame.Select(v => pp[i] + v).ToList());
        }
        if (pathClosed) rows.Add(rows[0].ToList());
        var mesh = FormCurves.Grid(rows, pClosed, _cap.Value && !pathClosed, _level.Value);
        if (pathClosed) mesh.WeldByDistance(1e-6);
        Results.Add(new ResultLine("Cage", $"{mesh.V.Count} points, {mesh.F.Count} faces"));
        var o = new CommandOutput { Label = "Freeform sweep", Icon = "form-sweep" };
        o.Add.Add(new(null, "Freeform", ObjKind.Form, Form: mesh));
        return o;
    }
}
