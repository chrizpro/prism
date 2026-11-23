using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>
/// Outlines traced from a mesh for Section Sketch (Mesh guide, MSH-P06): a plane section that knows where the plane only grazes
/// a face, the border of a selected region, sharp creases, and the silhouette seen along a direction.
/// </summary>
public static class MeshOutline
{
    /// <summary>A traced outline. Steepness is the mean sine of the angle between the cut faces and the plane (1 = the plane
    /// crosses walls at right angles, 0 = it slides along a face); only sections have it, other sources report 1.</summary>
    public sealed record Trace(List<Vec3> Points, bool Closed, double Steepness = 1);

    /// <summary>Below this steepness a section loop runs along a face rather than across walls, so it is scan noise.</summary>
    public const double GrazingSteepness = 0.5;

    // ------------------------------------------------------------ section

    /// <summary>The section at a plane, each loop with the steepness of the faces it crosses.</summary>
    public static List<Trace> Section(TriMesh m, Vec3 origin, Vec3 normal)
    {
        var n = normal.Normalized();
        double d0 = Vec3.Dot(n, origin);
        var dist = new double[m.VertexCount];
        for (int i = 0; i < m.VertexCount; i++) { double d = Vec3.Dot(n, m.P(i)) - d0; dist[i] = Math.Abs(d) < 1e-9 ? 1e-9 : d; }
        var cutIds = new Dictionary<long, int>(); var cutPts = new List<Vec3>();
        int Cut(int a, int b)
        {
            long key = TriMesh.EdgeKey(a, b, m.VertexCount);
            if (cutIds.TryGetValue(key, out int id)) return id;
            double t = dist[a] / (dist[a] - dist[b]);
            cutPts.Add(m.P(a) + (m.P(b) - m.P(a)) * t);
            return cutIds[key] = cutPts.Count - 1;
        }
        var segs = new List<(int a, int b, double steep, double len)>();
        for (int t = 0; t < m.TriCount; t++)
        {
            var (a, b, c) = m.Tri(t);
            bool pa = dist[a] > 0, pb = dist[b] > 0, pc = dist[c] > 0;
            if (pa == pb && pb == pc) continue;
            if (pa == pb) (a, b, c, pa) = (c, a, b, pc);
            else if (pa == pc) (a, b, c, pa) = (b, c, a, pb);
            int ab = Cut(a, b), ca = Cut(c, a);
            double cos = Math.Abs(Vec3.Dot(m.FaceNormal(t), n));
            double steep = Math.Sqrt(Math.Max(0, 1 - cos * cos));
            segs.Add(pa ? (ca, ab, steep, 0) : (ab, ca, steep, 0));
        }
        for (int i = 0; i < segs.Count; i++) segs[i] = segs[i] with { len = cutPts[segs[i].a].DistanceTo(cutPts[segs[i].b]) };
        var res = new List<Trace>();
        foreach (var (chain, closed) in Chain(segs.Select(s => (s.a, s.b)).ToList()))
        {
            double w = 0, sum = 0;
            foreach (int s in chain) { w += segs[s].len; sum += segs[s].len * segs[s].steep; }
            var pts = chain.Select(s => cutPts[segs[s].a]).ToList();
            if (!closed) pts.Add(cutPts[segs[chain[^1]].b]);
            res.Add(new Trace(pts, closed, w > 0 ? sum / w : 1));
        }
        return res;
    }

    /// <summary>Chains directed segments end to start; each chain lists segment indices.</summary>
    static List<(List<int> segs, bool closed)> Chain(List<(int a, int b)> segs)
    {
        var from = new Dictionary<int, List<int>>(); var into = new HashSet<int>();
        for (int i = 0; i < segs.Count; i++) { if (!from.TryGetValue(segs[i].a, out var l)) from[segs[i].a] = l = []; l.Add(i); into.Add(segs[i].b); }
        var used = new bool[segs.Count]; var res = new List<(List<int>, bool)>();
        // open chains first, from points nothing leads into, so they are not cut in the middle
        var starts = Enumerable.Range(0, segs.Count).Where(i => !into.Contains(segs[i].a)).Concat(Enumerable.Range(0, segs.Count));
        foreach (int s in starts)
        {
            if (used[s]) continue;
            var ids = new List<int>(); int cur = s; bool closed = false;
            while (true)
            {
                used[cur] = true; ids.Add(cur);
                int b = segs[cur].b;
                if (b == segs[s].a) { closed = true; break; }
                if (!from.TryGetValue(b, out var cand)) break;
                int nx = cand.FirstOrDefault(c => !used[c], -1);
                if (nx < 0) break;
                cur = nx;
            }
            res.Add((ids, closed));
        }
        return res;
    }

    // ------------------------------------------------------------ region border

    /// <summary>The border of a set of triangles: every edge with a selected triangle on one side only, chained into loops.</summary>
    public static List<Trace> RegionBorder(TriMesh m, IReadOnlyCollection<int> tris)
    {
        var sel = tris as ISet<int> ?? tris.ToHashSet();
        var nb = m.Topology().Neighbors;
        var segs = new List<(int a, int b)>();
        foreach (int t in sel)
            for (int k = 0; k < 3; k++)
            {
                int o = nb[3 * t + k];
                if (o >= 0 && sel.Contains(o)) continue;
                segs.Add((m.T[3 * t + k], m.T[3 * t + (k + 1) % 3]));
            }
        return Chain(segs).Select(c =>
        {
            var pts = c.segs.Select(s => m.P(segs[s].a)).ToList();
            if (!c.closed) pts.Add(m.P(segs[c.segs[^1]].b));
            return new Trace(pts, c.closed);
        }).ToList();
    }

    // ------------------------------------------------------------ sharp creases

    /// <summary>
    /// Edges where the surface folds by more than the angle, chained into curves. Chains shorter than minLength are dropped
    /// (noise and short facets), so a scan's sharp edges come out as a few long curves.
    /// </summary>
    public static List<Trace> Creases(TriMesh m, double angleDeg, double minLength)
    {
        var nb = m.Topology().Neighbors; double cosLimit = Math.Cos(angleDeg * Math.PI / 180);
        var edges = new List<(int a, int b)>(); var seen = new HashSet<long>();
        for (int t = 0; t < m.TriCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int o = nb[3 * t + k];
                if (o < 0) continue;
                int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3];
                if (!seen.Add(TriMesh.EdgeKey(a, b, m.VertexCount))) continue;
                if (Vec3.Dot(m.FaceNormal(t), m.FaceNormal(o)) < cosLimit) edges.Add((a, b));
            }
        // undirected chaining: walk through vertices that join exactly two crease edges
        var at = new Dictionary<int, List<int>>();
        for (int i = 0; i < edges.Count; i++) { Add(edges[i].a, i); Add(edges[i].b, i); }
        void Add(int v, int e) { if (!at.TryGetValue(v, out var l)) at[v] = l = []; l.Add(e); }
        var used = new bool[edges.Count]; var res = new List<Trace>();
        IEnumerable<int> Starts() => Enumerable.Range(0, edges.Count).Where(i => at[edges[i].a].Count != 2 || at[edges[i].b].Count != 2).Concat(Enumerable.Range(0, edges.Count));
        foreach (int s in Starts())
        {
            if (used[s]) continue;
            int v0 = at[edges[s].a].Count != 2 ? edges[s].a : edges[s].b;
            var verts = new List<int> { v0 }; int cur = s, v = v0; bool closed = false;
            while (true)
            {
                used[cur] = true;
                v = edges[cur].a == v ? edges[cur].b : edges[cur].a;
                if (v == v0) { closed = true; break; }
                verts.Add(v);
                if (at[v].Count != 2) break;
                int nx = at[v].FirstOrDefault(e => !used[e], -1);
                if (nx < 0) break;
                cur = nx;
            }
            var pts = verts.Select(m.P).ToList();
            double len = 0; for (int i = 1; i < pts.Count; i++) len += pts[i - 1].DistanceTo(pts[i]);
            if (closed) len += pts[^1].DistanceTo(pts[0]);
            if (len >= minLength && pts.Count >= 2) res.Add(new Trace(pts, closed));
        }
        return res;
    }

    // ------------------------------------------------------------ silhouette

    /// <summary>
    /// The outline of the mesh's shadow on a plane, looking along its normal: outer border and holes, as closed loops in the
    /// plane's (u, v) coordinates. The shadow is drawn into a grid of square cells of the given size and traced along the cell
    /// borders, so the outline is accurate to about half a cell; the fit smooths the steps.
    /// </summary>
    public static List<List<(double u, double v)>> Silhouette(TriMesh m, Plane3 plane, double cell)
    {
        var uv = new (double u, double v)[m.VertexCount];
        double umin = double.MaxValue, vmin = double.MaxValue, umax = double.MinValue, vmax = double.MinValue;
        for (int i = 0; i < m.VertexCount; i++)
        {
            var q = plane.Project(m.P(i)); uv[i] = (q.u, q.v);
            umin = Math.Min(umin, q.u); vmin = Math.Min(vmin, q.v); umax = Math.Max(umax, q.u); vmax = Math.Max(vmax, q.v);
        }
        if (m.TriCount == 0) return [];
        umin -= 2 * cell; vmin -= 2 * cell;
        int w = (int)Math.Ceiling((umax - umin) / cell) + 3, h = (int)Math.Ceiling((vmax - vmin) / cell) + 3;
        var cov = new bool[w * h];
        // a cell is covered when its centre lies inside any projected triangle
        for (int t = 0; t < m.TriCount; t++)
        {
            var (ia, ib, ic) = m.Tri(t); var a = uv[ia]; var b = uv[ib]; var c = uv[ic];
            double area = (b.u - a.u) * (c.v - a.v) - (b.v - a.v) * (c.u - a.u);
            if (Math.Abs(area) < 1e-18) continue;
            int x0 = Math.Max(0, (int)Math.Floor((Math.Min(a.u, Math.Min(b.u, c.u)) - umin) / cell - 0.5));
            int x1 = Math.Min(w - 1, (int)Math.Ceiling((Math.Max(a.u, Math.Max(b.u, c.u)) - umin) / cell - 0.5));
            int y0 = Math.Max(0, (int)Math.Floor((Math.Min(a.v, Math.Min(b.v, c.v)) - vmin) / cell - 0.5));
            int y1 = Math.Min(h - 1, (int)Math.Ceiling((Math.Max(a.v, Math.Max(b.v, c.v)) - vmin) / cell - 0.5));
            for (int y = y0; y <= y1; y++)
            {
                double py = vmin + (y + 0.5) * cell;
                for (int x = x0; x <= x1; x++)
                {
                    if (cov[y * w + x]) continue;
                    double px = umin + (x + 0.5) * cell;
                    double e0 = (b.u - a.u) * (py - a.v) - (b.v - a.v) * (px - a.u);
                    double e1 = (c.u - b.u) * (py - b.v) - (c.v - b.v) * (px - b.u);
                    double e2 = (a.u - c.u) * (py - c.v) - (a.v - c.v) * (px - c.u);
                    if (area > 0 ? e0 >= 0 && e1 >= 0 && e2 >= 0 : e0 <= 0 && e1 <= 0 && e2 <= 0) cov[y * w + x] = true;
                }
            }
        }
        bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && cov[y * w + x];
        // directed border edges between covered and empty cells, covered on the left; corners are grid nodes
        var segs = new List<(int a, int b)>();
        int Node(int x, int y) => y * (w + 1) + x;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!In(x, y)) continue;
                if (!In(x, y - 1)) segs.Add((Node(x, y), Node(x + 1, y)));
                if (!In(x + 1, y)) segs.Add((Node(x + 1, y), Node(x + 1, y + 1)));
                if (!In(x, y + 1)) segs.Add((Node(x + 1, y + 1), Node(x, y + 1)));
                if (!In(x - 1, y)) segs.Add((Node(x, y + 1), Node(x, y)));
            }
        var res = new List<List<(double u, double v)>>();
        foreach (var (chain, closed) in Chain(segs))
        {
            if (!closed || chain.Count < 8) continue;
            // the midpoints of the cell-border steps follow the true outline more closely than the step corners
            var loop = new List<(double u, double v)>();
            foreach (int s in chain)
            {
                int na = segs[s].a, nb2 = segs[s].b;
                double ux = (na % (w + 1) + nb2 % (w + 1)) / 2.0, vy = (na / (w + 1) + nb2 / (w + 1)) / 2.0;
                loop.Add((umin + ux * cell, vmin + vy * cell));
            }
            res.Add(Smooth(loop));
        }
        return res;
    }

    /// <summary>The grid steps are regular, not noise: a moving average over nine steps removes them, then every fourth point is kept.</summary>
    static List<(double u, double v)> Smooth(List<(double u, double v)> loop)
    {
        int n = loop.Count, half = Math.Min(4, (n - 1) / 2);
        var res = new List<(double u, double v)>();
        for (int i = 0; i < n; i += 4)
        {
            double u = 0, v = 0;
            for (int k = -half; k <= half; k++) { var q = loop[((i + k) % n + n) % n]; u += q.u; v += q.v; }
            res.Add((u / (2 * half + 1), v / (2 * half + 1)));
        }
        return res;
    }

    /// <summary>Grid cell for <see cref="Silhouette"/>: fine enough for the tolerance, capped so the grid stays a few million cells.</summary>
    public static double SilhouetteCell(TriMesh m, Plane3 plane, double tol)
    {
        double extent = 0;
        foreach (var corner in Corners(m.Min, m.Max))
        {
            var q = plane.Project(corner);
            extent = Math.Max(extent, Math.Max(Math.Abs(q.u - plane.Project(m.Center).u), Math.Abs(q.v - plane.Project(m.Center).v)) * 2);
        }
        return Math.Max(tol / 2, extent / 2500);
    }

    static IEnumerable<Vec3> Corners(Vec3 a, Vec3 b)
    {
        for (int i = 0; i < 8; i++) yield return new Vec3((i & 1) == 0 ? a.X : b.X, (i & 2) == 0 ? a.Y : b.Y, (i & 4) == 0 ? a.Z : b.Z);
    }
}
