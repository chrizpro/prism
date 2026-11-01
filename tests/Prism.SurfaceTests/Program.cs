using System.Runtime.InteropServices;
using Prism.Core.Form;
using Prism.Core;
using Prism.Commands;
using Prism.Kernel;

NativeSearch.Initialize();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
Vec3[] Net(bool reference, int side, bool reverse = false)
{
    var result = new Vec3[16];
    for (int u = 0; u < 4; u++) for (int v = 0; v < 4; v++)
    {
        double x = reference ? -10 * u : 3 + 10 * u;
        double y = -15 + 10 * v;
        double z = reference ? .012 * x * x + .003 * y * y : 8 + .2 * x;
        int i = side % 2 == 1 ? 3 - u : u, j = reference && reverse ? 3 - v : v;
        if (side >= 2) (i, j) = (j, i);
        result[i * 4 + j] = new(x, y, z);
    }
    return result;
}
for (int side = 0; side < 4; side++) for (int refSide = 0; refSide < 4; refSide++) foreach (bool reverse in new[] { false, true })
{
    using var source = Occ.NurbsSurface(Net(false, side), 4, 4);
    using var reference = Occ.NurbsSurface(Net(true, refSide, reverse), 4, 4);
    var original = Occ.FaceNet(reference).Points;
    var before = Occ.SurfaceContinuity(source, side, reference, refSide, reverse);
    Check(before.Gap > 3, $"fixture has a real gap {side}/{refSide}/{reverse}");
    for (int order = 0; order <= 2; order++)
    {
        using var matched = Occ.MatchSurface(source, side, reference, refSide, order, reverse, .8);
        var report = Occ.SurfaceContinuity(matched, side, reference, refSide, reverse, 101);
        Check(matched.IsValid && report.Gap < 1e-7, $"G{order} closes the boundary {side}/{refSide}/{reverse}");
        if (order >= 1) Check(report.NormalAngle < 1e-4, "matching tangents align the tangent planes");
        if (order == 2) Check(report.CurvatureDifference < 1e-8, "matching second derivatives aligns cross-boundary normal curvature");
    }
    Check(Occ.FaceNet(reference).Points.SequenceEqual(original), "reference control net remains fixed");
    Check(Occ.FaceNet(source).Points.SequenceEqual(Net(false, side)), "source input remains unchanged until accepted");
}
using (var source = Occ.NurbsSurface(Net(false, 0), 4, 4))
using (var reference = Occ.NurbsSurface(Net(true, 0), 4, 4))
{
    using var refined = Occ.RefineFace(reference);
    bool rejected = false; try { using var result = Occ.MatchSurface(source, 0, refined, 0, 2); } catch (KernelException) { rejected = true; }
    Check(rejected, "incompatible boundary knot layouts are rejected");
    using var refinedSource = Occ.RefineFace(source);
    using var matched = Occ.MatchSurface(refinedSource, 0, refined, 0, 2);
    Check(Occ.SurfaceContinuity(matched, 0, refined, 0).CurvatureDifference < 1e-8, "G2 works after compatible knot refinement");
    using var elevated = Occ.ElevateFace(source); using var elevatedRef = Occ.ElevateFace(reference);
    using var elevatedMatch = Occ.MatchSurface(elevated, 0, elevatedRef, 0, 2);
    Check(Occ.SurfaceContinuity(elevatedMatch, 0, elevatedRef, 0).NormalAngle < 1e-4, "G2 works after degree elevation");
}
using (var plane = Occ.NurbsSurface(Enumerable.Range(0, 16).Select(i => new Vec3(i / 4 * 10, i % 4 * 10, 0)).ToArray(), 4, 4))
{
    var edges = Occ.FreeEdges(plane);
    using var patch = Occ.PatchSupported(plane, edges, 1);
    Check(patch.IsValid && Occ.Distance(plane, patch).Distance < 1e-4, "opening fill builds a valid G1 patch on a planar boundary");
    bool invalidEdgeRejected = false;
    try { using var invalidPatch = Occ.PatchSupported(plane, [-1], 1); } catch (KernelException) { invalidEdgeRejected = true; }
    Check(invalidEdgeRejected, "opening fill rejects invalid supporting edges");
}
using (var plane = Occ.NurbsSurface(Enumerable.Range(0, 16).Select(i => new Vec3(i / 4 * 10, i % 4 * 10, 0)).ToArray(), 4, 4))
{
    var edges = Occ.FreeEdges(plane);
    var hosts = edges.Select(_ => plane).ToList();
    foreach (int order in new[] { 0, 1, 2 })
    {
        using var patch = Occ.BoundaryFill(hosts, edges, Enumerable.Repeat(order, edges.Length).ToArray(), Enumerable.Repeat(0.5, edges.Length).ToArray(), [], []);
        Check(patch.IsValid && Occ.Distance(plane, patch).Distance < 1e-4, $"boundary fill builds a valid G{order} patch on a planar boundary");
    }
    bool badEdgeRejected = false;
    try { using var invalidPatch = Occ.BoundaryFill(hosts[..1], [-1], [1], [0.5], [], []); } catch (KernelException) { badEdgeRejected = true; }
    Check(badEdgeRejected, "boundary fill rejects an out-of-range edge index");
}
var bsSession = new CadSession();
var square = new[] { Occ.Line(new(0, 0, 0), new(10, 0, 0)), Occ.Line(new(10, 0, 0), new(10, 10, 0)), Occ.Line(new(10, 10, 0), new(0, 10, 0)), Occ.Line(new(0, 10, 0), new(0, 0, 0)) }
    .Select((s, i) => bsSession.AddObject(s, $"Edge {i}", ObjKind.Curve)).ToList();
var bs = new BoundarySurfaceCommand(); bsSession.StartCommand(bs);
bs.ConfigureInputs(() => { foreach (var c in square) bs.Slots[0].Items.Add(SelItem.Of(c)); });
bs.OnChanged();
Check(bs.Error == null && bsSession.Preview.Count > 0, "boundary surface previews a patch over picked curves");
bsSession.CommitCommand();
var bsResult = bsSession.Objects.FirstOrDefault(o => o.Name.StartsWith("Boundary surface"));
Check(bsResult != null && bsResult.Shape.IsValid && bsResult.Shape.Count(SubKind.Face) > 0, "boundary surface commits a valid surface");
SubdMesh Quad() { var m = new SubdMesh(); m.V.AddRange([new(0,0,0),new(10,0,0),new(10,10,0),new(0,10,0)]); m.F.Add([0,1,2,3]); return m; }
var open = Quad(); var r = CageReport.Inspect(open);
Check(r.Valid && r.BoundaryEdges == 4 && r.Components == 1, "an open manifold cage is valid for surface conversion");
var invalid = Quad(); invalid.F.Add([0,1,999]);
Check(!CageReport.Inspect(invalid).Valid && CageReport.Inspect(invalid).InvalidFaces == 1, "invalid indices are reported safely");
var winding = Quad(); winding.V.AddRange([new(10,-10,0),new(0,-10,0)]); winding.F.Add([0,1,4,5]);
Check(CageReport.Inspect(winding).WindingConflicts == 1, "same-direction shared edges are detected");
var bowtie = Quad(); bowtie.V.AddRange([new(-10,0,0),new(-10,-10,0),new(0,-10,0)]); bowtie.F.Add([0,4,5,6]);
Check(CageReport.Inspect(bowtie).DisconnectedVertexFans == 1, "non-manifold bow-tie vertices are detected");
bool blocked = false; try { using var result = FormToBrep.ToShape(bowtie); } catch (KernelException) { blocked = true; }
Check(blocked, "non-manifold cages cannot be silently converted to CAD geometry");
var degenerate = Quad(); degenerate.V[2] = degenerate.V[1]; degenerate.V[3] = degenerate.V[0];
Check(CageReport.Inspect(degenerate).DegenerateFaces == 1, "zero-area faces are detected");
Check(!CageReport.Inspect(new SubdMesh()).Valid, "an empty cage does not pass validation");
var ribSession = new CadSession();
var ribBody = ribSession.AddObject(Occ.Box(20, 20, 10), "Rib target", ObjKind.Solid);
var ribSketch = ribSession.AddObject(Occ.Line(new(-5, 0, 10), new(5, 0, 10)), "Rib sketch", ObjKind.Sketch, plane: Plane3.XZ);
var ribCommand = new RibCommand(); ribSession.StartCommand(ribCommand);
ribCommand.ConfigureInputs(() => { ribCommand.Slots[0].Items.Add(SelItem.Of(ribSketch)); ribCommand.Slots[1].Items.Add(SelItem.Of(ribBody)); });
ribCommand.OnChanged();
Check(ribCommand.Error == null && ribSession.Preview.Count > 0, "rib command previews a rib attached to a solid");
ribSession.CommitCommand();
Check(ribSession.Find(ribBody.Id)!.Shape.Props().Volume > ribBody.Shape.Props().Volume + 100, "rib adds material to the target body");
ribSession.Undo(); Check(Math.Abs(ribSession.Find(ribBody.Id)!.Shape.Props().Volume - ribBody.Shape.Props().Volume) < 1e-6, "rib undo restores the body");
ribSession.Redo(); Check(ribSession.Find(ribBody.Id)!.Shape.Props().Volume > ribBody.Shape.Props().Volume + 100, "rib redo restores the feature");
var detachedSession = new CadSession();
var detachedBody = detachedSession.AddObject(Occ.Box(20, 20, 10), "Target", ObjKind.Solid);
var detachedSketch = detachedSession.AddObject(Occ.Line(new(-5, 0, 30), new(5, 0, 30)), "Sketch", ObjKind.Sketch, plane: Plane3.XZ);
var detachedRib = new RibCommand(); detachedSession.StartCommand(detachedRib);
detachedRib.ConfigureInputs(() => { detachedRib.Slots[0].Items.Add(SelItem.Of(detachedSketch)); detachedRib.Slots[1].Items.Add(SelItem.Of(detachedBody)); });
detachedRib.OnChanged();
Check(detachedRib.Error != null && detachedSession.Preview.Count == 0, "a disconnected rib is rejected");
var session = new CadSession();
var changing = session.AddObject(Occ.NurbsSurface(Net(false, 0), 4, 4), "Changing", ObjKind.Surface);
var fixedPatch = session.AddObject(Occ.NurbsSurface(Net(true, 0), 4, 4), "Reference", ObjKind.Surface);
var command = new MatchSurfaceCommand(); session.StartCommand(command);
command.ConfigureInputs(() =>
{
    command.Slots[0].Items.Add(SelItem.Of(changing)); command.Slots[1].Items.Add(SelItem.Of(fixedPatch));
    ((ChoiceParam)command.Params.Single(p => p.Label == "Reference boundary")).Index = 0;
    ((ChoiceParam)command.Params.Single(p => p.Label == "Continuity")).Index = 2;
});
command.OnChanged();
Check(command.Error == null && session.Preview.Count == 1, "matching produces a preview through the real command lifecycle");
Check(Occ.SurfaceContinuity(changing.Shape, 0, fixedPatch.Shape, 0).Gap > 3, "preview does not modify the source document");
session.CommitCommand();
Check(Occ.SurfaceContinuity(session.Find(changing.Id)!.Shape, 0, fixedPatch.Shape, 0).Gap < 1e-7, "accept applies the matched patch");
session.Undo(); Check(Occ.SurfaceContinuity(session.Find(changing.Id)!.Shape, 0, fixedPatch.Shape, 0).Gap > 3, "undo restores the original patch");
session.Redo(); Check(Occ.SurfaceContinuity(session.Find(changing.Id)!.Shape, 0, fixedPatch.Shape, 0).Gap < 1e-7, "redo restores the matched patch");
var folder = Path.GetFullPath("docs/surfacing-enhancements"); Directory.CreateDirectory(folder);
session.Save(Path.Combine(folder, "G2-matched-patches.prism"));
session.ExportStep(Path.Combine(folder, "G2-matched-patches.step"));
var reopened = new CadSession(); reopened.Open(session.FilePath!);
Check(reopened.Objects.All(o => o.Shape.IsValid) && Occ.SurfaceContinuity(reopened.Objects.First(o => o.Name.StartsWith("Changing")).Shape, 0,
    reopened.Objects.First(o => o.Name.StartsWith("Reference")).Shape, 0).Gap < 1e-7, "saved matching retains valid geometry and seam position");
using var imported = Occ.ImportStep(Path.Combine(folder, "G2-matched-patches.step"));
Check(imported.IsValid && imported.Count(SubKind.Face) == 2, "STEP roundtrip retains both valid patches");
Console.WriteLine($"SUCCESS: {checks} checks");

static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
