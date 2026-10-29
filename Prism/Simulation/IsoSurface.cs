using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Simulation;

/// <summary>Turns the element densities of a generative-design run into a closed triangle surface (marching tetrahedra, capped on the original faces).</summary>
public static class IsoSurface
{
    public static TriMesh Extract(FemMesh m, double[] density, double[] vol, Vec3[]? boundaryNormal, double threshold = 0.5)
    {
        int nc = m.CornerCount;
        // density at the corner nodes: volume-weighted mean of the elements around them
        var sum = new double[nc]; var wsum = new double[nc];
        for (int e = 0; e < m.TetCount; e++)
            for (int k = 0; k < 4; k++) { int n = m.Tets[10 * e + k]; sum[n] += density[e] * vol[e]; wsum[n] += vol[e]; }
        var v = new double[nc]; for (int i = 0; i < nc; i++) v[i] = wsum[i] > 0 ? sum[i] / wsum[i] : 0;
        Vec3 P(int n) => new(m.Nodes[3 * n], m.Nodes[3 * n + 1], m.Nodes[3 * n + 2]);

        var verts = new List<float>(); var tris = new List<int>();
        var map = new Dictionary<long, int>();
        int Vertex(int a, int b)
        {
            // a vertex is a node (a == b) or the crossing on the edge a-b; the key is the same whichever way round it is asked
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;
            if (map.TryGetValue(key, out int id)) return id;
            Vec3 p;
            if (lo == hi) p = P(lo);
            else
            {
                double vl = v[lo], vh = v[hi]; double t = Math.Abs(vh - vl) < 1e-12 ? 0.5 : (threshold - vl) / (vh - vl);
                p = P(lo) + (P(hi) - P(lo)) * Math.Clamp(t, 0, 1);
            }
            id = verts.Count / 3; map[key] = id;
            verts.Add((float)p.X); verts.Add((float)p.Y); verts.Add((float)p.Z);
            return id;
        }
        Vec3 Pos(int id) => new(verts[3 * id], verts[3 * id + 1], verts[3 * id + 2]);
        void Tri(int a, int b, int c, Vec3 outward)
        {
            if (a == b || b == c || a == c) return;
            var n = Vec3.Cross(Pos(b) - Pos(a), Pos(c) - Pos(a));
            if (n.Length < 1e-14) return;
            if (Vec3.Dot(n, outward) < 0) (b, c) = (c, b);
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        // the material boundary inside the volume
        for (int e = 0; e < m.TetCount; e++)
        {
            int[] n = [m.Tets[10 * e], m.Tets[10 * e + 1], m.Tets[10 * e + 2], m.Tets[10 * e + 3]];
            var inside = n.Where(i => v[i] >= threshold).ToList(); var outside = n.Where(i => v[i] < threshold).ToList();
            if (inside.Count == 0 || outside.Count == 0) continue;
            Vec3 cIn = inside.Aggregate(Vec3.Zero, (acc, i) => acc + P(i)) / inside.Count, cOut = outside.Aggregate(Vec3.Zero, (acc, i) => acc + P(i)) / outside.Count;
            var dir = cOut - cIn;
            if (inside.Count == 1) Tri(Vertex(inside[0], outside[0]), Vertex(inside[0], outside[1]), Vertex(inside[0], outside[2]), dir);
            else if (inside.Count == 3) Tri(Vertex(inside[0], outside[0]), Vertex(inside[1], outside[0]), Vertex(inside[2], outside[0]), dir);
            else
            {
                int a = inside[0], b = inside[1], c = outside[0], d = outside[1];
                int q0 = Vertex(a, c), q1 = Vertex(a, d), q2 = Vertex(b, d), q3 = Vertex(b, c);
                Tri(q0, q1, q2, dir); Tri(q0, q2, q3, dir);
            }
        }
        // the original outer faces, where the material reaches them
        for (int i = 0; i < m.BoundaryCount; i++)
        {
            int[] c = [m.Boundary[6 * i], m.Boundary[6 * i + 1], m.Boundary[6 * i + 2]];
            var poly = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                int a = c[k], b = c[(k + 1) % 3]; bool ia = v[a] >= threshold, ib = v[b] >= threshold;
                if (ia) poly.Add(Vertex(a, a));
                if (ia != ib) poly.Add(Vertex(a, b));
            }
            if (poly.Count < 3) continue;
            var outward = boundaryNormal != null && i < boundaryNormal.Length && boundaryNormal[i].Length > 0.5 ? boundaryNormal[i] : Vec3.Cross(P(c[1]) - P(c[0]), P(c[2]) - P(c[0]));
            for (int k = 1; k + 1 < poly.Count; k++) Tri(poly[0], poly[k], poly[k + 1], outward);
        }
        return new TriMesh([.. verts], [.. tris]);
    }
}
