using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Ribbon;
using Prism.ViewModels;

/// <summary>Edit Freeform: one tool per job, and a panel that only keeps what the ribbon has no room for.</summary>
static class FreeformToolTests
{
    public static void Run(Action<bool, string> check)
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = new FormBoxCommand(); box.Begin(s);
        s.Apply(box.TryBuild() ?? throw new Exception(box.Error));
        var cage = s.Objects.Single(o => o.Kind == ObjKind.Form);
        s.SelectObjects([cage.Id]);
        s.StartCommand(() => new EditFormCommand());
        var form = (EditFormCommand)s.ActiveCommand!;
        void Do(string label) => form.Params.OfType<ActionParam>().First(a => a.Label.StartsWith(label)).Run!();
        bool Shown(string label) => form.Params.Any(p => p.IsVisible && p.Label.StartsWith(label));
        form.OnChanged();

        check(form.Params.OfType<ActionParam>().Count(a => a.IsVisible) <= 8, "the Edit Freeform panel shows only a few actions of its own");
        check(Shown("Grow selection") && Shown("Select matching") && !Shown("Extrude selection") && !Shown("Delete selection") && !Shown("Bridge"), "tools on the ribbon are not repeated in the panel");
        check(!Shown("Place symmetry") && !Shown("Loops to insert"), "settings that do not apply yet stay hidden");
        form.Mode.Index = (int)FormMode.Edge; form.OnChanged();
        check(Shown("Bevel type") && Shown("Slide amount") && !Shown("Inset amount"), "edge mode shows the edge settings");
        form.Mode.Index = (int)FormMode.Face; form.OnChanged();
        check(!Shown("Bevel type") && Shown("Inset amount"), "face mode shows the face settings");

        var work = form.Work!;
        int a = 0, b = Enumerable.Range(1, work.F.Count - 1).First(f => work.F[f].Intersect(work.F[0]).Count() == 2);
        int faces = work.F.Count;
        form.Faces.Add(a); form.Faces.Add(b);
        Do("Merge selection");
        check(form.Work!.F.Count == faces - 1, "Merge joins selected faces into one");
        form.TryUndo();

        form.Mode.Index = (int)FormMode.Edge; form.OnChanged();
        var (ea, eb) = form.Work!.Edges().First();
        form.Edges.Add(SubdMesh.Key(ea, eb)); faces = form.Work.F.Count;
        Do("Merge selection");
        check(form.Work.F.Count == faces - 1, "Merge on an inner edge joins the faces either side");
        form.TryUndo();

        form.Mode.Index = (int)FormMode.Vertex; form.OnChanged();
        int points = form.Work!.V.Count;
        var (va, vb) = form.Work.Edges().First();
        form.Verts.Add(va); form.Verts.Add(vb);
        Do("Weld selection");
        check(form.Work.V.Count == points - 1, "Weld pulls selected points into one");
        form.TryUndo();

        form.Mode.Index = (int)FormMode.Edge; form.OnChanged();
        var (ca, cb) = form.Work!.Edges().First();
        form.Edges.Add(SubdMesh.Key(ca, cb)); points = form.Work.V.Count;
        Do("Weld selection");
        check(form.Work.V.Count == points - 1, "Weld on an edge collapses it to a point");
        form.TryUndo();

        var (fa, fb) = form.Work!.Edges().First();
        form.Edges.Add(SubdMesh.Key(fa, fb));
        Do("Freeze or unfreeze");
        check(form.Work.IsFrozen(fa) && form.Work.IsFrozen(fb), "Freeze locks the selection");
        Do("Freeze or unfreeze");
        check(!form.Work.IsFrozen(fa), "pressed on a frozen selection, it unlocks it");
        Do("Freeze or unfreeze"); form.ClearSelection();
        Do("Freeze or unfreeze");
        check(form.Work.Frozen.Count == 0, "with nothing selected, it unlocks everything");

        faces = form.Work.F.Count;
        var (ga, gb) = form.Work.Edges().First();
        foreach (var k in FormOps.EdgeLoop(form.Work, ga, gb)) form.Edges.Add(k);
        Do("Bevel selection");
        check(form.Work.F.Count > faces, "Bevel makes a flat bevel by default: " + s.Status);
        form.TryUndo();
        form.Params.OfType<ChoiceParam>().First(p => p.Label == "Bevel type").Index = 1;
        var (ha, hb) = form.Work!.Edges().First();
        foreach (var k in FormOps.EdgeLoop(form.Work, ha, hb)) form.Edges.Add(k); faces = form.Work.F.Count;
        Do("Bevel selection");
        check(form.Work.F.Count > faces, "and Tight adds support loops with the same tool: " + s.Status);
        s.CancelCommand();

        var freeform = vm.Tabs.Single(t => t.Title == "Freeform");
        var labels = freeform.Buttons.Select(b => b.Label.Replace("\n", " ")).ToList();
        check(labels.Contains("Merge") && labels.Contains("Bevel") && labels.Contains("Weld") && labels.Contains("Freeze"), "the Freeform ribbon has the merged tools");
        check(new[] { "Merge faces", "Merge edges", "Collapse", "Chamfer", "Unfreeze" }.All(n => !labels.Contains(n)), "and not the tools they replace");

        foreach (var tab in vm.Tabs.Where(t => new[] { "Transform", "Inspect", "Appearance" }.All(n => t.Groups.Any(g => g.Title == n))))
        {
            var titles = tab.Groups.Select(g => g.Title).Where(g => g is not ("Close" or "Exit")).ToList();
            check(titles.TakeLast(3).SequenceEqual(new[] { "Transform", "Inspect", "Appearance" }), $"{tab.Title} ends with Transform, Inspect, Appearance");
        }
        var left = new[] { "Select", "Datum", "References", "Curves" };
        foreach (var name in new[] { "Solid", "Surface", "NURBS", "Freeform", "Home" })
            check(vm.Tabs.Single(t => t.Title == name).Groups.Select(g => g.Title).Take(4).SequenceEqual(left), $"{name} starts with Select, Datum, References, Curves");
        var nurbs = vm.Tabs.Single(t => t.Title == "NURBS").Groups.Single(g => g.Title == "References").Items.OfType<RibbonButton>().First();
        check(nurbs.Label.Replace("\n", " ") == "Create Sketch" && nurbs.Shortcut == "K", "NURBS calls its sketch Create Sketch, with K as its key");
    }
}
