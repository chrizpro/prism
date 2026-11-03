using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Prism.Kernel;

namespace Prism.Core;

public enum FeatureKind { Face, Hole, Boss, Slot, Pocket, Fillet, Counterbore, Countersink }

/// <summary>How a click grows into a selection (the "intent" of a pick in the direct-editing tools).</summary>
public enum PickIntent { Feature, Face, Tangent, SameSize, Pattern }

/// <summary>A complete hole or boss: a point on its axis, the radius, whether it is a hole, and its extent along the axis.</summary>
public sealed record Round(Vec3 Origin, double Radius, bool Hole, double T0, double T1);

/// <summary>A recognised feature: its faces, what it is, a size key (equal keys mean "same feature again") and where it sits.</summary>
public sealed record Feat(int Face, FeatureKind Kind, string Key, Vec3 Centre, Vec3 Dir, IReadOnlyList<int> Faces, Round? Round)
{
    public string Name => Kind switch { FeatureKind.Hole => "Hole", FeatureKind.Boss => "Boss", FeatureKind.Slot => "Slot", FeatureKind.Pocket => "Pocket", FeatureKind.Fillet => "Fillet / round", FeatureKind.Counterbore => "Counterbore", FeatureKind.Countersink => "Countersink", _ => "Face" };
}

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
        for (int f = 0; f < n && f + 1 < m.FaceTriStart.Length; f++)
        {
            Vec3 sum = new(0, 0, 0); double w = 0; var seen = new HashSet<int>(); bool first = true;
            for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
            {
                Vec3 A = P(m, m.Indices[3 * t]), B = P(m, m.Indices[3 * t + 1]), C = P(m, m.Indices[3 * t + 2]);
                double area = Vec3.Cross(B - A, C - A).Length * 0.5; sum += (A + B + C) * (area / 3); w += area;
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
            if (a.Info[f].Type == "Plane") a.Normal[f] = a.Info[f].Normal.Normalized();
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
                    pairs[(fa, fb)] = pairs.TryGetValue((fa, fb), out var e) ? (e.count + 1, Math.Min(e.minDot, dot), e.mid + P(m, va), e.nA, e.nB, e.tA, e.tB) : (1, dot, P(m, va), na, nb, triOf[va], triOf[vb]);
                }
        }
        Vec3 TriCentre(int t) => (P(m, m.Indices[3 * t]) + P(m, m.Indices[3 * t + 1]) + P(m, m.Indices[3 * t + 2])) * (1.0 / 3);
        foreach (var (key, e) in pairs)
        {
            if (e.count < 2) continue;
            var (fa, fb) = key; var mid = e.mid * (1.0 / e.count);
            Edge cls;
            if (e.minDot > 0.9945) cls = Edge.Tangent;
            else
            {
                // convex: the next bit of each face lies behind the other's tangent plane; concave: in front of it (judged right at the edge, so big faces do not matter)
                double s = Vec3.Dot(TriCentre(e.tB) - mid, e.nA) + Vec3.Dot(TriCentre(e.tA) - mid, e.nB);
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
                double area = loop.faces.Sum(g => a.Info[g].Area);
                Add(loop.kind, loop.faces, loop.dir, $"{loop.kind}|{loop.faces.Count}|{area:0.##}");
                continue;
            }
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
    }

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
