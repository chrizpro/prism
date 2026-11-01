using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core.Sketching;

public sealed class SolveResult
{
    public bool Converged { get; init; }
    public double Error { get; init; }
    /// <summary>Remaining degrees of freedom (how many independent ways the sketch can still move).</summary>
    public int Dof { get; init; }
    /// <summary>True when constraints are consistent but some are redundant.</summary>
    public bool Redundant { get; init; }
    public HashSet<int> FreePoints { get; init; } = [];
    public HashSet<int> FreeEntities { get; init; } = [];
    public string Status => !Converged ? "Conflicting constraints" : Dof == 0 ? (Redundant ? "Fully constrained (some constraints are redundant)" : "Fully constrained") : $"{Dof} degree{(Dof == 1 ? "" : "s")} of freedom";
    public bool FullyConstrained => Converged && Dof == 0;
}

/// <summary>
/// Solves a sketch: every point coordinate (and circle radius) is an unknown; every constraint contributes residual equations.
/// Levenberg-Marquardt with a finite-difference Jacobian. The Jacobian's null space tells us which entities can still move.
/// </summary>
public static class SketchSolver
{
    sealed class Problem
    {
        public SketchModel M = null!;
        public Dictionary<int, int> PIdx = [];
        public Dictionary<int, int> RIdx = [];
        public int N;
        public List<(int pid, double tx, double ty, double w)> Drags = [];
        public (int eid, double r, double w)? RDrag;

        public Problem(SketchModel m)
        {
            M = m;
            foreach (var p in m.Points) PIdx[p.Id] = N++;
            N *= 2;
            foreach (var e in m.Entities.Where(e => e.Type is EntType.Circle or EntType.Ellipse)) RIdx[e.Id] = N++;
        }

        public double[] Pack()
        {
            var x = new double[N];
            foreach (var p in M.Points) { x[PIdx[p.Id] * 2] = p.X; x[PIdx[p.Id] * 2 + 1] = p.Y; }
            foreach (var (eid, i) in RIdx) x[i] = M.E(eid).R;
            return x;
        }

        public void Unpack(double[] x)
        {
            foreach (var p in M.Points) { p.X = x[PIdx[p.Id] * 2]; p.Y = x[PIdx[p.Id] * 2 + 1]; }
            foreach (var (eid, i) in RIdx) M.E(eid).R = Math.Abs(x[i]);
        }

        // ---- residuals
        const double Eps = 1e-12;
        public void Eval(double[] x, List<double> r)
        {
            r.Clear();
            double X(int pid) => x[PIdx[pid] * 2];
            double Y(int pid) => x[PIdx[pid] * 2 + 1];
            double Rad(SkEntity e) => e.Type == EntType.Circle ? x[RIdx[e.Id]] : Dist(X(e.A), Y(e.A), X(e.B), Y(e.B));
            (double dx, double dy, double len) Dir(SkEntity l) { double dx = X(l.B) - X(l.A), dy = Y(l.B) - Y(l.A); return (dx, dy, Math.Max(Math.Sqrt(dx * dx + dy * dy), Eps)); }
            double Signed(int pid, SkEntity l) { var (dx, dy, len) = Dir(l); return ((X(pid) - X(l.A)) * dy - (Y(pid) - Y(l.A)) * dx) / len; }   // distance of point from the (infinite) line
            double WrapPi(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

            foreach (var c in M.Constraints)
            {
                if (c.Disabled) continue;
                var f = c.Refs;
                switch (c.Type)
                {
                    case CType.Coincident: r.Add(X(f[0]) - X(f[1])); r.Add(Y(f[0]) - Y(f[1])); break;
                    case CType.PointOnEntity:
                    {
                        var e = M.E(f[1]);
                        if (e.Type == EntType.Line) r.Add(Signed(f[0], e));
                        else if (e.Type == EntType.Ellipse)
                        {
                            double a = Math.Max(Dist(X(e.A), Y(e.A), X(e.B), Y(e.B)), Eps), b = Math.Max(Math.Abs(x[RIdx[e.Id]]), Eps);
                            double dx = X(f[0]) - X(e.A), dy = Y(f[0]) - Y(e.A), ca = (X(e.B) - X(e.A)) / a, sa = (Y(e.B) - Y(e.A)) / a;
                            double lu = dx * ca + dy * sa, lv = -dx * sa + dy * ca;
                            r.Add((Math.Sqrt(lu * lu / (a * a) + lv * lv / (b * b)) - 1) * Math.Min(a, b));
                        }
                        else if (e.Type is EntType.Circle or EntType.Arc) r.Add(Dist(X(f[0]), Y(f[0]), X(e.A), Y(e.A)) - Rad(e));   // splines and the rest have no centre to measure from
                        break;
                    }
                    case CType.Horizontal: if (f.Length == 2) r.Add(Y(f[0]) - Y(f[1])); else { var l = M.E(f[0]); r.Add(Y(l.A) - Y(l.B)); } break;
                    case CType.Vertical: if (f.Length == 2) r.Add(X(f[0]) - X(f[1])); else { var l = M.E(f[0]); r.Add(X(l.A) - X(l.B)); } break;
                    case CType.Parallel: { var a = Dir(M.E(f[0])); var b = Dir(M.E(f[1])); r.Add((a.dx * b.dy - a.dy * b.dx) / (a.len * b.len)); break; }
                    case CType.Perpendicular: { var a = Dir(M.E(f[0])); var b = Dir(M.E(f[1])); r.Add((a.dx * b.dx + a.dy * b.dy) / (a.len * b.len)); break; }
                    case CType.Collinear: { var l = M.E(f[0]); var o = M.E(f[1]); r.Add(Signed(o.A, l)); r.Add(Signed(o.B, l)); break; }
                    case CType.Equal:
                    {
                        var a = M.E(f[0]); var b = M.E(f[1]);
                        if (a.Type == EntType.Line && b.Type == EntType.Line) r.Add(Dir(a).len - Dir(b).len);
                        else r.Add(Rad(a) - Rad(b));
                        break;
                    }
                    case CType.Tangent:
                    {
                        var a = M.E(f[0]); var b = M.E(f[1]);
                        if (a.Type == EntType.Line || b.Type == EntType.Line)
                        {
                            var l = a.Type == EntType.Line ? a : b; var c2 = a.Type == EntType.Line ? b : a;
                            r.Add(Math.Abs(Signed(c2.A, l)) - Rad(c2));
                        }
                        else
                        {
                            double d = Dist(X(a.A), Y(a.A), X(b.A), Y(b.A));
                            r.Add(c.Value >= 0 ? d - (Rad(a) + Rad(b)) : d - Math.Abs(Rad(a) - Rad(b)));   // Value: +1 external, -1 internal
                        }
                        break;
                    }
                    case CType.Concentric: { var a = M.E(f[0]); var b = M.E(f[1]); r.Add(X(a.A) - X(b.A)); r.Add(Y(a.A) - Y(b.A)); break; }
                    case CType.Midpoint: { var l = M.E(f[1]); r.Add(X(f[0]) - (X(l.A) + X(l.B)) / 2); r.Add(Y(f[0]) - (Y(l.A) + Y(l.B)) / 2); break; }
                    case CType.Symmetric:
                    {
                        var l = M.E(f[2]); var (dx, dy, len) = Dir(l);
                        double mx = (X(f[0]) + X(f[1])) / 2, my = (Y(f[0]) + Y(f[1])) / 2;
                        r.Add(((mx - X(l.A)) * dy - (my - Y(l.A)) * dx) / len);                    // midpoint lies on the axis
                        r.Add(((X(f[1]) - X(f[0])) * dx + (Y(f[1]) - Y(f[0])) * dy) / len);       // pair is perpendicular to the axis
                        break;
                    }
                    case CType.Fixed: r.Add(X(f[0]) - c.Value); r.Add(Y(f[0]) - c.V2); break;
                    case CType.Distance: r.Add(Dist(X(f[0]), Y(f[0]), X(f[1]), Y(f[1])) - c.Value); break;
                    case CType.LineLength: { var l = M.E(f[0]); r.Add(Dir(l).len - c.Value); break; }
                    case CType.HDistance: r.Add(X(f[1]) - X(f[0]) - c.Value); break;
                    case CType.VDistance: r.Add(Y(f[1]) - Y(f[0]) - c.Value); break;
                    case CType.Radius: r.Add(Rad(M.E(f[0])) - c.Value); break;
                    case CType.Diameter: r.Add(2 * Rad(M.E(f[0])) - c.Value); break;
                    case CType.Angle:
                    {
                        var a = Dir(M.E(f[0])); var b = Dir(M.E(f[1]));
                        double ang = Math.Atan2(a.dx * b.dy - a.dy * b.dx, a.dx * b.dx + a.dy * b.dy);
                        r.Add(WrapPi(ang - c.Value * Math.PI / 180) * 10);                          // radians scaled so degrees weigh like millimetres
                        break;
                    }
                    case CType.PointLineDistance: r.Add(Math.Abs(Signed(f[0], M.E(f[1]))) - c.Value); break;
                }
            }
            // arcs must be circular: end point as far from the centre as the start point
            foreach (var e in M.Entities.Where(e => e.Type == EntType.Arc))
                r.Add(Dist(X(e.A), Y(e.A), X(e.C), Y(e.C)) - Dist(X(e.A), Y(e.A), X(e.B), Y(e.B)));
            foreach (var dg in Drags) { r.Add(dg.w * (X(dg.pid) - dg.tx)); r.Add(dg.w * (Y(dg.pid) - dg.ty)); }
            if (RDrag is { } rd) r.Add(rd.w * (x[RIdx[rd.eid]] - rd.r));
        }

        static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }

    static double Cost(List<double> r) { double s = 0; foreach (var v in r) s += v * v; return s; }

    static double[,] Jacobian(Problem p, double[] x, List<double> r0)
    {
        int m = r0.Count, n = p.N;
        var J = new double[m, n];
        var r1 = new List<double>(m);
        for (int j = 0; j < n; j++)
        {
            double h = 1e-6 * Math.Max(1, Math.Abs(x[j])), old = x[j];
            x[j] = old + h; p.Eval(x, r1);
            for (int i = 0; i < m && i < r1.Count; i++) J[i, j] = (r1[i] - r0[i]) / h;
            x[j] = old;
        }
        return J;
    }

    /// <summary>Solve in place. Pass a drag target to pull one point toward the cursor while constraints stay satisfied.</summary>
    public static SolveResult Solve(SketchModel model, IEnumerable<(int pid, double tx, double ty)>? drags = null, (int eid, double r)? radiusDrag = null)
    {
        var p = new Problem(model);
        if (drags != null) foreach (var d in drags) if (p.PIdx.ContainsKey(d.pid)) p.Drags.Add((d.pid, d.tx, d.ty, 0.02));   // weak pull: constraints dominate
        if (radiusDrag is { } rr && p.RIdx.ContainsKey(rr.eid)) p.RDrag = (rr.eid, rr.r, 0.02);
        bool dragging = p.Drags.Count > 0 || p.RDrag != null;
        var x = p.Pack();
        var r = new List<double>();
        p.Eval(x, r);
        double cost = Cost(r), lambda = 1e-3;
        for (int it = 0; it < 120 && p.N > 0; it++)
        {
            if (cost < 1e-22 && !dragging) break;
            var J = Jacobian(p, x, r);
            int m = r.Count, n = p.N;
            // normal equations: (JtJ + lambda*I) dx = -Jt r
            var A = new double[n, n]; var g = new double[n];
            for (int i = 0; i < m; i++)
                for (int a = 0; a < n; a++)
                {
                    double ja = J[i, a]; if (ja == 0) continue;
                    g[a] -= ja * r[i];
                    for (int b = 0; b < n; b++) A[a, b] += ja * J[i, b];
                }
            bool improved = false;
            for (int tries = 0; tries < 12; tries++)
            {
                var Ad = (double[,])A.Clone();
                for (int a = 0; a < n; a++) Ad[a, a] += lambda * (1 + A[a, a]);
                var dx = SolveLinear(Ad, g, n);
                if (dx == null) { lambda *= 10; continue; }
                var xn = new double[n]; for (int a = 0; a < n; a++) xn[a] = x[a] + dx[a];
                var rn = new List<double>(m); p.Eval(xn, rn);
                double cn = Cost(rn);
                if (cn < cost)
                {
                    bool tiny = cost - cn < 1e-18 * Math.Max(1, cost);
                    x = xn; r = rn; cost = cn; lambda = Math.Max(lambda * 0.3, 1e-12); improved = true;
                    if (tiny) it = 1000;
                    break;
                }
                lambda *= 5;
            }
            if (!improved) break;
        }
        p.Unpack(x);

        if (dragging)
        {
            // second pass without the pull: removes any tiny constraint error the drag introduced
            return Solve(model);
        }

        // analyse the converged state
        p.Eval(x, r);
        double err = Math.Sqrt(Cost(r));
        bool ok = err < 1e-5;
        var Jfinal = Jacobian(p, x, r);
        var (rank, free) = NullSpace(Jfinal, r.Count, p.N);
        int dof = p.N - rank;
        var freePts = new HashSet<int>(); var freeEnts = new HashSet<int>();
        foreach (var pt in model.Points) if (free[p.PIdx[pt.Id] * 2] || free[p.PIdx[pt.Id] * 2 + 1]) freePts.Add(pt.Id);
        foreach (var e in model.Entities)
            if (model.PointsOf(e).Any(freePts.Contains) || (e.Type is EntType.Circle or EntType.Ellipse && free[p.RIdx[e.Id]])) freeEnts.Add(e.Id);
        return new SolveResult { Converged = ok, Error = err, Dof = ok ? dof : dof, Redundant = ok && r.Count > rank && r.Count > 0 && RedundantCount(Jfinal, r.Count, p.N) > 0, FreePoints = freePts, FreeEntities = freeEnts };
    }

    static int RedundantCount(double[,] J, int m, int n)
    {
        // rank of J versus number of independent rows: rows - rank = redundant equations
        var (rank, _) = NullSpace(J, m, n);
        return m - rank;
    }

    /// <summary>Gauss-Jordan with partial pivoting. Returns the rank and, per unknown, whether it can still move (has a null-space component).</summary>
    static (int rank, bool[] free) NullSpace(double[,] J, int m, int n)
    {
        var A = (double[,])J.Clone();
        double scale = 0; foreach (var v in A) scale = Math.Max(scale, Math.Abs(v));
        double tol = Math.Max(scale, 1) * 1e-5;
        var pivotCol = new List<int>(); int row = 0;
        for (int col = 0; col < n && row < m; col++)
        {
            int best = row; double bv = Math.Abs(A[row, col]);
            for (int i = row + 1; i < m; i++) if (Math.Abs(A[i, col]) > bv) { bv = Math.Abs(A[i, col]); best = i; }
            if (bv < tol) continue;
            if (best != row) for (int j = 0; j < n; j++) (A[row, j], A[best, j]) = (A[best, j], A[row, j]);
            double pv = A[row, col];
            for (int j = 0; j < n; j++) A[row, j] /= pv;
            for (int i = 0; i < m; i++)
            {
                if (i == row) continue;
                double f = A[i, col]; if (Math.Abs(f) < 1e-14) continue;
                for (int j = 0; j < n; j++) A[i, j] -= f * A[row, j];
            }
            pivotCol.Add(col); row++;
        }
        var free = new bool[n];
        var isPivot = new bool[n]; foreach (var c in pivotCol) isPivot[c] = true;
        for (int fcol = 0; fcol < n; fcol++)
        {
            if (isPivot[fcol]) continue;
            free[fcol] = true;
            for (int k = 0; k < pivotCol.Count; k++) if (Math.Abs(A[k, fcol]) > 1e-7) free[pivotCol[k]] = true;
        }
        return (pivotCol.Count, free);
    }

    static double[]? SolveLinear(double[,] A, double[] b, int n)
    {
        var M = new double[n, n + 1];
        for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) M[i, j] = A[i, j]; M[i, n] = b[i]; }
        for (int c = 0; c < n; c++)
        {
            int piv = c; double bv = Math.Abs(M[c, c]);
            for (int i = c + 1; i < n; i++) if (Math.Abs(M[i, c]) > bv) { bv = Math.Abs(M[i, c]); piv = i; }
            if (bv < 1e-14) return null;
            if (piv != c) for (int j = c; j <= n; j++) (M[c, j], M[piv, j]) = (M[piv, j], M[c, j]);
            for (int i = c + 1; i < n; i++)
            {
                double f = M[i, c] / M[c, c]; if (f == 0) continue;
                for (int j = c; j <= n; j++) M[i, j] -= f * M[c, j];
            }
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = M[i, n];
            for (int j = i + 1; j < n; j++) s -= M[i, j] * x[j];
            x[i] = s / M[i, i];
        }
        return x;
    }
}
