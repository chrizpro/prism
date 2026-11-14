using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Assembly;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/motion-analysis.md">Motion workflow.</seealso>
[Trait("Feature", "Motion")]
[Trait("Layer", "Headless")]
public class MotionWorkflowTests
{
    static MainViewModel Mechanism()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        SceneObject Part(string name, double offset)
        {
            var body = session.AddObject(Occ.Translate(Occ.Box(2, 3, 4), new(offset, 0, 0)), name, ObjKind.Solid);
            session.Update(body.Id, o => o.WithMaterial(Materials.Find("6061")!), false);
            return session.MakePart(body.Id);
        }
        var ground = Part("Base", 0);
        for (int i = 0; i < 2; i++)
        {
            var child = Part("Arm " + i, 10 + i * 10);
            var frame = JointInfo.Pack(Xform.Identity);
            var joint = new JointInfo("j" + i, "Hinge " + i, JointType.Revolute,
                ground.Component!.Key, child.Component!.Key, frame, frame, false, 0, 0, 0, false, 0, 0, 0, 0);
            var marker = session.AddObject(Occ.Line(Vec3.Zero, Vec3.UnitZ), joint.Name, ObjKind.Joint);
            session.Update(marker.Id, o => o.WithAsm(joint), false);
        }
        session.StartCommand(() => new MotionStudyCommand(MotionKind.Kinematics));
        return vm;
    }
    static T Input<T>(MotionStudyCommand command, string label, string? section = null) where T : Param =>
        command.Params.OfType<T>().Single(p => p.Label == label && (section == null || p.Section == section));

    static async Task Finished(MotionStudyCommand command)
    {
        for (int i = 0; i < 1000 && Input<ActionParam>(command, "Stop study").IsVisible; i++) await Task.Delay(10);
        Assert.False(Input<ActionParam>(command, "Stop study").IsVisible);
    }

    [AvaloniaFact(DisplayName = "MOT-H001 | Switching joint controls preserves every drive and does not invalidate solved results")]
    [Trait("TestId", "MOT-H001")]
    public async Task DriveSelectionPreservesBothJointInputs()
    {
        var vm = Mechanism();
        var command = Assert.IsType<MotionStudyCommand>(vm.Session.ActiveCommand);
        Input<NumberParam>(command, "Duration").Value = .2;
        Input<NumberParam>(command, "Output interval").Value = .05;
        Input<ChoiceParam>(command, "Hinge 0 — motion").Index = 1;
        Assert.Equal(new[] { "Setup", "Drives", "Hinge 0", "Solve", "Results" },
            command.WorkflowSections.Where(s => s.IsVisible).Select(s => s.Title));
        Input<NumberParam>(command, "Speed", "Hinge 0").Value = 30;
        Input<ChoiceParam>(command, "Joint drive").Index = 1;
        Assert.False(Input<ChoiceParam>(command, "Hinge 0 — motion").IsVisible);
        Assert.True(Input<ChoiceParam>(command, "Hinge 1 — motion").IsVisible);
        Input<ChoiceParam>(command, "Hinge 1 — motion").Index = 1;
        Input<NumberParam>(command, "Speed", "Hinge 1").Value = 60;
        Input<ActionParam>(command, "Run study").Run();
        await Finished(command);
        Assert.NotNull(command.Result);
        Assert.Equal(6, command.Result.Pos[0][^1], 6);
        Assert.Equal(12, command.Result.Pos[1][^1], 6);
        Input<ChoiceParam>(command, "Joint drive").Index = 0;
        Assert.DoesNotContain(command.Results, r => r.Label == "Settings changed");
        Input<NumberParam>(command, "Show the motion at").Value = .1;
        Assert.Contains(command.Results, r => r.Label == "Showing" && r.Value.StartsWith("0.1 s"));
        vm.Session.CancelCommand();
    }

    [AvaloniaFact(DisplayName = "MOT-H002 | Hidden free-joint loads stay active and Stop cancels an unfinished study")]
    [Trait("TestId", "MOT-H002")]
    public async Task HiddenPhysicalInputsAndCancellation()
    {
        var vm = Mechanism();
        var command = Assert.IsType<MotionStudyCommand>(vm.Session.ActiveCommand);
        Input<ChoiceParam>(command, "Gravity").Index = 0;
        Input<NumberParam>(command, "Duration").Value = .02;
        Input<NumberParam>(command, "Output interval").Value = .01;
        Input<ChoiceParam>(command, "Hinge 0 — motion").Index = 3;
        Assert.Equal(1, Input<ChoiceParam>(command, "Study mode").Index);
        Input<NumberParam>(command, "Applied torque", "Hinge 0").Value = 1e-7;
        Input<NumberParam>(command, "Viscous friction (N·m·s/rad)", "Hinge 0").Value = 1e-9;
        Input<ChoiceParam>(command, "Joint drive").Index = 1;
        Input<ActionParam>(command, "Run study").Run();
        await Finished(command);
        Assert.NotNull(command.Result);
        Assert.Equal(1e-7, command.Result.Dofs[0].Effort);
        Assert.Equal(1e-9, command.Result.Dofs[0].Damping);
        Input<NumberParam>(command, "Duration").Value = 10;
        Input<ActionParam>(command, "Run study").Run();
        Assert.Contains(vm.Session.Work.Items, work => work.Title == "Motion study");
        Input<ActionParam>(command, "Stop study").Run();
        await Finished(command);
        Assert.Null(command.Result);
        Assert.False(vm.Session.Work.HasTasks);
        Assert.Contains(command.Results, r => r.Label == "Status" && r.Value.Contains("stopped"));
        vm.Session.CancelCommand();
    }

    [AvaloniaFact(DisplayName = "MOT-H003 | Move to Position uses the shared curve and reports its sampled motion")]
    [Trait("TestId", "MOT-H003")]
    public async Task CustomMoveUsesSharedEditor()
    {
        var vm = Mechanism();
        var command = Assert.IsType<MotionStudyCommand>(vm.Session.ActiveCommand);
        Input<NumberParam>(command, "Duration").Value = 1;
        Input<NumberParam>(command, "Output interval").Value = .25;
        Input<ChoiceParam>(command, "Hinge 0 — motion").Index = 4;
        Input<NumberParam>(command, "Move to", "Hinge 0").Value = 80;
        Input<ChoiceParam>(command, "Move timing", "Hinge 0").Index = 1;
        Input<ActionParam>(command, "Open Curve Editor", "Hinge 0").Run();
        Assert.True(vm.ShowCurveEditor);
        vm.CurveEditor!.Curve.Load(new AnimCurve(1.0 / 3, 0, 2.0 / 3, 0));
        vm.ApplyCurveEditor();
        Input<ActionParam>(command, "Run study").Run();
        await Finished(command);
        Assert.NotNull(command.Result);
        Assert.Equal(10, command.Result.Pos[0][2], 5);
        Assert.Equal(60, command.Result.Vel[0][2], 5);
        Assert.Equal(240, command.Result.Acc[0][2], 4);
        vm.CurveEditor!.Curve.Load(new AnimCurve(0, 0, .7, 1));
        vm.ApplyCurveEditor();
        Input<ActionParam>(command, "Run study").Run();
        await Finished(command);
        Assert.Null(command.Result);
        Assert.Contains(command.Results, r => r.Label == "Could not run" && r.Value.Contains("handle time"));
        vm.Session.CancelCommand();
        Assert.Null(vm.CurveEditor);
    }

    [AvaloniaFact(DisplayName = "MOT-H004 | Editing the setup during a study prevents publication of stale results")]
    [Trait("TestId", "MOT-H004")]
    public async Task SetupChangeDiscardsUnfinishedResult()
    {
        var vm = Mechanism();
        var command = Assert.IsType<MotionStudyCommand>(vm.Session.ActiveCommand);
        Input<NumberParam>(command, "Duration").Value = 100;
        Input<NumberParam>(command, "Output interval").Value = .005;
        Input<ChoiceParam>(command, "Hinge 0 — motion").Index = 1;
        Input<ActionParam>(command, "Run study").Run();
        Input<NumberParam>(command, "Speed", "Hinge 0").Value = 45;
        await Finished(command);
        Assert.Null(command.Result);
        Assert.Contains(command.Results, r => r.Label == "Status" && r.Value.Contains("changed"));
        vm.Session.CancelCommand();
    }
}
