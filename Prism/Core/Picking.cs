using System;
using System.Collections.Generic;
using System.Numerics;
using Prism.Kernel;

namespace Prism.Core;

public readonly record struct PickResult(SelItem Item, Vec3 Point, Vec3 Normal, double Depth);

/// <summary>CPU picking against the cached tessellation: ray/triangle for faces, screen distance for edges.</summary>
public static class Picking
{
    public const double EdgeTolerancePx = 7;

    public static (SceneObject obj, int face, Vec3 point, Vec3 normal, double t)? RayFace(
        Camera cam, IEnumerable<SceneObject> objects, double px, double py, Func<SceneObject, bool>? include = null, Func<SceneObject, IReadOnlySet<int>?>? shownFaces = null)
    {
        var (o, d) = cam.Ray(px, py);
        (SceneObject, int, Vec3, Vec3, double)? best = null;
        foreach (var obj in objects)
        {
            if (!obj.Visible || obj.Kind is ObjKind.Curve or ObjKind.Sketch || obj.IsAnnotation) continue;
            if (include != null && !include(obj)) continue;
            var m = obj.Mesh;
            if (!RayBox(o, d, m.Min, m.Max, best?.Item5 ?? double.MaxValue)) continue;
            if (obj.Tri is { } tri)
            {
                if (tri.Bvh.Raycast(o, d, best?.Item5 ?? double.MaxValue, 1e-6) is { } hit0)
                    best = (obj, 0, o + d * hit0.t, tri.FaceNormal(hit0.tri), hit0.t);
                continue;
            }
            var P = m.Positions; var I = m.Indices; var only = shownFaces?.Invoke(obj);
            for (int t = 0; t < I.Length; t += 3)
            {
                if (only != null && !only.Contains(m.TriFace[t / 3])) continue;      // a face hidden by Isolate cannot be clicked: the pick goes through to what is behind
                int a = I[t] * 3, b = I[t + 1] * 3, c = I[t + 2] * 3;
                double? hit = RayTri(o, d,
                    P[a], P[a + 1], P[a + 2], P[b], P[b + 1], P[b + 2], P[c], P[c + 1], P[c + 2]);
                if (hit is double h && h > 0 && h < (best?.Item5 ?? double.MaxValue))
                {
                    var N = m.Normals;
                    var n = new Vec3(N[a] + N[b] + N[c], N[a + 1] + N[b + 1] + N[c + 1], N[a + 2] + N[b + 2] + N[c + 2]).Normalized();
                    best = (obj, m.TriFace[t / 3], o + d * h, n, h);
                }
            }
        }
        return best;
    }

    public static (Vec3 point, Vec3 normal, double t, int tri)? RayMesh(Camera cam, MeshData m, double px, double py)
    {
        var (o, d) = cam.Ray(px, py);
        var P = m.Positions; var I = m.Indices; var N = m.Normals;
        double bestT = double.MaxValue; int bestTri = -1;
        for (int t = 0; t < I.Length; t += 3)
        {
            int a = I[t] * 3, b = I[t + 1] * 3, c = I[t + 2] * 3;
            if (RayTri(o, d, P[a], P[a + 1], P[a + 2], P[b], P[b + 1], P[b + 2], P[c], P[c + 1], P[c + 2]) is double h && h > 0 && h < bestT) { bestT = h; bestTri = t; }
        }
        if (bestTri < 0) return null;
        int i0 = I[bestTri] * 3, i1 = I[bestTri + 1] * 3, i2 = I[bestTri + 2] * 3;
        var n = new Vec3(N[i0] + N[i1] + N[i2], N[i0 + 1] + N[i1 + 1] + N[i2 + 1], N[i0 + 2] + N[i1 + 2] + N[i2 + 2]).Normalized();
        return (o + d * bestT, n, bestT, bestTri / 3);
    }

    /// <summary>Nearest edge segment (in pixels) among visible objects. For curve/sketch objects every edge counts.</summary>
    public static (SceneObject obj, int edge, Vec3 point, double distPx, double depth)? NearestEdge(
        Camera cam, IEnumerable<SceneObject> objects, double px, double py, double maxPx, Func<SceneObject, bool>? include = null, Func<SceneObject, IReadOnlySet<int>?>? shownEdges = null)
    {
        var vp = cam.ViewProjection;
        (SceneObject, int, Vec3, double, double)? best = null;
        double bestD = maxPx;
        foreach (var obj in objects)
        {
            if (!obj.Visible) continue;
            if (include != null && !include(obj)) continue;
            var m = obj.Mesh; var E = m.EdgePoints; var only = shownEdges?.Invoke(obj);
            for (int s = 0; s < m.SegEdge.Length; s++)
            {
                if (only != null && !only.Contains(m.SegEdge[s])) continue;
                var a = cam.Project(new(E[6 * s], E[6 * s + 1], E[6 * s + 2]), vp);
                var b = cam.Project(new(E[6 * s + 3], E[6 * s + 4], E[6 * s + 5]), vp);
                if (a == null || b == null) continue;
                var (t, dist) = SegDist(px, py, a.Value.x, a.Value.y, b.Value.x, b.Value.y);
                if (dist < bestD)
                {
                    bestD = dist;
                    var wa = new Vec3(E[6 * s], E[6 * s + 1], E[6 * s + 2]);
                    var wb = new Vec3(E[6 * s + 3], E[6 * s + 4], E[6 * s + 5]);
                    best = (obj, m.SegEdge[s], Vec3.Lerp(wa, wb, t), dist, a.Value.depth + (b.Value.depth - a.Value.depth) * t);
                }
            }
        }
        return best;
    }

    /// <summary>Nearest projected point (control points, snaps) within maxPx; returns its index.</summary>
    public static int NearestPoint(Camera cam, IReadOnlyList<Vec3> pts, double px, double py, double maxPx)
    {
        int best = -1; double bestD = maxPx;
        var vp = cam.ViewProjection;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = cam.Project(pts[i], vp);
            if (p == null) continue;
            double d = Math.Sqrt((p.Value.x - px) * (p.Value.x - px) + (p.Value.y - py) * (p.Value.y - py));
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    public static (double t, double dist) SegDist(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-9 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1);
        double qx = ax + dx * t - px, qy = ay + dy * t - py;
        return (t, Math.Sqrt(qx * qx + qy * qy));
    }

    static bool RayBox(Vec3 o, Vec3 d, Vec3 min, Vec3 max, double tMax)
    {
        double t0 = 0, t1 = tMax;
        for (int i = 0; i < 3; i++)
        {
            double oi = i == 0 ? o.X : i == 1 ? o.Y : o.Z, di = i == 0 ? d.X : i == 1 ? d.Y : d.Z;
            double lo = i == 0 ? min.X : i == 1 ? min.Y : min.Z, hi = i == 0 ? max.X : i == 1 ? max.Y : max.Z;
            double pad = Math.Max((max - min).Length * 0.01, 1e-3);
            lo -= pad; hi += pad;
            if (Math.Abs(di) < 1e-12) { if (oi < lo || oi > hi) return false; continue; }
            double a = (lo - oi) / di, b = (hi - oi) / di;
            if (a > b) (a, b) = (b, a);
            t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
            if (t0 > t1) return false;
        }
        return true;
    }

    // Moller-Trumbore
    static double? RayTri(Vec3 o, Vec3 d, float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
    {
        double e1x = bx - ax, e1y = by - ay, e1z = bz - az, e2x = cx - ax, e2y = cy - ay, e2z = cz - az;
        double px = d.Y * e2z - d.Z * e2y, py = d.Z * e2x - d.X * e2z, pz = d.X * e2y - d.Y * e2x;
        double det = e1x * px + e1y * py + e1z * pz;
        if (Math.Abs(det) < 1e-14) return null;
        double inv = 1 / det;
        double tx = o.X - ax, ty = o.Y - ay, tz = o.Z - az;
        double u = (tx * px + ty * py + tz * pz) * inv;
        if (u < 0 || u > 1) return null;
        double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
        double v = (d.X * qx + d.Y * qy + d.Z * qz) * inv;
        if (v < 0 || u + v > 1) return null;
        return (e2x * qx + e2y * qy + e2z * qz) * inv;
    }
}
