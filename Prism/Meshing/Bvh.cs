using System;
using System.Collections.Generic;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>Bounding-volume hierarchy over a triangle mesh: ray casts and closest-point queries in O(log n).</summary>
public sealed class Bvh
{
    readonly TriMesh _m;
    // node arrays: bounds (6 floats), left/right child or (first,count) for leaves
    float[] _bb = new float[0];
    int[] _l = [], _r = [], _first = [], _count = [];
    int[] _tris;
    int _nodes;
    const int LeafSize = 6;

    public Bvh(TriMesh m)
    {
        _m = m;
        int n = m.TriCount;
        _tris = new int[n];
        for (int i = 0; i < n; i++) _tris[i] = i;
        int cap = Math.Max(1, 2 * n / (LeafSize / 2) + 8);
        _bb = new float[cap * 6]; _l = new int[cap]; _r = new int[cap]; _first = new int[cap]; _count = new int[cap];
        // centroids for splitting
        var cen = new float[n * 3];
        for (int t = 0; t < n; t++)
            for (int k = 0; k < 3; k++)
                cen[3 * t + k] = (m.V[3 * m.T[3 * t] + k] + m.V[3 * m.T[3 * t + 1] + k] + m.V[3 * m.T[3 * t + 2] + k]) / 3f;
        if (n > 0) Build(0, n, cen);
    }

    int Build(int lo, int hi, float[] cen)
    {
        int id = _nodes++;
        if (id >= _l.Length) Grow();
        float x0 = float.MaxValue, y0 = x0, z0 = x0, x1 = float.MinValue, y1 = x1, z1 = x1;
        float cx0 = x0, cy0 = x0, cz0 = x0, cx1 = x1, cy1 = x1, cz1 = x1;
        for (int i = lo; i < hi; i++)
        {
            int t = _tris[i];
            for (int k = 0; k < 3; k++)
            {
                int v = _m.T[3 * t + k];
                float x = _m.V[3 * v], y = _m.V[3 * v + 1], z = _m.V[3 * v + 2];
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z);
            }
            cx0 = Math.Min(cx0, cen[3 * t]); cx1 = Math.Max(cx1, cen[3 * t]); cy0 = Math.Min(cy0, cen[3 * t + 1]); cy1 = Math.Max(cy1, cen[3 * t + 1]); cz0 = Math.Min(cz0, cen[3 * t + 2]); cz1 = Math.Max(cz1, cen[3 * t + 2]);
        }
        _bb[6 * id] = x0; _bb[6 * id + 1] = y0; _bb[6 * id + 2] = z0; _bb[6 * id + 3] = x1; _bb[6 * id + 4] = y1; _bb[6 * id + 5] = z1;
        if (hi - lo <= LeafSize) { _first[id] = lo; _count[id] = hi - lo; _l[id] = -1; return id; }
        float ex = cx1 - cx0, ey = cy1 - cy0, ez = cz1 - cz0;
        int axis = ex >= ey && ex >= ez ? 0 : ey >= ez ? 1 : 2;
        // median split by nth_element-style partial sort
        var span = new ArraySegment<int>(_tris, lo, hi - lo);
        int mid = (lo + hi) / 2;
        Select(lo, hi - 1, mid, axis, cen);
        _count[id] = 0;
        int l = Build(lo, mid, cen); int r = Build(mid, hi, cen);
        _l[id] = l; _r[id] = r;
        return id;
    }

    void Grow()
    {
        int n = _l.Length * 2;
        Array.Resize(ref _bb, n * 6); Array.Resize(ref _l, n); Array.Resize(ref _r, n); Array.Resize(ref _first, n); Array.Resize(ref _count, n);
    }

    void Select(int lo, int hi, int k, int axis, float[] cen)
    {
        while (lo < hi)
        {
            float pivot = cen[3 * _tris[(lo + hi) / 2] + axis];
            int i = lo, j = hi;
            while (i <= j)
            {
                while (cen[3 * _tris[i] + axis] < pivot) i++;
                while (cen[3 * _tris[j] + axis] > pivot) j--;
                if (i <= j) { (_tris[i], _tris[j]) = (_tris[j], _tris[i]); i++; j--; }
            }
            if (k <= j) hi = j; else if (k >= i) lo = i; else return;
        }
    }

    /// <summary>Triangles whose bounding box overlaps the given box (candidates for exact tests).</summary>
    public void QueryBox(Vec3 min, Vec3 max, List<int> result)
    {
        if (_nodes == 0) return;
        var stack = new Stack<int>(); stack.Push(0);
        while (stack.Count > 0)
        {
            int id = stack.Pop();
            if (_bb[6 * id] > max.X || _bb[6 * id + 3] < min.X || _bb[6 * id + 1] > max.Y || _bb[6 * id + 4] < min.Y || _bb[6 * id + 2] > max.Z || _bb[6 * id + 5] < min.Z) continue;
            if (_l[id] < 0)
            {
                for (int i = _first[id]; i < _first[id] + _count[id]; i++)
                {
                    int t = _tris[i]; float x0 = float.MaxValue, y0 = x0, z0 = x0, x1 = float.MinValue, y1 = x1, z1 = x1;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = _m.T[3 * t + k]; float x = _m.V[3 * v], y = _m.V[3 * v + 1], z = _m.V[3 * v + 2];
                        x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z);
                    }
                    if (x0 <= max.X && x1 >= min.X && y0 <= max.Y && y1 >= min.Y && z0 <= max.Z && z1 >= min.Z) result.Add(t);
                }
            }
            else { stack.Push(_l[id]); stack.Push(_r[id]); }
        }
    }

    // ---------------------------------------------------------------- ray casting
    /// <summary>Nearest hit along a ray (t &gt; tMin). Returns triangle index and ray parameter; no culling.</summary>
    public (int tri, double t)? Raycast(Vec3 o, Vec3 d, double tMax = double.MaxValue, double tMin = 1e-9)
    {
        if (_m.TriCount == 0) return null;
        double inx = 1 / (Math.Abs(d.X) < 1e-30 ? 1e-30 : d.X), iny = 1 / (Math.Abs(d.Y) < 1e-30 ? 1e-30 : d.Y), inz = 1 / (Math.Abs(d.Z) < 1e-30 ? 1e-30 : d.Z);
        int best = -1; double bestT = tMax;
        var stack = new Stack<int>(); stack.Push(0);
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            if (!RayBox(n, o, inx, iny, inz, bestT)) continue;
            if (_l[n] < 0)
            {
                for (int i = _first[n]; i < _first[n] + _count[n]; i++)
                {
                    int t = _tris[i];
                    if (RayTri(t, o, d) is double h && h > tMin && h < bestT) { bestT = h; best = t; }
                }
            }
            else { stack.Push(_l[n]); stack.Push(_r[n]); }
        }
        return best < 0 ? null : (best, bestT);
    }

    bool RayBox(int n, Vec3 o, double inx, double iny, double inz, double tMax)
    {
        double t0 = 0, t1 = tMax;
        double a = (_bb[6 * n] - o.X) * inx, b = (_bb[6 * n + 3] - o.X) * inx;
        t0 = Math.Max(t0, Math.Min(a, b)); t1 = Math.Min(t1, Math.Max(a, b));
        a = (_bb[6 * n + 1] - o.Y) * iny; b = (_bb[6 * n + 4] - o.Y) * iny;
        t0 = Math.Max(t0, Math.Min(a, b)); t1 = Math.Min(t1, Math.Max(a, b));
        a = (_bb[6 * n + 2] - o.Z) * inz; b = (_bb[6 * n + 5] - o.Z) * inz;
        t0 = Math.Max(t0, Math.Min(a, b)); t1 = Math.Min(t1, Math.Max(a, b));
        return t0 <= t1;
    }

    double? RayTri(int t, Vec3 o, Vec3 d)
    {
        var a = _m.P(_m.T[3 * t]); var b = _m.P(_m.T[3 * t + 1]); var c = _m.P(_m.T[3 * t + 2]);
        var e1 = b - a; var e2 = c - a; var p = Vec3.Cross(d, e2);
        double det = Vec3.Dot(e1, p);
        if (Math.Abs(det) < 1e-18) return null;
        double inv = 1 / det; var s = o - a;
        double u = Vec3.Dot(s, p) * inv; if (u < 0 || u > 1) return null;
        var q = Vec3.Cross(s, e1);
        double v = Vec3.Dot(d, q) * inv; if (v < 0 || u + v > 1) return null;
        return Vec3.Dot(e2, q) * inv;
    }

    // ---------------------------------------------------------------- closest point
    public readonly record struct Closest(int Tri, Vec3 Point, double Dist);

    /// <summary>Closest point on the mesh surface to p (within maxDist).</summary>
    public Closest? ClosestPoint(Vec3 p, double maxDist = double.MaxValue)
    {
        if (_m.TriCount == 0) return null;
        int best = -1; double bestD2 = maxDist == double.MaxValue ? double.MaxValue : maxDist * maxDist; Vec3 bestP = Vec3.Zero;
        var stack = new Stack<(int node, double d2)>(); stack.Push((0, BoxDist2(0, p)));
        while (stack.Count > 0)
        {
            var (n, bd) = stack.Pop();
            if (bd >= bestD2) continue;
            if (_l[n] < 0)
            {
                for (int i = _first[n]; i < _first[n] + _count[n]; i++)
                {
                    int t = _tris[i];
                    var q = ClosestOnTri(t, p); double d2 = (q - p).Length; d2 *= d2;
                    if (d2 < bestD2) { bestD2 = d2; best = t; bestP = q; }
                }
            }
            else
            {
                double dl = BoxDist2(_l[n], p), dr = BoxDist2(_r[n], p);
                if (dl < dr) { stack.Push((_r[n], dr)); stack.Push((_l[n], dl)); } else { stack.Push((_l[n], dl)); stack.Push((_r[n], dr)); }
            }
        }
        return best < 0 ? null : new Closest(best, bestP, Math.Sqrt(bestD2));
    }

    double BoxDist2(int n, Vec3 p)
    {
        double dx = Math.Max(Math.Max(_bb[6 * n] - p.X, 0), p.X - _bb[6 * n + 3]);
        double dy = Math.Max(Math.Max(_bb[6 * n + 1] - p.Y, 0), p.Y - _bb[6 * n + 4]);
        double dz = Math.Max(Math.Max(_bb[6 * n + 2] - p.Z, 0), p.Z - _bb[6 * n + 5]);
        return dx * dx + dy * dy + dz * dz;
    }

    Vec3 ClosestOnTri(int t, Vec3 p)
    {
        var a = _m.P(_m.T[3 * t]); var b = _m.P(_m.T[3 * t + 1]); var c = _m.P(_m.T[3 * t + 2]);
        return ClosestOnTriangle(p, a, b, c);
    }

    /// <summary>Ericson's closest point on triangle.</summary>
    public static Vec3 ClosestOnTriangle(Vec3 p, Vec3 a, Vec3 b, Vec3 c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        double d1 = Vec3.Dot(ab, ap), d2 = Vec3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        var bp = p - b; double d3 = Vec3.Dot(ab, bp), d4 = Vec3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        var cp = p - c; double d5 = Vec3.Dot(ab, cp), d6 = Vec3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        double den = 1 / (va + vb + vc);
        return a + ab * (vb * den) + ac * (vc * den);
    }
}
