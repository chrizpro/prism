using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;

/// <summary>Rectangle and lasso selection, picking through the part, and invert.</summary>
static class SelectionTests
{
    public static void Run(Action<bool, string> check)
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var cam = s.Camera; cam.Width = 800; cam.Height = 600;
        var block = s.AddObject(Occ.Box(40, 30, 20), "Block", ObjKind.Solid);
        s.ZoomExtents();
        int faces = block.Shape.Count(SubKind.Face);
        var everything = RegionSelect.Region.Box(0, 0, 800, 600);

        var seen = RegionSelect.Collect(s, everything, SelMode.Face, false);
        var through = RegionSelect.Collect(s, everything, SelMode.Face, true);
        check(faces == 6 && through.Count == 6, "through the part, a rectangle around everything takes all 6 faces of a block");
        check(seen.Count is >= 1 and < 6, $"without Through it takes only the faces you can see ({seen.Count} of 6)");
        check(seen.All(i => i.Kind == SelKind.Face), "…and they are faces");

        var bodies = RegionSelect.Collect(s, everything, SelMode.Object, false);
        check(bodies.Count == 1 && bodies[0].Kind == SelKind.Object, "in Object mode a rectangle around the block picks the block");
        var tiny = RegionSelect.Region.Box(0, 0, 5, 5);
        check(RegionSelect.Collect(s, tiny, SelMode.Object, true).Count == 0, "a rectangle in an empty corner picks nothing");

        var edges = RegionSelect.Collect(s, everything, SelMode.Edge, true);
        check(edges.Count == 12, $"through the part, a rectangle takes all 12 edges of a block (got {edges.Count})");
        check(RegionSelect.Collect(s, everything, SelMode.Edge, false).Count < 12, "…and fewer when the back edges are hidden");

        var centre = cam.Project(new Vec3(20, 15, 20))!.Value;
        var small = RegionSelect.Region.Box(centre.x - 3, centre.y - 3, centre.x + 3, centre.y + 3);
        var smallCrossing = RegionSelect.Region.Box(centre.x + 3, centre.y + 3, centre.x - 3, centre.y - 3);
        check(RegionSelect.Collect(s, small, SelMode.Face, true).Count == 0, "a small rectangle left to right does not take faces that are only partly inside");
        check(RegionSelect.Collect(s, smallCrossing, SelMode.Face, true).Count >= 1, "…but right to left it takes the faces it touches");

        var lasso = new RegionSelect.Region(); lasso.Points.AddRange([(5, 5), (795, 5), (400, 595)]);
        check(RegionSelect.Collect(s, lasso, SelMode.Object, true).Count == 1, "a lasso that touches the block picks it");

        var plain = new PointerInfo(0, 0, true, false, false, false);
        s.Mode = SelMode.Face; s.XRay = true;
        s.ApplyRegion(everything, plain);
        check(s.Selection.Count == 6, "applying a region selects what it found");
        s.ApplyRegion(everything, new PointerInfo(0, 0, true, false, true, false));
        check(s.Selection.Count == 0, "Ctrl takes the region's items away again");

        s.ClearSelection(); s.XRay = false;
        s.SetSelection(new SelItem(block.Id, SelKind.Face, 0), false);
        s.InvertSelection();
        check(s.Selection.Count == 5 && !s.Selection.Contains(new SelItem(block.Id, SelKind.Face, 0)), "Invert on one picked face selects the other five of that body");
        s.InvertSelection();
        check(s.Selection.Count == 1 && s.Selection[0].Index == 0, "…and inverting again goes back");
        s.ClearSelection(); s.InvertSelection();
        check(s.Selection.Count == 6, "with nothing selected, Invert selects every face");
        s.Mode = SelMode.Object; s.ClearSelection(); s.InvertSelection();
        check(s.Selection.Count == 1 && s.Selection[0].Kind == SelKind.Object, "in Object mode Invert selects the bodies that were not selected");

        foreach (var tab in new[] { "Solid", "Surface", "Freeform", "Direct Modeling" })
        {
            var select = vm.Tabs.Single(t => t.Title == tab).Groups.Single(g => g.Title == "Select");
            var buttons = select.Items.OfType<Prism.Ribbon.RibbonButton>().ToList();
            check(buttons.Any(b => b.Id == "split:selection-method" && b.Menu.Count == 2) && buttons.Any(b => b.Label == "Invert") && buttons.Any(b => b.Label == "Through"), $"the {tab} tab's Select section has the Rectangle/Lasso dropdown, Invert and Through");
        }

        s.Method = SelectMethod.Click;
        var methodBtn = vm.Tabs.Single(t => t.Title == "Solid").Groups.Single(g => g.Title == "Select").Items.OfType<Prism.Ribbon.RibbonButton>().Single(b => b.Id == "split:selection-method");
        methodBtn.Menu[0].Execute();
        check(s.Method == SelectMethod.Rectangle, "choosing Rectangle in the dropdown switches the method");
        methodBtn.Menu[0].Execute();
        check(s.Method == SelectMethod.Click, "choosing it again goes back to clicking");

        s.Method = SelectMethod.Rectangle; s.Mode = SelMode.Face; s.XRay = true; s.ClearSelection();
        s.PointerDown(new PointerInfo(10, 10, true, false, false, false));
        s.PointerMove(new PointerInfo(790, 590, true, false, false, false));
        s.PointerUp(new PointerInfo(790, 590, true, false, false, false));
        check(s.Selection.Count == 6, "dragging a rectangle in the view selects through the part");
        s.Method = SelectMethod.Click; s.XRay = false; s.ClearSelection();

        // curves and Freeform bodies are picked too
        s.Mode = SelMode.Object;
        var wire = s.AddObject(Occ.Polyline([new(80, 0, 0), new(100, 20, 0), new(120, 0, 0)], false), "Wire", ObjKind.Curve);
        var box = new Prism.Commands.FormBoxCommand(); box.Begin(s);
        s.Apply(box.TryBuild() ?? throw new Exception(box.Error));
        var cage = s.Objects.Single(o => o.Kind == ObjKind.Form);
        s.Update(cage.Id, o => o, checkpoint: false);
        s.ZoomExtents();
        var picked = RegionSelect.Collect(s, everything, SelMode.Object, true);
        check(picked.Any(i => i.Obj == wire.Id), "a rectangle picks curves");
        check(picked.Any(i => i.Obj == cage.Id), "a rectangle picks Freeform bodies");
        check(RegionSelect.Collect(s, everything, SelMode.Face, true).All(i => i.Obj == block.Id), "in Face mode it leaves curves and Freeform bodies alone");

        // Invert and the lasso inside Edit Freeform
        s.SelectObjects([cage.Id]);
        s.StartCommand(() => new Prism.Commands.EditFormCommand());
        var editing = (Prism.Commands.EditFormCommand)s.ActiveCommand!;
        s.InvertSelection();
        check(editing.Faces.Count > 0, "Invert selects the cage faces inside Edit Freeform");
        s.CancelCommand();

        // Rectangle and Invert inside Edit Points
        var grid = new Vec3[16]; for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) grid[r * 4 + c] = new Vec3(c * 10, r * 10, 0);
        foreach (var o in s.Objects.ToList()) s.Update(o.Id, x => x.WithVisible(false), checkpoint: false);
        var sheet = s.AddObject(Occ.NurbsSurface(grid, 4, 4, 3, 3), "Sheet", ObjKind.Surface);
        s.ZoomExtents();
        var points = new Prism.Commands.EditPointsCommand(); s.StartCommand(points);
        points.Pick(SelItem.Of(sheet), false);
        var tool = (Prism.Commands.EditPointsTool)points.Tool!;
        int all = points.Model!.Points.Length;
        tool.Selected.Add(0);
        s.InvertSelection();
        check(tool.Selected.Count == all - 1 && !tool.Selected.Contains(0), "Invert works on control points in Edit Points");
        tool.Selected.Clear();
        s.Method = SelectMethod.Rectangle;
        s.PointerDown(new PointerInfo(1, 1, true, false, false, false));
        s.PointerMove(new PointerInfo(799, 599, true, false, false, false));
        s.PointerUp(new PointerInfo(799, 599, true, false, false, false));
        check(tool.Selected.Count == all, "a rectangle selects control points in Edit Points");
        s.CancelCommand(); s.Method = SelectMethod.Click;
    }
}
