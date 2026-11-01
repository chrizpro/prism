using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

/// <summary>Check &amp; Repair on parts with a sliver face, gaps, duplicate and non-manifold faces; Move Body keeping a face flush, a hole coaxial and a cylinder tangent.</summary>
static class RepairMoveTests
{
    static CadSession S = null!;

    static T Run<T>(T c, Action<T> set) where T : Command { c.Begin(S); c.ConfigureInputs(() => set(c)); return c; }
    static void Num(Command c, string l, double v) => c.Params.OfType<NumberParam>().Single(p => p.Label == l).Value = v;
    static void On(Command c, string startsWith) => c.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith(startsWith)).Value = true;
    static void Pick(Command c, string l, int i) => c.Params.OfType<ChoiceParam>().Single(p => p.Label == l).Index = i;
    static Shape Result(Command c) => c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "No result");
    static int Faces(Shape s) => s.Count(SubKind.Face);
    static Shape FaceOf(Shape s, int i) => s.Sub(SubKind.Face, i);
    static Shape Fix(Shape s, ObjKind k, Action<CheckRepairCommand> set, out SceneObject body, out CheckRepairCommand cmd)
    {
        body = S.AddObject(s, "Part", k);
        var b = body; cmd = Run(new CheckRepairCommand(), c => { c.Slots[0].TryAdd(new SelItem(b.Id, SelKind.Object, 0)); set(c); });
        return Result(cmd);
    }

    static int FaceWhere(SceneObject b, Func<FaceInfo, bool> pred)
    {
        for (int f = 0; f < Faces(b.Shape); f++) if (pred(Occ.FaceInfoOf(b.Shape.Sub(SubKind.Face, f)))) return f;
        throw new Exception("no such face");
    }

    public static void Run(Action<bool, string> check)
    {
        S = new CadSession();
        var identity = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        // ---- a block with a 0.02 mm chamfer: a sliver face
        var box = Occ.Box(20, 20, 10); double boxVol = box.Props().Volume;
        var sliver = Occ.Chamfer(box, [0], 0.02);
        var sb = S.AddObject(sliver, "Chamfered", ObjKind.Solid);
        var rep = DirectRepair.Analyse(sb, 0.05);
        check(rep.Valid && rep.IsSolid && rep.SliverFaces.Count == 1 && rep.FreeEdges == 0, "Check: a 0.02 mm chamfer is reported as one sliver face on a valid, closed solid");
        var cmd = Run(new CheckRepairCommand(), c => c.Slots[0].TryAdd(new SelItem(sb.Id, SelKind.Object, 0)));
        check(cmd.TryBuild() == null && cmd.Results.Any(l => l.Label == "Sliver faces" && l.IsWarn), "Check: with nothing ticked it only reports (sliver faces in warning colour)");
        check(cmd.Results.Any(l => l.Label == "Shape check" && l.IsGood), "Check: a valid shape is shown in the good colour");
        var fixedSliver = Fix(sliver, ObjKind.Solid, c => On(c, "Remove tiny"), out _, out cmd);
        check(fixedSliver.IsValid && Faces(fixedSliver) == 6 && Math.Abs(fixedSliver.Props().Volume - boxVol) < 0.5, "Repair: removing the sliver leaves a plain six-face block of the same volume");
        var tolSmall = Run(new CheckRepairCommand(), c => { c.Slots[0].TryAdd(new SelItem(sb.Id, SelKind.Object, 0)); Num(c, "Tiny size (faces and edges thinner than this)", 0.005); });
        tolSmall.TryBuild();
        check(tolSmall.Results.Any(l => l.Label.StartsWith("Sliver") && l.Value == "none"), "Check: the tolerance is a number; 0.005 mm does not flag the chamfer");

        // ---- six loose faces, one of them 0.005 mm off: a gap
        var cube = Occ.Box(20, 20, 20); double cubeVol = cube.Props().Volume;
        var loose = Enumerable.Range(0, 6).Select(i => FaceOf(cube, i)).ToList();
        int top = Enumerable.Range(0, 6).First(i => Occ.FaceInfoOf(loose[i]).Normal.Z > 0.99);
        loose[top] = Occ.Translate(loose[top], new Vec3(0, 0, 0.005));
        var gappy = Occ.Compound(loose);
        var gb = S.AddObject(gappy, "Gappy", ObjKind.Surface);
        rep = DirectRepair.Analyse(gb, 0.05);
        check(!rep.IsSolid && rep.FreeEdges > 0 && rep.Faces == 6, $"Check: six unsewn faces are an open surface ({rep.FreeEdges} open edges)");
        var closed = Fix(gappy, ObjKind.Surface, c => { On(c, "Heal"); On(c, "Close"); }, out _, out cmd);
        check(closed.IsValid && closed.HasSolids && Faces(closed) == 6 && Occ.Check(closed).FreeEdges == 0 && Math.Abs(closed.Props().Volume - cubeVol) < 10,
            $"Repair: sewing within the gap closes the loose faces into a valid solid (volume {closed.Props().Volume:0.##})");
        check(cmd.Results.Any(l => l.Label == "Sewn" && l.IsGood), "Repair: the sewing step is reported");
        var narrow = Run(new CheckRepairCommand(), c => { c.Slots[0].TryAdd(new SelItem(gb.Id, SelKind.Object, 0)); On(c, "Close"); Num(c, "Sewing gap (closes gaps up to this)", 0.001); });
        narrow.TryBuild();
        check(narrow.Results.Any(l => l.Label == "Close into solid" && l.IsWarn) || narrow.TryBuild() == null, "Repair: a gap wider than the sewing gap is not closed, and says so");

        // ---- a duplicate face
        var dupFaces = Enumerable.Range(0, 6).Select(i => FaceOf(cube, i)).ToList(); dupFaces.Add(Occ.Translate(FaceOf(cube, 2), new Vec3(0, 0, 0)));   // a real copy: the same face object twice would count once
        var dup = Occ.Compound(dupFaces);
        var db = S.AddObject(dup, "Dup", ObjKind.Surface);
        rep = DirectRepair.Analyse(db, 0.05);
        check(rep.DuplicateFaces.Count == 1, "Check: a face present twice is reported as a duplicate");
        var healed = Fix(dup, ObjKind.Surface, c => { On(c, "Heal"); On(c, "Close"); }, out _, out cmd);
        check(healed.IsValid && healed.HasSolids && Faces(healed) == 6 && Math.Abs(healed.Props().Volume - cubeVol) < 1, "Repair: the duplicate is dropped and the block sews into a solid");

        // ---- three pages meeting along one edge: non-manifold
        var unit = Occ.Box(10, 10, 10);
        int Plane(Shape s, Func<Vec3, bool> n) { for (int i = 0; i < 6; i++) if (n(Occ.FaceInfoOf(FaceOf(s, i)).Normal)) return i; throw new Exception("no face"); }
        var lo = unit.Props().Min;
        var p1 = FaceOf(unit, Plane(unit, n => n.Y < -0.9));
        var p2 = FaceOf(unit, Plane(unit, n => n.X < -0.9));
        var other = Occ.Translate(unit, new Vec3(-10, 0, 0));
        var p3 = FaceOf(other, Plane(other, n => n.Y < -0.9));
        var book = Occ.Compound([p1, p2, p3]);
        var nb = S.AddObject(book, "Book", ObjKind.Surface);
        rep = DirectRepair.Analyse(nb, 0.05);
        check(rep.NonManifoldEdges >= 1, $"Check: an edge shared by three faces is non-manifold ({rep.NonManifoldEdges})");
        _ = lo; _ = identity;

        // ---- Move Body
        var a = Occ.Box(40, 40, 40); var ao = S.AddObject(a, "A", ObjKind.Solid);
        var bShape = Occ.Translate(Occ.Box(20, 20, 20), new Vec3(60, 5, 30) - Occ.Box(20, 20, 20).Props().Min);
        var bo = S.AddObject(bShape, "B", ObjKind.Solid);
        int aTop = FaceWhere(ao, i => i.Type == "Plane" && i.Normal.Z > 0.99), bBottom = FaceWhere(bo, i => i.Type == "Plane" && i.Normal.Z < -0.99), bTop = FaceWhere(bo, i => i.Type == "Plane" && i.Normal.Z > 0.99);
        var bMin = bShape.Props().Min; double aTopZ = a.Props().Max.Z;
        var mv = Run(new MoveBodyCommand(), c => { c.Slots[0].TryAdd(new SelItem(bo.Id, SelKind.Face, bBottom)); c.Slots[1].TryAdd(new SelItem(ao.Id, SelKind.Face, aTop)); Num(c, "Move X", 5); Num(c, "Move Y", 7); Num(c, "Move Z", 9); });
        var moved = Result(mv); var mm = moved.Props();
        check(Math.Abs(mm.Min.Z - aTopZ) < 1e-6 && Math.Abs(mm.Min.X - (bMin.X + 5)) < 1e-6 && Math.Abs(mm.Min.Y - (bMin.Y + 7)) < 1e-6,
            "Move Body: the bottom face lands on A's top face and the typed 5, 7 slide stays, the Z part is dropped");
        check(Math.Abs(moved.Props().Volume - bShape.Props().Volume) < 1e-6 && mv.Results.Any(l => l.Label == "Typed move limited"), "Move Body: size is unchanged and the limit is reported");
        mv = Run(new MoveBodyCommand(), c => { c.Slots[0].TryAdd(new SelItem(bo.Id, SelKind.Face, bTop)); c.Slots[1].TryAdd(new SelItem(ao.Id, SelKind.Face, aTop)); Pick(c, "Flush means", 1); Num(c, "Move X", -3); Num(c, "Turn about the kept axis", 90); });
        moved = Result(mv); mm = moved.Props();
        check(Math.Abs(mm.Max.Z - aTopZ) < 1e-6 && Math.Abs(mm.Size.X - 20) < 1e-6 && Math.Abs(mm.Size.Z - 20) < 1e-6, "Move Body: aligned faces share one plane after a turn about the plane's normal");
        check(Math.Abs(Occ.FaceInfoOf(FaceOf(moved, bTop)).Normal.Z - 1) < 1e-6, "Move Body: the kept face keeps pointing the same way");

        // a tilted body is squared up by the snap
        var tilt = Occ.Rotate(bShape, bShape.Props().Center, new Vec3(1, 0, 0), 10);
        var to = S.AddObject(tilt, "Tilted", ObjKind.Solid);
        int tBottom = FaceWhere(to, i => i.Type == "Plane" && i.Normal.Z < -0.9);
        mv = Run(new MoveBodyCommand(), c => { c.Slots[0].TryAdd(new SelItem(to.Id, SelKind.Face, tBottom)); c.Slots[1].TryAdd(new SelItem(ao.Id, SelKind.Face, aTop)); });
        moved = Result(mv);
        var fn = Occ.FaceInfoOf(FaceOf(moved, tBottom)).Normal.Normalized(); var fc = Occ.AxisOf(FaceOf(moved, tBottom))!.Value.Origin;
        check(Math.Abs(fn.Z + 1) < 1e-6 && Math.Abs(fc.Z - aTopZ) < 1e-6, "Move Body: a tilted body is turned square and lowered until its face is flush");

        // coaxial: a plate with a hole, and a pin lying elsewhere
        var plate = Occ.Box(60, 60, 10); var plo = plate.Props().Min;
        var drill = Occ.Cylinder(3, 14); var dm = drill.Props().Min;
        plate = Occ.Cut(plate, Occ.Translate(drill, new Vec3(plo.X + 20 - 3 - dm.X, plo.Y + 25 - 3 - dm.Y, plo.Z - 2 - dm.Z)));
        var po = S.AddObject(plate, "Plate", ObjKind.Solid);
        var pin = Occ.Cylinder(3, 12); var pinm = pin.Props().Min;
        pin = Occ.Translate(pin, new Vec3(plo.X + 100 - pinm.X, plo.Y + 80 - pinm.Y, plo.Z + 30 - pinm.Z));
        var pino = S.AddObject(pin, "Pin", ObjKind.Solid);
        int holeFace = FaceWhere(po, i => i.Type == "Cylinder"), pinFace = FaceWhere(pino, i => i.Type == "Cylinder");
        var pc0 = pin.Props().Center;
        mv = Run(new MoveBodyCommand(), c => { c.Slots[0].TryAdd(new SelItem(pino.Id, SelKind.Face, pinFace)); c.Slots[1].TryAdd(new SelItem(po.Id, SelKind.Face, holeFace)); Pick(c, "Keep", 1); Num(c, "Move X", 4); Num(c, "Move Y", 4); Num(c, "Move Z", -6); });
        moved = Result(mv); var pc = moved.Props().Center;
        check(Math.Abs(pc.X - (plo.X + 20)) < 1e-6 && Math.Abs(pc.Y - (plo.Y + 25)) < 1e-6 && Math.Abs(pc.Z - (pc0.Z - 6)) < 1e-6,
            "Move Body: the pin is coaxial with the hole and the typed move runs along the axis only");
        // a pin lying on its side is turned upright
        var lying = Occ.Rotate(pin, pin.Props().Center, new Vec3(0, 1, 0), 90);
        var lo2 = S.AddObject(lying, "Lying pin", ObjKind.Solid);
        int lyFace = FaceWhere(lo2, i => i.Type == "Cylinder");
        mv = Run(new MoveBodyCommand(), c => { c.Slots[0].TryAdd(new SelItem(lo2.Id, SelKind.Face, lyFace)); c.Slots[1].TryAdd(new SelItem(po.Id, SelKind.Face, holeFace)); Pick(c, "Keep", 1); });
        moved = Result(mv); var lm = moved.Props();
        check(Math.Abs(lm.Size.X - 6) < 1e-6 && Math.Abs(lm.Size.Z - 12) < 1e-6 && Math.Abs(lm.Center.X - (plo.X + 20)) < 1e-6 && Math.Abs(lm.Center.Y - (plo.Y + 25)) < 1e-6, "Move Body: a pin on its side is turned upright and set on the hole's axis");

        // tangent: a rod lying above the block rests on its top
        var rod = Occ.Rotate(Occ.Cylinder(4, 30), Vec3.Zero, new Vec3(0, 1, 0), 90);
        rod = Occ.Translate(rod, new Vec3(0, 0, 0) + new Vec3(10, 10, 100) - rod.Props().Min);
        var ro = S.AddObject(rod, "Rod", ObjKind.Solid);
        int rodFace = FaceWhere(ro, i => i.Type == "Cylinder");
        mv = Run(new MoveBodyCommand(), c => { c.Slots[0].TryAdd(new SelItem(ro.Id, SelKind.Face, rodFace)); c.Slots[1].TryAdd(new SelItem(ao.Id, SelKind.Face, aTop)); Pick(c, "Keep", 2); Num(c, "Move X", 2); Num(c, "Move Z", 5); });
        moved = Result(mv); mm = moved.Props();
        check(Math.Abs(mm.Min.Z - aTopZ) < 1e-6 && Math.Abs(mm.Min.X - 12) < 1e-6, "Move Body: a rod rests tangent on the top face and only slides in the plane");
    }
}
