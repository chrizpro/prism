using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Prism.Kernel;

namespace Prism.Core.Sketching;

public enum EntType { Line, Circle, Arc, Spline, Point, Ellipse }

public sealed class SkPoint
{
    public int Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Offset along the drawing plane's normal. Zero for ordinary planar points; free 3D editing can lift a point off its plane.</summary>
    public double Z { get; set; }
}

/// <summary>A sketch entity. Lines use A,B; circles A (centre) + R; ellipses A (centre), B (end of the major axis) + R (semi-minor); arcs A (centre), B (start), C (end, counter-clockwise from start); splines use Pts.</summary>
public sealed class SkEntity
{
    public int Id { get; set; }
    public EntType Type { get; set; }
    public bool Construction { get; set; }
    public int A { get; set; } = -1;
    public int B { get; set; } = -1;
    public int C { get; set; } = -1;
    public double R { get; set; }
    public List<int> Pts { get; set; } = [];
    public bool Closed { get; set; }
    public bool ControlPoints { get; set; }
    /// <summary>Name of the shape this curve belongs to ("Rectangle 1", "Slot 2"...), so the panel can fold its parts together. Null for loose curves.</summary>
    public string? Group { get; set; }
}

public enum CType
{
    Coincident, PointOnEntity, Horizontal, Vertical, Parallel, Perpendicular, Collinear, Equal, Tangent, Concentric, Midpoint, Symmetric, Fixed,
    Distance, LineLength, HDistance, VDistance, Radius, Diameter, Angle, PointLineDistance,
}

/// <summary>
/// A geometric relation or a driving dimension. Refs are point or entity ids depending on the type
/// (see <see cref="SketchModel.Describe"/>). Value holds the dimension value (mm or degrees) or, for Fixed, the x coordinate (V2 = y).
/// </summary>
public sealed class SkConstraint
{
    public int Id { get; set; }
    public CType Type { get; set; }
    public int[] Refs { get; set; } = [];
    public double Value { get; set; }
    public double V2 { get; set; }
    public double? LabelU { get; set; }
    public double? LabelV { get; set; }
    public bool IsDimension { get; set; }
    /// <summary>Optional name of a dimension, unique within its sketch (case-insensitive). Other dimensions and other sketches refer to it as <c>name</c> or <c>sketch1.name</c>.</summary>
    public string? Name { get; set; }
    /// <summary>Optional expression that drives <see cref="Value"/>: numbers, units, named dimensions of this sketch, document parameters (<c>wall</c> or <c>doc.wall</c>) and dimensions of earlier sketches (<c>sketch1.width</c>).</summary>
    public string? Formula { get; set; }
    public bool Hidden { get; set; }
    /// <summary>Switched off by the user for troubleshooting: the solver ignores it, but it stays in the sketch and can be switched back on.</summary>
    public bool Disabled { get; set; }
}

/// <summary>An additional constrained drawing plane within the same editable sketch.</summary>
public sealed class SketchSection
{
    public string Name { get; set; } = "Plane";
    public Plane3 Plane { get; set; } = Plane3.XY;
    public SketchModel Model { get; set; } = new();
    public SketchSection Clone() => new() { Name = Name, Plane = Plane, Model = Model.Clone() };
}

public sealed class GreaseStroke
{
    public List<GreasePoint> Points { get; set; } = [];
    public float Width { get; set; } = 3;
    public GreaseStroke Clone() => new() { Points = [.. Points], Width = Width };
}

public readonly record struct GreasePoint(double U, double V, double Depth = 0);

public sealed class SketchModel
{
    public List<GreaseStroke> GreaseStrokes { get; set; } = [];
    public List<SketchSection> Sections { get; set; } = [];
    public List<SketchDatum> Datums { get; set; } = [];
    public List<SkPoint> Points { get; set; } = [];
    public List<SkEntity> Entities { get; set; } = [];
    public List<SkConstraint> Constraints { get; set; } = [];
    public int NextId { get; set; } = 1;
    public bool ShowConstruction { get; set; } = true;
    /// <summary>Spatial editing is opt-in. Null preserves compatibility with older multi-plane sketches.</summary>
    public bool? SpatialEditing { get; set; }
    public bool SharedNurbsCurves { get; set; }
    public bool? FreePointEditing { get; set; }
    public bool? FlatDrawing { get; set; }

    public SketchModel()
    {
        // Id 0 is the sketch origin: fixed, always available to snap to and constrain against.
        Points.Add(new SkPoint { Id = 0 });
        Constraints.Add(new SkConstraint { Id = NextId++, Type = CType.Fixed, Refs = [0], Hidden = true });
    }

    public SketchModel Clone()
    {
        var m = new SketchModel { GreaseStrokes = GreaseStrokes.Select(stroke => stroke.Clone()).ToList(), NextId = NextId, ShowConstruction = ShowConstruction, SpatialEditing = SpatialEditing, SharedNurbsCurves = SharedNurbsCurves, FreePointEditing = FreePointEditing, FlatDrawing = FlatDrawing, Sections = Sections.Select(s => s.Clone()).ToList(), Datums = Datums.Select(d => d.Clone()).ToList() };
        m.Points.Clear(); m.Constraints.Clear();
        foreach (var p in Points) m.Points.Add(new SkPoint { Id = p.Id, X = p.X, Y = p.Y, Z = p.Z });
        foreach (var e in Entities) m.Entities.Add(new SkEntity { Id = e.Id, Type = e.Type, Construction = e.Construction, A = e.A, B = e.B, C = e.C, R = e.R, Pts = [.. e.Pts], Closed = e.Closed, ControlPoints = e.ControlPoints, Group = e.Group });
        foreach (var c in Constraints) m.Constraints.Add(new SkConstraint { Id = c.Id, Type = c.Type, Refs = (int[])c.Refs.Clone(), Value = c.Value, V2 = c.V2, IsDimension = c.IsDimension, Name = c.Name, Formula = c.Formula, Hidden = c.Hidden, Disabled = c.Disabled, LabelU=c.LabelU,LabelV=c.LabelV });
        return m;
    }

    /// <summary>Display changes must not become sketch-edit sources when replaying feature history.</summary>
    public bool SameDefinition(SketchModel other)
    {
        if (ReferenceEquals(this, other)) return true;
        var a = Clone(); var b = other.Clone();
        void IgnoreDisplay(SketchModel model) { model.ShowConstruction = true; model.SpatialEditing = null; model.FreePointEditing = null; model.FlatDrawing = null; foreach(var c in model.Constraints){c.LabelU=null;c.LabelV=null;} foreach (var section in model.Sections) IgnoreDisplay(section.Model); }
        IgnoreDisplay(a); IgnoreDisplay(b);
        return System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);
    }

    // ---------------------------------------------------------------- lookups
    public SkPoint P(int id) => Points.First(p => p.Id == id);
    public SkPoint? PointOrNull(int id) => Points.FirstOrDefault(p => p.Id == id);
    public SkEntity E(int id) => Entities.First(e => e.Id == id);
    public SkEntity? EntityOrNull(int id) => Entities.FirstOrDefault(e => e.Id == id);

    /// <summary>Tie these curves together as one named shape (a rectangle's four lines, a slot's lines and arcs).</summary>
    public string NewGroup(string kind, IEnumerable<int> entityIds)
    {
        int n = 1 + Entities.Where(e => e.Group != null && e.Group.StartsWith(kind + " ")).Select(e => int.TryParse(e.Group![(kind.Length + 1)..], out var k) ? k : 0).DefaultIfEmpty(0).Max();
        string name = $"{kind} {n}";
        foreach (var id in entityIds) if (EntityOrNull(id) is { } e) e.Group = name;
        return name;
    }

    public int AddPoint(double x, double y) { int id = NextId++; Points.Add(new SkPoint { Id = id, X = x, Y = y }); return id; }
    public int AddLine(int a, int b, bool construction = false) { int id = NextId++; Entities.Add(new SkEntity { Id = id, Type = EntType.Line, A = a, B = b, Construction = construction }); return id; }
    public int AddCircle(int c, double r, bool construction = false) { int id = NextId++; Entities.Add(new SkEntity { Id = id, Type = EntType.Circle, A = c, R = r, Construction = construction }); return id; }
    public int AddEllipse(int c, int major, double minor, bool construction = false) { int id = NextId++; Entities.Add(new SkEntity { Id = id, Type = EntType.Ellipse, A = c, B = major, R = minor, Construction = construction }); return id; }
    public int AddArc(int c, int s, int e, bool construction = false) { int id = NextId++; Entities.Add(new SkEntity { Id = id, Type = EntType.Arc, A = c, B = s, C = e, Construction = construction }); return id; }
    public int AddSpline(IEnumerable<int> pts, bool closed, bool control, bool construction = false)
    {
        int id = NextId++; Entities.Add(new SkEntity { Id = id, Type = EntType.Spline, Pts = [.. pts], Closed = closed, ControlPoints = control, Construction = construction }); return id;
    }
    public int AddPointEntity(int p, bool construction = true) { int id = NextId++; Entities.Add(new SkEntity { Id = id, Type = EntType.Point, A = p, Construction = construction }); return id; }

    public SkConstraint Add(CType type, double value = 0, double v2 = 0, bool dimension = false, params int[] refs)
    {
        var c = new SkConstraint { Id = NextId++, Type = type, Refs = refs, Value = value, V2 = v2, IsDimension = dimension };
        Constraints.Add(c);
        return c;
    }

    public void Remove(SkConstraint c) => Constraints.Remove(c);

    /// <summary>Points an entity depends on.</summary>
    public IEnumerable<int> PointsOf(SkEntity e) => e.Type switch
    {
        EntType.Line or EntType.Ellipse => [e.A, e.B],
        EntType.Circle or EntType.Point => [e.A],
        EntType.Arc => [e.A, e.B, e.C],
        _ => e.Pts,
    };

    /// <summary>Delete an entity, every constraint that referenced it, and points nothing else uses.</summary>
    public void RemoveEntity(int entityId)
    {
        var e = EntityOrNull(entityId); if (e == null) return;
        Entities.Remove(e);
        Constraints.RemoveAll(c => !c.Hidden && RefsEntity(c, entityId));
        PruneUnusedPoints(PointsOf(e));
    }

    /// <summary>Remove a point (and the constraints on it) once no curve uses it any more.</summary>
    public void DropPointIfUnshared(int pid)
    {
        if (pid == 0 || Entities.Any(en => PointsOf(en).Contains(pid))) return;
        Constraints.RemoveAll(c => !c.Hidden && RefsPoint(c, pid));
        Points.RemoveAll(p => p.Id == pid);
    }

    public void PruneUnusedPoints(IEnumerable<int> candidates)
    {
        var used=Entities.SelectMany(PointsOf).ToHashSet();used.Add(0);
        bool changed;
        do
        {
            changed=false;
            foreach(var constraint in Constraints.Where(c=>!c.Hidden))
            {
                var mask=RefIsPoint(constraint.Type,constraint.Refs.Length);
                bool live=constraint.Refs.Select((id,i)=>mask[i]?used.Contains(id):EntityOrNull(id)!=null).Any(x=>x);
                if(live)for(int i=0;i<constraint.Refs.Length;i++)if(mask[i])changed|=used.Add(constraint.Refs[i]);
            }
        }while(changed);
        foreach(int pid in candidates.Distinct().Where(id=>id!=0&&!used.Contains(id)).ToList())
        {Constraints.RemoveAll(c=>!c.Hidden&&RefsPoint(c,pid));Points.RemoveAll(p=>p.Id==pid);}
    }

    /// <summary>Which of a constraint's refs are points (true) or entities (false).</summary>
    public static bool[] RefIsPoint(CType t, int count) => t switch
    {
        CType.Coincident or CType.Distance or CType.HDistance or CType.VDistance => [true, true],
        CType.PointOnEntity or CType.Midpoint or CType.PointLineDistance => [true, false],
        CType.Symmetric => [true, true, false],
        CType.Fixed => [true],
        CType.Horizontal or CType.Vertical => count == 2 ? [true, true] : [false],
        _ => Enumerable.Repeat(false, count).ToArray(),
    };

    /// <summary>Drops the constraints that refer to this curve as a whole (its length, horizontal, tangent...), keeping those on its points.</summary>
    public void DropEntityConstraints(int id) => Constraints.RemoveAll(c => !c.Hidden && RefsEntity(c, id));

    bool RefsEntity(SkConstraint c, int id)
    {
        var ip = RefIsPoint(c.Type, c.Refs.Length);
        for (int i = 0; i < c.Refs.Length; i++) if (!ip[i] && c.Refs[i] == id) return true;
        return false;
    }
    bool RefsPoint(SkConstraint c, int id)
    {
        var ip = RefIsPoint(c.Type, c.Refs.Length);
        for (int i = 0; i < c.Refs.Length; i++) if (ip[i] && c.Refs[i] == id) return true;
        return false;
    }

    /// <summary>All points that coincide with this one through Coincident constraints (including itself).</summary>
    public HashSet<int> CoincidentGroup(int pid)
    {
        var g = new HashSet<int> { pid }; bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var c in Constraints.Where(c => c.Type == CType.Coincident))
                if (g.Contains(c.Refs[0]) != g.Contains(c.Refs[1])) { g.Add(c.Refs[0]); g.Add(c.Refs[1]); grew = true; }
        }
        return g;
    }

    // ---------------------------------------------------------------- geometry helpers
    public (double cx, double cy, double r, double a0, double sweep) ArcGeometry(SkEntity e)
    {
        var c = P(e.A); var s = P(e.B); var en = P(e.C);
        double r = Math.Sqrt((s.X - c.X) * (s.X - c.X) + (s.Y - c.Y) * (s.Y - c.Y));
        double a0 = Math.Atan2(s.Y - c.Y, s.X - c.X), a1 = Math.Atan2(en.Y - c.Y, en.X - c.X);
        double sweep = a1 - a0; while (sweep <= 1e-9) sweep += 2 * Math.PI;
        return (c.X, c.Y, r, a0, sweep);
    }

    /// <summary>Centre, semi-axes and the angle of the first (A-B) axis. Either semi-axis may be the longer one.</summary>
    public (double cx, double cy, double a, double b, double rot) EllipseGeometry(SkEntity e)
    {
        var c = P(e.A); var m = P(e.B);
        return (c.X, c.Y, Math.Max(Math.Sqrt((m.X - c.X) * (m.X - c.X) + (m.Y - c.Y) * (m.Y - c.Y)), 1e-9), Math.Max(e.R, 1e-9), Math.Atan2(m.Y - c.Y, m.X - c.X));
    }

    public (double x, double y) EllipsePoint(SkEntity e, double t)
    {
        var (cx, cy, a, b, rot) = EllipseGeometry(e);
        double ex = a * Math.Cos(t), ey = b * Math.Sin(t), cr = Math.Cos(rot), sr = Math.Sin(rot);
        return (cx + ex * cr - ey * sr, cy + ex * sr + ey * cr);
    }

    public double Radius(SkEntity e) => e.Type == EntType.Circle ? e.R : ArcGeometry(e).r;

    public static Vec3 To3(Plane3 pl, double x, double y) => pl.PointAt(x, y);
    public static Vec3 To3(Plane3 pl, SkPoint p) => pl.PointAt(p.X, p.Y) + pl.Normal * p.Z;

    /// <summary>Display polylines in 3D for every entity (construction ones flagged). Points lifted off the plane keep their offset.</summary>
    public IEnumerable<(SkEntity ent, List<Vec3> pts)> Polylines(Plane3 pl)
    {
        foreach (var e in Entities)
        {
            var pts = new List<Vec3>();
            switch (e.Type)
            {
                case EntType.Line: pts.Add(To3(pl, P(e.A))); pts.Add(To3(pl, P(e.B))); break;
                case EntType.Circle:
                {
                    var c = P(e.A); var lift = pl.Normal * c.Z;
                    for (int i = 0; i <= 96; i++) { double a = i * 2 * Math.PI / 96; pts.Add(To3(pl, c.X + e.R * Math.Cos(a), c.Y + e.R * Math.Sin(a)) + lift); }
                    break;
                }
                case EntType.Arc:
                {
                    var (cx, cy, r, a0, sw) = ArcGeometry(e); var lift = pl.Normal * P(e.A).Z;
                    int n = Math.Max(8, (int)(sw / (2 * Math.PI) * 96));
                    for (int i = 0; i <= n; i++) { double a = a0 + sw * i / n; pts.Add(To3(pl, cx + r * Math.Cos(a), cy + r * Math.Sin(a)) + lift); }
                    break;
                }
                case EntType.Ellipse:
                { var lift = pl.Normal * P(e.A).Z; for (int i = 0; i <= 128; i++) { var q = EllipsePoint(e, i * 2 * Math.PI / 128); pts.Add(To3(pl, q.x, q.y) + lift); } break; }
                case EntType.Spline:
                    foreach (var q in SplineSamples3(e)) pts.Add(To3(pl, q.x, q.y) + pl.Normal * q.z);
                    break;
                default: continue;
            }
            yield return (e, pts);
        }
    }

    /// <summary>Cheap preview of a spline: Catmull-Rom through fit points, or a uniform cubic B-spline for control points.</summary>
    public List<(double x, double y)> SplineSamples(SkEntity e) => SplineSamples3(e).Select(q => (q.x, q.y)).ToList();

    /// <summary>Spline preview samples as (u, v, offset along the plane normal).</summary>
    public List<(double x, double y, double z)> SplineSamples3(SkEntity e)
    {
        var pts = e.Pts.Select(i => (P(i).X, P(i).Y, P(i).Z)).ToList();
        var res = new List<(double, double, double)>();
        if (pts.Count < 2) return res;
        if (e.ControlPoints)
        {
            if (pts.Count < 4) { res.AddRange(pts); return res; }
            // clamped by tripling end points
            var q = new List<(double X, double Y, double Z)> { pts[0], pts[0] }; q.AddRange(pts); q.Add(pts[^1]); q.Add(pts[^1]);
            for (int i = 0; i + 3 < q.Count; i++)
                for (int k = 0; k < 12; k++)
                {
                    double t = k / 12.0, t2 = t * t, t3 = t2 * t;
                    double b0 = (1 - t) * (1 - t) * (1 - t) / 6, b1 = (3 * t3 - 6 * t2 + 4) / 6, b2 = (-3 * t3 + 3 * t2 + 3 * t + 1) / 6, b3 = t3 / 6;
                    res.Add((b0 * q[i].X + b1 * q[i + 1].X + b2 * q[i + 2].X + b3 * q[i + 3].X, b0 * q[i].Y + b1 * q[i + 1].Y + b2 * q[i + 2].Y + b3 * q[i + 3].Y, b0 * q[i].Z + b1 * q[i + 1].Z + b2 * q[i + 2].Z + b3 * q[i + 3].Z));
                }
            res.Add(pts[^1]);
            return res;
        }
        var p = new List<(double X, double Y, double Z)>(pts);
        if (e.Closed && p.Count > 2) { p.Insert(0, p[^1]); p.Add(p[1]); p.Add(p[2]); }
        else { p.Insert(0, (2 * p[0].X - p[1].X, 2 * p[0].Y - p[1].Y, 2 * p[0].Z - p[1].Z)); p.Add((2 * p[^1].X - p[^2].X, 2 * p[^1].Y - p[^2].Y, 2 * p[^1].Z - p[^2].Z)); }
        for (int i = 1; i + 2 < p.Count; i++)
            for (int k = 0; k < 14; k++)
            {
                double t = k / 14.0, t2 = t * t, t3 = t2 * t;
                double Cr(Func<(double X, double Y, double Z), double> f) => 0.5 * ((2 * f(p[i])) + (-f(p[i - 1]) + f(p[i + 1])) * t + (2 * f(p[i - 1]) - 5 * f(p[i]) + 4 * f(p[i + 1]) - f(p[i + 2])) * t2 + (-f(p[i - 1]) + 3 * f(p[i]) - 3 * f(p[i + 1]) + f(p[i + 2])) * t3);
                res.Add((Cr(v => v.X), Cr(v => v.Y), Cr(v => v.Z)));
            }
        res.Add(pts[e.Closed && pts.Count > 2 ? 0 : pts.Count - 1]);
        return res;
    }

    /// <summary>Exact kernel curves for every non-construction entity.</summary>
    public IEnumerable<(SketchModel model, Plane3 plane)> DrawingModels(Plane3 root)
    {
        yield return (this, root);
        foreach (var section in Sections)
            foreach (var child in section.Model.DrawingModels(section.Plane)) yield return child;
    }

    public List<Shape> ToShapes(Plane3 pl, bool includeSections = true, bool includeConstruction = false)
    {
        var shapes = new List<Shape>();
        var normal = pl.Normal;
        Vec3 V(int pid) => To3(pl, P(pid));
        foreach (var e in Entities.Where(x => includeConstruction || !x.Construction))
        {
            switch (e.Type)
            {
                case EntType.Line:
                    if (V(e.A).DistanceTo(V(e.B)) > 1e-9) shapes.Add(Occ.Line(V(e.A), V(e.B)));
                    break;
                case EntType.Circle: if (e.R > 1e-9) shapes.Add(Occ.Circle(V(e.A), normal, e.R)); break;
                case EntType.Arc:
                {
                    var (cx, cy, r, a0, sw) = ArcGeometry(e);
                    if (r < 1e-9) break;
                    if (Math.Abs(sw - 2 * Math.PI) < 1e-6) { shapes.Add(Occ.Circle(V(e.A), normal, r)); break; }
                    double am = a0 + sw / 2;
                    shapes.Add(Occ.Arc3(V(e.B), To3(pl, cx + r * Math.Cos(am), cy + r * Math.Sin(am)) + normal * P(e.A).Z, V(e.C)));
                    break;
                }
                case EntType.Ellipse:
                {
                    var (cx, cy, a, b, rot) = EllipseGeometry(e);
                    if (a < 1e-9 || b < 1e-9) break;
                    // the kernel wants the long semi-axis first
                    bool swap = b > a; double major = swap ? b : a, minor = swap ? a : b, ang = rot + (swap ? Math.PI / 2 : 0);
                    shapes.Add(Occ.Ellipse(V(e.A), normal, pl.U * Math.Cos(ang) + pl.V * Math.Sin(ang), major, minor));
                    break;
                }
                case EntType.Spline:
                    if (e.Pts.Count >= 2)
                    {
                        var pts = e.Pts.Select(V).ToList();
                        shapes.Add(e.ControlPoints ? Occ.NurbsCurve(pts, 3) : Occ.Spline(pts, e.Closed && pts.Count > 2));
                    }
                    break;
            }
        }
        if (includeSections) foreach (var section in Sections) shapes.AddRange(section.Model.ToShapes(section.Plane, true, includeConstruction));
        return shapes;
    }

    // ---------------------------------------------------------------- descriptions
    public static string Describe(CType t) => t switch
    {
        CType.Coincident => "Coincident", CType.PointOnEntity => "Point on curve", CType.Horizontal => "Horizontal", CType.Vertical => "Vertical",
        CType.Parallel => "Parallel", CType.Perpendicular => "Perpendicular", CType.Collinear => "Collinear", CType.Equal => "Equal", CType.Tangent => "Tangent",
        CType.Concentric => "Concentric", CType.Midpoint => "Midpoint", CType.Symmetric => "Symmetric", CType.Fixed => "Fixed",
        CType.Distance => "Distance", CType.LineLength => "Length", CType.HDistance => "Horizontal distance", CType.VDistance => "Vertical distance",
        CType.Radius => "Radius", CType.Diameter => "Diameter", CType.Angle => "Angle", CType.PointLineDistance => "Point to line distance", _ => t.ToString(),
    };

    public static string Glyph(CType t) => t switch
    {
        CType.Coincident => "●", CType.PointOnEntity => "◉", CType.Horizontal => "━", CType.Vertical => "┃", CType.Parallel => "∥", CType.Perpendicular => "⊥",
        CType.Collinear => "≡", CType.Equal => "＝", CType.Tangent => "◠", CType.Concentric => "◎", CType.Midpoint => "┿", CType.Symmetric => "⇋", CType.Fixed => "⚓", _ => "",
    };
}
