using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>A set of selected triangles on one mesh: the region later tools fit surfaces to, extract or delete.</summary>
public sealed class MeshSelection
{
    public Guid ObjectId { get; }
    public TriMesh Mesh { get; }
    public HashSet<int> Tris { get; } = [];
    readonly int[] _nb;
    MeshData? _display;
    public int Version { get; private set; }

    public MeshSelection(Guid id, TriMesh mesh) { ObjectId = id; Mesh = mesh; _nb = mesh.Topology().Neighbors; }

    public bool IsEmpty => Tris.Count == 0;
    public void Touch() => Version++;

    public double Area() { double a = 0; foreach (int t in Tris) a += Mesh.TriArea(t); return a; }

    // ---------------------------------------------------------------- editing
    public void Add(IEnumerable<int> tris) { foreach (int t in tris) Tris.Add(t); Touch(); }
    public void Remove(IEnumerable<int> tris) { foreach (int t in tris) Tris.Remove(t); Touch(); }
    public void Clear() { Tris.Clear(); Touch(); }
    public void All() { for (int t = 0; t < Mesh.TriCount; t++) Tris.Add(t); Touch(); }

    public void Invert()
    {
        var now = new HashSet<int>(Tris); Tris.Clear();
        for (int t = 0; t < Mesh.TriCount; t++) if (!now.Contains(t)) Tris.Add(t);
        Touch();
    }

    /// <summary>Grow by one ring of triangles (sharing a vertex when byVertex), repeated rings times.</summary>
    public void Grow(int rings = 1)
    {
        var vt = VertexTriangles();
        for (int r = 0; r < rings; r++)
        {
            var add = new List<int>();
            foreach (int t in Tris)
                for (int k = 0; k < 3; k++)
                    foreach (int u in vt[Mesh.T[3 * t + k]]) if (!Tris.Contains(u)) add.Add(u);
            foreach (int u in add) Tris.Add(u);
        }
        Touch();
    }

    public void Shrink(int rings = 1)
    {
        var vt = VertexTriangles();
        for (int r = 0; r < rings; r++)
        {
            var drop = new List<int>();
            foreach (int t in Tris)
            {
                bool edge = false;
                for (int k = 0; k < 3 && !edge; k++) foreach (int u in vt[Mesh.T[3 * t + k]]) if (!Tris.Contains(u)) { edge = true; break; }
                if (edge) drop.Add(t);
            }
            foreach (int t in drop) Tris.Remove(t);
        }
        Touch();
    }

    int[][]? _vt;
    int[][] VertexTriangles()
    {
        if (_vt != null) return _vt;
        var lists = new List<int>[Mesh.VertexCount];
        for (int i = 0; i < lists.Length; i++) lists[i] = [];
        for (int t = 0; t < Mesh.TriCount; t++) for (int k = 0; k < 3; k++) lists[Mesh.T[3 * t + k]].Add(t);
        return _vt = lists.Select(l => l.ToArray()).ToArray();
    }

    // ---------------------------------------------------------------- region finders
    /// <summary>Flood from a seed over edges where neighbouring triangles turn less than the angle, and never drift more than 2.5x the angle from the seed's direction.</summary>
    public List<int> SmartPatch(int seed, double angleDeg)
    {
        double cosStep = Math.Cos(angleDeg * Math.PI / 180), cosDrift = Math.Cos(Math.Min(angleDeg * 2.5, 85) * Math.PI / 180);
        var fn = SmoothedNormals(); var seen = new HashSet<int> { seed }; var res = new List<int> { seed };
        double sx = fn[3 * seed], sy = fn[3 * seed + 1], sz = fn[3 * seed + 2];
        // a running mean normal keeps gently curved patches (cylinders, domes) from leaking while still following the curvature
        double mx = sx, my = sy, mz = sz;
        for (int i = 0; i < res.Count; i++)
        {
            int t = res[i];
            double tx = fn[3 * t], ty = fn[3 * t + 1], tz = fn[3 * t + 2];
            for (int k = 0; k < 3; k++)
            {
                int nb = _nb[3 * t + k];
                if (nb < 0 || seen.Contains(nb)) continue;
                double nx = fn[3 * nb], ny = fn[3 * nb + 1], nz = fn[3 * nb + 2];
                if (nx * tx + ny * ty + nz * tz < cosStep) continue;
                double ml = Math.Sqrt(mx * mx + my * my + mz * mz); if (ml < 1e-9) ml = 1;
                bool nearSeed = (nx * sx + ny * sy + nz * sz) >= cosDrift || (nx * mx + ny * my + nz * mz) / ml >= cosStep;
                if (!nearSeed) continue;
                seen.Add(nb); res.Add(nb);
                if (res.Count % 8 == 0) { mx = (mx * 7 + nx) / 8; my = (my * 7 + ny) / 8; mz = (mz * 7 + nz) / 8; }
            }
        }
        return res;
    }

    PrimitiveFit.Prepared? _prep; double _prepTol; List<FitRegion>? _regions; int[]? _regionOf; double _segTol = -1;

    /// <summary>
    /// Smart patch by surface fitting. On first use the whole mesh is segmented into planes, cylinders, spheres and cones (cached);
    /// a click takes the region under it. Triangles outside every region fall back to a local fit, then to an edge-angle flood for organic surfaces.
    /// </summary>
    public List<int> SmartFit(int seed, double tolerance, double angleDeg, out string what)
    {
        if (Mesh.TriCount <= 120000)
        {
            if (_regions == null || Math.Abs(_segTol - tolerance) > 1e-9)
            {
                _regions = PrimitiveFit.Detect(Mesh, new PrimitiveFit.Options(tolerance, 14, 10, true, true, true, true, 60));
                _regionOf = new int[Mesh.TriCount]; Array.Fill(_regionOf, -1);
                for (int i = 0; i < _regions.Count; i++) foreach (int t in _regions[i].Tris) _regionOf[t] = i;
                _segTol = tolerance;
            }
            if (_regionOf![seed] >= 0) { var reg = _regions[_regionOf[seed]]; what = reg.Describe(); return reg.Tris.ToList(); }
        }
        if (_prep == null || Math.Abs(_prepTol - tolerance) > 1e-9) { _prep = PrimitiveFit.Prepare(Mesh, new PrimitiveFit.Options(tolerance, 15, 3)); _prepTol = tolerance; }
        var r = PrimitiveFit.FitAround(_prep, seed);
        if (r != null && r.Tris.Length >= 12) { what = r.Describe(); return r.Tris.ToList(); }
        what = "free-form surface";
        return SmartPatch(seed, angleDeg);
    }

    float[]? _smooth;
    /// <summary>
    /// Face normals averaged with the surrounding triangles that point roughly the same way, so scan noise and thin slivers
    /// do not break a patch apart while a real crease (a much different normal) is never averaged away.
    /// </summary>
    float[] SmoothedNormals()
    {
        if (_smooth != null) return _smooth;
        var fn = Mesh.FaceNormals; int nt = Mesh.TriCount; var vt = VertexTriangles();
        var res = new float[nt * 3];
        const double cosSame = 0.5;     // within 60 degrees of the triangle's own normal
        for (int t = 0; t < nt; t++)
        {
            double nx = fn[3 * t], ny = fn[3 * t + 1], nz = fn[3 * t + 2], x = 0, y = 0, z = 0;
            for (int k = 0; k < 3; k++)
                foreach (int u in vt[Mesh.T[3 * t + k]])
                {
                    double ux = fn[3 * u], uy = fn[3 * u + 1], uz = fn[3 * u + 2];
                    if (ux * nx + uy * ny + uz * nz < cosSame) continue;
                    double w = Math.Max(Mesh.TriArea(u), 1e-12);
                    x += ux * w; y += uy * w; z += uz * w;
                }
            double l = Math.Sqrt(x * x + y * y + z * z);
            if (l < 1e-12) { res[3 * t] = (float)nx; res[3 * t + 1] = (float)ny; res[3 * t + 2] = (float)nz; }
            else { res[3 * t] = (float)(x / l); res[3 * t + 1] = (float)(y / l); res[3 * t + 2] = (float)(z / l); }
        }
        return _smooth = res;
    }

    /// <summary>Triangles whose centroid lies within radius of a point.</summary>
    public IEnumerable<int> Brush(Vec3 center, double radius, Vec3? facing = null)
    {
        double r2 = radius * radius;
        for (int t = 0; t < Mesh.TriCount; t++)
        {
            var c = (Mesh.P(Mesh.T[3 * t]) + Mesh.P(Mesh.T[3 * t + 1]) + Mesh.P(Mesh.T[3 * t + 2])) / 3;
            var d = c - center;
            if (d.X * d.X + d.Y * d.Y + d.Z * d.Z > r2) continue;
            if (facing is { } f && Vec3.Dot(Mesh.FaceNormal(t), f) < 0) continue;
            yield return t;
        }
    }

    // ---------------------------------------------------------------- display
    /// <summary>Per-triangle render mesh (unshared vertices) whose scalars flag selected triangles; updated in place as the selection changes.</summary>
    public MeshData Display
    {
        get
        {
            if (_display == null)
            {
                var d = Mesh.ToMeshData();
                int nt = Mesh.TriCount;
                var m = new MeshData(nt * 3, nt, 0) { Min = Mesh.Min, Max = Mesh.Max, FaceCount = 1, EdgeCount = 0 };
                for (int t = 0; t < nt; t++)
                    for (int k = 0; k < 3; k++)
                    {
                        int src = d.Indices[3 * t + k], dst = 3 * t + k;
                        m.Positions[3 * dst] = d.Positions[3 * src]; m.Positions[3 * dst + 1] = d.Positions[3 * src + 1]; m.Positions[3 * dst + 2] = d.Positions[3 * src + 2];
                        m.Normals[3 * dst] = d.Normals[3 * src]; m.Normals[3 * dst + 1] = d.Normals[3 * src + 1]; m.Normals[3 * dst + 2] = d.Normals[3 * src + 2];
                        m.Indices[3 * t + k] = dst;
                    }
                Array.Fill(m.TriFace, 0); m.FaceTriStart = [0, nt]; m.EdgeSegStart = [0];
                m.Scalars = new float[nt * 3];
                _display = m;
                _shown = null;
            }
            return _display;
        }
    }

    HashSet<int>? _shown;
    int _shownVersion = -1;

    /// <summary>Write the current selection into the display scalars (only the triangles that changed).</summary>
    public MeshData Refresh()
    {
        var m = Display; var sc = m.Scalars!;
        if (_shownVersion != Version)
        {
            if (_shown == null) { Array.Clear(sc); _shown = []; }
            foreach (int t in _shown) if (!Tris.Contains(t)) sc[3 * t] = sc[3 * t + 1] = sc[3 * t + 2] = 0;
            foreach (int t in Tris) if (!_shown.Contains(t)) sc[3 * t] = sc[3 * t + 1] = sc[3 * t + 2] = 1;
            _shown = new HashSet<int>(Tris);
            _shownVersion = Version;
            m.ScalarsVersion++;
        }
        return m;
    }

    // ---------------------------------------------------------------- results
    public TriMesh Extract() => Mesh.Subset(Tris.OrderBy(t => t));

    public TriMesh Without()
    {
        var keep = Enumerable.Range(0, Mesh.TriCount).Where(t => !Tris.Contains(t));
        return MeshOps.Compact(Mesh.V, keep.SelectMany(t => new[] { Mesh.T[3 * t], Mesh.T[3 * t + 1], Mesh.T[3 * t + 2] }).ToArray());
    }

    /// <summary>Ordered outline(s) of the selected region: polylines along its boundary edges (outer loop first).</summary>
    public List<List<Vec3>> Outlines()
    {
        var sub = Extract();
        var loops = sub.Topology().HoleLoops;
        return loops.Select(l => l.Select(sub.P).ToList()).OrderByDescending(l => Extent(l)).ToList();
    }

    static double Extent(List<Vec3> p) { var mn = p[0]; var mx = p[0]; foreach (var q in p) { mn = new(Math.Min(mn.X, q.X), Math.Min(mn.Y, q.Y), Math.Min(mn.Z, q.Z)); mx = new(Math.Max(mx.X, q.X), Math.Max(mx.Y, q.Y), Math.Max(mx.Z, q.Z)); } return (mx - mn).Length; }
}
