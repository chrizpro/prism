using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Bring geometry from outside the sketch into it: edges projected straight onto the sketch plane, every edge of a body, or the curves
/// where a body crosses the plane. The result is ordinary sketch geometry, locked in place so other curves can be built from it.
/// Useful for lofts: copy the outline of an existing body onto a new plane, or take a section of it, and use that as a profile.
/// </summary>
public sealed class ProjectTool(int variant = 0) : SketchTool
{
    int _mode = variant;
    public override string Name => "Project";
    public override string Mode => _mode switch { 0 => "Project", 1 => "Project body", 2 => "Intersect", _ => "Pierce points" };
    bool _construction;
    public override void Activate(CadSession s) => _construction = Sk(s).Construction;
    public override string PanelTitle => "PROJECT / INCLUDE";
    public override IReadOnlyList<ToolControl>? Panel =>
    [
        new(ToolControlKind.Toggle, "construction", "As construction", Text: "Reference geometry stays out of profiles. Its visibility can be toggled in the tree.", On: _construction),
        new(ToolControlKind.Info, "link", "Creates fixed references", Text: "References are snapshots. Changes to the source do not update them automatically."),
    ];
    public override void PanelSet(CadSession s, string id, object? value)
    {
        if (id == "construction") _construction = value is true;
        s.RaiseState(); s.Invalidate();
    }
    static readonly ToolOption[] Opts =
    [
        new("Edges", "Click an edge or curve to project it onto the sketch plane", "project-edge"),
        new("Body", "Click a body to project all its edges", "project-body"),
        new("Intersect", "Click a sketch curve, edge or body to include its intersections with the sketch plane", "project-section"),
        new("Pierce points", "Include only the points where a curve crosses this plane", "point"),
    ];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => _mode;
    protected override void ApplyOption(int index) => _mode = index;
    public override string Prompt => _mode switch
    {
        0 => "Project (edges): click an edge, curve or sketch curve outside this sketch to copy it onto the sketch plane.",
        1 => "Project (body): click a body to copy all of its edges onto the sketch plane.",
        2 => "Intersect: click a curve on another sketch plane, an outside edge or a body for its intersection with this plane.",
        _ => "Pierce points: click a curve, including one on another plane in this sketch, to include only its crossing points.",
    };

    (SceneObject obj, int edge)? _hoverEdge;
    SceneObject? _hoverBody;
    (SketchSection section, int entity)? _hoverSection;
    (Shape shape, Plane3 plane, List<List<Vec3>> curves)? _secCache;
    (SceneObject obj, int edge, Plane3 plane, List<List<Vec3>> curves)? _edgeCache;

    List<List<Vec3>> EdgeIntersections(SketchContext sk, SceneObject obj, int edge)
    {
        if (_edgeCache is { } cached && ReferenceEquals(cached.obj, obj) && cached.edge == edge && cached.plane == sk.Plane) return cached.curves;
        using var shape = obj.Shape.Sub(SubKind.Edge, edge);
        var curves = Intersections(shape, sk.Plane);
        _edgeCache = (obj, edge, sk.Plane, curves);
        return curves;
    }

    List<List<Vec3>> SectionCurves(SketchContext sk, SceneObject o)
        => SectionCurves(sk, o.Shape);

    List<List<Vec3>> SectionCurves(SketchContext sk, Shape shape)
    {
        if (_secCache is { } c && ReferenceEquals(c.shape, shape) && c.plane == sk.Plane) return c.curves;
        var curves = Intersections(shape, sk.Plane);
        _secCache = (shape, sk.Plane, curves);
        return curves;
    }

    /// <summary>Exact kernel intersections, including isolated vertices where a curve pierces the plane.</summary>
    public static List<List<Vec3>> Intersections(Shape shape, Plane3 plane)
    {
        var curves = new List<List<Vec3>>();
        using var section = Occ.Section(shape, plane.Origin, plane.Normal);
        if (section.Count(SubKind.Edge) > 0) curves.AddRange(AllEdges(section.Tessellate()));
        var vertices = new List<Vec3>();
        for (int i = 0; i < section.Count(SubKind.Vertex); i++)
        {
            using var vertex = section.Sub(SubKind.Vertex, i);
            var point = vertex.Props().Center;
            if (vertices.Any(p => p.DistanceTo(point) < 1e-6)) continue;
            vertices.Add(point);
            if (curves.Any(poly => poly[0].DistanceTo(point) < 1e-5 || poly[^1].DistanceTo(point) < 1e-5)) continue;
            curves.Add([point]);
        }
        return curves;
    }

    // ------------------------------------------------------------ geometry gathering
    static IEnumerable<SceneObject> Candidates(CadSession s) => s.Objects.Where(o => o.Visible && !o.IsAnnotation && o.Id != s.Sketch?.EditingId);

    /// <summary>The ordered polyline of one tessellated edge.</summary>
    static List<Vec3> EdgePolyline(MeshData m, int edge)
    {
        var E = m.EdgePoints; var pts = new List<Vec3>();
        for (int sg = 0; sg < m.SegEdge.Length; sg++)
        {
            if (m.SegEdge[sg] != edge) continue;
            var a = new Vec3(E[6 * sg], E[6 * sg + 1], E[6 * sg + 2]); var b = new Vec3(E[6 * sg + 3], E[6 * sg + 4], E[6 * sg + 5]);
            if (pts.Count == 0 || pts[^1].DistanceTo(a) > 1e-6) pts.Add(a);
            pts.Add(b);
        }
        return pts;
    }

    static List<List<Vec3>> AllEdges(MeshData m)
    {
        var res = new List<List<Vec3>>();
        foreach (var edge in m.SegEdge.Distinct().OrderBy(x => x)) { var p = EdgePolyline(m, edge); if (p.Count > 1) res.Add(p); }
        return res;
    }

    // ------------------------------------------------------------ turning polylines into sketch geometry
    /// <summary>Add a projected polyline as a line, arc, circle or spline (whichever it really is) with every point locked.</summary>
    public static int AddPolyline(SketchContext sk, List<(double u, double v)> raw, bool construction = false)
    {
        var m = sk.Model;
        var pts = new List<(double u, double v)>();
        foreach (var p in raw) if (pts.Count == 0 || Dist(pts[^1], p) > 1e-6) pts.Add(p);
        if (pts.Count < 2) return -1;
        double len = 0; for (int i = 0; i + 1 < pts.Count; i++) len += Dist(pts[i], pts[i + 1]);
        if (len < 1e-6) return -1;
        bool closed = pts.Count > 2 && Dist(pts[0], pts[^1]) < 1e-6 * Math.Max(1, len);
        double tol = 2e-4 * len + 1e-6;

        int Fix(double u, double v) { int id = m.AddPoint(u, v); m.Add(CType.Fixed, u, v, false, id); return id; }

        // straight?
        var (a, b) = (pts[0], pts[^1]);
        double chord = Dist(a, b);
        if (!closed && chord > 1e-9 && pts.All(p => Math.Abs(((p.u - a.u) * (b.v - a.v) - (p.v - a.v) * (b.u - a.u)) / chord) < tol))
            return AddLine(m, Fix(a.u, a.v), Fix(b.u, b.v), construction);

        // circular?
        var mid = pts[pts.Count / 2];
        var third = closed ? pts[pts.Count / 3] : mid;
        var second = closed ? pts[2 * pts.Count / 3] : a;
        var cc = closed ? Circum(a, third, second) : Circum(a, mid, b);
        if (cc is { } k && k.r < 1e7 && pts.All(p => Math.Abs(Dist(p, (k.cx, k.cy)) - k.r) < 2e-3 * k.r + 1e-6))
        {
            if (closed)
            {
                int ctr = Fix(k.cx, k.cy);
                int circle = m.AddCircle(ctr, k.r, construction);
                m.Add(CType.Radius, k.r, 0, true, circle);
                return circle;
            }
            // arcs run counter-clockwise from start to end; flip when the polyline goes the other way round
            double a0 = Math.Atan2(a.v - k.cy, a.u - k.cx);
            double Rel(double ang) { double r = ang - a0; while (r < 0) r += 2 * Math.PI; return r; }
            bool ccw = Rel(Math.Atan2(mid.v - k.cy, mid.u - k.cx)) < Rel(Math.Atan2(b.v - k.cy, b.u - k.cx));
            int c0 = Fix(k.cx, k.cy), s0 = Fix((ccw ? a : b).u, (ccw ? a : b).v), e0 = Fix((ccw ? b : a).u, (ccw ? b : a).v);
            return m.AddArc(c0, s0, e0, construction);
        }

        // anything else: a spline through (at most) 32 points along the curve
        int n = Math.Min(pts.Count, 32);
        var fit = new List<int>();
        int last = closed ? pts.Count - 1 : pts.Count;       // a closed curve must not repeat its first point
        for (int i = 0; i < n; i++)
        {
            var q = pts[(int)Math.Round(i * (last - 1.0) / Math.Max(n - 1, 1))];
            if (i == n - 1 && closed) break;
            fit.Add(Fix(q.u, q.v));
        }
        if (!closed && fit.Count > 0 && Dist(pts[^1], (m.P(fit[^1]).X, m.P(fit[^1]).Y)) > 1e-6) fit.Add(Fix(pts[^1].u, pts[^1].v));
        if (fit.Count < 2) return -1;
        return m.AddSpline(fit, closed && fit.Count > 2, false, construction);
    }

    static int AddLine(SketchModel m, int a, int b, bool construction) => m.AddLine(a, b, construction);
    static double Dist((double u, double v) a, (double u, double v) b) => Math.Sqrt((a.u - b.u) * (a.u - b.u) + (a.v - b.v) * (a.v - b.v));

    static (double cx, double cy, double r)? Circum((double u, double v) p, (double u, double v) q, (double u, double v) r)
    {
        double d = 2 * (p.u * (q.v - r.v) + q.u * (r.v - p.v) + r.u * (p.v - q.v));
        if (Math.Abs(d) < 1e-9) return null;
        double ux = ((p.u * p.u + p.v * p.v) * (q.v - r.v) + (q.u * q.u + q.v * q.v) * (r.v - p.v) + (r.u * r.u + r.v * r.v) * (p.v - q.v)) / d;
        double uy = ((p.u * p.u + p.v * p.v) * (r.u - q.u) + (q.u * q.u + q.v * q.v) * (p.u - r.u) + (r.u * r.u + r.v * r.v) * (q.u - p.u)) / d;
        return (ux, uy, Dist(p, (ux, uy)));
    }

    /// <summary>Project 3D polylines onto the sketch plane and add them. Returns how many curves were created.</summary>
    public static int Bring(CadSession s, SketchContext sk, IEnumerable<List<Vec3>> polylines, string what, bool? construction = null)
    {
        int made = 0;
        var seen = new List<List<(double u, double v)>>();
        var inputs = polylines.Where(p => p.Count > 0).ToList();
        if (inputs.Count == 0) return 0;
        bool success = sk.Edit(s, what, () =>
        {
            foreach (var poly in inputs)
            {
                var flat = poly.Select(p => sk.Plane.Project(p)).ToList();
                if (seen.Any(prev => prev.Count == flat.Count && (prev.Zip(flat).All(p => Dist(p.First, p.Second) < 1e-6) || prev.AsEnumerable().Reverse().Zip(flat).All(p => Dist(p.First, p.Second) < 1e-6)))) continue;
                seen.Add(flat);
                if (flat.Count == 1)
                {
                    var q = flat[0];
                    if (sk.Model.Entities.Any(e => e.Type == EntType.Point && Dist((sk.Model.P(e.A).X, sk.Model.P(e.A).Y), q) < 1e-6)) continue;
                    int point = sk.Model.AddPoint(q.u, q.v);
                    sk.Model.Add(CType.Fixed, q.u, q.v, false, point);
                    sk.Model.AddPointEntity(point, construction ?? sk.Construction); made++;
                }
                else if (AddPolyline(sk, flat, construction ?? sk.Construction) >= 0) made++;
            }
        });
        return success ? made : 0;
    }

    // ------------------------------------------------------------ the three sources
    public static int ProjectEdge(CadSession s, SceneObject o, int edge, bool? construction = null) =>
        Bring(s, Sk(s), [EdgePolyline(o.Mesh, edge)], "That projection", construction);

    public static int ProjectBody(CadSession s, SceneObject o, bool? construction = null) => Bring(s, Sk(s), AllEdges(o.Mesh), "That projection", construction);

    public static int SectionBody(CadSession s, SceneObject o, bool? construction = null)
    {
        var sk = Sk(s);
        return Bring(s, sk, Intersections(o.Shape, sk.Plane), "That intersection", construction);
    }

    public static int IntersectEdge(CadSession s, SceneObject o, int edge, bool? construction = null)
    {
        using var shape = o.Shape.Sub(SubKind.Edge, edge);
        return Bring(s, Sk(s), Intersections(shape, Sk(s).Plane), "That intersection", construction);
    }

    // ------------------------------------------------------------ input
    static (SketchSection section, int entity)? InternalAt(CadSession s, double x, double y)
    {
        var sk = Sk(s); double best = 10; (SketchSection,int)? hit = null;
        foreach (var section in sk.DrawingPlanes.Where(section => !ReferenceEquals(section.Model,sk.Model)))
        foreach (var (entity,poly) in section.Model.Polylines(section.Plane))
        for (int i=1;i<poly.Count;i++)
        {
            var a=s.Camera.Project(poly[i-1]);var b=s.Camera.Project(poly[i]); if(a==null||b==null)continue;
            double dx=b.Value.x-a.Value.x,dy=b.Value.y-a.Value.y;
            double t=Math.Clamp(((x-a.Value.x)*dx+(y-a.Value.y)*dy)/Math.Max(1e-12,dx*dx+dy*dy),0,1);
            double distance=Math.Sqrt(Math.Pow(x-a.Value.x-t*dx,2)+Math.Pow(y-a.Value.y-t*dy,2));
            if(distance<best){best=distance;hit=(section,entity.Id);}
        }
        return hit;
    }

    static List<List<Vec3>> InternalIntersections(SketchSection section,int entity,Plane3 target,bool pointsOnly)
    {
        var model=section.Model.Clone(); model.Sections=[];model.Datums=[];
        model.Entities.RemoveAll(e=>e.Id!=entity);model.Entities.ForEach(e=>e.Construction=false);
        var shapes=model.ToShapes(section.Plane);var curves=new List<List<Vec3>>();
        try { foreach(var shape in shapes)curves.AddRange(Intersections(shape,target)); }
        finally { foreach(var shape in shapes)shape.Dispose(); }
        return pointsOnly?curves.Where(poly=>poly.Count==1).ToList():curves;
    }

    public static int IntersectSketchEntity(CadSession s,SketchSection section,int entity,bool pointsOnly=false,bool construction=false)
        =>Bring(s,Sk(s),InternalIntersections(section,entity,Sk(s).Plane,pointsOnly),"That intersection",construction);

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        int n = 0;
        try
        {
            if (_mode != 1 && InternalAt(s,p.X,p.Y) is { } source)
            {
                if(_mode>=2)n=IntersectSketchEntity(s,source.section,source.entity,_mode==3,_construction);
                else n=Bring(s,Sk(s),source.section.Model.Polylines(source.section.Plane).Where(poly=>poly.ent.Id==source.entity).Select(poly=>poly.pts),"That projection",_construction);
            }
            else if (_mode == 0)
            {
                if (Picking.NearestEdge(s.Camera, Candidates(s), p.X, p.Y, 10) is { } hit) n = ProjectEdge(s, hit.obj, hit.edge, _construction);
                else { s.Say("Click on an edge or curve.", true); return true; }
            }
            else if (_mode >= 2 && Picking.NearestEdge(s.Camera, Candidates(s), p.X, p.Y, 10, o => o.HasBrep) is { } edgeHit)
            {
                using var shape=edgeHit.obj.Shape.Sub(SubKind.Edge,edgeHit.edge);
                var cuts=Intersections(shape,Sk(s).Plane);
                n=Bring(s,Sk(s),_mode==3?cuts.Where(poly=>poly.Count==1):cuts,"That intersection",_construction);
            }
            else
            {
                var body = BodyAt(s, p);
                if (body == null) { s.Say("Click on a body.", true); return true; }
                n = _mode == 1 ? ProjectBody(s, body, _construction) : Bring(s,Sk(s),_mode==3?SectionCurves(Sk(s),body).Where(poly=>poly.Count==1):SectionCurves(Sk(s),body),"That intersection",_construction);
            }
        }
        catch (Exception e) { s.Say(e.Message, true); return true; }
        if (n > 0) s.Say($"Included {n} fixed reference{(n == 1 ? "" : "s")}{(_construction ? " as construction" : "")}. Source changes do not update these references.");
        else s.Say(_mode == 2 ? "No new intersections with the sketch plane." : "No new geometry could be projected.", true);
        s.Invalidate();
        return true;
    }

    SceneObject? BodyAt(CadSession s, PointerInfo p)
    {
        var hit = Picking.RayFace(s.Camera, Candidates(s), p.X, p.Y, o => o.HasBrep);
        if (hit != null) return hit.Value.obj;
        // a wireframe-ish pick: the nearest edge of a body also counts
        return Picking.NearestEdge(s.Camera, Candidates(s), p.X, p.Y, 10, o => o.Kind != ObjKind.Curve && o.Kind != ObjKind.Sketch && o.HasBrep)?.obj;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        _hoverEdge = null; _hoverBody = null; _hoverSection=_mode==1?null:InternalAt(s,p.X,p.Y);
        if (_mode != 1) { if (Picking.NearestEdge(s.Camera, Candidates(s), p.X, p.Y, 10, o => o.HasBrep) is { } h) _hoverEdge = (h.obj, h.edge); else if (_mode == 2) _hoverBody = BodyAt(s, p); }
        else _hoverBody = BodyAt(s, p);
        var sk = Sk(s);
        sk.Cursor = null;
        if (sk.PlanePoint(s, p.X, p.Y) is { } w) s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
        s.Invalidate();
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        try { BuildPreview(s, o); }
        catch (Exception error) when (error is KernelException or InvalidOperationException or ArgumentException)
        { _edgeCache = null; _secCache = null; }
    }

    void BuildPreview(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        const uint hot = 0xFF35B6FF;
        if(_hoverSection is { } source)
        {
            foreach(var (_,poly) in source.section.Model.Polylines(source.section.Plane).Where(poly=>poly.ent.Id==source.entity))
            { o.AddPolyline(poly,hot,4f);if(_mode==0)o.AddPolyline(poly.Select(q=>{var uv=sk.Plane.Project(q);return sk.Plane.PointAt(uv.u,uv.v);}).ToList(),0xFF7AA7E8,2.5f); }
            if(_mode>=2)DrawIntersections(o,InternalIntersections(source.section,source.entity,sk.Plane,_mode==3));
            return;
        }
        if (_hoverEdge is { } he)
        {
            var poly = EdgePolyline(he.obj.Mesh, he.edge);
            if (poly.Count > 1)
            {
                o.AddPolyline(poly, hot, 4f);
                if (_mode >= 2)
                {
                    DrawIntersections(o, EdgeIntersections(sk, he.obj, he.edge).Where(poly=>_mode!=3||poly.Count==1));
                }
                else o.AddPolyline(poly.Select(q => { var (u, v) = sk.Plane.Project(q); return sk.Plane.PointAt(u, v); }).ToList(), 0xFF7AA7E8, 2.5f);
            }
        }
        else if (_hoverBody is { } hb)
        {
            if (_mode == 1) foreach (var poly in AllEdges(hb.Mesh)) o.AddPolyline(poly, hot, 3f);
            else
            {
                DrawIntersections(o, SectionCurves(sk, hb).Where(poly=>_mode!=3||poly.Count==1));
            }
        }
    }

    static void DrawIntersections(Overlay overlay, IEnumerable<List<Vec3>> curves)
    {
        foreach (var poly in curves)
            if (poly.Count == 1) overlay.AddDot(poly[0], 0xFF35B6FF, 11);
            else overlay.AddPolyline(poly, 0xFF35B6FF, 4f);
    }
}
