using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Selection;

/// <summary>
/// Edge selection mode picks every kind of curve (owner request 2026-11-19: "make sure that all curves could be selected if we are in edge mode"):
/// straight and curved edges of solids and surfaces, curve objects (line, arc, circle, spline, polyline) and the curves of a finished sketch.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/selection/README.md">Selection.</seealso>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public sealed class EdgeModeCurveTests(Xunit.Abstractions.ITestOutputHelper log)
{
    /// <summary>A point on one of the object's displayed edges, part way along its first segment (not a corner, where a vertex or face may win).</summary>
    static Vec3 OnEdge(SceneObject o, int segment = 0)
    {
        var e = o.Mesh.EdgePoints; int i = 6 * segment;
        var a = new Vec3(e[i], e[i + 1], e[i + 2]); var b = new Vec3(e[i + 3], e[i + 4], e[i + 5]);
        return a + (b - a) * 0.5;
    }

    [AvaloniaFact(DisplayName = "SEL-H064 | Edge mode picks straight and curved edges of solids and surfaces, curve objects of every type and the curves of a sketch")]
    [Trait("TestId", "SEL-H064")]
    public void EveryCurveIsPickableInEdgeMode()
    {
        var s = new CadSession();
        var objects = new List<(string what, SceneObject obj)>
        {
            ("box edge", s.AddObject(BoxAt(new Vec3(0, 0, 0), 10, 10, 10), "Box", ObjKind.Solid)),
            ("cylinder rim", s.AddObject(CylinderAt(new Vec3(30, 5, 0), 5, 10), "Cylinder", ObjKind.Solid)),
            ("line", s.AddObject(Occ.Line(new Vec3(0, 30, 0), new Vec3(10, 30, 0)), "Line", ObjKind.Curve)),
            ("arc", s.AddObject(Occ.Arc3(new Vec3(20, 30, 0), new Vec3(25, 35, 0), new Vec3(30, 30, 0)), "Arc", ObjKind.Curve)),
            ("circle", s.AddObject(Occ.Circle(new Vec3(45, 30, 0), Vec3.UnitZ, 4), "Circle", ObjKind.Curve)),
            ("spline", s.AddObject(Occ.Spline([new Vec3(0, 50, 0), new Vec3(5, 55, 0), new Vec3(10, 50, 0), new Vec3(15, 55, 0)]), "Spline", ObjKind.Curve)),
            ("polyline", s.AddObject(Occ.Polyline([new Vec3(20, 50, 0), new Vec3(25, 55, 0), new Vec3(30, 50, 0)], false), "Polyline", ObjKind.Curve)),
        };
        using (var sheet = BoxAt(new Vec3(40, 0, 0), 10, 10, 1)) objects.Add(("surface edge", s.AddObject(sheet.Sub(SubKind.Face, 0), "Sheet", ObjKind.Surface)));
        objects.Add(("sketch curve", CreateRectangleSketch(s, Plane3.XY, 8, 8, 40, 45)));
        s.ZoomExtents();
        s.Mode = SelMode.Edge;
        var missed = new List<string>();
        foreach (var (what, obj) in objects)
        {
            bool hit = false;
            // try a few segments: one may be hidden behind the object's own faces from this view
            for (int seg = 0; seg < Math.Min(6, obj.Mesh.EdgePoints.Length / 6) && !hit; seg++)
            {
                if (s.Camera.Project(OnEdge(obj, seg)) is not { } at) continue;
                if (s.Pick(at.x, at.y, s.ActiveFilter) is { } r && r.Item.Obj == obj.Id && r.Item.Kind == SelKind.Edge) hit = true;
            }
            log.WriteLine($"{what}: {(hit ? "picked" : "MISSED")}");
            if (!hit) missed.Add(what);
        }
        Assert.Empty(missed);
    }
}
