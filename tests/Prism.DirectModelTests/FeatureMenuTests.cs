using Prism.Core;
using Prism.ViewModels;

/// <summary>The context menu with faces selected offers actions for the feature they belong to.</summary>
static class FeatureMenuTests
{
    public static void Run(Action<bool, string> check)
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var groups = vm.Tabs.Single(t => t.Title == "Direct Modeling").Groups.Select(g => g.Title).ToList();
        int At(string g) => groups.IndexOf(g);
        check(At("Select") < At("Datum") && At("Datum") < At("Add") && At("Add") < At("Edit faces") && At("Edit faces") < At("Edit features") && At("Edit features") < At("Repair") && At("Repair") < At("Transform") && At("Transform") < At("Inspect") && At("Inspect") < At("Appearance"), "the Direct Modeling tab reads Select, Datum, Add, Edit faces, Edit features, Repair, Transform, Inspect, Appearance");
        var (_, plate) = FeatureTests.BuildPlate();
        var body = s.AddObject(plate.Shape, "Plate", ObjKind.Solid);
        var slot = FeatureFinder.Features(body).First(f => f.Kind == FeatureKind.Slot);
        var hole = FeatureFinder.Features(body).First(f => f.Kind == FeatureKind.Hole);
        List<string> Labels() => vm.BuildTraditionalMenu().Select(m => m.Label).ToList();

        s.SelectItems([new SelItem(body.Id, SelKind.Face, slot.Faces[0])]);
        var l = Labels();
        check(l.Contains("Select the whole slot") && l.Contains("Move slot…") && l.Contains("Copy slot…") && l.Contains("Resize slot…") && l.Contains("Delete slot") && !l.Contains("Change hole type…"), "right-clicking a slot face offers slot actions only");
        vm.BuildTraditionalMenu().First(m => m.Label == "Select the whole slot").Run!();
        check(s.Selection.Count == 4, "“Select the whole slot” selects all four faces");
        check(!Labels().Contains("Select the whole slot"), "…and the offer goes away once the whole slot is selected");

        s.SelectItems([new SelItem(body.Id, SelKind.Face, hole.Faces[0])]);
        l = Labels();
        check(l.Contains("Change hole type…") && l.Contains("Move hole…"), "right-clicking a hole offers Change hole type");
        vm.BuildTraditionalMenu().First(m => m.Label == "Resize hole…").Run!();
        check(s.ActiveCommand is Prism.Commands.FeatureEditCommand, "a menu action opens Edit Feature with the feature picked");
        s.CancelCommand();

        var side = Enumerable.Range(0, body.Shape.Count(Prism.Kernel.SubKind.Face)).First(f => FeatureFinder.FeatureOf(body, f)!.Kind == FeatureKind.Face);
        s.SelectItems([new SelItem(body.Id, SelKind.Face, side)]);
        l = Labels();
        check(l.Contains("Push / Pull…") && l.Contains("Delete face") && !l.Any(x => x.StartsWith("Move slot")), "a plain face offers face actions");
    }
}
