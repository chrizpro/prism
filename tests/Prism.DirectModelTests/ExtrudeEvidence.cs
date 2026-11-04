using System.Text.Json;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Rendering;

/// <summary>Characterization of existing implementation, authorized by the owner on 2026-11-03.
/// These checks do not approve the open product decisions in Stories #6, #7 and #8.</summary>
static class ExtrudeEvidence
{
    const double Tol = 1e-4; // OCCT bounding boxes include a small tolerance.
    static readonly List<object> Results = [];
    static readonly HashSet<string> Images = [];
    static string Output = "";
    static void Require(bool condition, string why) { if (!condition) throw new Exception(why); }
    static void Near(double actual, double expected, string what) => Require(double.IsFinite(actual) && Math.Abs(actual - expected) < Tol,
        $"{what}: expected {expected:G12}, got {actual:G12}");
    static T Param<T>(Command c, string label) where T : Prism.Commands.Param => c.Params.OfType<T>().Single(p => p.Label == label);
    static void Number(Command c, string label, double value) => Param<NumberParam>(c, label).Value = value;
    static void Choice(Command c, string label, int value) => Param<ChoiceParam>(c, label).Index = value;
    static Shape At(Shape shape, Vec3 min) => Occ.Translate(shape, min - shape.Props().Min);
    static (CadSession s, SceneObject profile) Fixture(bool open = false)
    {
        var m = new SketchModel();
        int[] p = [m.AddPoint(0, 0), m.AddPoint(10, 0), m.AddPoint(10, 6), m.AddPoint(0, 6)];
        for (int i = 0; i < (open ? 3 : 4); i++) m.AddLine(p[i], p[(i + 1) % 4]);
        var s = new CadSession(); s.EnterSketch(Plane3.XY, m); s.ExitSketch(true);
        return (s, s.Objects.Single());
    }
    static ExtrudeCommand Start(CadSession s, SceneObject profile, bool surface = false)
    {
        s.SelectItems([SelItem.Of(profile)]); s.StartCommand(() => new ExtrudeCommand(surface));
        var c = (ExtrudeCommand)s.ActiveCommand!;
        if (!surface) Choice(c, "Operation", 0); // Explicit New body isolates extent from automatic Cut.
        return c;
    }
    static Shape Build(ExtrudeCommand c) => (c.TryBuild() ?? throw new Exception(c.Error ?? "No output")).Add.Single().Shape!;
    static void Solid(Shape shape, double volume, double zmin, double zmax)
    {
        Require(shape.IsValid, "OCCT validity"); Require(shape.Count(SubKind.Solid) == 1, "one solid");
        Require(shape.Count(SubKind.Face) == 6, "six planar faces");
        var p = shape.Props(); Near(Math.Abs(p.Volume), volume, "volume");
        Near(p.Min.X, 0, "min X"); Near(p.Max.X, 10, "max X"); Near(p.Min.Y, 0, "min Y"); Near(p.Max.Y, 6, "max Y");
        Near(p.Min.Z, zmin, "min Z"); Near(p.Max.Z, zmax, "max Z");
    }
    static void Image(MeshData mesh, string name)
    {
        var pixels = StudioPreview.Render(new(mesh, [], [], false, name), false);
        Require(pixels.Bgra.Distinct().Count() > 24, "render contains geometry shading/edges");
        PngWriter.Save(Path.Combine(Output, name + ".png"), pixels.Bgra, pixels.Width, pixels.Height);
        Images.Add(name + ".png");
    }
    static void Image(Shape shape, string name)
    {
        var scene = new CadSession(); var obj = scene.AddObject(shape, name);
        Image(obj.Mesh, name); scene.Save(Path.Combine(Output, name + ".prism"));
    }
    public static int Run()
    {
        Output = Path.GetFullPath(Environment.GetEnvironmentVariable("PRISM_EVIDENCE_DIR") ?? "artifacts/extrude");
        Directory.CreateDirectory(Output);
        int passed = 0, failed = 0;
        var report = new List<string> { "# Extrude local characterization", "", $"Run: {DateTimeOffset.Now:O}",
            "", "Existing observable behavior; proposed requirements remain awaiting owner review. Refs Stories #6–8; Solid spec: docs/modeling/extrude/README.md. Surface/Freeform drafts remain in PR #9.",
            "", "Images are software renders of actual command geometry using Prism StudioPreview, not interactive OpenGL viewport captures or synthetic tool-help examples. Each image uses automatic fit, so it does not establish relative scale. Geometry is asserted separately.", "", "| Case | Result | Detail |", "|---|---|---|" };
        void Case(string id, Action run)
        {
            try { run(); passed++; Results.Add(new { id, status = "PASS" }); report.Add($"| {id} | PASS | |"); Console.WriteLine("PASS " + id); }
            catch (Exception e) { failed++; Results.Add(new { id, status = "FAIL", error = e.ToString() }); report.Add($"| {id} | FAIL | {e.Message.Replace('|', '/').Replace('\n', ' ')} |"); Console.WriteLine("FAIL " + id + ": " + e.Message); }
        }
        foreach (var (id, extent, distance, second, reverse, lo, hi) in new[] {
            ("solid-distance", 0, 12d, 0d, false, 0d, 12d),
            ("solid-negative", 0, -12d, 0d, false, -12d, 0d),
            ("solid-reverse", 0, 12d, 0d, true, -12d, 0d),
            ("solid-negative-reverse", 0, -12d, 0d, true, 0d, 12d),
            ("solid-two-sides", 1, 12d, 4d, false, -4d, 12d),
            ("solid-two-sides-reverse", 1, 12d, 4d, true, -12d, 4d),
            ("solid-symmetric", 2, 12d, 0d, false, -6d, 6d) })
            Case(id, () => {
                var (s, profile) = Fixture(); var c = Start(s, profile);
                c.ConfigureInputs(() => { Choice(c, "Extent", extent); Number(c, "Distance", distance); Number(c, "Distance 2 (other side)", second); Param<BoolParam>(c, "Reverse direction").Value = reverse; });
                var shape = Build(c); Solid(shape, 60 * (hi - lo), lo, hi);
                s.Save(Path.Combine(Output, id + "-input.prism"));
                Image(profile.Mesh, id + "-input"); Image(shape, id + "-result");
                var replay = new ExtrudeCommand(); replay.Begin(s); replay.Restore(c.Capture()!); Solid(Build(replay), 60 * (hi - lo), lo, hi);
            });
        foreach (bool surface in new[] { false, true })
            Case(surface ? "surface-open-chain" : "solid-rejects-open-chain", () => {
                var (s, profile) = Fixture(true); var c = Start(s, profile, surface); Number(c, "Distance", 12);
                if (!surface) { Require(c.TryBuild() == null && !string.IsNullOrEmpty(c.Error), "open solid rejected with error"); Require(s.Objects.Count == 1, "document preserved"); return; }
                var shape = Build(c); Require(shape.IsValid && shape.Count(SubKind.Solid) == 0 && shape.Count(SubKind.Face) == 3, "three valid surface faces, no solid");
                Near(shape.Props().Area, 26 * 12, "surface area"); Near(shape.Props().Max.Z, 12, "height");
                s.Save(Path.Combine(Output, "surface-open-chain-input.prism"));
                Image(profile.Mesh, "surface-open-chain-input"); Image(shape, "surface-open-chain-result");
            });
        foreach (var (id, extent, reverse, lo, hi) in new[] { ("surface-reverse", 0, true, -12d, 0d), ("surface-two-sides", 1, false, -4d, 12d), ("surface-symmetric", 2, false, -6d, 6d) })
            Case(id, () => {
                var (s, profile) = Fixture(true); var c = Start(s, profile, true);
                c.ConfigureInputs(() => { Choice(c, "Extent", extent); Number(c, "Distance", 12); Number(c, "Distance 2 (other side)", 4); Param<BoolParam>(c, "Reverse direction").Value = reverse; });
                var shape = Build(c); Require(shape.IsValid && shape.Count(SubKind.Solid) == 0 && shape.Count(SubKind.Face) == 3, "valid uncapped surface topology");
                Near(shape.Props().Area, 26 * (hi - lo), "surface area"); Near(shape.Props().Min.Z, lo, "surface min Z"); Near(shape.Props().Max.Z, hi, "surface max Z");
                var replay = new ExtrudeCommand(true); replay.Begin(s); replay.Restore(c.Capture()!); Near(Build(replay).Props().Area, 26 * (hi - lo), "replayed area");
                Image(shape, id + "-result");
            });
        Case("zero-distance-preserves-document", () => {
            var (s, profile) = Fixture(); var c = Start(s, profile); Number(c, "Distance", 0);
            Require(c.TryBuild() == null, "zero distance has no output"); s.CommitCommand(); Require(s.Objects.Count == 1 && s.Objects[0].Id == profile.Id, "failed commit preserves sketch");
        });
        Case("through-all-without-body", () => {
            var (s, profile) = Fixture(); var c = Start(s, profile); Choice(c, "Extent", 4);
            Require(c.TryBuild() == null && c.Error?.Contains("no body", StringComparison.OrdinalIgnoreCase) == true, "explicit no-body diagnostic");
        });
        foreach (var (id, mode, lo, hi) in new[] { ("through-all", 4, 0d, 21d), ("through-all-both", 5, -5d, 21d) })
            Case(id, () => {
                var (s, profile) = Fixture(); s.AddObject(At(Occ.Box(2, 2, 24), new Vec3(0, 0, -4)), "Extent reference", ObjKind.Solid);
                // Reference occupies z=-4..20. Explicit New body avoids automatic Cut.
                var c = Start(s, profile); Choice(c, "Extent", mode); var shape = Build(c); Solid(shape, 60 * (hi - lo), lo, hi); Image(shape, id + "-result");
            });
        foreach (var (id, stop, offset, height) in new[] { ("to-body-near", 0, 0d, 10d), ("to-body-far-offset", 1, 2d, 16d) })
            Case(id, () => {
                var (s, profile) = Fixture(); var target = s.AddObject(At(Occ.Box(2, 2, 4), new Vec3(0, 0, 10)), "Target", ObjKind.Solid);
                var c = Start(s, profile); c.ConfigureInputs(() => { Choice(c, "Extent", 3); Choice(c, "Stop at", stop); Number(c, "Offset from the target", offset); c.Slots.Single(x => x.Label.StartsWith("Extend to")).TryAdd(SelItem.Of(target)); });
                Solid(Build(c), 60 * height, 0, height);
            });
        foreach (var (name, op, expected) in new[] { ("join", 1, 2280d), ("cut", 2, 1560d), ("intersect", 3, 360d) })
            Case("boolean-" + name, () => {
                var (s, profile) = Fixture(); var target = s.AddObject(At(Occ.Box(20, 16, 6), new Vec3(-5, -5, 0)), "Target", ObjKind.Solid);
                var untouched = s.AddObject(Occ.Translate(Occ.Box(2, 2, 2), new Vec3(50, 0, 0)), "Other body", ObjKind.Solid);
                var c = Start(s, profile); c.ConfigureInputs(() => { Number(c, "Distance", 12); Choice(c, "Operation", op); c.Slots.Single(x => x.Label.StartsWith("Target body")).TryAdd(SelItem.Of(target)); });
                var output = c.TryBuild() ?? throw new Exception(c.Error); var replacement = output.Replace.Single();
                Require(output.Add.Count == 0 && replacement.Old.Id == target.Id, "only chosen target replaced");
                Require(replacement.New.IsValid, "valid Boolean result"); Near(Math.Abs(replacement.New.Props().Volume), expected, "Boolean volume");
                Near(Math.Abs(untouched.Shape.Props().Volume), 8, "unrelated volume"); Image(replacement.New, "boolean-" + name + "-result");
            });
        Case("history-edit-undo-redo-save-reopen", () => {
            var (s, profile) = Fixture(); var c = Start(s, profile); Number(c, "Distance", 12); s.CommitCommand();
            Require(s.ActiveCommand == null && !s.StatusIsError, "commit succeeds");
            Solid(s.Objects.Single(o => o.Kind == ObjKind.Solid).Shape, 720, 0, 12);
            s.Undo(); Require(s.Objects.All(o => o.Kind != ObjKind.Solid), "undo removes solid"); s.Redo();
            Solid(s.Objects.Single(o => o.Kind == ObjKind.Solid).Shape, 720, 0, 12);
            Require(s.EditFeature(s.Current), "feature opens"); Number(s.ActiveCommand!, "Distance", 18); s.CommitCommand();
            Solid(s.Objects.Single(o => o.Kind == ObjKind.Solid).Shape, 1080, 0, 18);
            string path = Path.Combine(Output, "edited-extrude.prism"); s.Save(path); var loaded = new CadSession(); loaded.Open(path);
            Solid(loaded.Objects.Single(o => o.Kind == ObjKind.Solid).Shape, 1080, 0, 18);
            Require(loaded.Current.Recipe?.Title == "Extrude" && loaded.EditFeature(loaded.Current), "saved recipe remains editable");
            Number(loaded.ActiveCommand!, "Distance", 9); loaded.CommitCommand(); Solid(loaded.Objects.Single(o => o.Kind == ObjKind.Solid).Shape, 540, 0, 9);
        });
        foreach (var (id, axis, distance, scale, cap) in new[] { ("freeform-normal-capped", 0, 12d, 1d, true), ("freeform-z-taper-open", 3, -12d, .5d, false), ("freeform-x", 1, 12d, 1d, true), ("freeform-y", 2, 12d, 1d, true) })
            Case(id, () => {
                var (s, profile) = Fixture(); s.SelectItems([SelItem.Of(profile)]); s.StartCommand(() => new FormExtrudeCommand()); var c = s.ActiveCommand!;
                c.ConfigureInputs(() => { Choice(c, "Direction", axis); Number(c, "Distance", distance); Number(c, "Scale at the far end", scale); Param<IntParam>(c, "Points on the profile").Value = 8; Param<IntParam>(c, "Steps along the way").Value = 3; Param<IntParam>(c, "Smoothness (levels)").Value = 1; Param<BoolParam>(c, "Close the ends (closed profile)").Value = cap; });
                var form = (c.TryBuild() ?? throw new Exception(c.Error)).Add.Single().Form!;
                Require(form.V.Count == 32 && form.F.Count == (cap ? 26 : 24), "cage rows, side quads and cap counts"); Require(form.IsClosed() == cap, "cap controls closure");
                var direction = axis == 1 ? Vec3.UnitX : axis == 2 ? Vec3.UnitY : Vec3.UnitZ;
                for (int i = 0; i < 8; i++) {
                    var first = form.V[i]; var last = form.V[24 + i]; var center = form.V.Take(8).Aggregate(Vec3.Zero, (a, b) => a + b) / 8;
                    Require(last.DistanceTo(center + (first - center) * scale + direction * distance) < Tol, "far row follows signed direction and scale");
                }
                s.CommitCommand(); Require(s.Objects.Any(o => o.Kind == ObjKind.Form), "form commits"); Image(s.Objects.Single(o => o.Kind == ObjKind.Form).Mesh, id + "-result"); s.Save(Path.Combine(Output, id + "-result.prism"));
                s.Undo(); Require(s.Objects.All(o => o.Kind != ObjKind.Form), "form undo"); s.Redo(); Require(s.Objects.Single(o => o.Kind == ObjKind.Form).Form!.V.Count == 32, "form redo preserves cage");
            });
        report.AddRange(["", $"Passed: {passed}; failed: {failed}.", "", "## Actual command geometry renders", ""]);
        foreach (var path in Images.Order()) report.Add($"![{Path.GetFileNameWithoutExtension(path)}]({path})");
        report.AddRange(["", "## Not covered by this run", "", "Tapered exact solids; region selection; face profiles; plane/face To object; hidden-body extents; automatic Cut/reversal; negative two-sided/symmetric semantics; Freeform Modify Extrude; interactive selection and OpenGL preview. These require further coverage; uncertain semantics remain owner decisions. Revolve, Loft and Sweep remain next phases."]);
        File.WriteAllText(Path.Combine(Output, "report.md"), string.Join(Environment.NewLine, report));
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{(failed == 0 ? "SUCCESS" : "FAIL")} Extrude evidence: {passed} passed, {failed} failed; {Output}");
        return failed == 0 ? 0 : 1;
    }
}
