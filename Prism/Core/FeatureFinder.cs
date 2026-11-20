using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Prism.Kernel;

namespace Prism.Core;

public enum FeatureKind { Face, Hole, Boss, Slot, Pocket, Fillet, Counterbore, Countersink, Chamfer, Text }

/// <summary>How a click grows into a selection (the "intent" of a pick in the direct-editing tools).</summary>
public enum PickIntent { Feature, Face, Tangent, SameSize, Pattern }

/// <summary>A complete hole or boss: a point on its axis, the radius, whether it is a hole, and its extent along the axis.</summary>
public sealed record Round(Vec3 Origin, double Radius, bool Hole, double T0, double T1);

/// <summary>A recognised feature: its faces, what it is, a size key (equal keys mean "same feature again") and where it sits.</summary>
public sealed record Feat(int Face, FeatureKind Kind, string Key, Vec3 Centre, Vec3 Dir, IReadOnlyList<int> Faces, Round? Round)
{
    public string Name => Kind switch { FeatureKind.Hole => "Hole", FeatureKind.Boss => "Boss", FeatureKind.Slot => "Slot", FeatureKind.Pocket => "Pocket", FeatureKind.Fillet => "Fillet / round", FeatureKind.Counterbore => "Counterbore", FeatureKind.Countersink => "Countersink", FeatureKind.Chamfer => "Chamfer", FeatureKind.Text => "Text", _ => "Face" };
}

/// <summary>A chamfer's two setbacks measured from the sharp edge it replaced (along face A and face B), and the angle it makes with face A.</summary>
public sealed record ChamferSize(double SetbackA, double SetbackB, double AngleDeg, int FaceA, int FaceB, double Length)
{
    public bool EqualSetbacks => Math.Abs(SetbackA - SetbackB) < 1e-3;
}

/// <summary>A round hole's ends: through (open at both ends) or blind, its depth, the drill-point angle when it ends in a cone, and the nearest metric size.</summary>
public sealed record HoleDetail(bool Through, double Depth, double? PointAngleDeg, string? Standard);

/// <summary>Engraved (cut in) or embossed (raised) text: its depth or height, how many letters, how tall they are, the reading direction, and the plane it sits on (normal and height along it).</summary>
public sealed record TextInfo(bool Engraved, double Depth, int Letters, double Height, Vec3 Along, Vec3 Normal, double PlaneHeight);

/// <summary>
/// Works out what the faces of a body are: holes, bosses, slots, pockets and fillet chains, and which of them repeat in lines, grids or circles.
/// Direct-editing tools use it so a click on any one face selects the whole feature, and so edits can follow a pattern.
/// Everything is derived from the exact face types and the display mesh (shared boundary points give face adjacency), and cached per shape.
/// </summary>
public static class FeatureFinder
{
    enum Edge { Tangent, Convex, Concave }
    readonly record struct Link(int To, Edge Class, int Shared);

    sealed class Analysis
    {
        public required FaceInfo[] Info;
        public required (Vec3 Origin, Vec3 Dir)?[] Axis;
        public required Vec3[] Centre;
        public required List<Link>[] Adj;
        public required Vec3[] Normal;           // plane normal, or the mesh normal at the face's first point
        public required Vec3[] FirstPoint;
        public required MeshData Mesh;
        public List<Feat> Features = [];
        public int[] FeatureOfFace = [];
        public Dictionary<int, ChamferSize> Chamfers = [];
        public Dictionary<int, TextInfo> Texts = [];
    }

    static readonly ConditionalWeakTable<Shape, Analysis> Cache = new();

    /// <summary>The body's features, worked out once per shape.</summary>
    public static IReadOnlyList<Feat> Features(SceneObject body) => Get(body).Features;

    public static Feat? FeatureOf(SceneObject body, int face)
    {
        var a = Get(body);
        return face >= 0 && face < a.FeatureOfFace.Length && a.FeatureOfFace[face] >= 0 ? a.Features[a.FeatureOfFace[face]] : null;
    }

    static Analysis Get(SceneObject body) => Cache.GetValue(body.Shape, _ => Analyse(body));

    // ---------------------------------------------------------------- analysis
    static Analysis Analyse(SceneObject body)
    {
        var shape = body.Shape; var m = body.Mesh; int n = shape.Count(SubKind.Face);
        var info = new FaceInfo[n]; var axis = new (Vec3, Vec3)?[n];
        for (int f = 0; f < n; f++)
        {
            var sh = shape.Sub(SubKind.Face, f); info[f] = Occ.FaceInfoOf(sh);
            if (info[f].Type is "Cylinder" or "Cone") axis[f] = Occ.AxisOf(sh);
        }
        var a = new Analysis { Info = info, Axis = axis, Centre = new Vec3[n], Adj = new List<Link>[n], Normal = new Vec3[n], FirstPoint = new Vec3[n], Mesh = m };
        for (int f = 0; f < n; f++) a.Adj[f] = [];
        BuildAdjacency(a, n);
        FindFeatures(a, n);
        return a;
    }

    static Vec3 P(MeshData m, int vi) => new(m.Positions[3 * vi], m.Positions[3 * vi + 1], m.Positions[3 * vi + 2]);
    static Vec3 N(MeshData m, int vi) => new(m.Normals[3 * vi], m.Normals[3 * vi + 1], m.Normals[3 * vi + 2]);

    /// <summary>Two faces are neighbours when their triangulations share boundary points: those lie on the common edge, and only there.</summary>
    static void BuildAdjacency(Analysis a, int n)
    {
        var m = a.Mesh; double q = Math.Max((m.Max - m.Min).Length * 2e-5, 1e-6);
        var at = new Dictionary<(long, long, long), List<(int face, int vi)>>();
        var triOf = new int[m.Positions.Length / 3];
        // every triangle edge, keyed by its two end points: an edge two faces share lies on their common B-rep edge, with one triangle on each side
        var segs = new Dictionary<((long, long, long), (long, long, long)), List<(int face, int tri, int va, int vb)>>();
        (long, long, long) KeyOf(Vec3 p) => ((long)Math.Round(p.X / q), (long)Math.Round(p.Y / q), (long)Math.Round(p.Z / q));
        for (int f = 0; f < n && f + 1 < m.FaceTriStart.Length; f++)
        {
            Vec3 sum = new(0, 0, 0); double w = 0; var seen = new HashSet<int>(); bool first = true;
            for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
            {
                Vec3 A = P(m, m.Indices[3 * t]), B = P(m, m.Indices[3 * t + 1]), C = P(m, m.Indices[3 * t + 2]);
                double area = Vec3.Cross(B - A, C - A).Length * 0.5; sum += (A + B + C) * (area / 3); w += area;
                for (int c = 0; c < 3; c++)
                {
                    int i0 = m.Indices[3 * t + c], i1 = m.Indices[3 * t + (c + 1) % 3];
                    var k0 = KeyOf(P(m, i0)); var k1 = KeyOf(P(m, i1));
                    var sk = k0.CompareTo(k1) <= 0 ? (k0, k1) : (k1, k0);
                    if (!segs.TryGetValue(sk, out var sl)) segs[sk] = sl = [];
                    sl.Add((f, t, i0, i1));
                }
                for (int c = 0; c < 3; c++)
                {
                    int vi = m.Indices[3 * t + c]; triOf[vi] = t; if (!seen.Add(vi)) continue;
                    var p = P(m, vi);
                    if (first) { a.FirstPoint[f] = p; a.Normal[f] = N(m, vi); first = false; }
                    var key = ((long)Math.Round(p.X / q), (long)Math.Round(p.Y / q), (long)Math.Round(p.Z / q));
                    if (!at.TryGetValue(key, out var list)) at[key] = list = [];
                    list.Add((f, vi));
                }
            }
            a.Centre[f] = w > 1e-12 ? sum * (1 / w) : a.FirstPoint[f];
            // the plane's own normal, turned to face out of the body: a reversed face (after a mirror, or from some exporters) reports its
            // surface's normal, which then points into the material; the display mesh's normals always point out
            if (a.Info[f].Type == "Plane")
            {
                var pn = a.Info[f].Normal.Normalized();
                a.Normal[f] = !first && Vec3.Dot(pn, a.Normal[f]) < 0 ? -pn : pn;
            }
        }
        var pairs = new Dictionary<(int, int), (int count, double minDot, Vec3 mid, Vec3 nA, Vec3 nB, int tA, int tB)>();
        foreach (var list in at.Values)
        {
            if (list.Count < 2 || list.Count > 8) continue;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var (fa, va) = list[i]; var (fb, vb) = list[j]; if (fa == fb) continue;
                    if (fa > fb) { (fa, fb) = (fb, fa); (va, vb) = (vb, va); }
                    var na = N(m, va); var nb = N(m, vb); double dot = Vec3.Dot(na, nb);
                    // keep the first shared point and its two triangles: the convex/concave judgement is made right there (an average of
                    // the points would sit on the axis of a circular edge, far from the edge)
                    pairs[(fa, fb)] = pairs.TryGetValue((fa, fb), out var e) ? (e.count + 1, Math.Min(e.minDot, dot), e.mid, e.nA, e.nB, e.tA, e.tB) : (1, dot, P(m, va), na, nb, triOf[va], triOf[vb]);
                }
        }
        Vec3 TriCentre(int t) => (P(m, m.Indices[3 * t]) + P(m, m.Indices[3 * t + 1]) + P(m, m.Indices[3 * t + 2])) * (1.0 / 3);
        Vec3 TriNormal(int t) => N(m, m.Indices[3 * t]) + N(m, m.Indices[3 * t + 1]) + N(m, m.Indices[3 * t + 2]);
        // Inside or outside corner, voted along the whole shared edge: at the middle of each shared triangle edge, does the triangle on the other
        // side rise in front of this face (inside corner) or fall behind it (outside corner)? Judging at a single point went wrong at the inward
        // corners of letter outlines and on circular edges.
        var votes = new Dictionary<(int, int), double>();
        foreach (var list in segs.Values)
        {
            if (list.Count != 2 || list[0].face == list[1].face) continue;
            var (x, y) = list[0].face < list[1].face ? (list[0], list[1]) : (list[1], list[0]);
            var mid0 = (P(m, x.va) + P(m, x.vb)) * 0.5; double len = (P(m, x.va) - P(m, x.vb)).Length;
            double sv = Vec3.Dot(TriCentre(y.tri) - mid0, TriNormal(x.tri).Normalized()) + Vec3.Dot(TriCentre(x.tri) - mid0, TriNormal(y.tri).Normalized());
            votes[(x.face, y.face)] = votes.GetValueOrDefault((x.face, y.face)) + Math.Sign(sv) * len;
        }
        foreach (var (key, e) in pairs)
        {
            if (e.count < 2) continue;
            var (fa, fb) = key; var mid = e.mid;
            Edge cls;
            if (e.minDot > 0.9945) cls = Edge.Tangent;
            else
            {
                // convex: the next bit of each face lies behind the other's tangent plane; concave: in front of it (judged right at the edge, so big faces do not matter)
                double s = votes.TryGetValue((fa, fb), out var v) && Math.Abs(v) > 1e-12 ? v : Vec3.Dot(TriCentre(e.tB) - mid, e.nA) + Vec3.Dot(TriCentre(e.tA) - mid, e.nB);
                cls = s > 0 ? Edge.Concave : Edge.Convex;
            }
            a.Adj[fa].Add(new Link(fb, cls, e.count)); a.Adj[fb].Add(new Link(fa, cls, e.count));
        }
    }

    /// <summary>Plain-text dump of each face's type and neighbours (for diagnosing recognition).</summary>
    public static string Describe(SceneObject body)
    {
        var a = Get(body); var sb = new System.Text.StringBuilder();
        for (int f = 0; f < a.Adj.Length; f++)
            sb.AppendLine($"{f,3} {a.Info[f].Type,-9} n=({a.Normal[f].X:0.##},{a.Normal[f].Y:0.##},{a.Normal[f].Z:0.##}) -> " + string.Join(" ", a.Adj[f].Select(l => $"{l.To}:{(l.Class == Edge.Tangent ? "T" : l.Class == Edge.Convex ? "x" : "V")}{l.Shared}")));
        return sb.ToString();
    }

    // ---------------------------------------------------------------- recognition
    static bool Contains(Analysis a, int f, Vec3 d)
    {
        var i = a.Info[f];
        if (i.Type == "Plane") return Math.Abs(Vec3.Dot(a.Normal[f], d)) < 1e-3;
        if (i.Type == "Cylinder" && a.Axis[f] is { } ax) return Vec3.Cross(ax.Dir.Normalized(), d).Length < 1e-3;
        return false;
    }

    static IEnumerable<Vec3> Directions(Analysis a, int seed)
    {
        var found = new List<Vec3>();
        bool Add(Vec3 d) { if (d.Length < 1e-6) return false; d = d.Normalized(); if (found.Any(x => Vec3.Cross(x, d).Length < 1e-4)) return false; found.Add(d); return true; }
        if (a.Info[seed].Type == "Cylinder" && a.Axis[seed] is { } ax) Add(ax.Dir);
        foreach (var l in a.Adj[seed])
        {
            if (a.Info[seed].Type == "Plane" && a.Info[l.To].Type == "Plane" && Math.Abs(Vec3.Dot(a.Normal[seed], a.Normal[l.To])) < 1e-3)
            { Add(Vec3.Cross(a.Normal[seed], a.Normal[l.To])); Add(a.Normal[l.To]); }
            else if (a.Info[seed].Type == "Plane" && a.Axis[l.To] is { } ca && Math.Abs(Vec3.Dot(ca.Dir.Normalized(), a.Normal[seed])) < 1e-3) Add(ca.Dir);
        }
        return found;
    }

    /// <summary>The closed ring of faces around an axis that bounds a hole, slot or pocket (concave or smooth joins) or a boss (convex or smooth joins).</summary>
    static List<int>? Ring(Analysis a, int seed, Vec3 d, bool boss)
    {
        if (!Contains(a, seed, d)) return null;
        var comp = new List<int> { seed }; var set = new HashSet<int> { seed };
        for (int i = 0; i < comp.Count; i++)
            foreach (var l in a.Adj[comp[i]])
            {
                bool ok = l.Class == Edge.Tangent || l.Class == (boss ? Edge.Convex : Edge.Concave);
                if (!ok || set.Contains(l.To) || !Contains(a, l.To, d)) continue;
                set.Add(l.To); comp.Add(l.To);
                if (comp.Count > 64) return null;
            }
        if (comp.Count < 3) return null;
        // closed: every face joins two others of the ring
        foreach (int f in comp)
            if (a.Adj[f].Count(l => set.Contains(l.To) && (l.Class == Edge.Tangent || l.Class == (boss ? Edge.Convex : Edge.Concave))) < 2) return null;
        // A ring joined only by smooth edges reads both ways, so the walls decide: a hole's walls face in towards its axis, a boss's face out.
        // Without this the rounded outline of a plate is taken for one huge hole or slot.
        Vec3 mid = new(0, 0, 0); double w = 0;
        foreach (int f in comp) { double ar = Math.Max(a.Info[f].Area, 1e-9); mid += a.Centre[f] * ar; w += ar; }
        mid *= 1 / w; double inward = 0;
        foreach (int f in comp)
        {
            var r = mid - a.Centre[f]; r -= d * Vec3.Dot(r, d);
            if (r.Length > 1e-9) inward += Vec3.Dot(a.Normal[f], r.Normalized()) * a.Info[f].Area;
        }
        if (boss ? inward > 0 : inward < 0) return null;
        return comp;
    }

    static (FeatureKind kind, List<int> faces, Vec3 dir)? Loop(Analysis a, int seed)
    {
        foreach (var d in Directions(a, seed))
        {
            if (Ring(a, seed, d, false) is { } hole)
            {
                var faces = new List<int>(hole);
                // a blind pocket also has a floor: a flat face square to the axis that meets most of the ring in an inside corner
                var set = hole.ToHashSet(); var floors = new Dictionary<int, int>();
                foreach (int f in hole) foreach (var l in a.Adj[f])
                    if (!set.Contains(l.To) && l.Class == Edge.Concave && a.Info[l.To].Type == "Plane" && Math.Abs(Vec3.Dot(a.Normal[l.To], d)) > 0.999)
                        floors[l.To] = floors.GetValueOrDefault(l.To) + 1;
                foreach (var (fl, cnt) in floors) if (cnt * 2 >= hole.Count) faces.Add(fl);
                bool curved = hole.Any(f => a.Info[f].Type != "Plane"), flat = hole.Any(f => a.Info[f].Type == "Plane");
                return (curved && flat ? FeatureKind.Slot : curved ? FeatureKind.Hole : FeatureKind.Pocket, faces, d);
            }
            if (Ring(a, seed, d, true) is { } ring)
            {
                var set = ring.ToHashSet();
                // a boss stands on something: most of the ring meets a flat face square to the axis in an inside corner. One side alone is the end of a plate against a wall, not a boss
                bool stands = ring.Count(f => a.Adj[f].Any(l => !set.Contains(l.To) && l.Class == Edge.Concave && a.Info[l.To].Type == "Plane" && Math.Abs(Vec3.Dot(a.Normal[l.To], d)) > 0.999)) * 2 > ring.Count;
                if (stands)
                {
                    // the boss's top: a flat face square to the axis that meets most of the ring in an outside corner
                    var faces = new List<int>(ring); var caps = new Dictionary<int, int>();
                    foreach (int f in ring) foreach (var l in a.Adj[f])
                        if (!set.Contains(l.To) && l.Class == Edge.Convex && a.Info[l.To].Type == "Plane" && Math.Abs(Vec3.Dot(a.Normal[l.To], d)) > 0.999)
                            caps[l.To] = caps.GetValueOrDefault(l.To) + 1;
                    foreach (var (cap, cnt) in caps) if (cnt * 2 >= ring.Count) faces.Add(cap);
                    return (FeatureKind.Boss, faces, d);
                }
            }
        }
        return null;
    }

    static List<int> TangentChain(Analysis a, int seed, bool curvedOnly, int cap = 400)
    {
        var comp = new List<int> { seed }; var set = new HashSet<int> { seed };
        for (int i = 0; i < comp.Count && comp.Count < cap; i++)
            foreach (var l in a.Adj[comp[i]])
                if (l.Class == Edge.Tangent && !set.Contains(l.To) && (!curvedOnly || a.Info[l.To].Type != "Plane")) { set.Add(l.To); comp.Add(l.To); }
        return comp;
    }

    static void FindFeatures(Analysis a, int n)
    {
        a.FeatureOfFace = Enumerable.Repeat(-1, n).ToArray();
        void Add(FeatureKind kind, List<int> faces, Vec3 dir, string key, Round? round = null)
        {
            Vec3 c = new(0, 0, 0); double w = 0;
            foreach (int f in faces) { double ar = Math.Max(a.Info[f].Area, 1e-9); c += a.Centre[f] * ar; w += ar; }
            var feat = new Feat(faces[0], kind, key, round != null ? round.Origin + dir * ((round.T0 + round.T1) / 2) : c * (1 / w), dir, faces, round);
            foreach (int f in faces) a.FeatureOfFace[f] = a.Features.Count;
            a.Features.Add(feat);
        }
        // First pass: holes, bosses, slots and pockets. Fillet chains come second, or the rounded rim of a slot would be claimed as a fillet before its walls are looked at.
        for (int f = 0; f < n; f++)
        {
            if (a.FeatureOfFace[f] >= 0) continue;
            var info = a.Info[f];
            // a complete hole or boss: round faces sharing an axis and radius that go all the way round
            if (info.Type == "Cylinder" && a.Axis[f] is { } ax)
            {
                var dir = ax.Dir.Normalized(); var group = new List<int> { f };
                for (int g = f + 1; g < n; g++)
                {
                    if (a.FeatureOfFace[g] >= 0 || a.Info[g].Type != "Cylinder" || Math.Abs(a.Info[g].R1 - info.R1) > 1e-6 || a.Axis[g] is not { } ga) continue;
                    if (Vec3.Cross(ga.Dir.Normalized(), dir).Length > 1e-6) continue;
                    var off = ga.Origin - ax.Origin; if ((off - dir * Vec3.Dot(off, dir)).Length > 1e-5) continue;
                    group.Add(g);
                }
                var (t0, t1) = Extent(a, group, ax.Origin, dir);
                double full = 2 * Math.PI * info.R1 * (t1 - t0), have = group.Sum(g => a.Info[g].Area);
                if (have >= full * 0.9)
                {
                    var rel = a.FirstPoint[f] - ax.Origin; bool hole = Vec3.Dot(a.Normal[f], rel - dir * Vec3.Dot(rel, dir)) < 0;
                    Add(hole ? FeatureKind.Hole : FeatureKind.Boss, group, dir, $"cyl|{info.R1:0.####}|{hole}|{t1 - t0:0.###}", new Round(ax.Origin, info.R1, hole, t0, t1));
                    continue;
                }
            }
            if (Loop(a, f) is { } loop && loop.faces.All(g => a.FeatureOfFace[g] < 0))
            {
                var faces = loop.faces;
                // a slot with a rounded rim: the smooth curved faces that run on from its round walls belong to it
                if (loop.kind == FeatureKind.Slot)
                {
                    var extended = new List<int>(faces);
                    foreach (int c in faces.Where(g => a.Info[g].Type != "Plane"))
                        foreach (int g in TangentChain(a, c, true)) if (a.FeatureOfFace[g] < 0 && !extended.Contains(g)) extended.Add(g);
                    if (extended.Count <= 200) faces = extended;
                }
                double area = faces.Sum(g => a.Info[g].Area);
                Add(loop.kind, faces, loop.dir, $"{loop.kind}|{faces.Count}|{area:0.##}");
            }
        }
        // Chamfers before fillets: a flat strip at an outside corner is not part of any round chain.
        for (int f = 0; f < n; f++)
        {
            if (a.FeatureOfFace[f] >= 0 || ChamferAt(a, f) is not { } ch) continue;
            a.Chamfers[f] = ch;
            var (lo, hi) = ch.SetbackA <= ch.SetbackB ? (ch.SetbackA, ch.SetbackB) : (ch.SetbackB, ch.SetbackA);
            Add(FeatureKind.Chamfer, [f], a.Normal[f], $"chamfer|{lo:0.###}|{hi:0.###}");
        }
        // Second pass: fillet chains and plain faces.
        for (int f = 0; f < n; f++)
        {
            if (a.FeatureOfFace[f] >= 0) continue;
            var info = a.Info[f];
            if (info.Type != "Plane" && a.Adj[f].Any(l => l.Class == Edge.Tangent))
            {
                var chain = TangentChain(a, f, true).Where(g => a.FeatureOfFace[g] < 0).ToList();
                if (chain.Count > 0 && chain.Count < 200 && chain.Contains(f))
                { Add(FeatureKind.Fillet, chain, Vec3.UnitZ, $"fillet|{chain.Count}|{chain.Sum(g => a.Info[g].Area):0.##}"); continue; }
            }
            var nrm = info.Type == "Plane" ? a.Normal[f] : Vec3.UnitZ;
            string key = info.Type == "Plane" ? $"pln|{info.Area:0.###}|{nrm.X:0.###},{nrm.Y:0.###},{nrm.Z:0.###}" : $"{info.Type}|{info.Area:0.###}|{info.R1:0.####}|{info.R2:0.####}";
            Add(FeatureKind.Face, [f], nrm, key);
        }
        MergeSteppedHoles(a, n, (k, fc, d, key, r) => Add(k, fc, d, key, r));
        FindText(a, n);
    }

    /// <summary>
    /// Engraved or embossed text: letters cut to one depth below a flat face (or raised to one height above it). Each letter is a floor (or a top)
    /// whose every neighbour is a wall running along the face's normal, and whose walls end on faces lying in one plane (the face the text is
    /// on, and the islands inside letters such as O). Letters close together at the same depth make one word. A lone pocket is not text: it
    /// takes at least two letters and at least one with the many walls or curved walls that letter outlines have.
    /// </summary>
    static void FindText(Analysis a, int n)
    {
        var letters = new List<(int top, List<int> walls, Vec3 nrm, double rim, double depth, bool engraved, double size, Vec3 centre)>();
        for (int f = 0; f < n; f++)
        {
            if (a.Info[f].Type != "Plane" || a.Adj[f].Count < 3) continue;
            var nf = a.Normal[f];
            var walls = a.Adj[f].Select(l => l.To).Distinct().ToList();
            var cls = a.Adj[f].Select(l => l.Class).Distinct().ToList();
            if (cls.Count != 1 || cls[0] == Edge.Tangent) continue;
            bool engraved = cls[0] == Edge.Concave;
            if (walls.Any(w => Math.Abs(Vec3.Dot(a.Normal[w], nf)) > 0.05 || a.Info[w].Type is "Cylinder" && a.Axis[w] is { } ax && Vec3.Cross(ax.Dir.Normalized(), nf).Length > 1e-3)) continue;
            var wallSet = walls.ToHashSet(); wallSet.Add(f);
            var rims = new HashSet<int>(); bool ok = true;
            foreach (int w in walls)
                foreach (var l in a.Adj[w])
                {
                    if (wallSet.Contains(l.To)) continue;
                    if (a.Info[l.To].Type != "Plane" || Vec3.Dot(a.Normal[l.To], nf) < 0.999 || l.Class != (engraved ? Edge.Convex : Edge.Concave)) { ok = false; break; }
                    rims.Add(l.To);
                }
            if (!ok || rims.Count == 0) continue;
            double h0 = Vec3.Dot(a.Centre[rims.First()], nf);
            if (rims.Any(r => Math.Abs(Vec3.Dot(a.Centre[r], nf) - h0) > 1e-4)) continue;
            double depth = engraved ? h0 - Vec3.Dot(a.Centre[f], nf) : Vec3.Dot(a.Centre[f], nf) - h0;
            if (depth <= 1e-6) continue;
            var (u, v) = Meshing.SurfaceMath.Basis(nf);
            var (u0, u1) = Span(a, f, u); var (v0, v1) = Span(a, f, v);
            letters.Add((f, walls, nf, h0, depth, engraved, Math.Max(u1 - u0, v1 - v0), a.Centre[f]));
        }
        if (letters.Count < 2) return;
        // words: letters on the same plane at the same depth, each within a few letter sizes of another
        var group = Enumerable.Range(0, letters.Count).ToArray();
        int Root(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
        for (int i = 0; i < letters.Count; i++)
            for (int j = i + 1; j < letters.Count; j++)
            {
                var (p, q) = (letters[i], letters[j]);
                if (p.engraved != q.engraved || Vec3.Dot(p.nrm, q.nrm) < 0.999 || Math.Abs(p.rim - q.rim) > 1e-4 || Math.Abs(p.depth - q.depth) > 1e-3) continue;
                if ((p.centre - q.centre).Length > 2.5 * Math.Max(p.size, q.size)) continue;
                group[Root(i)] = Root(j);
            }
        var merges = new List<(List<int> faces, TextInfo info)>();
        foreach (var word in Enumerable.Range(0, letters.Count).GroupBy(Root).Select(g => g.Select(i => letters[i]).ToList()))
        {
            if (word.Count < 2) continue;
            bool letterLike = word.Any(l => l.walls.Count >= 8 || l.walls.Any(w => a.Info[w].Type != "Plane"));
            if (!letterLike) continue;
            var nrm = word[0].nrm; var (bu, bv) = Meshing.SurfaceMath.Basis(nrm);
            // reading direction: the long way of the letters' spread
            var cs = word.Select(l => l.centre).ToList(); var mid = cs.Aggregate(Vec3.Zero, (s, c) => s + c) * (1.0 / cs.Count);
            double suu = 0, suv = 0, svv = 0;
            foreach (var c in cs) { double x = Vec3.Dot(c - mid, bu), y = Vec3.Dot(c - mid, bv); suu += x * x; suv += x * y; svv += y * y; }
            double ang = 0.5 * Math.Atan2(2 * suv, suu - svv);
            var along = (bu * Math.Cos(ang) + bv * Math.Sin(ang)).Normalized(); var up = Vec3.Cross(nrm, along).Normalized();
            var faces = word.SelectMany(l => l.walls.Prepend(l.top)).Distinct().ToList();
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (int f in faces) { var (s0, s1) = Span(a, f, up); lo = Math.Min(lo, s0); hi = Math.Max(hi, s1); }
            merges.Add((faces, new TextInfo(word[0].engraved, word[0].depth, word.Count, hi - lo, along, nrm, word[0].rim)));
        }
        if (merges.Count == 0) return;
        var drop = merges.SelectMany(m => m.faces).ToHashSet();
        var keep = a.Features.Where(ft => !ft.Faces.Any(drop.Contains)).ToList();
        // a feature that lost only some of its faces to the text keeps the rest as plain faces
        var orphans = a.Features.Where(ft => ft.Faces.Any(drop.Contains)).SelectMany(ft => ft.Faces).Where(f => !drop.Contains(f)).ToList();
        a.Features.Clear(); a.FeatureOfFace = Enumerable.Repeat(-1, n).ToArray();
        foreach (var ft in keep) { foreach (int g in ft.Faces) a.FeatureOfFace[g] = a.Features.Count; a.Features.Add(ft); }
        foreach (int f in orphans)
        {
            var nrm = a.Info[f].Type == "Plane" ? a.Normal[f] : Vec3.UnitZ;
            a.FeatureOfFace[f] = a.Features.Count; a.Features.Add(new Feat(f, FeatureKind.Face, $"face|{f}", a.Centre[f], nrm, [f], null));
        }
        foreach (var (faces, info) in merges)
        {
            Vec3 c = Vec3.Zero; double w = 0;
            foreach (int f in faces) { double ar = Math.Max(a.Info[f].Area, 1e-9); c += a.Centre[f] * ar; w += ar; }
            foreach (int f in faces) a.FeatureOfFace[f] = a.Features.Count;
            a.Texts[faces[0]] = info;
            a.Features.Add(new Feat(faces[0], FeatureKind.Text, $"text|{info.Engraved}|{info.Depth:0.###}|{info.Letters}|{info.Height:0.##}", c * (1 / w), info.Normal, faces, null));
        }
    }

    /// <summary>What a recognised piece of text is: engraved or embossed, its depth or height, how many letters, and how tall they stand.</summary>
    public static TextInfo? TextOf(SceneObject body, Feat feat) =>
        feat.Kind == FeatureKind.Text && Get(body).Texts.TryGetValue(feat.Face, out var t) ? t : null;

    /// <summary>
    /// Joins a round hole to what steps or flares it: a flat annulus square to the axis between two coaxial holes of different radius is a counterbore
    /// (both bores and the annulus), and a cone coaxial with a hole is a countersink (the cone and the hole).
    /// </summary>
    static void MergeSteppedHoles(Analysis a, int n, Action<FeatureKind, List<int>, Vec3, string, Round?> add)
    {
        bool Coaxial(Round r, Vec3 d, Vec3 origin, Vec3 dir)
        {
            if (Vec3.Cross(d.Normalized(), dir.Normalized()).Length > 1e-4) return false;
            var off = origin - r.Origin; return (off - dir.Normalized() * Vec3.Dot(off, dir.Normalized())).Length < 1e-4;
        }
        var merges = new List<(FeatureKind kind, List<int> faces, Vec3 dir, string key)>(); var taken = new HashSet<int>();
        for (int f = 0; f < n; f++)
        {
            int own = a.FeatureOfFace[f]; if (own < 0 || a.Features[own].Kind != FeatureKind.Face) continue;
            var holes = new List<Feat>();
            foreach (var l in a.Adj[f])
            {
                int fi = a.FeatureOfFace[l.To];
                if (fi >= 0 && a.Features[fi] is { Kind: FeatureKind.Hole, Round: not null } h && !holes.Contains(h) && !taken.Contains(fi)) holes.Add(h);
            }
            if (a.Info[f].Type == "Plane")
            {
                if (holes.Count != 2) continue;
                var (h1, h2) = (holes[0], holes[1]);
                if (Math.Abs(Vec3.Dot(a.Normal[f], h1.Dir.Normalized())) < 0.999 || !Coaxial(h1.Round!, h1.Dir, h2.Round!.Origin, h2.Dir) || Math.Abs(h1.Round!.Radius - h2.Round.Radius) < 1e-6) continue;
                var big = h1.Round.Radius > h2.Round.Radius ? h1 : h2; var small = big == h1 ? h2 : h1;
                taken.Add(a.FeatureOfFace[h1.Face]); taken.Add(a.FeatureOfFace[h2.Face]);
                merges.Add((FeatureKind.Counterbore, [.. big.Faces, .. small.Faces, f], small.Dir,
                    $"cbore|{big.Round!.Radius:0.####}|{small.Round!.Radius:0.####}|{big.Round.T1 - big.Round.T0:0.###}|{small.Round.T1 - small.Round.T0:0.###}"));
            }
            else if (a.Info[f].Type == "Cone" && a.Axis[f] is { } ax)
            {
                var h = holes.FirstOrDefault(x => Coaxial(x.Round!, x.Dir, ax.Origin, ax.Dir));
                if (h == null) continue;
                if (RadialSpan(a, f, ax.Origin, ax.Dir.Normalized()).lo < 0.05 * h.Round!.Radius) continue;   // a cone that closes to a tip is a drill point, not a countersink
                taken.Add(a.FeatureOfFace[h.Face]);
                merges.Add((FeatureKind.Countersink, [.. h.Faces, f], h.Dir, $"csink|{h.Round!.Radius:0.####}|{a.Info[f].Area:0.###}"));
            }
        }
        if (merges.Count == 0) return;
        var drop = merges.SelectMany(m => m.faces).ToHashSet();
        var keep = a.Features.Where(ft => !ft.Faces.Any(drop.Contains)).ToList();
        a.Features.Clear(); a.FeatureOfFace = Enumerable.Repeat(-1, n).ToArray();
        foreach (var ft in keep) { foreach (int g in ft.Faces) a.FeatureOfFace[g] = a.Features.Count; a.Features.Add(ft); }
        foreach (var m in merges) add(m.kind, m.faces, m.dir, m.key, null);
    }

    /// <summary>
    /// A flat strip that replaced a sharp outside edge between two faces: both of its long sides meet those faces at outside corners, it leans
    /// part-way between them, and it is narrow next to its length. The setbacks follow from its width and the two angles (law of sines in the
    /// section square to the edge). A cone joining a boss's wall to its top at outside corners is a chamfer on a round edge.
    /// </summary>
    static ChamferSize? ChamferAt(Analysis a, int f)
    {
        var info = a.Info[f];
        if (info.Type == "Cone" && a.Axis[f] is { } cax) return RoundChamfer(a, f, cax);
        if (info.Type != "Plane") return null;
        var nf = a.Normal[f];
        if (a.Adj[f].Any(l => l.Class != Edge.Convex)) return null;
        var sides = a.Adj[f].Where(l => a.Info[l.To].Type == "Plane").OrderByDescending(l => l.Shared).ToList();
        if (sides.Count < 2) return null;
        for (int i = 0; i < sides.Count; i++)
            for (int j = i + 1; j < sides.Count; j++)
            {
                int fa = sides[i].To, fb = sides[j].To; Vec3 na = a.Normal[fa], nb = a.Normal[fb];
                var e = Vec3.Cross(na, nb); if (e.Length < 0.1) continue;     // the two faces must meet at a real angle
                e = e.Normalized(); if (Math.Abs(Vec3.Dot(nf, e)) > 1e-3) continue;
                double ba = Math.Acos(Math.Clamp(Vec3.Dot(nf, na), -1, 1)), bb = Math.Acos(Math.Clamp(Vec3.Dot(nf, nb), -1, 1));
                if (ba < 0.17 || bb < 0.17) continue;                          // under about 10 degrees it is a slight bend, not a chamfer
                double turn = Math.Acos(Math.Clamp(Vec3.Dot(na, nb), -1, 1));  // how far the normal turns across the edge
                if (Math.Abs(ba + bb - turn) > 1e-3) continue;                 // the strip must lean between the two faces
                var u = Vec3.Cross(e, nf).Normalized();
                var (w0, w1) = Span(a, f, u); var (l0, l1) = Span(a, f, e);
                double width = w1 - w0, length = l1 - l0;
                if (width <= 1e-6 || (width > 0.5 * length && width > 0.08 * (a.Mesh.Max - a.Mesh.Min).Length)) continue;
                if (info.Area > Math.Min(a.Info[fa].Area, a.Info[fb].Area)) continue;   // a bevel bigger than a face it joins is a face of its own
                // section square to the edge: the old sharp corner's angle is pi - turn, and the strip meets face A at angle ba
                double corner = Math.PI - turn, atB = Math.PI - corner - ba;
                double setA = width * Math.Sin(atB) / Math.Sin(corner), setB = width * Math.Sin(ba) / Math.Sin(corner);
                return new ChamferSize(setA, setB, ba * 180 / Math.PI, fa, fb, length);
            }
        return null;
    }

    static ChamferSize? RoundChamfer(Analysis a, int f, (Vec3 Origin, Vec3 Dir) ax)
    {
        var d = ax.Dir.Normalized(); int wall = -1, top = -1;
        foreach (var l in a.Adj[f])
        {
            if (l.Class != Edge.Convex) return null;
            var t = a.Info[l.To].Type;
            if (t == "Cylinder" && a.Axis[l.To] is { } w && Vec3.Cross(w.Dir.Normalized(), d).Length < 1e-4) wall = l.To;
            else if (t == "Plane" && Math.Abs(Vec3.Dot(a.Normal[l.To], d)) > 0.999) top = l.To;
        }
        if (wall < 0 || top < 0) return null;
        // only on a boss: the same cone at the mouth of a hole is a countersink, which belongs to the hole
        var out_ = a.FirstPoint[wall] - ax.Origin; out_ -= d * Vec3.Dot(out_, d);
        if (Vec3.Dot(a.Normal[wall], out_) <= 0) return null;
        var (r0, r1) = RadialSpan(a, f, ax.Origin, d); var (t0, t1) = Span(a, f, d);
        double setTop = r1 - r0, setWall = t1 - t0;
        if (setTop <= 1e-6 || setWall <= 1e-6) return null;
        return new ChamferSize(setWall, setTop, Math.Atan2(setTop, setWall) * 180 / Math.PI, wall, top, 2 * Math.PI * r1);
    }

    static (double lo, double hi) Span(Analysis a, int f, Vec3 dir)
    {
        var m = a.Mesh; double lo = double.MaxValue, hi = double.MinValue;
        for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
            for (int c = 0; c < 3; c++) { double v = Vec3.Dot(P(m, m.Indices[3 * t + c]), dir); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        return (lo, hi);
    }

    static (double lo, double hi) RadialSpan(Analysis a, int f, Vec3 origin, Vec3 dir)
    {
        var m = a.Mesh; double lo = double.MaxValue, hi = double.MinValue;
        for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
            for (int c = 0; c < 3; c++) { var r = P(m, m.Indices[3 * t + c]) - origin; r -= dir * Vec3.Dot(r, dir); lo = Math.Min(lo, r.Length); hi = Math.Max(hi, r.Length); }
        return (lo, hi);
    }

    /// <summary>The setbacks of a recognised chamfer, or null for anything else.</summary>
    public static ChamferSize? ChamferOf(SceneObject body, Feat feat) =>
        feat.Kind == FeatureKind.Chamfer && Get(body).Chamfers.TryGetValue(feat.Face, out var c) ? c : null;

    /// <summary>
    /// How a round hole ends. It is blind when a flat floor square to its axis or a coaxial cone (a drill point) closes one end, through otherwise.
    /// Depth is the length of the round wall; the standard is the nearest metric clearance, tapping or nominal size.
    /// </summary>
    public static HoleDetail? HoleOf(SceneObject body, Feat feat)
    {
        if (feat.Round is not { Hole: true } r) return null;
        var a = Get(body); var d = feat.Dir.Normalized(); var own = feat.Faces.ToHashSet();
        bool floor = false; double? point = null;
        foreach (int f in feat.Faces)
            foreach (var l in a.Adj[f])
            {
                if (own.Contains(l.To)) continue;
                var info = a.Info[l.To];
                if (info.Type == "Plane" && l.Class == Edge.Concave && Math.Abs(Vec3.Dot(a.Normal[l.To], d)) > 0.999) floor = true;
                else if (info.Type == "Cone" && a.Axis[l.To] is { } c && Vec3.Cross(c.Dir.Normalized(), d).Length < 1e-4)
                {
                    var (r0, r1) = RadialSpan(a, l.To, r.Origin, d); var (t0, t1) = Span(a, l.To, d);
                    if (r0 < 0.05 * r.Radius) point = 2 * Math.Atan2(r1 - r0, Math.Max(t1 - t0, 1e-9)) * 180 / Math.PI;
                }
            }
        return new HoleDetail(!floor && point == null, r.T1 - r.T0, point, StandardHoles.Match(2 * r.Radius));
    }

    static (double t0, double t1) Extent(Analysis a, IEnumerable<int> faces, Vec3 origin, Vec3 dir)
    {
        var m = a.Mesh; double t0 = double.MaxValue, t1 = double.MinValue;
        foreach (int f in faces)
            for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
                for (int c = 0; c < 3; c++) { double d = Vec3.Dot(P(m, m.Indices[3 * t + c]) - origin, dir); t0 = Math.Min(t0, d); t1 = Math.Max(t1, d); }
        return (t0, t1);
    }

    // ---------------------------------------------------------------- picking and patterns
    /// <summary>The faces a click on <paramref name="face"/> should select for the given intent.</summary>
    public static List<int> Expand(SceneObject body, int face, PickIntent intent)
    {
        var a = Get(body); var feat = FeatureOf(body, face);
        switch (intent)
        {
            case PickIntent.Face: return [face];
            case PickIntent.Tangent: return TangentChain(a, face, false);
            case PickIntent.Feature: return feat != null ? [.. feat.Faces] : [face];
            default:
                if (feat == null) return [face];
                var picked = new List<Feat> { feat };
                picked.AddRange(Siblings(body, feat, intent == PickIntent.Pattern));
                return picked.SelectMany(f => f.Faces).Distinct().ToList();
        }
    }

    /// <summary>Other features of the same kind and size: all of them, or only those laid out as one line, grid or circle with <paramref name="feat"/>.</summary>
    public static List<Feat> Siblings(SceneObject body, Feat feat, bool patternOnly, ICollection<int>? exclude = null)
    {
        var sib = Features(body).Where(f => f != feat && f.Key == feat.Key && (exclude == null || !f.Faces.Any(exclude.Contains))).ToList();
        if (!patternOnly || sib.Count == 0) return sib;
        var centres = sib.Select(f => f.Centre).ToList();
        var lat = LatticeMembers(feat.Centre, centres); var circ = CircleMembers(feat.Centre, feat.Dir, centres);
        return (circ.Count > lat.Count ? circ : lat).Select(i => sib[i]).ToList();
    }

    /// <summary>How many other features form a pattern with this one (0 when it stands alone).</summary>
    public static int PatternSize(SceneObject body, Feat feat) => Siblings(body, feat, true).Count;

    /// <summary>Members of the same regular grid (or line) as the picked feature, from the centres of its matching siblings.</summary>
    static HashSet<int> LatticeMembers(Vec3 c0, List<Vec3> others)
    {
        var res = new HashSet<int>(); if (others.Count == 0) return res;
        var d = others.Select(o => o - c0).ToList();
        int i1 = Enumerable.Range(0, d.Count).OrderBy(i => d[i].Length).First(); var v1 = d[i1]; double l1 = v1.Length;
        if (l1 < 1e-6) return res;
        int i2 = -1; double best = double.MaxValue;
        for (int i = 0; i < d.Count; i++)
        {
            double len = d[i].Length; if (len < 1e-6 || Vec3.Cross(v1, d[i]).Length < 1e-3 * l1 * len) continue;
            if (len < best) { best = len; i2 = i; }
        }
        double tol = 1e-3 * l1 + 1e-4;
        var line = new HashSet<int>();
        for (int i = 0; i < d.Count; i++)
        {
            double k = Vec3.Dot(d[i], v1) / (l1 * l1);
            if (Math.Abs(k - Math.Round(k)) * l1 < tol && (d[i] - v1 * Math.Round(k)).Length < tol) line.Add(i);
        }
        if (i2 < 0) return line;
        // a second direction only counts as a grid when some point lies off both axes (otherwise it is just a stray feature)
        var v2 = d[i2]; var grid = new HashSet<int>(); bool diagonal = false;
        double a11 = Vec3.Dot(v1, v1), a12 = Vec3.Dot(v1, v2), a22 = Vec3.Dot(v2, v2), det = a11 * a22 - a12 * a12;
        for (int i = 0; i < d.Count; i++)
        {
            double r1 = Vec3.Dot(d[i], v1), r2 = Vec3.Dot(d[i], v2);
            double p = (r1 * a22 - r2 * a12) / det, q = (a11 * r2 - a12 * r1) / det;
            if ((d[i] - v1 * Math.Round(p) - v2 * Math.Round(q)).Length >= tol || Math.Abs(p - Math.Round(p)) > 1e-2 || Math.Abs(q - Math.Round(q)) > 1e-2) continue;
            grid.Add(i); if (Math.Round(p) != 0 && Math.Round(q) != 0) diagonal = true;
        }
        return diagonal ? grid : line;
    }

    /// <summary>Members of the circular pattern through the picked feature about its own axis: equal steps on one circle.</summary>
    static HashSet<int> CircleMembers(Vec3 c0, Vec3 axis, List<Vec3> others)
    {
        var best = new HashSet<int>(); var n = axis.Normalized();
        Vec3 Flat(Vec3 v) => v - n * Vec3.Dot(v, n);
        for (int a = 0; a < others.Count; a++)
            for (int b = a + 1; b < others.Count; b++)
            {
                Vec3 p = Flat(others[a] - c0), q = Flat(others[b] - c0); var cr = Vec3.Cross(p, q);
                if (cr.Length < 1e-6) continue;
                var centre = (Vec3.Cross(cr, p) * Vec3.Dot(q, q) + Vec3.Cross(q, cr) * Vec3.Dot(p, p)) * (1 / (2 * Vec3.Dot(cr, cr)));
                double rad = centre.Length; if (rad < 1e-6) continue;
                var ux = (-centre).Normalized(); var uy = Vec3.Cross(n, ux);
                var members = new Dictionary<int, double>();
                for (int i = 0; i < others.Count; i++)
                {
                    var rel = Flat(others[i] - c0) - centre; var ax = Vec3.Dot(others[i] - c0, n);
                    if (Math.Abs(rel.Length - rad) > 1e-3 * rad + 1e-4 || Math.Abs(ax) > 1e-3 * rad + 1e-4) continue;
                    double ang = Math.Atan2(Vec3.Dot(rel, uy), Vec3.Dot(rel, ux)); if (ang < 0) ang += 2 * Math.PI;
                    members[i] = ang;
                }
                if (members.Count < 2) continue;
                double step = members.Values.Min();
                if (step < 1e-3 || members.Values.Any(v => Math.Abs(v / step - Math.Round(v / step)) > 0.02)) continue;
                if (members.Count > best.Count) best = [.. members.Keys];
            }
        return best;
    }

    // ---------------------------------------------------------------- pattern geometry
    /// <summary>How a feature repeats: a line or grid of equal steps (<c>V1</c>, <c>V2</c>) or a circle (<c>Centre</c>, <c>Axis</c>, <c>Step</c> in radians).</summary>
    public enum PatternKind { None, Line, Grid, Circle }

    /// <summary>
    /// The pattern a feature belongs to. <c>Others</c> are the other members and <c>Cells</c> where each sits: whole steps along V1 and V2 from the picked
    /// feature (negative when it lies before it), or for a circle (steps, 0) counted anticlockwise about <c>Axis</c>. The picked feature is cell (0, 0).
    /// </summary>
    public sealed record PatternInfo(PatternKind Kind, IReadOnlyList<Feat> Others, IReadOnlyList<(int A, int B)> Cells, Vec3 V1, Vec3 V2, Vec3 Centre, Vec3 Axis, double Step)
    {
        public int MinA => Cells.Count == 0 ? 0 : Math.Min(0, Cells.Min(c => c.A));
        public int MaxA => Cells.Count == 0 ? 0 : Math.Max(0, Cells.Max(c => c.A));
        public int MinB => Cells.Count == 0 ? 0 : Math.Min(0, Cells.Min(c => c.B));
        public int MaxB => Cells.Count == 0 ? 0 : Math.Max(0, Cells.Max(c => c.B));
    }

    /// <summary>
    /// A grid read the way a person describes it: rows along <c>Along</c> with <c>Step</c> between neighbours, rows <c>RowSpacing</c> apart along
    /// <c>Across</c>, and every other row shifted by <c>Shift</c> along the rows (0 for a plain grid, half the step for a staggered one).
    /// <c>Cells</c> give (place in its row, row) for each of the pattern's other members, in the order of <see cref="PatternInfo.Others"/>;
    /// the picked feature is (0, 0), and odd rows are the shifted ones.
    /// </summary>
    public sealed record PatternRows(Vec3 Along, double Step, Vec3 Across, double RowSpacing, double Shift, IReadOnlyList<(int A, int B)> Cells)
    {
        public bool Staggered => Shift > 1e-6;
    }

    /// <summary>
    /// The rows reading of a grid pattern, or null when it has none (a line, a circle, or a slanted lattice that is not rows of equal steps with at
    /// most every other row shifted). Row directions along a world axis are preferred, then those with the most features per row, then shorter steps.
    /// </summary>
    public static PatternRows? RowsOf(PatternInfo p)
    {
        if (p.Kind != PatternKind.Grid) return null;
        var d = new List<Vec3> { Vec3.Zero };
        for (int i = 0; i < p.Others.Count; i++) d.Add(p.V1 * p.Cells[i].A + p.V2 * p.Cells[i].B);
        double tol = 1e-3 * Math.Min(p.V1.Length, p.V2.Length) + 1e-4;
        var normal = Vec3.Cross(p.V1, p.V2).Normalized();
        var cands = new List<Vec3>();
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                var u = p.V1 * i + p.V2 * j; if (u.Length < 1e-6) continue;
                // one candidate per direction, the shortest lattice step along it (a longer multiple would skip every other feature)
                int same = cands.FindIndex(c => Vec3.Cross(c, u).Length < 1e-6 * c.Length * u.Length);
                if (same < 0) cands.Add(u); else if (u.Length < cands[same].Length - 1e-9) cands[same] = u;
            }
        PatternRows? best = null; (int axis, double perRow, double len) bestScore = (int.MaxValue, 0, 0);
        foreach (var u in cands)
        {
            // point both directions the way their largest component is positive, so the reading does not depend on which feature was clicked
            var along = Positive(u.Normalized()); var across = Positive(Vec3.Cross(normal, along).Normalized()); double step = u.Length;
            var ts = d.Select(v => Vec3.Dot(v, across)).ToList();
            var rowTs = ts.OrderBy(t => t).Aggregate(new List<double>(), (acc, t) => { if (acc.Count == 0 || t - acc[^1] > tol) acc.Add(t); return acc; });
            if (rowTs.Count < 2) continue;
            double spacing = Enumerable.Range(1, rowTs.Count - 1).Min(k => rowTs[k] - rowTs[k - 1]);
            if (spacing < tol || rowTs.Any(t => Math.Abs(t / spacing - Math.Round(t / spacing)) * spacing > tol)) continue;
            // offsets along the row, relative to the picked feature's row: even rows must sit on whole steps, odd rows on whole steps plus one shift
            double? shift = null; bool ok = true; var cells = new List<(int, int)>();
            for (int k = 0; k < d.Count && ok; k++)
            {
                int b = (int)Math.Round(ts[k] / spacing); double x = Vec3.Dot(d[k], along);
                double frac = x / step - Math.Floor(x / step + tol / step); if (frac * step > step - tol) frac = 0;
                double off = frac * step;
                if (b % 2 == 0) { if (off > tol) ok = false; }
                else if (shift == null) shift = off;
                else if (Math.Abs(off - shift.Value) > tol) ok = false;
                if (k > 0) cells.Add(((int)Math.Round((x - (b % 2 != 0 ? shift ?? 0 : 0)) / step), b));
            }
            if (!ok) continue;
            int axis = Math.Abs(along.X) > 0.9999 ? 0 : Math.Abs(along.Y) > 0.9999 ? 1 : Math.Abs(along.Z) > 0.9999 ? 2 : 3;
            double perRow = (double)d.Count / rowTs.Count;
            var score = (axis, perRow, step);
            bool better = best == null || score.axis < bestScore.axis
                || score.axis == bestScore.axis && (score.perRow > bestScore.perRow + 1e-9 || Math.Abs(score.perRow - bestScore.perRow) < 1e-9 && score.step < bestScore.len - tol);
            if (!better) continue;
            best = new PatternRows(along, step, across, spacing, shift is { } s2 && s2 > tol ? s2 : 0, cells);
            bestScore = (score.axis, score.perRow, score.step);
        }
        return best;
    }

    static Vec3 Positive(Vec3 v)
    {
        double ax = Math.Abs(v.X), ay = Math.Abs(v.Y), az = Math.Abs(v.Z);
        double big = ax >= ay - 1e-9 && ax >= az - 1e-9 ? v.X : ay >= az - 1e-9 ? v.Y : v.Z;
        return big < 0 ? -v : v;
    }

    /// <summary>A sentence describing the pattern a feature belongs to, such as "50 slots in staggered rows: 40 mm apart along X, rows 20 mm apart along Y, every other row shifted 20 mm".</summary>
    public static string? PatternText(SceneObject body, Feat feat)
    {
        var p = PatternOf(body, feat); if (p.Kind == PatternKind.None) return null;
        int n = p.Others.Count + 1; string what = n + " " + feat.Name.ToLowerInvariant() + (n > 1 ? "s" : "");
        string Axis(Vec3 v) => Math.Abs(v.X) > 0.9999 ? "X" : Math.Abs(v.Y) > 0.9999 ? "Y" : Math.Abs(v.Z) > 0.9999 ? "Z" : $"({v.X:0.###}, {v.Y:0.###}, {v.Z:0.###})";
        switch (p.Kind)
        {
            case PatternKind.Circle: return $"{what} on a circle, {p.Step * 180 / Math.PI:0.###}° apart";
            case PatternKind.Line: return $"{what} in a line, {p.V1.Length:0.###} mm apart along {Axis(p.V1.Normalized())}";
            default:
                if (RowsOf(p) is not { } r) return $"{what} in a grid, steps {p.V1.Length:0.###} and {p.V2.Length:0.###} mm";
                int rows = r.Cells.Select(c => c.B).Append(0).Distinct().Count();
                return $"{what} in {(r.Staggered ? "staggered " : "")}rows: {r.Step:0.###} mm apart along {Axis(r.Along)}, {rows} rows {r.RowSpacing:0.###} mm apart along {Axis(r.Across)}"
                    + (r.Staggered ? $", every other row shifted {r.Shift:0.###} mm" : "");
        }
    }

    /// <summary>The line, grid or circle of equal features that <paramref name="feat"/> belongs to (Kind None when it stands alone).</summary>
    public static PatternInfo PatternOf(SceneObject body, Feat feat)
    {
        var none = new PatternInfo(PatternKind.None, [], [], default, default, default, default, 0);
        var sib = Features(body).Where(f => f != feat && f.Key == feat.Key).ToList();
        if (sib.Count == 0) return none;
        var centres = sib.Select(f => f.Centre).ToList(); var c0 = feat.Centre;
        var lat = LatticeMembers(c0, centres); var circ = CircleMembers(c0, feat.Dir, centres);
        if (lat.Count == 0 && circ.Count == 0) return none;
        if (circ.Count > lat.Count || (circ.Count == lat.Count && circ.Count >= 4))   // a hexagon of holes fits a lattice too: from five on, call it a ring
        {
            var n = feat.Dir.Normalized(); var idx = circ.OrderBy(i => i).ToList();
            Vec3 Flat(Vec3 v) => v - n * Vec3.Dot(v, n);
            Vec3 p = Flat(centres[idx[0]] - c0), q = Flat(centres[idx[1]] - c0); var cr = Vec3.Cross(p, q);
            var centre = (Vec3.Cross(cr, p) * Vec3.Dot(q, q) + Vec3.Cross(q, cr) * Vec3.Dot(p, p)) * (1 / (2 * Vec3.Dot(cr, cr)));
            var ux = (-centre).Normalized(); var uy = Vec3.Cross(n, ux);
            var angs = idx.Select(i => { var rel = Flat(centres[i] - c0) - centre; double a = Math.Atan2(Vec3.Dot(rel, uy), Vec3.Dot(rel, ux)); return a < 0 ? a + 2 * Math.PI : a; }).ToList();
            double step = angs.Min();
            return new PatternInfo(PatternKind.Circle, idx.Select(i => sib[i]).ToList(), angs.Select(a => ((int)Math.Round(a / step), 0)).ToList(), default, default, c0 + centre, n, step);
        }
        var d = centres.Select(c => c - c0).ToList();
        var order = Enumerable.Range(0, d.Count).OrderBy(i => d[i].Length).ToList();
        var v1 = d[order[0]]; double l1 = v1.Length; double tol = 1e-3 * l1 + 1e-4;
        var members = lat.OrderBy(i => i).ToList();
        bool grid = members.Any(i => (d[i] - v1 * Math.Round(Vec3.Dot(d[i], v1) / (l1 * l1))).Length >= tol);
        if (!grid)
            return new PatternInfo(PatternKind.Line, members.Select(i => sib[i]).ToList(), members.Select(i => ((int)Math.Round(Vec3.Dot(d[i], v1) / (l1 * l1)), 0)).ToList(), v1, default, default, default, 0);
        Vec3 v2 = default; double best = double.MaxValue;
        foreach (int i in order)
        {
            double len = d[i].Length; if (len < 1e-6 || Vec3.Cross(v1, d[i]).Length < 1e-3 * l1 * len) continue;
            if (len < best) { best = len; v2 = d[i]; }
        }
        double a11 = Vec3.Dot(v1, v1), a12 = Vec3.Dot(v1, v2), a22 = Vec3.Dot(v2, v2), det = a11 * a22 - a12 * a12;
        var cells = members.Select(i =>
        {
            double r1 = Vec3.Dot(d[i], v1), r2 = Vec3.Dot(d[i], v2);
            return ((int)Math.Round((r1 * a22 - r2 * a12) / det), (int)Math.Round((a11 * r2 - a12 * r1) / det));
        }).ToList();
        return new PatternInfo(PatternKind.Grid, members.Select(i => sib[i]).ToList(), cells, v1, v2, default, default, 0);
    }
}

/// <summary>Metric hole sizes (ISO 273 clearance, coarse-thread tap drills, nominal), for naming what a recognised hole is for.</summary>
public static class StandardHoles
{
    static readonly (string M, double Nominal, double Fine, double Medium, double Coarse, double Tap)[] Table =
    [
        ("M2", 2, 2.2, 2.4, 2.6, 1.6), ("M2.5", 2.5, 2.7, 2.9, 3.1, 2.05), ("M3", 3, 3.2, 3.4, 3.6, 2.5), ("M4", 4, 4.3, 4.5, 4.8, 3.3),
        ("M5", 5, 5.3, 5.5, 5.8, 4.2), ("M6", 6, 6.4, 6.6, 7, 5), ("M8", 8, 8.4, 9, 10, 6.8), ("M10", 10, 10.5, 11, 12, 8.5),
        ("M12", 12, 13, 13.5, 14.5, 10.2), ("M14", 14, 15, 15.5, 16.5, 12), ("M16", 16, 17, 17.5, 18.5, 14), ("M20", 20, 21, 22, 24, 17.5),
        ("M24", 24, 25, 26, 28, 21),
    ];

    /// <summary>"M6 clearance (medium)", "M6 tap drill", "M6 nominal" and so on, or null when the diameter is none of them (within 0.03 mm).</summary>
    public static string? Match(double diameter)
    {
        const double tol = 0.03;
        // a hole of 5 mm is far more often an M6 tap drill than an M5 pin hole, so the drilling sizes are tried before the nominal ones
        foreach (var t in Table)
        {
            if (Math.Abs(diameter - t.Tap) < tol) return $"{t.M} tap drill";
            if (Math.Abs(diameter - t.Medium) < tol) return $"{t.M} clearance (medium)";
            if (Math.Abs(diameter - t.Fine) < tol) return $"{t.M} clearance (close)";
            if (Math.Abs(diameter - t.Coarse) < tol) return $"{t.M} clearance (loose)";
        }
        foreach (var t in Table) if (Math.Abs(diameter - t.Nominal) < tol) return $"{t.M} nominal";
        return null;
    }

    /// <summary>The diameters for a size such as "M6": clearance (medium), tap drill and nominal; null for an unknown size.</summary>
    public static (double Clearance, double TapDrill, double Nominal)? Of(string size)
    {
        foreach (var t in Table) if (t.M.Equals(size, StringComparison.OrdinalIgnoreCase)) return (t.Medium, t.Tap, t.Nominal);
        return null;
    }
}
