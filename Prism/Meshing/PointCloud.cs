using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>An unstructured set of 3D points (laser scan, photogrammetry) with optional normals and colours. Positions are millimetres.</summary>
public sealed class PointCloud
{
    /// <summary>Packed xyz.</summary>
    public double[] P { get; }
    /// <summary>Packed unit normals (same count as points) or null.</summary>
    public double[]? N { get; set; }
    /// <summary>Packed RGB bytes (3 per point) or null.</summary>
    public byte[]? C { get; set; }
    public int Count => P.Length / 3;
    public bool HasNormals => N != null;
    public bool HasColors => C != null;

    public PointCloud(double[] p, double[]? n = null, byte[]? c = null)
    {
        P = p; N = n; C = c;
        if (n != null && n.Length != p.Length) throw new ArgumentException("Normals must match the points.");
        if (c != null && c.Length != p.Length) throw new ArgumentException("Colours must match the points.");
    }

    public Vec3 Point(int i) => new(P[3 * i], P[3 * i + 1], P[3 * i + 2]);
    public Vec3 Normal(int i) => N == null ? Vec3.Zero : new(N[3 * i], N[3 * i + 1], N[3 * i + 2]);

    public (Vec3 min, Vec3 max) Bounds()
    {
        if (Count == 0) return (Vec3.Zero, Vec3.Zero);
        double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1;
        for (int i = 0; i < P.Length; i += 3)
        {
            x0 = Math.Min(x0, P[i]); x1 = Math.Max(x1, P[i]); y0 = Math.Min(y0, P[i + 1]); y1 = Math.Max(y1, P[i + 1]); z0 = Math.Min(z0, P[i + 2]); z1 = Math.Max(z1, P[i + 2]);
        }
        return (new(x0, y0, z0), new(x1, y1, z1));
    }

    /// <summary>A new cloud made of the listed points.</summary>
    public PointCloud Subset(IReadOnlyList<int> idx)
    {
        var p = new double[idx.Count * 3]; var n = N == null ? null : new double[idx.Count * 3]; var c = C == null ? null : new byte[idx.Count * 3];
        for (int k = 0; k < idx.Count; k++)
        {
            int i = idx[k];
            for (int a = 0; a < 3; a++) { p[3 * k + a] = P[3 * i + a]; if (n != null) n[3 * k + a] = N![3 * i + a]; if (c != null) c[3 * k + a] = C![3 * i + a]; }
        }
        return new PointCloud(p, n, c);
    }

    // ================================================================= loading
    public static readonly string[] ReadPatterns = ["*.xyz", "*.pts", "*.xyzn", "*.txt", "*.csv", "*.ply"];
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>True for extensions that are always treated as point clouds (PLY depends on its content, see <see cref="PlyHasNoFaces"/>).</summary>
    public static bool IsPointCloudExtension(string path) => Path.GetExtension(path).ToLowerInvariant() is ".xyz" or ".pts" or ".xyzn" or ".csv" or ".txt";

    /// <summary>Load XYZ / PTS / CSV-ish text or a PLY that has vertices only.</summary>
    public static PointCloud Load(string path)
    {
        if (Path.GetExtension(path).Equals(".ply", StringComparison.OrdinalIgnoreCase)) return LoadPly(File.ReadAllBytes(path));
        return LoadText(File.ReadLines(path), Path.GetExtension(path));
    }

    static bool IsSep(char c) => c is ' ' or '\t' or ',' or ';' or '\r';

    static int Split(ReadOnlySpan<char> s, Span<Range> toks)
    {
        int n = 0, i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && IsSep(s[i])) i++;
            if (i >= s.Length) break;
            int st = i; while (i < s.Length && !IsSep(s[i])) i++;
            if (n < toks.Length) toks[n] = new Range(st, i);
            n++;
        }
        return Math.Min(n, toks.Length);
    }

    /// <summary>Tolerant text parser: any of space, tab, comma or semicolon separate values; comment lines, a PTS point-count line and a header with column names are understood.</summary>
    public static PointCloud LoadText(IEnumerable<string> lines, string ext = ".xyz")
    {
        ext = ext.ToLowerInvariant();
        var data = new List<double>(1 << 16);
        int cols = 0, rows = 0; string[]? header = null;
        Span<Range> toks = stackalloc Range[32]; Span<double> vals = stackalloc double[32];
        foreach (var raw in lines)
        {
            var s = raw.AsSpan().Trim();
            if (s.Length == 0) continue;
            bool comment = s[0] == '#' || s.StartsWith("//") || s[0] == '%';
            if (comment)
            {
                if (header == null && rows == 0) header = TryHeader(s.TrimStart("#/% ".AsSpan()));
                continue;
            }
            int nt = Split(s, toks);
            if (nt == 0) continue;
            // numeric?
            bool allNum = true;
            for (int k = 0; k < nt; k++)
                if (!double.TryParse(s[toks[k]], NumberStyles.Float, Inv, out vals[k])) { allNum = false; break; }
            if (!allNum)
            {
                if (rows == 0 && header == null) header = TryHeader(s);
                continue;                                  // text junk line
            }
            if (nt < 3) continue;                          // PTS point count / scan separators
            if (cols == 0) cols = nt;
            if (nt < cols) continue;
            for (int k = 0; k < cols; k++) data.Add(vals[k]);
            rows++;
        }
        if (rows == 0) throw new InvalidDataException("No points found in that file.");
        return FromTable(data.ToArray(), rows, cols, header, ext);
    }

    static ReadOnlySpan<double> CollectionsMarshalAsSpan(List<double> l) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(l);

    static string[]? TryHeader(ReadOnlySpan<char> s)
    {
        var t = new List<string>(); Span<Range> toks = stackalloc Range[64]; int n = Split(s, toks);
        for (int i = 0; i < n; i++) t.Add(s[toks[i]].ToString().Trim('"', '\'').ToLowerInvariant());
        return t.Contains("x") && t.Contains("y") && t.Contains("z") || t.Any(x => x is "//x" or "x[mm]") ? [.. t] : null;
    }

    static PointCloud FromTable(double[] d, int rows, int cols, string[]? header, string ext)
    {
        int ix = 0, iy = 1, iz = 2, inx = -1;
        bool Unit(int c0)
        {
            if (c0 + 2 >= cols) return false;
            int step = Math.Max(1, rows / 400), good = 0, tot = 0;
            for (int r = 0; r < rows; r += step)
            {
                double a = d[r * cols + c0], b = d[r * cols + c0 + 1], c = d[r * cols + c0 + 2], l = Math.Sqrt(a * a + b * b + c * c);
                tot++; if (Math.Abs(l - 1) < 0.02) good++;
            }
            return good >= 0.9 * tot;
        }
        int[]? rgbIdx = null;
        if (header != null && header.Length >= 3)
        {
            int Find(params string[] names) { for (int i = 0; i < header.Length && i < cols; i++) if (names.Contains(header[i].TrimStart('/'))) return i; return -1; }
            int hx = Find("x", "x[mm]"), hy = Find("y", "y[mm]"), hz = Find("z", "z[mm]");
            if (hx >= 0 && hy >= 0 && hz >= 0) { ix = hx; iy = hy; iz = hz; }
            int nx = Find("nx", "normal_x", "normx", "n_x"), ny = Find("ny", "normal_y", "normy", "n_y"), nz = Find("nz", "normal_z", "normz", "n_z");
            if (nx >= 0 && ny >= 0 && nz >= 0) inx = nx;      // assumed contiguous below when packed
            int rr = Find("r", "red", "diffuse_red"), gg = Find("g", "green", "diffuse_green"), bb = Find("b", "blue", "diffuse_blue");
            if (rr >= 0 && gg >= 0 && bb >= 0) rgbIdx = [rr, gg, bb];
            if (inx >= 0) { var ni = new[] { nx, ny, nz }; return Build(d, rows, cols, [ix, iy, iz], ni, rgbIdx); }
            if (rgbIdx != null) return Build(d, rows, cols, [ix, iy, iz], null, rgbIdx);
        }
        int[]? nIdx = null;
        switch (cols)
        {
            case 3: case 4: case 5: break;
            case 6: if (Unit(3)) nIdx = [3, 4, 5]; else rgbIdx = [3, 4, 5]; break;
            case 7: rgbIdx = [4, 5, 6]; break;                                      // PTS: x y z intensity r g b
            case 8: if (Unit(3)) nIdx = [3, 4, 5]; break;
            default:
                if (Unit(3)) { nIdx = [3, 4, 5]; if (cols >= 9) rgbIdx = [6, 7, 8]; }
                else if (cols >= 9 && Unit(6)) { nIdx = [6, 7, 8]; rgbIdx = [3, 4, 5]; }
                else if (cols >= 7 && ext == ".pts") rgbIdx = [4, 5, 6];
                else rgbIdx = [3, 4, 5];
                break;
        }
        return Build(d, rows, cols, [ix, iy, iz], nIdx, rgbIdx);
    }

    static PointCloud Build(double[] d, int rows, int cols, int[] xyz, int[]? nIdx, int[]? rgb)
    {
        var p = new double[rows * 3]; var n = nIdx == null ? null : new double[rows * 3]; byte[]? c = rgb == null ? null : new byte[rows * 3];
        double maxC = 0;
        if (rgb != null) for (int r = 0; r < rows; r++) for (int a = 0; a < 3; a++) maxC = Math.Max(maxC, d[r * cols + rgb[a]]);
        double cs = maxC <= 1.0 ? 255 : 1;
        for (int r = 0; r < rows; r++)
        {
            for (int a = 0; a < 3; a++)
            {
                p[3 * r + a] = d[r * cols + xyz[a]];
                if (n != null) n[3 * r + a] = d[r * cols + nIdx![a]];
                if (c != null) c[3 * r + a] = (byte)Math.Clamp(Math.Round(d[r * cols + rgb![a]] * cs), 0, 255);
            }
            if (n != null) { var l = Math.Sqrt(n[3 * r] * n[3 * r] + n[3 * r + 1] * n[3 * r + 1] + n[3 * r + 2] * n[3 * r + 2]); if (l > 1e-12) { n[3 * r] /= l; n[3 * r + 1] /= l; n[3 * r + 2] /= l; } }
        }
        return new PointCloud(p, n, c);
    }

    // ---------------------------------------------------------------- PLY
    sealed record PlyProp(string Name, string Type, string? CountType);
    sealed record PlyElem(string Name, long Count, List<PlyProp> Props);

    static (string format, List<PlyElem> elems, int dataStart) ReadPlyHeader(ReadOnlySpan<byte> data)
    {
        int pos = 0; string format = "ascii"; var elems = new List<PlyElem>();
        bool first = true;
        while (pos < data.Length)
        {
            int e = pos; while (e < data.Length && data[e] != '\n') e++;
            var line = Encoding.ASCII.GetString(data[pos..e]).Trim(); pos = Math.Min(data.Length, e + 1);
            if (first) { if (line != "ply") throw new InvalidDataException("Not a PLY file."); first = false; continue; }
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            switch (t[0])
            {
                case "format": format = t[1]; break;
                case "element": elems.Add(new PlyElem(t[1], long.Parse(t[2], Inv), [])); break;
                case "property":
                    if (elems.Count == 0) break;
                    if (t[1] == "list") elems[^1].Props.Add(new PlyProp(t[4], t[3], t[2])); else elems[^1].Props.Add(new PlyProp(t[2], t[1], null));
                    break;
                case "end_header": return (format, elems, pos);
            }
        }
        throw new InvalidDataException("PLY header is not terminated.");
    }

    /// <summary>True when a PLY file has vertices but no faces, i.e. it is a point cloud. Reads only the header.</summary>
    public static bool PlyHasNoFaces(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[Math.Min(fs.Length, 1 << 16)]; int n = fs.Read(buf, 0, buf.Length);
            var (_, elems, _) = ReadPlyHeader(buf.AsSpan(0, n));
            var v = elems.FirstOrDefault(e => e.Name == "vertex"); var f = elems.FirstOrDefault(e => e.Name == "face");
            return v != null && v.Count > 0 && (f == null || f.Count == 0);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or IndexOutOfRangeException) { return false; }
    }

    static int PlySize(string t) => t switch { "char" or "uchar" or "int8" or "uint8" => 1, "short" or "ushort" or "int16" or "uint16" => 2, "int" or "uint" or "float" or "int32" or "uint32" or "float32" => 4, "double" or "float64" => 8, _ => throw new InvalidDataException("Unknown PLY type " + t) };

    static double PlyRead(ReadOnlySpan<byte> d, ref int pos, string type, bool big)
    {
        int sz = PlySize(type); Span<byte> b = stackalloc byte[8]; d.Slice(pos, sz).CopyTo(b); pos += sz;
        if (big) b[..sz].Reverse();
        return type switch
        {
            "char" or "int8" => (sbyte)b[0], "uchar" or "uint8" => b[0],
            "short" or "int16" => BitConverter.ToInt16(b), "ushort" or "uint16" => BitConverter.ToUInt16(b),
            "int" or "int32" => BitConverter.ToInt32(b), "uint" or "uint32" => BitConverter.ToUInt32(b),
            "float" or "float32" => BitConverter.ToSingle(b), _ => BitConverter.ToDouble(b),
        };
    }

    /// <summary>Read the vertices of a PLY (ASCII or binary) with optional normals and colours; faces, if any, are ignored.</summary>
    public static PointCloud LoadPly(byte[] data)
    {
        var (format, elems, start) = ReadPlyHeader(data);
        bool ascii = format == "ascii", big = format == "binary_big_endian";
        string[] toks = []; int tokPos = 0; int pos = start;
        if (ascii)
        {
            var text = Encoding.ASCII.GetString(data, start, data.Length - start);
            toks = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        }
        double Next(string type)
        {
            if (ascii) return double.Parse(toks[tokPos++], NumberStyles.Float, Inv);
            return PlyRead(data, ref pos, type, big);
        }
        double[]? P = null, N = null; byte[]? C = null;
        foreach (var el in elems)
        {
            bool isVertex = el.Name == "vertex";
            int n = (int)el.Count;
            int ix = el.Props.FindIndex(p => p.Name == "x"), iy = el.Props.FindIndex(p => p.Name == "y"), iz = el.Props.FindIndex(p => p.Name == "z");
            int inx = el.Props.FindIndex(p => p.Name == "nx"), iny = el.Props.FindIndex(p => p.Name == "ny"), inz = el.Props.FindIndex(p => p.Name == "nz");
            int ir = el.Props.FindIndex(p => p.Name is "red" or "r" or "diffuse_red"), ig = el.Props.FindIndex(p => p.Name is "green" or "g" or "diffuse_green"), ib = el.Props.FindIndex(p => p.Name is "blue" or "b" or "diffuse_blue");
            if (isVertex)
            {
                if (ix < 0 || iy < 0 || iz < 0) throw new InvalidDataException("PLY vertices have no x, y, z.");
                P = new double[n * 3];
                if (inx >= 0 && iny >= 0 && inz >= 0) N = new double[n * 3];
                if (ir >= 0 && ig >= 0 && ib >= 0) C = new byte[n * 3];
            }
            var vals = new double[el.Props.Count];
            for (int r = 0; r < n; r++)
            {
                for (int k = 0; k < el.Props.Count; k++)
                {
                    var pr = el.Props[k];
                    if (pr.CountType != null)
                    {
                        int cnt = (int)Next(pr.CountType);
                        for (int q = 0; q < cnt; q++) Next(pr.Type);
                        vals[k] = 0;
                    }
                    else vals[k] = Next(pr.Type);
                }
                if (!isVertex) continue;
                P![3 * r] = vals[ix]; P[3 * r + 1] = vals[iy]; P[3 * r + 2] = vals[iz];
                if (N != null) { N[3 * r] = vals[inx]; N[3 * r + 1] = vals[iny]; N[3 * r + 2] = vals[inz]; }
                if (C != null)
                {
                    bool unitFloat = el.Props[ir].Type is "float" or "double" or "float32" or "float64";
                    double s = unitFloat ? 255 : 1;
                    C[3 * r] = (byte)Math.Clamp(vals[ir] * s, 0, 255); C[3 * r + 1] = (byte)Math.Clamp(vals[ig] * s, 0, 255); C[3 * r + 2] = (byte)Math.Clamp(vals[ib] * s, 0, 255);
                }
            }
            if (isVertex && N != null)
                for (int r = 0; r < n; r++) { double a = N[3 * r], b = N[3 * r + 1], c = N[3 * r + 2], l = Math.Sqrt(a * a + b * b + c * c); if (l > 1e-12) { N[3 * r] /= l; N[3 * r + 1] /= l; N[3 * r + 2] /= l; } }
            if (isVertex && !elems.Any(e => e != el && e.Count > 0 && elems.IndexOf(e) > elems.IndexOf(el))) break;
        }
        if (P == null || P.Length == 0) throw new InvalidDataException("That PLY file has no vertices.");
        return new PointCloud(P, N, C);
    }

    // ================================================================= cleaning
    /// <summary>Keep one averaged point per cubic cell of the given size (positions, normals and colours are averaged).</summary>
    public PointCloud VoxelDownsample(double size)
    {
        if (size <= 0 || Count == 0) return this;
        var (min, _) = Bounds();
        var map = new Dictionary<(long, long, long), int>();
        var sum = new List<double>(); var cnt = new List<int>(); var nsum = N == null ? null : new List<double>(); var csum = C == null ? null : new List<double>();
        double inv = 1.0 / size;
        for (int i = 0; i < Count; i++)
        {
            var key = ((long)Math.Floor((P[3 * i] - min.X) * inv), (long)Math.Floor((P[3 * i + 1] - min.Y) * inv), (long)Math.Floor((P[3 * i + 2] - min.Z) * inv));
            if (!map.TryGetValue(key, out int g))
            {
                g = cnt.Count; map[key] = g; cnt.Add(0);
                sum.AddRange([0, 0, 0]); nsum?.AddRange([0, 0, 0]); csum?.AddRange([0, 0, 0]);
            }
            cnt[g]++;
            for (int a = 0; a < 3; a++) { sum[3 * g + a] += P[3 * i + a]; if (nsum != null) nsum[3 * g + a] += N![3 * i + a]; if (csum != null) csum[3 * g + a] += C![3 * i + a]; }
        }
        int m = cnt.Count; var p = new double[m * 3]; var n = nsum == null ? null : new double[m * 3]; var c = csum == null ? null : new byte[m * 3];
        for (int g = 0; g < m; g++)
        {
            for (int a = 0; a < 3; a++) { p[3 * g + a] = sum[3 * g + a] / cnt[g]; if (c != null) c[3 * g + a] = (byte)Math.Round(csum![3 * g + a] / cnt[g]); }
            if (n != null)
            {
                double x = nsum![3 * g], y = nsum[3 * g + 1], z = nsum[3 * g + 2], l = Math.Sqrt(x * x + y * y + z * z);
                if (l > 1e-12) { n[3 * g] = x / l; n[3 * g + 1] = y / l; n[3 * g + 2] = z / l; }
            }
        }
        return new PointCloud(p, n, c);
    }

    /// <summary>Statistical outlier removal: drops points whose mean distance to their k neighbours exceeds the global mean by more than <paramref name="stdRatio"/> standard deviations.</summary>
    public PointCloud RemoveOutliers(int k = 12, double stdRatio = 2.0, CancellationToken ct = default)
    {
        if (Count <= k + 1) return this;
        var grid = new PointGrid(P);
        var mean = new double[Count];
        Parallel.For(0, Count, new ParallelOptions { CancellationToken = ct }, () => new Knn(k + 1), (i, _, kn) =>
        {
            grid.Nearest(P[3 * i], P[3 * i + 1], P[3 * i + 2], k + 1, kn);
            double s = 0; int c = 0; for (int j = 0; j < kn.Found; j++) if (kn.Idx[j] != i) { s += Math.Sqrt(kn.D2[j]); c++; }
            mean[i] = c > 0 ? s / c : 0; return kn;
        }, _ => { });
        double mu = mean.Average(), sd = Math.Sqrt(mean.Sum(x => (x - mu) * (x - mu)) / Count);
        double thr = mu + stdRatio * sd;
        var keep = new List<int>(Count);
        for (int i = 0; i < Count; i++) if (mean[i] <= thr) keep.Add(i);
        return keep.Count == Count ? this : Subset(keep);
    }

    // ================================================================= normals
    /// <summary>Per-point normals from PCA over the k nearest neighbours (orientation is arbitrary; call <see cref="OrientNormals"/>).</summary>
    public double[] EstimateNormals(int k = 16, CancellationToken ct = default)
    {
        var (nbr, kk) = Neighbours(k, ct);
        var nrm = new double[P.Length];
        Parallel.For(0, Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            double cx = P[3 * i], cy = P[3 * i + 1], cz = P[3 * i + 2]; int c = 1;
            for (int j = 0; j < kk; j++) { int q = nbr[i * kk + j]; if (q < 0) break; cx += P[3 * q]; cy += P[3 * q + 1]; cz += P[3 * q + 2]; c++; }
            cx /= c; cy /= c; cz /= c;
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            void Acc(int q) { double dx = P[3 * q] - cx, dy = P[3 * q + 1] - cy, dz = P[3 * q + 2] - cz; xx += dx * dx; xy += dx * dy; xz += dx * dz; yy += dy * dy; yz += dy * dz; zz += dz * dz; }
            Acc(i);
            for (int j = 0; j < kk; j++) { int q = nbr[i * kk + j]; if (q < 0) break; Acc(q); }
            var (_, vec) = MeshAnalysis.EigenSym([xx, xy, xz, yy, yz, zz]);
            var n = vec[2];
            nrm[3 * i] = n.X; nrm[3 * i + 1] = n.Y; nrm[3 * i + 2] = n.Z;
        });
        return nrm;
    }

    /// <summary>Indices of the k nearest neighbours of every point (self excluded, -1 padded), row-major n x k.</summary>
    public (int[] idx, int k) Neighbours(int k, CancellationToken ct = default)
    {
        k = Math.Clamp(k, 1, Math.Max(1, Count - 1));
        var grid = new PointGrid(P); var res = new int[Count * k];
        Parallel.For(0, Count, new ParallelOptions { CancellationToken = ct }, () => new Knn(k + 1), (i, _, kn) =>
        {
            grid.Nearest(P[3 * i], P[3 * i + 1], P[3 * i + 2], k + 1, kn);
            int w = 0;
            for (int j = 0; j < kn.Found && w < k; j++) if (kn.Idx[j] != i) res[i * k + w++] = kn.Idx[j];
            while (w < k) res[i * k + w++] = -1;
            return kn;
        }, _ => { });
        return (res, k);
    }

    /// <summary>
    /// Make normals consistent by propagating orientation along a minimum spanning tree of the neighbour graph (edge cost 1 - |n_i . n_j|).
    /// Each connected piece is then flipped as a whole: towards <paramref name="viewpoint"/> when given, otherwise so its top-most point
    /// faces <paramref name="up"/> when given, otherwise outward from the cloud centre.
    /// </summary>
    public static void OrientNormals(PointCloud cloud, double[] normals, int k = 10, Vec3? viewpoint = null, Vec3? up = null, CancellationToken ct = default)
    {
        int n = cloud.Count; if (n == 0) return;
        var (nbr, kk) = cloud.Neighbours(k, ct);
        // symmetric adjacency is not required: Prim over directed kNN edges plus reverse lookups through the queue is enough in practice,
        // but reverse edges are added so isolated sinks still get reached.
        var rev = new List<int>[n];
        for (int i = 0; i < n; i++) for (int j = 0; j < kk; j++) { int q = nbr[i * kk + j]; if (q < 0) break; (rev[q] ??= []).Add(i); }
        var P = cloud.P; var visited = new bool[n]; var comp = new int[n]; int ncomp = 0;
        var compSeed = new List<int>();
        var pq = new PriorityQueue<(int from, int to), float>();
        // seeds: top-most (largest z) first so that "up" has a sensible anchor; remaining unvisited points start new pieces
        var order = Enumerable.Range(0, n).OrderByDescending(i => up.HasValue ? P[3 * i] * up.Value.X + P[3 * i + 1] * up.Value.Y + P[3 * i + 2] * up.Value.Z : P[3 * i + 2]).ToArray();
        void Push(int a, int b)
        {
            double d = normals[3 * a] * normals[3 * b] + normals[3 * a + 1] * normals[3 * b + 1] + normals[3 * a + 2] * normals[3 * b + 2];
            pq.Enqueue((a, b), (float)(1 - Math.Abs(d)));
        }
        foreach (var seed in order)
        {
            if (visited[seed]) continue;
            ct.ThrowIfCancellationRequested();
            visited[seed] = true; comp[seed] = ncomp; compSeed.Add(seed);
            void Expand(int a)
            {
                for (int j = 0; j < kk; j++) { int q = nbr[a * kk + j]; if (q < 0) break; if (!visited[q]) Push(a, q); }
                if (rev[a] != null) foreach (var q in rev[a]) if (!visited[q]) Push(a, q);
            }
            Expand(seed);
            while (pq.Count > 0)
            {
                var (a, b) = pq.Dequeue();
                if (visited[b]) continue;
                visited[b] = true; comp[b] = ncomp;
                double d = normals[3 * a] * normals[3 * b] + normals[3 * a + 1] * normals[3 * b + 1] + normals[3 * a + 2] * normals[3 * b + 2];
                if (d < 0) { normals[3 * b] = -normals[3 * b]; normals[3 * b + 1] = -normals[3 * b + 1]; normals[3 * b + 2] = -normals[3 * b + 2]; }
                Expand(b);
            }
            ncomp++;
        }
        // per-piece global sign
        var score = new double[ncomp];
        var (mn, mx) = cloud.Bounds(); var c0 = (mn + mx) * 0.5;
        for (int i = 0; i < n; i++)
        {
            double nx = normals[3 * i], ny = normals[3 * i + 1], nz = normals[3 * i + 2];
            if (viewpoint is { } vp) score[comp[i]] += nx * (vp.X - P[3 * i]) + ny * (vp.Y - P[3 * i + 1]) + nz * (vp.Z - P[3 * i + 2]);
            else if (up == null) score[comp[i]] += nx * (P[3 * i] - c0.X) + ny * (P[3 * i + 1] - c0.Y) + nz * (P[3 * i + 2] - c0.Z);
        }
        if (viewpoint == null && up is { } u)
            for (int c = 0; c < ncomp; c++) { int s = compSeed[c]; score[c] = normals[3 * s] * u.X + normals[3 * s + 1] * u.Y + normals[3 * s + 2] * u.Z; }
        for (int i = 0; i < n; i++)
            if (score[comp[i]] < 0) { normals[3 * i] = -normals[3 * i]; normals[3 * i + 1] = -normals[3 * i + 1]; normals[3 * i + 2] = -normals[3 * i + 2]; }
    }

    /// <summary>Estimate and orient normals, store them on this cloud and return it.</summary>
    public PointCloud WithEstimatedNormals(int k = 16, Vec3? viewpoint = null, Vec3? up = null, CancellationToken ct = default)
    {
        var n = EstimateNormals(k, ct);
        OrientNormals(this, n, Math.Min(k, 10), viewpoint, up, ct);
        return new PointCloud(P, n, C);
    }
}

public interface IPointVisitor { void Visit(int index, double dx, double dy, double dz, double d2); }

/// <summary>Scratch buffer for one nearest-neighbour query (sorted ascending by distance).</summary>
public sealed class Knn
{
    public readonly int[] Idx; public readonly double[] D2; public int Found;
    public Knn(int k) { Idx = new int[k]; D2 = new double[k]; }
}

/// <summary>Uniform grid over a point set for k-nearest and radius queries.</summary>
public sealed class PointGrid
{
    readonly double[] _p; readonly double _minX, _minY, _minZ, _cell, _inv; readonly int _nx, _ny, _nz;
    readonly int[] _start, _items;
    public double CellSize => _cell;

    public PointGrid(double[] p, double? cellSize = null)
    {
        _p = p; int n = p.Length / 3;
        double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1;
        for (int i = 0; i < p.Length; i += 3) { x0 = Math.Min(x0, p[i]); x1 = Math.Max(x1, p[i]); y0 = Math.Min(y0, p[i + 1]); y1 = Math.Max(y1, p[i + 1]); z0 = Math.Min(z0, p[i + 2]); z1 = Math.Max(z1, p[i + 2]); }
        if (n == 0) { x0 = y0 = z0 = x1 = y1 = z1 = 0; }
        _minX = x0; _minY = y0; _minZ = z0;
        double sx = x1 - x0, sy = y1 - y0, sz = z1 - z0, side = Math.Max(Math.Max(sx, sy), Math.Max(sz, 1e-9));
        double ex = Math.Max(sx, side * 0.01), ey = Math.Max(sy, side * 0.01), ez = Math.Max(sz, side * 0.01);
        double cell = cellSize ?? Math.Cbrt(ex * ey * ez / Math.Max(n, 1) * 2);
        bool Fit(double c) { return ex / c + 1 <= 4096 && ey / c + 1 <= 4096 && ez / c + 1 <= 4096 && (ex / c + 1) * (ey / c + 1) * (ez / c + 1) <= 24_000_000; }
        while (!Fit(cell)) cell *= 1.5;
        if (cellSize == null)
            for (int attempt = 0; attempt < 4 && Fit(cell / 2); attempt++)
            {
                int cx = Math.Max(1, (int)(ex / cell + 1)), cy = Math.Max(1, (int)(ey / cell + 1)), cz = Math.Max(1, (int)(ez / cell + 1));
                var seen = new HashSet<int>();
                for (int i = 0; i < n; i++) seen.Add(IndexOf(p[3 * i], p[3 * i + 1], p[3 * i + 2], cell, cx, cy, cz, x0, y0, z0));
                if ((double)n / Math.Max(seen.Count, 1) > 6) cell /= 2; else break;
            }
        _cell = cell; _inv = 1 / cell;
        _nx = Math.Max(1, (int)(ex / cell + 1)); _ny = Math.Max(1, (int)(ey / cell + 1)); _nz = Math.Max(1, (int)(ez / cell + 1));
        var cnt = new int[_nx * _ny * _nz + 1];
        var cellOf = new int[n];
        for (int i = 0; i < n; i++) { int c = CellIndex(p[3 * i], p[3 * i + 1], p[3 * i + 2]); cellOf[i] = c; cnt[c + 1]++; }
        for (int c = 0; c < cnt.Length - 1; c++) cnt[c + 1] += cnt[c];
        _start = cnt; _items = new int[n];
        var fill = new int[_nx * _ny * _nz];
        for (int i = 0; i < n; i++) { int c = cellOf[i]; _items[_start[c] + fill[c]++] = i; }
    }

    static int IndexOf(double x, double y, double z, double cell, int nx, int ny, int nz, double x0, double y0, double z0)
    {
        int i = Math.Clamp((int)((x - x0) / cell), 0, nx - 1), j = Math.Clamp((int)((y - y0) / cell), 0, ny - 1), k = Math.Clamp((int)((z - z0) / cell), 0, nz - 1);
        return i + nx * (j + ny * k);
    }

    int CellIndex(double x, double y, double z) => IndexOf(x, y, z, _cell, _nx, _ny, _nz, _minX, _minY, _minZ);

    /// <summary>The k nearest points to a position, sorted by distance, into <paramref name="r"/>.</summary>
    public void Nearest(double x, double y, double z, int k, Knn r)
    {
        r.Found = 0;
        int ci = Math.Clamp((int)Math.Floor((x - _minX) * _inv), 0, _nx - 1), cj = Math.Clamp((int)Math.Floor((y - _minY) * _inv), 0, _ny - 1), ck = Math.Clamp((int)Math.Floor((z - _minZ) * _inv), 0, _nz - 1);
        int maxR = Math.Max(_nx, Math.Max(_ny, _nz));
        for (int ring = 0; ring <= maxR; ring++)
        {
            int i0 = ci - ring, i1 = ci + ring, j0 = cj - ring, j1 = cj + ring, k0 = ck - ring, k1 = ck + ring;
            for (int kz = Math.Max(k0, 0); kz <= Math.Min(k1, _nz - 1); kz++)
                for (int jy = Math.Max(j0, 0); jy <= Math.Min(j1, _ny - 1); jy++)
                {
                    bool shell = kz == k0 || kz == k1 || jy == j0 || jy == j1;
                    if (shell) for (int ix = Math.Max(i0, 0); ix <= Math.Min(i1, _nx - 1); ix++) Scan(ix + _nx * (jy + _ny * kz), x, y, z, k, r);
                    else
                    {
                        if (i0 >= 0) Scan(i0 + _nx * (jy + _ny * kz), x, y, z, k, r);
                        if (i1 < _nx && i1 != i0) Scan(i1 + _nx * (jy + _ny * kz), x, y, z, k, r);
                    }
                }
            // anything in an outer ring is at least `ring` cells minus the query's offset inside its cell away
            if (r.Found == k)
            {
                double dx = Math.Min(x - (_minX + ci * _cell), _minX + (ci + 1) * _cell - x), dy = Math.Min(y - (_minY + cj * _cell), _minY + (cj + 1) * _cell - y), dz = Math.Min(z - (_minZ + ck * _cell), _minZ + (ck + 1) * _cell - z);
                double safe = ring * _cell + Math.Max(0, Math.Min(dx, Math.Min(dy, dz)));
                if (r.D2[k - 1] <= safe * safe) break;
            }
        }
    }

    /// <summary>Call <paramref name="v"/> for every point within <paramref name="r"/> of a position.</summary>
    public void Visit<T>(double x, double y, double z, double r, ref T v) where T : struct, IPointVisitor
    {
        int i0 = Math.Max(0, (int)Math.Floor((x - r - _minX) * _inv)), i1 = Math.Min(_nx - 1, (int)Math.Floor((x + r - _minX) * _inv));
        int j0 = Math.Max(0, (int)Math.Floor((y - r - _minY) * _inv)), j1 = Math.Min(_ny - 1, (int)Math.Floor((y + r - _minY) * _inv));
        int k0 = Math.Max(0, (int)Math.Floor((z - r - _minZ) * _inv)), k1 = Math.Min(_nz - 1, (int)Math.Floor((z + r - _minZ) * _inv));
        double r2 = r * r;
        for (int k = k0; k <= k1; k++)
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int c = i + _nx * (j + _ny * k);
                    for (int t = _start[c]; t < _start[c + 1]; t++)
                    {
                        int q = _items[t]; double dx = _p[3 * q] - x, dy = _p[3 * q + 1] - y, dz = _p[3 * q + 2] - z, d2 = dx * dx + dy * dy + dz * dz;
                        if (d2 <= r2) v.Visit(q, dx, dy, dz, d2);
                    }
                }
    }

    void Scan(int cell, double x, double y, double z, int k, Knn r)
    {
        for (int t = _start[cell]; t < _start[cell + 1]; t++)
        {
            int q = _items[t]; double dx = _p[3 * q] - x, dy = _p[3 * q + 1] - y, dz = _p[3 * q + 2] - z, d2 = dx * dx + dy * dy + dz * dz;
            if (r.Found == k && d2 >= r.D2[k - 1]) continue;
            int pos = r.Found < k ? r.Found++ : k - 1;
            while (pos > 0 && r.D2[pos - 1] > d2) { r.D2[pos] = r.D2[pos - 1]; r.Idx[pos] = r.Idx[pos - 1]; pos--; }
            r.D2[pos] = d2; r.Idx[pos] = q;
        }
    }
}
