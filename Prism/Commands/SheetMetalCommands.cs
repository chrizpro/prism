using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Core.Drawing;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// A flange from a profile: sketch the cross-section of a bent part as straight lines (sharp corners), and the bends are rounded for you.
/// Covers brackets, angles, channels and hats in one tool; the width is the length the profile is extruded.
/// </summary>
public sealed class ContourFlangeCommand : Command
{
    readonly SelectionSlot _profile;
    readonly NumberParam _t, _r, _w, _hemLen, _hemGap;
    readonly ChoiceParam _side;
    readonly BoolParam _sym, _flip;

    static double _lastT = 2, _lastR = 2;           // the rules stay for the next flange

    public ContourFlangeCommand()
    {
        _profile = Slot(new SelectionSlot { Label = "Profile sketch (straight lines)", Filter = SelFilter.SketchObj });
        _t = Num("Thickness", _lastT, 0.01, 100, 0.5);
        _r = Num("Bend radius (inside)", _lastR, 0, 1000, 0.5);
        _side = Choice("The metal lies", 0, "Left of the profile", "Right of the profile");
        _w = Num("Width", 30, 0.01, 1e5, 5);
        _sym = Check("Centred on the sketch plane", false);
        _flip = Check("Reverse width direction", false);
        _hemLen = Num("Hem length at the end (0 = no hem)", 0, 0, 1000, 1);
        _hemGap = Num("Hem gap", 0.5, 0.05, 100, 0.25);
    }

    public override string Title => "Flange";
    public override string Icon => "flange";
    public override string Hint => "Pick a sketch of the part's cross-section drawn as an open chain of straight lines with sharp corners. Set the thickness, bend radius and width: the corners become bends and the profile gets thickness on the side you choose.";

    protected override void UpdateVisibility() { }

    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_profile.Items.Count == 0) return [];
            var o = S.Find(_profile.Items[0].Obj); if (o?.SketchPlane is not { } pl || o.SketchData is not { } model) return [];
            var list = new List<HandleSpec>();
            if (!_sym.Value) list.Add(new(pl.Origin, pl.Normal * (_flip.Value ? -1 : 1), _w));
            try
            {
                var P = ChainOf(model).Select(q => pl.PointAt(q.u, q.v)).ToList();
                if (P.Count >= 2)
                {
                    var nrm = pl.Normal.Normalized();
                    double s = _side.Index == 0 ? 1 : -1;
                    var d0 = (P[1] - P[0]).Normalized();
                    list.Add(new(P[0], Vec3.Cross(nrm, d0).Normalized() * s, _t));                 // thickness: sideways off the first leg
                    if (P.Count >= 3)
                    {
                        var d1 = (P[2] - P[1]).Normalized();
                        var bis = (d1 - d0).Normalized();                                          // toward the bend centre side
                        if (bis.Length > 0.5) list.Add(new(P[1], bis * s, _r));
                    }
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException) { }
            return list;
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    static List<(double u, double v)> ChainOf(SketchModel m)
    {
        var real = m.Entities.Where(e => !e.Construction && e.Type != EntType.Point).ToList();
        if (real.Any(e => e.Type != EntType.Line)) throw new InvalidOperationException("Draw the profile with straight lines only: the bends are rounded from the bend radius.");
        if (real.Count == 0) throw new InvalidOperationException("The sketch has no lines.");
        var deg = new Dictionary<int, int>();
        foreach (var l in real) { deg[l.A] = deg.GetValueOrDefault(l.A) + 1; deg[l.B] = deg.GetValueOrDefault(l.B) + 1; }
        var ends = deg.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
        if (ends.Count != 2 || deg.Any(kv => kv.Value > 2)) throw new InvalidOperationException("The profile must be one open chain of lines: not closed, and without branches.");
        var used = new HashSet<int>(); int cur = ends[0];
        var pts = new List<(double, double)> { (m.P(cur).X, m.P(cur).Y) };
        while (real.FirstOrDefault(l => !used.Contains(l.Id) && (l.A == cur || l.B == cur)) is { } next)
        {
            used.Add(next.Id); cur = next.A == cur ? next.B : next.A; pts.Add((m.P(cur).X, m.P(cur).Y));
        }
        return pts;
    }

    protected override CommandOutput? Build()
    {
        if (_profile.Items.Count == 0) return null;
        Results.Clear();
        _lastT = _t.Value; _lastR = _r.Value;
        var obj = S.Find(_profile.Items[0].Obj) ?? throw new InvalidOperationException("The sketch is gone.");
        if (obj.SketchData is not { } model || obj.SketchPlane is not { } plane) throw new InvalidOperationException("Pick a sketch.");
        var uv = ChainOf(model);
        if (uv.Count < 2) throw new InvalidOperationException("The profile needs at least one line.");
        var P = uv.Select(q => plane.PointAt(q.u, q.v)).ToList();
        var nrm = plane.Normal.Normalized();
        double t = _t.Value, r = _r.Value, s = _side.Index == 0 ? 1 : -1;
        Vec3 Left(Vec3 d) => Vec3.Cross(nrm, d).Normalized();

        var prof = new List<Shape>(); var off = new List<Shape>();
        int n = P.Count - 1;
        var dirs = Enumerable.Range(0, n).Select(i => (P[i + 1] - P[i]).Normalized()).ToList();
        var lens = Enumerable.Range(0, n).Select(i => P[i].DistanceTo(P[i + 1])).ToList();
        var tang = new double[P.Count];
        var info = new (Vec3 T1, Vec3 M, Vec3 T2, Vec3 C, double R, bool towardCenter)?[P.Count];
        for (int i = 1; i < n; i++)
        {
            var d1 = dirs[i - 1]; var d2 = dirs[i];
            double turn = Vec3.Dot(Vec3.Cross(d1, d2), nrm), theta = Math.Acos(Math.Clamp(Vec3.Dot(d1, d2), -1, 1));
            if (theta < 1e-4) continue;                                      // straight on: no bend
            bool towardCenter = (turn > 0) == (s > 0);                         // the metal lies on the inside of this bend
            double R = towardCenter ? r + t : r;                               // radius of the profile line itself
            if (R < 1e-6) R = 1e-3;
            double T = R * Math.Tan(theta / 2); tang[i] = T;
            var T1 = P[i] - d1 * T; var T2 = P[i] + d2 * T;
            var n1 = turn > 0 ? Left(d1) : -Left(d1);
            var C = T1 + n1 * R;
            var mid = (T1 + T2) * 0.5 - C; var M = C + mid.Normalized() * R;
            info[i] = (T1, M, T2, C, R, towardCenter);
        }
        for (int i = 0; i < n; i++)
            if (tang[i] + tang[i + 1] > lens[i] - 1e-6) throw new InvalidOperationException($"Line {i + 1} of the profile is too short for the bends at its ends. Make it longer or use a smaller bend radius.");

        Vec3 Shift(Vec3 p, Vec3 d) => p + Left(d) * (s * t);
        Vec3 prevA = P[0], prevB = Shift(P[0], dirs[0]);
        for (int i = 1; i < n; i++)
        {
            if (info[i] is not { } b) continue;
            double Ro = b.towardCenter ? b.R - t : b.R + t;                    // radius of the other surface of the metal
            Vec3 OnOff(Vec3 p) => b.C + (p - b.C).Normalized() * Ro;
            prof.Add(Occ.Line(prevA, b.T1)); prof.Add(Occ.Arc3(b.T1, b.M, b.T2));
            off.Add(Occ.Line(prevB, OnOff(b.T1))); off.Add(Occ.Arc3(OnOff(b.T1), OnOff(b.M), OnOff(b.T2)));
            prevA = b.T2; prevB = OnOff(b.T2);
        }
        var endA = P[n]; var endB = Shift(P[n], dirs[n - 1]);
        prof.Add(Occ.Line(prevA, endA)); off.Add(Occ.Line(prevB, endB));
        var edges = new List<Shape>(prof); edges.AddRange(off);
        if (_hemLen.Value > 1e-9)
        {
            // fold the end back over the metal: a half-turn bend, then a straight return of the hem length
            var d = dirs[n - 1]; var m = Left(d) * s; double gap = _hemGap.Value, ri = gap / 2, Ro = ri + t;
            Vec3 At(double along, double up) => endA + d * along + m * up;
            edges.Add(Occ.Arc3(endA, At(Ro, t + ri), At(0, 2 * t + gap)));
            edges.Add(Occ.Line(At(0, 2 * t + gap), At(-_hemLen.Value, 2 * t + gap)));
            edges.Add(Occ.Line(At(-_hemLen.Value, 2 * t + gap), At(-_hemLen.Value, t + gap)));
            edges.Add(Occ.Line(At(-_hemLen.Value, t + gap), At(0, t + gap)));
            edges.Add(Occ.Arc3(At(0, t + gap), At(ri, t + ri), endB));
            Results.Add(new ResultLine("Hem", $"{_hemLen.Value:0.##} long, {gap:0.##} gap"));
        }
        else edges.Add(Occ.Line(endA, endB));
        edges.Add(Occ.Line(P[0], Shift(P[0], dirs[0])));
        var face = Occ.Profile(edges, true);
        double w = _w.Value * (_flip.Value ? -1 : 1);
        if (_sym.Value) face = Occ.Translate(face, -nrm * (w / 2));
        var body = Occ.Extrude(face, nrm * w);
        int bends = info.Count(x => x != null);
        Results.Add(new ResultLine("Bends", bends.ToString()));
        var o = new CommandOutput { Label = "Flange", Icon = "flange" };
        o.Add.Add(new(body, "Sheet metal", ObjKind.Solid));
        return o;
    }
}

/// <summary>
/// Unfold a bent sheet-metal body into its flat pattern: the flat outline with cut-outs, and a line at the middle of every bend.
/// Works on parts whose bends all run in one direction (brackets, channels, hats, boxes bent along parallel lines).
/// </summary>
public sealed class FlatPatternCommand : Command
{
    readonly SelectionSlot _body;
    readonly NumberParam _k;
    readonly BoolParam _beside;
    List<List<(double x, double y)>> _loops = [];
    List<((double x, double y) a, (double x, double y) b)> _bendLines = [];
    string _name = "Part";

    public FlatPatternCommand()
    {
        _body = Slot(new SelectionSlot { Label = "Sheet metal body", Filter = SelFilter.Solid });
        _k = Num("K-factor", 0.44, 0, 1, 0.01, "");
        _beside = Check("Place it beside the part", true);
        Add(new ActionParam { Label = "Save the flat pattern as DXF", Run = SaveDxf });
    }

    public override string Title => "Flat Pattern";
    public override string Icon => "flat-pattern";
    public override string Hint => "Pick a bent sheet-metal body. It is unfolded into a flat surface (with its holes) and a line at each bend. The K-factor says where the neutral line sits inside the thickness: 0.44 is typical for air bending steel; 0.33 for soft materials.";

    sealed class Node
    {
        public bool IsArc; public int Face; public (double x, double y) E0, E1, Enter, Exit; public double Radius, Span, SStart, SLen;
        public Vec3 Dir; public List<int> Next = [];
    }

    static (double x, double y) Sub((double x, double y) a, (double x, double y) b) => (a.x - b.x, a.y - b.y);
    static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    protected override CommandOutput? Build()
    {
        if (_body.Items.Count == 0) return null;
        Results.Clear(); _loops = []; _bendLines = [];
        var obj = ObjectsOf(_body).First(); _name = obj.Name;
        int nf = obj.Mesh.FaceCount;
        var faces = Enumerable.Range(0, nf).Select(i => S.ShapeOf(new SelItem(obj.Id, SelKind.Face, i)) ?? throw new InvalidOperationException("Could not read a face.")).ToList();
        var cyl = new Occ.CylFace?[nf];
        for (int i = 0; i < nf; i++) { var c = Occ.Cylinders(faces[i]); if (c.Count == 1 && !c[0].Full) cyl[i] = c[0]; }
        int firstBend = Array.FindIndex(cyl, c => c != null);
        if (firstBend < 0) throw new InvalidOperationException("This body has no bends, so it is already flat.");
        var d = cyl[firstBend]!.Value.Axis.Normalized();
        for (int i = 0; i < nf; i++)
            if (cyl[i] is { } c && Vec3.Cross(c.Axis.Normalized(), d).Length > 1e-3) throw new InvalidOperationException("The bends are not all parallel. This tool unfolds parts bent along one direction.");
        var e1 = (Math.Abs(d.Z) < 0.9 ? Vec3.Cross(Vec3.UnitZ, d) : Vec3.Cross(Vec3.UnitX, d)).Normalized(); var e2 = Vec3.Cross(d, e1);
        (double x, double y) P2(Vec3 p) => (Vec3.Dot(p, e1), Vec3.Dot(p, e2));
        double Yof(Vec3 p) => Vec3.Dot(p, d);
        var pr = obj.Shape.Props(); double tol = 1e-4 * (pr.Max - pr.Min).Length + 1e-4;

        // thickness: the two radii of a bend share a centre
        double rIn = double.MaxValue, rOut = 0;
        var groups = new Dictionary<(long, long), List<double>>();
        for (int i = 0; i < nf; i++)
            if (cyl[i] is { } c) { var c2 = P2(c.Origin); var key = ((long)Math.Round(c2.x * 100), (long)Math.Round(c2.y * 100)); if (!groups.TryGetValue(key, out var l)) groups[key] = l = []; l.Add(c.Radius); }
        double t = 0;
        foreach (var g in groups.Values) if (g.Count >= 2) { var lo = g.Min(); var hi = g.Max(); if (hi - lo > 1e-6) { t = hi - lo; break; } }
        if (t <= 0) throw new InvalidOperationException("Could not find the thickness: each bend needs an inner and an outer surface.");
        double K = _k.Value;
        // nodes: planar faces whose plane contains the bend direction, and the bend faces
        var nodes = new List<Node>();
        for (int i = 0; i < nf; i++)
        {
            if (cyl[i] is { } c)
            {
                var loops = CamGeom.Loops(faces[i], 0.05); var c2 = P2(c.Origin);
                var ang = loops.SelectMany(l => l.pts).Select(p => { var q = P2(p); return Math.Atan2(q.y - c2.y, q.x - c2.x); }).OrderBy(a => a).ToList();
                if (ang.Count < 2) continue;
                int gi = 0; double gap = -1;
                for (int k = 0; k < ang.Count; k++) { double g = (ang[(k + 1) % ang.Count] - ang[k] + 2 * Math.PI) % (2 * Math.PI); if (k == ang.Count - 1) g = ang[0] + 2 * Math.PI - ang[k]; if (g > gap) { gap = g; gi = k; } }
                double aEnd = ang[gi], aStart = ang[(gi + 1) % ang.Count];
                (double, double) At(double a) => (c2.x + c.Radius * Math.Cos(a), c2.y + c.Radius * Math.Sin(a));
                nodes.Add(new Node { IsArc = true, Face = i, E0 = At(aStart), E1 = At(aEnd), Radius = c.Radius, Span = c.Span });
            }
            else
            {
                var fi = Occ.FaceInfoOf(faces[i]);
                if (!fi.Type.Contains("lane", StringComparison.OrdinalIgnoreCase) || Math.Abs(Vec3.Dot(fi.Normal.Normalized(), d)) > 1e-3) continue;
                var loops = CamGeom.Loops(faces[i], 0.05); var pts = loops.SelectMany(l => l.pts).Select(P2).ToList();
                var n3 = fi.Normal.Normalized(); (double x, double y) n2 = (Vec3.Dot(n3, e1), Vec3.Dot(n3, e2)); double nl = Math.Sqrt(n2.x * n2.x + n2.y * n2.y); n2 = (n2.x / nl, n2.y / nl);
                var t2 = (x: -n2.y, y: n2.x); double c0 = pts.Average(p => p.x * n2.x + p.y * n2.y);
                double lo = pts.Min(p => p.x * t2.x + p.y * t2.y), hi = pts.Max(p => p.x * t2.x + p.y * t2.y);
                nodes.Add(new Node { Face = i, E0 = (c0 * n2.x + lo * t2.x, c0 * n2.y + lo * t2.y), E1 = (c0 * n2.x + hi * t2.x, c0 * n2.y + hi * t2.y) });
            }
        }
        // connect nodes that touch at an end
        for (int a = 0; a < nodes.Count; a++)
            for (int b = a + 1; b < nodes.Count; b++)
            {
                var A = nodes[a]; var B = nodes[b];
                if (A.IsArc == B.IsArc) continue;                                              // a flat piece joins the next one through a bend, never directly (the thin end faces would link the two sides of the sheet)
                if (new[] { Dist(A.E0, B.E0), Dist(A.E0, B.E1), Dist(A.E1, B.E0), Dist(A.E1, B.E1) }.Min() < tol) { A.Next.Add(b); B.Next.Add(a); }
            }
        // follow the chain on the side of the first (largest) bend
        int start = nodes.FindIndex(x => x.IsArc && Math.Abs(x.Radius - nodes.Where(y => y.IsArc).Max(y => y.Radius)) < 1e-9);
        var comp = new HashSet<int> { start }; var queue = new Queue<int>([start]);
        while (queue.Count > 0) { int cur = queue.Dequeue(); foreach (var nx in nodes[cur].Next) if (comp.Add(nx)) queue.Enqueue(nx); }
        int head = comp.FirstOrDefault(i => nodes[i].Next.Count(x => comp.Contains(x)) <= 1, start);
        var order = new List<int> { head }; var seen = new HashSet<int> { head };
        for (int cur = head; ;) { int nx = nodes[cur].Next.FirstOrDefault(x => comp.Contains(x) && !seen.Contains(x), -1); if (nx < 0) break; order.Add(nx); seen.Add(nx); cur = nx; }
        // walk the chain: where each piece starts along the flat strip
        double s = 0; (double x, double y)? prevExit = null;
        for (int k = 0; k < order.Count; k++)
        {
            var nd = nodes[order[k]];
            if (k == 0)
            {
                var nxt = k + 1 < order.Count ? nodes[order[k + 1]] : null;
                bool e1Joins = nxt != null && (Dist(nd.E1, nxt.E0) < tol || Dist(nd.E1, nxt.E1) < tol);
                (nd.Enter, nd.Exit) = e1Joins ? (nd.E0, nd.E1) : nxt == null ? (nd.E0, nd.E1) : (nd.E1, nd.E0);
            }
            else (nd.Enter, nd.Exit) = Dist(nd.E0, prevExit!.Value) <= Dist(nd.E1, prevExit.Value) ? (nd.E0, nd.E1) : (nd.E1, nd.E0);
            nd.SStart = s;
            if (nd.IsArc)
            {
                var grp = groups.Values.First(g => g.Any(r => Math.Abs(r - nd.Radius) < 1e-6)); double rin = grp.Min();
                nd.SLen = nd.Span * (rin + K * t);
            }
            else { nd.SLen = Dist(nd.Enter, nd.Exit); var dd = Sub(nd.Exit, nd.Enter); nd.Dir = new Vec3(dd.x / Math.Max(nd.SLen, 1e-12), dd.y / Math.Max(nd.SLen, 1e-12), 0); }
            s += nd.SLen; prevExit = nd.Exit;
        }
        // flat outlines
        var shapes = new List<Shape>(); var bendCurves = new List<Shape>();
        double sMin = 0, yMin = double.MaxValue, yMax = double.MinValue;
        var loopsFlat = new List<List<List<(double x, double y)>>>();
        foreach (var idx in order)
        {
            var nd = nodes[idx]; var all = CamGeom.Loops(faces[nd.Face], 0.05);
            if (nd.IsArc)
            {
                double y0 = all.SelectMany(l => l.pts).Min(Yof), y1 = all.SelectMany(l => l.pts).Max(Yof);
                loopsFlat.Add([[(nd.SStart, y0), (nd.SStart + nd.SLen, y0), (nd.SStart + nd.SLen, y1), (nd.SStart, y1)]]);
                _bendLines.Add(((nd.SStart + nd.SLen / 2, y0), (nd.SStart + nd.SLen / 2, y1)));
                yMin = Math.Min(yMin, y0); yMax = Math.Max(yMax, y1);
                var ang = nd.Span * 180 / Math.PI; Results.Add(new ResultLine("Bend", $"{ang:0.#}°, allowance {nd.SLen:0.##} mm"));
            }
            else
            {
                var mapped = new List<List<(double x, double y)>>();
                foreach (var (pts, _) in all)
                {
                    var l = pts.Select(p => { var q = P2(p); var rel = Sub(q, nd.Enter); return (nd.SStart + rel.x * nd.Dir.X + rel.y * nd.Dir.Y, Yof(p)); }).ToList();
                    mapped.Add(l);
                    yMin = Math.Min(yMin, l.Min(p => p.Item2)); yMax = Math.Max(yMax, l.Max(p => p.Item2));
                }
                loopsFlat.Add(mapped);
            }
        }
        _loops = loopsFlat.SelectMany(f => f).ToList();
        double sMax = s;
        double dx = 0, dy = 0;
        if (_beside.Value) { dx = pr.Max.X + 20 - sMin; dy = pr.Min.Y - yMin; }
        Vec3 W((double x, double y) p) => new(p.x + dx, p.y + dy, 0);
        foreach (var f in loopsFlat)
            if (CamGeom.FaceFromLoops(f.Select(l => l.Select(W).ToList())) is { } fs) shapes.Add(fs);
        foreach (var (a, b) in _bendLines) bendCurves.Add(Occ.Line(W(a), W(b)));
        if (shapes.Count == 0) throw new InvalidOperationException("Could not build the flat pattern.");
        Results.Add(new ResultLine("Thickness", $"{t:0.###} mm"));
        Results.Add(new ResultLine("Flat size", $"{sMax - sMin:0.##} × {yMax - yMin:0.##} mm", 1));
        var o = new CommandOutput { Label = "Flat pattern", Icon = "flat-pattern" };
        o.Add.Add(new(shapes.Count == 1 ? shapes[0] : Occ.Compound(shapes), _name + " flat", ObjKind.Surface));
        if (bendCurves.Count > 0) o.Add.Add(new(bendCurves.Count == 1 ? bendCurves[0] : Occ.Compound(bendCurves), "Bend lines", ObjKind.Curve));
        return o;
    }

    void SaveDxf()
    {
        if (_loops.Count == 0) throw new InvalidOperationException("Pick a sheet metal body first.");
        double minx = _loops.Min(l => l.Min(p => p.x)), maxx = _loops.Max(l => l.Max(p => p.x)), miny = _loops.Min(l => l.Min(p => p.y)), maxy = _loops.Max(l => l.Max(p => p.y));
        const double m = 10;
        (double, double) T((double x, double y) p) => (p.x - minx + m, maxy - p.y + m);
        var prims = new List<Prim>();
        foreach (var l in _loops) prims.Add(new PolyP(l.Select(T).ToArray(), LineStyle.Visible, true));
        foreach (var (a, b) in _bendLines) prims.Add(new PolyP([T(a), T(b)], LineStyle.Center));
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "Drawings"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, string.Concat(_name.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + "_flat.dxf");
        DrawingOutput.Save(path, prims, maxx - minx + 2 * m, maxy - miny + 2 * m);
        Results.Add(new ResultLine("Saved", path)); S.Say("Flat pattern saved to " + path);
    }
}
