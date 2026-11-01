using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

/// <summary>Edit Feature: converting a hole to a modeled thread, and resizing fillet chains and rim fillets.</summary>
static class EditGapTests
{
    static double Vol(Shape s) => s.Props().Volume;

    static Shape Edit(CadSession s, SceneObject body, Feat f, int action, Action<FeatureEditCommand> set)
    {
        var c = new FeatureEditCommand(); c.Begin(s);
        c.ConfigureInputs(() =>
        {
            c.Slots[0].Items.Clear(); c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, f.Faces[0]));
            c.Params.OfType<ChoiceParam>().Single(p => p.Label == "Action").Index = action;
            set(c);
        });
        return c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "Edit Feature built nothing");
    }
    static void Num(FeatureEditCommand c, string label, double v) => c.Params.OfType<NumberParam>().Single(p => p.Label == label).Value = v;

    public static void Run(Action<bool, string> check)
    {
        var s = new CadSession();
        // ---- threaded: a plain 5 mm hole becomes M6 x 1, and matches what the Hole tool makes in a plain plate
        var box = Occ.Box(60, 60, 10); var lo = box.Props().Min;
        Shape Place(Shape sh, double x, double y, double z) { var m = sh.Props().Min; return Occ.Translate(sh, new Vec3(x - m.X, y - m.Y, z - m.Z)); }
        var drilled = s.AddObject(Occ.Cut(box, Place(Occ.Cylinder(2.5, 14), lo.X + 30 - 2.5, lo.Y + 30 - 2.5, lo.Z - 2)), "Drilled", ObjKind.Solid);
        var hole = FeatureFinder.Features(drilled).First(f => f.Kind == FeatureKind.Hole);
        var threaded = Edit(s, drilled, hole, 4, c =>
        {
            c.Params.OfType<ChoiceParam>().Single(p => p.Label == "New hole type").Index = 3;
            Num(c, "Thread diameter", 6); Num(c, "Thread pitch", 1);
        });
        var plain = s.AddObject(Occ.Box(60, 60, 10), "Plain plate", ObjKind.Solid);
        var hc = new HoleCommand { Factory = () => new HoleCommand() }; hc.Begin(s);
        hc.ConfigureInputs(() =>
        {
            hc.Slots[0].Items.Clear(); hc.Slots[0].TryAdd(SelItem.Of(plain));
            hc.Params.OfType<ChoiceParam>().Single(p => p.Label == "Hole type").Index = 1;
            hc.Params.OfType<NumberParam>().Single(p => p.Label.Contains("diameter", StringComparison.OrdinalIgnoreCase)).Value = 6;
            hc.Params.OfType<NumberParam>().Single(p => p.Label == "Thread pitch").Value = 1;
        });
        var reference = hc.TryBuild()?.Replace.Single().New ?? throw new Exception(hc.Error ?? "Hole built nothing");
        check(threaded.IsValid && Math.Abs(Vol(threaded) - Vol(reference)) < 1.0, $"Change type to Threaded gives the same part as the Hole tool's M6 x 1 hole ({Vol(threaded):0.##} vs {Vol(reference):0.##} mm³)");
        bool refusedPitch = false; try { Edit(s, drilled, hole, 4, c => { c.Params.OfType<ChoiceParam>().Single(p => p.Label == "New hole type").Index = 3; Num(c, "Thread diameter", 3); Num(c, "Thread pitch", 3); }); } catch (Exception e) { refusedPitch = e.Message.Contains("pitch"); }
        check(refusedPitch, "a pitch too coarse for the diameter is refused with a clear message");

        // ---- the four top edges of a block rounded together, resized from 3 mm to 1.5 mm
        var block = Occ.Box(40, 30, 20); double zTop = block.Props().Max.Z;
        int[] TopEdges(Shape sh, string type) => Enumerable.Range(0, sh.Count(SubKind.Edge)).Where(e => { var ei = Occ.EdgeInfoOf(sh.Sub(SubKind.Edge, e)); return ei.Type == type && Math.Abs(ei.Start.Z - zTop) < 1e-6 && Math.Abs(ei.End.Z - zTop) < 1e-6; }).ToArray();
        var topEdges = TopEdges(block, "Line");
        var rounded = s.AddObject(Occ.Fillet(block, topEdges, 3), "Rounded", ObjKind.Solid);
        var chain = FeatureFinder.Features(rounded).Where(f => f.Kind == FeatureKind.Fillet).ToList();
        check(chain.Count == 4, "the four top fillets are four fillet features");
        // pick one face of each and resize them together: neighbours that meet are rounded in one go
        var four = new FeatureEditCommand(); four.Begin(s);
        four.ConfigureInputs(() =>
        {
            four.Slots[0].Items.Clear(); foreach (var ft in chain) four.Slots[0].TryAdd(new SelItem(rounded.Id, SelKind.Face, ft.Faces[0]));
            four.Params.OfType<ChoiceParam>().Single(p => p.Label == "Action").Index = 1; Num(four, "New fillet radius (0 = keep)", 1.5);
        });
        var smaller = four.TryBuild()?.Replace.Single().New ?? throw new Exception(four.Error ?? "nothing");
        double expected = Vol(Occ.Fillet(block, topEdges, 1.5));
        check(smaller.IsValid && Math.Abs(Vol(smaller) - expected) < 0.5, $"Resize changes all four top fillets together to 1.5 mm ({Vol(smaller):0.##} vs {expected:0.##} mm³)");

        // ---- a fillet around a cylinder's top rim
        var cyl = Occ.Cylinder(10, 20); zTop = cyl.Props().Max.Z;
        var rim = TopEdges(cyl, "Circle");
        check(rim.Length == 1, "the cylinder has one top rim edge");
        var rimBody = s.AddObject(Occ.Fillet(cyl, rim, 3), "Rim", ObjKind.Solid);
        var rimFillet = FeatureFinder.Features(rimBody).First(f => f.Kind == FeatureKind.Fillet);
        var rim2 = Edit(s, rimBody, rimFillet, 1, c => Num(c, "New fillet radius (0 = keep)", 2));
        double rimExpected = Vol(Occ.Fillet(cyl, rim, 2));
        check(rim2.IsValid && Math.Abs(Vol(rim2) - rimExpected) < 0.5, $"Resize changes a fillet around a round rim from 3 mm to 2 mm ({Vol(rim2):0.##} vs {rimExpected:0.##} mm³)");
    }
}
