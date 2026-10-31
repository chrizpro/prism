using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>One rotation step of a pattern instance (applied about a pivot, in world space).</summary>
public readonly record struct Rot(Vec3 Pivot, Vec3 Axis, double Deg);

/// <summary>Where one pattern instance goes: translate by Pre, rotate through Rots in order, then translate by Post.</summary>
public readonly record struct Placement(Vec3 Pre, Rot[] Rots, Vec3 Post, Vec3 Anchor)
{
    public bool IsIdentity => Pre.Length < 1e-9 && Post.Length < 1e-9 && Rots.All(r => Math.Abs(r.Deg) < 1e-9);
    public static Placement Move(Vec3 delta, Vec3 anchor) => new(Vec3.Zero, [], delta, anchor);
}

/// <summary>Geometry helpers for patterns: ordered polylines from shapes, rotation and point-in-region tests.</summary>
public static class PatternGeometry
{
    /// <summary>Connected polylines of all edges in the shapes (closed ones have first == last).</summary>
    public static List<List<Vec3>> Chains(IEnumerable<Shape> shapes)
    {
        var segs = new List<(Vec3 a, Vec3 b)>();
        foreach (var sh in shapes)
        {
            var m = sh.Tessellate(); var e = m.EdgePoints;
            for (int i = 0; i + 5 < e.Length; i += 6)
                segs.Add((new Vec3(e[i], e[i + 1], e[i + 2]), new Vec3(e[i + 3], e[i + 4], e[i + 5])));
        }
        static (long, long, long) Key(Vec3 p) => ((long)Math.Round(p.X * 1e3), (long)Math.Round(p.Y * 1e3), (long)Math.Round(p.Z * 1e3));
        var at = new Dictionary<(long, long, long), List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            foreach (var p in new[] { segs[i].a, segs[i].b })
            {
                if (!at.TryGetValue(Key(p), out var l)) at[Key(p)] = l = [];
                l.Add(i);
            }
        }
        var used = new bool[segs.Count];
        var result = new List<List<Vec3>>();
        // start from open ends first so open paths are walked end to end, then pick up loops
        var order = Enumerable.Range(0, segs.Count)
            .OrderBy(i => (at[Key(segs[i].a)].Count == 1 || at[Key(segs[i].b)].Count == 1) ? 0 : 1).ToList();
        foreach (var s0 in order)
        {
            if (used[s0]) continue;
            used[s0] = true;
            var chain = new List<Vec3>();
            bool aOpen = at[Key(segs[s0].a)].Count == 1;
            var (first, second) = aOpen || at[Key(segs[s0].b)].Count != 1 ? (segs[s0].a, segs[s0].b) : (segs[s0].b, segs[s0].a);
            chain.Add(first); chain.Add(second);
            while (true)
            {
                var tail = chain[^1];
                int next = at[Key(tail)].FirstOrDefault(i => !used[i], -1);
                if (next < 0) break;
                used[next] = true;
                chain.Add(Key(segs[next].a) == Key(tail) ? segs[next].b : segs[next].a);
            }
            result.Add(chain);
        }
        return result;
    }

    public static double Length(IReadOnlyList<Vec3> pts) { double l = 0; for (int i = 0; i + 1 < pts.Count; i++) l += pts[i].DistanceTo(pts[i + 1]); return l; }
    public static bool IsClosed(IReadOnlyList<Vec3> pts) => pts.Count > 3 && pts[0].DistanceTo(pts[^1]) < 1e-3;

    /// <summary>Point and unit tangent at arc length s along a polyline.</summary>
    public static (Vec3 p, Vec3 t) Sample(IReadOnlyList<Vec3> pts, double s)
    {
        double acc = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            double l = pts[i].DistanceTo(pts[i + 1]);
            if (l < 1e-12) continue;
            if (s <= acc + l || i == pts.Count - 2) { double u = Math.Clamp((s - acc) / l, 0, 1); return (Vec3.Lerp(pts[i], pts[i + 1], u), (pts[i + 1] - pts[i]).Normalized()); }
            acc += l;
        }
        return (pts[0], Vec3.UnitX);
    }

    public static Vec3 RotatePoint(Vec3 p, Vec3 origin, Vec3 axis, double deg)
    {
        var k = axis.Normalized(); double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var v = p - origin;
        return origin + v * c + Vec3.Cross(k, v) * s + k * (Vec3.Dot(k, v) * (1 - c));
    }

    /// <summary>Smallest rotation that turns direction a onto direction b: (axis, degrees).</summary>
    public static Rot Align(Vec3 pivot, Vec3 a, Vec3 b)
    {
        a = a.Normalized(); b = b.Normalized();
        var ax = Vec3.Cross(a, b); double sin = ax.Length, cos = Vec3.Dot(a, b);
        if (sin < 1e-9)
        {
            if (cos > 0) return new Rot(pivot, Vec3.UnitZ, 0);
            var perp = Math.Abs(a.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
            return new Rot(pivot, Vec3.Cross(a, perp).Normalized(), 180);
        }
        return new Rot(pivot, ax / sin, Math.Atan2(sin, cos) * 180 / Math.PI);
    }

    public static bool Inside(IReadOnlyList<List<(double u, double v)>> loops, double u, double v)
    {
        bool inside = false;
        foreach (var l in loops)
            for (int i = 0, j = l.Count - 1; i < l.Count; j = i++)
                if ((l[i].v > v) != (l[j].v > v) && u < (l[j].u - l[i].u) * (v - l[i].v) / (l[j].v - l[i].v) + l[i].u) inside = !inside;
        return inside;
    }

    public static double DistanceToLoops(IReadOnlyList<List<(double u, double v)>> loops, double u, double v)
    {
        double best = double.MaxValue;
        foreach (var l in loops)
            for (int i = 0, j = l.Count - 1; i < l.Count; j = i++)
            {
                double ax = l[j].u, ay = l[j].v, bx = l[i].u, by = l[i].v, dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-18 ? 0 : Math.Clamp(((u - ax) * dx + (v - ay) * dy) / len2, 0, 1);
                double px = ax + t * dx - u, py = ay + t * dy - v;
                best = Math.Min(best, Math.Sqrt(px * px + py * py));
            }
        return best;
    }
}

/// <summary>
/// Common behaviour of all pattern tools: pick objects, compute instance placements, then either create new bodies,
/// or fuse the instances into / cut them out of a target body (patterning a hole or a boss).
/// </summary>
public abstract class PatternCommand : Command
{
    public const int MaxInstances = 400;
    protected readonly SelectionSlot Src;
    SelectionSlot _target = null!;
    ChoiceParam _result = null!;
    TextParam _skip = null!;
    BoolParam _moveOriginal = null!;
    readonly bool _offerMove;
    readonly List<(Vec3 p, bool skipped)> _marks = [];
    protected readonly List<(Vec3 a, Vec3 b)> Guides = [];
    protected readonly List<IReadOnlyList<Vec3>> GuideLines = [];
    protected readonly List<HandleSpec> PatHandles = [];
    public override IReadOnlyList<HandleSpec> Handles => PatHandles.ToArray();
    public override HandleSpec? Handle => PatHandles.Count > 0 ? PatHandles[0] : null;

    protected PatternCommand(bool offerMoveOriginal)
    {
        _offerMove = offerMoveOriginal;
        Src = Slot(new SelectionSlot { Label = "Objects to pattern", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.CurveObj, Multi = true });
    }

    /// <summary>Call at the end of the derived constructor so these controls come last in the panel.</summary>
    protected void AddCommonControls()
    {
        _result = Add(new ChoiceParam { Label = "Result", Caption = "Operation", Options = ["New bodies", "Join into a body", "Cut from a body"], IsResult = true });
        _target = Slot(new SelectionSlot { Label = "Target body", Filter = SelFilter.Solid, Optional = true, IsVisible = false, IsResult = true });
        _skip = Text("Skip instances", "", "e.g. 3, 5-7 (numbers shown in the viewport)");
        if (_offerMove) _moveOriginal = Check("Move the original to the first position", true);
    }

    public override string Icon => "pattern-linear";
    public override string OkText => "Create pattern";

    protected override void UpdateVisibility()
    {
        _target.IsVisible = _result.Index != 0;
        if (_moveOriginal != null) _moveOriginal.IsVisible = _result.Index == 0;
        UpdateExtraVisibility();
    }
    protected virtual void UpdateExtraVisibility() { }

    protected void Row(string label, string value, int level = 0) => Results.Add(new ResultLine(label, value, level));

    /// <summary>Instance placements, instance 1 first. refPoint is the center of the picked objects.</summary>
    protected abstract List<Placement> Layout(Vec3 refPoint, Vec3 min, Vec3 max);

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var l in GuideLines) o.AddPolyline(l, 0xFF35B6FF, 2f);
        foreach (var (a, b) in Guides) o.AddLine(a, b, 0xFF35B6FF, 2f);
        for (int i = 0; i < _marks.Count; i++)
        {
            o.AddDot(_marks[i].p, _marks[i].skipped ? 0xFFE5484D : 0xFFFF8A1F, _marks[i].skipped ? 9 : 7);
            if (_marks.Count <= 60) o.AddLabel(_marks[i].p, (i + 1).ToString(), false, 1);
        }
    }

    static string StripNumber(string n) { int i = n.LastIndexOf(' '); return i > 0 && int.TryParse(n[(i + 1)..], out _) ? n[..i] : n; }

    static HashSet<int> ParseSkip(string text)
    {
        var set = new HashSet<int>();
        foreach (var tok in text.Split([',', ';', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = tok.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b)) { for (int i = Math.Min(a, b); i <= Math.Max(a, b) && i < 100000; i++) set.Add(i); }
            else if (int.TryParse(tok, out var n)) set.Add(n);
        }
        return set;
    }

    static Shape Apply(Shape s, Placement p)
    {
        if (p.Pre.Length > 1e-9) s = Occ.Translate(s, p.Pre);
        foreach (var r in p.Rots) if (Math.Abs(r.Deg) > 1e-9) s = Occ.Rotate(s, r.Pivot, r.Axis, r.Deg);
        if (p.Post.Length > 1e-9) s = Occ.Translate(s, p.Post);
        return s;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear(); _marks.Clear(); Guides.Clear(); GuideLines.Clear(); PatHandles.Clear();
        if (!Complete) return null;
        var srcs = ObjectsOf(Src).Distinct().ToList();
        if (srcs.Count == 0) return null;

        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var max = -min;
        foreach (var o in srcs)
        {
            var p = o.Shape.Props();
            min = new(Math.Min(min.X, p.Min.X), Math.Min(min.Y, p.Min.Y), Math.Min(min.Z, p.Min.Z));
            max = new(Math.Max(max.X, p.Max.X), Math.Max(max.Y, p.Max.Y), Math.Max(max.Z, p.Max.Z));
        }
        var center = (min + max) * 0.5;
        var layout = Layout(center, min, max);
        if (layout.Count == 0) { Row("No instances", "Adjust the parameters", 2); return null; }
        if (layout.Count > MaxInstances) { Row("Too many instances", $"Showing the first {MaxInstances} of {layout.Count}", 2); layout = layout.Take(MaxInstances).ToList(); }

        var skip = ParseSkip(_skip.Value);
        if (skip.Remove(1)) Row("Instance 1", "is the original and cannot be skipped", 2);
        var kept = new List<Placement>();
        for (int i = 0; i < layout.Count; i++)
        {
            bool sk = skip.Contains(i + 1);
            _marks.Add((layout[i].Anchor, sk));
            if (!sk) kept.Add(layout[i]);
        }
        Row("Instances", skip.Count > 0 ? $"{kept.Count} ({layout.Count} - {layout.Count - kept.Count} skipped)" : kept.Count.ToString(), 1);

        var o2 = new CommandOutput { Icon = Icon };
        int mode = _result.Index;
        if (mode == 0)
        {
            bool moveFirst = _offerMove && _moveOriginal.Value;
            o2.Label = "Pattern";
            for (int i = 0; i < kept.Count; i++)
            {
                if (kept[i].IsIdentity) continue;
                foreach (var obj in srcs)
                {
                    var shape = Apply(obj.Shape, kept[i]);
                    if (i == 0 && moveFirst) o2.Replace.Add((obj, shape));
                    else o2.Add.Add(new(shape, StripNumber(obj.Name), obj.Kind, obj.Color, obj.SketchPlane, null, obj.Look));
                }
            }
            return o2;
        }

        if (_target.Items.Count == 0) { Row("Pick a target body", "the pattern will be " + (mode == 1 ? "joined to" : "cut from") + " it", 2); return null; }
        var target = S.Find(_target.Items[0].Obj)!;
        var tools = srcs.Where(x => x.Id != target.Id).ToList();
        if (tools.Count == 0) { Row("Objects", "the target cannot also be the pattern source", 2); return null; }
        var res = target.Shape;
        foreach (var pl in kept)
            foreach (var t in tools)
            {
                var inst = Apply(t.Shape, pl);
                res = mode == 1 ? Occ.Fuse(res, inst) : Occ.Cut(res, inst);
            }
        o2.Label = mode == 1 ? "Pattern (join)" : "Pattern (cut)";
        o2.Replace.Add((target, res));
        o2.Remove.AddRange(tools);
        return o2;
    }

    /// <summary>World direction chosen by a Choice (X, Y, Z or a picked edge / round face).</summary>
    protected (Vec3 origin, Vec3 dir)? Dir(ChoiceParam c, SelectionSlot pick)
    {
        if (c.Index < 3) return (Vec3.Zero, c.Index == 0 ? Vec3.UnitX : c.Index == 1 ? Vec3.UnitY : Vec3.UnitZ);
        if (pick.Items.Count == 0) { Row("Direction", "pick an edge or round face", 2); return null; }
        var sh = ShapeOf(pick);
        if (sh != null && Occ.AxisOf(sh) is { } ax) return (ax.Origin, ax.Dir.Normalized());
        Row("Direction", "that item has no axis (use a straight edge, circle or round face)", 2);
        return null;
    }
}

// =================================================================== Linear
public sealed class LinearPatternCommand : PatternCommand
{
    readonly ChoiceParam _d1, _d2, _d3, _mode;
    readonly IntParam _n1, _n2, _n3;
    readonly NumberParam _s1, _s2, _s3;
    readonly SelectionSlot _e1, _e2, _e3;

    public LinearPatternCommand() : base(false)
    {
        string[] dirs = ["X axis", "Y axis", "Z axis", "Picked edge / axis"];
        _mode = Choice("Spacing is", 0, "Between instances", "Overall extent");
        _d1 = Choice("Direction 1", 0, dirs);
        _e1 = Slot(new SelectionSlot { Label = "Direction 1 (edge or round face)", Filter = SelFilter.Edge | SelFilter.Face, Optional = true, IsVisible = false });
        _n1 = Add(new IntParam { Label = "Count 1", Value = 4, Min = 1, Max = 200 });
        _s1 = Num("Spacing 1", 20);
        _d2 = Choice("Direction 2", 1, dirs);
        _e2 = Slot(new SelectionSlot { Label = "Direction 2 (edge or round face)", Filter = SelFilter.Edge | SelFilter.Face, Optional = true, IsVisible = false });
        _n2 = Add(new IntParam { Label = "Count 2", Value = 1, Min = 1, Max = 200 });
        _s2 = Num("Spacing 2", 20);
        _d3 = Choice("Direction 3", 2, dirs);
        _e3 = Slot(new SelectionSlot { Label = "Direction 3 (edge or round face)", Filter = SelFilter.Edge | SelFilter.Face, Optional = true, IsVisible = false });
        _n3 = Add(new IntParam { Label = "Count 3", Value = 1, Min = 1, Max = 200 });
        _s3 = Num("Spacing 3", 20);
        AddCommonControls();
    }

    public override string Title => "Linear Pattern";
    public override string Hint => "Pick the objects, then set how many copies go in each direction. Use counts in two or three directions for grids and blocks; negative spacing flips a direction.";
    public override string Icon => "pattern-linear";

    protected override void UpdateExtraVisibility()
    {
        _e1.IsVisible = _d1.Index == 3; _e2.IsVisible = _d2.Index == 3 && _n2.Value > 1; _e3.IsVisible = _d3.Index == 3 && _n3.Value > 1;
        _d2.IsVisible = _s2.IsVisible = _n2.Value > 1; _d3.IsVisible = _s3.IsVisible = _n3.Value > 1;
    }

    protected override List<Placement> Layout(Vec3 r, Vec3 min, Vec3 max)
    {
        var dirs = new[] { (_d1, _e1, _n1, _s1), (_d2, _e2, _n2, _s2), (_d3, _e3, _n3, _s3) };
        var axes = new List<(Vec3 d, int n, double step)>();
        foreach (var (dc, es, n, sp) in dirs)
        {
            if (n.Value <= 1) { axes.Add((Vec3.UnitX, 1, 0)); continue; }
            var d = Dir(dc, es);
            if (d == null) return [];
            double step = _mode.Index == 1 ? sp.Value / (n.Value - 1) : sp.Value;
            axes.Add((d.Value.dir, n.Value, step));
        }
        var list = new List<Placement>();
        for (int k = 0; k < axes[2].n; k++)
            for (int j = 0; j < axes[1].n; j++)
                for (int i = 0; i < axes[0].n; i++)
                {
                    var delta = axes[0].d * (i * axes[0].step) + axes[1].d * (j * axes[1].step) + axes[2].d * (k * axes[2].step);
                    list.Add(Placement.Move(delta, r + delta));
                }
        for (int a = 0; a < 3; a++)
            if (axes[a].n > 1)
            {
                Guides.Add((r, r + axes[a].d * (axes[a].step * (axes[a].n - 1))));
                var sp = a == 0 ? _s1 : a == 1 ? _s2 : _s3;
                PatHandles.Add(new HandleSpec(r, axes[a].d, sp, _mode.Index == 1 ? 1.0 : 1.0 / (axes[a].n - 1)));
            }
        return list;
    }
}

// =================================================================== Circular
public sealed class CircularPatternCommand : PatternCommand
{
    readonly ChoiceParam _axis, _through, _angleMode;
    readonly SelectionSlot _axisPick;
    readonly IntParam _count, _rings;
    readonly NumberParam _angle, _ringSp;
    readonly BoolParam _rotate;

    public CircularPatternCommand() : base(false)
    {
        _axis = Choice("Axis", 2, "X axis", "Y axis", "Z axis", "Picked edge / round face");
        _axisPick = Slot(new SelectionSlot { Label = "Axis (circle, line or round face)", Filter = SelFilter.Edge | SelFilter.Face, Optional = true, IsVisible = false });
        _through = Choice("Axis passes through", 0, "World origin", "Center of the objects");
        _count = Add(new IntParam { Label = "Count", Value = 6, Min = 1, Max = 200 });
        _angleMode = Choice("Angle", 0, "Total angle", "Angle between instances");
        _angle = Num("Angle value", 360, -3600, 3600, 5, "°");
        _rings = Add(new IntParam { Label = "Rings", Value = 1, Min = 1, Max = 50 });
        _ringSp = Num("Ring spacing", 20);
        _rotate = Check("Rotate the instances", true);
        AddCommonControls();
    }

    public override string Title => "Circular Pattern";
    public override string Hint => "Pick the objects and an axis. A full 360° spreads the copies evenly around; add rings for concentric copies.";
    public override string Icon => "pattern-circular";

    protected override void UpdateExtraVisibility() { _axisPick.IsVisible = _axis.Index == 3; _through.IsVisible = _axis.Index < 3; _ringSp.IsVisible = _rings.Value > 1; }

    protected override List<Placement> Layout(Vec3 r, Vec3 min, Vec3 max)
    {
        var ax = Dir(_axis, _axisPick);
        if (ax == null) return [];
        var (o0, d) = ax.Value;
        var origin = _axis.Index < 3 ? (_through.Index == 0 ? Vec3.Zero : r) : o0;
        var radial = r - origin; radial -= d * Vec3.Dot(radial, d);
        if (radial.Length < 1e-6) { var h = Math.Abs(d.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY; radial = Vec3.Cross(d, h); }
        double radialLen = radial.Length > 1e-6 ? radial.Length : 0;
        radial = radial.Normalized();

        double step;
        int n = _count.Value;
        if (_angleMode.Index == 1) step = _angle.Value;
        else if (Math.Abs(Math.Abs(_angle.Value) - 360) < 1e-6) step = _angle.Value / n;
        else step = n > 1 ? _angle.Value / (n - 1) : 0;

        var list = new List<Placement>();
        for (int j = 0; j < _rings.Value; j++)
        {
            var pre = radial * (j * _ringSp.Value);
            for (int i = 0; i < n; i++)
            {
                double a = i * step;
                var at = r + pre;
                if (_rotate.Value) list.Add(new Placement(pre, [new Rot(origin, d, a)], Vec3.Zero, PatternGeometry.RotatePoint(at, origin, d, a)));
                else { var q = PatternGeometry.RotatePoint(at, origin, d, a); list.Add(new Placement(pre, [], q - at, q)); }
            }
        }
        double size = (max - min).Length * 1.2 + 10;
        Guides.Add((origin - d * size, origin + d * size));
        PatHandles.Add(new HandleSpec(origin, d, _angle, 1, radial, radialLen > 1e-6 ? radialLen : Math.Max(size * 0.3, 5)));
        if (_rings.Value > 1) PatHandles.Add(new HandleSpec(r, radial, _ringSp));
        return list;
    }
}

// =================================================================== Along a curve
public sealed class CurvePatternCommand : PatternCommand
{
    readonly SelectionSlot _path;
    readonly ChoiceParam _dist, _orient, _align;
    readonly IntParam _count;
    readonly NumberParam _spacing, _start, _end, _twist;
    readonly BoolParam _reverse;

    public CurvePatternCommand() : base(true)
    {
        _path = Slot(new SelectionSlot { Label = "Path (edges or curves)", Filter = SelFilter.Edge | SelFilter.CurveObj | SelFilter.SketchObj, Multi = true, SelectionRuleIndex = 1 });
        _dist = Choice("Distribution", 0, "Equal count along the path", "Fixed spacing");
        _count = Add(new IntParam { Label = "Count", Value = 8, Min = 1, Max = 400 });
        _spacing = Num("Spacing", 15, 0.001);
        _start = Num("Start offset", 0, 0);
        _end = Num("End offset", 0, 0);
        _reverse = Check("Reverse direction", false);
        _orient = Choice("Orientation", 1, "Keep orientation", "Follow the curve");
        _align = Choice("Aligned axis", 0, "X axis follows the curve", "Y axis follows the curve", "Z axis follows the curve");
        _twist = Num("Twist", 0, -360, 360, 5, "°");
        AddCommonControls();
    }

    public override string Title => "Pattern Along Curve";
    public override string Hint => "Pick the objects, then the curve to follow. Each copy lands on the path, optionally turned to follow its direction.";
    public override string Icon => "pattern-curve";

    protected override void UpdateExtraVisibility()
    {
        _count.IsVisible = _dist.Index == 0; _spacing.IsVisible = _dist.Index == 1;
        _align.IsVisible = _twist.IsVisible = _orient.Index == 1;
    }

    protected override List<Placement> Layout(Vec3 r, Vec3 min, Vec3 max)
    {
        var shapes = _path.Items.Select(i => S.ShapeOf(i)).Where(x => x != null).Select(x => x!).ToList();
        var chains = PatternGeometry.Chains(shapes);
        if (chains.Count == 0) { Row("Path", "no curve found in the selection", 2); return []; }
        var pts = chains.OrderByDescending(PatternGeometry.Length).First();
        if (chains.Count > 1) Row("Path", $"{chains.Count} separate curves picked - using the longest", 2);
        if (_reverse.Value) { pts = [.. pts]; pts.Reverse(); }
        double len = PatternGeometry.Length(pts), usable = len - _start.Value - _end.Value;
        bool closed = PatternGeometry.IsClosed(pts) && _start.Value == 0 && _end.Value == 0;
        if (usable <= 0) { Row("Path", "the offsets are longer than the curve", 2); return []; }
        int n; double step;
        if (_dist.Index == 0) { n = _count.Value; step = n <= 1 ? 0 : closed ? usable / n : usable / (n - 1); }
        else { step = _spacing.Value; n = (int)Math.Floor(usable / step + 1e-9) + (closed ? 0 : 1); if (closed && n * step > usable + 1e-6) n--; n = Math.Max(n, 1); }
        Row("Path length", $"{len:0.##} mm");
        if (_dist.Index == 1) Row("Spacing", $"{step:0.##} mm", 0);
        else Row("Spacing", $"{step:0.##} mm");

        var axisLocal = _align.Index == 0 ? Vec3.UnitX : _align.Index == 1 ? Vec3.UnitY : Vec3.UnitZ;
        var list = new List<Placement>();
        for (int i = 0; i < Math.Min(n, MaxInstances + 1); i++)
        {
            var (p, t) = PatternGeometry.Sample(pts, _start.Value + i * step);
            var rots = new List<Rot>();
            if (_orient.Index == 1)
            {
                rots.Add(PatternGeometry.Align(r, axisLocal, t));
                if (Math.Abs(_twist.Value) > 1e-9) rots.Add(new Rot(r, t, _twist.Value));
            }
            list.Add(new Placement(Vec3.Zero, [.. rots], p - r, p));
        }
        GuideLines.Add(pts);
        if (_dist.Index == 1 && pts.Count > 1) { var (p0, t0) = PatternGeometry.Sample(pts, _start.Value); PatHandles.Add(new HandleSpec(p0, t0, _spacing)); }
        return list;
    }
}

// =================================================================== Fill a region
public sealed class FillPatternCommand : PatternCommand
{
    readonly SelectionSlot _region;
    readonly ChoiceParam _layout, _origin;
    readonly NumberParam _spacing, _rowSpacing, _margin, _angle;

    public FillPatternCommand() : base(true)
    {
        _region = Slot(new SelectionSlot { Label = "Region to fill (planar face or closed sketch)", Filter = SelFilter.Face | SelFilter.SketchObj | SelFilter.CurveObj });
        _layout = Choice("Layout", 1, "Square grid", "Staggered grid (hex)", "Concentric rings", "Spiral (golden angle)");
        _spacing = Num("Spacing", 12, 0.01);
        _rowSpacing = Num("Row / ring spacing (0 = automatic)", 0, 0);
        _margin = Num("Margin from the boundary", 3, 0);
        _angle = Num("Layout rotation", 0, -360, 360, 5, "°");
        _origin = Choice("Layout origin", 0, "Center of the region", "Position of the original");
        AddCommonControls();
    }

    public override string Title => "Fill Pattern";
    public override string Hint => "Pick the objects, then a planar face or closed sketch to fill. Copies are placed at the chosen spacing wherever they fit inside, keeping the margin from the boundary.";
    public override string Icon => "pattern-fill";

    protected override void UpdateExtraVisibility() { _rowSpacing.IsVisible = _layout.Index != 3; }

    protected override List<Placement> Layout(Vec3 r, Vec3 min, Vec3 max)
    {
        var shape = ShapeOf(_region);
        if (shape == null) return [];
        var closed = PatternGeometry.Chains([shape]).Where(PatternGeometry.IsClosed).ToList();
        if (closed.Count == 0) { Row("Region", "needs a closed outline (planar face or closed sketch)", 2); return []; }

        // plane of the region from the biggest loop (Newell normal)
        Vec3 Newell(List<Vec3> p) { Vec3 n = Vec3.Zero; for (int i = 0; i < p.Count - 1; i++) { var a = p[i]; var b = p[i + 1]; n += new Vec3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y)); } return n; }
        var outer = closed.OrderByDescending(c => Newell(c).Length).First();
        var normal = Newell(outer).Normalized();
        if (normal.Length < 0.5) { Row("Region", "is not planar", 2); return []; }
        var o = outer.Aggregate(Vec3.Zero, (a, b) => a + b) / outer.Count;
        var href = Math.Abs(normal.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
        var U = Vec3.Cross(normal, href).Normalized(); var V = Vec3.Cross(normal, U).Normalized();
        var plane = new Plane3(o, U, V);

        var loops = new List<List<(double u, double v)>>();
        foreach (var c in closed)
        {
            // keep only loops lying in the same plane
            if (c.Any(p => Math.Abs(Vec3.Dot(p - o, normal)) > 1e-2 * Math.Max(1, (max - min).Length))) continue;
            loops.Add(c.Take(c.Count - 1).Select(p => plane.Project(p)).ToList());
        }
        double minU = loops.SelectMany(l => l).Min(p => p.u), maxU = loops.SelectMany(l => l).Max(p => p.u);
        double minV = loops.SelectMany(l => l).Min(p => p.v), maxV = loops.SelectMany(l => l).Max(p => p.v);

        var rProj = r - normal * Vec3.Dot(r - o, normal);
        var (ru, rv) = plane.Project(rProj);
        double cu = _origin.Index == 0 ? (minU + maxU) / 2 : ru, cv = _origin.Index == 0 ? (minV + maxV) / 2 : rv;
        double rad = new[] { (minU, minV), (minU, maxV), (maxU, minV), (maxU, maxV) }.Max(c => Math.Sqrt((c.Item1 - cu) * (c.Item1 - cu) + (c.Item2 - cv) * (c.Item2 - cv))) + _spacing.Value;

        double sx = _spacing.Value, th = _angle.Value * Math.PI / 180, ct = Math.Cos(th), st = Math.Sin(th);
        var cand = new List<(double u, double v)>();
        void Add(double x, double y) { cand.Add((cu + x * ct - y * st, cv + x * st + y * ct)); }
        switch (_layout.Index)
        {
            case 0:
            case 1:
            {
                bool stag = _layout.Index == 1;
                double sy = _rowSpacing.Value > 0 ? _rowSpacing.Value : stag ? sx * 0.8660254 : sx;
                int nj = (int)Math.Ceiling(rad / sy), ni = (int)Math.Ceiling(rad / sx) + 1;
                for (int j = -nj; j <= nj; j++)
                    for (int i = -ni; i <= ni; i++) Add(i * sx + (stag && ((j % 2) + 2) % 2 == 1 ? sx / 2 : 0), j * sy);
                break;
            }
            case 2:
            {
                double rs = _rowSpacing.Value > 0 ? _rowSpacing.Value : sx;
                Add(0, 0);
                for (int k = 1; k * rs <= rad; k++)
                {
                    int m = Math.Max(1, (int)Math.Round(2 * Math.PI * k * rs / sx));
                    for (int i = 0; i < m; i++) { double a = 2 * Math.PI * i / m; Add(k * rs * Math.Cos(a), k * rs * Math.Sin(a)); }
                }
                break;
            }
            default:
            {
                double c0 = sx / 1.9;
                for (int k = 0; k < 20000; k++)
                {
                    double rr = c0 * Math.Sqrt(k); if (rr > rad) break;
                    double a = k * 2.399963229728653; Add(rr * Math.Cos(a), rr * Math.Sin(a));
                }
                break;
            }
        }

        var pts = cand.Where(p => PatternGeometry.Inside(loops, p.u, p.v) && PatternGeometry.DistanceToLoops(loops, p.u, p.v) >= _margin.Value - 1e-9).ToList();
        if (pts.Count == 0) { Row("Fill", "nothing fits - reduce the spacing or margin", 2); return []; }
        // the point nearest the original goes first, so "move original to first position" disturbs it least
        int near = 0; double best = double.MaxValue;
        for (int i = 0; i < pts.Count; i++) { double d = (pts[i].u - ru) * (pts[i].u - ru) + (pts[i].v - rv) * (pts[i].v - rv); if (d < best) { best = d; near = i; } }
        var first = pts[near]; pts.RemoveAt(near); pts.Insert(0, first);

        var list = new List<Placement>();
        foreach (var (u, v) in pts)
        {
            var delta = plane.PointAt(u, v) - rProj;
            list.Add(Placement.Move(delta, r + delta));
        }
        foreach (var c in closed) GuideLines.Add(c);
        var hc = plane.PointAt(cu, cv);
        PatHandles.Add(new HandleSpec(hc, (plane.PointAt(cu + ct, cv + st) - hc).Normalized(), _spacing));
        return list;
    }
}

// =================================================================== Table
public sealed class TablePatternCommand : PatternCommand
{
    readonly TextParam _rows;
    readonly ChoiceParam _kind;

    public TablePatternCommand() : base(false)
    {
        _kind = Choice("Rows are", 0, "Offsets from the original", "Absolute positions of the center");
        _rows = Text("Positions", "30, 0, 0\n60, 0, 0, 45\n90, 20, 0", "One per line: x, y, z [, rotation about Z in degrees]");
        AddCommonControls();
    }

    public override string Title => "Table Pattern";
    public override string Hint => "Place copies at exactly the positions you type, one per line: x, y, z and optionally a rotation about Z. Good for irregular layouts or data from a drawing.";
    public override string Icon => "pattern-table";

    protected override List<Placement> Layout(Vec3 r, Vec3 min, Vec3 max)
    {
        var list = new List<Placement> { Placement.Move(Vec3.Zero, r) };
        int line = 0;
        foreach (var raw in _rows.Value.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            line++;
            var f = raw.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null).ToList();
            if (f.Count < 3 || f.Any(x => x == null)) { Row($"Line {line}", "needs numbers: x, y, z [, angle]", 2); continue; }
            var p = new Vec3(f[0]!.Value, f[1]!.Value, f[2]!.Value);
            var delta = _kind.Index == 0 ? p : p - r;
            var rots = f.Count > 3 && Math.Abs(f[3]!.Value) > 1e-9 ? new[] { new Rot(r, Vec3.UnitZ, f[3]!.Value) } : [];
            list.Add(new Placement(Vec3.Zero, rots, delta, r + delta));
        }
        return list;
    }
}
