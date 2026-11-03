using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.Simulation;

namespace Prism.Commands;

/// <summary>How the motion tools turn a click into a point or a path.</summary>
public static class MotionPicks
{
    /// <summary>A point for a picked face, edge or body: a circle's centre, a face's centre point, the middle of an edge, the middle of a body.</summary>
    public static Vec3 PointOf(CadSession s, SelItem it)
    {
        if (it.Kind == SelKind.Edge)
        {
            var pts = s.EdgePoints(it);
            if (pts.Count >= 2)
            {
                if ((pts[0] - pts[^1]).Length < 1e-6 && s.FrameOf(it) is { } circle) return circle.origin;
                double half = 0, total = 0; for (int i = 1; i < pts.Count; i++) total += (pts[i] - pts[i - 1]).Length;
                for (int i = 1; i < pts.Count; i++) { double seg = (pts[i] - pts[i - 1]).Length; if (half + seg >= total / 2) return pts[i - 1] + (pts[i] - pts[i - 1]) * ((total / 2 - half) / Math.Max(seg, 1e-12)); half += seg; }
            }
        }
        if (it.Kind == SelKind.Face) { if (s.FrameOf(it) is { } f) return f.origin; if (s.FaceFrame(it) is { } ff) return ff.point; }
        var o = s.Find(it.Obj) ?? throw new FormatException("That pick no longer exists.");
        var m = o.Mesh; return (m.Min + m.Max) * 0.5;
    }

    /// <summary>The component a pick belongs to, or null when it is a plain body (which then counts as fixed in space).</summary>
    public static SceneObject? ComponentOf(CadSession s, SelItem it) => s.Find(it.Obj) is { Component: not null } o ? o : null;

    public static double[] Local(SceneObject comp, Vec3 world) { var p = comp.Component!.Placement.Inverse().Apply(world); return [p.X, p.Y, p.Z]; }

    public static readonly string[] AxisNames = ["+X", "−X", "+Y", "−Y", "+Z", "−Z", "Along the picked face"];
    public static readonly Vec3[] Axes = [Vec3.UnitX, -Vec3.UnitX, Vec3.UnitY, -Vec3.UnitY, Vec3.UnitZ, -Vec3.UnitZ];

    /// <summary>A path from a picked curve edge, or from all the edges of a picked curve body or sketch, joined end to end.</summary>
    public static List<Vec3> CurveOf(CadSession s, SelItem it)
    {
        if (it.Kind == SelKind.Edge) return s.EdgePoints(it);
        var o = s.Find(it.Obj) ?? throw new FormatException("That pick no longer exists.");
        int edges = o.Mesh.EdgeSegStart.Length - 1; var pieces = new List<List<Vec3>>();
        for (int e = 0; e < edges; e++) { var p = s.EdgePoints(new SelItem(it.Obj, SelKind.Edge, e)); if (p.Count >= 2) pieces.Add(p); }
        return Join(pieces);
    }

    public static List<Vec3> Join(List<List<Vec3>> pieces)
    {
        if (pieces.Count == 0) return [];
        var res = new List<Vec3>(pieces[0]); var left = pieces.Skip(1).ToList();
        while (left.Count > 0)
        {
            double best = double.MaxValue; int bi = 0; bool flip = false, front = false;
            for (int i = 0; i < left.Count; i++)
            {
                double a = (res[^1] - left[i][0]).Length, b = (res[^1] - left[i][^1]).Length, c = (res[0] - left[i][^1]).Length, d = (res[0] - left[i][0]).Length;
                foreach (var (dist, fl, fr) in new[] { (a, false, false), (b, true, false), (c, false, true), (d, true, true) }) if (dist < best) { best = dist; bi = i; flip = fl; front = fr; }
            }
            var piece = left[bi]; left.RemoveAt(bi); if (flip) piece = [.. piece.AsEnumerable().Reverse()];
            if (front) { piece.AddRange(res.Skip(1)); res = piece; } else res.AddRange(piece.Skip(1));
        }
        return res;
    }
}

/// <summary>A spring, damper or actuator (hydraulic or electric cylinder) between a point on one part and a point on another, or fixed in space.</summary>
public sealed class MotionSpringCommand : Command
{
    readonly SelectionSlot _a, _b;
    readonly NumberParam _k, _c, _push, _rest;
    readonly BoolParam _auto;

    public MotionSpringCommand()
    {
        _a = Slot(new SelectionSlot { Label = "First end (a face, circle or edge on a part)", Filter = SelFilter.Face | SelFilter.Edge | SelFilter.AnyObject });
        _b = Slot(new SelectionSlot { Label = "Second end (on another part, or on a fixed body for a point in space)", Filter = SelFilter.Face | SelFilter.Edge | SelFilter.AnyObject });
        _k = Num("Stiffness", 1, 0, 1e9, 0.1, "N/mm");
        _c = Num("Damping", 0, 0, 1e9, 0.001, "N·s/mm");
        _push = Num("Actuator push (negative pulls)", 0, -1e9, 1e9, 10, "N");
        _auto = Check("Free length is the current distance", true);
        _rest = Num("Free length", 100, 0, 1e9, 5, "mm");
    }

    public override string Title => "Spring / Actuator";
    public override string Icon => "motion-spring";
    public override string OkText => "Add";
    public override string Hint => "A spring, damper or actuator along the line between two points. Stiffness and damping make a spring-damper (a suspension, a return spring); an actuator push makes a cylinder that pushes its ends apart with a constant force (its pressure times its area). The second end can be on a fixed body to anchor it in space. It acts when a Motion Study includes masses and forces.";

    protected override void UpdateVisibility() { if (_rest != null) _rest.IsVisible = !_auto.Value; }

    (SpringInfo info, Vec3 a, Vec3 b)? Plan()
    {
        if (_a.Items.Count == 0 || _b.Items.Count == 0) return null;
        var ca = MotionPicks.ComponentOf(S, _a.Items[0]) ?? throw new FormatException("The first end must be on a component of the assembly (a part that can move).");
        var cb = MotionPicks.ComponentOf(S, _b.Items[0]);
        if (cb != null && cb.Component!.Key == ca.Component!.Key) throw new FormatException("Pick the two ends on two different parts.");
        var wa = MotionPicks.PointOf(S, _a.Items[0]); var wb = MotionPicks.PointOf(S, _b.Items[0]);
        double len = (wb - wa).Length;
        string name = _k.Value == 0 && _push.Value != 0 ? "Actuator" : "Spring";
        var info = new SpringInfo(CadSession.NewKey(), name, ca.Component!.Key, MotionPicks.Local(ca, wa), cb?.Component!.Key, cb != null ? MotionPicks.Local(cb, wb) : [wb.X, wb.Y, wb.Z],
            _k.Value, _c.Value, _auto.Value ? len : _rest.Value, _push.Value);
        return (info, wa, wb);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var plan = Plan(); if (plan == null) return null;
        var (info, a, b) = plan.Value;
        Results.Add(new ResultLine("Between", $"{S.ComponentByKey(info.CompA)!.Name}  →  {(info.CompB == null ? "a fixed point in space" : S.ComponentByKey(info.CompB)!.Name)}"));
        Results.Add(new ResultLine("Length now", $"{(b - a).Length:0.###} mm   (free length {info.RestLength:0.###} mm)"));
        if (info.Stiffness > 0) Results.Add(new ResultLine("Force at rest position", $"{info.Stiffness * ((b - a).Length - info.RestLength):0.###} N pulling the ends together"));
        if (info.Push != 0) Results.Add(new ResultLine("Actuator", $"{Math.Abs(info.Push):0.###} N {(info.Push > 0 ? "pushing the ends apart" : "pulling the ends together")}"));
        var o = new CommandOutput { Label = info.Name, Icon = Icon };
        o.Add.Add(new(Occ.Line(a, a + new Vec3(0, 0, 0.01)), info.Name, ObjKind.Joint, Asm: info));
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_a.Items.Count == 0) return;
        var a = MotionPicks.PointOf(s, _a.Items[0]); o.AddDot(a, 0xFF3FCF8E, 8);
        if (_b.Items.Count > 0) { var b = MotionPicks.PointOf(s, _b.Items[0]); o.AddDot(b, 0xFF3FCF8E, 8); o.AddLine(a, b, 0xFF3FCF8E, 2.4f); }
    }
}

/// <summary>A force or torque on a part, constant or changing in time.</summary>
public sealed class MotionLoadCommand : Command
{
    readonly SelectionSlot _where;
    readonly ChoiceParam _kind, _dir, _shape;
    readonly BoolParam _fixed;
    readonly NumberParam _force, _moment, _start, _length, _freq;

    bool Torque => _kind.Index == 1;
    double Size => Torque ? _moment.Value : _force.Value;

    public MotionLoadCommand(bool torque = false)
    {
        _where = Slot(new SelectionSlot { Label = "Where it acts (a face, circle or edge of a part)", Filter = SelFilter.Face | SelFilter.Edge | SelFilter.AnyObject });
        _kind = Choice("Load", torque ? 1 : 0, "Force", "Torque");
        _force = Num("Force", 10, -1e9, 1e9, 1, "N");
        _moment = Num("Torque", 1, -1e9, 1e9, 0.1, "N·m");
        _dir = Choice("Direction or axis", 4, MotionPicks.AxisNames);
        _fixed = Check("Turns with the part", false);
        _shape = Choice("In time", 0, "Constant", "Pulse (on for a while)", "Sine (alternating)");
        _start = Num("Starts at", 0, 0, 1e6, 0.1, "s");
        _length = Num("Lasts", 0.1, 0.0001, 1e6, 0.05, "s");
        _freq = Num("Frequency", 1, 0.0001, 1e6, 0.5, "Hz");
    }

    public override string Title => "Force / Torque";
    public override string Icon => "motion-force";
    public override string OkText => "Add";
    public override string Hint => "A push or a turning moment on a part: a weight, a wind load, a punch stroke. It can be constant, a pulse or alternating, and its direction is fixed in space or turns with the part. It acts when a Motion Study includes masses and forces.";

    protected override void UpdateVisibility()
    {
        if (_shape == null) return;
        _force.IsVisible = !Torque; _moment.IsVisible = Torque;
        _start.IsVisible = _length.IsVisible = _shape.Index == 1; _freq.IsVisible = _shape.Index == 2;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_where.Items.Count == 0) return null;
        var comp = MotionPicks.ComponentOf(S, _where.Items[0]) ?? throw new FormatException("Pick a part of the assembly (a component).");
        var world = MotionPicks.PointOf(S, _where.Items[0]);
        Vec3 dir;
        if (_dir.Index < MotionPicks.Axes.Length) dir = MotionPicks.Axes[_dir.Index];
        else dir = (S.FrameOf(_where.Items[0])?.z ?? S.FaceFrame(_where.Items[0])?.normal) ?? throw new FormatException("That pick has no direction. Choose an axis instead.");
        if (_fixed.Value) dir = comp.Component!.Placement.Inverse().Rotate(dir);
        string name = Torque ? "Torque" : "Force";
        var info = new LoadInfo(CadSession.NewKey(), name, Torque, comp.Component!.Key, MotionPicks.Local(comp, world), [dir.X, dir.Y, dir.Z], _fixed.Value, Size, _shape.Index, _start.Value, _length.Value, _freq.Value);
        Results.Add(new ResultLine("On", comp.Name));
        Results.Add(new ResultLine(name, $"{Size:0.###} {(Torque ? "N·m" : "N")}, {_shape.Selected.ToLowerInvariant()}"));
        var o = new CommandOutput { Label = name, Icon = Icon };
        o.Add.Add(new(Occ.Line(world, world + new Vec3(0, 0, 0.01)), name, ObjKind.Joint, Asm: info));
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_where.Items.Count > 0) o.AddDot(MotionPicks.PointOf(s, _where.Items[0]), 0xFFFF6B6F, 8);
    }
}
