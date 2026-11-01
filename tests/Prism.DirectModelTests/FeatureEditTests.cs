using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

/// <summary>Edit Feature: move, copy, resize and delete each kind of feature on the test plate, checked against the volumes the geometry dictates.</summary>
static class FeatureEditTests
{
    public static void Run(Action<bool, string> check)
    {
        var (s, body) = FeatureTests.BuildPlate();
        double v0 = body.Shape.Props().Volume;
        var feats = FeatureFinder.Features(body);
        Feat Of(FeatureKind k, Func<Feat, bool>? pick = null) => feats.First(f => f.Kind == k && (pick == null || pick(f)));
        var hole = Of(FeatureKind.Hole); var slot = Of(FeatureKind.Slot); var pocket = Of(FeatureKind.Pocket);
        var boxBoss = Of(FeatureKind.Boss, f => f.Faces.Count == 5); var pin = Of(FeatureKind.Boss, f => f.Round != null);
        double holeV = Math.PI * 9 * 10, slotV = (20 * 6 + Math.PI * 9) * 10, pocketV = 16 * 10 * 4.999, boxBossV = 12 * 12 * 8, pinV = Math.PI * 16 * 6;
        int faces0 = body.Shape.Count(SubKind.Face);

        Shape Edit(Feat f, string action, Action<FeatureEditCommand>? set = null)
        {
            var c = new FeatureEditCommand(); c.Begin(s);
            c.ConfigureInputs(() =>
            {
                c.Slots[0].Items.Clear(); c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, f.Faces[0]));
                c.Params.OfType<ChoiceParam>().Single(p => p.Label == "Action").Index = Array.IndexOf(["Move", "Resize", "Copy", "Delete"], action);
                set?.Invoke(c);
            });
            return c.TryBuild()?.Replace.Single().New ?? throw new Exception(c.Error ?? "Edit Feature built nothing");
        }
        void Num(FeatureEditCommand c, string label, double v) => c.Params.OfType<NumberParam>().Single(p => p.Label == label).Value = v;
        double Vol(Shape sh) => sh.Props().Volume;

        foreach (var (name, f, vol) in new[] { ("round hole", hole, holeV), ("slot", slot, slotV), ("pocket", pocket, pocketV) })
        {
            var del = Edit(f, "Delete");
            check(del.IsValid && Math.Abs(Vol(del) - (v0 + vol)) < 2, $"Delete removes the {name} and closes the gap");
            var mv = Edit(f, "Move", c => Num(c, "Move Y", 12));
            check(mv.IsValid && Math.Abs(Vol(mv) - v0) < 2 && mv.Count(SubKind.Face) == faces0, $"Move slides the {name} without changing the part's volume or face count");
            var cp = Edit(f, "Copy", c => { Num(c, "Move Y", 25); Num(c, "Number of copies", 2); });
            check(cp.IsValid && Math.Abs(Vol(cp) - (v0 - 2 * vol)) < 2, $"Copy makes two more of the {name}");
        }
        foreach (var (name, f, vol) in new[] { ("rectangular boss", boxBoss, boxBossV), ("round boss", pin, pinV) })
        {
            var del = Edit(f, "Delete");
            check(del.IsValid && Math.Abs(Vol(del) - (v0 - vol)) < 2, $"Delete removes the {name}");
            var mv = Edit(f, "Move", c => Num(c, "Move X", 20));
            check(mv.IsValid && Math.Abs(Vol(mv) - v0) < 2, $"Move slides the {name} without changing the volume");
            var cp = Edit(f, "Copy", c => { Num(c, "Move X", 25); Num(c, "Number of copies", 1); });
            check(cp.IsValid && Math.Abs(Vol(cp) - (v0 + vol)) < 2, $"Copy adds another {name}");
        }
        // resize
        var big = Edit(hole, "Resize", c => Num(c, "New diameter (0 = keep)", 10));
        check(big.IsValid && Math.Abs(Vol(big) - (v0 - (Math.PI * 25 - Math.PI * 9) * 10)) < 2, "Resize changes a round hole's diameter");
        var wide = Edit(slot, "Resize", c => { Num(c, "New slot width (0 = keep)", 8); Num(c, "New slot length (0 = keep)", 30); });
        double newSlotV = ((30 - 8) * 8 + Math.PI * 16) * 10;
        check(wide.IsValid && Math.Abs(Vol(wide) - (v0 + slotV - newSlotV)) < 3, "Resize changes a slot's width and length about its centre");
        var longer = Edit(slot, "Resize", c => Num(c, "New slot length (0 = keep)", 36));
        check(longer.IsValid && Math.Abs(Vol(longer) - (v0 + slotV - ((36 - 6) * 6 + Math.PI * 9) * 10)) < 3, "Resize can change only the slot length");
        bool refused = false; try { Edit(pocket, "Resize", c => Num(c, "New diameter (0 = keep)", 5)); } catch (Exception e) { refused = e.Message.Contains("pocket"); }
        check(refused, "Resize on a pocket says what to use instead");
        // a fillet can be deleted but not moved
        var block = s.AddObject(Occ.Fillet(Occ.Box(40, 30, 20), [FilletEdge(Occ.Box(40, 30, 20))], 4), "Block", ObjKind.Solid);
        var fillet = FeatureFinder.Features(block).First(f => f.Kind == FeatureKind.Fillet);
        var c2 = new FeatureEditCommand(); c2.Begin(s);
        c2.ConfigureInputs(() => { c2.Slots[0].Items.Clear(); c2.Slots[0].TryAdd(new SelItem(block.Id, SelKind.Face, fillet.Faces[0])); c2.Params.OfType<ChoiceParam>().Single(p => p.Label == "Action").Index = 3; });
        var healed = c2.TryBuild()?.Replace.Single().New;
        check(healed != null && healed.IsValid && healed.Count(SubKind.Face) == 6 && Math.Abs(Vol(healed) - 40 * 30 * 20) < 2, "Delete on a fillet restores the sharp corner");
        // the Delete key with a face selected removes its feature, not the whole body, and is one undo step
        var (ks, kb) = FeatureTests.BuildPlate();
        var kslot = FeatureFinder.Features(kb).First(f => f.Kind == FeatureKind.Slot);
        ks.SelectItems([new SelItem(kb.Id, SelKind.Face, kslot.Faces[2])]);
        double kv = kb.Shape.Props().Volume; int objs = ks.Objects.Count;
        ks.Key("Delete");
        var after = ks.Objects.Single();
        check(ks.Objects.Count == objs && after.Shape.IsValid && Math.Abs(after.Shape.Props().Volume - (kv + slotV)) < 2 && ks.ActiveCommand == null, "pressing Delete with a slot face selected removes the slot, keeps the body, and leaves no tool open");
        ks.Undo();
        check(Math.Abs(ks.Objects.Single().Shape.Props().Volume - kv) < 1, "Undo brings the slot back");
        ks.SelectItems([SelItem.Of(ks.Objects.Single())]); ks.Key("Delete");
        check(ks.Objects.Count == 0, "Delete with the whole body selected still deletes the body");

        // the panel only offers what applies
        var panel = new FeatureEditCommand(); panel.Begin(s);
        panel.ConfigureInputs(() => { panel.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, slot.Faces[0])); panel.Params.OfType<ChoiceParam>().Single(p => p.Label == "Action").Index = 1; });
        panel.OnChanged();
        bool Vis(string l) => panel.Params.Single(p => p.Label == l).IsVisible;
        check(Vis("New slot width (0 = keep)") && !Vis("Move X") && !Vis("New diameter (0 = keep)"), "with a slot picked and Resize chosen, the panel shows slot width and length only");
    }

    static int FilletEdge(Shape block)
    {
        var min = block.Props().Min;
        for (int e = 0; e < block.Count(SubKind.Edge); e++)
        {
            var ei = Occ.EdgeInfoOf(block.Sub(SubKind.Edge, e));
            if (ei.Type == "Line" && Math.Abs((ei.End - ei.Start).Z) > 15 && ei.Start.X > min.X + 1 && ei.Start.Y > min.Y + 1) return e;
        }
        throw new Exception("no vertical edge");
    }
}
