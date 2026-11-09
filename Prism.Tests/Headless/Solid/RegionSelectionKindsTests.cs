using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <summary>
/// Rectangle, lasso and paint selection reach every kind of thing a click can: bodies, surfaces, curves, sketches, Freeform cages,
/// construction planes and datums (points, axes, planes, gizmos).
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/interface/selection/README.md">Selection controls and projected regions.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class RegionSelectionKindsTests
{
    static CadSession TopView()
    {
        var session = new CadSession();
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        return session;
    }

    static (double x, double y) Px(CadSession s, Vec3 p) { var q = s.Camera.Project(p)!.Value; return (q.x, q.y); }

    static SketchDatum Datum(CadSession s, SketchDatumKind kind, Vec3 at)
    {
        var f = kind == SketchDatumKind.Axis ? new Plane3(at, Vec3.UnitX, Vec3.UnitY) : new Plane3(at, Vec3.UnitX, Vec3.UnitY);
        return s.AddModelDatum(new SketchDatum { Kind = kind, Frame = f });
    }

    /// <summary>A rectangle around the given world points, padded in pixels. Right-to-left makes it a crossing rectangle.</summary>
    static RegionSelect.Region Around(CadSession s, double pad, bool crossing, params Vec3[] points)
    {
        var px = points.Select(p => Px(s, p)).ToList();
        double x0 = px.Min(p => p.x) - pad, x1 = px.Max(p => p.x) + pad, y0 = px.Min(p => p.y) - pad, y1 = px.Max(p => p.y) + pad;
        return crossing ? RegionSelect.Region.Box(x1, y0, x0, y1) : RegionSelect.Region.Box(x0, y0, x1, y1);
    }

    [AvaloniaFact(DisplayName = "SOL-H097 | A window rectangle selects every kind of datum inside it and leaves a far one alone")]
    [Trait("TestId", "SOL-H097")]
    public void WindowSelectsAllDatumKinds()
    {
        var session = TopView();
        var kinds = new[] { SketchDatumKind.Point, SketchDatumKind.Axis, SketchDatumKind.Plane, SketchDatumKind.Gizmo };
        var spots = kinds.Select((_, i) => new Vec3(i * 6, 0, 0)).ToArray();
        for (int i = 0; i < kinds.Length; i++) Datum(session, kinds[i], spots[i]);
        Datum(session, SketchDatumKind.Point, new Vec3(1500, 1500, 0));

        var found = RegionSelect.Collect(session, Around(session, 110, false, spots), SelMode.Object, through: false);
        Assert.Equal(4, found.Count(i => i.Kind == SelKind.Datum));
        Assert.DoesNotContain(found, i => i.Kind == SelKind.Datum && i.Index == 4);
    }

    [AvaloniaFact(DisplayName = "SOL-H098 | A datum axis sticking out of a window rectangle is left alone, but a crossing rectangle takes it")]
    [Trait("TestId", "SOL-H098")]
    public void CrossingTakesAxisWindowDoesNot()
    {
        var session = TopView();
        Datum(session, SketchDatumKind.Axis, Vec3.Zero);
        var c = Px(session, Vec3.Zero);
        var window = RegionSelect.Region.Box(c.x - 8, c.y - 8, c.x + 8, c.y + 8);
        var crossing = RegionSelect.Region.Box(c.x + 8, c.y - 8, c.x - 8, c.y + 8);
        Assert.Empty(RegionSelect.Collect(session, window, SelMode.Object, through: true));
        Assert.Single(RegionSelect.Collect(session, crossing, SelMode.Object, through: true));
    }

    [AvaloniaFact(DisplayName = "SOL-H099 | Lasso and paint select a datum point they enclose or cross")]
    [Trait("TestId", "SOL-H099")]
    public void LassoAndPaintSelectDatums()
    {
        var session = TopView();
        Datum(session, SketchDatumKind.Point, Vec3.Zero);
        var c = Px(session, Vec3.Zero);

        var lasso = new RegionSelect.Region();
        foreach (var p in new[] { (c.x - 30, c.y - 30), (c.x + 30, c.y - 30), (c.x + 30, c.y + 30), (c.x - 30, c.y + 30) }) lasso.Points.Add(p);
        Assert.Single(RegionSelect.Collect(session, lasso, SelMode.Object, through: false));

        var paint = new RegionSelect.Region { Stroke = true };
        paint.Points.Add((c.x - 40, c.y)); paint.Points.Add((c.x + 40, c.y));
        Assert.Single(RegionSelect.Collect(session, paint, SelMode.Object, through: false));

        var miss = new RegionSelect.Region { Stroke = true };
        miss.Points.Add((c.x - 40, c.y + 50)); miss.Points.Add((c.x + 40, c.y + 50));
        Assert.Empty(RegionSelect.Collect(session, miss, SelMode.Object, through: false));
    }

    [AvaloniaFact(DisplayName = "SOL-H100 | One rectangle takes a solid, surface, curve, sketch, Freeform cage, construction plane and datums together")]
    [Trait("TestId", "SOL-H100")]
    public void OneRectangleTakesEveryKind()
    {
        var session = TopView();
        var solid = CreateBoxBody(session, new Vec3(0, 0, 0), 10, 10, 10);
        using var loop = Occ.Polyline([new(30, 0, 0), new(40, 0, 0), new(40, 10, 0), new(30, 10, 0)], true);
        var surface = session.AddObject(Occ.Profile([loop], true), "Sheet", ObjKind.Surface);
        var curve = CreateCurve(session, [new(60, 0, 0), new(70, 10, 0)]);
        var sketch = CreateRectangleSketch(session, Plane3.XY with { Origin = new Vec3(90, 0, 0) }, 10, 10);
        using var outline = Occ.Polyline([new(120, -10, 0), new(140, -10, 0), new(140, 10, 0), new(120, 10, 0)], true);
        var plane = session.AddObject(outline, "Plane", ObjKind.Plane, plane: Plane3.XY with { Origin = new Vec3(130, 0, 0) });
        var form = session.AddObject(null, "Cage", ObjKind.Form, form: FormOps.Box(4, 6, 8, 1, 1, 1));
        var mesh = Prism.HeadlessTests.Mesh.MeshToolTests.AddMesh(session, Prism.HeadlessTests.Mesh.MeshToolTests.BoxMesh(new Vec3(200, 0, 0), 10, 10, 10));
        Datum(session, SketchDatumKind.Point, new Vec3(160, 0, 0));
        Datum(session, SketchDatumKind.Gizmo, new Vec3(170, 0, 0));

        var region = Around(session, 150, true, new Vec3(0, 0, 0), new Vec3(210, 10, 0));
        var found = RegionSelect.Collect(session, region, SelMode.Object, through: true);
        foreach (var obj in new[] { solid, surface, curve, sketch, plane, form, mesh })
            Assert.Contains(found, i => i.Kind == SelKind.Object && i.Obj == obj.Id);
        Assert.Equal(2, found.Count(i => i.Kind == SelKind.Datum));
    }

    [AvaloniaFact(DisplayName = "SOL-H104 | Face and Edge levels work by drag on a surface, and give a mesh or cage what a click would")]
    [Trait("TestId", "SOL-H104")]
    public void FaceAndEdgeLevelsByDrag()
    {
        var session = TopView();
        using var loop = Occ.Polyline([new(0, 0, 0), new(20, 0, 0), new(20, 20, 0), new(0, 20, 0)], true);
        var sheet = session.AddObject(Occ.Profile([loop], true), "Sheet", ObjKind.Surface);
        var around = Around(session, 40, true, new Vec3(0, 0, 0), new Vec3(20, 20, 0));
        Assert.Contains(RegionSelect.Collect(session, around, SelMode.Face, through: false), i => i.Obj == sheet.Id && i.Kind == SelKind.Face);
        var edges = RegionSelect.Collect(session, Around(session, 40, false, new Vec3(0, 0, 0), new Vec3(20, 20, 0)), SelMode.Edge, through: false);
        Assert.Equal(4, edges.Count(i => i.Obj == sheet.Id && i.Kind == SelKind.Edge));
        var corner = Px(session, new Vec3(0, 0, 0));
        var touch = RegionSelect.Region.Box(corner.x + 6, corner.y + 6, corner.x - 6, corner.y - 6);
        Assert.InRange(RegionSelect.Collect(session, touch, SelMode.Edge, through: false).Count(i => i.Obj == sheet.Id), 1, 3);

        var mesh = Prism.HeadlessTests.Mesh.MeshToolTests.AddMesh(session, Prism.HeadlessTests.Mesh.MeshToolTests.BoxMesh(new Vec3(100, 0, 0), 10, 10, 10));
        var cage = session.AddObject(null, "Cage", ObjKind.Form, form: FormOps.Box(10, 10, 10, 1, 1, 1));
        var all = Around(session, 300, true, new Vec3(0, 0, 0), new Vec3(110, 10, 0));
        var faces = RegionSelect.Collect(session, all, SelMode.Face, through: true);
        Assert.Contains(faces, i => i.Obj == mesh.Id && i.Kind == SelKind.Face && i.Index == 0);
        Assert.Contains(faces, i => i.Obj == cage.Id && i.Kind == SelKind.Face);
    }

    [AvaloniaFact(DisplayName = "SOL-H105 | A placed picture is selected by a drag like the plane it sits on")]
    [Trait("TestId", "SOL-H105")]
    public void PictureSelectsByDrag()
    {
        var session = TopView();
        var plane = Plane3.XY with { Origin = new Vec3(50, 0, 0) };
        var info = new Prism.Core.Assembly.ImageInfo("AAAA", 10, 10, 20);
        var output = new CommandOutput { Label = "Import image", Icon = "image" };
        using var frame = Occ.Polyline([new(40, -10, 0), new(60, -10, 0), new(60, 10, 0), new(40, 10, 0)], true);
        output.Add.Add(new(frame, "Image", ObjKind.Plane, null, plane, null, null, null, info));
        session.Apply(output);
        var picture = session.Objects.Last();
        var found = RegionSelect.Collect(session, Around(session, 60, true, new Vec3(0, 0, 0), new Vec3(100, 20, 0)), SelMode.Object, through: false);
        Assert.Contains(found, i => i.Obj == picture.Id);
    }

    [AvaloniaFact(DisplayName = "SOL-H101 | A datum behind a solid is selected through the part only when Through is on")]
    [Trait("TestId", "SOL-H101")]
    public void DatumBehindSolidNeedsThrough()
    {
        var session = TopView();
        CreateBoxBody(session, new Vec3(-20, -20, 10), 40, 40, 10);
        Datum(session, SketchDatumKind.Point, new Vec3(0, 0, -30));
        var c = Px(session, Vec3.Zero);
        var inside = RegionSelect.Region.Box(c.x + 20, c.y - 20, c.x - 20, c.y + 20);
        Assert.DoesNotContain(RegionSelect.Collect(session, inside, SelMode.Object, through: false), i => i.Kind == SelKind.Datum);
        Assert.Contains(RegionSelect.Collect(session, inside, SelMode.Object, through: true), i => i.Kind == SelKind.Datum);
    }

    [AvaloniaFact(DisplayName = "SOL-H103 | Invert with nothing selected takes the datums too, and with one datum selected takes the rest")]
    [Trait("TestId", "SOL-H103")]
    public void InvertIncludesDatums()
    {
        var session = TopView();
        for (int i = 0; i < 3; i++) Datum(session, SketchDatumKind.Point, new Vec3(i * 8, 0, 0));
        var body = CreateBoxBody(session, new Vec3(100, 0, 0), 10, 10, 10);
        session.InvertSelection();
        Assert.Equal(3, session.Selection.Count(i => i.Kind == SelKind.Datum));
        Assert.Contains(session.Selection, i => i.Obj == body.Id);

        var first = session.Selection.First(i => i.Kind == SelKind.Datum);
        session.SelectItems([first]);
        session.InvertSelection();
        Assert.Equal(2, session.Selection.Count(i => i.Kind == SelKind.Datum));
        Assert.DoesNotContain(first, session.Selection);
    }

    [AvaloniaFact(DisplayName = "SOL-H106 | Selected and hovered datums are drawn orange and blue in the viewport, whether picked by click or by drag")]
    [Trait("TestId", "SOL-H106")]
    public void SelectedDatumsAreHighlighted()
    {
        var session = TopView();
        for (int i = 0; i < 3; i++) Datum(session, SketchDatumKind.Point, new Vec3(i * 8, 0, 0));
        var o = new Overlay(); session.BuildOverlay(o);
        Assert.DoesNotContain(o.Dots, d => d.Color == 0xFFFF8A1F);

        var a = Px(session, new Vec3(-10, 0, 0)); var b = Px(session, new Vec3(30, 0, 0));
        session.PointerDown(new(b.x + 80, a.y + 80, true, false, false, false));
        session.PointerMove(new(a.x - 80, a.y - 80, true, false, false, false));
        session.PointerUp(new(a.x - 80, a.y - 80, false, false, false, false));
        o = new Overlay(); session.BuildOverlay(o);
        Assert.Equal(3, o.Dots.Count(d => d.Color == 0xFFFF8A1F));

        session.SetSelection(session.Selection[0], false);
        session.Hover = session.Selection[0] with { Index = (session.Selection[0].Index + 1) % 3 };
        o = new Overlay(); session.BuildOverlay(o);
        Assert.Equal(1, o.Dots.Count(d => d.Color == 0xFFFF8A1F));
        Assert.Equal(1, o.Dots.Count(d => d.Color == 0xFF35B6FF));
    }

    [AvaloniaFact(DisplayName = "SOL-H102 | Dragging a rectangle over datums in the session selects them, so Move and Delete can act on them")]
    [Trait("TestId", "SOL-H102")]
    public void SessionDragSelectsDatums()
    {
        var session = TopView();
        for (int i = 0; i < 3; i++) Datum(session, SketchDatumKind.Point, new Vec3(i * 8, 0, 0));
        var a = Px(session, new Vec3(-10, 0, 0)); var b = Px(session, new Vec3(30, 0, 0));
        session.PointerDown(new(b.x + 80, a.y + 80, true, false, false, false));
        session.PointerMove(new(a.x - 80, a.y - 80, true, false, false, false));
        session.PointerUp(new(a.x - 80, a.y - 80, false, false, false, false));
        Assert.Equal(3, session.Selection.Count(i => i.Kind == SelKind.Datum));
    }
}
