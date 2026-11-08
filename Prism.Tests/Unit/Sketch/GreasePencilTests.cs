using System.Text.Json;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sketches/README.md#grease-pencil">Grease pencil specification.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Unit")]
public class GreasePencilTests
{
    static PointerInfo Pointer(double x, double y, bool left = true) => new(x, y, left, false, false, false);

    [Theory(DisplayName = "SKT-GP005 | Each palette color survives preview, serialization, cloning and undo")]
    [InlineData("red", 0xFFEF4444u)]
    [InlineData("blue", 0xFF3B82F6u)]
    [InlineData("green", 0xFF22C55Eu)]
    [InlineData("yellow", 0xFFFACC15u)]
    [InlineData("purple", 0xFFA855F7u)]
    [Trait("TestId", "SKT-GP005")]
    public void PaletteColorPersistsPerStroke(string name, uint expected)
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY);
        var sk = s.Sketch!;
        var tool = new GreasePencilTool();
        sk.SetTool(s, tool);
        tool.PanelSet(s, "color-" + name, true);
        Assert.Equal("color-" + name, Assert.Single(tool.Panel!, c => c.Id.StartsWith("color-") && c.On).Id);
        tool.PointerDown(s, Pointer(400, 300));
        tool.PointerMove(s, Pointer(420, 310));
        var preview = new Overlay();
        tool.BuildOverlay(s, preview);
        Assert.Equal(expected, Assert.Single(preview.Lines).Color);
        tool.PointerUp(s, Pointer(440, 300, false));
        tool.PanelSet(s, "color-" + (name == "red" ? "blue" : "red"), true);
        var restored = JsonSerializer.Deserialize<SketchModel>(JsonSerializer.Serialize(sk.ExportModel()))!;
        Assert.Equal(expected, Assert.Single(restored.Clone().GreaseStrokes).Color);
        var overlay = new Overlay();
        SketchContext.DrawGrease(overlay, restored, sk.Plane);
        Assert.All(overlay.Lines, line => Assert.Equal(expected, line.Color));
        tool.PanelSet(s, "remove-last", null);
        Assert.True(sk.TryUndo(s));
        Assert.Equal(expected, Assert.Single(sk.Model.GreaseStrokes).Color);
    }

    [Fact(DisplayName = "SKT-GP001 | A freehand stroke is one undoable annotation without modeling entities")]
    [Trait("TestId", "SKT-GP001")]
    public void StrokeIsIndependentOfGeometryAndUndoable()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY);
        var sk = s.Sketch!;
        var tool = new GreasePencilTool();
        sk.SetTool(s, tool);
        Assert.True(tool.PointerDown(s, Pointer(400, 300)));
        tool.PointerMove(s, Pointer(420, 310));
        tool.PointerUp(s, Pointer(440, 300, false));
        Assert.Equal(3, Assert.Single(sk.Model.GreaseStrokes).Points.Count);
        Assert.Empty(sk.Model.Entities);
        Assert.Single(sk.Model.Points);
        Assert.True(sk.HasContent);
        Assert.True(sk.TryUndo(s));
        Assert.Empty(sk.Model.GreaseStrokes);
        Assert.False(sk.HasContent);
    }

    [Fact(DisplayName = "SKT-GP002 | Escape cancels a stroke; 3D sketch draws planar marks only in orthographic view")]
    [Trait("TestId", "SKT-GP002")]
    public void CancelAndOrthographicRestriction()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY);
        var sk = s.Sketch!;
        var tool = new GreasePencilTool();
        sk.SetTool(s, tool);
        tool.PointerDown(s, Pointer(400, 300));
        tool.PointerMove(s, Pointer(420, 310));
        Assert.True(tool.Key(s, "Escape"));
        tool.PointerUp(s, Pointer(440, 300, false));
        Assert.Empty(sk.Model.GreaseStrokes);
        sk.Set3DEnabled(s, true);
        s.SketchProjection(perspective: true);
        Assert.False(tool.PointerDown(s, Pointer(400, 300)));
        s.SketchProjection(perspective: false);
        Assert.True(tool.PointerDown(s, Pointer(400, 300)));
        tool.PointerUp(s, Pointer(440, 300, false));
        Assert.Single(sk.Model.GreaseStrokes);
        s.SketchProjection(perspective: true);
        Assert.Single(sk.Model.GreaseStrokes);
        var overlay = new Overlay();
        SketchContext.DrawGrease(overlay, sk.Model, sk.Plane);
        Assert.Single(overlay.Lines);
    }

    [Fact(DisplayName = "SKT-GP003 | Grease marks survive export and serialization with independent undo copies")]
    [Trait("TestId", "SKT-GP003")]
    public void MarksRoundTripAndCloneIndependently()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY);
        var sk = s.Sketch!;
        var tool = new GreasePencilTool();
        sk.SetTool(s, tool);
        tool.Step(s, 2);
        tool.PointerDown(s, Pointer(400, 300));
        tool.PointerUp(s, Pointer(440, 320, false));
        var model = sk.ExportModel();
        var restored = JsonSerializer.Deserialize<SketchModel>(JsonSerializer.Serialize(model))!;
        var stroke = Assert.Single(restored.GreaseStrokes);
        Assert.Equal(5, stroke.Width);
        Assert.Equal(model.GreaseStrokes[0].Points, stroke.Points);
        var clone = restored.Clone();
        clone.GreaseStrokes[0].Points.Clear();
        Assert.Equal(2, stroke.Points.Count);
        tool.PanelSet(s, "remove-last", null);
        Assert.Empty(sk.Model.GreaseStrokes);
        Assert.True(sk.TryUndo(s));
        Assert.Single(sk.Model.GreaseStrokes);
    }

    [Fact(DisplayName = "SKT-GP004 | View strokes in 3D stay on the plane captured at pointer down")]
    [Trait("TestId", "SKT-GP004")]
    public void ViewPlaneIsFrozenForEntireStroke()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY);
        var sk = s.Sketch!;
        sk.Set3DEnabled(s, true);
        s.Camera.SetView(-35, 28);
        var tool = new GreasePencilTool();
        sk.SetTool(s, tool);
        tool.PanelSet(s, "view-plane", true);
        tool.PanelSet(s, "orthographic", null);
        var normal = s.Camera.EyeDirection;
        s.PointerDown(Pointer(400, 300));
        s.PointerMove(Pointer(420, 310));
        s.Camera.SetView(-20, 40);
        s.PointerUp(Pointer(440, 320, false));
        var stroke = Assert.Single(sk.Model.GreaseStrokes);
        Assert.Equal(3, stroke.Points.Count);
        Assert.Contains(stroke.Points, p => Math.Abs(p.Depth) > 1e-6);
        foreach (var p in stroke.Points)
        {
            var world = sk.Plane.PointAt(p.U, p.V) + sk.Plane.Normal * p.Depth;
            Assert.InRange(Math.Abs(Vec3.Dot(world - sk.Plane.Origin, normal)), 0, 1e-8);
        }
        var restored = JsonSerializer.Deserialize<SketchModel>(JsonSerializer.Serialize(sk.ExportModel()))!;
        Assert.Equal(stroke.Points, Assert.Single(restored.GreaseStrokes).Points);
    }
}
