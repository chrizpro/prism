using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Commands;
using Prism.Controls;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.ViewModels;
using Prism.Views;
using Prism.Kernel;
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

    [AvaloniaFact(DisplayName = "ASM-H009 | The parameter table opens a track and dependent parameters retain their formulas")]
    [Trait("TestId", "ASM-H009")]
    public void ParameterTableOpensNumericAnimationTrack()
    {
        var vm = new MainViewModel();
        Assert.True(vm.Session.ApplyDocumentParameters([new("Height", "20 mm"), new("Width", "Height * 2")]));
        vm.OpenStudio("parameters");
        vm.SelectedParameterDraft = vm.DocumentParameterRows.Single(p => p.Name == "Width");
        vm.AnimateSelectedParameter();
        Assert.Null(vm.Session.ActiveCommand);
        Assert.Contains("formula", vm.StudioMessage);
        vm.SelectedParameterDraft = vm.DocumentParameterRows.Single(p => p.Name == "Height");
        vm.AnimateSelectedParameter();
        var editor = Assert.IsType<AnimationCommand>(vm.Session.ActiveCommand);
        Assert.Equal(CadSession.ParamKey("Height"), editor.SelectedTrack!.JointKey);
        Assert.Equal(20, editor.SelectedKey!.V);
        Assert.Equal("Height * 2", vm.Session.DocumentParameters.Single(p => p.Name == "Width").Formula);
    }

    [AvaloniaFact(DisplayName = "ASM-H010 | Animation retains five shaft turns and per-segment timing through save and reopen")]
    [Trait("TestId", "ASM-H010")]
    public void MultipleTurnsAndTimingRoundtrip()
    {
        var session = new CadSession();
        var first = session.MakePart(session.AddObject(Occ.Box(2, 3, 4), "Base", ObjKind.Solid).Id);
        var second = session.MakePart(session.AddObject(Occ.Cylinder(1, 4), "Shaft", ObjKind.Solid).Id);
        var frame = JointInfo.Pack(Xform.Identity);
        var joint = new JointInfo("shaft", "Shaft", JointType.Revolute, first.Component!.Key, second.Component!.Key,
            frame, frame, false, 0, 0, 0, false, 0, 0, 0, 0);
        var marker = session.AddObject(Occ.Line(Vec3.Zero, Vec3.UnitZ), "Hinge", ObjKind.Joint);
        session.Update(marker.Id, o => o.WithAsm(joint), false);
        session.StartCommand(() => new AnimationCommand(true));
        var editor = Assert.IsType<AnimationCommand>(session.ActiveCommand);
        editor.Time = 5;
        editor.Params.OfType<NumberParam>().Single(p => p.Label == "Shaft  (angle)").Value = 1800;
        editor.KeyChangedValues();
        var track = editor.Rows().Single(r => r.JointKey == "shaft").Track!;
        Assert.Equal(1800, track.Keys.Last().V);
        editor.SelectKey(track, track.Keys[0]);
        editor.Params.OfType<ChoiceParam>().Single(p => p.Label == "Timing to next key").Index = (int)AnimTiming.Linear;
        editor.Params.OfType<ActionParam>().Single(p => p.Label == "Apply key").Run();
        editor.Time = 1.25;
        Assert.Equal(450, editor.Params.OfType<NumberParam>().Single(p => p.Label == "Shaft  (angle)").Value);
        editor.SelectKey(track, track.Keys[0]);
        editor.Params.OfType<ChoiceParam>().Single(p => p.Label == "Timing to next key").Index = (int)AnimTiming.Custom;
        var curve = Assert.Single(editor.Params.OfType<TimingCurveParam>());
        Assert.True(editor.Params.OfType<ActionParam>().Single(p => p.Label == "Open Curve Editor").IsVisible);
        curve.Load(new AnimCurve(1.0 / 3, 0, 2.0 / 3, 0));
        editor.Params.OfType<ActionParam>().Single(p => p.Label == "Apply key").Run();
        editor.Time = 1.25;
        Assert.Equal(28.125, editor.Params.OfType<NumberParam>().Single(p => p.Label == "Shaft  (angle)").Value, 6);
        session.CommitCommand();
        var file = Path.Combine(Path.GetTempPath(), $"prism-animation-{Guid.NewGuid():N}.prism_asm");
        try
        {
            session.Save(file);
            var reopened = new CadSession();
            reopened.Open(file);
            var animation = Assert.Single(reopened.Objects.Select(o => o.Asm).OfType<AnimInfo>());
            Assert.Equal(1800, Assert.Single(animation.Tracks).Keys.Last().V);
            Assert.Equal(AnimTiming.Custom, animation.Tracks[0].Keys[0].Timing);
            Assert.Equal(curve.Curve, animation.Tracks[0].Keys[0].Curve);
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact(DisplayName = "ASM-H011 | Custom curve handles can be dragged with the pointer")]
    [Trait("TestId", "ASM-H011")]
    public void CustomCurveHandleUsesPointerInput()
    {
        var curve = new TimingCurveParam();
        var view = new TimingCurveView { DataContext = curve, Width = 280 };
        var window = new Window { Width = 280, Height = 150, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Point At(double x, double y) => view.TranslatePoint(new Point(16 + x * 248, 134 - y * 118), window)!.Value;
            window.MouseDown(At(.25, .1), MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(At(.75, .25), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(.75, .25), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(.75, curve.X1, 5);
            Assert.Equal(.25, curve.Y1, 5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact(DisplayName = "ASM-H012 | The curve editor opens as a separate reusable panel and releases its target when editing ends")]
    [Trait("TestId", "ASM-H012")]
    public void CurveEditorPanelFollowsTheSelectedAnimation()
    {
        var vm = new MainViewModel();
        Assert.True(vm.Session.ApplyDocumentParameters([new("Height", "20 mm")]));
        vm.Session.StartCommand(() => new AnimationCommand(true));
        var editor = Assert.IsType<AnimationCommand>(vm.Session.ActiveCommand);
        editor.FocusParameter("Height");
        var row = editor.Rows().Single(r => r.JointKey == CadSession.ParamKey("Height"));
        editor.AddKey(row, 2);
        var track = editor.Rows().Single(r => r.JointKey == row.JointKey).Track!;
        editor.SelectKey(track, track.Keys[0]);
        editor.Params.OfType<ChoiceParam>().Single(p => p.Label == "Timing to next key").Index = (int)AnimTiming.Custom;
        editor.Params.OfType<ActionParam>().Single(p => p.Label == "Open Curve Editor").Run();
        Assert.True(vm.ShowCurveEditor);
        Assert.Same(editor.CustomCurve, vm.CurveEditor!.Curve);
        var panel = new CurveEditorPanel { DataContext = vm };
        var window = new Window { Width = 380, Height = 500, Content = panel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(Assert.Single(panel.GetVisualDescendants().OfType<TimingCurveEditor>()).IsEffectivelyVisible);
        }
        finally { window.Close(); }
        vm.CurveEditor.Curve.Load(new AnimCurve(.3, .1, .8, .9));
        vm.ApplyCurveEditor();
        Assert.Equal(new AnimCurve(.3, .1, .8, .9), track.Keys[0].Curve);
        vm.Session.CancelCommand();
        Assert.Null(vm.CurveEditor);
        Assert.False(vm.ShowCurveEditor);
    }
}
