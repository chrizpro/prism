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
}
