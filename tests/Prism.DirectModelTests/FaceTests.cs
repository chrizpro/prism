using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

/// <summary>Offset Face, Rotate Face and Remove Fillets on a plate with holes and on a filleted block.</summary>
static class FaceTests
{
    static CadSession S = null!;

    static T Run<T>(T c, Action<T> set) where T : Command { c.Begin(S); c.ConfigureInputs(() => set(c)); return c; }
    static void Num(Command c, string l, double v) => c.Params.OfType<NumberParam>().Single(p => p.Label == l).Value = v;
    static Shape Result(Command c) => c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "No result");
    static int Faces(Shape s) => s.Count(SubKind.Face);

    static int FaceWhere(SceneObject b, Func<FaceInfo, bool> pred)
    {
        for (int f = 0; f < Faces(b.Shape); f++) if (pred(Occ.FaceInfoOf(b.Shape.Sub(SubKind.Face, f)))) return f;
        throw new Exception("no such face");
    }

    public static void Run(Action<bool, string> check)
    {
        S = new CadSession();
        // a 100 x 80 x 10 plate with two 6 mm through holes
        var plate = Occ.Box(100, 80, 10); var lo = plate.Props().Min;
        foreach (var (x, y) in new[] { (25.0, 40.0), (75.0, 40.0) })
        {
            var c = Occ.Cylinder(3, 14); var m = c.Props().Min;
            plate = Occ.Cut(plate, Occ.Translate(c, new Vec3(lo.X + x - 3 - m.X, lo.Y + y - 3 - m.Y, lo.Z - 2 - m.Z)));
        }
        var body = S.AddObject(plate, "Plate", ObjKind.Solid);
        double vol = plate.Props().Volume, area = 100 * 80 - 2 * Math.PI * 9;
        int top = FaceWhere(body, i => i.Type == "Plane" && i.Normal.Z > 0.99), hole = FaceWhere(body, i => i.Type == "Cylinder");

        var off = Run(new OffsetFaceCommand(), c => { c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, top)); Num(c, "Offset distance (positive moves faces out)", 2); });
        var r = Result(off);
        check(r.IsValid && Math.Abs(r.Props().Volume - (vol + 2 * area)) < 1 && Faces(r) == Faces(plate), "Offset Face: the top of a plate with holes moves out 2 mm");
        off = Run(new OffsetFaceCommand(), c => { c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, top)); Num(c, "Offset distance (positive moves faces out)", -3); });
        r = Result(off);
        check(r.IsValid && Math.Abs(r.Props().Volume - (vol - 3 * area)) < 1, "Offset Face: a negative distance moves the top face in");

        off = Run(new OffsetFaceCommand(), c => { c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, hole)); Num(c, "Offset distance (positive moves faces out)", 1); });
        r = Result(off);
        check(r.IsValid && Math.Abs(r.Props().Volume - (vol + Math.PI * (9 - 4) * 10)) < 1, "Offset Face: a whole hole offset out by 1 mm shrinks from radius 3 to 2");
        off = Run(new OffsetFaceCommand(), c => { c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, hole)); Num(c, "Offset distance (positive moves faces out)", -1); });
        r = Result(off);
        check(r.IsValid && Math.Abs(r.Props().Volume - (vol - Math.PI * (16 - 9) * 10)) < 1, "Offset Face: a whole hole offset in by 1 mm grows to radius 4");

        // rotate the +X side about world Y through its middle: equal wedge added and removed
        int side = FaceWhere(body, i => i.Type == "Plane" && i.Normal.X > 0.99);
        var rot = Run(new RotateFaceCommand(), c => { c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, side)); Num(c, "Rotation angle", 10); c.Params.OfType<ChoiceParam>().Single(p => p.Label == "Rotate about").Index = 3; });
        r = Result(rot);
        check(r.IsValid && Math.Abs(r.Props().Volume - vol) < 2 && Faces(r) == Faces(plate), "Rotate Face: the side tilts about world Y and the plate keeps its volume");

        // rotate it about the bottom edge of that side (a line along Y): a wedge of 0.5 * h^2 * tan(a) * L
        int edge = -1;
        for (int e = 0; e < body.Shape.Count(SubKind.Edge) && edge < 0; e++)
        {
            var ei = Occ.EdgeInfoOf(body.Shape.Sub(SubKind.Edge, e));
            if (ei.Type == "Line" && Math.Abs(ei.End.Y - ei.Start.Y) > 70 && Math.Abs(ei.Start.X - (lo.X + 100)) < 1e-3 && Math.Abs(ei.Start.Z - lo.Z) < 1e-3) edge = e;
        }
        rot = Run(new RotateFaceCommand(), c => { c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, side)); c.Slots[1].TryAdd(new SelItem(body.Id, SelKind.Edge, edge)); Num(c, "Rotation angle", -10); });
        r = Result(rot);
        double wedge = 0.5 * 10 * 10 * Math.Tan(10 * Math.PI / 180) * 80;
        check(r.IsValid && Math.Abs(Math.Abs(r.Props().Volume - vol) - wedge) < wedge * 0.03, $"Rotate Face: turning a side about its bottom edge changes the volume by the wedge ({wedge:0.#} mm³)");

        // a block with one filleted edge
        var block = Occ.Box(40, 30, 20); int fe = -1;
        for (int e = 0; e < block.Count(SubKind.Edge) && fe < 0; e++) { var ei = Occ.EdgeInfoOf(block.Sub(SubKind.Edge, e)); if (ei.Type == "Line" && Math.Abs((ei.End - ei.Start).Z) > 15 && ei.Start.X > block.Props().Min.X + 1 && ei.Start.Y > block.Props().Min.Y + 1) fe = e; }
        double boxVol = block.Props().Volume;
        var fb = S.AddObject(Occ.Fillet(block, [fe], 4), "Block", ObjKind.Solid);
        int round = FaceWhere(fb, i => i.Type == "Cylinder");
        var rm = Run(new RemoveFilletsCommand(), c => c.Slots[0].TryAdd(new SelItem(fb.Id, SelKind.Face, round)));
        r = Result(rm);
        check(r.IsValid && Faces(r) == 6 && Math.Abs(r.Props().Volume - boxVol) < 0.5, "Remove Fillets: a picked fillet is removed and the block is a plain box again");
        rm = Run(new RemoveFilletsCommand(), c => { c.Slots[1].TryAdd(new SelItem(fb.Id, SelKind.Object, 0)); Num(c, "Also remove all rounds up to radius (0 = only picked)", 5); });
        r = Result(rm);
        check(r.IsValid && Faces(r) == 6 && Math.Abs(r.Props().Volume - boxVol) < 0.5, "Remove Fillets: all rounds up to radius 5 removes the 4 mm fillet");
        rm = Run(new RemoveFilletsCommand(), c => { c.Slots[1].TryAdd(new SelItem(fb.Id, SelKind.Object, 0)); Num(c, "Also remove all rounds up to radius (0 = only picked)", 2); });
        check(rm.TryBuild() == null, "Remove Fillets: a 2 mm limit leaves the 4 mm fillet alone");
    }
}
