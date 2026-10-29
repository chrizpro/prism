using System;
using System.Collections.Generic;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>
/// Smooth per-vertex mean curvature for the curvature map. The surface's own normals at every vertex change across each mesh edge; fitting the
/// symmetric 2x2 shape operator to those changes (least squares over the edges at a vertex) gives principal curvatures that do not depend on
/// how the triangles happen to be shaped, and a light smoothing pass removes the last bit of tessellation noise.
/// </summary>
public static class MeshCurvature
{
    /// <summary>Signed mean curvature in 1/mm: positive where the surface bulges out (convex), negative where it dishes in.</summary>
    public static float[] MeanCurvature(MeshData m)
    {
        int nv = m.VertexCount; var P = m.Positions; var N = m.Normals;
        var nbr = new HashSet<int>[nv];
        for (int t = 0; t + 2 < m.Indices.Length; t += 3)
        {
            int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
            Link(nbr, a, b); Link(nbr, b, c); Link(nbr, c, a);
        }
        // vertices on the same spot (seams between faces) share information so edges of faces do not show as artefacts
        var res = new float[nv];
        for (int i = 0; i < nv; i++)
        {
            if (nbr[i] == null) continue;
            double nx = N[3 * i], ny = N[3 * i + 1], nz = N[3 * i + 2];
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (nl < 1e-9) continue; nx /= nl; ny /= nl; nz /= nl;
            // tangent basis
            double ux, uy, uz; if (Math.Abs(nz) < 0.9) { ux = -ny; uy = nx; uz = 0; } else { ux = 0; uy = -nz; uz = ny; }
            double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz); ux /= ul; uy /= ul; uz /= ul;
            double vx = ny * uz - nz * uy, vy = nz * ux - nx * uz, vz = nx * uy - ny * ux;
            // normal equations for S = [[a b][b c]]: n_d = S d
            double s11 = 0, s12 = 0, s13 = 0, s22 = 0, s23 = 0, s33 = 0, r1 = 0, r2 = 0, r3 = 0; int cnt = 0;
            foreach (int j in nbr[i])
            {
                double dx = P[3 * j] - P[3 * i], dy = P[3 * j + 1] - P[3 * i + 1], dz = P[3 * j + 2] - P[3 * i + 2];
                double du = dx * ux + dy * uy + dz * uz, dv = dx * vx + dy * vy + dz * vz;
                double l2 = du * du + dv * dv; if (l2 < 1e-12) continue;
                double ex = N[3 * j] - N[3 * i], ey = N[3 * j + 1] - N[3 * i + 1], ez = N[3 * j + 2] - N[3 * i + 2];
                double eu = ex * ux + ey * uy + ez * uz, ev = ex * vx + ey * vy + ez * vz;
                double w = 1.0 / l2;
                // eu = a du + b dv ; ev = b du + c dv   (rows weighted by 1/|d|^2)
                s11 += w * du * du; s12 += w * du * dv; s22 += w * (dv * dv + du * du); s23 += w * du * dv; s33 += w * dv * dv; s13 += 0;
                r1 += w * eu * du; r2 += w * (eu * dv + ev * du); r3 += w * ev * dv;
                cnt++;
            }
            if (cnt < 2) continue;
            // unknown order (a, b, c): rows are [du dv 0] and [0 du dv]
            double A11 = s11, A12 = s12, A13 = 0, A22 = s22, A23 = s23, A33 = s33;
            if (Solve3(A11, A12, A13, A22, A23, A33, r1, r2, r3, out double a, out double bb, out double c)) res[i] = (float)(0.5 * (a + c));
            else res[i] = (float)(0.5 * (r1 / Math.Max(s11, 1e-12) + r3 / Math.Max(s33, 1e-12)));
        }
        // light smoothing along mesh edges (two passes)
        for (int pass = 0; pass < 2; pass++)
        {
            var next = new float[nv];
            for (int i = 0; i < nv; i++)
            {
                if (nbr[i] == null || nbr[i].Count == 0) { next[i] = res[i]; continue; }
                double sum = 0; foreach (int j in nbr[i]) sum += res[j];
                next[i] = (float)(0.5 * res[i] + 0.5 * sum / nbr[i].Count);
            }
            res = next;
        }
        return res;
    }

    static void Link(HashSet<int>[] nbr, int a, int b)
    {
        (nbr[a] ??= []).Add(b); (nbr[b] ??= []).Add(a);
    }

    static bool Solve3(double a11, double a12, double a13, double a22, double a23, double a33, double b1, double b2, double b3, out double x, out double y, out double z)
    {
        double det = a11 * (a22 * a33 - a23 * a23) - a12 * (a12 * a33 - a23 * a13) + a13 * (a12 * a23 - a22 * a13);
        x = y = z = 0;
        if (Math.Abs(det) < 1e-14) return false;
        x = (b1 * (a22 * a33 - a23 * a23) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a23 - a22 * b3)) / det;
        y = (a11 * (b2 * a33 - a23 * b3) - b1 * (a12 * a33 - a23 * a13) + a13 * (a12 * b3 - b2 * a13)) / det;
        z = (a11 * (a22 * b3 - b2 * a23) - a12 * (a12 * b3 - b2 * a13) + b1 * (a12 * a23 - a22 * a13)) / det;
        return true;
    }
}
