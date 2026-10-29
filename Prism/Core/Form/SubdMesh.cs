using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Form;

/// <summary>
/// A polygon control cage for a Catmull-Clark subdivision surface ("Form"). Faces are CCW seen from outside.
/// Edges can carry a crease sharpness (0 = smooth, >= 1 = fully sharp, fractional = semi-sharp).
/// </summary>
public sealed class SubdMesh
{
    public List<Vec3> V { get; } = [];
    public List<int[]> F { get; } = [];
    public Dictionary<long, double> Crease { get; } = [];
    /// <summary>Points that are locked ("frozen"): they cannot be moved or deleted by form editing. Stored by position so they survive changes of vertex numbers.</summary>
    public HashSet<(long, long, long)> Frozen { get; } = [];
    static (long, long, long) Q(Vec3 p) => ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
    public bool IsFrozen(int v) => Frozen.Count > 0 && Frozen.Contains(Q(V[v]));
    public bool FaceFrozen(int f) => Frozen.Count > 0 && F[f].All(IsFrozen);
    public void Freeze(IEnumerable<int> verts) { foreach (var v in verts) Frozen.Add(Q(V[v])); }
    public void Unfreeze(IEnumerable<int> verts) { foreach (var v in verts) Frozen.Remove(Q(V[v])); }
    /// <summary>Subdivision levels used for display (1-4).</summary>
    public int Level { get; set; } = 3;
    /// <summary>Persistent symmetry of the form: "off", "x", "y", "z" (mirror across the plane with that normal) or "radial:N". Edits are repeated on the mirror images.</summary>
    public string Sym { get; set; } = "off";

    public static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    public static (int a, int b) Unkey(long k) => ((int)(k >> 32), (int)(k & 0xFFFFFFFF));

    public SubdMesh Clone()
    {
        var m = new SubdMesh { Level = Level, Sym = Sym };
        m.V.AddRange(V);
        foreach (var f in F) m.F.Add((int[])f.Clone());
        foreach (var kv in Crease) m.Crease[kv.Key] = kv.Value;
        foreach (var p in Frozen) m.Frozen.Add(p);
        return m;
    }

    public bool IsEmpty => F.Count == 0;

    // ---------------------------------------------------------------- topology queries
    public sealed class Topology
    {
        public Dictionary<long, List<int>> EdgeFaces { get; } = [];
        public List<int>[] VertFaces { get; }
        public Topology(SubdMesh m)
        {
            VertFaces = new List<int>[m.V.Count];
            for (int i = 0; i < VertFaces.Length; i++) VertFaces[i] = [];
            for (int fi = 0; fi < m.F.Count; fi++)
            {
                var f = m.F[fi];
                for (int i = 0; i < f.Length; i++)
                {
                    int a = f[i], b = f[(i + 1) % f.Length];
                    var k = Key(a, b);
                    if (!EdgeFaces.TryGetValue(k, out var l)) EdgeFaces[k] = l = [];
                    l.Add(fi);
                    VertFaces[a].Add(fi);
                }
            }
        }
    }

    public Topology Topo() => new(this);

    public IEnumerable<(int a, int b)> Edges() => new Topology(this).EdgeFaces.Keys.Select(Unkey);

    public Vec3 FaceNormal(int fi)
    {
        var f = F[fi]; Vec3 n = Vec3.Zero;
        for (int i = 0; i < f.Length; i++)
        {
            var p = V[f[i]]; var q = V[f[(i + 1) % f.Length]];
            n += new Vec3((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y));
        }
        return n.Normalized();
    }

    public Vec3 FaceCenter(int fi) => F[fi].Aggregate(Vec3.Zero, (s, i) => s + V[i]) / F[fi].Length;

    public bool IsClosed()
    {
        var t = Topo();
        return t.EdgeFaces.Count > 0 && t.EdgeFaces.Values.All(l => l.Count == 2);
    }

    public (Vec3 min, Vec3 max) Bounds()
    {
        if (V.Count == 0) return (Vec3.Zero, Vec3.Zero);
        var min = new Vec3(V.Min(v => v.X), V.Min(v => v.Y), V.Min(v => v.Z));
        var max = new Vec3(V.Max(v => v.X), V.Max(v => v.Y), V.Max(v => v.Z));
        return (min, max);
    }

    // ---------------------------------------------------------------- Catmull-Clark
    /// <summary>One Catmull-Clark step. Child faces are emitted in parent order, each as (corner, next-edge pt, face pt, prev-edge pt).</summary>
    public SubdMesh Refine(out int[] parent)
    {
        var topo = Topo();
        int nv = V.Count, nf = F.Count;
        var r = new SubdMesh { Level = Level, Sym = Sym };

        // face points
        var fp = new Vec3[nf];
        for (int i = 0; i < nf; i++) fp[i] = FaceCenter(i);

        // edge points
        var edgeIndex = new Dictionary<long, int>();
        var edgePt = new List<Vec3>();
        foreach (var (k, faces) in topo.EdgeFaces)
        {
            var (a, b) = Unkey(k);
            var mid = (V[a] + V[b]) * 0.5;
            Crease.TryGetValue(k, out double s);
            Vec3 p;
            if (faces.Count != 2 || s >= 1) p = mid;
            else
            {
                var smooth = (V[a] + V[b] + fp[faces[0]] + fp[faces[1]]) * 0.25;
                p = s > 0 ? Vec3.Lerp(smooth, mid, s) : smooth;
            }
            edgeIndex[k] = nv + nf + edgePt.Count;
            edgePt.Add(p);
        }

        // updated original vertices
        var nvp = new Vec3[nv];
        for (int v = 0; v < nv; v++)
        {
            var vf = topo.VertFaces[v];
            if (vf.Count == 0) { nvp[v] = V[v]; continue; }
            // incident edges
            var nbrs = new HashSet<int>();
            foreach (var fi in vf) { var f = F[fi]; int i = Array.IndexOf(f, v); nbrs.Add(f[(i + 1) % f.Length]); nbrs.Add(f[(i + f.Length - 1) % f.Length]); }
            var sharp = new List<(int other, double s)>();
            foreach (var o in nbrs)
            {
                var k = Key(v, o);
                bool boundary = topo.EdgeFaces[k].Count != 2;
                Crease.TryGetValue(k, out double s);
                if (boundary) s = Math.Max(s, 1);
                if (s > 0) sharp.Add((o, s));
            }
            int n = nbrs.Count;
            Vec3 smoothP()
            {
                Vec3 F2 = Vec3.Zero, R = Vec3.Zero;
                foreach (var fi in vf) F2 += fp[fi];
                F2 /= vf.Count;
                foreach (var o in nbrs) R += (V[v] + V[o]) * 0.5;
                R /= n;
                return (F2 + R * 2 + V[v] * (n - 3)) / n;
            }
            Crease.TryGetValue(Key(v, v), out double cornerS);                       // a crease on (v,v) is a sharp corner point
            if (cornerS >= 1) nvp[v] = V[v];
            else if (sharp.Count >= 3) nvp[v] = V[v];
            else if (sharp.Count == 2)
            {
                var creaseP = V[v] * 0.75 + (V[sharp[0].other] + V[sharp[1].other]) * 0.125;
                double s = Math.Min(sharp[0].s, sharp[1].s);
                nvp[v] = s >= 1 ? creaseP : Vec3.Lerp(smoothP(), creaseP, s);
            }
            else nvp[v] = smoothP();
            if (cornerS > 0 && cornerS < 1) nvp[v] = Vec3.Lerp(nvp[v], V[v], cornerS);
        }

        r.V.AddRange(nvp);
        r.V.AddRange(fp);
        r.V.AddRange(edgePt);

        var par = new List<int>();
        for (int fi = 0; fi < nf; fi++)
        {
            var f = F[fi]; int n = f.Length;
            for (int i = 0; i < n; i++)
            {
                int v = f[i];
                int eNext = edgeIndex[Key(v, f[(i + 1) % n])];
                int ePrev = edgeIndex[Key(f[(i + n - 1) % n], v)];
                r.F.Add([v, eNext, nv + fi, ePrev]);
                par.Add(fi);
            }
        }
        // crease sharpness propagates to the two halves of each edge, reduced by one level
        foreach (var (k, s) in Crease)
        {
            if (s <= 1e-9) continue;
            var (ka, kb) = Unkey(k);
            if (ka == kb) { if (s - 1 > 0) r.Crease[k] = s - 1; continue; }                // corner point: same vertex number in the child
            if (!edgeIndex.TryGetValue(k, out int e)) continue;
            double ns = s - 1;
            if (ns <= 0) continue;
            var (a, b) = Unkey(k);
            r.Crease[Key(a, e)] = ns; r.Crease[Key(e, b)] = ns;
        }
        parent = par.ToArray();
        // frozen regions carry over: the pieces of every frozen face stay frozen, and so do frozen single points (which keep their number)
        if (Frozen.Count > 0)
        {
            for (int v = 0; v < nv; v++) if (IsFrozen(v)) r.Freeze([v]);
            var frozenFace = new bool[nf];
            for (int i = 0; i < nf; i++) frozenFace[i] = FaceFrozen(i);
            for (int c = 0; c < r.F.Count; c++) if (frozenFace[par[c]]) r.Freeze(r.F[c]);
        }
        return r;
    }

    /// <summary>Take over the shape (points, faces, creases and frozen points) of another cage, keeping this cage's smoothness level.</summary>
    public void ReplaceWith(SubdMesh r)
    {
        V.Clear(); V.AddRange(r.V); F.Clear(); F.AddRange(r.F); Sym = r.Sym;
        Crease.Clear(); foreach (var kv in r.Crease) Crease[kv.Key] = kv.Value;
        Frozen.Clear(); foreach (var p in r.Frozen) Frozen.Add(p);
    }

    // ---------------------------------------------------------------- display tessellation
    /// <summary>Smooth shaded triangles of the limit-ish surface; triangles are grouped by control face so selection can address them.</summary>
    public MeshData BuildMesh(int? level = null)
    {
        int lv = Math.Clamp(level ?? Level, 1, 4);
        // Sharpness is consumed during refinement. Remember which original face seams
        // are still sharp at this display level so shading does not erase the crease.
        var hardFacePairs = new HashSet<long>();
        if (Crease.Any(c => c.Value >= lv))
        {
            var originalTopology = Topo();
            foreach (var (edge, sharpness) in Crease)
                if (sharpness >= lv && originalTopology.EdgeFaces.TryGetValue(edge, out var faces) && faces.Count == 2)
                    hardFacePairs.Add(Key(faces[0], faces[1]));
        }
        var m = this; int[] cum = Enumerable.Range(0, F.Count).ToArray();
        for (int i = 0; i < lv; i++)
        {
            var next = m.Refine(out var par);
            cum = par.Select(p => cum[p]).ToArray();
            m = next;
        }
        return m.ToTriangles(cum, F.Count, hardFacePairs);
    }

    MeshData ToTriangles(int[] parentOfFace, int controlFaces, HashSet<long> hardFacePairs)
    {
        // normals per corner, smoothing only across faces whose normals are within ~40 degrees
        var faceN = new Vec3[F.Count];
        for (int i = 0; i < F.Count; i++) faceN[i] = FaceNormal(i);
        var topo = Topo();
        var smoothingGroups = new Dictionary<int, int>?[V.Count];
        if (hardFacePairs.Count > 0)
        {
            var barriers = topo.EdgeFaces.Where(pair => pair.Value.Count == 2 &&
                hardFacePairs.Contains(Key(parentOfFace[pair.Value[0]], parentOfFace[pair.Value[1]]))).Select(pair => pair.Key).ToHashSet();
            foreach (int vertex in barriers.SelectMany(edge => { var (a, b) = Unkey(edge); return new[] { a, b }; }).Distinct())
            {
                var groups = new Dictionary<int, int>();
                foreach (int seed in topo.VertFaces[vertex])
                {
                    if (groups.ContainsKey(seed)) continue;
                    var queue = new Queue<int>(); groups[seed] = seed; queue.Enqueue(seed);
                    while (queue.TryDequeue(out int face))
                    {
                        var corners = F[face]; int at = Array.IndexOf(corners, vertex);
                        foreach (int neighbor in new[] { corners[(at + 1) % corners.Length], corners[(at + corners.Length - 1) % corners.Length] })
                        {
                            long edge = Key(vertex, neighbor);
                            if (barriers.Contains(edge)) continue;
                            foreach (int next in topo.EdgeFaces[edge])
                                if (groups.TryAdd(next, seed)) queue.Enqueue(next);
                        }
                    }
                }
                smoothingGroups[vertex] = groups;
            }
        }
        int nq = F.Count;
        var pos = new List<float>(nq * 12); var nrm = new List<float>(nq * 12); var idx = new List<int>(nq * 6);
        var triFace = new List<int>(nq * 2);
        double cosT = Math.Cos(40 * Math.PI / 180);
        var (mn, mx) = Bounds();
        // emit in parent-face order (already the case)
        for (int fi = 0; fi < F.Count; fi++)
        {
            var f = F[fi];
            int baseIdx = pos.Count / 3;
            for (int c = 0; c < f.Length; c++)
            {
                var p = V[f[c]];
                Vec3 n = Vec3.Zero;
                var group = smoothingGroups[f[c]];
                foreach (var g in topo.VertFaces[f[c]])
                    if ((group == null || group[g] == group[fi]) && Vec3.Dot(faceN[g], faceN[fi]) >= cosT) n += faceN[g];
                n = n.Normalized();
                pos.Add((float)p.X); pos.Add((float)p.Y); pos.Add((float)p.Z);
                nrm.Add((float)n.X); nrm.Add((float)n.Y); nrm.Add((float)n.Z);
            }
            for (int c = 1; c + 1 < f.Length; c++) { idx.Add(baseIdx); idx.Add(baseIdx + c); idx.Add(baseIdx + c + 1); triFace.Add(parentOfFace[fi]); }
        }
        var d = new MeshData(pos.Count / 3, idx.Count / 3, 0)
        {
            Min = mn, Max = mx, FaceCount = controlFaces, EdgeCount = 0,
        };
        pos.CopyTo(d.Positions); nrm.CopyTo(d.Normals); idx.CopyTo(d.Indices); triFace.CopyTo(d.TriFace);
        var starts = new int[controlFaces + 1];
        int t = 0;
        for (int k = 0; k <= controlFaces; k++) { while (t < triFace.Count && triFace[t] < k) t++; starts[k] = t; }
        d.FaceTriStart = starts;
        d.EdgeSegStart = new int[1];
        return d;
    }

    // ---------------------------------------------------------------- housekeeping
    public void RemoveUnusedVertices()
    {
        var used = new bool[V.Count];
        foreach (var f in F) foreach (var v in f) used[v] = true;
        var map = new int[V.Count]; var nv = new List<Vec3>();
        for (int i = 0; i < V.Count; i++) if (used[i]) { map[i] = nv.Count; nv.Add(V[i]); }
        for (int i = 0; i < F.Count; i++) F[i] = F[i].Select(v => map[v]).ToArray();
        var nc = new Dictionary<long, double>();
        foreach (var (k, s) in Crease) { var (a, b) = Unkey(k); if (used[a] && used[b]) nc[Key(map[a], map[b])] = s; }
        V.Clear(); V.AddRange(nv);
        Crease.Clear(); foreach (var kv in nc) Crease[kv.Key] = kv.Value;
    }

    /// <summary>Merges vertices closer than tol and drops faces that collapse.</summary>
    public void WeldByDistance(double tol)
    {
        var map = new int[V.Count]; var keep = new List<Vec3>();
        var grid = new Dictionary<(long, long, long), List<int>>();
        (long, long, long) Cell(Vec3 p) => ((long)Math.Floor(p.X / tol), (long)Math.Floor(p.Y / tol), (long)Math.Floor(p.Z / tol));
        for (int i = 0; i < V.Count; i++)
        {
            var c = Cell(V[i]); int found = -1;
            for (int dx = -1; dx <= 1 && found < 0; dx++)
                for (int dy = -1; dy <= 1 && found < 0; dy++)
                    for (int dz = -1; dz <= 1 && found < 0; dz++)
                        if (grid.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out var l))
                            foreach (var j in l) if (keep[j].DistanceTo(V[i]) <= tol) { found = j; break; }
            if (found < 0) { found = keep.Count; keep.Add(V[i]); if (!grid.TryGetValue(c, out var l2)) grid[c] = l2 = []; l2.Add(found); }
            map[i] = found;
        }
        Remap(map, keep);
    }

    /// <summary>Merge the given vertices into their centroid.</summary>
    public void Weld(IEnumerable<int> verts)
    {
        var set = verts.Distinct().ToList();
        if (set.Count < 2) return;
        var c = set.Aggregate(Vec3.Zero, (s, i) => s + V[i]) / set.Count;
        int target = set[0];
        var map = Enumerable.Range(0, V.Count).ToArray();
        foreach (var v in set) map[v] = target;
        var nv = new List<Vec3>(V); nv[target] = c;
        Remap(map, nv);
    }

    void Remap(int[] map, List<Vec3> verts)
    {
        var nf = new List<int[]>();
        foreach (var f in F)
        {
            var g = new List<int>();
            foreach (var v in f) { int m = map[v]; if (g.Count == 0 || g[^1] != m) g.Add(m); }
            while (g.Count > 1 && g[0] == g[^1]) g.RemoveAt(g.Count - 1);
            if (g.Distinct().Count() >= 3 && g.Distinct().Count() == g.Count) nf.Add(g.ToArray());
        }
        var nc = new Dictionary<long, double>();
        foreach (var (k, s) in Crease) { var (a, b) = Unkey(k); if (a == b || map[a] != map[b]) nc[Key(map[a], map[b])] = s; }
        V.Clear(); V.AddRange(verts);
        F.Clear(); F.AddRange(nf);
        Crease.Clear(); foreach (var kv in nc) Crease[kv.Key] = kv.Value;
        RemoveUnusedVertices();
    }
}
