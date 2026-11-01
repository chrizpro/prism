using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

/// <summary>Split Face, Merge Faces, Fill / Cap, Extend / Trim Face and Match Face on blocks and plates built in code.</summary>
static class FaceOpsTests
{
    static CadSession S = null!;

    static T Run<T>(T c, Action<T> set) where T : Command { c.Begin(S); c.ConfigureInputs(() => set(c)); return c; }
    static Shape Result(Command c) => c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "No result");
    static int Faces(Shape s) => s.Count(SubKind.Face);
    static double Vol(Shape s) => s.Props().Volume;

    /// <summary>The shape moved so its bounding box starts at the given corner.</summary>
    static Shape At(Shape s, double x, double y, double z) => Occ.Translate(s, new Vec3(x, y, z) - s.Props().Min);

    static int FaceWhere(SceneObject b, Func<FaceInfo, bool> pred)
    {
        for (int f = 0; f < Faces(b.Shape); f++) if (pred(Occ.FaceInfoOf(b.Shape.Sub(SubKind.Face, f)))) return f;
        throw new Exception("no such face");
    }

    static SelItem Face(SceneObject b, int f) => new(b.Id, SelKind.Face, f);

    static bool Refused(Command c)
    {
        try { return c.TryBuild() == null && c.Error != null; } catch (Exception) { return true; }
    }

    public static void Run(Action<bool, string> check)
    {
        S = new CadSession();
        var block = S.AddObject(At(Occ.Box(40, 30, 10), -20, -15, 0), "Block", ObjKind.Solid);
        double v0 = Vol(block.Shape);
        int top = FaceWhere(block, i => i.Type == "Plane" && i.Normal.Z > 0.99);

        // ---- Split Face: the origin plane with a normal along X cuts the top face in two
        int px = Enumerable.Range(0, 3).First(i => Math.Abs(S.PlaneOf(new SelItem(Guid.Empty, SelKind.Origin, i))!.Value.Normal.X) > 0.99);
        var split = Run(new SplitFaceCommand(), c => { c.Slots[0].TryAdd(Face(block, top)); c.Slots[1].TryAdd(new SelItem(Guid.Empty, SelKind.Origin, px)); });
        var halves = Result(split);
        check(halves.IsValid && halves.Count(SubKind.Solid) == 1 && Faces(halves) == Faces(block.Shape) + 1 && Math.Abs(Vol(halves) - v0) < 1e-3, "Split Face: a plane through the middle divides the top face in two and the body keeps its volume");

        // a tool that misses the face is refused
        var far = S.AddObject(At(Occ.Box(40, 30, 10), 100, 100, 0), "Far", ObjKind.Solid);
        var miss = Run(new SplitFaceCommand(), c => { c.Slots[0].TryAdd(Face(far, FaceWhere(far, i => i.Type == "Plane" && i.Normal.Z > 0.99))); c.Slots[1].TryAdd(new SelItem(Guid.Empty, SelKind.Origin, px)); });
        check(Refused(miss), "Split Face: a plane that does not cross the face is refused");

        // ---- Merge Faces: put the split body in the scene and join its two top halves again
        var pieces = S.AddObject(halves, "Pieces", ObjKind.Solid);
        var tops = Enumerable.Range(0, Faces(pieces.Shape)).Where(f => { var i = Occ.FaceInfoOf(pieces.Shape.Sub(SubKind.Face, f)); return i.Type == "Plane" && i.Normal.Z > 0.99; }).ToList();
        check(tops.Count == 2, "the split body has two top faces");
        var merge = Run(new MergeFacesCommand(), c => { foreach (int f in tops) c.Slots[0].TryAdd(Face(pieces, f)); });
        var whole = Result(merge);
        check(whole.IsValid && Faces(whole) == 6 && Math.Abs(Vol(whole) - v0) < 1e-3, "Merge Faces: the two halves of the top become one face again");
        var one = Run(new MergeFacesCommand(), c => { c.Slots[0].TryAdd(Face(block, top)); c.Slots[0].TryAdd(Face(block, FaceWhere(block, i => i.Type == "Plane" && i.Normal.Z < -0.99))); });
        check(Refused(one), "Merge Faces: two faces that are not neighbours on one surface are refused");

        // ---- Fill / Cap: a box shell with its top missing
        var all = Enumerable.Range(0, Faces(block.Shape)).Where(f => f != top).Select(f => block.Shape.Sub(SubKind.Face, f)).ToList();
        var open = S.AddObject(Occ.Sew(all, 0.01, false), "Open box", ObjKind.Surface);
        var free = Occ.FreeEdges(open.Shape);
        check(free.Length == 4, "the box with a missing top has four open edges");
        foreach (var (mode, name) in new[] { (0, "flat cap"), (2, "smooth patch") })
        {
            var fill = Run(new FillCapCommand(), c => { c.Slots[0].TryAdd(new SelItem(open.Id, SelKind.Edge, free[0])); c.Params.OfType<ChoiceParam>().Single(p => p.Label == "Cap").Index = mode; });
            var closed = Result(fill);
            check(closed.IsValid && closed.HasSolids && Math.Abs(Vol(closed) - v0) < 1, $"Fill / Cap: one open edge finds the whole loop; a {name} closes the box into a solid of the same volume");
        }

        // ---- Extend / Trim Face: the +X wall reaches a plane above or below the top
        foreach (var (z, label) in new[] { (15.0, "extends up to a plane 5 mm above the top"), (6.0, "trims down to a plane 4 mm below the top") })
        {
            var refBody = S.AddObject(At(Occ.Box(10, 10, 1), 50, 0, z), "Target", ObjKind.Solid);
            int tf = FaceWhere(refBody, i => i.Type == "Plane" && i.Normal.Z < -0.99);
            int wall = FaceWhere(block, i => i.Type == "Plane" && i.Normal.X > 0.99);
            var ext = Run(new ExtendTrimFaceCommand(), c => { c.Slots[0].TryAdd(Face(block, wall)); c.Slots[1].TryAdd(Face(refBody, tf)); });
            var r = Result(ext);
            check(r.IsValid && Faces(r) == 6 && Math.Abs(Vol(r) - 40 * 30 * z) < 1e-3, $"Extend / Trim Face: the wall {label}");
        }

        // ---- Match Face
        // flat: the top follows a taller block's top
        var tall = S.AddObject(At(Occ.Box(10, 10, 14), 60, 0, 0), "Tall", ObjKind.Solid);
        var match = Run(new MatchFaceCommand(), c => { c.Slots[0].TryAdd(Face(block, top)); c.Slots[1].TryAdd(Face(tall, FaceWhere(tall, i => i.Type == "Plane" && i.Normal.Z > 0.99))); });
        var m1 = Result(match);
        check(m1.IsValid && Faces(m1) == 6 && Math.Abs(Vol(m1) - 40 * 30 * 14) < 1e-3, "Match Face: a flat face moves onto the source plane");

        // cylinder: a 3 mm radius hole takes the radius of a 5 mm cylinder
        var plate = At(Occ.Box(60, 40, 10), 100, 100, 0); var pm = plate.Props().Min;
        var pv0 = Vol(plate);
        var drilled = Occ.Cut(plate, At(Occ.Cylinder(3, 14), pm.X + 27, pm.Y + 17, pm.Z - 2));
        var pb = S.AddObject(drilled, "Plate", ObjKind.Solid);
        var pin = S.AddObject(Occ.Cylinder(5, 10), "Pin", ObjKind.Solid);
        match = Run(new MatchFaceCommand(), c => { c.Slots[0].TryAdd(Face(pb, FaceWhere(pb, i => i.Type == "Cylinder"))); c.Slots[1].TryAdd(Face(pin, FaceWhere(pin, i => i.Type == "Cylinder"))); });
        var m2 = Result(match);
        check(m2.IsValid && Math.Abs(Vol(m2) - (pv0 - Math.PI * 25 * 10)) < 0.5, "Match Face: a hole takes the radius of the source cylinder");

        // cone: a countersink 4 -> 8 mm radius takes a 30 degree wall and keeps its 8 mm mouth
        var sink = At(Occ.Cone(4, 8, 10), pm.X + 22, pm.Y + 12, pm.Z);
        var cb = S.AddObject(Occ.Cut(plate, sink), "Countersunk", ObjKind.Solid);
        double ang = 30 * Math.PI / 180;
        var coneSrc = S.AddObject(Occ.Cone(3, 3 + 10 * Math.Tan(ang), 10), "Cone", ObjKind.Solid);
        match = Run(new MatchFaceCommand(), c => { c.Slots[0].TryAdd(Face(cb, FaceWhere(cb, i => i.Type == "Cone"))); c.Slots[1].TryAdd(Face(coneSrc, FaceWhere(coneSrc, i => i.Type == "Cone"))); });
        var m3 = Result(match);
        double rn = 8 - 10 * Math.Tan(ang), frustum = Math.PI * 10 / 3 * (rn * rn + rn * 8 + 64);
        check(m3.IsValid && Math.Abs(Vol(m3) - (pv0 - frustum)) < 1, $"Match Face: a countersink takes the source cone's angle (volume {Vol(m3):0.#}, expected {pv0 - frustum:0.#})");
        var wrong = Run(new MatchFaceCommand(), c => { c.Slots[0].TryAdd(Face(cb, FaceWhere(cb, i => i.Type == "Cone"))); c.Slots[1].TryAdd(Face(pin, FaceWhere(pin, i => i.Type == "Cylinder"))); });
        check(Refused(wrong), "Match Face: a cone cannot take a cylinder's radius");
    }
}
