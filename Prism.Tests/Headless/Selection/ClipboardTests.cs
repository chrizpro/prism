using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Selection;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/selection/README.md#copy-paste-and-alt-drag-copy">Copy, paste and Alt-drag copy.</seealso>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public class ClipboardTests
{
    [AvaloniaFact(DisplayName = "SEL-H028 | Copy and Paste add an independent copy in the same place and select it")]
    [Trait("TestId", "SEL-H028")]
    public void CopyAndPaste()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new Vec3(5, 5, 0), 10, 10, 10);
        session.Update(body.Id, o => o.WithColor(0xFF336699), checkpoint: false);
        Assert.False(session.CanCopy);
        session.SelectObjects([body.Id]);
        Assert.True(session.CanCopy);
        Assert.True(session.CopySelection());

        session.SelectObjects([]);                                  // the copy stands on its own: edits to the original change nothing
        session.Update(body.Id, o => o.WithShape(Occ.Translate(o.Shape, new Vec3(100, 0, 0))), checkpoint: false);
        Assert.True(session.CanPaste);
        var pasted = Assert.Single(session.Paste());
        Assert.Equal(2, session.Objects.Count);
        Assert.NotEqual(body.Id, pasted.Id);
        Assert.Equal(0xFF336699u, pasted.Color);
        Assert.Equal(10, pasted.Shape.Props().Size.X, 6);
        Assert.Equal(5, pasted.Mesh.Min.X, 6);                       // where it was when copied, not where the original went
        Assert.Equal([SelItem.Of(pasted)], session.Selection);

        session.Undo();                                             // one undo step takes the paste away
        Assert.Single(session.Objects);
        Assert.Contains(Prism.Core.Hotkeys.Actions, h => h.Id == "edit.copy" && h.Default == "Ctrl+C");
        Assert.Contains(Prism.Core.Hotkeys.Actions, h => h.Id == "edit.paste" && h.Default == "Ctrl+V");
    }

    [AvaloniaFact(DisplayName = "SEL-H029 | Alt-dragging a gizmo arrow in Move leaves the original and moves a copy")]
    [Trait("TestId", "SEL-H029")]
    public void AltDragCopies()
    {
        foreach (bool alt in new[] { false, true })
        {
            var session = new CadSession();
            var body = CreateBoxBody(session, new Vec3(-5, -5, 0), 10, 10, 10);
            session.Camera.Width = 800; session.Camera.Height = 600; session.Camera.SetView(0, 0); session.Camera.Fit(new(-10, -10, 0), new(30, 10, 10));
            session.ShowDatumGizmo(Vec3.Zero);
            var gizmo = Assert.IsType<DatumGizmoTool>(session.Datum);
            gizmo.MoveMode = true;
            gizmo.BindTargets(session, [SelItem.Of(body)]);
            var origin = gizmo.Frame.Origin;
            double length = GizmoVisual.Length(session, origin);
            var grab = session.Camera.Project(origin + Vec3.UnitX * length * 0.7)!.Value;
            var start = session.Camera.Project(origin + Vec3.UnitX * length * 0.7)!.Value;
            var end = session.Camera.Project(origin + Vec3.UnitX * (length * 0.7 + 20))!.Value;
            session.PointerDown(new PointerInfo(grab.x, grab.y, true, false, false, alt));
            session.PointerMove(new PointerInfo(end.x, end.y, true, false, false, alt));
            session.PointerUp(new PointerInfo(end.x, end.y, false, false, false, alt));

            if (!alt) { Assert.Single(session.Objects); Assert.True(session.Find(body.Id)!.Mesh.Min.X > -4.9); continue; }
            Assert.Equal(2, session.Objects.Count);
            Assert.Equal(-5, session.Find(body.Id)!.Mesh.Min.X, 3);   // the original stayed
            var copy = Assert.Single(session.Objects, o => o.Id != body.Id);
            Assert.True(copy.Mesh.Min.X > -4, $"the copy was dragged along X: {copy.Mesh.Min.X}");
            Assert.Equal([SelItem.Of(copy)], session.Selection);
        }
    }
}
