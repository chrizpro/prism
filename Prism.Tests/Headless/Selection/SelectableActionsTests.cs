using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;
namespace Prism.HeadlessTests.Selection;
/// <summary>
/// Everything selectable can be copied and pasted, deleted and moved (owner rule 2026-11-20). The only exceptions are ones that would tear a body:
/// an edge or corner of a solid does not move on its own, and a lone corner of a solid, surface or finished sketch is not deleted (open the sketch to
/// delete its points); each of these says why instead of doing nothing.
/// </summary>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public sealed class SelectableActionsTests(Xunit.Abstractions.ITestOutputHelper log)
{
    static (CadSession s, Dictionary<string, SelItem> items) Scene()
    {
        var s = new CadSession();
        var box = s.AddObject(BoxAt(new Vec3(0, 0, 0), 10, 10, 10), "Box", ObjKind.Solid);
        var curve = s.AddObject(Occ.Line(new Vec3(0, 20, 0), new Vec3(10, 20, 0)), "Line", ObjKind.Curve);
        using (var face = BoxAt(new Vec3(0, 40, 0), 10, 10, 1)) s.AddObject(face.Sub(SubKind.Face, 0), "Sheet", ObjKind.Surface);
        var surface = s.Objects.Last();
        var sketch = CreateRectangleSketch(s, Plane3.XY, 5, 5, 30, 0);
        var mesh = s.AddObject(null, "Scan", ObjKind.Mesh);
        s.Update(mesh.Id, o => o.WithTri(Prism.Meshing.MeshAnalysis.FromMeshData(box.Mesh)), checkpoint: false);
        using (var rect = Occ.Polyline([new Vec3(-5, -5, 30), new Vec3(5, -5, 30), new Vec3(5, 5, 30), new Vec3(-5, 5, 30)], true))
            s.AddObject(Occ.Profile([rect], true), "Plane", ObjKind.Plane, plane: new Plane3(new Vec3(0, 0, 30), Vec3.UnitX, Vec3.UnitY));
        var plane = s.Objects.Last();
        var items = new Dictionary<string, SelItem>
        {
            ["solid"] = SelItem.Of(box), ["solid face"] = new(box.Id, SelKind.Face, 0), ["solid edge"] = new(box.Id, SelKind.Edge, 0), ["solid vertex"] = new(box.Id, SelKind.Vertex, 0),
            ["curve"] = SelItem.Of(curve), ["curve edge"] = new(curve.Id, SelKind.Edge, 0), ["curve vertex"] = new(curve.Id, SelKind.Vertex, 0),
            ["surface"] = SelItem.Of(surface), ["surface face"] = new(surface.Id, SelKind.Face, 0), ["surface edge"] = new(surface.Id, SelKind.Edge, 0), ["surface vertex"] = new(surface.Id, SelKind.Vertex, 0),
            ["sketch"] = SelItem.Of(sketch), ["sketch edge"] = new(sketch.Id, SelKind.Edge, 0), ["sketch vertex"] = new(sketch.Id, SelKind.Vertex, 0),
            ["mesh"] = SelItem.Of(s.Find(mesh.Id)!), ["mesh triangle"] = new(mesh.Id, SelKind.MeshFace, 0),
            ["datum plane"] = SelItem.Of(plane),
        };
        return (s, items);
    }

    [AvaloniaFact(DisplayName = "SEL-H067 | Every selectable thing (objects of each kind, their faces, edges, points, mesh triangles, datum planes) copies, pastes, deletes and moves, apart from the listed exceptions")]
    [Trait("TestId", "SEL-H067")]
    public void Run()
    {
        foreach (var name in Scene().items.Keys)
        {
            var problems = new List<string>();
            // copy + paste
            { var (s, items) = Scene(); s.SelectItems([items[name]]); bool can = s.CanCopy; bool ok = can && s.CopySelection(); int n = s.Objects.Count; if (ok) s.Paste(); if (!ok || s.Objects.Count <= n) problems.Add($"copy/paste (canCopy={can}) {s.Status}"); }
            // delete
            {
                var (s, items) = Scene(); var it = items[name]; s.SelectItems([it]);
                var before = s.Find(it.Obj)!; int n = s.Objects.Count; s.DeleteSelection(); var after = s.Find(it.Obj);
                bool changed = after == null || !ReferenceEquals(after, before) || s.Objects.Count != n;
                if (!changed) problems.Add($"delete: {s.Status}");
            }
            // move
            {
                var (s, items) = Scene(); s.SelectItems([items[name]]);
                s.StartCommand(() => new TransformCommand(TransformCommand.Kind.Move));
                var c = s.ActiveCommand!; c.ConfigureInputs(() => SetNumber(c, "Δ X", 5));
                if (c.Slots[0].Items.Count == 0) problems.Add("move: not taken");
                else if (c.TryBuild() == null) problems.Add($"move: {c.Error}");
                s.CancelCommand();
            }
            log.WriteLine($"{name,-15} {(problems.Count == 0 ? "ok" : string.Join(" | ", problems))}");
            var expected = name switch { "solid edge" => new[] { "move" }, "solid vertex" => ["delete", "move"], "surface vertex" or "sketch vertex" => ["delete"], _ => [] };
            Assert.Equal(expected, problems.Select(p => p.Split(':', ' ')[0]).ToArray());
            Assert.All(problems, p => Assert.True(p.Length > 20, $"{name}: a refusal must say why ({p})"));
        }
    }
}
