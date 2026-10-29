using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>
/// Unfolds a piece of surface into the plane (a UV layout) with least-squares conformal mapping. Surfaces that can be rolled out without stretching
/// (planes, cylinders, cones, bent sheet) come out essentially exact; others are flattened with as little distortion of angles as possible and the
/// remaining stretch is reported.
/// </summary>
public static class Flattener
{
    public sealed class Patch
    {
        public required List<List<(double u, double v)>> Loops { get; init; }      // loops[0] is the outer outline, the rest are holes
        public double Area3D { get; init; }
        public double Area2D { get; init; }
        public double MeanStrain { get; init; }            // average |stretch - 1|, as a fraction
        public double MaxStrain { get; init; }
        public int Triangles { get; init; }
        public (double u, double v) Min { get; init; }
        public (double u, double v) Max { get; init; }
        public required double[] VertexU { get; init; }
        public required double[] VertexV { get; init; }
        public required int[] Tris { get; init; }          // indices into VertexU / VertexV
        public required Vec3[] Vertex3D { get; init; }
    }

    public sealed record Result(List<Patch> Patches, List<string> Notes);

    /// <summary>Input: triangles over shared vertices. Each connected piece becomes a patch.</summary>
    public static Result Flatten(Vec3[] pos, int[] tri, Action<string>? progress = null)
    {
        var notes = new List<string>();
        int nt = tri.Length / 3;
        // connected components over shared vertices
        var parent = Enumerable.Range(0, pos.Length).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int t = 0; t < nt; t++) { Union(tri[3 * t], tri[3 * t + 1]); Union(tri[3 * t + 1], tri[3 * t + 2]); }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        var groups = new Dictionary<int, List<int>>();
        for (int t = 0; t < nt; t++) { int r = Find(tri[3 * t]); if (!groups.TryGetValue(r, out var l)) groups[r] = l = []; l.Add(t); }

        var patches = new List<Patch>(); int k = 0;
        foreach (var g in groups.Values.OrderByDescending(l => l.Count))
        {
            k++;
            try { patches.Add(FlattenComponent(pos, tri, g, notes, k)); }
            catch (Exception e) { notes.Add($"Piece {k}: {e.Message}"); }
        }
        return new Result(patches, notes);
    }

    static Patch FlattenComponent(Vec3[] pos, int[] tri, List<int> tris, List<string> notes, int piece)
    {
        // local vertex numbering
        var map = new Dictionary<int, int>(); var verts = new List<int>();
        int L(int v) { if (!map.TryGetValue(v, out int id)) { id = verts.Count; map[v] = id; verts.Add(v); } return id; }
        var T = new int[tris.Count * 3];
        for (int i = 0; i < tris.Count; i++) for (int c = 0; c < 3; c++) T[3 * i + c] = L(tri[3 * tris[i] + c]);
        int nv = verts.Count, nt = tris.Count;
        var P = verts.Select(v => pos[v]).ToArray();

        // topology: boundary edges, Euler characteristic
        var edgeCount = new Dictionary<long, int>();
        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        for (int t = 0; t < nt; t++) for (int c = 0; c < 3; c++) { long key = Key(T[3 * t + c], T[3 * t + (c + 1) % 3]); edgeCount[key] = edgeCount.GetValueOrDefault(key) + 1; }
        var boundary = edgeCount.Where(e => e.Value == 1).Select(e => e.Key).ToList();
        if (boundary.Count == 0) throw new InvalidOperationException("this piece is closed (like a whole box or sphere). Pick some of its faces instead, or leave one face out so it can open up.");
        if (edgeCount.Any(e => e.Value > 2)) notes.Add($"Piece {piece}: some edges are shared by more than two faces; the result may fold over.");

        // boundary loops
        var adj = new Dictionary<int, List<int>>();
        foreach (var e in boundary) { int a = (int)(e >> 32), b = (int)(e & 0xFFFFFFFF); (adj.TryGetValue(a, out var la) ? la : adj[a] = []).Add(b); (adj.TryGetValue(b, out var lb) ? lb : adj[b] = []).Add(a); }
        var loops = new List<List<int>>(); var seen = new HashSet<int>();
        foreach (var start in adj.Keys)
        {
            if (seen.Contains(start)) continue;
            var loop = new List<int> { start }; seen.Add(start); int prev = -1, cur = start;
            while (true)
            {
                int next = adj[cur].FirstOrDefault(x => x != prev && !seen.Contains(x), -1);
                if (next < 0) break;
                loop.Add(next); seen.Add(next); prev = cur; cur = next;
            }
            loops.Add(loop);
        }
        int chi = nv - edgeCount.Count + nt;
        if (chi != 2 - loops.Count)
            throw new InvalidOperationException("this piece cannot be laid flat as it is: it has handles or is pinched (like a donut or a bowtie). Pick fewer faces.");

        // pin the two boundary vertices that are farthest apart
        var bverts = adj.Keys.ToList();
        int pa = bverts[0];
        { double best = -1; foreach (var v in bverts) { double d = (P[v] - P[pa]).Length; if (d > best) { best = d; pa = v; } } }
        int pb = pa;
        { double best = -1; foreach (var v in bverts) { double d = (P[v] - P[pa]).Length; if (d > best) { best = d; pb = v; } } }
        double dist = (P[pa] - P[pb]).Length;

        // free vertex indices
        var freeIdx = new int[nv]; int nf = 0;
        for (int v = 0; v < nv; v++) freeIdx[v] = v == pa || v == pb ? -1 : nf++;
        double[] pinU = new double[nv], pinV = new double[nv];
        pinU[pa] = 0; pinV[pa] = 0; pinU[pb] = dist; pinV[pb] = 0;

        // rows: each triangle gives two equations with six unknowns
        var rowCol = new int[nt * 2, 6]; var rowVal = new double[nt * 2, 6]; var rhs = new double[nt * 2]; int rows = 0;
        var loc = new double[nt * 6];      // local 2D coordinates of every triangle (kept for the strain measure)
        double area3 = 0;
        for (int t = 0; t < nt; t++)
        {
            var p0 = P[T[3 * t]]; var p1 = P[T[3 * t + 1]]; var p2 = P[T[3 * t + 2]];
            var e1 = p1 - p0; var e2 = p2 - p0; double l1 = e1.Length;
            var n = Vec3.Cross(e1, e2); double nl = n.Length;
            if (l1 < 1e-12 || nl < 1e-14) { for (int c = 0; c < 6; c++) loc[6 * t + c] = double.NaN; continue; }
            var xa = e1 / l1; var ya = Vec3.Cross(n / nl, xa);
            double x1 = 0, y1 = 0, x2 = l1, y2 = 0, x3 = Vec3.Dot(e2, xa), y3 = Vec3.Dot(e2, ya);
            loc[6 * t] = x1; loc[6 * t + 1] = y1; loc[6 * t + 2] = x2; loc[6 * t + 3] = y2; loc[6 * t + 4] = x3; loc[6 * t + 5] = y3;
            double d = x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2);
            area3 += d / 2;
            double sd = Math.Sqrt(Math.Abs(d));
            double[] wr = [(x3 - x2) / sd, (x1 - x3) / sd, (x2 - x1) / sd], wi = [(y3 - y2) / sd, (y1 - y3) / sd, (y2 - y1) / sd];
            for (int eq = 0; eq < 2; eq++)
            {
                double b = 0;
                for (int j = 0; j < 3; j++)
                {
                    int v = T[3 * t + j];
                    double cu = eq == 0 ? wr[j] : wi[j], cv = eq == 0 ? -wi[j] : wr[j];
                    if (freeIdx[v] < 0) { b -= cu * pinU[v] + cv * pinV[v]; rowCol[rows, 2 * j] = -1; rowCol[rows, 2 * j + 1] = -1; rowVal[rows, 2 * j] = 0; rowVal[rows, 2 * j + 1] = 0; }
                    else { rowCol[rows, 2 * j] = freeIdx[v]; rowVal[rows, 2 * j] = cu; rowCol[rows, 2 * j + 1] = nf + freeIdx[v]; rowVal[rows, 2 * j + 1] = cv; }
                }
                rhs[rows] = b; rows++;
            }
        }

        // least squares by preconditioned conjugate gradients on A^T A
        int n2 = 2 * nf;
        var x = new double[n2];
        // start from the unit-scale guess: a plain projection onto the best-fit plane
        InitialGuess(P, T, nt, freeIdx, nf, x, pa, pb);
        double[] AtA_diag = new double[n2];
        for (int r = 0; r < rows; r++) for (int c = 0; c < 6; c++) { int col = rowCol[r, c]; if (col >= 0) AtA_diag[col] += rowVal[r, c] * rowVal[r, c]; }
        void Mul(double[] v, double[] outv)
        {
            Array.Clear(outv);
            for (int r = 0; r < rows; r++)
            {
                double s = 0; for (int c = 0; c < 6; c++) { int col = rowCol[r, c]; if (col >= 0) s += rowVal[r, c] * v[col]; }
                if (s == 0) continue;
                for (int c = 0; c < 6; c++) { int col = rowCol[r, c]; if (col >= 0) outv[col] += rowVal[r, c] * s; }
            }
        }
        var bvec = new double[n2];
        for (int r = 0; r < rows; r++) for (int c = 0; c < 6; c++) { int col = rowCol[r, c]; if (col >= 0) bvec[col] += rowVal[r, c] * rhs[r]; }
        var res = new double[n2]; var z = new double[n2]; var pdir = new double[n2]; var Ap = new double[n2];
        Mul(x, Ap);
        for (int i = 0; i < n2; i++) res[i] = bvec[i] - Ap[i];
        double bnorm = Math.Sqrt(bvec.Sum(v => v * v)) + 1e-30;
        for (int i = 0; i < n2; i++) { z[i] = res[i] / Math.Max(AtA_diag[i], 1e-30); pdir[i] = z[i]; }
        double rz = 0; for (int i = 0; i < n2; i++) rz += res[i] * z[i];
        int maxIt = Math.Min(20000, 200 + 6 * n2);
        for (int it = 0; it < maxIt; it++)
        {
            double rn = Math.Sqrt(res.Sum(v => v * v));
            if (rn / bnorm < 1e-9) break;
            Mul(pdir, Ap);
            double pAp = 0; for (int i = 0; i < n2; i++) pAp += pdir[i] * Ap[i];
            if (Math.Abs(pAp) < 1e-300) break;
            double alpha = rz / pAp;
            for (int i = 0; i < n2; i++) { x[i] += alpha * pdir[i]; res[i] -= alpha * Ap[i]; }
            for (int i = 0; i < n2; i++) z[i] = res[i] / Math.Max(AtA_diag[i], 1e-30);
            double rzNew = 0; for (int i = 0; i < n2; i++) rzNew += res[i] * z[i];
            double beta = rzNew / Math.Max(rz, 1e-300); rz = rzNew;
            for (int i = 0; i < n2; i++) pdir[i] = z[i] + beta * pdir[i];
        }

        var U = new double[nv]; var V = new double[nv];
        for (int v = 0; v < nv; v++) { if (freeIdx[v] < 0) { U[v] = pinU[v]; V[v] = pinV[v]; } else { U[v] = x[freeIdx[v]]; V[v] = x[nf + freeIdx[v]]; } }

        // orientation and scale (keep the total area)
        double area2 = 0;
        for (int t = 0; t < nt; t++) area2 += TriArea(U, V, T, t);
        if (area2 < 0) { for (int v = 0; v < nv; v++) V[v] = -V[v]; area2 = -area2; }
        double s0 = area2 > 1e-12 ? Math.Sqrt(Math.Abs(area3) / area2) : 1;
        for (int v = 0; v < nv; v++) { U[v] *= s0; V[v] *= s0; }
        area2 *= s0 * s0;

        // turn the pattern so its long direction runs along X (principal axes of the outline)
        {
            var bv = adj.Keys.ToList(); double mu = bv.Average(v => U[v]), mv = bv.Average(v => V[v]);
            double cxx = 0, cyy = 0, cxy = 0;
            foreach (var v in bv) { double du = U[v] - mu, dv = V[v] - mv; cxx += du * du; cyy += dv * dv; cxy += du * dv; }
            double ang = 0.5 * Math.Atan2(2 * cxy, cxx - cyy), ca = Math.Cos(-ang), sa = Math.Sin(-ang);
            for (int v = 0; v < nv; v++) { double u0 = U[v] - mu, v0 = V[v] - mv; U[v] = u0 * ca - v0 * sa; V[v] = u0 * sa + v0 * ca; }
        }

        // distortion: singular values of the map from each 3D triangle to its flat image
        double sumStrain = 0, maxStrain = 0; int counted = 0; int flipped = 0;
        for (int t = 0; t < nt; t++)
        {
            if (double.IsNaN(loc[6 * t])) continue;
            double x1 = loc[6 * t], y1 = loc[6 * t + 1], x2 = loc[6 * t + 2], y2 = loc[6 * t + 3], x3 = loc[6 * t + 4], y3 = loc[6 * t + 5];
            int a = T[3 * t], b = T[3 * t + 1], c = T[3 * t + 2];
            double ax = x2 - x1, ay = y2 - y1, bx = x3 - x1, by = y3 - y1;            // source edges
            double cx = U[b] - U[a], cy = V[b] - V[a], dx = U[c] - U[a], dy = V[c] - V[a];     // image edges
            double det = ax * by - ay * bx; if (Math.Abs(det) < 1e-18) continue;
            // J = [c d] * inverse([a b])
            double j11 = (cx * by - dx * ay) / det, j12 = (dx * ax - cx * bx) / det, j21 = (cy * by - dy * ay) / det, j22 = (dy * ax - cy * bx) / det;
            double e = (j11 + j22) / 2, f = (j11 - j22) / 2, g = (j21 + j12) / 2, h = (j21 - j12) / 2;
            double q = Math.Sqrt(e * e + h * h), r = Math.Sqrt(f * f + g * g);
            double smax = q + r, smin = Math.Abs(q - r);
            if (j11 * j22 - j12 * j21 < 0) flipped++;
            double strain = Math.Max(Math.Abs(smax - 1), Math.Abs(smin - 1));
            sumStrain += strain; maxStrain = Math.Max(maxStrain, strain); counted++;
        }
        if (flipped > 0) notes.Add($"Piece {piece}: {flipped} triangle(s) came out folded over; the surface curves too much to lay flat cleanly.");

        // outline loops in flat coordinates, outer one first
        var flat = loops.Select(lp => lp.Select(v => (U[v], V[v])).ToList()).ToList();
        double Area(List<(double u, double v)> l) { double a = 0; for (int i = 0; i < l.Count; i++) { var p = l[i]; var q2 = l[(i + 1) % l.Count]; a += p.u * q2.v - q2.u * p.v; } return a / 2; }
        flat = flat.OrderByDescending(l => Math.Abs(Area(l))).ToList();
        double mnU = U.Min(), mxU = U.Max(), mnV = V.Min(), mxV = V.Max();
        return new Patch
        {
            Loops = flat, Area3D = Math.Abs(area3), Area2D = area2, MeanStrain = counted > 0 ? sumStrain / counted : 0, MaxStrain = maxStrain, Triangles = nt,
            Min = (mnU, mnV), Max = (mxU, mxV), VertexU = U, VertexV = V, Tris = T, Vertex3D = P,
        };
    }

    static double TriArea(double[] U, double[] V, int[] T, int t)
    {
        int a = T[3 * t], b = T[3 * t + 1], c = T[3 * t + 2];
        return ((U[b] - U[a]) * (V[c] - V[a]) - (U[c] - U[a]) * (V[b] - V[a])) / 2;
    }

    /// <summary>A starting point for the solver: the vertices projected onto the plane through the two pinned points, facing the average normal.</summary>
    static void InitialGuess(Vec3[] P, int[] T, int nt, int[] freeIdx, int nf, double[] x, int pa, int pb)
    {
        Vec3 n = Vec3.Zero;
        for (int t = 0; t < nt; t++) n += Vec3.Cross(P[T[3 * t + 1]] - P[T[3 * t]], P[T[3 * t + 2]] - P[T[3 * t]]);
        if (n.Length < 1e-12) return;
        n = n.Normalized();
        var ux = (P[pb] - P[pa]); ux = (ux - n * Vec3.Dot(ux, n)); if (ux.Length < 1e-12) return; ux = ux.Normalized();
        var uy = Vec3.Cross(n, ux);
        for (int v = 0; v < P.Length; v++) if (freeIdx[v] >= 0) { var d = P[v] - P[pa]; x[freeIdx[v]] = Vec3.Dot(d, ux); x[nf + freeIdx[v]] = Vec3.Dot(d, uy); }
    }

    /// <summary>Douglas-Peucker on a closed loop: drop points that sit within tol of the line between their neighbours.</summary>
    public static List<(double u, double v)> Simplify(List<(double u, double v)> loop, double tol)
    {
        if (loop.Count < 8 || tol <= 0) return loop;
        // split at the two points farthest apart so both halves are open chains
        int a = 0, b = 0; double best = -1;
        for (int i = 0; i < loop.Count; i += Math.Max(1, loop.Count / 200)) for (int j = i + 1; j < loop.Count; j += Math.Max(1, loop.Count / 200)) { double d = Dist(loop[i], loop[j]); if (d > best) { best = d; a = i; b = j; } }
        var keep = new bool[loop.Count]; keep[a] = keep[b] = true;
        void Rec(int i, int j)
        {
            // indices run forward around the loop from i to j
            int n = (j - i + loop.Count) % loop.Count; if (n < 2) return;
            double md = -1; int mi = -1;
            for (int k = 1; k < n; k++) { int idx = (i + k) % loop.Count; double d = DistToSeg(loop[idx], loop[i], loop[j]); if (d > md) { md = d; mi = idx; } }
            if (md > tol) { keep[mi] = true; Rec(i, mi); Rec(mi, j); }
        }
        Rec(a, b); Rec(b, a);
        return loop.Where((_, i) => keep[i]).ToList();
    }

    static double Dist((double u, double v) p, (double u, double v) q) => Math.Sqrt((p.u - q.u) * (p.u - q.u) + (p.v - q.v) * (p.v - q.v));
    static double DistToSeg((double u, double v) p, (double u, double v) a, (double u, double v) b)
    {
        double dx = b.u - a.u, dy = b.v - a.v, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-18 ? 0 : Math.Clamp(((p.u - a.u) * dx + (p.v - a.v) * dy) / l2, 0, 1);
        return Dist(p, (a.u + dx * t, a.v + dy * t));
    }
}
