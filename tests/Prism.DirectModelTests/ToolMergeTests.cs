using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;

/// <summary>Tools that were folded together on the Solid, Surface and NURBS ribbons still do everything the old ones did.</summary>
static class ToolMergeTests
{
    static CadSession S = null!;
    static T Run<T>(T c, Action<T> set) where T : Command { c.Begin(S); Cfg(c, () => set(c)); return c; }
    static void Cfg(Command c, Action set) { c.ConfigureInputs(set); c.OnChanged(); }
    static SceneObject Curve(Shape s) => S.AddObject(s, "Curve", ObjKind.Curve);
    static SelItem It(SceneObject o) => SelItem.Of(o);
    static Shape Line(params Vec3[] p) => Occ.Polyline(p, false);
    static void Set(Command c, string label, double v) => c.Params.OfType<NumberParam>().First(p => p.Label.StartsWith(label)).Value = v;
    static void Pick(Command c, string label, int i) => c.Params.OfType<ChoiceParam>().First(p => p.Label.StartsWith(label)).Index = i;
    static bool Visible(Command c, string label) => c.Params.First(p => p.Label.StartsWith(label)).IsVisible;
    static Shape Added(Command c) { var o = c.TryBuild() ?? throw new Exception(c.Error ?? "No result"); return o.Add.Single().Shape!; }
    static SceneObject Strip(double x0, double x1, double z) =>
        S.AddObject(Occ.NurbsSurface([new(x0, 0, z), new(x1, 0, z), new(x0, 30, z), new(x1, 30, z)], 2, 2, 1, 1), "Strip", ObjKind.Surface);
    static int EdgeAtX(SceneObject o, double x) => Enumerable.Range(0, o.Shape.Count(SubKind.Edge)).First(e => { var i = Occ.EdgeInfoOf(o.Shape.Sub(SubKind.Edge, e)); return Math.Abs(i.Start.X - x) < 1e-6 && Math.Abs(i.End.X - x) < 1e-6; });

    public static void Run(Action<bool, string> check)
    {
        S = new CadSession();

        // Sweep with a second rail is the old Sweep 2 Rails
        var profile = Curve(Line(new(0, 0, 0), new(0, 10, 0)));
        var rail1 = Curve(Line(new(0, 0, 0), new(50, 0, 0)));
        var rail2 = Curve(Line(new(0, 10, 0), new(50, 20, 0)));
        var sweep = Run(new SweepCommand(true), c => { c.Slots[0].TryAdd(It(profile)); c.Slots[1].TryAdd(It(rail1)); });
        check(Visible(sweep, "Scale at the end"), "Sweep shows its orientation settings with one path");
        var plain = Added(sweep);
        check(Math.Abs(plain.Props().Area - 500) < 1e-3, "Sweep along one path still works");
        Cfg(sweep, () => sweep.Slots[2].TryAdd(It(rail2)));
        check(!Visible(sweep, "Scale at the end"), "…and hides them once a second rail is picked");
        var railed = Added(sweep);
        var old = Occ.SweepRails(profile.Shape, rail1.Shape, rail2.Shape, false);
        check(Math.Abs(railed.Props().Area - old.Props().Area) < 1e-6 && railed.Props().Size.DistanceTo(old.Props().Size) < 1e-6, $"a second rail gives the same surface as Sweep 2 Rails (area {railed.Props().Area:0.##})");

        // Boundary Surface takes over Planar Surface
        var outer = Curve(Occ.Polyline([new(0, 0, 0), new(40, 0, 0), new(40, 30, 0), new(0, 30, 0)], true));
        var inner = Curve(Occ.Circle(new(20, 15, 0), Vec3.UnitZ, 5));
        var flat = Added(Run(new BoundarySurfaceCommand(), c => { c.Slots[0].TryAdd(It(outer)); c.Slots[0].TryAdd(It(inner)); }));
        check(Math.Abs(flat.Props().Area - (1200 - Math.PI * 25)) < 1e-2, "Boundary on a flat loop makes a flat surface, and a loop inside it becomes a hole");

        // ...and Patch on a bent loop
        var bent = Curve(Occ.Spline([new(0, 0, 0), new(20, 0, 8), new(40, 0, 0), new(40, 30, 0), new(20, 30, 8), new(0, 30, 0)], true));
        var smooth = Added(Run(new BoundarySurfaceCommand(), c => c.Slots[0].TryAdd(It(bent))));
        check(smooth.HasFaces && smooth.Props().Size.Z > 1, "Boundary on a bent loop makes a smooth surface");
        var refused = Run(new BoundarySurfaceCommand(), c => { c.Slots[0].TryAdd(It(bent)); Pick(c, "Fill with", 1); });
        check(refused.TryBuild() == null && (refused.Error ?? "").Contains("flat loop"), "asking for a flat surface on a bent loop is refused with a clear message");

        // ...and Ruled, for two curves
        var a = Curve(Line(new(0, 0, 0), new(30, 0, 0))); var b = Curve(Line(new(0, 20, 10), new(30, 20, 10)));
        var ruled = Occ.Ruled(a.Shape, b.Shape);
        var two = Added(Run(new BoundarySurfaceCommand(), c => { c.Slots[0].TryAdd(It(a)); c.Slots[0].TryAdd(It(b)); }));
        check(Math.Abs(two.Props().Area - ruled.Props().Area) < 1e-6, "two curves in Boundary give the same surface as Ruled");

        // ...and Network, with curves across the inside
        var ring = Curve(Occ.Polyline([new(0, 0, 0), new(40, 0, 0), new(40, 40, 0), new(0, 40, 0)], true));
        var across = Curve(Occ.Spline([new(0, 20, 0), new(20, 20, 6), new(40, 20, 0)], false));
        var network = Run(new BoundarySurfaceCommand(), c => { c.Slots[0].TryAdd(It(ring)); c.Slots[1].TryAdd(It(across)); });
        check(!Visible(network, "Surface degree") && Visible(network, "Inside curves"), "with guide curves Boundary offers Inside curves; the fit settings stay hidden until asked for");
        var exact = Added(network);
        Cfg(network, () => Pick(network, "Inside curves", 1));
        check(Visible(network, "Surface degree"), "…and appear for Fit through points");
        var fitted = Added(network);
        var oldNet = Added(Run(new NetworkSurfaceCommand(), c => { c.Slots[0].TryAdd(It(ring)); c.Slots[1].TryAdd(It(across)); }));
        check(exact.Props().Size.Z > 3 && Math.Abs(fitted.Props().Area - oldNet.Props().Area) < 1e-6, $"Fit through points matches Network Surface, and following exactly also bulges up to the curve ({exact.Props().Size.Z:0.##} mm)");

        // ...and Blend, for two separate surface edges
        var s1 = Strip(0, 20, 0); var s2 = Strip(40, 60, 10);
        int e1 = EdgeAtX(s1, 20), e2 = EdgeAtX(s2, 40);
        var blendTool = Run(new BoundarySurfaceCommand(), c => { c.Slots[0].TryAdd(new SelItem(s1.Id, SelKind.Edge, e1)); c.Slots[0].TryAdd(new SelItem(s2.Id, SelKind.Edge, e2)); });
        check(Visible(blendTool, "Fullness") && !Visible(blendTool, "Fill with"), "two separate surface edges switch Boundary to a blend");
        var blendOut = blendTool.TryBuild() ?? throw new Exception(blendTool.Error);
        var blended = blendOut.Add.Single().Shape!;
        var oldBlend = Occ.BlendSurface(s1.Shape, e1, s2.Shape, e2, 1, 1);
        check(blendOut.Remove.Count == 2 && Math.Abs(blended.Props().Area - (s1.Shape.Props().Area + s2.Shape.Props().Area + oldBlend.Props().Area)) < 1e-2, "the blend matches Blend Surface and joins to both surfaces");

        // curve tools with one button
        var ctrl = new CurveByPointsCommand(true); ctrl.Begin(S);
        ctrl.SetPoints([new(0, 0, 0), new(10, 10, 0), new(20, 0, 0), new(30, 10, 0)]);
        var through = Added(ctrl);
        Cfg(ctrl, () => Pick(ctrl, "Curve type", 1));
        check(!ctrl.Through && Visible(ctrl, "Degree") && !Visible(ctrl, "Closed"), "Curve switches to control points in its panel");
        check(Added(ctrl).Props().Size.Y < through.Props().Size.Y, "…and the curve is pulled toward the points instead of passing through them");

        var data = Run(new EquationCurveCommand(false), c => { });
        var helix = Added(data);
        Cfg(data, () => Pick(data, "Points from", 1));
        check(Visible(data, "…or read a file") && !Visible(data, "t from") && helix.Props().Size.Z > 30, "Curve from Data switches between equation and table");
        check(Math.Abs(Added(data).Props().Size.X - 60) < 1e-3, "…and the table is used");

        var corner = Curve(Occ.Polyline([new(0, 0, 0), new(30, 0, 0), new(30, 30, 0)], false));
        var round = Run(new CurveFilletCommand(false), c => { c.Slots[0].TryAdd(It(corner)); Set(c, "Fillet radius", 5); });
        Added(round);
        Cfg(round, () => Pick(round, "Corners", 1));
        check(Visible(round, "Chamfer distance") && !Visible(round, "Fillet radius"), "Fillet / Chamfer switches its size setting");
        check(Added(round) != null, "…and chamfers");

        var line = Curve(Line(new(0, 0, 0), new(100, 0, 0)));
        var trim = Run(new TrimCurveCommand(), c => c.Slots[0].TryAdd(It(line)));
        Cfg(trim, () => { Pick(trim, "Action", 1); trim.Params.OfType<TextParam>().First(p => p.Label.StartsWith("Split at")).Value = "25, 50"; });
        check((trim.TryBuild() ?? throw new Exception(trim.Error)).Add.Count == 3, "Trim Curve also cuts a curve into pieces");

        var both = new IntersectCurvesCommand(); both.Begin(S);
        Cfg(both, () => Pick(both, "Between", 1));
        check(both.Slots[0].Filter.HasFlag(SelFilter.CurveObj) && !both.Slots[0].Filter.HasFlag(SelFilter.Solid), "Intersection Curve picks curves in its Two flat curves mode");

        // the ribbons
        var vm = new MainViewModel();
        List<string> Labels(string tab) => vm.Tabs.Single(t => t.Title == tab).Buttons.Select(b => b.Label.Replace("\n", " ")).ToList();
        var nurbs = Labels("NURBS");
        check(nurbs.Contains("Create Sketch") && nurbs.Contains("Control Grid") && !nurbs.Contains("NURBS Patch") && !nurbs.Contains("Patch") && !nurbs.Contains("Network Surface") && !nurbs.Contains("Blend Surface") && !nurbs.Contains("Sweep 2 rails") && !nurbs.Contains("Deform"), "NURBS keeps Draw (K) and Control Grid, and the folded tools are gone");
        check(nurbs.Count(l => l == "Boundary Surface") == 1 && nurbs.Contains("Warp"), "NURBS lists Boundary Surface once and has Warp");
        var shape = vm.Tabs.Single(t => t.Title == "NURBS").Groups.Single(g => g.Title == "Shape").Items.OfType<Prism.Ribbon.RibbonButton>().Select(b => b.Label).ToList();
        check(shape.Contains("Edit\nPoints") && shape.Contains("Sculpt"), "Edit Points and Sculpt are direct buttons on NURBS");
        var surface = Labels("Surface");
        check(!surface.Contains("Edit Points") && !surface.Contains("Sculpt") && !surface.Contains("Control Grid") && !surface.Contains("Patch") && !surface.Contains("Ruled"), "point editing stays off Surface, and Patch and Ruled are folded in");
        foreach (var tab in new[] { "Surface", "NURBS" })
            check(vm.Tabs.Single(t => t.Title == tab).Groups.Any(g => g.Title == "Curves") && vm.Tabs.Single(t => t.Title == tab).Groups.Any(g => g.Title == "Edit curves"), $"{tab} has the shared Curves and Edit curves sections");
        var solid = Labels("Solid");
        check(solid.Contains("Sweep") && solid.Contains("Loft") && solid.Contains("Emboss") && solid.Contains("Curve"), "Solid has Sweep, Loft, Emboss and curve creation");
        var inspect = Labels("Surface");
        check(inspect.Contains("Zebra Stripes") && inspect.Contains("Curvature Map") && inspect.Contains("Curvature Graph"), "the curvature checks are in Inspect on Surface");
        foreach (var tab in new[] { "Solid", "Surface", "Freeform", "NURBS", "Sheet Metal", "ECAD" })
            check(vm.Tabs.Single(t => t.Title == tab).Groups.Single(g => g.Title == "References").Items.OfType<Prism.Ribbon.RibbonButton>().All(b => !b.HasMenu), $"{tab} shows Construction Plane as a plain button");
        var sheet = Labels("Sheet Metal");
        check(sheet.Contains("Flange") && sheet.Contains("Flat Pattern") && sheet.Contains("Hole") && sheet.Contains("Fillet") && !sheet.Contains("Subtract"), "Sheet Metal has its essentials, with Hole added and Subtract gone");
        var ecadTools = Labels("ECAD");
        check(ecadTools.Contains("Create Sketch") && ecadTools.Contains("Materials") && ecadTools.Count(l => l == "Measure") == 1 && ecadTools.Count(l => l.StartsWith("Section", StringComparison.OrdinalIgnoreCase)) == 1, "ECAD can sketch a board outline and no longer repeats Measure and Section Analysis");
        check(Labels("Script").Contains("Script Editor"), "the Script tab can open the editor without starting a new script");

        // Python coloring
        var src = "import cadkit as cad\n# note\n@wrap\ndef plate(w=1.5e2):\n    s = f'a {w}'\n    print(\"\"\"x\ny\"\"\", cad.box(w))\n";
        var spans = Prism.Core.Scripting.PythonHighlighter.Tokenize(src);
        string Of(Prism.Core.Scripting.SyntaxKind k) => string.Join("|", spans.Where(x => x.Kind == k).Select(x => src.Substring(x.Start, x.Length)));
        check(spans.Sum(x => x.Length) == src.Length && spans.Zip(spans.Skip(1)).All(p => p.First.Start + p.First.Length == p.Second.Start), "the coloring covers the whole script with no gaps");
        check(Of(Prism.Core.Scripting.SyntaxKind.Comment) == "# note" && Of(Prism.Core.Scripting.SyntaxKind.Decorator) == "@wrap" && Of(Prism.Core.Scripting.SyntaxKind.Number) == "1.5e2", "comments, decorators and numbers are colored");
        check(Of(Prism.Core.Scripting.SyntaxKind.String) == "f'a {w}'|\"\"\"x\ny\"\"\"", "strings are colored, prefixed and across lines");
        check(Of(Prism.Core.Scripting.SyntaxKind.Keyword).Contains("import") && Of(Prism.Core.Scripting.SyntaxKind.Keyword).Contains("def") && Of(Prism.Core.Scripting.SyntaxKind.Function).Contains("plate") && Of(Prism.Core.Scripting.SyntaxKind.Function).Contains("box") && Of(Prism.Core.Scripting.SyntaxKind.Builtin) == "print", "keywords, functions and builtins are colored");
        check(Prism.Core.Scripting.PythonHighlighter.Tokenize("x = 'open\ny = \"\"\"never closed").Count > 0, "an unfinished string does not break the coloring");
        check(!solid.Contains("Project") && !solid.Contains("Isocurves") && !solid.Contains("Contour") && !solid.Contains("Duplicate Edge") && !solid.Contains("Zebra Stripes") && !solid.Contains("Curvature Map"), "Solid keeps only the curves it needs and no styling checks");
        var freeformTools = Labels("Freeform");
        check(freeformTools.Contains("Curve") && freeformTools.Contains("Join") && freeformTools.Contains("Zebra Stripes") && !freeformTools.Contains("Isocurves") && !freeformTools.Contains("Curvature Map"), "Freeform has its curve set and Zebra");
        foreach (var tab in new[] { "Solid", "Freeform" })
            check(vm.Tabs.Single(t => t.Title == tab).Groups.Single(g => g.Title == "Curves").Items.Count == 1, $"{tab} shows curves as their own section");
        var modify = vm.Tabs.Single(t => t.Title == "Surface").Groups.Single(g => g.Title == "Modify").Items.OfType<Prism.Ribbon.RibbonButton>().ToList();
        check(modify[0].Label == "Trim" && modify[1].Label == "Sew" && modify[2].HasMenu, "Trim and Sew are direct buttons on Surface");
        check(vm.Tabs.Single(t => t.Title == "NURBS").Groups.Any(g => g.Title == "Convert") && vm.Tabs.Single(t => t.Title == "NURBS").Groups.Any(g => g.Title == "Continuity") && vm.Tabs.Single(t => t.Title == "Surface").Groups.All(g => g.Title != "Continuity"), "Convert is visible on NURBS, and Match Surface lives only there");
    }
}
