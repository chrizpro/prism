using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string msg) { if (!ok) throw new Exception("FAIL " + msg); Console.WriteLine("PASS " + msg); checks++; }
var rnd = new Random(7);
double Gauss() { double u = 1 - rnd.NextDouble(), v = rnd.NextDouble(); return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v); }

PointCloud Sphere(int n, double r, double noise, Vec3 c)
{
    var p = new double[n * 3];
    for (int i = 0; i < n; i++)
    {
        var d = new Vec3(Gauss(), Gauss(), Gauss()).Normalized(); var q = c + d * (r + Gauss() * noise);
        p[3 * i] = q.X; p[3 * i + 1] = q.Y; p[3 * i + 2] = q.Z;
    }
    return new PointCloud(p);
}
PointCloud Box(int n, double a, double noise)
{
    var p = new double[n * 3];
    for (int i = 0; i < n; i++)
    {
        int face = rnd.Next(6); double u = rnd.NextDouble() * a, v = rnd.NextDouble() * a, w = (face % 2) * a;
        double x, y, z;
        switch (face / 2) { case 0: x = w; y = u; z = v; break; case 1: x = u; y = w; z = v; break; default: x = u; y = v; z = w; break; }
        p[3 * i] = x + Gauss() * noise; p[3 * i + 1] = y + Gauss() * noise; p[3 * i + 2] = z + Gauss() * noise;
    }
    return new PointCloud(p);
}

// ---- sphere
{
    var cloud = Sphere(60000, 50, 0.3, new Vec3(1000, -20, 5));
    var sw = Stopwatch.StartNew();
    var m = SurfaceReconstruction.FromCloud(cloud, 0, false, 16, false, null, new ReconstructOptions { Depth = 6 });
    Console.WriteLine($"sphere: {m.TriCount} tris in {sw.ElapsedMilliseconds} ms");
    var rep = MeshAnalysis.Report(m);
    Check(rep.Closed && rep.NonManifoldEdges == 0 && rep.InconsistentEdges == 0, "sphere reconstruction is closed, manifold and consistently oriented");
    Check(rep.Shells == 1, "sphere is one shell");
    double vol = 4.0 / 3 * Math.PI * 125000;
    Check(Math.Abs(rep.Volume - vol) / vol < 0.04, $"sphere volume {rep.Volume:0} within 4% of {vol:0}");
    Check(rep.Volume > 0, "outward orientation (positive volume)");
}
// ---- box with noise
{
    var cloud = Box(120000, 100, 0.2);
    var m = SurfaceReconstruction.FromCloud(cloud, 0, false, 16, false, null, new ReconstructOptions { Depth = 6 });
    var rep = MeshAnalysis.Report(m);
    Console.WriteLine(rep);
    Check(rep.Closed && rep.NonManifoldEdges == 0 && rep.InconsistentEdges == 0 && rep.Shells == 1, "box reconstruction is closed and manifold");
    Check(Math.Abs(rep.Volume - 1e6) / 1e6 < 0.05, $"box volume {rep.Volume:0} within 5% of 1e6");
    Check(Math.Abs(m.Size.X - 100) < 3 && Math.Abs(m.Size.Z - 100) < 3, "box extents are right");
}
// ---- normals orientation
{
    var cloud = Sphere(20000, 30, 0.05, Vec3.Zero);
    var n = cloud.EstimateNormals(16);
    PointCloud.OrientNormals(cloud, n, 10);
    int good = 0; for (int i = 0; i < cloud.Count; i++) { var d = cloud.Point(i).Normalized(); if (d.X * n[3 * i] + d.Y * n[3 * i + 1] + d.Z * n[3 * i + 2] > 0.9) good++; }
    Check(good > 0.98 * cloud.Count, $"normals consistently outward ({good}/{cloud.Count})");
    var nv = cloud.EstimateNormals(16); PointCloud.OrientNormals(cloud, nv, 10, viewpoint: Vec3.Zero);
    int inward = 0; for (int i = 0; i < cloud.Count; i++) { var d = cloud.Point(i).Normalized(); if (d.X * nv[3 * i] + d.Y * nv[3 * i + 1] + d.Z * nv[3 * i + 2] < -0.9) inward++; }
    Check(inward > 0.98 * cloud.Count, "viewpoint inside flips normals inward");
}
// ---- partial scan (hemisphere) stays open
{
    var all = Sphere(40000, 40, 0.1, Vec3.Zero);
    var keep = Enumerable.Range(0, all.Count).Where(i => all.P[3 * i + 2] > 0).ToList();
    var half = all.Subset(keep);
    var m = SurfaceReconstruction.FromCloud(half, 0, false, 16, false, new Vec3(0, 0, 500), new ReconstructOptions { Depth = 6, Trim = 0.5 });
    var rep = MeshAnalysis.Report(m);
    Check(rep.BoundaryEdges > 0 && m.Max.Z < 41 && m.Min.Z > -3, $"a partial scan stays open (z {m.Min.Z:0.0}..{m.Max.Z:0.0})");
}
// ---- speed on 500k
{
    var cloud = Sphere(500000, 50, 0.1, Vec3.Zero);
    var sw = Stopwatch.StartNew();
    var m = SurfaceReconstruction.FromCloud(cloud, 0, false, 12, false, null, new ReconstructOptions { Depth = 7 });
    Console.WriteLine($"500k points -> {m.TriCount} tris in {sw.ElapsedMilliseconds} ms");
    Check(MeshAnalysis.Report(m).Closed && sw.Elapsed.TotalSeconds < 60, "500k points reconstruct closed in under a minute");
}
// ---- downsample and outliers
{
    var cloud = Sphere(30000, 20, 0.0, Vec3.Zero);
    var d = cloud.VoxelDownsample(4);
    Check(d.Count < cloud.Count / 5 && d.Count > 50, $"voxel downsample {cloud.Count} -> {d.Count}");
    var p = cloud.P.Concat(new double[] { 200, 200, 200, -150, 90, 10, 80, -80, 120 }).ToArray();
    var noisy = new PointCloud(p);
    var clean = noisy.RemoveOutliers(10, 2.0);
    Check(clean.Count < noisy.Count && clean.Count >= cloud.Count - cloud.Count / 100, $"outliers removed ({noisy.Count} -> {clean.Count})");
    Check(Enumerable.Range(0, clean.Count).All(i => clean.Point(i).Length < 25), "no far point survives");
}
// ---- loaders
{
    var xyz = PointCloud.LoadText(["# comment", "1.5 2 3", "4,5,6", "7;8;9", "", "x y z", "1e1\t2\t3"], ".xyz");
    Check(xyz.Count == 4 && xyz.P[0] == 1.5 && xyz.P[3] == 4 && xyz.P[9] == 10 && !xyz.HasNormals && !xyz.HasColors, "xyz: mixed separators, comments, blanks, junk");
    var xyzn = PointCloud.LoadText(["0 0 0 0 0 1", "1 0 0 0 1 0", "2 0 0 1 0 0"], ".xyz");
    Check(xyzn.HasNormals && !xyzn.HasColors && Math.Abs(xyzn.N![4] - 1) < 1e-9, "xyz with normals (unit-length columns detected)");
    var xyzc = PointCloud.LoadText(["0 0 0 255 0 10", "1 0 0 128 64 3", "2 0 0 0 0 255"], ".xyz");
    Check(xyzc.HasColors && !xyzc.HasNormals && xyzc.C![0] == 255 && xyzc.C[2] == 10, "xyz with 0-255 colours");
    var xyzf = PointCloud.LoadText(["0 0 0 1 0.5 0", "1 0 0 0.2 0.2 0.2", "2 0 0 0 0 1"], ".xyz");
    Check(xyzf.HasColors && xyzf.C![1] == 128, "float colours scale to bytes");
    var pts = PointCloud.LoadText(["3", "1 2 3 100 10 20 30", "4 5 6 90 40 50 60", "7 8 9 80 70 80 90"], ".pts");
    Check(pts.Count == 3 && pts.HasColors && pts.C![0] == 10 && pts.C[8] == 90, "pts: count line, intensity, rgb");
    var all9 = PointCloud.LoadText(["0 0 0 0 0 1 255 0 0", "1 0 0 0 1 0 0 255 0"], ".xyz");
    Check(all9.HasNormals && all9.HasColors && all9.C![0] == 255 && all9.N![2] == 1, "xyz nx ny nz r g b");
    var csv = PointCloud.LoadText(["X,Y,Z,R,G,B,Nx,Ny,Nz", "1,2,3,10,20,30,0,0,1", "4,5,6,40,50,60,0,1,0"], ".csv");
    Check(csv.Count == 2 && csv.HasColors && csv.HasNormals && csv.C![1] == 20 && csv.N![4] == 1, "csv with header names in any order");

    string dir = Path.Combine(Path.GetTempPath(), "prism-pc-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try
    {
        string[] Header(bool nrm, bool col, string fmt, int faces) => new[]
        { "ply", "format " + fmt + " 1.0", "element vertex 3", "property float x", "property float y", "property float z" }
            .Concat(nrm ? new[] { "property float nx", "property float ny", "property float nz" } : [])
            .Concat(col ? new[] { "property uchar red", "property uchar green", "property uchar blue" } : [])
            .Concat(faces > 0 ? new[] { "element face " + faces, "property list uchar int vertex_indices" } : [])
            .Concat(["end_header"]).ToArray();
        string f1 = Path.Combine(dir, "a.ply"); File.WriteAllLines(f1, Header(false, false, "ascii", 0).Concat(["0 0 0", "1 0 0", "0 1 0"]));
        var a = PointCloud.Load(f1);
        Check(a.Count == 3 && !a.HasNormals && !a.HasColors && PointCloud.PlyHasNoFaces(f1), "ascii PLY vertices only");
        string f2 = Path.Combine(dir, "b.ply"); File.WriteAllLines(f2, Header(true, true, "ascii", 0).Concat(["0 0 0 0 0 1 255 0 0", "1 0 0 0 0 1 0 255 0", "0 1 0 0 0 1 0 0 255"]));
        var b = PointCloud.Load(f2);
        Check(b.HasNormals && b.HasColors && b.C![3] == 0 && b.C[4] == 255 && b.N![2] == 1, "ascii PLY with normals and colours");
        string f3 = Path.Combine(dir, "c.ply");
        using (var ms = new MemoryStream())
        {
            var h = string.Join("\n", Header(true, true, "binary_little_endian", 0)) + "\n";
            ms.Write(System.Text.Encoding.ASCII.GetBytes(h));
            using var bw = new BinaryWriter(ms);
            for (int i = 0; i < 3; i++) { bw.Write((float)i); bw.Write(2f); bw.Write(3f); bw.Write(0f); bw.Write(1f); bw.Write(0f); bw.Write((byte)(i * 100)); bw.Write((byte)5); bw.Write((byte)6); }
            bw.Flush(); File.WriteAllBytes(f3, ms.ToArray());
        }
        var c = PointCloud.Load(f3);
        Check(c.Count == 3 && c.P[3] == 1 && c.N![1] == 1 && c.C![3] == 100 && c.C[8] == 6, "binary PLY with normals and colours");
        string f4 = Path.Combine(dir, "d.ply"); File.WriteAllLines(f4, Header(false, false, "ascii", 1).Concat(["0 0 0", "1 0 0", "0 1 0", "3 0 1 2"]));
        Check(!PointCloud.PlyHasNoFaces(f4), "PLY with faces is a mesh, not a cloud");
        string f5 = Path.Combine(dir, "e.xyz"); File.WriteAllText(f5, "1 2 3\n4 5 6\n");
        Check(PointCloud.Load(f5).Count == 2, "load by path");
    }
    finally { Directory.Delete(dir, true); }
}
// ---- session integration
{
    string dir = Path.Combine(Path.GetTempPath(), "prism-pc-s-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try
    {
        var cloud = Sphere(20000, 25, 0.1, Vec3.Zero);
        string f = Path.Combine(dir, "ball.xyz");
        File.WriteAllLines(f, Enumerable.Range(0, cloud.Count).Select(i => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2}", cloud.P[3 * i], cloud.P[3 * i + 1], cloud.P[3 * i + 2])));
        var s = new CadSession();
        s.ImportFile(f);
        Check(s.ActiveCommand is ReconstructSurfaceCommand, "importing an xyz file opens Reconstruct Surface");
        s.CommitCommand();
        var obj = s.Objects.FirstOrDefault(o => o.Tri != null);
        Check(obj != null && MeshAnalysis.Report(obj.Tri!).Closed && obj.Name.StartsWith("ball"), "applying it adds a closed mesh object");
    }
    finally { Directory.Delete(dir, true); }
}
Console.WriteLine($"{checks} checks passed");
