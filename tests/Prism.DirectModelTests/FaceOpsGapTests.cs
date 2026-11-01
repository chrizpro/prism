using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

/// <summary>Merge Faces joins only the faces you pick; a finished import reports bodies that need repair.</summary>
static class FaceOpsGapTests
{
    static CadSession S = null!;
    static T Run<T>(T c, Action<T> set) where T : Command { c.Begin(S); c.ConfigureInputs(() => set(c)); return c; }
    static Shape Result(Command c) => c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "No result");
    static int Faces(Shape s) => s.Count(SubKind.Face);
    static Shape At(Shape s, double x, double y, double z) => Occ.Translate(s, new Vec3(x, y, z) - s.Props().Min);
    static List<int> FacesWhere(SceneObject b, Func<FaceInfo, bool> pred) => Enumerable.Range(0, Faces(b.Shape)).Where(f => pred(Occ.FaceInfoOf(b.Shape.Sub(SubKind.Face, f)))).ToList();

    public static void Run(Action<bool, string> check)
    {
        S = new CadSession();
        var block = S.AddObject(At(Occ.Box(40, 30, 10), -20, -15, 0), "Block", ObjKind.Solid);
        double v0 = block.Shape.Props().Volume;
        int px = Enumerable.Range(0, 3).First(i => Math.Abs(S.PlaneOf(new SelItem(Guid.Empty, SelKind.Origin, i))!.Value.Normal.X) > 0.99);
        // split the top face and then the bottom face down the middle: two separate pairs of faces on one surface each
        var top = FacesWhere(block, i => i.Type == "Plane" && i.Normal.Z > 0.99)[0];
        var one = Result(Run(new SplitFaceCommand(), c => { c.Slots[0].TryAdd(new SelItem(block.Id, SelKind.Face, top)); c.Slots[1].TryAdd(new SelItem(Guid.Empty, SelKind.Origin, px)); }));
        var b1 = S.AddObject(one, "SplitTop", ObjKind.Solid);
        var bottom = FacesWhere(b1, i => i.Type == "Plane" && i.Normal.Z < -0.99)[0];
        var two = Result(Run(new SplitFaceCommand(), c => { c.Slots[0].TryAdd(new SelItem(b1.Id, SelKind.Face, bottom)); c.Slots[1].TryAdd(new SelItem(Guid.Empty, SelKind.Origin, px)); }));
        var both = S.AddObject(two, "SplitBoth", ObjKind.Solid);
        var tops = FacesWhere(both, i => i.Type == "Plane" && i.Normal.Z > 0.99); var bottoms = FacesWhere(both, i => i.Type == "Plane" && i.Normal.Z < -0.99);
        check(Faces(two) == 8 && tops.Count == 2 && bottoms.Count == 2, "the test body has its top and its bottom each split in two (8 faces)");

        var merged = Result(Run(new MergeFacesCommand(), c => { foreach (int f in tops) c.Slots[0].TryAdd(new SelItem(both.Id, SelKind.Face, f)); }));
        var mb = S.AddObject(merged, "MergedTop", ObjKind.Solid);
        check(merged.IsValid && Faces(merged) == 7 && FacesWhere(mb, i => i.Type == "Plane" && i.Normal.Z > 0.99).Count == 1 && FacesWhere(mb, i => i.Type == "Plane" && i.Normal.Z < -0.99).Count == 2
              && Math.Abs(merged.Props().Volume - v0) < 1e-3, "Merge Faces joins the two picked top halves and leaves the split bottom alone");
        var both2 = Result(Run(new MergeFacesCommand(), c => { foreach (int f in tops.Concat(bottoms)) c.Slots[0].TryAdd(new SelItem(both.Id, SelKind.Face, f)); }));
        check(both2.IsValid && Faces(both2) == 6, "picking both pairs merges both");
        var mixed = Run(new MergeFacesCommand(), c => { c.Slots[0].TryAdd(new SelItem(both.Id, SelKind.Face, tops[0])); c.Slots[0].TryAdd(new SelItem(both.Id, SelKind.Face, bottoms[0])); });
        check(mixed.TryBuild() == null && mixed.Error != null, "a top half and a bottom half are not merged");

        // Move Body tangent: a pin inside a hole, and a pin against another pin
        Shape Place(Shape sh, double x, double y, double z) { var m = sh.Props().Min; return Occ.Translate(sh, new Vec3(x - m.X, y - m.Y, z - m.Z)); }
        var plate = Occ.Box(60, 60, 10); var lo = plate.Props().Min;
        plate = Occ.Cut(plate, Place(Occ.Cylinder(5, 14), lo.X + 20 - 5, lo.Y + 25 - 5, lo.Z - 2));
        var po = S.AddObject(plate, "Plate", ObjKind.Solid);
        var pin = S.AddObject(Place(Occ.Cylinder(3, 12), 100 - 3, 80 - 3, 40), "Pin", ObjKind.Solid);
        int holeFace = FacesWhere(po, i => i.Type == "Cylinder")[0], pinFace = FacesWhere(pin, i => i.Type == "Cylinder")[0];
        void Keep(Command c, int index) => c.Params.OfType<ChoiceParam>().Single(p => p.Label == "Keep").Index = index;
        var inside = Result(Run(new MoveBodyCommand(), c => { c.Slots[0].TryAdd(new SelItem(pin.Id, SelKind.Face, pinFace)); c.Slots[1].TryAdd(new SelItem(po.Id, SelKind.Face, holeFace)); Keep(c, 2); }));
        var ic = inside.Props().Center;
        check(Math.Abs(Math.Sqrt(Math.Pow(ic.X - (lo.X + 20), 2) + Math.Pow(ic.Y - (lo.Y + 25), 2)) - 2) < 1e-6 && Math.Abs(inside.Props().Size.Z - 12) < 1e-6, "Move Body: a 6 mm pin set tangent inside a 10 mm hole sits exactly 2 mm off the hole's axis");
        var rod = S.AddObject(Place(Occ.Cylinder(4, 20), -4, -4, 0), "Rod", ObjKind.Solid);
        int rodFace = FacesWhere(rod, i => i.Type == "Cylinder")[0];
        var beside = Result(Run(new MoveBodyCommand(), c => { c.Slots[0].TryAdd(new SelItem(pin.Id, SelKind.Face, pinFace)); c.Slots[1].TryAdd(new SelItem(rod.Id, SelKind.Face, rodFace)); Keep(c, 2); }));
        var bc = beside.Props().Center;
        check(Math.Abs(Math.Sqrt(bc.X * bc.X + bc.Y * bc.Y) - 7) < 1e-6, "Move Body: a 3 mm pin set tangent to a 4 mm pin sits exactly 7 mm from its axis");
        var box2 = Occ.Box(60, 60, 10); var lo2 = box2.Props().Min; var plate2 = S.AddObject(Occ.Cut(box2, Place(Occ.Cylinder(5, 14), lo2.X + 25, lo2.Y + 25, lo2.Z - 2)), "Plate 2", ObjKind.Solid);
        var twoHoles = Run(new MoveBodyCommand(), c => { c.Slots[0].TryAdd(new SelItem(plate2.Id, SelKind.Face, FacesWhere(plate2, i => i.Type == "Cylinder")[0])); c.Slots[1].TryAdd(new SelItem(po.Id, SelKind.Face, holeFace)); Keep(c, 2); });
        check(twoHoles.TryBuild() == null && (twoHoles.Error ?? "").Contains("holes"), "Move Body: two holes cannot be tangent and the message says so");

        // import: a finished import counts the bodies the kernel rejects, so the status bar can point at Check and Repair
        string dir = Path.Combine(Path.GetTempPath(), "prism-import-hint-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string good = Path.Combine(dir, "good.step"); Occ.ExportStep([At(Occ.Box(10, 10, 10), 0, 0, 0)], good);
            var prepared = CadSession.PrepareImport(good);
            check(prepared.Parts.Count == 1 && prepared.Invalid == 0, "a sound STEP file reports no problems after import");
            var fresh = new CadSession(); fresh.AddPrepared(prepared, good);
            check(fresh.Status.Contains("Imported 1 object") && !fresh.Status.Contains("problems"), "…and the status line says only what was imported");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
