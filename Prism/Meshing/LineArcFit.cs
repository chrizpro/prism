using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Meshing;

/// <summary>A fitted sketch element: a straight line or a circular arc (start, mid, end are all on it; sweep is signed degrees, positive = counter-clockwise).</summary>
public sealed record SketchSeg(bool IsArc, (double x, double y) P0, (double x, double y) Mid, (double x, double y) P1, (double x, double y) Center, double Radius, double SweepDeg);

/// <summary>Tolerance-driven conversion of a polyline (e.g. a mesh section) into lines and arcs.</summary>
public static class LineArcFit
{
    sealed class Fitter
    {
        public (double x, double y)[] P = [];
        public double Tol;

        // total least squares line through P[i..j]: point + unit direction
        public ((double x, double y) c, (double x, double y) d, double dev) Line(int i, int j)
        {
            int n = j - i + 1; double mx = 0, my = 0; for (int k = i; k <= j; k++) { mx += P[k].x; my += P[k].y; }
            mx /= n; my /= n; double sxx = 0, sxy = 0, syy = 0;
            for (int k = i; k <= j; k++) { double dx = P[k].x - mx, dy = P[k].y - my; sxx += dx * dx; sxy += dx * dy; syy += dy * dy; }
            double th = 0.5 * Math.Atan2(2 * sxy, sxx - syy); double ux = Math.Cos(th), uy = Math.Sin(th);
            if ((P[j].x - P[i].x) * ux + (P[j].y - P[i].y) * uy < 0) { ux = -ux; uy = -uy; }
            double dev = 0; for (int k = i; k <= j; k++) dev = Math.Max(dev, Math.Abs(-(P[k].x - mx) * uy + (P[k].y - my) * ux));
            return ((mx, my), (ux, uy), dev);
        }

        public bool LineOk(int i, int j) => j - i < 1 || Line(i, j).dev <= Tol;

        public (double cx, double cy, double r, double dev, double sweep)? Circle(int i, int j)
        {
            var pts = new List<(double x, double y)>(); for (int k = i; k <= j; k++) pts.Add(P[k]);
            if (FitCircleFast(pts) is not { } c) return null;
            double dev = 0; foreach (var p in pts) dev = Math.Max(dev, Math.Abs(Math.Sqrt((p.x - c.cx) * (p.x - c.cx) + (p.y - c.cy) * (p.y - c.cy)) - c.r));
            double sweep = 0, prev = Math.Atan2(pts[0].y - c.cy, pts[0].x - c.cx);
            for (int k = 1; k < pts.Count; k++)
            {
                double a = Math.Atan2(pts[k].y - c.cy, pts[k].x - c.cx), da = a - prev;
                while (da > Math.PI) da -= 2 * Math.PI;
                while (da < -Math.PI) da += 2 * Math.PI;
                sweep += da; prev = a;
            }
            return (c.cx, c.cy, c.r, dev, sweep);
        }

        public bool ArcOk(int i, int j)
        {
            if (j - i < 4) return false;
            if (Circle(i, j) is not { } c) return false;
            if (c.dev > Tol || c.r < 2 * Tol || Math.Abs(c.sweep) > 340 * Math.PI / 180) return false;
            double chord = Dist(P[i], P[j]);
            return c.r - Math.Sqrt(Math.Max(c.r * c.r - chord * chord / 4, 0)) > Tol * 0.5 || Math.Abs(c.sweep) > 0.35;
        }

        /// <summary>Largest j (up to n-1) for which ok(i, j) holds, assuming it holds up to some point and then stops. -1 when even 'first' fails.</summary>
        public int Extend(int i, int first, Func<int, int, bool> ok)
        {
            int n = P.Length; if (first > n - 1 || !ok(i, first)) return -1;
            int lo = first, step = 2;
            while (lo + step <= n - 1 && ok(i, lo + step)) { lo += step; step *= 2; }
            int hi = Math.Min(lo + step, n);
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (ok(i, mid)) lo = mid; else hi = mid; }
            return lo;
        }
    }

    // algebraic (Kasa-style, centred) circle fit
    static (double cx, double cy, double r)? FitCircleFast(List<(double x, double y)> pts)
    {
        int n = pts.Count; double mx = 0, my = 0; foreach (var p in pts) { mx += p.x; my += p.y; } mx /= n; my /= n;
        double suu = 0, suv = 0, svv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
        foreach (var p in pts)
        {
            double u = p.x - mx, v = p.y - my; suu += u * u; suv += u * v; svv += v * v; suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
        }
        double det = suu * svv - suv * suv; if (Math.Abs(det) < 1e-18 * (1 + suu * svv)) return null;
        double b1 = 0.5 * (suuu + suvv), b2 = 0.5 * (svvv + svuu);
        double uc = (b1 * svv - b2 * suv) / det, vc = (suu * b2 - suv * b1) / det;
        double r2 = uc * uc + vc * vc + (suu + svv) / n; if (r2 <= 0) return null;
        return (uc + mx, vc + my, Math.Sqrt(r2));
    }

    public static List<SketchSeg> Fit(IReadOnlyList<(double x, double y)> input, bool closed, double tol)
    {
        var pts = input.ToList();
        if (closed && pts.Count > 1 && Dist(pts[0], pts[^1]) < tol * 0.01) pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 2) return [];
        var f = new Fitter { Tol = Math.Max(tol, 1e-9) };

        if (closed)
        {
            f.P = pts.ToArray();
            if (pts.Count >= 8 && f.Circle(0, pts.Count - 1) is { } wc && wc.dev <= tol && Math.Abs(wc.sweep) > 340 * Math.PI / 180)
            {
                var c = (wc.cx, wc.cy); double a0 = Math.Atan2(pts[0].y - c.cy, pts[0].x - c.cx), sg = Math.Sign(wc.sweep);
                (double, double) At(double a) => (c.cx + wc.r * Math.Cos(a), c.cy + wc.r * Math.Sin(a));
                return [new SketchSeg(true, At(a0), At(a0 + sg * Math.PI / 2), At(a0 + sg * Math.PI), c, wc.r, sg * 180), new SketchSeg(true, At(a0 + sg * Math.PI), At(a0 + sg * 1.5 * Math.PI), At(a0), c, wc.r, sg * 180)];
            }
            // start at the sharpest corner so no primitive straddles the seam
            int best = 0; double bt = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[(i + pts.Count - 1) % pts.Count]; var b = pts[i]; var d = pts[(i + 1) % pts.Count];
                double t = Math.Abs(Angle(Sub(b, a), Sub(d, b))); if (t > bt) { bt = t; best = i; }
            }
            pts = pts.Skip(best).Concat(pts.Take(best)).ToList(); pts.Add(pts[0]);
        }
        f.P = pts.ToArray(); int n = pts.Count;

        List<(int i, int j, bool arc)> Greedy()
        {
            var rs = new List<(int i, int j, bool arc)>(); int s = 0;
            while (s < n - 1)
            {
                int jl = f.Extend(s, s + 1, f.LineOk); if (jl < 0) jl = s + 1;
                int ja = f.Extend(s, s + 4, f.ArcOk);
                bool arc = ja > jl; int e = arc ? ja : jl;
                rs.Add((s, e, arc)); s = e;
            }
            return rs;
        }
        var ranges = Greedy();
        if (closed && ranges.Count > 1)
        {
            // the seam may have cut one smooth element in two: restart at the last element's start and keep the better result
            int k0 = ranges[^1].i; var rot = pts.Take(pts.Count - 1).Skip(k0).Concat(pts.Take(k0)).ToList(); rot.Add(rot[0]);
            var keepP = f.P; f.P = rot.ToArray();
            var alt = Greedy();
            if (alt.Count < ranges.Count) { ranges = alt; pts = rot; n = pts.Count; } else f.P = keepP;
        }

        var segs = new List<(bool arc, (double x, double y) c, (double x, double y) d, double r, double sweep, (double x, double y) a, (double x, double y) b)>();
        foreach (var (i, j, arc) in ranges)
        {
            if (arc)
            {
                var c = f.Circle(i, j)!.Value; var cc = (x: c.cx, y: c.cy);
                (double x, double y) Proj((double x, double y) p) { double d = Dist(p, cc); return d < 1e-12 ? p : (cc.x + (p.x - cc.x) * c.r / d, cc.y + (p.y - cc.y) * c.r / d); }
                segs.Add((true, cc, (0, 0), c.r, c.sweep, Proj(pts[i]), Proj(pts[j])));
            }
            else
            {
                var l = f.Line(i, j);
                (double x, double y) Proj((double x, double y) p) { double t = (p.x - l.c.x) * l.d.x + (p.y - l.c.y) * l.d.y; return (l.c.x + l.d.x * t, l.c.y + l.d.y * t); }
                segs.Add((false, l.c, l.d, 0, 0, Proj(pts[i]), Proj(pts[j])));
            }
        }
        int m = segs.Count; var joint = new (double x, double y)[m + 1];
        (double x, double y) JunctionOf(int k, int a, int b)
        {
            var sa = segs[a]; var sb = segs[b];
            if (!sa.arc && !sb.arc)
            {
                double cr = sa.d.x * sb.d.y - sa.d.y * sb.d.x;
                if (Math.Abs(cr) > 0.17)
                {
                    double t = ((sb.c.x - sa.c.x) * sb.d.y - (sb.c.y - sa.c.y) * sb.d.x) / cr;
                    var q = (x: sa.c.x + sa.d.x * t, y: sa.c.y + sa.d.y * t);
                    if (Dist(q, pts[k]) <= 3 * tol) return q;
                }
            }
            return ((sa.b.x + sb.a.x) / 2, (sa.b.y + sb.a.y) / 2);
        }
        joint[0] = closed ? JunctionOf(ranges[0].i, m - 1, 0) : segs[0].a;
        for (int k = 1; k < m; k++) joint[k] = JunctionOf(ranges[k].i, k - 1, k);
        joint[m] = closed ? joint[0] : segs[m - 1].b;

        var res = new List<SketchSeg>();
        for (int k = 0; k < m; k++)
        {
            var a = joint[k]; var b = joint[k + 1]; var sg = segs[k];
            if (!sg.arc) { res.Add(new SketchSeg(false, a, ((a.x + b.x) / 2, (a.y + b.y) / 2), b, (0, 0), 0, 0)); continue; }
            double a0 = Math.Atan2(a.y - sg.c.y, a.x - sg.c.x), a1 = Math.Atan2(b.y - sg.c.y, b.x - sg.c.x), sw = a1 - a0, dir = Math.Sign(sg.sweep);
            if (dir >= 0) { while (sw < 0) sw += 2 * Math.PI; while (sw > 2 * Math.PI) sw -= 2 * Math.PI; }
            else { while (sw > 0) sw -= 2 * Math.PI; while (sw < -2 * Math.PI) sw += 2 * Math.PI; }
            double am = a0 + sw / 2;
            res.Add(new SketchSeg(true, a, (sg.c.x + sg.r * Math.Cos(am), sg.c.y + sg.r * Math.Sin(am)), b, sg.c, sg.r, sw * 180 / Math.PI));
        }
        return res;
    }

    /// <summary>Largest distance from any input point to the fitted lines and arcs.</summary>
    public static double MaxDeviation(IReadOnlyList<(double x, double y)> pts, IReadOnlyList<SketchSeg> segs)
    {
        double worst = 0;
        foreach (var p in pts)
        {
            double best = double.MaxValue;
            foreach (var s in segs)
            {
                double d;
                if (!s.IsArc)
                {
                    double dx = s.P1.x - s.P0.x, dy = s.P1.y - s.P0.y, l2 = dx * dx + dy * dy, t = l2 < 1e-24 ? 0 : Math.Clamp(((p.x - s.P0.x) * dx + (p.y - s.P0.y) * dy) / l2, 0, 1);
                    d = Dist(p, (s.P0.x + dx * t, s.P0.y + dy * t));
                }
                else
                {
                    double a0 = Math.Atan2(s.P0.y - s.Center.y, s.P0.x - s.Center.x), ap = Math.Atan2(p.y - s.Center.y, p.x - s.Center.x), sw = s.SweepDeg * Math.PI / 180;
                    double rel = ap - a0;
                    if (sw >= 0) { while (rel < 0) rel += 2 * Math.PI; while (rel >= 2 * Math.PI) rel -= 2 * Math.PI; }
                    else { while (rel > 0) rel -= 2 * Math.PI; while (rel <= -2 * Math.PI) rel += 2 * Math.PI; }
                    d = Math.Abs(rel) <= Math.Abs(sw) ? Math.Abs(Dist(p, s.Center) - s.Radius) : Math.Min(Dist(p, s.P0), Dist(p, s.P1));
                }
                best = Math.Min(best, d);
            }
            worst = Math.Max(worst, best);
        }
        return worst;
    }

    static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    static (double x, double y) Sub((double x, double y) a, (double x, double y) b) => (a.x - b.x, a.y - b.y);
    static double Angle((double x, double y) a, (double x, double y) b) => Math.Atan2(a.x * b.y - a.y * b.x, a.x * b.x + a.y * b.y);
}
