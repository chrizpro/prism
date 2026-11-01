using Prism.Kernel;
using Prism.Meshing;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); checks++; }

TriMesh Cube(double s = 10, float[]? extraV = null, int[]? extraT = null)
{
    var v = new List<float> { 0,0,0, (float)s,0,0, (float)s,(float)s,0, 0,(float)s,0, 0,0,(float)s, (float)s,0,(float)s, (float)s,(float)s,(float)s, 0,(float)s,(float)s };
    int[] t = [0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7];
    var ti = t.ToList();
    if (extraV != null) v.AddRange(extraV);
    if (extraT != null) ti.AddRange(extraT);
    return new TriMesh([.. v], [.. ti]);
}

// ---- single triangles
var eq = MeshQuality.Triangle(new(0, 0, 0), new(1, 0, 0), new(0.5, Math.Sqrt(3) / 2, 0));
Check(Math.Abs(eq.AspectRatio - 1) < 1e-9 && Math.Abs(eq.MinAngle - 60) < 1e-6 && Math.Abs(eq.MaxAngle - 60) < 1e-6 && eq.Class == TriClass.Good, "equilateral triangle: aspect 1, all angles 60, good");
var right = MeshQuality.Triangle(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0));
Check(Math.Abs(right.MaxAngle - 90) < 1e-6 && Math.Abs(right.MinAngle - 45) < 1e-6 && right.Class == TriClass.Good && Math.Abs(right.AspectRatio - 1.3938) < 1e-3, "right isosceles triangle: 90/45 degrees");
var needle = MeshQuality.Triangle(new(0, 0, 0), new(10, 0, 0), new(10, 0.2, 0));
Check(needle.Class == TriClass.Needle && needle.MinAngle < 2 && needle.AspectRatio > 10, "long thin triangle is a needle");
var cap = MeshQuality.Triangle(new(0, 0, 0), new(10, 0, 0), new(5, 0.2, 0));
Check(cap.Class == TriClass.Cap && cap.MaxAngle > 170, "wide-angle triangle is a cap");
var deg = MeshQuality.Triangle(new(0, 0, 0), new(1, 0, 0), new(2, 0, 0));
Check(deg.Class == TriClass.Degenerate && double.IsInfinity(deg.AspectRatio), "collinear triangle is degenerate");

// ---- triangle/triangle intersection
Check(MeshQuality.TrianglesIntersect(new(0, 0, 0), new(4, 0, 0), new(0, 4, 0), new(1, 1, -1), new(1, 1, 1), new(3, 1, 0)), "piercing triangles intersect");
Check(!MeshQuality.TrianglesIntersect(new(0, 0, 0), new(4, 0, 0), new(0, 4, 0), new(0, 0, 1), new(4, 0, 1), new(0, 4, 1)), "parallel offset triangles do not intersect");
Check(!MeshQuality.TrianglesIntersect(new(0, 0, 0), new(4, 0, 0), new(0, 4, 0), new(1, 1, 0.001), new(1, 1, 1), new(3, 1, 1)), "triangle above the plane does not intersect");
Check(MeshQuality.TrianglesIntersect(new(0, 0, 0), new(4, 0, 0), new(0, 4, 0), new(1, 1, 0), new(5, 1, 0), new(1, 5, 0)), "overlapping coplanar triangles intersect");
Check(!MeshQuality.TrianglesIntersect(new(0, 0, 0), new(4, 0, 0), new(0, 4, 0), new(5, 5, 0), new(9, 5, 0), new(5, 9, 0)), "disjoint coplanar triangles do not intersect");

// ---- clean cube
var cube = Cube();
var clean = MeshQuality.SelfIntersections(cube);
Check(clean.Triangles.Length == 0 && clean.Pairs.Count == 0, "a clean cube has no self-intersections");
var (open0, nm0) = MeshQuality.EdgeDefects(cube);
Check(open0.Count == 0 && nm0.Count == 0, "a clean cube has no open or non-manifold edges");
var sCube = MeshQuality.Summarize(cube);
Check(sCube.Triangles == 12 && sCube.OpenEdges == 0 && sCube.NonManifoldEdges == 0, "cube summary counts");
Check(sCube.Slivers == 0 && sCube.Good == 12, "cube has no slivers (45/90 degree triangles)");
Check(Math.Abs(sCube.EdgeMin - 10) < 1e-6 && Math.Abs(sCube.EdgeMax - 10 * Math.Sqrt(2)) < 1e-6, "cube edge length min/max");

// ---- cube with a deliberately intersecting triangle: passes through the +X face (x=10), from inside to outside
var bad = Cube(10, [5, 3, 3, 15, 3, 3, 8, 7, 6], [8, 9, 10]);
var hit = MeshQuality.SelfIntersections(bad);
Check(hit.Triangles.Contains(12), "the stray triangle is reported");
Check(hit.Triangles.Length == 2 && hit.Triangles.Contains(6) || hit.Triangles.Length == 3 && hit.Triangles.Contains(6) && hit.Triangles.Contains(7), "stray triangle and the +X face triangle(s) it crosses (" + string.Join(",", hit.Triangles) + ")");
var (openB, nmB) = MeshQuality.EdgeDefects(bad);
Check(openB.Count == 3 && nmB.Count == 0, "the stray triangle adds 3 open edges, got " + openB.Count);

// ---- sliver glued above the cube (floating, no intersection)
var tm = Cube(10, [2, 2, 12, 8, 2, 12, 8, 2.1f, 12], [8, 9, 10]);
var met = MeshQuality.All(tm);
Check(met[12].Class is TriClass.Needle or TriClass.Cap, "added thin triangle classified as sliver");
var sum = MeshQuality.Summarize(tm);
Check(sum.Slivers == 1 && sum.Good == 12, "summary counts exactly one sliver");
Check(sum.OpenEdges == 3, "open edges counted for the floating triangle");
Check(MeshQuality.SelfIntersections(tm).Triangles.Length == 0, "floating sliver does not intersect anything");

// ---- non-manifold: three triangles on one edge
var nmv = new TriMesh([0,0,0, 1,0,0, 0,1,0, 0,-1,0, 0,0,1], [0,1,2, 1,0,3, 0,1,4]);
var (o2, n2) = MeshQuality.EdgeDefects(nmv);
Check(n2.Count == 1 && o2.Count == 6, $"non-manifold edge found (nm {n2.Count}, open {o2.Count})");

// ---- open mesh: cube without the top face (triangles 2,3)
var openCube = new TriMesh(cube.V, cube.T.Where((_, i) => i / 3 != 2 && i / 3 != 3).ToArray());
Check(openCube.TriCount == 10, "open cube has 10 triangles");
Check(MeshQuality.Summarize(openCube).OpenEdges == 4, "removing the top face opens 4 edges");

// ---- histograms add up
foreach (var (name, m) in new[] { ("cube", cube), ("bad", bad), ("thin", tm), ("nm", nmv) })
{
    var s = MeshQuality.Summarize(m);
    Check(s.AspectHistogram.Sum(b => b.Count) == s.Triangles && s.MinAngleHistogram.Sum(b => b.Count) == s.Triangles && s.MaxAngleHistogram.Sum(b => b.Count) == s.Triangles, name + ": histogram totals equal triangle count");
}
Check(sum.AspectHistogram.Last().Count >= 1 && sum.MinAngleHistogram[0].Count >= 1, "sliver lands in the worst buckets");

// ---- severity and vertex map
var sev = MeshQuality.Severity(met, QualityMetric.AspectRatio);
Check(sev.Length == tm.TriCount && sev[12] > 0.9f && sev[0] < 0.3f, "severity high for sliver, low for good triangles");
Check(MeshQuality.ToVertexMax(tm, sev).Length == tm.VertexCount, "vertex severity covers every vertex");

// ---- defect bundle
var d = MeshQuality.Defects(bad, MeshQuality.All(bad), hit.Triangles);
Check(d.OpenEdges.Count == 3 && d.SelfIntersecting.Count == hit.Triangles.Length, "defect set carries open edges and intersecting triangles");
var dOff = MeshQuality.Defects(bad, MeshQuality.All(bad), hit.Triangles, open: false, nonManifold: false, slivers: false);
Check(dOff.OpenEdges.Count == 0 && dOff.Slivers.Count == 0, "defect toggles switch categories off");

Console.WriteLine($"{checks} checks passed");
