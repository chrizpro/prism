using Prism.Kernel;
using Prism.Meshing;

int checks = 0, failed = 0;
void Check(bool ok, string msg) { Console.WriteLine((ok ? "PASS " : "FAIL ") + msg); checks++; if (!ok) failed++; }

TriMesh Box(double x0, double y0, double z0, double x1, double y1, double z1)
{
    var v = new float[] { (float)x0, (float)y0, (float)z0, (float)x1, (float)y0, (float)z0, (float)x1, (float)y1, (float)z0, (float)x0, (float)y1, (float)z0,
                          (float)x0, (float)y0, (float)z1, (float)x1, (float)y0, (float)z1, (float)x1, (float)y1, (float)z1, (float)x0, (float)y1, (float)z1 };
    int[] q = [0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7];
    var t = new List<int>();
    for (int f = 0; f < 6; f++) { t.AddRange([q[4 * f], q[4 * f + 1], q[4 * f + 2]]); t.AddRange([q[4 * f], q[4 * f + 2], q[4 * f + 3]]); }
    return new TriMesh(v, [.. t]);
}

TriMesh Sphere(Vec3 c, double r, int levels)
{
    double g = (1 + Math.Sqrt(5)) / 2;
    var pts = new List<Vec3> { new(-1, g, 0), new(1, g, 0), new(-1, -g, 0), new(1, -g, 0), new(0, -1, g), new(0, 1, g), new(0, -1, -g), new(0, 1, -g), new(g, 0, -1), new(g, 0, 1), new(-g, 0, -1), new(-g, 0, 1) };
    var tr = new List<(int, int, int)> { (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1) };
    for (int l = 0; l < levels; l++)
    {
        var mid = new Dictionary<(int, int), int>(); var nt = new List<(int, int, int)>();
        int M(int a, int b) { var k = a < b ? (a, b) : (b, a); if (!mid.TryGetValue(k, out int i)) { pts.Add((pts[a] + pts[b]) * 0.5); mid[k] = i = pts.Count - 1; } return i; }
        foreach (var (a, b, d) in tr) { int ab = M(a, b), bd = M(b, d), da = M(d, a); nt.Add((a, ab, da)); nt.Add((b, bd, ab)); nt.Add((d, da, bd)); nt.Add((ab, bd, da)); }
        tr = nt;
    }
    var v = new float[pts.Count * 3];
    for (int i = 0; i < pts.Count; i++) { var p = c + pts[i].Normalized() * r; v[3 * i] = (float)p.X; v[3 * i + 1] = (float)p.Y; v[3 * i + 2] = (float)p.Z; }
    return new TriMesh(v, tr.SelectMany(t => new[] { t.Item1, t.Item2, t.Item3 }).ToArray());
}

bool Closed(TriMesh m) { var t = m.Topology(); return m.TriCount > 0 && t.BoundaryEdges == 0 && t.NonManifoldEdges == 0 && t.InconsistentEdges == 0; }
bool Near(double a, double b, double rel) => Math.Abs(a - b) <= rel * Math.Abs(b);

// ------------------------------------------------------------ remesh
var cube = Box(0, 0, 0, 10, 10, 10);
Check(Closed(cube) && Near(cube.Volume(), 1000, 1e-6), "test cube is closed with volume 1000");
{
    var r = MeshRemesh.Remesh(cube, new MeshRemesh.Options(2.0, 6, true, 35, true));
    Console.WriteLine($"  cube remesh: {r.TriCount} tris, volume {r.Volume():0.###}, uniformity {MeshRemesh.EdgeUniformity(r, 2.0):0.00}, min angle {MeshRemesh.MinAngleDeg(r):0.0}");
    Check(Closed(r), "remeshed cube is closed and manifold");
    Check(Near(r.Volume(), 1000, 0.005), "remeshed cube keeps its volume");
    Check(r.TriCount > 100, "remeshed cube has many more triangles");
    Check(MeshRemesh.EdgeUniformity(r, 2.0) > 0.85, "cube edge lengths are uniform around the target");
    Check(Near(r.Size.X, 10, 1e-4) && Near(r.Size.Z, 10, 1e-4), "sharp cube corners and edges are kept");
    Check(MeshRemesh.MinAngleDeg(r) > 20, "cube remesh has decent triangle quality");
}
{
    var sph = Sphere(Vec3.Zero, 10, 2);
    double mean = MeshRemesh.MeanEdge(sph);
    var r = MeshRemesh.Remesh(sph, new MeshRemesh.Options(mean * 0.6, 6, false, 35, true));
    Console.WriteLine($"  sphere remesh: {sph.TriCount} -> {r.TriCount} tris, volume {sph.Volume():0.#} -> {r.Volume():0.#}, uniformity {MeshRemesh.EdgeUniformity(r, mean * 0.6):0.00}, min angle {MeshRemesh.MinAngleDeg(r):0.0}");
    Check(Closed(r), "remeshed sphere is closed and manifold");
    Check(Near(r.Volume(), sph.Volume(), 0.03), "remeshed sphere volume within 3%");
    Check(MeshRemesh.EdgeUniformity(r, mean * 0.6) > 0.85, "sphere edge lengths are uniform around the target");
    var coarse = MeshRemesh.Remesh(sph, new MeshRemesh.Options(mean * 2, 6, false, 35, true));
    Console.WriteLine($"  sphere coarsen: {coarse.TriCount} tris, volume {coarse.Volume():0.#}");
    Check(Closed(coarse) && coarse.TriCount < sph.TriCount / 2, "coarsening reduces triangles and stays closed");
    // a 66-triangle polyhedron inscribed in the sphere is inherently ~15% smaller; just make sure it did not collapse
    Check(Near(coarse.Volume(), sph.Volume(), 0.2), "coarsened sphere volume within 20% (inscribed coarse polyhedron)");
}

// ------------------------------------------------------------ boolean
double V(BooleanResult r) => r.Mesh!.Volume();
{
    var a = Box(0, 0, 0, 10, 10, 10); var b = Box(5, 5, 5, 15, 15, 15);
    var u = MeshBoolean.Run(a, b, BooleanOp.Union); var d = MeshBoolean.Run(a, b, BooleanOp.Subtract); var i = MeshBoolean.Run(a, b, BooleanOp.Intersect);
    Check(u.Mesh != null && d.Mesh != null && i.Mesh != null, "cube/cube booleans succeed " + u.Error + d.Error + i.Error);
    Console.WriteLine($"  cubes: union {V(u):0.###} diff {V(d):0.###} inter {V(i):0.###}");
    Check(Near(V(u), 1875, 1e-3), "union volume 1875");
    Check(Near(V(d), 875, 1e-3), "difference volume 875");
    Check(Near(V(i), 125, 1e-3), "intersection volume 125");
    Check(Closed(u.Mesh!) && Closed(d.Mesh!) && Closed(i.Mesh!), "cube/cube results closed and manifold");
    var rev = MeshBoolean.Run(b, a, BooleanOp.Subtract);
    Check(rev.Mesh != null && Near(V(rev), 875, 1e-3), "B minus A volume 875");
}
{
    var cubeB = Box(-10, -10, -10, 10, 10, 10); var s = Sphere(new Vec3(10, 10, 10), 8, 4);
    double sv = s.Volume();
    var u = MeshBoolean.Run(cubeB, s, BooleanOp.Union); var d = MeshBoolean.Run(cubeB, s, BooleanOp.Subtract); var i = MeshBoolean.Run(cubeB, s, BooleanOp.Intersect);
    Check(u.Mesh != null && d.Mesh != null && i.Mesh != null, "sphere/cube booleans succeed " + u.Error + d.Error + i.Error);
    Console.WriteLine($"  sphere-cube: union {V(u):0.#} diff {V(d):0.#} inter {V(i):0.#} (sphere {sv:0.#}); warnings: {u.Warning}{d.Warning}{i.Warning}");
    // the sphere's centre sits on the cube corner, so one octant (1/8) of the sphere overlaps
    Check(Near(V(i), sv / 8, 0.01), "intersection is one octant of the sphere");
    Check(Near(V(u), 8000 + sv - sv / 8, 1e-3), "union volume");
    Check(Near(V(d), 8000 - sv / 8, 1e-3), "difference volume");
    Check(Closed(u.Mesh!) && Closed(d.Mesh!) && Closed(i.Mesh!), "sphere/cube results closed and manifold");
    Check(Near(V(u) + V(i), 8000 + sv, 1e-3), "inclusion-exclusion holds");
}
{
    var a = Box(0, 0, 0, 10, 10, 10); var inner = Box(2, 2, 2, 4, 4, 4); var far = Box(50, 50, 50, 60, 60, 60);
    var d = MeshBoolean.Run(a, inner, BooleanOp.Subtract);
    Check(d.Mesh != null && Near(V(d), 1000 - 8, 1e-6), "nested cube subtract leaves a cavity");
    Check(MeshBoolean.Run(a, inner, BooleanOp.Union).Mesh is { } uu && Near(uu.Volume(), 1000, 1e-6), "union with nested cube is the outer cube");
    Check(MeshBoolean.Run(a, far, BooleanOp.Intersect).Error != null, "disjoint intersection is reported as empty");
    Check(MeshBoolean.Run(a, far, BooleanOp.Union).Mesh is { } fu && Near(fu.Volume(), 2000, 1e-6), "disjoint union keeps both");
}
{
    // coplanar side faces
    var a = Box(0, 0, 0, 10, 10, 10); var b = Box(0, 0, 5, 10, 10, 15);
    var u = MeshBoolean.Run(a, b, BooleanOp.Union);
    Check(u.Mesh != null && Closed(u.Mesh) && Near(u.Mesh.Volume(), 1500, 1e-3), "overlapping cubes with coplanar side faces union to 1500");
}
{
    var open = cube.Subset(Enumerable.Range(0, cube.TriCount - 2));
    var r = MeshBoolean.Run(open, Box(5, 5, 5, 15, 15, 15), BooleanOp.Union);
    Console.WriteLine("  message: " + r.Error);
    Check(r.Mesh == null && r.Error != null && r.Error.Contains("Repair"), "open mesh is rejected with a Repair hint");
    var inv = cube.Transformed(p => p, true);
    var r2 = MeshBoolean.Run(Box(5, 5, 5, 15, 15, 15), inv, BooleanOp.Union);
    Check(r2.Mesh == null && r2.Error != null && r2.Error.Contains("inside out"), "inside-out mesh is rejected");
}

Console.WriteLine($"{checks - failed}/{checks} checks passed");
return failed == 0 ? 0 : 1;
