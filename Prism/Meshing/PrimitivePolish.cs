using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>
/// Geometric least-squares refinement of a detected plane, cylinder, sphere or cone.
/// Detection fits triangle centroids, which sit inside a curved surface by the chord sag, and finds a cylinder's axis from how
/// the normals spread. This polish minimises the true distance from the region's vertices (which lie on the scanned surface)
/// to the shape with Levenberg-Marquardt, and drops points far outside the noise (edges of neighbouring features).
/// </summary>
public static class PrimitivePolish
{
    const int MaxPoints = 6000;

    public static FitRegion Refine(FitRegion r, TriMesh m)
    {
        if (r.Kind is not (PrimKind.Plane or PrimKind.Cylinder or PrimKind.Sphere or PrimKind.Cone) || r.Tris.Length == 0) return r;
        var unique = new HashSet<int>();
        foreach (int t in r.Tris) for (int k = 0; k < 3; k++) unique.Add(m.T[3 * t + k]);
        var all = unique.Select(m.P).ToList();
        int step = Math.Max(1, all.Count / MaxPoints);
        var pts = all.Where((_, i) => i % step == 0).ToList();
        if (pts.Count < 8) return r;

        var u = Perp(r.Axis);
        var v = Vec3.Cross(r.Axis, u);
        var shape = new Shape(r.Kind, r.Origin, r.Axis, u, v, r.Radius, r.HalfAngle * Math.PI / 180);
        double scale = Math.Max((r.Max - r.Min).Length, 1e-3);

        for (int pass = 0; pass < 2; pass++)
        {
            shape = Solve(shape, pts, scale);
            // drop rim points that belong to neighbouring features, then solve again on what is left
            var e = pts.Select(p => Math.Abs(shape.Residual(p))).ToArray();
            double rms = Math.Sqrt(e.Sum(x => x * x) / e.Length);
            var kept = pts.Where((_, i) => e[i] <= Math.Max(3 * rms, 1e-9)).ToList();
            if (kept.Count < 8 || kept.Count == pts.Count) break;
            pts = kept;
        }

        var res = all.Select(shape.Residual).Select(Math.Abs).ToArray();
        double finalRms = Math.Sqrt(res.Select(x => x * x).Average());

        double t0 = r.T0, t1 = r.T1;
        if (r.Kind is PrimKind.Cylinder or PrimKind.Cone)
        {
            // axial extent from the inlier vertices, so noisy rim points do not stretch the surface past the region
            var h = all.Where((p, i) => res[i] <= 3 * finalRms + 1e-9).Select(p => Vec3.Dot(p - shape.Origin, shape.Axis)).OrderBy(x => x).ToList();
            if (h.Count > 0) { t0 = h[0]; t1 = h[^1]; }
        }
        var origin = shape.Origin;
        if (r.Kind == PrimKind.Cylinder)
        {
            // keep the axis point near the region, as detection does
            var c = all.Aggregate(Vec3.Zero, (s, p) => s + p) / all.Count;
            double shift = Vec3.Dot(c - origin, shape.Axis);
            origin += shape.Axis * shift; t0 -= shift; t1 -= shift;
        }
        var axis = shape.Axis;
        if (Vec3.Dot(axis, r.Axis) < 0 && r.Kind != PrimKind.Cone) { axis = -axis; (t0, t1) = (-t1, -t0); }
        return new FitRegion
        {
            Kind = r.Kind, Tris = r.Tris, Area = r.Area, Convex = r.Convex, Min = r.Min, Max = r.Max,
            Origin = origin, Axis = axis, Radius = shape.Radius, HalfAngle = shape.Half * 180 / Math.PI,
            T0 = t0, T1 = t1, Rms = finalRms, MaxDev = res.Max(),
        };
    }

    /// <summary>
    /// Fit one shape of the given kind to every triangle of a picked region, without segmentation: a closed-form estimate from the
    /// triangle positions and normals, then <see cref="Refine"/>. Returns null when the region cannot define that shape
    /// (for example a cylinder from a flat patch).
    /// </summary>
    public static FitRegion? FitWhole(TriMesh m, PrimKind kind)
    {
        int n = m.TriCount;
        if (n < 4) return null;
        var c = new Vec3[n]; var nr = new Vec3[n]; var a = new double[n];
        double area = 0; var cen = Vec3.Zero;
        for (int t = 0; t < n; t++)
        {
            var (i, j, k) = m.Tri(t);
            var cross = Vec3.Cross(m.P(j) - m.P(i), m.P(k) - m.P(i));
            a[t] = cross.Length / 2; c[t] = (m.P(i) + m.P(j) + m.P(k)) / 3; nr[t] = a[t] > 0 ? cross.Normalized() : Vec3.Zero;
            area += a[t]; cen += c[t] * a[t];
        }
        if (area <= 0) return null;
        cen /= area;
        var all = Enumerable.Range(0, n).ToArray();
        var mn = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var mx = -mn;
        for (int v = 0; v < m.VertexCount; v++) { var p = m.P(v); mn = new(Math.Min(mn.X, p.X), Math.Min(mn.Y, p.Y), Math.Min(mn.Z, p.Z)); mx = new(Math.Max(mx.X, p.X), Math.Max(mx.Y, p.Y), Math.Max(mx.Z, p.Z)); }
        var meanN = Vec3.Zero; for (int t = 0; t < n; t++) meanN += nr[t] * a[t];

        // scatter of the normals about their mean: its weakest direction is the axis of a cylinder or cone, the strongest-mean direction a plane's normal
        double[] Scatter(Func<int, Vec3> f, Vec3 centre)
        {
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            for (int t = 0; t < n; t++) { var d = f(t) - centre; double w = a[t]; xx += w * d.X * d.X; xy += w * d.X * d.Y; xz += w * d.X * d.Z; yy += w * d.Y * d.Y; yz += w * d.Y * d.Z; zz += w * d.Z * d.Z; }
            return [xx, xy, xz, yy, yz, zz];
        }

        FitRegion Seed(Vec3 origin, Vec3 axis, double radius = 0, double halfDeg = 0, bool convex = true) => new()
        {
            Kind = kind, Tris = all, Area = area, Min = mn, Max = mx, Origin = origin, Axis = axis.Normalized(), Radius = radius, HalfAngle = halfDeg, Convex = convex, Rms = double.MaxValue,
        };

        // a boss's normals point away from its axis, a hole's towards it
        bool ConvexCylinder(Vec3 origin, Vec3 ax)
        {
            double s = 0;
            for (int t = 0; t < n; t++) { var d = c[t] - origin; d -= ax * Vec3.Dot(d, ax); s += Vec3.Dot(d, nr[t]) * a[t]; }
            return s >= 0;
        }

        FitRegion? seed;
        switch (kind)
        {
            case PrimKind.Plane:
            {
                var normal = MeshAnalysis.EigenSym(Scatter(t => c[t], cen)).vectors[2];
                if (Vec3.Dot(normal, meanN) < 0) normal = -normal;
                seed = Seed(cen, normal);
                break;
            }
            case PrimKind.Sphere:
            {
                // |p|^2 = 2 c.p + k, linear in the centre and k
                var ata = new double[4, 5];
                for (int t = 0; t < n; t++)
                {
                    var p = c[t] - cen; double[] row = [2 * p.X, 2 * p.Y, 2 * p.Z, 1]; double rhs = Vec3.Dot(p, p);
                    for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) ata[i, j] += a[t] * row[i] * row[j]; ata[i, 4] += a[t] * row[i] * rhs; }
                }
                if (Gauss(ata, 4) is not { } x) return null;
                var centre = new Vec3(x[0], x[1], x[2]); double r2 = x[3] + Vec3.Dot(centre, centre);
                if (!(r2 > 0)) return null;
                seed = Seed(centre + cen, Vec3.UnitZ, Math.Sqrt(r2), convex: Vec3.Dot(meanN, cen - (centre + cen)) >= 0);
                break;
            }
            case PrimKind.Cylinder:
            case PrimKind.Cone:
            {
                var mean = meanN / area;
                var axis = MeshAnalysis.EigenSym(Scatter(t => nr[t], mean)).vectors[2];
                var u = Perp(axis); var v = Vec3.Cross(axis, u);
                if (kind == PrimKind.Cylinder)
                {
                    // circle fit in the plane across the axis
                    var ata = new double[3, 4];
                    for (int t = 0; t < n; t++)
                    {
                        var d = c[t] - cen; double px = Vec3.Dot(d, u), py = Vec3.Dot(d, v);
                        double[] row = [2 * px, 2 * py, 1]; double rhs = px * px + py * py;
                        for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) ata[i, j] += a[t] * row[i] * row[j]; ata[i, 3] += a[t] * row[i] * rhs; }
                    }
                    if (Gauss(ata, 3) is not { } x) return null;
                    double r2 = x[2] + x[0] * x[0] + x[1] * x[1];
                    if (!(r2 > 0)) return null;
                    var origin = cen + u * x[0] + v * x[1];
                    seed = Seed(origin, axis, Math.Sqrt(r2), convex: ConvexCylinder(origin, axis));
                }
                else
                {
                    // every tangent plane passes through the apex: n.(apex - p) = 0 for each triangle
                    var ata = new double[3, 4];
                    for (int t = 0; t < n; t++)
                    {
                        var q = nr[t]; double rhs = Vec3.Dot(q, c[t] - cen); double[] row = [q.X, q.Y, q.Z];
                        for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) ata[i, j] += a[t] * row[i] * row[j]; ata[i, 3] += a[t] * row[i] * rhs; }
                    }
                    if (Gauss(ata, 3) is not { } x) return null;
                    var apex = cen + new Vec3(x[0], x[1], x[2]);
                    if (Vec3.Dot(cen - apex, axis) < 0) axis = -axis;
                    double s = 0; for (int t = 0; t < n; t++) s += Math.Abs(Vec3.Dot(nr[t], axis)) * a[t];
                    double half = Math.Asin(Math.Clamp(s / area, 1e-4, 0.9999)) * 180 / Math.PI;
                    if (half < 0.5 || half > 89) return null;
                    seed = Seed(apex, axis, 0, half);
                }
                break;
            }
            default: return null;
        }
        if (seed == null) return null;
        // axial extent for the refinement's bounds
        if (kind is PrimKind.Cylinder or PrimKind.Cone)
        {
            double t0 = double.MaxValue, t1 = double.MinValue;
            for (int v = 0; v < m.VertexCount; v++) { double h = Vec3.Dot(m.P(v) - seed.Origin, seed.Axis); t0 = Math.Min(t0, h); t1 = Math.Max(t1, h); }
            seed = new FitRegion { Kind = seed.Kind, Tris = all, Area = area, Min = mn, Max = mx, Origin = seed.Origin, Axis = seed.Axis, Radius = seed.Radius, HalfAngle = seed.HalfAngle, Convex = seed.Convex, T0 = t0, T1 = t1, Rms = double.MaxValue };
        }
        var fit = Refine(seed, m);
        if (!double.IsFinite(fit.Rms) || kind is PrimKind.Cylinder or PrimKind.Sphere && !(fit.Radius > 0)) return null;
        return fit;
    }

    static Vec3 Perp(Vec3 a) => (Math.Abs(a.X) < 0.9 ? Vec3.Cross(a, Vec3.UnitX) : Vec3.Cross(a, Vec3.UnitY)).Normalized();

    /// <summary>A shape plus a local frame; parameters are small changes around it so the problem stays well conditioned.</summary>
    readonly record struct Shape(PrimKind Kind, Vec3 Origin, Vec3 Axis, Vec3 U, Vec3 V, double Radius, double Half)
    {
        public int Count => Kind switch { PrimKind.Plane => 3, PrimKind.Sphere => 4, PrimKind.Cylinder => 5, _ => 6 };

        public double Residual(Vec3 p)
        {
            var d = p - Origin;
            switch (Kind)
            {
                case PrimKind.Plane: return Vec3.Dot(d, Axis);
                case PrimKind.Sphere: return d.Length - Radius;
                case PrimKind.Cylinder: return (d - Axis * Vec3.Dot(d, Axis)).Length - Radius;
                default:
                {
                    double h = Vec3.Dot(d, Axis), rho = (d - Axis * h).Length;
                    return rho * Math.Cos(Half) - h * Math.Sin(Half);
                }
            }
        }

        /// <summary>The shape moved by the parameter vector: tilts about U and V, shifts along U, V (and the axis for spheres and cones), radius or angle.</summary>
        public Shape Apply(double[] x)
        {
            var axis = (Axis + U * x[0] + V * x[1]).Normalized();
            switch (Kind)
            {
                case PrimKind.Plane: return Reframe(Origin + Axis * x[2], axis, Radius, Half);
                case PrimKind.Sphere: return this with { Origin = Origin + U * x[0] + V * x[1] + Axis * x[2], Radius = Radius + x[3] };
                case PrimKind.Cylinder: return Reframe(Origin + U * x[2] + V * x[3], axis, Radius + x[4], Half);
                default: return Reframe(Origin + U * x[2] + V * x[3] + Axis * x[4], axis, Radius, Math.Clamp(Half + x[5], 1e-4, Math.PI / 2 - 1e-4));
            }
        }

        Shape Reframe(Vec3 origin, Vec3 axis, double radius, double half)
        {
            var u = (U - axis * Vec3.Dot(U, axis)).Normalized();
            return new Shape(Kind, origin, axis, u, Vec3.Cross(axis, u), radius, half);
        }
    }

    static Shape Solve(Shape s, List<Vec3> pts, double scale)
    {
        int n = s.Count;
        double lambda = 1e-3;
        double Cost(Shape q) => pts.Sum(p => { double e = q.Residual(p); return e * e; });
        double cost = Cost(s);
        for (int it = 0; it < 40; it++)
        {
            // numerical Jacobian: the parameters are dimensionless tilts or millimetre shifts, so scale the steps accordingly
            var jtj = new double[n, n]; var jte = new double[n];
            var steps = Enumerable.Range(0, n).Select(i => IsAngle(s.Kind, i) ? 1e-6 : 1e-6 * scale).ToArray();
            var plus = Enumerable.Range(0, n).Select(i => { var x = new double[6]; x[i] = steps[i]; return s.Apply(x); }).ToArray();
            var row = new double[n];
            foreach (var p in pts)
            {
                double e = s.Residual(p);
                for (int i = 0; i < n; i++) row[i] = (plus[i].Residual(p) - e) / steps[i];
                for (int i = 0; i < n; i++) { jte[i] += row[i] * e; for (int j = 0; j < n; j++) jtj[i, j] += row[i] * row[j]; }
            }
            bool improved = false;
            for (int tries = 0; tries < 8 && !improved; tries++)
            {
                var a = new double[n, n + 1];
                for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) a[i, j] = jtj[i, j]; a[i, i] += lambda * Math.Max(jtj[i, i], 1e-12); a[i, n] = -jte[i]; }
                var dx = Gauss(a, n);
                if (dx == null) { lambda *= 10; continue; }
                var x = new double[6]; Array.Copy(dx, x, n);
                var next = s.Apply(x);
                double c = Cost(next);
                if (c < cost) { s = next; improved = cost - c > cost * 1e-12; cost = c; lambda = Math.Max(lambda / 3, 1e-9); if (!improved) return s; }
                else lambda *= 10;
            }
            if (!improved) break;
        }
        return s;
    }

    static bool IsAngle(PrimKind kind, int i) => kind switch
    {
        PrimKind.Sphere => false,
        PrimKind.Cone => i is 0 or 1 or 5,
        _ => i is 0 or 1,
    };

    static double[]? Gauss(double[,] a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            if (Math.Abs(a[p, c]) < 1e-18) return null;
            if (p != c) for (int k = 0; k <= n; k++) (a[c, k], a[p, k]) = (a[p, k], a[c, k]);
            for (int r = c + 1; r < n; r++) { double f = a[r, c] / a[c, c]; for (int k = c; k <= n; k++) a[r, k] -= f * a[c, k]; }
        }
        var x = new double[n];
        for (int r = n - 1; r >= 0; r--) { double s = a[r, n]; for (int k = r + 1; k < n; k++) s -= a[r, k] * x[k]; x[r] = s / a[r, r]; }
        return x;
    }
}
