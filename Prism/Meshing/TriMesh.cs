using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>
/// An indexed triangle mesh (scan data, STL imports, converted CAD). Vertices are float xyz in millimetres, triangles are CCW from outside.
/// Instances are treated as immutable: every operation returns a new mesh.
/// </summary>
public sealed class TriMesh
{
    public float[] V { get; }
    public int[] T { get; }
    public int VertexCount => V.Length / 3;
    public int TriCount => T.Length / 3;

    Vec3? _min, _max;
    float[]? _faceNormals;
    Bvh? _bvh;

    /// <summary>Optional ARGB colour per vertex (scan / PLY / OBJ vertex colours). Null when the mesh is uncoloured. A wrong-length array is dropped.</summary>
    public uint[]? VertexColors { get; }
    /// <summary>Optional ARGB colour per triangle (OBJ materials, 3MF materials). Null when the mesh is uncoloured. A wrong-length array is dropped.</summary>
    public uint[]? FaceColors { get; }
    public bool HasColors => VertexColors != null || FaceColors != null;

    public TriMesh(float[] v, int[] t, uint[]? vertexColors = null, uint[]? faceColors = null)
    {
        V = v; T = t;
        VertexColors = vertexColors != null && vertexColors.Length == v.Length / 3 ? vertexColors : null;
        FaceColors = faceColors != null && faceColors.Length == t.Length / 3 ? faceColors : null;
    }

    /// <summary>The same geometry with different colours (the arrays are shared, not copied).</summary>
    public TriMesh WithColors(uint[]? vertexColors, uint[]? faceColors) => new(V, T, vertexColors, faceColors);

    /// <summary>The colour of one triangle: its face colour, else the average of its vertex colours, else <paramref name="fallback"/>.</summary>
    public uint ColorOf(int t, uint fallback = 0xFFC8D0DA)
    {
        if (FaceColors != null) return FaceColors[t];
        if (VertexColors == null) return fallback;
        uint a = VertexColors[T[3 * t]], b = VertexColors[T[3 * t + 1]], c = VertexColors[T[3 * t + 2]];
        static uint Ch(uint x, uint y, uint z, int sh) => (((x >> sh) & 0xFF) + ((y >> sh) & 0xFF) + ((z >> sh) & 0xFF)) / 3;
        return Ch(a, b, c, 24) << 24 | Ch(a, b, c, 16) << 16 | Ch(a, b, c, 8) << 8 | Ch(a, b, c, 0);
    }

    MeshData? _colorDisplay;
    /// <summary>Render mesh that shows the colours (flat colour per triangle, packed into the shader's scalar). Null when the mesh has no colours.</summary>
    public MeshData? ColorDisplay
    {
        get
        {
            if (!HasColors) return null;
            if (_colorDisplay != null) return _colorDisplay;
            var label = new int[TriCount];
            for (int t = 0; t < label.Length; t++) label[t] = (int)(ColorOf(t) & 0xFFFFFF);
            var (m, sc) = ToLabelledMeshData(label);
            m.Scalars = sc;
            return _colorDisplay = m;
        }
    }

    public static TriMesh Empty => new([], []);

    public Vec3 P(int i) => new(V[3 * i], V[3 * i + 1], V[3 * i + 2]);
    public (int a, int b, int c) Tri(int t) => (T[3 * t], T[3 * t + 1], T[3 * t + 2]);

    public Vec3 Min { get { EnsureBounds(); return _min!.Value; } }
    public Vec3 Max { get { EnsureBounds(); return _max!.Value; } }
    public Vec3 Size => Max - Min;
    public Vec3 Center => (Min + Max) * 0.5;

    void EnsureBounds()
    {
        if (_min != null) return;
        if (VertexCount == 0) { _min = _max = Vec3.Zero; return; }
        double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1;
        for (int i = 0; i < V.Length; i += 3)
        {
            x0 = Math.Min(x0, V[i]); x1 = Math.Max(x1, V[i]); y0 = Math.Min(y0, V[i + 1]); y1 = Math.Max(y1, V[i + 1]); z0 = Math.Min(z0, V[i + 2]); z1 = Math.Max(z1, V[i + 2]);
        }
        _min = new(x0, y0, z0); _max = new(x1, y1, z1);
    }

    public Bvh Bvh => _bvh ??= new Bvh(this);

    /// <summary>Unit normal per triangle (zero for degenerate ones), xyz packed.</summary>
    public float[] FaceNormals
    {
        get
        {
            if (_faceNormals != null) return _faceNormals;
            var n = new float[TriCount * 3];
            for (int t = 0; t < TriCount; t++)
            {
                var c = Cross(t); double l = c.Length;
                if (l > 1e-18) { n[3 * t] = (float)(c.X / l); n[3 * t + 1] = (float)(c.Y / l); n[3 * t + 2] = (float)(c.Z / l); }
            }
            return _faceNormals = n;
        }
    }

    public Vec3 FaceNormal(int t) => new(FaceNormals[3 * t], FaceNormals[3 * t + 1], FaceNormals[3 * t + 2]);

    /// <summary>Unnormalised normal (twice the area) of a triangle.</summary>
    public Vec3 Cross(int t)
    {
        var a = P(T[3 * t]); var b = P(T[3 * t + 1]); var c = P(T[3 * t + 2]);
        return Vec3.Cross(b - a, c - a);
    }

    public double TriArea(int t) => Cross(t).Length * 0.5;

    public double Area() { double s = 0; for (int t = 0; t < TriCount; t++) s += TriArea(t); return s; }

    /// <summary>Signed enclosed volume (positive for outward-facing closed meshes).</summary>
    public double Volume()
    {
        double v = 0;
        for (int t = 0; t < TriCount; t++)
        {
            var a = P(T[3 * t]); var b = P(T[3 * t + 1]); var c = P(T[3 * t + 2]);
            v += Vec3.Dot(a, Vec3.Cross(b, c));
        }
        return v / 6.0;
    }

    public Vec3 Centroid()
    {
        // area-weighted surface centroid (stable for open meshes too)
        Vec3 s = Vec3.Zero; double w = 0;
        for (int t = 0; t < TriCount; t++)
        {
            double a = TriArea(t); var c = (P(T[3 * t]) + P(T[3 * t + 1]) + P(T[3 * t + 2])) / 3.0;
            s += c * a; w += a;
        }
        return w > 0 ? s / w : Center;
    }

    /// <summary>Centre of mass of the enclosed volume (closed meshes); falls back to the surface centroid.</summary>
    public Vec3 CenterOfMass()
    {
        Vec3 s = Vec3.Zero; double vol = 0;
        for (int t = 0; t < TriCount; t++)
        {
            var a = P(T[3 * t]); var b = P(T[3 * t + 1]); var c = P(T[3 * t + 2]);
            double v = Vec3.Dot(a, Vec3.Cross(b, c));
            s += (a + b + c) * v; vol += v;
        }
        return Math.Abs(vol) < 1e-12 ? Centroid() : s / (4 * vol);
    }

    /// <summary>Area-weighted smooth vertex normals.</summary>
    public float[] VertexNormals()
    {
        var acc = new double[VertexCount * 3];
        for (int t = 0; t < TriCount; t++)
        {
            var c = Cross(t);
            for (int k = 0; k < 3; k++) { int v = T[3 * t + k]; acc[3 * v] += c.X; acc[3 * v + 1] += c.Y; acc[3 * v + 2] += c.Z; }
        }
        var n = new float[acc.Length];
        for (int i = 0; i < VertexCount; i++)
        {
            double l = Math.Sqrt(acc[3 * i] * acc[3 * i] + acc[3 * i + 1] * acc[3 * i + 1] + acc[3 * i + 2] * acc[3 * i + 2]);
            if (l > 1e-18) { n[3 * i] = (float)(acc[3 * i] / l); n[3 * i + 1] = (float)(acc[3 * i + 1] / l); n[3 * i + 2] = (float)(acc[3 * i + 2] / l); }
        }
        return n;
    }

    public TriMesh Transformed(Func<Vec3, Vec3> f, bool flipWinding = false)
    {
        var v = new float[V.Length];
        for (int i = 0; i < VertexCount; i++) { var p = f(P(i)); v[3 * i] = (float)p.X; v[3 * i + 1] = (float)p.Y; v[3 * i + 2] = (float)p.Z; }
        var t = (int[])T.Clone();
        if (flipWinding) for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
        return new TriMesh(v, t, VertexColors, FaceColors);
    }

    /// <summary>A copy with only the listed triangles (vertices are compacted).</summary>
    public TriMesh Subset(IEnumerable<int> tris)
    {
        var map = new Dictionary<int, int>(); var v = new List<float>(); var t = new List<int>();
        var vc = VertexColors != null ? new List<uint>() : null; var fc = FaceColors != null ? new List<uint>() : null;
        foreach (int tri in tris)
        {
            for (int k = 0; k < 3; k++)
            {
                int old = T[3 * tri + k];
                if (!map.TryGetValue(old, out int nv)) { nv = map.Count; map[old] = nv; v.Add(V[3 * old]); v.Add(V[3 * old + 1]); v.Add(V[3 * old + 2]); vc?.Add(VertexColors![old]); }
                t.Add(nv);
            }
            fc?.Add(FaceColors![tri]);
        }
        return new TriMesh([.. v], [.. t], vc?.ToArray(), fc?.ToArray());
    }

    public static TriMesh Merge(IEnumerable<TriMesh> parts)
    {
        var v = new List<float>(); var t = new List<int>();
        var list = parts as IList<TriMesh> ?? parts.ToList();
        foreach (var m in list) { int off = v.Count / 3; v.AddRange(m.V); t.AddRange(m.T.Select(i => i + off)); }
        // colours survive when every part has vertex colours; otherwise any coloured part makes the result per-face coloured (plain parts get the default grey)
        uint[]? vc = null, fc = null;
        if (list.Count > 0 && list.All(m => m.VertexColors != null)) vc = list.SelectMany(m => m.VertexColors!).ToArray();
        else if (list.Any(m => m.HasColors))
        {
            var f = new List<uint>();
            foreach (var m in list) for (int i = 0; i < m.TriCount; i++) f.Add(m.ColorOf(i));
            fc = [.. f];
        }
        return new TriMesh([.. v], [.. t], vc, fc);
    }

    // ---------------------------------------------------------------- topology
    public static long EdgeKey(int a, int b, int nv) => a < b ? (long)a * nv + b : (long)b * nv + a;

    /// <summary>Counts of triangles per undirected edge, built by sorting half-edges.</summary>
    public MeshTopology Topology() => new(this);

    // ---------------------------------------------------------------- display
    /// <summary>Kernel-style render mesh. Normals are smoothed across edges flatter than creaseDeg; sharper edges stay crisp.</summary>
    public MeshData ToMeshData(double creaseDeg = 38, int region = 0)
    {
        int nt = TriCount;
        var fn = FaceNormals;
        double cosC = Math.Cos(creaseDeg * Math.PI / 180);
        // per-vertex incident triangles (CSR)
        var start = new int[VertexCount + 1];
        for (int i = 0; i < T.Length; i++) start[T[i] + 1]++;
        for (int i = 0; i < VertexCount; i++) start[i + 1] += start[i];
        var fill = (int[])start.Clone(); var inc = new int[T.Length];
        for (int t = 0; t < nt; t++) for (int k = 0; k < 3; k++) inc[fill[T[3 * t + k]]++] = t;

        var pos = new List<float>(VertexCount * 3); var nrm = new List<float>(VertexCount * 3); var idx = new int[T.Length];
        var cache = new Dictionary<(int, int, int, int), int>();
        for (int t = 0; t < nt; t++)
        {
            double nx0 = fn[3 * t], ny0 = fn[3 * t + 1], nz0 = fn[3 * t + 2];
            for (int k = 0; k < 3; k++)
            {
                int v = T[3 * t + k];
                double sx = 0, sy = 0, sz = 0;
                for (int j = start[v]; j < start[v + 1]; j++)
                {
                    int u = inc[j];
                    double ux = fn[3 * u], uy = fn[3 * u + 1], uz = fn[3 * u + 2];
                    if (ux * nx0 + uy * ny0 + uz * nz0 < cosC) continue;
                    // area weight via the corner angle is overkill here; plain face weighting keeps this fast
                    sx += ux; sy += uy; sz += uz;
                }
                double l = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                if (l < 1e-12) { sx = nx0; sy = ny0; sz = nz0; l = 1; }
                sx /= l; sy /= l; sz /= l;
                var key = (v, (int)Math.Round(sx * 400), (int)Math.Round(sy * 400), (int)Math.Round(sz * 400));
                if (!cache.TryGetValue(key, out int id))
                {
                    id = pos.Count / 3; cache[key] = id;
                    pos.Add(V[3 * v]); pos.Add(V[3 * v + 1]); pos.Add(V[3 * v + 2]);
                    nrm.Add((float)sx); nrm.Add((float)sy); nrm.Add((float)sz);
                }
                idx[3 * t + k] = id;
            }
        }
        var d = new MeshData(pos.Count / 3, nt, 0) { Min = Min, Max = Max, FaceCount = 1, EdgeCount = 0 };
        pos.CopyTo(d.Positions); nrm.CopyTo(d.Normals); Array.Copy(idx, d.Indices, idx.Length);
        Array.Fill(d.TriFace, region);
        d.FaceTriStart = [0, nt];
        d.EdgeSegStart = [0];
        return d;
    }

    /// <summary>Render mesh with one flat colour group per triangle label (vertices are unshared so labels can differ per triangle).</summary>
    public (MeshData mesh, float[] scalars) ToLabelledMeshData(int[] label, double creaseDeg = 38)
    {
        var d = ToMeshData(creaseDeg);
        // scalar per render vertex: label of the first triangle that uses it. Vertices were split by normal, so re-split by label.
        var pos = new List<float>(); var nrm = new List<float>(); var sc = new List<float>(); var idx = new int[T.Length];
        var cache = new Dictionary<(int, int), int>();
        for (int t = 0; t < TriCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int old = d.Indices[3 * t + k];
                var key = (old, label[t]);
                if (!cache.TryGetValue(key, out int id))
                {
                    id = pos.Count / 3; cache[key] = id;
                    pos.Add(d.Positions[3 * old]); pos.Add(d.Positions[3 * old + 1]); pos.Add(d.Positions[3 * old + 2]);
                    nrm.Add(d.Normals[3 * old]); nrm.Add(d.Normals[3 * old + 1]); nrm.Add(d.Normals[3 * old + 2]);
                    sc.Add(label[t]);
                }
                idx[3 * t + k] = id;
            }
        var m = new MeshData(pos.Count / 3, TriCount, 0) { Min = Min, Max = Max, FaceCount = 1, EdgeCount = 0 };
        pos.CopyTo(m.Positions); nrm.CopyTo(m.Normals); Array.Copy(idx, m.Indices, idx.Length);
        Array.Fill(m.TriFace, 0); m.FaceTriStart = [0, TriCount]; m.EdgeSegStart = [0];
        return (m, [.. sc]);
    }

    /// <summary>Render mesh whose per-vertex scalars are given per original mesh vertex (smooth fields such as thickness or deviation).</summary>
    public (MeshData mesh, float[] scalars) ToScalarMeshData(float[] perVertex, double creaseDeg = 38)
    {
        // use an unsplit mesh so the field is continuous
        var d = new MeshData(VertexCount, TriCount, 0) { Min = Min, Max = Max, FaceCount = 1, EdgeCount = 0 };
        Array.Copy(V, d.Positions, V.Length); Array.Copy(VertexNormals(), d.Normals, V.Length); Array.Copy(T, d.Indices, T.Length);
        Array.Fill(d.TriFace, 0); d.FaceTriStart = [0, TriCount]; d.EdgeSegStart = [0];
        return (d, perVertex);
    }
}

/// <summary>Edge adjacency of a mesh: boundary loops, non-manifold edges and orientation consistency.</summary>
public sealed class MeshTopology
{
    public readonly TriMesh Mesh;
    /// <summary>Undirected edge keys sorted, with the number of triangles on each.</summary>
    public int BoundaryEdges, NonManifoldEdges, InconsistentEdges;
    public List<int[]> HoleLoops { get; } = [];
    /// <summary>For every triangle, up to three neighbours across manifold edges (-1 = none).</summary>
    public int[] Neighbors { get; }

    public MeshTopology(TriMesh m)
    {
        Mesh = m;
        int nt = m.TriCount, nv = m.VertexCount;
        var he = new (long key, int tri, int dir, int a, int b)[nt * 3];
        for (int t = 0; t < nt; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3];
                he[3 * t + k] = (TriMesh.EdgeKey(a, b, Math.Max(nv, 1)), t, a < b ? 1 : -1, a, b);
            }
        Array.Sort(he, (x, y) => x.key.CompareTo(y.key));
        Neighbors = new int[nt * 3]; Array.Fill(Neighbors, -1);
        var boundary = new List<(int a, int b)>();
        for (int i = 0; i < he.Length;)
        {
            int j = i; while (j < he.Length && he[j].key == he[i].key) j++;
            int cnt = j - i;
            if (cnt == 1) { BoundaryEdges++; boundary.Add((he[i].a, he[i].b)); }
            else if (cnt == 2)
            {
                if (he[i].dir == he[i + 1].dir) InconsistentEdges++;
                int sa = SlotOf(m, he[i].tri, he[i].a, he[i].b), sb = SlotOf(m, he[i + 1].tri, he[i + 1].a, he[i + 1].b);
                Neighbors[3 * he[i].tri + sa] = he[i + 1].tri; Neighbors[3 * he[i + 1].tri + sb] = he[i].tri;
            }
            else
            {
                NonManifoldEdges++;
                for (int q = i; q < j; q++) { int s = SlotOf(m, he[q].tri, he[q].a, he[q].b); Neighbors[3 * he[q].tri + s] = -1; }
            }
            i = j;
        }
        BuildLoops(boundary);
    }

    static int SlotOf(TriMesh m, int t, int a, int b)
    {
        for (int k = 0; k < 3; k++) if (m.T[3 * t + k] == a && m.T[3 * t + (k + 1) % 3] == b) return k;
        return 0;
    }

    void BuildLoops(List<(int a, int b)> boundary)
    {
        var next = new Dictionary<int, List<int>>();
        for (int i = 0; i < boundary.Count; i++)
        {
            if (!next.TryGetValue(boundary[i].a, out var l)) next[boundary[i].a] = l = [];
            l.Add(i);
        }
        var used = new bool[boundary.Count];
        for (int s = 0; s < boundary.Count; s++)
        {
            if (used[s]) continue;
            var loop = new List<int>(); int cur = s; bool closed = false;
            while (true)
            {
                used[cur] = true; loop.Add(boundary[cur].a);
                int b = boundary[cur].b;
                if (b == boundary[s].a) { closed = true; break; }
                if (!next.TryGetValue(b, out var cand)) break;
                int nxt = cand.FirstOrDefault(c => !used[c], -1);
                if (nxt < 0) break;
                cur = nxt;
            }
            if (closed && loop.Count >= 3) HoleLoops.Add([.. loop]);
        }
    }
}
