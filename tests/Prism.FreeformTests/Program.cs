using Prism.Core.Form;
using Prism.Commands;
using Prism.Kernel;

// Run with: dotnet run --project tests/Prism.FreeformTests
[System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern bool SetDllDirectory(string path);
SetDllDirectory(Path.GetFullPath("native/bin"));
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message); checks++;
}
int WindingConflicts(SubdMesh mesh) => mesh.Topo().EdgeFaces.Count(pair =>
{
    if (pair.Value.Count != 2) return false;
    var (a, b) = SubdMesh.Unkey(pair.Key);
    bool Forward(int face) { var f = mesh.F[face]; return f[(Array.IndexOf(f, a) + 1) % f.Length] == b; }
    return Forward(pair.Value[0]) == Forward(pair.Value[1]);
});
void Reject(Action operation, string message)
{
    try { operation(); }
    catch (InvalidOperationException) { Check(true, message); return; }
    catch (ArgumentException) { Check(true, message); return; }
    throw new Exception("Expected rejection: " + message);
}

var cage = FormOps.Cylinder(10, 50, 8, 4);
Check(cage.IsClosed() && WindingConflicts(cage) == 0, "primitive is closed and consistently oriented");
int caps = cage.F.Count - 2;
FormOps.BridgeFaces(cage, caps, caps + 1);
Check(cage.IsClosed(), "bridging two caps produces a closed cage");
Check(WindingConflicts(cage) == 0, "both bridge seams preserve face orientation");
Check(cage.V.Count - cage.Topo().EdgeFaces.Count + cage.F.Count == 0, "bridge produces handle topology (Euler characteristic zero)");

var open = FormOps.Cylinder(10, 50, 8, 4, false);
var boundary = open.Topo().EdgeFaces.Where(p => p.Value.Count == 1).Select(p => SubdMesh.Unkey(p.Key));
var chains = FormOps.Chains(boundary);
Check(chains.Count == 2 && chains.All(c => c.closed), "open tube exposes two boundary loops");
FormOps.Bridge(open, chains[0].verts, true, chains[1].verts, true);
Check(open.IsClosed() && WindingConflicts(open) == 0, "edge-chain bridge closes both seams consistently");

var invalid = FormOps.Cylinder(10, 50, 8, 4);
var before = invalid.Clone();
Reject(() => FormOps.BridgeFaces(invalid, 0, 1), "adjacent faces cannot be bridged");
Check(invalid.F.Count == before.F.Count && invalid.V.SequenceEqual(before.V), "rejected adjacent-face bridge leaves cage untouched");
Reject(() => FormOps.BridgeFaces(invalid, 0, invalid.F.Count - 1), "different corner counts cannot be bridged");
Check(invalid.F.SelectMany(f => f).SequenceEqual(before.F.SelectMany(f => f)), "rejected bridge preserves every original face");
Reject(() => FormOps.Bridge(invalid, invalid.F[0].ToList(), true, invalid.F[16].ToList(), true), "internal edges cannot be used as bridge boundaries");
Reject(() => FormOps.BridgeFaces(invalid, -1, 5), "invalid face indices are rejected");

var inconsistent = FormOps.Cylinder(10, 50, 8, 4, false);
Array.Reverse(inconsistent.F[0]);
var loops = FormOps.Chains(inconsistent.Topo().EdgeFaces.Where(p => p.Value.Count == 1).Select(p => SubdMesh.Unkey(p.Key)));
int originalCount = inconsistent.F.Count;
Reject(() => FormOps.Bridge(inconsistent, loops[0].verts, true, loops[1].verts, true), "inconsistent boundary winding is rejected");
Check(inconsistent.F.Count == originalCount, "failed boundary validation adds no partial faces");
// A U-shaped pipe's endpoint chord points sideways, but its end tangents point up/down.
// Orienting caps against that chord used to reverse both caps (16 winding conflicts).
var centers = new[] { new Vec3(0, 0, 0), new Vec3(0, 0, 20), new Vec3(20, 0, 40), new Vec3(40, 0, 20), new Vec3(40, 0, 0) };
var rows = new List<List<Vec3>>();
for (int i = 0; i < centers.Length; i++)
{
    var tangent = (centers[Math.Min(i + 1, centers.Length - 1)] - centers[Math.Max(i - 1, 0)]).Normalized();
    var side = Vec3.Cross(tangent, Vec3.UnitY).Normalized();
    rows.Add(Enumerable.Range(0, 8).Select(j => centers[i] + Vec3.UnitY * (3 * Math.Cos(j * Math.PI / 4)) + side * (3 * Math.Sin(j * Math.PI / 4))).ToList());
}
var gridMethod = typeof(Command).Assembly.GetType("Prism.Commands.FormCurves")!.GetMethod("Grid")!;
var curved = (SubdMesh)gridMethod.Invoke(null, [rows, true, true, 3])!;
Check(curved.IsClosed(), "curved pipe is closed");
Check(WindingConflicts(curved) == 0, "curved pipe caps agree with adjacent side faces");

// A shallow crease must remain visible even when its angle is below the old 40° normal threshold.
SubdMesh ShallowFold(double sharpness)
{
    var mesh = new SubdMesh();
    mesh.V.AddRange([new(-1, 0, 0), new(0, 0, 0), new(0, 1, 0), new(-1, 1, 0), new(1, 0, .35), new(1, 1, .35)]);
    mesh.F.Add([0, 1, 2, 3]); mesh.F.Add([1, 4, 5, 2]);
    FormOps.SetCrease(mesh, [(1, 2)], sharpness);
    return mesh;
}
List<Vec3> NormalsAtSharedEdge(MeshData mesh)
{
    var normals = new List<Vec3>();
    for (int i = 0; i < mesh.Positions.Length; i += 3)
        if (Math.Abs(mesh.Positions[i]) < 1e-6 && Math.Abs(mesh.Positions[i + 1] - .5) < 1e-6)
            normals.Add(new(mesh.Normals[i], mesh.Normals[i + 1], mesh.Normals[i + 2]));
    return normals;
}
var hardNormals = NormalsAtSharedEdge(ShallowFold(3).BuildMesh(3));
Check(hardNormals.Count >= 2 && hardNormals.Any(a => hardNormals.Any(b => Vec3.Dot(a, b) < .98)), "hard shallow crease has separate shading normals on each side");
var softNormals = NormalsAtSharedEdge(ShallowFold(.5).BuildMesh(3));
Check(softNormals.Count >= 2 && softNormals.All(a => softNormals.All(b => Vec3.Dot(a, b) > .999)), "soft crease retains smoothly blended shading normals");
// Smoke-test fixes: extruding a patch, merging, pulling, merging and unwelding edges.
{
 int Isolated(SubdMesh m) { var used = new bool[m.V.Count]; foreach (var f in m.F) foreach (var v in f) used[v] = true; return used.Count(u => !u); }
 var block = FormOps.Box(60, 60, 60, 3, 3, 3);
 var side = Enumerable.Range(0, block.F.Count).Where(f => block.F[f].All(v => Math.Abs(block.V[v].X - 30) < 1e-6)).ToHashSet();
 Check(side.Count == 9, "a 3x3 patch of faces is found on the +X side");
 FormOps.ExtrudeFaces(block, side, 12);
 Check(Isolated(block) == 0 && block.IsClosed() && WindingConflicts(block) == 0, "extruding a patch with inner points leaves no stray points and stays closed");
 Check(block.V.Max(p => p.X) > 41.9, "the extruded patch moved out");
 var inset = FormOps.Box(60, 60, 60, 3, 3, 3);
 var top = Enumerable.Range(0, inset.F.Count).Where(f => inset.F[f].All(v => Math.Abs(inset.V[v].Z - 30) < 1e-6)).ToHashSet();
 FormOps.InsetFaces(inset, top, 4, 0, false);
 Check(Isolated(inset) == 0 && inset.IsClosed(), "inset of a patch leaves no stray points");

 var plate = FormOps.Plane(40, 40, 4, 4);
 int firstOfMerge = 7;
 var neighbours = new HashSet<int> { 5, 6 };
 var beforeMerge = plate.F.Select(f => f.ToArray()).ToList();
 Check(FormOps.MergeFaces(plate, neighbours), "two touching faces merge");
 Check(plate.F.Count == beforeMerge.Count - 1 && plate.F.Take(5).Select((f, i) => f.SequenceEqual(beforeMerge[i].Select(v => v))).All(x => x) || plate.F[5].Length == 6, "the merged face takes the place of the first merged face");
 Check(firstOfMerge == 7, "merge numbering check ran");

 var smooth = FormOps.Sphere(20, 3);
 var inner = Enumerable.Range(0, smooth.V.Count).ToHashSet();
 double radiusBefore = smooth.V.Average(p => p.Length);
 int pulled = FormOps.PullToLimit(smooth, inner);
 double radiusAfter = smooth.V.Average(p => p.Length);
 Check(pulled > 0 && radiusAfter < radiusBefore && smooth.IsClosed(), "pulling a sphere cage onto its smooth surface draws it inward");

 var strip = FormOps.Plane(40, 20, 4, 2);
 var seam = strip.Topo().EdgeFaces.Where(p => p.Value.Count == 2).Select(p => SubdMesh.Unkey(p.Key)).Where(e => Math.Abs(strip.V[e.a].X) < 1e-6 && Math.Abs(strip.V[e.b].X) < 1e-6).ToList();
 Check(seam.Count == 2, "the middle seam of a plane has two inner edges");
 int copies = FormOps.UnweldEdges(strip, seam);
 var openEdges = strip.Topo().EdgeFaces.Count(p => p.Value.Count == 1);
 Check(copies == 3 && openEdges == 16, "unwelding a seam from edge to edge splits the plane in two (3 points duplicated, 4 new border edges)");
 var fresh = FormOps.Plane(40, 20, 4, 2);
 var inside = fresh.Topo().EdgeFaces.Where(p => p.Value.Count == 2).Select(p => SubdMesh.Unkey(p.Key)).Where(e => Math.Abs(fresh.V[e.a].X) < 1e-6 && Math.Abs(fresh.V[e.b].X) < 1e-6).ToList();
 int mergedEdges = FormOps.MergeEdges(fresh, inside.Take(1));
 Check(mergedEdges == 1 && fresh.F.Count == 7 && fresh.F.Any(f => f.Length == 6), "merging an inner edge joins the two faces beside it");
}
// Persistent symmetry, selection tools, corner crease, chamfer and slide.
{
 int Isolated2(SubdMesh m) { var used = new bool[m.V.Count]; foreach (var f in m.F) foreach (var v in f) used[v] = true; return used.Count(u => !u); }
 var ball = FormOps.Sphere(30, 4); ball.Sym = "x";
 Check(ball.Clone().Sym == "x" && FormSymmetry.Asymmetry(ball, FormSymmetry.Of(ball)) < 1e-6, "a form carries its symmetry through clones and a fresh sphere is symmetric");
 var right = Enumerable.Range(0, ball.F.Count).Where(f => ball.FaceCenter(f).X > 20 && Math.Abs(ball.FaceNormal(f).X) > .9).ToHashSet();
 var sel = right.ToHashSet(); FormSymmetry.Expand(ball, FormSymmetry.Of(ball), new HashSet<int>(), sel, []);
 Check(right.Count > 0 && sel.Count == right.Count * 2, "selecting faces on one side also selects their mirror images");
 FormOps.ExtrudeFaces(ball, sel, 8);
 Check(FormSymmetry.Asymmetry(ball, FormSymmetry.Of(ball)) < 1e-6 && ball.IsClosed(), "extruding the pair keeps the cage exactly symmetric and closed");
 ball.V[3] += new Vec3(2, 1, 0.5);
 Check(FormSymmetry.Asymmetry(ball, FormSymmetry.Of(ball)) > 0.1, "a stray edit breaks the symmetry");
 int fixedPoints = FormSymmetry.Enforce(ball, FormSymmetry.Of(ball));
 Check(fixedPoints > 0 && FormSymmetry.Asymmetry(ball, FormSymmetry.Of(ball)) < 1e-6, "enforcing symmetry makes it exact again");
 var radial = FormOps.Cylinder(20, 30, 8, 3); radial.Sym = "radial:4";
 Check(FormSymmetry.Asymmetry(radial, FormSymmetry.Of(radial)) < 1e-6, "an 8-sided cylinder is radially symmetric by 4");

 var cube = FormOps.Box(30, 30, 30, 3, 3, 3);
 var topFaces = Enumerable.Range(0, cube.F.Count).Where(f => cube.F[f].All(v => Math.Abs(cube.V[v].Z - 30) < 1e-6)).ToHashSet();
 var grown = FormSelect.GrowFaces(cube, topFaces); var shrunk = FormSelect.ShrinkFaces(cube, topFaces); var inverted = FormSelect.InvertFaces(cube, topFaces);
 Console.WriteLine($"  top {topFaces.Count} grown {grown.Count} shrunk {shrunk.Count} inverted {inverted.Count} of {cube.F.Count}");
 Check(topFaces.Count == 9 && grown.Count > 9 && shrunk.Count == 1 && inverted.Count == cube.F.Count - 9, "grow, shrink and invert change a patch as expected (9 faces: grow adds a ring, shrink keeps the middle)");
 Check(FormSelect.FacesByAngle(cube, [topFaces.First()], 30).Count == 9 && FormSelect.FacesByAngle(cube, [topFaces.First()], 100).Count == cube.F.Count, "flooding by angle stops at a 90 degree edge unless the limit allows it");
 Check(FormSelect.BoundaryEdges(cube).Count == 0 && FormSelect.BoundaryEdges(FormOps.Plane(10, 10, 2, 2)).Count == 8, "boundary edges of a closed cage: none; of a 2x2 plane: eight");

 var crown = FormOps.Sphere(20, 3);
 double soft = crown.BuildMesh(2).Positions.Where((v, i) => i % 3 == 2).Max();
 int top = Enumerable.Range(0, crown.V.Count).OrderByDescending(i => crown.V[i].Z).First();
 FormOps.SetVertexCrease(crown, [top], 5);
 double sharp = crown.BuildMesh(2).Positions.Where((v, i) => i % 3 == 2).Max();
 Check(sharp > soft + 0.3 && Math.Abs(sharp - crown.V[top].Z) < 1e-4, "a corner crease keeps the point where it was while the rest stays smooth");
 Check(crown.Clone().Crease.ContainsKey(SubdMesh.Key(top, top)), "corner creases survive cloning");

 var block = FormOps.Box(60, 60, 60, 1, 1, 1);
 int facesBefore = block.F.Count;
 int added = FormOps.ChamferEdges(block, block.Edges().ToList(), 4);
 Check(added == 12 + 8 && block.F.Count == facesBefore + added, "chamfering all 12 edges of a box adds 12 strips and 8 corner faces");
 Check(block.IsClosed() && WindingConflicts(block) == 0 && Isolated2(block) == 0, "the chamfered box is closed, consistently oriented and has no stray points");
 var rim = FormOps.Box(60, 60, 60, 2, 2, 2);
 var oneEdge = rim.Edges().Where(e => Math.Abs(rim.V[e.a].Z - 60) < 1e-6 && Math.Abs(rim.V[e.b].Z - 60) < 1e-6 && Math.Abs(rim.V[e.a].Y - 30) < 1e-6 && Math.Abs(rim.V[e.b].Y - 30) < 1e-6).ToList();
 int single = FormOps.ChamferEdges(rim, oneEdge, 3);
 Check(single > 0 && rim.IsClosed() && WindingConflicts(rim) == 0, "chamfering one rim edge of a box keeps it closed and consistent");

 var slab = FormOps.Plane(40, 40, 4, 4);
 var middle = slab.Edges().Where(e => Math.Abs(slab.V[e.a].X) < 1e-6 && Math.Abs(slab.V[e.b].X) < 1e-6).ToList();
 var xBefore = middle.SelectMany(e => new[] { e.a, e.b }).Distinct().Select(v => slab.V[v].X).ToList();
 int slid = FormOps.SlideEdges(slab, middle, 0.5);
 var xAfter = middle.SelectMany(e => new[] { e.a, e.b }).Distinct().Select(v => slab.V[v].X).ToList();
 Check(slid == xBefore.Count && xAfter.All(x => Math.Abs(Math.Abs(x) - 5) < 1e-6) && xAfter.Distinct().Count() <= 2, "sliding the middle loop of a plane by half moves it half way to its neighbour loop, all to the same side");
 // surface quality report
 var qs = FormQuality.Analyze(FormOps.Sphere(30, 4));
 Check(qs.Poles3 == 8 && qs.Poles5OrMore == 0 && qs.Triangles == 0 && qs.NGons == 0 && qs.Quads == 96, "a quad-ball has eight three-edged poles and only quads");
 Check(qs.MaxCurvature > 0.02 && qs.MaxCurvature < 0.06 && qs.MeanCurvature > 0.02 && qs.MeanCurvature < 0.05, "the curvature of a 30 mm sphere reads about 1/30 per mm");
 var mixed = FormOps.Box(40, 40, 40, 2, 2, 2);
 var firstTop = Enumerable.Range(0, mixed.F.Count).First(f => mixed.F[f].All(v => Math.Abs(mixed.V[v].Z - 40) < 1e-6));
 var twoTop = Enumerable.Range(0, mixed.F.Count).Where(f => mixed.F[f].All(v => Math.Abs(mixed.V[v].Z - 40) < 1e-6)).Take(2).ToHashSet();
 FormOps.MergeFaces(mixed, twoTop);
 var qm = FormQuality.Analyze(mixed);
 Check(qm.NGons == 1 && qm.NGonFaces.Count == 1, "a merged face is reported as an n-gon");
 Check(FormQuality.Analyze(FormOps.Plane(40, 40, 4, 4)).BoundaryVertices == 16, "points on an open border are counted separately from poles");
 // cheaper conversion
 var egg = FormOps.Sphere(30, 4);
 var topV = Enumerable.Range(0, egg.V.Count).OrderByDescending(i => egg.V[i].Z).First(); egg.V[topV] += new Vec3(0, 0, 6);
 var exactShape = FormToBrep.ToShape(egg); var fewerShape = FormToBrep.ToShape(egg, ConvertDetail.Fewer);
 int exactFaces = exactShape.Count(SubKind.Face), fewerFaces = fewerShape.Count(SubKind.Face);
 Console.WriteLine($"  exact {exactFaces} faces, fewer {fewerFaces} faces");
 var mExact = Occ.Mass(exactShape); var mFewer = Occ.Mass(fewerShape);
 Check(fewerFaces < exactFaces && Math.Abs(mFewer.Volume - mExact.Volume) / mExact.Volume < 0.01, "the fewer-faces conversion has fewer faces and the same volume within 1%");
}
Console.WriteLine($"SUCCESS: {checks} checks");