using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Viewport;

/// <summary>
/// The home orientation: one View tab button (and one view cube button) that sets the current view as home and,
/// once set, resets to the original orientation. The orientation is saved in the document.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/view-home/README.md">Home orientation.</seealso>
[Trait("Feature", "Viewport home")]
[Trait("Layer", "Headless")]
public class HomeOrientationTests
{
    static Prism.Ribbon.RibbonButton HomeButton(MainViewModel vm) => vm.Tabs.Single(t => t.Title == "View").Buttons.Single(b => b.Label.EndsWith("Home"));

    static Camera At((double yaw, double pitch, double roll) a) => new() { Yaw = a.yaw, Pitch = a.pitch, Roll = a.roll };

    static void AssertSameView(Camera expected, Camera actual)
    {
        Assert.True((expected.EyeDirection - actual.EyeDirection).Length < 1e-9);
        Assert.True((expected.Up - actual.Up).Length < 1e-9);
    }


    [AvaloniaFact(DisplayName = "HOME-H001 | A new document keeps the stock orientation and the button offers Set Home")]
    [Trait("TestId", "HOME-H001")]
    public void StockOrientationOffersSetHome()
    {
        var vm = new MainViewModel();
        var s = vm.Session;
        Assert.True(s.HomeIsOriginal);
        var button = HomeButton(vm);
        Assert.Equal("Set\nHome", button.Label);
        var stock = new Camera { Yaw = -35 * Math.PI / 180, Pitch = 28 * Math.PI / 180 };
        AssertSameView(stock, At(s.HomeAngles()));
        var front = At(s.HomeViewAngles(0, 0));
        Assert.True((front.EyeDirection - new Vec3(0, -1, 0)).Length < 1e-9);
    }

    [AvaloniaFact(DisplayName = "HOME-H002 | Set Home makes the current view the home view and turns the standard views, without moving geometry")]
    [Trait("TestId", "HOME-H002")]
    public void SetHomeTurnsStandardViews()
    {
        var s = new CadSession();
        var solid = CreateBoxBody(s, Vec3.Zero, 4, 6, 8);
        var meshBefore = s.Objects.Single().Mesh.Max;
        var cam = s.Cameras[0]; cam.Yaw = 1.1; cam.Pitch = -0.4; cam.Roll = 0.3;
        var eye = cam.EyeDirection; var up = cam.Up;
        s.Dirty = false;

        s.SetHomeToCurrentView();

        Assert.False(s.HomeIsOriginal);
        Assert.True(s.Dirty);
        AssertSameView(cam, At(s.HomeAngles()));           // Home returns to the view that was set
        Assert.True((At(s.HomeAngles()).EyeDirection - eye).Length < 1e-9 && (At(s.HomeAngles()).Up - up).Length < 1e-9);
        var stockFront = new Camera { Yaw = 0, Pitch = 0 };
        var front = At(s.HomeViewAngles(0, 0));            // Front now looks along the part's own -Y axis
        Assert.True((front.EyeDirection - s.FromHome(stockFront.EyeDirection)).Length < 1e-9);
        Assert.True((front.Up - s.FromHome(stockFront.Up)).Length < 1e-9);
        Assert.Equal(meshBefore, s.Objects.Single().Mesh.Max);   // only the view changed
        Assert.Equal(solid.Id, s.Objects.Single().Id);
    }

    [AvaloniaFact(DisplayName = "HOME-H003 | After a change the same button reads Reset Home and restores the original orientation")]
    [Trait("TestId", "HOME-H003")]
    public void ButtonBecomesResetHome()
    {
        var vm = new MainViewModel();
        var s = vm.Session;
        var button = HomeButton(vm);
        s.Camera.Yaw = 0.7; s.Camera.Pitch = 0.9;

        button.Invoke();
        Assert.False(s.HomeIsOriginal);
        Assert.Equal("Reset\nHome", button.Label);
        Assert.Equal("home-reset", button.Icon);

        button.Invoke();
        Assert.True(s.HomeIsOriginal);
        Assert.Equal("Set\nHome", button.Label);
        Assert.Equal("home-set", button.Icon);
        AssertSameView(new Camera { Yaw = -35 * Math.PI / 180, Pitch = 28 * Math.PI / 180 }, At(s.HomeAngles()));
    }

    [AvaloniaFact(DisplayName = "HOME-H004 | The home orientation is saved in the file and a new document starts original")]
    [Trait("TestId", "HOME-H004")]
    public void OrientationPersists()
    {
        string folder = Path.Combine(Path.GetTempPath(), "prism-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "part.prism");
            var s = new CadSession();
            CreateBoxBody(s, Vec3.Zero, 4, 6, 8);
            s.Cameras[0].Yaw = 2.0; s.Cameras[0].Pitch = 0.5;
            s.SetHomeToCurrentView();
            var frame = s.HomeFrame.ToArray();
            s.Save(path);

            var reopened = new CadSession();
            reopened.Open(path);
            Assert.False(reopened.HomeIsOriginal);
            Assert.Equal(frame.Length, reopened.HomeFrame.Length);
            for (int i = 0; i < frame.Length; i++) Assert.Equal(frame[i], reopened.HomeFrame[i], 9);

            reopened.NewDocument();
            Assert.True(reopened.HomeIsOriginal);

            var plain = new CadSession(); CreateBoxBody(plain, Vec3.Zero, 1, 1, 1);
            string plainPath = Path.Combine(folder, "plain.prism"); plain.Save(plainPath);
            var again = new CadSession(); again.Open(plainPath);
            Assert.True(again.HomeIsOriginal);
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact(DisplayName = "HOME-H005 | Starting Loft from an orthographic or straight-on view moves to perspective; a chosen perspective view is left alone")]
    [Trait("TestId", "HOME-H005")]
    public void LoftStartsInPerspectiveFromTheHomeView()
    {
        var s = new MainViewModel().Session;
        s.Ortho = true;
        s.StartCommand(() => new Prism.Commands.LoftCommand());
        Assert.False(s.Ortho); Assert.False(s.Camera.Orthographic);
        s.CancelCommand();

        s.Ortho = false;
        s.Camera.SetView(0, 0);                 // straight-on front view, even in perspective
        s.StartCommand(() => new Prism.Commands.LoftCommand());
        Assert.False(s.Ortho);
        s.CancelCommand();

        s.Camera.SetView(-50, 20);              // an angle the user chose
        double yaw = s.Camera.Yaw, pitch = s.Camera.Pitch;
        s.StartCommand(() => new Prism.Commands.LoftCommand());
        Assert.Equal(yaw, s.Camera.Yaw); Assert.Equal(pitch, s.Camera.Pitch);
        s.CancelCommand();
    }

    [AvaloniaFact(DisplayName = "HOME-H006 | Only tools that pick edges, curves or faces around the model open in the home view; Extrude, Revolve and the rest stay as they are")]
    [Trait("TestId", "HOME-H006")]
    public void OnlyPickHeavyToolsOpenInTheHomeView()
    {
        var s = new MainViewModel().Session;
        var opted = new Func<Prism.Commands.Command>[]
        {
            () => new Prism.Commands.LoftCommand(), () => new Prism.Commands.LoftCommand(true), () => new Prism.Commands.SweepCommand(),
            () => new Prism.Commands.BoundarySurfaceCommand(), () => new Prism.Commands.RuledSurfaceCommand(), () => new Prism.Commands.PatchCommand(),
            () => new Prism.Commands.BlendSurfaceCommand(), () => new Prism.Commands.ShellCommand(),
        };
        foreach (var make in opted)
        {
            s.Ortho = true;
            s.StartCommand(make);
            Assert.False(s.Ortho, make().Title);
            s.CancelCommand();
        }
        var untouched = new Func<Prism.Commands.Command>[] { () => new Prism.Commands.ExtrudeCommand(), () => new Prism.Commands.RevolveCommand(), () => new Prism.Commands.MeasureCommand() };
        foreach (var make in untouched)
        {
            s.Ortho = true;
            s.StartCommand(make);
            Assert.True(s.Ortho, make().Title);
            s.CancelCommand();
        }
    }
}
