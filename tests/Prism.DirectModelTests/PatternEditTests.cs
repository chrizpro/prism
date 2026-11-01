using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

/// <summary>Edit Pattern (spacing, count, removing an instance for lines and rings) and Mirror Feature, checked against exact volumes and feature positions.</summary>
static class PatternEditTests
{
    const double HoleV = Math.PI * 9 * 10;

    static (CadSession, SceneObject) Drilled(double w, double d, IEnumerable<(double x, double y)> holes)
    {
        Shape plate = Occ.Box(w, d, 10); var lo = plate.Props().Min;
        foreach (var (x, y) in holes)
        {
            using var cyl = Occ.Cylinder(3, 12); var m = cyl.Props().Min;
            plate = Occ.Cut(plate, Occ.Translate(cyl, new Vec3(lo.X + x - 3 - m.X, lo.Y + y - 3 - m.Y, lo.Z - 1 - m.Z)));
        }
        var s = new CadSession(); return (s, s.AddObject(plate, "Plate", ObjKind.Solid));
    }

    static List<Vec3> HoleCentres(Shape sh)
    {
        var s = new CadSession(); var b = s.AddObject(sh, "Check", ObjKind.Solid);
        return FeatureFinder.Features(b).Where(f => f.Kind == FeatureKind.Hole).Select(f => f.Centre).ToList();
    }

    static Shape Edit(CadSession s, SceneObject body, Feat f, Action<EditPatternCommand> set)
    {
        var c = new EditPatternCommand(); c.Begin(s);
        c.ConfigureInputs(() => { c.Slots[0].Items.Clear(); c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, f.Faces[0])); set(c); });
        return c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "Edit Pattern built nothing");
    }
    static void Num(EditPatternCommand c, string label, double v) => c.Params.OfType<NumberParam>().Single(p => p.Label == label).Value = v;

    public static void Run(Action<bool, string> check)
    {
        // a line of four holes, 20 apart, picked at the first one
        var (s, body) = Drilled(160, 60, [(20, 30), (40, 30), (60, 30), (80, 30)]);
        double v0 = body.Shape.Props().Volume; double lo0 = body.Shape.Props().Min.X;
        var first = FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Hole).OrderBy(f => f.Centre.X).First();
        var pat = FeatureFinder.PatternOf(body, first);
        check(pat.Kind == FeatureFinder.PatternKind.Line && pat.Others.Count == 3 && Math.Abs(pat.V1.Length - 20) < 1e-3, "PatternOf reads a line of four holes with 20 mm spacing");

        var wide = Edit(s, body, first, c => Num(c, "Spacing", 30));
        var xs = HoleCentres(wide).Select(p => Math.Round(p.X - lo0, 3)).OrderBy(x => x).ToList();
        check(wide.IsValid && Math.Abs(wide.Props().Volume - v0) < 1 && xs.SequenceEqual([20, 50, 80, 110]), "Edit Pattern re-spaces a line of holes from the clicked one without changing the volume");
        var fewer = Edit(s, body, first, c => Num(c, "Number of instances", 3));
        check(fewer.IsValid && Math.Abs(fewer.Props().Volume - (v0 + HoleV)) < 1 && HoleCentres(fewer).Count == 3, "Edit Pattern with a smaller count fills the surplus holes");
        var more = Edit(s, body, first, c => Num(c, "Number of instances", 6));
        check(more.IsValid && Math.Abs(more.Props().Volume - (v0 - 2 * HoleV)) < 1 && HoleCentres(more).Count == 6, "Edit Pattern with a larger count adds holes");
        var gap = Edit(s, body, first, c => Num(c, "Remove instance (0 = none)", 3));
        xs = HoleCentres(gap).Select(p => Math.Round(p.X - lo0, 3)).OrderBy(x => x).ToList();
        check(gap.IsValid && Math.Abs(gap.Props().Volume - (v0 + HoleV)) < 1 && xs.SequenceEqual([20, 40, 80]), "Edit Pattern removes one instance by number");
        bool refused = false; try { Edit(s, body, first, c => Num(c, "Remove instance (0 = none)", 1)); } catch (Exception e) { refused = e.Message.Contains("clicked"); }
        check(refused, "Edit Pattern will not remove the clicked feature");

        // a ring of six holes about (50, 50)
        var (rs, rb) = Drilled(100, 100, Enumerable.Range(0, 6).Select(i => (50 + 30 * Math.Cos(i * Math.PI / 3), 50 + 30 * Math.Sin(i * Math.PI / 3))));
        double rv = rb.Shape.Props().Volume;
        var rf = FeatureFinder.Features(rb).First(f => f.Kind == FeatureKind.Hole);
        var ring = FeatureFinder.PatternOf(rb, rf);
        check(ring.Kind == FeatureFinder.PatternKind.Circle && ring.Others.Count == 5 && Math.Abs(ring.Step - Math.PI / 3) < 1e-6, "PatternOf reads a ring of six holes with a 60 degree step");
        var four = Edit(rs, rb, rf, c => Num(c, "Number of instances", 4));
        check(four.IsValid && Math.Abs(four.Props().Volume - (rv + 2 * HoleV)) < 1 && HoleCentres(four).Count == 4, "Edit Pattern with a smaller ring count fills two holes");
        var tight = Edit(rs, rb, rf, c => { Num(c, "Angular step", 30); Num(c, "Number of instances", 6); });
        check(tight.IsValid && Math.Abs(tight.Props().Volume - rv) < 1 && HoleCentres(tight).Count == 6, "Edit Pattern squeezes a ring to a 30 degree step");

        // an open arc of three holes (0, 45, 90 degrees) grows on the side it already extends to
        var (asn, ab) = Drilled(100, 100, new[] { 0, 45, 90 }.Select(a => (50 + 30 * Math.Cos(a * Math.PI / 180), 50 + 30 * Math.Sin(a * Math.PI / 180))));
        var lo = ab.Shape.Props().Min; var af = FeatureFinder.Features(ab).Where(f => f.Kind == FeatureKind.Hole).OrderBy(f => f.Centre.Y).First();
        var arc = Edit(asn, ab, af, c => Num(c, "Number of instances", 4));
        double want = 135 * Math.PI / 180; var got = HoleCentres(arc);
        check(arc.IsValid && got.Count == 4 && got.Any(p => Math.Abs(p.X - (lo.X + 50 + 30 * Math.Cos(want))) < 0.05 && Math.Abs(p.Y - (lo.Y + 50 + 30 * Math.Sin(want))) < 0.05), "Edit Pattern extends an open arc in its own direction");

        // mirror a boss and a pocket
        var (ps, pb) = FeatureTests.BuildPlate(); double pv = pb.Shape.Props().Volume; var pmin = pb.Shape.Props().Min;
        var pfeats = FeatureFinder.Features(pb);
        var box = pfeats.First(f => f.Kind == FeatureKind.Boss && f.Faces.Count == 5); var pocket = pfeats.First(f => f.Kind == FeatureKind.Pocket);
        SelItem MirrorPlane(int axis, double at)
        {
            var cube = Occ.Box(10, 10, 10); var cm = cube.Props().Min;
            var aux = ps.AddObject(Occ.Translate(cube, new Vec3((axis == 0 ? pmin.X + at : pmin.X) - cm.X, (axis == 1 ? pmin.Y + at : pmin.Y) - cm.Y, pmin.Z - cm.Z)), "Aux", ObjKind.Solid);
            for (int i = 0; i < aux.Shape.Count(SubKind.Face); i++)
                if (ps.PlaneOf(new SelItem(aux.Id, SelKind.Face, i)) is { } p && Math.Abs(Vec3.Dot(p.Normal.Normalized(), axis == 0 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0))) > 0.99
                    && Math.Abs((axis == 0 ? p.Origin.X : p.Origin.Y) - ((axis == 0 ? pmin.X : pmin.Y) + at)) < 1e-6) return new SelItem(aux.Id, SelKind.Face, i);
            throw new Exception("no mirror face");
        }
        Shape Mirror(SceneObject b, Feat f, SelItem plane)
        {
            var c = new MirrorFeatureCommand(); c.Begin(ps);
            c.ConfigureInputs(() => { c.Slots[0].Items.Clear(); c.Slots[0].TryAdd(new SelItem(b.Id, SelKind.Face, f.Faces[0])); c.Slots[1].Items.Clear(); c.Slots[1].TryAdd(plane); });
            return c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "Mirror Feature built nothing");
        }
        var mb = Mirror(pb, box, MirrorPlane(0, 60));
        check(mb.IsValid && Math.Abs(mb.Props().Volume - (pv + 12 * 12 * 8)) < 2, "Mirror Feature adds a twin of a boss across a plane");
        var mp = Mirror(pb, pocket, MirrorPlane(1, 40));
        check(mp.IsValid && Math.Abs(mp.Props().Volume - (pv - 16 * 10 * 4.999)) < 2, "Mirror Feature adds a twin of a pocket across a plane");
    }
}
