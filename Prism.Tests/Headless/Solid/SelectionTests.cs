using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Solid.SolidRibbonTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/interface/selection/README.md">Selection controls and projected regions.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class SelectionTests
{
    [AvaloniaFact(DisplayName = "SOL-H074 | Vertex selection opens cage editing only for a selected Freeform body")]
    [Trait("TestId", "SOL-H074")]
    public void VertexRequiresFreeformCage()
    {
        var vm = new MainViewModel();
        var vertex = SolidButtons(vm).Single(button => button.Label == "Vertex");
        var session = vm.Session;
        var solid = CreateBoxBody(session, Vec3.Zero, 4, 6, 8);
        session.SelectItems([SelItem.Of(solid)]);
        vertex.Refresh();
        Assert.False(vertex.IsEnabled);
        vertex.Invoke();
        Assert.Null(session.ActiveCommand);

        var form = session.AddObject(null, "Cage", ObjKind.Form, form: FormOps.Box(4, 6, 8, 1, 1, 1));
        session.SelectItems([SelItem.Of(form)]);
        vertex.Refresh();
        Assert.True(vertex.IsEnabled);
        vertex.Invoke();
        Assert.IsType<EditFormCommand>(session.ActiveCommand);
        Assert.True(vm.IsVertexMode);
    }

    [AvaloniaFact(DisplayName = "SOL-H075 | A partial rectangle selects a body only in crossing direction")]
    [Trait("TestId", "SOL-H075")]
    public void RectangleDirectionControlsContainment()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(-10, -10, -5), 20, 20, 10);
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        var pixel = session.Camera.Project(Vec3.Zero)!.Value;
        var inside = RegionSelect.Region.Box(pixel.x - 100, pixel.y - 100, pixel.x + 5, pixel.y + 100);
        var crossing = RegionSelect.Region.Box(pixel.x + 5, pixel.y - 100, pixel.x - 100, pixel.y + 100);
        Assert.Empty(RegionSelect.Collect(session, inside, SelMode.Object, through: true));
        Assert.Equal(source.Id, Assert.Single(RegionSelect.Collect(session, crossing, SelMode.Object, through: true)).Obj);
    }

    [AvaloniaFact(DisplayName = "SOL-H076 | Through includes a fully occluded body in region selection")]
    [Trait("TestId", "SOL-H076")]
    public void ThroughIncludesOccludedBody()
    {
        var session = new CadSession();
        var front = CreateBoxBody(session, new Vec3(-15, -15, 10), 30, 30, 4);
        var back = CreateBoxBody(session, new Vec3(-5, -5, -10), 10, 10, 4);
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        var pixel = session.Camera.Project(Vec3.Zero)!.Value;
        var region = RegionSelect.Region.Box(pixel.x - 150, pixel.y - 150, pixel.x + 150, pixel.y + 150);
        Assert.Equal(front.Id, Assert.Single(RegionSelect.Collect(session, region, SelMode.Object, through: false)).Obj);
        var result = RegionSelect.Collect(session, region, SelMode.Object, through: true);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Obj == back.Id);
    }

    static PointerInfo Down(double x, double y) => new(x, y, true, false, false, false);
    static PointerInfo Up(double x, double y) => new(x, y, false, false, false, false);

    [AvaloniaFact(DisplayName = "SOL-H081 | A drag on empty space draws a selection rectangle without arming a method first")]
    [Trait("TestId", "SOL-H081")]
    public void EmptySpaceDragSelectsWithoutArming()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new Vec3(-10, -10, -5), 20, 20, 10);
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        var pixel = session.Camera.Project(Vec3.Zero)!.Value;
        Assert.Equal(SelectMethod.Click, session.Method);    // the default: nothing armed

        session.PointerDown(Down(pixel.x + 140, pixel.y + 100));   // press on empty space
        session.PointerMove(Down(pixel.x - 140, pixel.y - 100));
        Assert.True(session.DrawingRegion);                        // the rectangle appears
        session.PointerUp(Up(pixel.x - 140, pixel.y - 100));       // right to left: whatever it touches
        Assert.Contains(session.Selection, item => item.Obj == body.Id);
    }

    [AvaloniaFact(DisplayName = "SOL-H082 | A drag on empty space keeps working when there is nothing to hit")]
    [Trait("TestId", "SOL-H082")]
    public void EmptyCanvasDragDrawsRectangle()
    {
        var session = new CadSession();                      // completely empty scene
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        session.PointerDown(Down(50, 50));
        session.PointerMove(Down(180, 140));
        Assert.True(session.DrawingRegion);
        session.PointerUp(Up(180, 140));
        Assert.False(session.DrawingRegion);
        Assert.Empty(session.Selection);
    }

    [AvaloniaFact(DisplayName = "SOL-H083 | Paint select takes whatever the stroke crosses and nothing else")]
    [Trait("TestId", "SOL-H083")]
    public void PaintSelectsOnlyCrossedBodies()
    {
        var session = new CadSession();
        var hitBody = CreateBoxBody(session, new Vec3(-10, -10, -5), 20, 20, 10);
        var missed = CreateBoxBody(session, new Vec3(60, 60, -5), 10, 10, 10);
        session.Method = SelectMethod.Paint;
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        var at = session.Camera.Project(Vec3.Zero)!.Value;
        var off = session.Camera.Project(new Vec3(65, 65, 0))!.Value;

        session.PointerDown(Down(at.x - 120, at.y));
        session.PointerMove(Down(at.x - 40, at.y + 3));      // the stroke passes over the box
        session.PointerMove(Down(at.x + 40, at.y - 2));
        session.PointerMove(Down(off.x - 160, off.y + 160)); // and well away from the second body
        Assert.True(session.DrawingRegion);
        session.PointerUp(Up(off.x - 160, off.y + 160));
        Assert.Contains(session.Selection, item => item.Obj == hitBody.Id);
        Assert.DoesNotContain(session.Selection, item => item.Obj == missed.Id);
    }

    [AvaloniaFact(DisplayName = "SOL-H084 | The datum gizmo is global: it places and drags without a sketch or command")]
    [Trait("TestId", "SOL-H084")]
    public void DatumGizmoWorksOutsideSketch()
    {
        var session = new CadSession();
        session.Camera.SetView(45, 45);                   // an angled view spreads the gizmo's axes
        session.Camera.Orthographic = true;
        Assert.Null(session.Sketch); Assert.Null(session.ActiveCommand);

        session.ShowDatumGizmo(Vec3.Zero);
        Assert.True(session.DatumVisible);                   // global: no sketch, no command
        Assert.Equal(Vec3.Zero, session.Datum!.Frame.Origin);

        var pixel = session.Camera.Project(Vec3.Zero)!.Value;
        Assert.True(session.PointerDown(Down(pixel.x, pixel.y)));   // grabbing the gizmo centre claims the press
        session.PointerMove(Down(pixel.x + 60, pixel.y + 20));
        session.PointerUp(Up(pixel.x + 60, pixel.y + 20));
        Assert.NotEqual(Vec3.Zero, session.Datum!.Frame.Origin);    // the frame actually moved

        Assert.True(session.Datum.Key(session, "Escape"));
        Assert.False(session.DatumVisible);
    }
}
