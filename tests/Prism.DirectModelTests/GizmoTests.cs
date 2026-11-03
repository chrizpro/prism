using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;

/// <summary>Direct Edit: the gizmo opens with the tab, its handles do the work of the older tools, and its edits are not history by default.</summary>
static class GizmoTests
{
    static void Num(Command c, string label, double v) => c.Params.OfType<NumberParam>().Single(p => p.Label.StartsWith(label)).Value = v;
    static int FacesWhere(SceneObject b, Func<FaceInfo, bool> pred) => Enumerable.Range(0, b.Shape.Count(SubKind.Face)).Count(f => pred(Occ.FaceInfoOf(b.Shape.Sub(SubKind.Face, f))));

    public static void Run(Action<bool, string> check)
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var (_, plateSession) = FeatureTests.BuildPlate();
        var body = s.AddObject(plateSession.Shape, "Plate", ObjKind.Solid);
        var feats = FeatureFinder.Features(body);
        var hole = feats.First(f => f.Kind == FeatureKind.Hole);
        var boss = feats.First(f => f.Kind == FeatureKind.Boss);
        double v0 = body.Shape.Props().Volume;

        vm.EnterDirectEdit();
        var gizmo = s.ActiveCommand as DirectEditCommand;
        check(gizmo != null && s.DirectEditing, "Direct Edit opens the gizmo, ready for a click");
        check(gizmo!.Handles.Count == 0, "…with no handles until something is picked");

        // a flat face: pull, three move arrows and a tilt arc
        int top = Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(f => { var i = Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, f)); return i.Type == "Plane" && i.Normal.Z > 0.99 && FeatureFinder.FeatureOf(body, f)?.Kind == FeatureKind.Face; });
        gizmo.Pick(new SelItem(body.Id, SelKind.Face, top), false);
        var h = gizmo.Handles;
        check(h.Count == 5 && h.Count(x => x.Ref != null) == 1 && h.Select(x => x.Colour).Distinct().Count() == 5, "a flat face gets a pull handle, three move arrows and a tilt arc, each its own colour");

        // pulling a boss moves every wall along its own normal
        gizmo.Pick(new SelItem(body.Id, SelKind.Face, boss.Faces[0]), false);
        check(gizmo.Handles.All(x => x.Ref == null), "a feature has no tilt handle");

        // reset: pick the round hole and pull it
        vm.ExitDirectEdit();
        check(s.ActiveCommand == null && !s.DirectEditing, "Finish Editing closes the gizmo");
        vm.EnterDirectEdit();
        gizmo = (DirectEditCommand)s.ActiveCommand!;
        gizmo.Pick(new SelItem(body.Id, SelKind.Face, hole.Faces[0]), false);
        Num(gizmo, "Pull", 1);
        var pulled = gizmo.TryBuild();
        check(pulled != null && pulled.Replace.Single().New.Props().Volume > v0, $"pulling a hole's wall out of the part makes the hole smaller, as with Offset Face (volume {pulled?.Replace.Single().New.Props().Volume:0.##} > {v0:0.##})");

        // the rim handle resizes a round hole
        Num(gizmo, "Pull", 0);
        check(gizmo.Handles.Count(x => x.Colour == 0xFF3FC6C6) == 1, "a round hole has a rim handle that sets its radius");
        Num(gizmo, "Radius change", 0.5);
        var grown = gizmo.TryBuild();
        check(grown != null && grown.Replace.Single().New.Props().Volume < v0 - 1, $"dragging the rim handle out makes the hole bigger (volume {grown?.Replace.Single().New.Props().Volume:0.##} < {v0:0.##})");
        Num(gizmo, "Pull", 1);
        check(gizmo.TryBuild() == null && (gizmo.Error ?? "").Contains("one handle"), "resize and pull at once are refused");
        Num(gizmo, "Pull", 0); Num(gizmo, "Radius change", 0);

        // moving a hole with the arrows moves the whole hole
        Num(gizmo, "Pull", 0); Num(gizmo, "Move X", 5);
        var moved = gizmo.TryBuild();
        check(moved != null && Math.Abs(moved.Replace.Single().New.Props().Volume - v0) < 1e-3, "moving a hole by its arrow keeps the volume");
        Num(gizmo, "Pull", 1);
        check(gizmo.TryBuild() == null && gizmo.Error != null && gizmo.Error.Contains("one handle"), "two handles at once are refused with a clear message");
        Num(gizmo, "Pull", 0); Num(gizmo, "Move X", 0);

        // edits are plain undo steps, not history, unless the body is tracked
        var before = s.AllNodes().Count();
        Num(gizmo, "Move X", 5);
        s.CommitCommand();
        var node = s.Current;
        check(node.Recipe == null && s.AllNodes().Count() == before + 1, "a direct edit is one undo step with no recipe");
        check(s.ActiveCommand is DirectEditCommand, "the gizmo is back after each edit");

        // the Delete key removes the picked feature, never the whole body
        body = s.Objects.Single(o => o.Kind == ObjKind.Solid);
        var holeNow = FeatureFinder.Features(body).First(f => f.Kind == FeatureKind.Hole);
        int facesBefore = body.Shape.Count(SubKind.Face);
        ((DirectEditCommand)s.ActiveCommand!).Pick(new SelItem(body.Id, SelKind.Face, holeNow.Faces[0]), false);
        check(s.Key("Delete"), "Delete is handled while the gizmo is open");
        var after = s.Objects.Single(o => o.Kind == ObjKind.Solid);
        check(after.Shape.Count(SubKind.Face) < facesBefore && after.Shape.Props().Volume > moved!.Replace.Single().New.Props().Volume - 1e-6 && s.Current.Recipe == null, "Delete heals the hole away in one undo step and the body stays");

        // tracking turned on: the same kind of edit keeps a recipe
        s.Update(after.Id, o => o.WithTracking(true), label: "Track history");
        var tracked = s.Objects.Single(o => o.Kind == ObjKind.Solid);
        check(tracked.TrackHistory, "a body can be set to track history");
        var cmd = (DirectEditCommand)s.ActiveCommand!;
        var bossNow = FeatureFinder.Features(tracked).First(f => f.Kind == FeatureKind.Boss);
        cmd.Pick(new SelItem(tracked.Id, SelKind.Face, bossNow.Faces[0]), false);
        Num(cmd, "Pull", 0); Num(cmd, "Move Y", 3);
        s.CommitCommand();
        check(s.Objects.Single(o => o.Kind == ObjKind.Solid).TrackHistory && s.Current.Recipe != null, "…and then the edit is recorded as an editable history step, and tracking survives it");
        vm.ExitDirectEdit();
    }
}
