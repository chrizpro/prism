using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Images;

/// <summary>Exercises the reference picture calibration command through viewport pointer coordinates.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/reference-images/README.md">Reference images guide.</seealso>
[Trait("Feature", "Images")]
[Trait("Layer", "Headless")]
public class ImageCalibrationTests
{
    [AvaloniaFact(DisplayName = "IMG-H005 | Window key events switch image gizmo modes")]
    [Trait("TestId", "IMG-H005")]
    public void WindowRoutesGizmoKeys()
    {
        var vm = new MainViewModel();
        vm.Session.StartCommand(() => new PlaceImageCommand());
        var cmd = Assert.IsType<PlaceImageCommand>(vm.Session.ActiveCommand);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            void KeyPress(Key key) => window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            KeyPress(Key.R); Assert.Equal(1, cmd.GizmoMode.Index);
            KeyPress(Key.S); Assert.Equal(2, cmd.GizmoMode.Index);
            KeyPress(Key.T); Assert.Equal(0, cmd.GizmoMode.Index);
        }
        finally { vm.Session.CancelCommand(); window.Close(); }
    }
    [AvaloniaFact(DisplayName = "IMG-H004 | Picture handles update the editor and cancellation restores the image")]
    [Trait("TestId", "IMG-H004")]
    public void EditHandlesAndCancel()
    {
        var s = new CadSession();
        var plane = Plane3.XY;
        var info = new ImageInfo("", 200, 100, 200);
        var frame = Occ.Polyline([plane.PointAt(-100, -50), plane.PointAt(100, -50), plane.PointAt(100, 50), plane.PointAt(-100, 50)], true);
        var output = new CommandOutput();
        output.Add.Add(new(frame, "Image", ObjKind.Plane, null, plane, null, null, null, info));
        s.Apply(output); var image = s.Objects.Single();
        s.Camera.Pitch = Math.PI / 2; s.Camera.Yaw = 0; s.Camera.Orthographic = true;
        var cmd = StartCommand(s, image, () => new EditImageCommand());
        Assert.IsType<ImageGizmoTool>(cmd.Tool);
        var center = cmd.GizmoPlane!.Value.Origin;
        double length = GizmoVisual.Length(s, center);
        var from = s.Camera.Project(center + plane.U * length * .8)!.Value;
        var to = s.Camera.Project(center + plane.U * (length * .8 + 20))!.Value;
        s.PointerDown(new PointerInfo(from.x, from.y, true, false, false, true));
        s.PointerMove(new PointerInfo(to.x, to.y, true, false, false, true));
        s.PointerUp(new PointerInfo(to.x, to.y, true, false, false, true));
        Assert.Equal(20, cmd.Params.OfType<NumberParam>().Single(p => p.Label == "Move right").Value, 2);
        Assert.Equal(20, s.ImageOverrides[image.Id].plane.Origin.X, 2);
        Assert.True(s.Key("Char:R"));
        center = cmd.GizmoPlane!.Value.Origin;
        length = GizmoVisual.Length(s, center);
        from = s.Camera.Project(center + plane.U * length * .8)!.Value;
        to = s.Camera.Project(center + plane.V * length * .8)!.Value;
        s.PointerDown(new PointerInfo(from.x, from.y, true, false, false, true));
        s.PointerMove(new PointerInfo(to.x, to.y, true, false, false, true));
        s.PointerUp(new PointerInfo(to.x, to.y, true, false, false, true));
        Assert.Equal(90, cmd.Params.OfType<NumberParam>().Single(p => p.Label == "Turn").Value, 2);
        Assert.True(s.Key("Char:S"));
        center = cmd.GizmoPlane!.Value.Origin;
        var projected = s.Camera.Project(center)!.Value;
        from = projected;
        to = (projected.x + 20, projected.y, projected.depth);
        s.PointerDown(new PointerInfo(from.x, from.y, true, false, false, true));
        s.PointerMove(new PointerInfo(to.x, to.y, true, false, false, true));
        s.PointerUp(new PointerInfo(to.x, to.y, true, false, false, true));
        Assert.Equal(400, cmd.Params.OfType<NumberParam>().Single(p => p.Label == "Width").Value, 1);
        s.CancelCommand();
        Assert.Equal(0, s.Find(image.Id)!.SketchPlane!.Value.Origin.X, 4);
        cmd = StartCommand(s, s.Find(image.Id)!, () => new EditImageCommand());
        SetNumber(cmd, "Move right", 15);
        SetNumber(cmd, "Width", 300);
        s.CommitCommand();
        Assert.Equal(15, s.Find(image.Id)!.SketchPlane!.Value.Origin.X, 4);
        Assert.Equal(300, ((ImageInfo)s.Find(image.Id)!.Asm!).WidthMm, 4);
        s.Undo();
        Assert.Equal(0, s.Find(image.Id)!.SketchPlane!.Value.Origin.X, 4);
        Assert.Equal(200, ((ImageInfo)s.Find(image.Id)!.Asm!).WidthMm, 4);
    }
    [AvaloniaFact(DisplayName = "IMG-H003 | Import exposes the picture and updates its preview dimensions")]
    [Trait("TestId", "IMG-H003")]
    public void ImportExposesImagePreview()
    {
        string path = Path.Combine(Path.GetTempPath(), $"prism-image-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aWQAAAAASUVORK5CYII="));
        try
        {
            var s = new CadSession();
            var cmd = Begin(s, () => new PlaceImageCommand(path));
            Assert.NotNull(cmd.ImagePreview);
            Assert.IsType<ImageGizmoTool>(cmd.Tool);
            Assert.True(s.Key("Char:R")); Assert.Equal(1, cmd.GizmoMode.Index);
            Assert.True(s.Key("Char:S")); Assert.Equal(2, cmd.GizmoMode.Index);
            Assert.True(s.Key("Char:T")); Assert.Equal(0, cmd.GizmoMode.Index);
            SetNumber(cmd, "Width", 50);
            Assert.Equal(50, cmd.ImagePreview!.Value.info.WidthMm);
            s.CancelCommand();
            Assert.Empty(s.Objects);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact(DisplayName = "IMG-H002 | Picture selection works over a visible origin plane")]
    [Trait("TestId", "IMG-H002")]
    public void SelectPictureOverOriginPlane()
    {
        var s = new CadSession();
        var plane = Plane3.XY;
        var info = new ImageInfo("", 200, 100, 200);
        var frame = Occ.Polyline([plane.PointAt(-100, -50), plane.PointAt(100, -50), plane.PointAt(100, 50), plane.PointAt(-100, 50)], true);
        var output = new CommandOutput();
        output.Add.Add(new(frame, "Image", ObjKind.Plane, null, plane, null, null, null, info));
        s.Apply(output);
        var image = s.Objects.Single();
        s.Camera.Pitch = Math.PI / 2;
        s.Camera.Yaw = 0;
        s.Camera.Orthographic = true;
        s.SetOriginVisible(0, true);
        var cmd = Begin(s, () => new CalibrateImageCommand());
        var screen = s.Camera.Project(Vec3.Zero)!.Value;
        s.PointerDown(new PointerInfo(screen.x, screen.y, true, false, false, false));
        Assert.Equal(image.Id, cmd.Target?.obj.Id);
    }

    [AvaloniaFact(DisplayName = "IMG-H001 | Two picked points set picture scale around the first point")]
    [Trait("TestId", "IMG-H001")]
    public void CalibrateFromViewportPoints()
    {
        var s = new CadSession();
        var plane = Plane3.XY;
        var info = new ImageInfo("", 200, 100, 200);
        var frame = Occ.Polyline([plane.PointAt(-100, -50), plane.PointAt(100, -50), plane.PointAt(100, 50), plane.PointAt(-100, 50)], true);
        var output = new CommandOutput();
        output.Add.Add(new(frame, "Image", ObjKind.Plane, null, plane, null, null, null, info));
        s.Apply(output);
        var image = s.Objects.Single();
        var cmd = StartCommand(s, image, () => new CalibrateImageCommand());
        s.Camera.Pitch = Math.PI / 2;
        s.Camera.Yaw = 0;
        s.Camera.Orthographic = true;
        void Click(Vec3 p)
        {
            var screen = s.Camera.Project(p)!.Value;
            s.PointerDown(new PointerInfo(screen.x, screen.y, true, false, false, false));
        }
        Click(new Vec3(-20, 0, 0));
        Click(new Vec3(20, 0, 0));
        SetNumber(cmd, "Real distance between the two points", 80);
        SetChoice(cmd, "In", "mm");
        s.CommitCommand();
        var result = s.Find(image.Id)!;
        Assert.Equal(400, ((ImageInfo)result.Asm!).WidthMm, 3);
        Assert.Equal(20, result.SketchPlane!.Value.Origin.X, 3);
    }
}
