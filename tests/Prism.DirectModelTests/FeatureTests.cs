using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

/// <summary>Feature recognition: what each face of a plate with a round hole, a slot, a pocket, two bosses and a filleted block turns out to be.</summary>
static class FeatureTests
{
    /// <summary>A 120 x 80 x 10 plate: round through hole, through slot (26 x 6), blind pocket (16 x 10 x 5), rectangular boss (12 x 12 x 8) and round boss (r 4, h 6).</summary>
    public static (CadSession, SceneObject) BuildPlate()
    {
        var plate = Occ.Box(120, 80, 10); var lo = plate.Props().Min;
        // put a shape's own bounding-box corner at a spot on the plate (the kernel's primitives are not all anchored the same way)
        Shape Rel(Shape s, double x, double y, double z) { var m = s.Props().Min; return Occ.Translate(s, new Vec3(lo.X + x - m.X, lo.Y + y - m.Y, lo.Z + z - m.Z)); }
        Shape Cyl(double r, double h, double cx, double cy, double z) => Rel(Occ.Cylinder(r, h), cx - r, cy - r, z);
        // round through hole, through slot (box + two ends), blind rectangular pocket
        plate = Occ.Cut(plate, Cyl(3, 12, 15, 15, -1));
        plate = Occ.Cut(plate, Occ.Fuse(Occ.Fuse(Rel(Occ.Box(20, 6, 12), 40, 12, -1), Cyl(3, 12, 40, 15, -1)), Cyl(3, 12, 60, 15, -1)));
        plate = Occ.Cut(plate, Rel(Occ.Box(16, 10, 5), 80, 10, 5.001));
        // a rectangular boss and a round boss standing on top
        plate = Occ.Fuse(plate, Rel(Occ.Box(12, 12, 8), 15, 50, 10));
        plate = Occ.Fuse(plate, Cyl(4, 6, 60, 55, 10));
        var s = new CadSession(); var body = s.AddObject(plate, "Plate", ObjKind.Solid);
        return (s, body);
    }

    public static void Run(Action<bool, string> check)
    {
        var (s, body) = BuildPlate();
        var feats = FeatureFinder.Features(body);
        foreach (var k in Enum.GetValues<FeatureKind>()) Console.WriteLine($"  {k,-8} {feats.Count(f => f.Kind == k)}");
        int Count(FeatureKind k) => feats.Count(f => f.Kind == k);
        check(Count(FeatureKind.Slot) == 1, "the slot is one feature, however many faces it has");
        check(feats.First(f => f.Kind == FeatureKind.Slot).Faces.Count == 4, "a slot is its two flat walls and its two round ends");
        check(Count(FeatureKind.Pocket) == 1 && feats.First(f => f.Kind == FeatureKind.Pocket).Faces.Count == 5, "a blind rectangular pocket is its four walls and its floor");
        check(Count(FeatureKind.Hole) == 1, "the round hole is recognised");
        check(Count(FeatureKind.Boss) == 2, "the rectangular boss and the round boss are both bosses");
        check(feats.Where(f => f.Kind == FeatureKind.Boss).Any(f => f.Faces.Count == 5), "the rectangular boss is its four side walls and its top");

        // clicking any face of the slot, whichever kind it is, gives the whole slot
        var slot = feats.First(f => f.Kind == FeatureKind.Slot);
        foreach (int face in slot.Faces) check(FeatureFinder.Expand(body, face, PickIntent.Feature).Count == 4, $"clicking slot face {face} selects the whole slot");
        check(FeatureFinder.Expand(body, slot.Faces[0], PickIntent.Face).Count == 1, "the Single face intent still picks one face");
        // the outside of the plate is not mistaken for a feature
        var side = Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(f => FeatureFinder.FeatureOf(body, f)!.Kind == FeatureKind.Face && Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, f)).Type == "Plane");
        check(FeatureFinder.Expand(body, side, PickIntent.Feature).Count == 1, "a plain flat face stays a single face");

        // a filleted block: the fillet faces chain together, the flat faces do not
        var block = Occ.Box(40, 30, 20); int edge = -1;
        for (int e = 0; e < block.Count(SubKind.Edge) && edge < 0; e++) { var ei = Occ.EdgeInfoOf(block.Sub(SubKind.Edge, e)); if (ei.Type == "Line" && Math.Abs((ei.End - ei.Start).Z) > 15 && ei.Start.X > block.Props().Min.X + 1 && ei.Start.Y > block.Props().Min.Y + 1) edge = e; }
        var filleted = Occ.Fillet(block, [edge], 4);
        var fb = s.AddObject(filleted, "Block", ObjKind.Solid);
        var fillets = FeatureFinder.Features(fb).Where(f => f.Kind == FeatureKind.Fillet).ToList();
        check(fillets.Count == 1 && fillets[0].Faces.Count == 1, "a fillet is recognised as a fillet");
        check(FeatureFinder.Expand(fb, fillets[0].Faces[0], PickIntent.Tangent).Count == 3, "Tangent chain follows a fillet into the flat faces it blends with");
        // the real click path: Command.Pick with the panel's "Select by" choice
        var del = new Prism.Commands.DeleteFaceCommand(); del.Begin(s);
        var slotSlot = del.Slots[0];
        del.Pick(new SelItem(body.Id, SelKind.Face, slot.Faces[2]), false);
        check(slotSlot.Items.Count == 4, "clicking one slot wall in Delete Face selects the whole slot (4 faces)");
        del.Pick(new SelItem(body.Id, SelKind.Face, slot.Faces[0]), false);
        check(slotSlot.Items.Count == 0, "clicking the slot again deselects it");
        slotSlot.IntentIndex = 1; del.Pick(new SelItem(body.Id, SelKind.Face, slot.Faces[2]), false);
        check(slotSlot.Items.Count == 1, "with Single face selected, a click picks one face");
        slotSlot.Items.Clear(); slotSlot.IntentIndex = 0; del.Pick(new SelItem(body.Id, SelKind.Face, slot.Faces[2]), false);
        var removed = del.TryBuild() ?? throw new Exception(del.Error ?? "Delete Face failed");
        var after = removed.Replace.Single().New;
        double slotVolume = body.Shape.Props().Volume;
        check(after.IsValid && after.Props().Volume > slotVolume + 20 * 6 * 10 * 0.9 && after.Count(SubKind.Face) == body.Shape.Count(SubKind.Face) - 4, "Delete Face on a selected slot fills it in and leaves 4 fewer faces");
        var mh = new Prism.Commands.MoveHoleCommand(); mh.Begin(s);
        mh.Pick(new SelItem(body.Id, SelKind.Face, slot.Faces[0]), false);
        mh.Params.OfType<NumberParam>().Single(p => p.Label == "Move Y").Value = 5;
        check(mh.TryBuild() == null && mh.Error!.Contains("slot"), "Move Hole on a slot says it is a slot and what to use instead");

        FeatureEditTests.Run(check);
        Dump.Plate(body, slot, FeatureFinder.Features(body));
        Dump.Patterns(check);

        // a plain box has no features other than faces
        var plain = s.AddObject(Occ.Box(30, 20, 10), "Box", ObjKind.Solid);
        check(FeatureFinder.Features(plain).All(f => f.Kind == FeatureKind.Face), "a plain box is just faces: its outside is not a boss");
    }
}

/// <summary>Writes what the tests saw (geometry, recognised features, selections) as JSON so it can be drawn. Only when PRISM_TEST_IMAGES names a folder.</summary>
static class Dump
{
    static string? Dir => Environment.GetEnvironmentVariable("PRISM_TEST_IMAGES");

    public static void Plate(SceneObject body, Feat slot, IReadOnlyList<Feat> feats)
    {
        if (Dir == null) return; var m = body.Mesh;
        int faces = body.Shape.Count(SubKind.Face); var kind = new string[faces]; var feature = new int[faces];
        for (int f = 0; f < faces; f++) { var ft = FeatureFinder.FeatureOf(body, f)!; kind[f] = ft.Kind.ToString(); feature[f] = feats.ToList().IndexOf(ft); }
        var clicked = FeatureFinder.Expand(body, slot.Faces[2], PickIntent.Feature);
        File.WriteAllText(Path.Combine(Dir, "plate.json"), System.Text.Json.JsonSerializer.Serialize(new
        { positions = m.Positions, indices = m.Indices, triStart = m.FaceTriStart, kind, feature, clicked, clickedFace = slot.Faces[2] }));
    }

    public static void Patterns(Action<bool, string> check)
    {
        var scenes = new List<object>();
        foreach (var (name, pts, expect) in new (string, (double x, double y)[], int)[]
        {
            ("Line of 4 plus a stray hole", [(-30, 0), (-10, 0), (10, 0), (30, 0), (20, 20)], 4),
            ("Grid of 3 x 2 plus a stray hole", [(-30, -10), (0, -10), (30, -10), (-30, 10), (0, 10), (30, 10), (40, 25)], 6),
            ("Ring of 6 plus a stray hole", [.. Enumerable.Range(0, 6).Select(i => (22 * Math.Cos(i * Math.PI / 3), 22 * Math.Sin(i * Math.PI / 3))), (8.0, 8.0)], 6),
        })
        {
            var plate = Occ.Box(100, 70, 8); var lo = plate.Props().Min;
            foreach (var (x, y) in pts)
            { var c = Occ.Cylinder(3, 10); var cm = c.Props().Min; plate = Occ.Cut(plate, Occ.Translate(c, new Vec3(lo.X + 50 + x - 3 - cm.X, lo.Y + 35 + y - 3 - cm.Y, lo.Z - 1 - cm.Z))); }
            var s = new CadSession(); var body = s.AddObject(plate, "P", ObjKind.Solid);
            var holes = FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Hole).ToList();
            var first = holes.OrderBy(f => f.Centre.X).First();
            int[] Pick(PickIntent i) => FeatureFinder.Expand(body, first.Face, i).Select(f => holes.FindIndex(h => h.Faces.Contains(f))).Distinct().ToArray();
            check(Pick(PickIntent.Pattern).Length == expect && Pick(PickIntent.Feature).Length == 1 && Pick(PickIntent.SameSize).Length == pts.Length, $"{name}: Pattern takes {expect}, Same size takes all {pts.Length}, Whole feature takes 1");
            if (Dir != null) scenes.Add(new { name, holes = holes.Select(h => new[] { h.Centre.X - lo.X, h.Centre.Y - lo.Y }).ToArray(), w = 100, h = 70, picked = holes.IndexOf(first),
                single = Pick(PickIntent.Face), feature = Pick(PickIntent.Feature), sameSize = Pick(PickIntent.SameSize), pattern = Pick(PickIntent.Pattern) });
        }
        if (Dir != null) File.WriteAllText(Path.Combine(Dir, "patterns.json"), System.Text.Json.JsonSerializer.Serialize(scenes));
    }
}
