using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>Settings for <see cref="SurfaceReconstruction"/>.</summary>
public sealed class ReconstructOptions
{
    /// <summary>Grid cells along the longest side are about 2^Depth (used when <see cref="VoxelSize"/> is 0).</summary>
    public int Depth { get; set; } = 7;
    /// <summary>Grid cell size in mm; 0 derives it from <see cref="Depth"/>.</summary>
    public double VoxelSize { get; set; }
    /// <summary>Multiplier on the automatic smoothing radius (1 = default; larger removes more noise but rounds corners).</summary>
    public double Smoothing { get; set; } = 1.0;
    /// <summary>0..1: surface where the data is thinner than this fraction of the typical density is removed (0 keeps everything).</summary>
    public double Trim { get; set; } = 0.1;
    /// <summary>Close boundary loops of up to this many edges (0 = leave holes open).</summary>
    public int FillHoleEdges { get; set; }
    /// <summary>Remove floating shells smaller than this many triangles.</summary>
    public int MinShellTriangles { get; set; } = 30;
}

/// <summary>
/// Surface reconstruction from an oriented point cloud: a Hoppe-style signed field (Gaussian-weighted average of the distances to the tangent planes
/// of nearby points) is sampled on a uniform grid, one layer at a time, and its zero set is extracted with marching tetrahedra
/// (six tetrahedra per cell, a consistent split so the result is crack-free and manifold). Density trimming removes surface far from the data.
/// </summary>
public static class SurfaceReconstruction
{
    struct FieldVisitor : IPointVisitor
    {
        public double[] N; public double Inv2S2, SumW, SumWF;
        public void Visit(int q, double dx, double dy, double dz, double d2)
        {
            double w = Math.Exp(-d2 * Inv2S2);
            // (x - p) . n, where dx = p - x
            SumW += w; SumWF -= w * (dx * N[3 * q] + dy * N[3 * q + 1] + dz * N[3 * q + 2]);
        }
    }

    /// <summary>Typical distance between neighbouring points.</summary>
    public static double EstimateSpacing(PointCloud c, int samples = 4000)
    {
        int n = c.Count; if (n < 5) return 1;
        var grid = new PointGrid(c.P); var kn = new Knn(4); var d = new List<double>();
        int step = Math.Max(1, n / samples);
        for (int i = 0; i < n; i += step) { grid.Nearest(c.P[3 * i], c.P[3 * i + 1], c.P[3 * i + 2], 4, kn); if (kn.Found == 4) d.Add(Math.Sqrt(kn.D2[3])); }
        if (d.Count == 0) return 1;
        d.Sort(); return Math.Max(d[d.Count / 2] * 0.7, 1e-9);
    }

    static readonly int[][] Tets = [[0, 1, 3, 7], [0, 1, 5, 7], [0, 2, 3, 7], [0, 2, 6, 7], [0, 4, 5, 7], [0, 4, 6, 7]];

    readonly record struct EV(long Key, float X, float Y, float Z);

    public static TriMesh Reconstruct(PointCloud cloud, ReconstructOptions? opt = null, Action<string>? progress = null, CancellationToken ct = default)
    {
        opt ??= new ReconstructOptions();
        if (cloud.N == null) throw new InvalidOperationException("The point cloud needs normals. Estimate them first.");
        int n = cloud.Count; if (n < 10) throw new InvalidOperationException("Too few points to build a surface.");
        var (bmin, bmax) = cloud.Bounds();
        var size = bmax - bmin; double side = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (side < 1e-9) throw new InvalidOperationException("All points are in the same place.");
        var pr = new double[cloud.P.Length];
        for (int i = 0; i < n; i++) { pr[3 * i] = cloud.P[3 * i] - bmin.X; pr[3 * i + 1] = cloud.P[3 * i + 1] - bmin.Y; pr[3 * i + 2] = cloud.P[3 * i + 2] - bmin.Z; }
        var nr = cloud.N;
        progress?.Invoke("Measuring point spacing");
        double spacing = EstimateSpacing(new PointCloud(pr));
        double voxel = opt.VoxelSize > 0 ? opt.VoxelSize : side / Math.Pow(2, Math.Clamp(opt.Depth, 3, 10));
        voxel = Math.Max(voxel, side / 700);
        double sigma = Math.Max(voxel * 1.25, spacing * 1.6) * Math.Max(0.2, opt.Smoothing);
        double R = sigma * 2.6, inv2s2 = 1 / (2 * sigma * sigma);
        var grid = new PointGrid(pr, R);

        var dens = new List<double>(); int st = Math.Max(1, n / 3000);
        for (int i = 0; i < n; i += st)
        {
            var v = new FieldVisitor { N = nr, Inv2S2 = inv2s2 };
            grid.Visit(pr[3 * i], pr[3 * i + 1], pr[3 * i + 2], R, ref v); dens.Add(v.SumW);
        }
        dens.Sort(); double dref = dens[dens.Count / 2];
        double dMin = 0.02 * dref, dTrim = Math.Clamp(opt.Trim, 0, 0.95) * dref;

        double margin = voxel * 2;
        int nx = (int)Math.Ceiling((size.X + 2 * margin) / voxel) + 1, ny = (int)Math.Ceiling((size.Y + 2 * margin) / voxel) + 1, nz = (int)Math.Ceiling((size.Z + 2 * margin) / voxel) + 1;
        if ((long)nx * ny * nz > 1_500_000_000L) throw new InvalidOperationException("The grid would be too large. Use a coarser detail level.");

        float[] vLo = new float[nx * ny], dLo = new float[nx * ny], vHi = new float[nx * ny], dHi = new float[nx * ny];
        void Layer(int k, float[] val, float[] den)
        {
            double z = k * voxel - margin;
            Parallel.For(0, ny, new ParallelOptions { CancellationToken = ct }, j =>
            {
                double y = j * voxel - margin;
                for (int i = 0; i < nx; i++)
                {
                    double x = i * voxel - margin;
                    var v = new FieldVisitor { N = nr, Inv2S2 = inv2s2 };
                    grid.Visit(x, y, z, R, ref v);
                    int o = i + nx * j;
                    if (v.SumW < dMin) { val[o] = float.NaN; den[o] = 0; continue; }
                    double f = v.SumWF / v.SumW;
                    if (Math.Abs(f) < 1e-7 * voxel) f = f < 0 ? -1e-7 * voxel : 1e-7 * voxel;
                    val[o] = (float)f; den[o] = (float)v.SumW;
                }
            });
        }

        var verts = new List<float>(); var tris = new List<int>();
        var dicts = new[] { new Dictionary<long, int>(), new Dictionary<long, int>() };
        progress?.Invoke("Sampling the field");
        Layer(0, vLo, dLo);
        var rowOut = new List<EV>?[ny];
        long layerSize = (long)nx * ny;
        for (int k = 0; k + 1 < nz; k++)
        {
            ct.ThrowIfCancellationRequested();
            if (k % 8 == 0) progress?.Invoke($"Extracting surface ({100 * k / nz}%)");
            Layer(k + 1, vHi, dHi);
            var lo = vLo; var hi = vHi; var dl = dLo; var dh = dHi; int kk = k;
            Parallel.For(0, ny - 1, new ParallelOptions { CancellationToken = ct }, j =>
            {
                List<EV>? outp = null;
                var f = new float[8]; var cx = new double[8]; var cy = new double[8]; var cz = new double[8];
                var neg = new int[4]; var pos = new int[4];
                for (int i = 0; i < nx - 1; i++)
                {
                    bool bad = false, anyNeg = false, anyPos = false; double minDen = double.MaxValue;
                    for (int c = 0; c < 8; c++)
                    {
                        int o = (i + (c & 1)) + nx * (j + ((c >> 1) & 1));
                        float val = (c & 4) == 0 ? lo[o] : hi[o], d = (c & 4) == 0 ? dl[o] : dh[o];
                        if (float.IsNaN(val)) { bad = true; break; }
                        f[c] = val; if (val < 0) anyNeg = true; else anyPos = true; minDen = Math.Min(minDen, d);
                    }
                    if (bad || !anyNeg || !anyPos || minDen < dTrim) continue;
                    for (int c = 0; c < 8; c++) { cx[c] = (i + (c & 1)) * voxel - margin; cy[c] = (j + ((c >> 1) & 1)) * voxel - margin; cz[c] = (kk + ((c >> 2) & 1)) * voxel - margin; }
                    foreach (var tet in Tets)
                    {
                        int nn = 0, np = 0;
                        foreach (int c in tet) if (f[c] < 0) neg[nn++] = c; else pos[np++] = c;
                        if (nn == 0 || np == 0) continue;
                        EV Edge(int a, int b)
                        {
                            if (a > b) (a, b) = (b, a);
                            double t = f[a] / (f[a] - f[b]);
                            long node = (i + (a & 1)) + (long)nx * (j + ((a >> 1) & 1)) + layerSize * (kk + ((a >> 2) & 1));
                            return new EV(node * 8 + (a ^ b), (float)(cx[a] + t * (cx[b] - cx[a])), (float)(cy[a] + t * (cy[b] - cy[a])), (float)(cz[a] + t * (cz[b] - cz[a])));
                        }
                        double ox = 0, oy = 0, oz = 0;
                        for (int q = 0; q < np; q++) { ox += cx[pos[q]] / np; oy += cy[pos[q]] / np; oz += cz[pos[q]] / np; }
                        for (int q = 0; q < nn; q++) { ox -= cx[neg[q]] / nn; oy -= cy[neg[q]] / nn; oz -= cz[neg[q]] / nn; }
                        outp ??= [];
                        void Emit(EV a, EV b, EV c3)
                        {
                            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c3.X - a.X, vy = c3.Y - a.Y, vz = c3.Z - a.Z;
                            double nxx = uy * vz - uz * vy, nyy = uz * vx - ux * vz, nzz = ux * vy - uy * vx;
                            if (nxx * ox + nyy * oy + nzz * oz >= 0) { outp.Add(a); outp.Add(b); outp.Add(c3); } else { outp.Add(a); outp.Add(c3); outp.Add(b); }
                        }
                        if (nn == 1) Emit(Edge(neg[0], pos[0]), Edge(neg[0], pos[1]), Edge(neg[0], pos[2]));
                        else if (np == 1) Emit(Edge(pos[0], neg[0]), Edge(pos[0], neg[1]), Edge(pos[0], neg[2]));
                        else
                        {
                            var e00 = Edge(neg[0], pos[0]); var e01 = Edge(neg[0], pos[1]); var e11 = Edge(neg[1], pos[1]); var e10 = Edge(neg[1], pos[0]);
                            Emit(e00, e01, e11); Emit(e00, e11, e10);
                        }
                    }
                }
                rowOut[j] = outp;
            });
            var cur = dicts[k & 1]; var next = dicts[(k + 1) & 1];
            for (int j = 0; j < ny - 1; j++)
            {
                var r = rowOut[j]; if (r == null) continue;
                foreach (var e in r)
                {
                    long lz = (e.Key >> 3) / layerSize;
                    var dd = lz == k ? cur : next;
                    if (!dd.TryGetValue(e.Key, out int id)) { id = verts.Count / 3; dd[e.Key] = id; verts.Add((float)(e.X + bmin.X)); verts.Add((float)(e.Y + bmin.Y)); verts.Add((float)(e.Z + bmin.Z)); }
                    tris.Add(id);
                }
                rowOut[j] = null;
            }
            cur.Clear();                    // edges whose lower node is in layer k are finished
            (vLo, vHi) = (vHi, vLo); (dLo, dHi) = (dHi, dLo);
        }

        progress?.Invoke("Cleaning up");
        var mesh = new TriMesh([.. verts], [.. tris]);
        if (mesh.TriCount == 0) throw new InvalidOperationException("No surface was found. Try a lower trim value or check the normals.");
        if (opt.MinShellTriangles > 1) mesh = MeshOps.RemoveSmallShells(mesh, opt.MinShellTriangles, 0.02).mesh;
        if (opt.FillHoleEdges > 0)
        {
            var filled = MeshOps.FillHoles(mesh, opt.FillHoleEdges);
            if (filled.filled > 0) mesh = MeshOps.FixOrientation(filled.mesh).mesh;
        }
        return mesh;
    }

    /// <summary>The whole pipeline: optional outlier removal and downsampling, normals (estimated and oriented unless the file has them), reconstruction.</summary>
    public static TriMesh FromCloud(PointCloud cloud, double downsample, bool removeOutliers, int normalNeighbours, bool useFileNormals, Vec3? viewpoint, ReconstructOptions opt, Action<string>? progress = null, CancellationToken ct = default)
    {
        var c = cloud;
        if (removeOutliers) { progress?.Invoke("Removing outliers"); c = c.RemoveOutliers(12, 2.5, ct); }
        if (downsample > 0) { progress?.Invoke("Downsampling"); c = c.VoxelDownsample(downsample); }
        if (!(useFileNormals && c.HasNormals))
        {
            progress?.Invoke("Estimating normals"); var nrm = c.EstimateNormals(normalNeighbours, ct);
            progress?.Invoke("Orienting normals"); PointCloud.OrientNormals(c, nrm, Math.Min(normalNeighbours, 10), viewpoint, null, ct);
            c = new PointCloud(c.P, nrm, c.C);
        }
        progress?.Invoke("Building the surface");
        return Reconstruct(c, opt, progress, ct);
    }
}
