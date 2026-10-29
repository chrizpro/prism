using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Form;

/// <summary>What the cage and its smooth surface look like to someone judging surface quality.</summary>
public sealed record FormQuality(
    int Vertices, int Faces, int Quads, int Triangles, int NGons,
    int Poles3, int Poles5OrMore, int BoundaryVertices,
    IReadOnlyDictionary<int, int> Valence,
    double ShortestEdge, double LongestEdge, double MeanEdge, double WorstAspect, int StretchedFaces,
    double MeanCurvature, double MaxCurvature, Vec3 CurvatureHotSpot, double CurvatureSpread,
    IReadOnlyList<(int vertex, int valence)> Poles, IReadOnlyList<int> NGonFaces, IReadOnlyList<int> TriangleFaces)
{
    public int ExtraordinaryPoints => Poles3 + Poles5OrMore;

    public static FormQuality Analyze(SubdMesh m, int level = 2)
    {
        var topo = m.Topo();
        var neighbours = new HashSet<int>[m.V.Count];
        for (int i = 0; i < neighbours.Length; i++) neighbours[i] = [];
        var lengths = new List<double>();
        foreach (var (a, b) in m.Edges()) { neighbours[a].Add(b); neighbours[b].Add(a); lengths.Add(m.V[a].DistanceTo(m.V[b])); }

        int quads = 0, tris = 0, ngons = 0, stretched = 0; double worst = 1;
        var triFaces = new List<int>(); var ngonFaces = new List<int>();
        for (int f = 0; f < m.F.Count; f++)
        {
            var fc = m.F[f];
            if (fc.Length == 4) quads++; else if (fc.Length == 3) { tris++; triFaces.Add(f); } else { ngons++; ngonFaces.Add(f); }
            var el = Enumerable.Range(0, fc.Length).Select(i => m.V[fc[i]].DistanceTo(m.V[fc[(i + 1) % fc.Length]])).ToList();
            double aspect = el.Max() / Math.Max(el.Min(), 1e-9);
            worst = Math.Max(worst, aspect); if (aspect > 3) stretched++;
        }

        var histogram = new SortedDictionary<int, int>(); var poles = new List<(int, int)>(); int p3 = 0, p5 = 0, border = 0;
        for (int v = 0; v < m.V.Count; v++)
        {
            if (topo.VertFaces[v].Count == 0) continue;
            bool onBorder = neighbours[v].Any(n => topo.EdgeFaces[SubdMesh.Key(v, n)].Count == 1);
            if (onBorder) { border++; continue; }
            int valence = neighbours[v].Count;
            histogram[valence] = histogram.GetValueOrDefault(valence) + 1;
            if (valence == 3) { p3++; poles.Add((v, 3)); } else if (valence > 4) { p5++; poles.Add((v, valence)); }
        }

        // curvature of the smooth surface: the change of the normal along every edge of the displayed mesh
        double meanK = 0, maxK = 0, spread = 0; var hot = Vec3.Zero;
        if (m.F.Count > 0)
        {
            var d = m.BuildMesh(level); int vn = d.VertexCount; var sum = new double[vn]; var count = new int[vn];
            for (int t = 0; t < d.TriangleCount; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int i = d.Indices[t * 3 + e], j = d.Indices[t * 3 + (e + 1) % 3];
                    var pi = new Vec3(d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]); var pj = new Vec3(d.Positions[j * 3], d.Positions[j * 3 + 1], d.Positions[j * 3 + 2]);
                    var ni = new Vec3(d.Normals[i * 3], d.Normals[i * 3 + 1], d.Normals[i * 3 + 2]); var nj = new Vec3(d.Normals[j * 3], d.Normals[j * 3 + 1], d.Normals[j * 3 + 2]);
                    var dp = pj - pi; double len2 = Vec3.Dot(dp, dp); if (len2 < 1e-12) continue;
                    double k = Vec3.Dot(nj - ni, dp) / len2;
                    sum[i] += k; count[i]++; sum[j] += k; count[j]++;
                }
            }
            var values = new List<double>();
            for (int i = 0; i < vn; i++)
            {
                if (count[i] == 0) continue; double k = sum[i] / count[i]; values.Add(Math.Abs(k));
                if (Math.Abs(k) > maxK) { maxK = Math.Abs(k); hot = new Vec3(d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]); }
            }
            if (values.Count > 0) { meanK = values.Average(); values.Sort(); spread = values[(int)(values.Count * 0.95)] / Math.Max(values[values.Count / 2], 1e-9); }
        }
        return new FormQuality(m.V.Count, m.F.Count, quads, tris, ngons, p3, p5, border, histogram,
            lengths.Count == 0 ? 0 : lengths.Min(), lengths.Count == 0 ? 0 : lengths.Max(), lengths.Count == 0 ? 0 : lengths.Average(), worst, stretched,
            meanK, maxK, hot, spread, poles, ngonFaces, triFaces);
    }
}
