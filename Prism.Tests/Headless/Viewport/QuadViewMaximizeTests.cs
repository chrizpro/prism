using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Rendering;
using Prism.ViewModels;
using Prism.Views;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Viewport;

[Trait("Feature", "Viewport")]
[Trait("Layer", "Headless")]
public class QuadViewMaximizeTests
{
    sealed class NoopGlHost : IGlViewportHost
    {
        public Control Visual { get; } = new Border();
        public void RequestFrame() { }
    }

    static (MainWindow window, MainView view, MainViewModel vm) OpenMainWindow()
    {
        GlViewportHosts.Factory ??= _ => new NoopGlHost();
        var vm = new MainViewModel { HomeOpen = false };
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = (MainView)window.Content!;
        return (window, view, vm);
    }

    static Point CenterInWindow(Control target, Window window)
    {
        var origin = target.TranslatePoint(new Point(target.Bounds.Width * 0.5, target.Bounds.Height * 0.5), window);
        Assert.True(origin.HasValue, $"Expected {target.Name} to be laid out in window");
        return origin.Value;
    }

    static void TapSpace(Window window)
    {
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(DisplayName = "QUAD-H001 | Hovering any pane in Four Views and pressing Space maximizes it and Space again restores all four")]
    [Trait("TestId", "QUAD-H001")]
    public void HoverAndSpaceTogglesEachQuadPane()
    {
        var (window, view, vm) = OpenMainWindow();
        try
        {
            var s = vm.Session;
            CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
            s.SetLayout(ViewLayout.Quad);
            Dispatcher.UIThread.RunJobs();

            Grid[] cells =
            [
                view.FindControl<Grid>("Cell0")!,
                view.FindControl<Grid>("Cell1")!,
                view.FindControl<Grid>("Cell2")!,
                view.FindControl<Grid>("Cell3")!,
            ];
            var sepV = view.FindControl<Border>("SepV")!;
            var sepH = view.FindControl<Border>("SepH")!;

            for (int index = 0; index < 4; index++)
            {
                window.MouseMove(CenterInWindow(cells[index], window));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(index, view.HoveredQuadViewIndex());

                TapSpace(window);

                Assert.Equal(ViewLayout.Quad, s.Layout);
                Assert.Equal(index, s.MaximizedView);
                Assert.Same(s.Cameras[index], s.Camera);
                Assert.Equal(1, s.VisibleViews);
                Assert.Same(s.Cameras[index], Assert.Single(s.VisibleCameras));
                for (int i = 0; i < 4; i++)
                {
                    Assert.Equal(i == index, cells[i].IsVisible);
                    if (i == index)
                    {
                        Assert.Equal(2, Grid.GetColumnSpan(cells[i]));
                        Assert.Equal(2, Grid.GetRowSpan(cells[i]));
                    }
                }
                Assert.False(sepV.IsVisible);
                Assert.False(sepH.IsVisible);

                // Pressing Space again while hovering over the maximized pane returns to the 4-window view
                TapSpace(window);

                Assert.Equal(ViewLayout.Quad, s.Layout);
                Assert.Null(s.MaximizedView);
                Assert.Equal(4, s.VisibleViews);
                for (int i = 0; i < 4; i++)
                {
                    Assert.True(cells[i].IsVisible);
                    Assert.Equal(1, Grid.GetColumnSpan(cells[i]));
                    Assert.Equal(1, Grid.GetRowSpan(cells[i]));
                }
                Assert.True(sepV.IsVisible);
                Assert.True(sepH.IsVisible);
            }

            // Moving pointer over another quadrant while maximized updates which quadrant is hovered once restored
            window.MouseMove(CenterInWindow(cells[1], window));
            Dispatcher.UIThread.RunJobs();
            TapSpace(window);
            Assert.Equal(1, s.MaximizedView);

            var grid = ViewGrid(view);
            var bottomRight = grid.TranslatePoint(new Point(grid.Bounds.Width * 0.75, grid.Bounds.Height * 0.75), window);
            Assert.True(bottomRight.HasValue);
            window.MouseMove(bottomRight.Value);
            Dispatcher.UIThread.RunJobs();

            TapSpace(window);
            Assert.Null(s.MaximizedView);
            Assert.Equal(0, view.HoveredQuadViewIndex());

            TapSpace(window);
            Assert.Equal(0, s.MaximizedView);
        }
        finally { window.Close(); }
    }

    static Grid ViewGrid(MainView view) => view.FindControl<Grid>("ViewGrid")!;

    [AvaloniaFact(DisplayName = "QUAD-H002 | Focused ribbon button does not steal Space on KeyDown or KeyUp, and holding Space does not revert")]
    [Trait("TestId", "QUAD-H002")]
    public void FocusedButtonDoesNotStealSpaceWhenViewportHovered()
    {
        var (window, view, vm) = OpenMainWindow();
        try
        {
            var s = vm.Session;
            s.SetLayout(ViewLayout.Quad);
            Dispatcher.UIThread.RunJobs();

            int clicks = 0;
            var btn = view.GetVisualDescendants().OfType<Button>().First(b => b.IsVisible && b.Focusable);
            btn.Click += (_, _) =>
            {
                clicks++;
                s.SetLayout(ViewLayout.Quad);
            };
            btn.Focus();
            Dispatcher.UIThread.RunJobs();

            var front = view.FindControl<Grid>("Cell1")!;
            window.MouseMove(CenterInWindow(front, window));
            Dispatcher.UIThread.RunJobs();

            // Press, hold (repeat KeyDown), and release Space: focused button must not click on KeyUp or revert maximize
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, s.MaximizedView);

            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, s.MaximizedView);

            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, s.MaximizedView);
            Assert.Equal(0, clicks);

            // Non-animation commands (Command scope active, AnimationEditor == null) also allow Space maximize/restore
            s.StartCommand(() => new ExtrudeCommand());
            Dispatcher.UIThread.RunJobs();
            btn.Focus();
            Dispatcher.UIThread.RunJobs();
            TapSpace(window);
            Assert.Null(s.MaximizedView);
            Assert.Equal(0, clicks);
            s.CancelCommand();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact(DisplayName = "QUAD-H003 | Animation timeline keeps Space for Play/Pause, and TextBox / SketchTextTool keep Space for typing")]
    [Trait("TestId", "QUAD-H003")]
    public void AnimationTimelineAndTypingKeepSpace()
    {
        var (window, view, vm) = OpenMainWindow();
        try
        {
            var s = vm.Session;
            s.SetLayout(ViewLayout.Quad);
            s.AddAnimation(new Prism.Core.Assembly.AnimInfo(5, [new Prism.Core.Assembly.AnimTrack(AnimationCommand.ExplodeKey, false, [new Prism.Core.Assembly.AnimKey(0, 0), new Prism.Core.Assembly.AnimKey(2, 1)])], false));
            Dispatcher.UIThread.RunJobs();

            var front = view.FindControl<Grid>("Cell1")!;
            window.MouseMove(CenterInWindow(front, window));
            Dispatcher.UIThread.RunJobs();

            // 1. Animation timeline open: Space plays/pauses instead of maximizing
            vm.OpenAnimationTimeline();
            Dispatcher.UIThread.RunJobs();
            var editor = Assert.IsType<AnimationCommand>(s.ActiveCommand);
            Assert.False(editor.IsPlaying);

            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsPlaying);
            Assert.Null(s.MaximizedView);
            s.CancelCommand();
            Dispatcher.UIThread.RunJobs();

            // 2. TextBox focused while pointer hovers a quad pane: Space does not maximize
            var search = view.FindControl<TextBox>("SearchBox")!;
            search.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(s.MaximizedView);

            // 3. SketchTextTool editing: Space does not maximize
            window.Focus();
            s.EnterSketch(Plane3.XY);
            var textTool = new SketchTextTool();
            s.Sketch!.SetTool(s, textTool);
            // Put SketchTextTool into Editing state via pointer click on the sketch plane
            s.PointerDown(new PointerInfo(front.Bounds.Width * 0.5, front.Bounds.Height * 0.5, true, false, false, false));
            s.PointerUp(new PointerInfo(front.Bounds.Width * 0.5, front.Bounds.Height * 0.5, false, false, false, false));
            Assert.True(textTool.Editing);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(s.MaximizedView);
            s.ExitSketch(false);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact(DisplayName = "QUAD-H004 | Space outside Four Views or outside the viewport does nothing, and toggling preserves pan and zoom")]
    [Trait("TestId", "QUAD-H004")]
    public void OutsideQuadOrOutsideViewportDoesNothingAndPreservesCamera()
    {
        var (window, view, vm) = OpenMainWindow();
        try
        {
            var s = vm.Session;
            CreateBoxBody(s, Vec3.Zero, 10, 10, 10);

            // Single / Dual layouts ignore Space maximize
            s.SetLayout(ViewLayout.Dual);
            Dispatcher.UIThread.RunJobs();
            var cell0 = view.FindControl<Grid>("Cell0")!;
            window.MouseMove(CenterInWindow(cell0, window));
            TapSpace(window);
            Assert.Null(s.MaximizedView);

            // Quad layout: pointer outside ViewGrid (e.g. over Tree) does nothing
            s.SetLayout(ViewLayout.Quad);
            Dispatcher.UIThread.RunJobs();
            var tree = view.FindControl<TreeView>("Tree")!;
            window.MouseMove(CenterInWindow(tree, window));
            TapSpace(window);
            Assert.Null(s.MaximizedView);

            // Pan/zoom in Quad, then maximize + restore preserves Target and Distance
            var rightCell = view.FindControl<Grid>("Cell2")!;
            s.Cameras[2].Target = new Vec3(42, -13, 7);
            s.Cameras[2].Distance = 333;
            s.SyncViews(s.Cameras[2]);

            window.MouseMove(CenterInWindow(rightCell, window));
            TapSpace(window);
            Assert.Equal(2, s.MaximizedView);
            Assert.Equal(new Vec3(42, -13, 7), s.Cameras[2].Target);
            Assert.Equal(333, s.Cameras[2].Distance);

            TapSpace(window);
            Assert.Null(s.MaximizedView);
            Assert.Equal(new Vec3(42, -13, 7), s.Cameras[2].Target);
            Assert.Equal(333, s.Cameras[2].Distance);

            // Explicitly switching layout resets MaximizedView
            TapSpace(window);
            Assert.Equal(2, s.MaximizedView);
            s.SetLayout(ViewLayout.Quad);
            Assert.Null(s.MaximizedView);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact(DisplayName = "QUAD-H005 | Four Views maximize shortcut appears in Settings Keyboard preferences and respects rebinding")]
    [Trait("TestId", "QUAD-H005")]
    public void ShortcutAppearsInSettingsAndRespectsRebinding()
    {
        bool hadOverride = AppSettings.Current.Hotkeys.TryGetValue("view.quad.maximize", out var prevOverride);
        var (window, view, vm) = OpenMainWindow();
        SettingsWindow? settings = null;
        try
        {
            var s = vm.Session;
            s.SetLayout(ViewLayout.Quad);
            Dispatcher.UIThread.RunJobs();

            settings = new SettingsWindow(vm);
            settings.Show();
            Dispatcher.UIThread.RunJobs();

            settings.FindControl<ListBox>("Nav")!.SelectedIndex = 6; // Keyboard
            Dispatcher.UIThread.RunJobs();

            var keyboardHost = settings.FindControl<StackPanel>("KeyboardHost")!;
            var searchBox = keyboardHost.Children.OfType<TextBox>().First();
            searchBox.Text = "Four Views";
            Dispatcher.UIThread.RunJobs();

            static Button? FindKeyButton(StackPanel host)
            {
                IEnumerable<TreeViewItem> Walk(ItemCollection items)
                {
                    foreach (var tvi in items.OfType<TreeViewItem>())
                    {
                        yield return tvi;
                        foreach (var child in Walk(tvi.Items)) yield return child;
                    }
                }
                var tree = host.Children.OfType<TreeView>().First();
                return Walk(tree.Items)
                    .Select(i => i.Header as Control)
                    .Where(h => h != null)
                    .SelectMany(h => h!.GetLogicalDescendants().OfType<Button>())
                    .FirstOrDefault(b => Equals(b.Tag, "view.quad.maximize"));
            }

            var keyButton = FindKeyButton(keyboardHost);
            Assert.NotNull(keyButton);
            Assert.Equal("Space", keyButton!.Content);

            // Rebind to F12 via the Settings UI capture flow
            keyButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            settings.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);
            settings.KeyRelease(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("F12", Hotkeys.Effective(Hotkeys.Find("view.quad.maximize")!).Gesture);
            var reboundButton = FindKeyButton(keyboardHost);
            Assert.NotNull(reboundButton);
            Assert.Equal("F12", reboundButton!.Content);

            var front = view.FindControl<Grid>("Cell1")!;
            window.MouseMove(CenterInWindow(front, window));
            Dispatcher.UIThread.RunJobs();

            // Old Space binding no longer toggles, while new F12 binding does
            TapSpace(window);
            Assert.Null(s.MaximizedView);

            window.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);
            Dispatcher.UIThread.RunJobs();
            window.KeyRelease(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, s.MaximizedView);
        }
        finally
        {
            settings?.Close();
            window.Close();
            if (hadOverride && prevOverride != null) AppSettings.Current.Hotkeys["view.quad.maximize"] = prevOverride;
            else Hotkeys.Reset("view.quad.maximize");
            AppSettings.Save();
        }
    }
}
