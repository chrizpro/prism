using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;

// Loft, tested against answers worked out by hand. Every shape here has a volume or area that a formula gives, so a wrong result is a wrong number, not a matter of opinion.
//   * each parameter is changed on its own and the result compared with the formula
//   * properties that must hold whatever the shape: scaling by k scales volume by k³, moving or turning the inputs changes nothing, reversing the order changes nothing
//   * every error path says something, and none of them throws
// A failed check is reported and the run carries on, so one run shows everything that is wrong.

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
NativeSearch.Initialize();
int checks = 0, failures = 0;
void Check(bool ok, string message) { checks++; if (!ok) failures++; Console.WriteLine((ok ? "PASS " : "FAIL ") + message); }
void Note(string message) => Console.WriteLine("---- " + message);
bool Near(double got, double want, double rel) => Math.Abs(got - want) <= rel * Math.Max(Math.Abs(want), 1e-9);
string Pct(double got, double want) => $"{got:0.##} against {want:0.##} ({(got - want) / want * 100:+0.00;-0.00}%)";

// ---------------------------------------------------------------- the shapes
Shape Square(double z, double w, double x0 = 0, double y0 = 0, int start = 0, bool reverse = false)
{
    var corners = new List<Vec3> { new(x0, y0, z), new(x0 + w, y0, z), new(x0 + w, y0 + w, z), new(x0, y0 + w, z) };
    if (reverse) corners.Reverse();
    var order = Enumerable.Range(0, 4).Select(i => corners[(i + start) % 4]).ToList();
    return Occ.Polyline(order, true);
}
Shape Disc(double z, double r, double cx = 0, double cy = 0) => Occ.Circle(new Vec3(cx, cy, z), Vec3.UnitZ, r);
double FrustumSquare(double a, double b, double h) => h / 3 * (a * a + a * b + b * b);
double FrustumRound(double r1, double r2, double h) => Math.PI * h / 3 * (r1 * r1 + r1 * r2 + r2 * r2);

var s = new CadSession();
SceneObject Curve(Shape shape, string name = "Section") => s.AddObject(shape, name, ObjKind.Curve);

LoftCommand Make(CadSession session, bool surface, IEnumerable<SceneObject> sections, IEnumerable<SceneObject>? rails = null, Action<LoftCommand>? set = null)
{
    var c = new LoftCommand(surface) { Factory = () => new LoftCommand(surface) };
    c.Begin(session);
    c.ConfigureInputs(() =>
    {
        foreach (var o in sections) c.Slots[0].TryAdd(SelItem.Of(o));
        if (rails != null) foreach (var o in rails) c.Slots[1].TryAdd(SelItem.Of(o));
    });
    set?.Invoke(c); c.OnChanged();
    return c;
}
T P<T>(Command c, string label) where T : Param => c.Params.OfType<T>().First(p => p.Label.StartsWith(label));
string Why(Command c) => c.Error ?? string.Join("; ", c.Results.Select(r => r.Label + ": " + r.Value));
double VolumeOf(CommandOutput o) => o.Add.Count > 0 ? o.Add.Sum(a => a.Shape == null ? 0 : Math.Abs(Occ.Mass(a.Shape).Volume)) : Math.Abs(Occ.Mass(o.Replace[0].New).Volume);
Shape ShapeOf(CommandOutput o) => o.Add.Count > 0 ? o.Add[0].Shape! : o.Replace[0].New;
double slowest = 0;
(double v, Shape shape, CommandOutput o)? Solve(LoftCommand c, string what)
{
    var o = c.TryBuild();
    if (o == null) { Check(false, what + " builds: " + Why(c)); return null; }
    var shape = ShapeOf(o);
    // the viewport meshes every result; a surface that takes seconds to mesh freezes the app
    var clock = System.Diagnostics.Stopwatch.StartNew();
    int triangles = shape.Tessellate().Indices.Length / 3; double seconds = clock.Elapsed.TotalSeconds;
    if (seconds > 1.5 || triangles > 250000) Check(false, $"{what} is cheap to draw: took {seconds:0.#} s and {triangles:N0} triangles");
    slowest = Math.Max(slowest, seconds);
    return (VolumeOf(o), shape, o);
}

// ================================================================ two sections
Note("two sections: the volume of a frustum");
var a = Curve(Square(0, 10)); var b = Curve(Square(20, 12));
var two = Make(s, false, [a, b]);
if (Solve(two, "two squares") is { } sq)
{
    double want = FrustumSquare(10, 12, 20);
    Check(Near(sq.v, want, .005), "a square 10 to a square 12 over 20 is a frustum: volume " + Pct(sq.v, want));
    Check(sq.shape.HasSolids && Occ.Check(sq.shape).Valid, "and it is a valid solid");
    var box = sq.shape.Props();
    Check(Math.Abs(box.Min.Z) < 1e-6 && Near(box.Max.Z, 20, 1e-6) && Near(box.Max.X, 12, 1e-3), $"its extent is where the sections are (z {box.Min.Z:0.##} to {box.Max.Z:0.##}, x up to {box.Max.X:0.##})");
    Check(Near(Math.Abs(Occ.Mass(sq.shape).Area), 2 * 100 / 2.0 * 0 + 100 + 144 + 4 * 11 * Math.Sqrt(400 + 1), .01), "the surface area is the two caps plus four slanted sides");
}
var ruled = Make(s, false, [a, b], null, c => P<BoolParam>(c, "Straight").Value = true);
if (Solve(ruled, "ruled two squares") is { } rr) Check(Near(rr.v, FrustumSquare(10, 12, 20), .005), "straight (ruled) transitions give the same frustum when there are only two sections: " + Pct(rr.v, FrustumSquare(10, 12, 20)));

var c1 = Curve(Disc(0, 10)); var c2 = Curve(Disc(30, 5));
if (Solve(Make(s, false, [c1, c2]), "two circles") is { } cone) Check(Near(cone.v, FrustumRound(10, 5, 30), .005), "two circles make a cone frustum: " + Pct(cone.v, FrustumRound(10, 5, 30)));

Note("different shapes at each end");
var square20 = Curve(Square(30, 20, -10, -10));
if (Solve(Make(s, false, [c1, square20]), "circle to square") is { } mix)
{
    double lo = Math.PI * 100 * 30, hi = 400 * 30;
    Check(mix.shape.HasSolids && Occ.Check(mix.shape).Valid && mix.v > lo && mix.v < hi, $"a circle (r 10) to a square (20 across) is valid and sits between the two prisms: {mix.v:0} in [{lo:0}, {hi:0}]");
}
var hexagon = Curve(Occ.Polyline(Enumerable.Range(0, 6).Select(i => new Vec3(8 * Math.Cos(i * Math.PI / 3), 8 * Math.Sin(i * Math.PI / 3), 20)).ToList(), true));
if (Solve(Make(s, false, [Curve(Disc(0, 8)), hexagon]), "circle to hexagon") is { } hex)
    Check(hex.shape.HasSolids && Occ.Check(hex.shape).Valid && hex.v < Math.PI * 64 * 20 && hex.v > 3 * Math.Sqrt(3) / 2 * 64 * 20 * .98, $"a circle to a hexagon inscribed in it is valid and between the two prisms: {hex.v:0}");

// ================================================================ order and direction
Note("the order, direction and starting corner of the sections");
var reversedOrder = Make(s, false, [b, a]);
if (Solve(reversedOrder, "sections picked top first") is { } ro) Check(Near(ro.v, FrustumSquare(10, 12, 20), .005), "picking the top section first gives the same solid: " + Pct(ro.v, FrustumSquare(10, 12, 20)));
var turned = Curve(Square(20, 12, 0, 0, 2));
if (Solve(Make(s, false, [a, turned]), "second square starting at another corner") is { } st) Check(Near(st.v, FrustumSquare(10, 12, 20), .01), "the second square starting at a different corner does not twist the loft: " + Pct(st.v, FrustumSquare(10, 12, 20)));
var backwards = Curve(Square(20, 12, 0, 0, 0, true));
if (Solve(Make(s, false, [a, backwards]), "second square drawn the other way round") is { } bw) Check(Near(bw.v, FrustumSquare(10, 12, 20), .01), "the second square drawn the other way round does not turn the loft inside out: " + Pct(bw.v, FrustumSquare(10, 12, 20)));

// ================================================================ three sections: smooth against ruled
Note("three sections: smooth and straight transitions");
var r5 = Curve(Disc(0, 5)); var r10 = Curve(Disc(15, 10)); var r5b = Curve(Disc(30, 5));
double Quadratic()
{
    double sum = 0; int n = 3000; double h = 30.0 / n;
    for (int i = 0; i < n; i++) { double z = (i + .5) * h, r = 5 + 5 * (1 - Math.Pow((z - 15) / 15, 2)); sum += Math.PI * r * r * h; }
    return sum;
}
var smooth3 = Make(s, false, [r5, r10, r5b]); var ruled3 = Make(s, false, [r5, r10, r5b], null, c => P<BoolParam>(c, "Straight").Value = true);
if (Solve(ruled3, "ruled barrel") is { } rb)
{
    double want = 2 * FrustumRound(5, 10, 15);
    Check(Near(rb.v, want, .005), "three circles with straight transitions are two cones end to end: " + Pct(rb.v, want));
    if (Solve(smooth3, "smooth barrel") is { } barrel)
    {
        Check(barrel.v > rb.v && barrel.v < Math.PI * 100 * 30, $"smooth bulges more than straight and less than a cylinder: {barrel.v:0} in ({rb.v:0}, {Math.PI * 100 * 30:0})");
        Check(Near(barrel.v, Quadratic(), .03), "and follows the curve through the three radii (a parabola): " + Pct(barrel.v, Quadratic()));
        Check(Occ.Check(barrel.shape).Valid && Near(barrel.shape.Props().Max.X, 10, .02), $"widest at the middle section (x up to {barrel.shape.Props().Max.X:0.###})");
    }
}

// ================================================================ more sections
Note("many sections");
var many = Enumerable.Range(0, 6).Select(i => Curve(Disc(i * 8, 6 + (i % 2) * 2))).ToList();
var manyRuled = Make(s, false, many, null, c => P<BoolParam>(c, "Straight").Value = true);
if (Solve(manyRuled, "six ruled sections") is { } m6)
{
    double want = Enumerable.Range(0, 5).Sum(i => FrustumRound(6 + (i % 2) * 2, 6 + ((i + 1) % 2) * 2, 8));
    Check(Near(m6.v, want, .005), "six circles with straight transitions are five cones: " + Pct(m6.v, want));
}

// ================================================================ closing the loft
Note("close the loft");
Shape Ring(double angleDeg) { double t = angleDeg * Math.PI / 180; var c = new Vec3(20 * Math.Cos(t), 20 * Math.Sin(t), 0); return Occ.Circle(c, new Vec3(-Math.Sin(t), Math.Cos(t), 0), 3); }
var ring = Enumerable.Range(0, 8).Select(i => Curve(Ring(i * 45))).ToList();
var open8 = Make(s, false, ring); var closed8 = Make(s, false, ring, null, c => P<BoolParam>(c, "Close").Value = true);
var openRes = Solve(open8, "eight tube sections, open"); var closedRes = Solve(closed8, "eight tube sections, closed");
if (openRes != null && closedRes != null)
{
    double tube = Math.PI * 9 * 2 * Math.PI * 20;
    Check(closedRes.Value.shape.HasSolids && Occ.Check(closedRes.Value.shape).Valid, "a closed loft is a valid solid");
    Check(closedRes.Value.v > openRes.Value.v * 1.1, $"closing adds the last leg: {closedRes.Value.v:0} closed against {openRes.Value.v:0} open");
    Check(Near(closedRes.Value.v, tube, .05), "and is close to a round tube of radius 3 around a ring of radius 20 (Pappus: area × path): " + Pct(closedRes.Value.v, tube));
    Check(Occ.Check(closedRes.Value.shape).Shells == 1 && Math.Abs(closedRes.Value.shape.Props().Max.Z - 3) < .05, "with one shell, and the tube is 6 across");
}
var ring3 = new[] { 0, 120, 240 }.Select(d => Curve(Ring(d))).ToList();
var closed3 = Make(s, false, ring3, null, c => P<BoolParam>(c, "Close").Value = true); var open3 = Make(s, false, ring3);
if (Solve(closed3, "three tube sections, closed") is { } t3c && Solve(open3, "three tube sections, open") is { } t3o)
    Check(t3c.shape.HasSolids && Occ.Check(t3c.shape).Valid && t3c.v > t3o.v * 1.3, $"three sections also close into a valid solid, though it is a rounded triangle, not a ring ({t3c.v:0} closed, {t3o.v:0} open)");
var closed2 = Make(s, false, [a, b], null, c => P<BoolParam>(c, "Close").Value = true);
if (Solve(closed2, "two sections with Close on") is { } c2r) Check(Near(c2r.v, FrustumSquare(10, 12, 20), .005), "closing does nothing with only two sections");

// ================================================================ ending at a point
Note("end at a sketch point");
var model = new SketchModel(); int px = model.AddPoint(5, 5); model.AddPointEntity(px, false);
var apexPlane = new Plane3(new Vec3(0, 0, 20), Vec3.UnitX, Vec3.UnitY);
s.AddObject(Occ.Vertex(apexPlane.PointAt(5, 5)), "Apex", ObjKind.Sketch, plane: apexPlane, sketch: model);
var toPoint = Make(s, false, [a], null, c => P<ChoiceParam>(c, "End at sketch point").Index = 1);
if (Solve(toPoint, "square to a point") is { } pyr)
{
    Check(Near(pyr.v, 100.0 * 20 / 3, .01), "a square tapering to a point is a pyramid, V = A·h/3: " + Pct(pyr.v, 100.0 * 20 / 3));
    Check(Near(pyr.shape.Props().Max.Z, 20, 1e-3) && Occ.Check(pyr.shape).Valid, "and its tip is at the point");
}
var discToPoint = Make(s, false, [c1], null, c => P<ChoiceParam>(c, "End at sketch point").Index = 1);
if (Solve(discToPoint, "circle to a point") is { } cn) Check(Near(cn.v, Math.PI * 100 * 20 / 3, .01), "a circle tapering to a point is a cone, V = πr²h/3: " + Pct(cn.v, Math.PI * 100 * 20 / 3));
var pointClosed = Make(s, false, [a, b], null, c => { P<ChoiceParam>(c, "End at sketch point").Index = 1; P<BoolParam>(c, "Close").Value = true; });
Check(pointClosed.TryBuild() == null && (pointClosed.Error ?? "").Contains("open loft"), "a point endpoint with Close on is refused, and says why: " + pointClosed.Error);

// ================================================================ guide rails
Note("guide rails");
Shape RailLine(Vec3 from, Vec3 to) => Occ.Line(from, to);
var rail1 = Curve(RailLine(new Vec3(0, 0, 0), new Vec3(0, 0, 20)), "Rail");
var rail2 = Curve(RailLine(new Vec3(10, 10, 0), new Vec3(12, 12, 20)), "Rail");
var plain = Make(s, false, [a, b]); double plainVolume = Solve(plain, "the loft with no rails")?.v ?? 0;
var oneRail = Make(s, false, [a, b], [rail1]);
if (Solve(oneRail, "one straight rail") is { } r1) Check(Near(r1.v, plainVolume, .01), "one straight rail along the loft changes nothing: " + Pct(r1.v, plainVolume));
var twoRails = Make(s, false, [a, b], [rail1, rail2]);
if (Solve(twoRails, "two rails on opposite corners") is { } r2)
{
    Check(Near(r2.v, FrustumSquare(10, 12, 20), .01), "two rails on opposite corners that match the sections give the same frustum: " + Pct(r2.v, FrustumSquare(10, 12, 20)));
    Check(Occ.Check(r2.shape).Valid && r2.shape.HasSolids, "and a valid solid");
}
var railBack = Curve(RailLine(new Vec3(12, 12, 20), new Vec3(10, 10, 0)), "Rail");
if (Solve(Make(s, false, [a, b], [rail1, railBack]), "a rail drawn from the top down") is { } rd) Check(Near(rd.v, FrustumSquare(10, 12, 20), .01), "a rail drawn from the last section to the first works the same: " + Pct(rd.v, FrustumSquare(10, 12, 20)));
var swapped = Make(s, false, [a, b], [rail2, rail1]);
if (Solve(swapped, "the rails in the other order") is { } rs) Check(Near(rs.v, FrustumSquare(10, 12, 20), .01), "the order of two rails does not matter: " + Pct(rs.v, FrustumSquare(10, 12, 20)));
Check(P<IntParam>(plain, "Guide rail accuracy").IsVisible == false && P<IntParam>(twoRails, "Guide rail accuracy").IsVisible, "the rail accuracy setting shows only when there are rails");

// found by building one in the app: sections drawn with the Curve tool are smooth closed splines, not four-edge polygons, and cuts through them behave differently
Vec3[] Corners(double z, double w) { double h = w / 2; return [new(-h, -h, z), new(h, -h, z), new(h, h, z), new(-h, h, z)]; }
var splineA = Curve(Occ.Spline(Corners(0, 40), true)); var splineB = Curve(Occ.Spline(Corners(40, 24), true));
var splineRailA = Curve(Occ.Spline([new Vec3(-20, -20, 0), new Vec3(-12, -12, 40)]), "Rail"); var splineRailB = Curve(Occ.Spline([new Vec3(20, 20, 0), new Vec3(12, 12, 40)]), "Rail");
var splinePlain = Solve(Make(s, false, [splineA, splineB]), "two smooth closed sections");
var splineLoft = Make(s, false, [splineA, splineB], [splineRailA, splineRailB]);
var splineClock = System.Diagnostics.Stopwatch.StartNew();
var splineOut = splineLoft.TryBuild();
Check(splineClock.Elapsed.TotalSeconds < 20, $"smooth closed sections with two straight rails answer quickly, built or refused ({splineClock.Elapsed.TotalSeconds:0.#} s)");
Check(splineOut != null, "smooth closed sections with two rails build: " + splineLoft.Error);
if (splineOut != null)
{
    var g = ShapeOf(splineOut);
    Check(Occ.Check(g).Valid && g.HasSolids && splinePlain != null && Near(VolumeOf(splineOut), splinePlain.Value.v, .05), $"and are a valid solid close to the unguided one ({VolumeOf(splineOut):0} against {splinePlain?.v:0})");
}// a rail that bows away from the profiles: the loft follows it, and the accuracy setting says how many sections do the following.
// A rail that bows along an edge of the profile (still touching it) steers nothing, so this one bows out of the profile, 2 mm at the middle.
Shape Bow() => Occ.Arc3(new Vec3(0, 0, 0), new Vec3(0, -2, 10), new Vec3(0, 0, 20));
var bow = Curve(Bow(), "Bowed rail");
double plainY = Occ.Mass(plain.TryBuild()!.Add[0].Shape!).Cog.Y;
var bowed = new Dictionary<int, (double v, double dy, double minY)>();
foreach (int steps in new[] { 4, 8, 16, 32 })
{
    var c = Make(s, false, [a, b], [bow], x => P<IntParam>(x, "Guide rail accuracy").Value = steps);
    if (Solve(c, $"a bowed rail at {steps} sections") is { } br)
    {
        double dy = Occ.Mass(br.shape).Cog.Y - plainY, minY = br.shape.Props().Min.Y;
        bowed[steps] = (br.v, dy, minY);
        Check(Occ.Check(br.shape).Valid && br.shape.HasSolids, $"{steps} sections along a bowed rail: valid solid, volume {br.v:0.#}, centre moves {dy:0.##} in y, lowest y {minY:0.##}");
        Check(c.Results.Any(x => x.Value.Contains($"{steps} sections")), $"{steps} sections along a bowed rail: the panel says {steps}");
    }
}
// the section at height z moves by -2·(1 - ((z-10)/10)²), which averages -4/3 over the length (weighted a little towards the wider top)
if (bowed.Count == 4)
{
    Check(bowed.Values.All(x => x.dy < -.9 && x.dy > -1.8), $"the loft follows the rail: its centre shifts about 1.3 towards it ({string.Join(", ", bowed.OrderBy(k => k.Key).Select(k => $"{k.Key}: {k.Value.dy:0.##}"))})");
    Check(bowed.Values.All(x => x.minY < -1.5 && x.minY > -2.6), $"and it reaches out to the rail's 2 mm bow at the middle ({string.Join(", ", bowed.OrderBy(k => k.Key).Select(k => $"{k.Key}: {k.Value.minY:0.##}"))})");
    // the cuts are parallel to the sections, not square to the rail, so a leaning rail does not enlarge them (they used to be oblique slices, up to 7% too big at a 23° lean)
    Check(bowed.Values.All(x => Near(x.v, 2426.67, .005)), $"bending the loft keeps its volume ({bowed.Values.Min(x => x.v):0} to {bowed.Values.Max(x => x.v):0} against 2427)");
    Check(Math.Abs(bowed[32].dy - bowed[16].dy) <= Math.Abs(bowed[8].dy - bowed[4].dy) + .02, $"more sections settle the answer: 4→8 moves the centre {Math.Abs(bowed[8].dy - bowed[4].dy):0.###}, 16→32 moves it {Math.Abs(bowed[32].dy - bowed[16].dy):0.###}");
}
var threeRails = Make(s, false, [a, b], [rail1, rail2, bow]);
if (Solve(threeRails, "three rails") is { }) Check(threeRails.Results.Any(x => x.Value.Contains("first two")), "a third rail is ignored, and the panel says so");

// ================================================================ the surface variant
Note("Loft Surface");
var surfaceLoft = Make(s, true, [a, b]);
var surfaceOut = surfaceLoft.TryBuild();
if (surfaceOut != null)
{
    var skin = surfaceOut.Add[0];
    double area = 4 * 11 * Math.Sqrt(400 + 1);
    Check(skin.Kind == ObjKind.Surface && !skin.Shape!.HasSolids, "Loft Surface makes a surface, not a solid");
    Check(Near(Math.Abs(Occ.Mass(skin.Shape!).Area), area, .01), "with no caps, its area is the four slanted sides: " + Pct(Math.Abs(Occ.Mass(skin.Shape!).Area), area));
    Check(!surfaceLoft.Params.OfType<ChoiceParam>().Any(p => p.Label == "Operation"), "and has no Operation choice");
}
else Check(false, "Loft Surface builds: " + Why(surfaceLoft));

// ================================================================ Operation: new body, join, cut, intersect
Note("Operation");
var os = new CadSession();
var block = os.AddObject(Occ.Translate(Occ.Box(20, 20, 10), new Vec3(5, 5, 5)), "Block", ObjKind.Solid);
var oa = os.AddObject(Square(0, 10), "A", ObjKind.Curve); var ob = os.AddObject(Square(20, 12), "B", ObjKind.Curve);
double loftVol = FrustumSquare(10, 12, 20), blockVol = 20 * 20 * 10, shared = (Math.Pow(11.5, 3) - Math.Pow(10.5, 3)) / .3;
(string name, int index, double want)[] ops = [("New body", 0, loftVol), ("Join", 1, blockVol + loftVol - shared), ("Cut", 2, blockVol - shared), ("Intersect", 3, shared)];
foreach (var (name, index, want) in ops)
{
    var c = Make(os, false, [oa, ob], null, x => P<ChoiceParam>(x, "Operation").Index = index);
    if (Solve(c, name) is { } r)
    {
        Check(Near(r.v, want, .01), $"{name}: " + Pct(r.v, want));
        Check(index == 0 ? r.o.Add.Count == 1 && r.o.Replace.Count == 0 : r.o.Replace.Count == 1 && r.o.Replace[0].Old.Id == block.Id, $"{name}: {(index == 0 ? "adds a body and leaves the block alone" : "changes the block and adds nothing")}");
    }
}

// ================================================================ what must hold whatever the shape
Note("properties that hold for every loft");
double baseVolume = FrustumSquare(10, 12, 20);
foreach (double k in new[] { .5, 2, 7 })
{
    var sk = new CadSession();
    var ka = sk.AddObject(Occ.Scale(Square(0, 10), Vec3.Zero, k), "A", ObjKind.Curve); var kb = sk.AddObject(Occ.Scale(Square(20, 12), Vec3.Zero, k), "B", ObjKind.Curve);
    if (Solve(Make(sk, false, [ka, kb]), $"scaled by {k}") is { } scaled) Check(Near(scaled.v, baseVolume * k * k * k, .005), $"scaling the sections by {k} scales the volume by {k * k * k:0.###}: " + Pct(scaled.v, baseVolume * k * k * k));
}
var moved = new CadSession();
var ma = moved.AddObject(Occ.Translate(Square(0, 10), new Vec3(500, -300, 40)), "A", ObjKind.Curve); var mb = moved.AddObject(Occ.Translate(Square(20, 12), new Vec3(500, -300, 40)), "B", ObjKind.Curve);
if (Solve(Make(moved, false, [ma, mb]), "moved far from the origin") is { } far) Check(Near(far.v, baseVolume, .005), "moving the sections far from the origin changes nothing: " + Pct(far.v, baseVolume));
var spun = new CadSession();
var sa = spun.AddObject(Occ.Rotate(Square(0, 10), new Vec3(3, 4, 0), new Vec3(1, 1, 0).Normalized(), 37), "A", ObjKind.Curve); var sb = spun.AddObject(Occ.Rotate(Square(20, 12), new Vec3(3, 4, 0), new Vec3(1, 1, 0).Normalized(), 37), "B", ObjKind.Curve);
if (Solve(Make(spun, false, [sa, sb]), "turned about a slanted axis") is { } turnedBack) Check(Near(turnedBack.v, baseVolume, .005), "turning the sections about a slanted axis changes nothing: " + Pct(turnedBack.v, baseVolume));
foreach (var (what, shapes) in new (string, Shape[])[] { ("circle to circle", [Disc(0, 6), Disc(25, 9)]), ("square to square", [Square(0, 6), Square(25, 9, -1.5, -1.5)]) })
{
    var sx = new CadSession(); var objs = shapes.Select(x => sx.AddObject(x, "S", ObjKind.Curve)).ToList();
    if (Solve(Make(sx, false, objs), what) is { } f1 && Solve(Make(sx, false, Enumerable.Reverse(objs)), what + " reversed") is { } f2) Check(Near(f1.v, f2.v, .001), $"{what}: picking the sections in the opposite order gives the same volume ({f1.v:0.##}, {f2.v:0.##})");
}

// ================================================================ errors say something and never throw
Note("when it cannot work");
var none = Make(s, false, []);
Check(none.TryBuild() == null, "no sections: nothing is built");
var single = Make(s, false, [a]);
Check(single.TryBuild() == null, "one section: nothing is built");
var identical = Make(s, false, [a, Curve(Square(0, 10))]);
var identicalOut = identical.TryBuild();
Check(identicalOut == null ? identical.Error != null : !(Occ.Check(ShapeOf(identicalOut)).Valid && ShapeOf(identicalOut).HasSolids && VolumeOf(identicalOut) > 1), "two sections in the same place give an error, or at worst a result that is not passed off as a solid" + (identical.Error != null ? ": " + identical.Error : " (no solid)"));
var tooFar = Curve(RailLine(new Vec3(500, 500, 0), new Vec3(500, 500, 20)), "Far rail");
var offRail = Make(s, false, [a, b], [tooFar]);
var offOut = offRail.TryBuild();
Check(offOut == null ? offRail.Error != null : true, "a rail nowhere near the sections gives an error or a result, never a crash" + (offRail.Error != null ? ": " + offRail.Error : ""));
if (offOut != null) Console.WriteLine($"    (it built a solid of volume {VolumeOf(offOut):0.#}, against {plainVolume:0.#} with no rail)");
var openLines = Make(s, false, [Curve(Occ.Polyline([new Vec3(0, 0, 0), new Vec3(10, 0, 0), new Vec3(10, 10, 0)], false)), Curve(Occ.Polyline([new Vec3(0, 0, 20), new Vec3(10, 0, 20), new Vec3(10, 10, 20)], false))]);
var openOut = openLines.TryBuild();
Check(openOut == null ? openLines.Error != null : ShapeOf(openOut) != null, "open profiles either build or say why" + (openLines.Error != null ? ": " + openLines.Error : $" (built: solids={ShapeOf(openOut!).HasSolids})"));

// ================================================================ the command as a user meets it
Note("through the panel: commit, undo, redo and the saved recipe");
var us = new CadSession();
var ua = us.AddObject(Square(0, 10), "A", ObjKind.Curve); var ub = us.AddObject(Square(20, 12), "B", ObjKind.Curve);
us.StartCommand(() => new LoftCommand());
var live = us.ActiveCommand!;
live.ConfigureInputs(() => { live.Slots[0].TryAdd(SelItem.Of(ua)); live.Slots[0].TryAdd(SelItem.Of(ub)); });
live.OnChanged();
var recipe = live.Capture();
Check(recipe != null, "a loft can be edited later: it records a recipe");
us.CommitCommand();
var solids = us.Objects.Where(o => o.Kind == ObjKind.Solid).ToList();
Check(solids.Count == 1 && Near(Math.Abs(Occ.Mass(solids[0].Shape).Volume), baseVolume, .005), "pressing OK adds one solid of the right size");
us.Undo();
Check(!us.Objects.Any(o => o.Kind == ObjKind.Solid), "undo takes it away");
us.Redo();
var back = us.Objects.Where(o => o.Kind == ObjKind.Solid).ToList();
Check(back.Count == 1 && Near(Math.Abs(Occ.Mass(back[0].Shape).Volume), baseVolume, .005), "redo brings back the same solid");

var preview = new CadSession();
var pa = preview.AddObject(Square(0, 10), "A", ObjKind.Curve); var pb = preview.AddObject(Square(20, 12), "B", ObjKind.Curve);
var live2 = Make(preview, false, [pa, pb]);
var first = live2.TryBuild(); var second = live2.TryBuild();
Check(first != null && second != null && Near(VolumeOf(first), VolumeOf(second), 1e-9), "building twice from the same inputs gives the same result");
P<BoolParam>(live2, "Straight").Value = true; P<BoolParam>(live2, "Straight").Value = false;
Check(live2.TryBuild() is { } toggled && Near(VolumeOf(toggled), baseVolume, .005), "switching a setting on and off again returns to the same solid");

// a closed curve through points must be as even as its points: it was once lopsided (four corners gave x from -32 to 26)
{
    Vec3[] corners = [new(-20, -20, 0), new(20, -20, 0), new(20, 20, 0), new(-20, 20, 0)];
    var loop = Occ.Spline(corners, true); var lb = loop.Props();
    Check(Math.Abs(lb.Min.X + lb.Max.X) < .01 && Math.Abs(lb.Min.Y + lb.Max.Y) < .01 && Math.Abs(lb.Max.X - lb.Max.Y) < .01, $"a closed spline through four corners is symmetric: x {lb.Min.X:0.##}..{lb.Max.X:0.##}, y {lb.Min.Y:0.##}..{lb.Max.Y:0.##}");
    Check(corners.All(p => Occ.Distance(Occ.Vertex(p), loop).Distance < 1e-6), "and passes through every point");
    var ellipse = Enumerable.Range(0, 8).Select(a => new Vec3(30 * Math.Cos(a * Math.PI / 4), 20 * Math.Sin(a * Math.PI / 4), 0)).ToArray();
    var eb = Occ.Spline(ellipse, true).Props();
    Check(Math.Abs(eb.Max.X - 30) < .02 && Math.Abs(eb.Min.X + 30) < .02 && Math.Abs(eb.Max.Y - 20) < .02 && Math.Abs(eb.Min.Y + 20) < .02, $"eight points on an ellipse stay within 0.02 of it: x {eb.Min.X:0.###}..{eb.Max.X:0.###}");
}
Check(slowest < 1.5, $"every result in this suite was drawn in under 1.5 s (slowest {slowest:0.##} s)");
Console.WriteLine();
Console.WriteLine(failures == 0 ? $"SUCCESS: {checks} checks" : $"{checks - failures} of {checks} checks passed, {failures} failed");
return failures == 0 ? 0 : 1;

static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
