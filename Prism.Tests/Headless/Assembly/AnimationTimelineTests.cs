using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Prism.Commands;
using Prism.Controls;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Assembly;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/assembly/README.md">Animation workflow.</seealso>
[Trait("Feature", "Assembly")]
[Trait("Layer", "Headless")]
public class AnimationTimelineTests
{
    [AvaloniaFact(DisplayName = "ASM-H007 | Key editing keeps selection, preserves occupied times and applies exact input")]
    [Trait("TestId", "ASM-H007")]
    public void TimelineKeysStaySelectedThroughRetimeAndExactEdits()
    {
        var session = new CadSession();
        session.AddAnimation(new AnimInfo(5, [new AnimTrack(AnimationCommand.ExplodeKey, false,
            [new AnimKey(0, 0), new AnimKey(1, .5), new AnimKey(3, 1)])], false));
        session.StartCommand(() => new AnimationCommand());
        var editor = Assert.IsType<AnimationCommand>(session.ActiveCommand);
        var track = Assert.Single(editor.Rows()).Track!;
        var timeline = new AnimationTimelineView { Command = editor, Width = 600 };
        var window = new Window { Width = 600, Height = 120, Content = timeline };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Point KeyPoint(double time) => timeline.TranslatePoint(new Point(182 + time / 5 * 404, 62), window)!.Value;
            window.MouseDown(KeyPoint(1), MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(KeyPoint(1.25), RawInputModifiers.LeftMouseButton);
            window.MouseMove(KeyPoint(1.35), RawInputModifiers.LeftMouseButton);
            window.MouseUp(KeyPoint(1.35), MouseButton.Left, RawInputModifiers.None);
            Assert.Same(track.Keys[1], editor.SelectedKey);
            Assert.Equal(1.35, editor.SelectedKey!.T);
            double expandedHeight = timeline.DesiredSize.Height;
            var heading = timeline.TranslatePoint(new Point(20, 36), window)!.Value;
            window.MouseDown(heading, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(heading, MouseButton.Left, RawInputModifiers.None);
            timeline.Measure(new Size(600, 120));
            Assert.True(timeline.DesiredSize.Height < expandedHeight);
            timeline.ZoomBy(2, 600);
            Assert.Equal(1200, timeline.Width);
        }
        finally
        {
            window.Close();
        }
        Assert.Same(track.Keys[1], editor.SelectedKey);
        editor.MoveKey(track, editor.SelectedKey!, 3);
        Assert.Equal(new[] { 0, 1.35, 3 }, track.Keys.Select(k => k.T));
        var inspector = editor.WorkflowSections.Single(section => section.Title == "Selected key");
        Assert.True(inspector.IsVisible);
        inspector.Items.OfType<NumberParam>().Single(p => p.Label == "Key time").Value = 2.125;
        inspector.Items.OfType<NumberParam>().Single(p => p.Label == "Key value").Value = .625;
        Assert.Single(inspector.Items.OfType<ActionParam>()).Run();
        Assert.Equal(new AnimKey(2.125, .625), editor.SelectedKey);
        Assert.Equal(2.125, editor.Time);
        session.CommitCommand();
        var saved = Assert.IsType<AnimInfo>(Assert.Single(session.Objects).Asm);
        Assert.Equal(new AnimKey(2.125, .625), Assert.Single(saved.Tracks).Keys[1]);
    }

    [AvaloniaFact(DisplayName = "ASM-H008 | New Animation opens the editor and the parameter panel switches units and inputs")]
    [Trait("TestId", "ASM-H008")]
    public void NewAnimationOpensWithDynamicParameterSettings()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        Assert.True(session.ApplyDocumentParameters([new("Length", "20 mm"), new("Turn", "45 deg")]));
        session.AddAnimation(new AnimInfo(7, [], false));
        var animate = vm.Tabs.Single(t => t.Title == "Animate");
        animate.Buttons.Single(b => b.Label == "New\nAnimation").Invoke();
        var editor = Assert.IsType<AnimationCommand>(session.ActiveCommand);
        Assert.Same(editor, vm.AnimationEditor);
        Assert.Equal(5, editor.Duration);
        var panel = editor.WorkflowSections.Single(s => s.Title == "Document parameter");
        Assert.Contains(panel.Items.OfType<NumberParam>(), p => p.Label == "Length  (parameter)");
        Assert.DoesNotContain(panel.Items.OfType<NumberParam>(), p => p.Label == "Turn  (parameter)");
        Assert.Single(panel.Items.OfType<ChoiceParam>()).Index = 1;
        Assert.Contains(panel.Items.OfType<NumberParam>(), p => p.Label == "Turn  (parameter)" && p.Unit == "°");
        Assert.Contains(panel.Items.OfType<NumberParam>(), p => p.DisplayLabel == "From (°)");
        Assert.DoesNotContain(panel.Items.OfType<NumberParam>(), p => p.Label == "Length  (parameter)");
        session.CancelCommand();
        Assert.Equal(7, Assert.IsType<AnimInfo>(Assert.Single(session.Objects).Asm).Duration);
    }
}
