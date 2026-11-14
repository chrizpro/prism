using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.Rendering;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Interface;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/operation-progress.md">Usability and progress.</seealso>
[Trait("Feature", "Interface")]
[Trait("Layer", "Headless")]
public class UsabilityWorkflowTests
{
    [AvaloniaFact(DisplayName = "UX-H001 | Finishing a pending part feature inserts it once and returns to Assemble")]
    [Trait("TestId", "UX-H001")]
    public void FinishPartReturnsToAssembly()
    {
        var vm = new MainViewModel(); vm.SetWorkspace("all");
        var assembly = vm.ActiveDocument;
        vm.CreatePartInAssembly("Pin", 0);
        var part = vm.ActiveDocument;
        Assert.True(vm.ShowEditBanner);
        vm.BackToAssembly(); // Empty parts must not lose their editing context.
        Assert.Same(part, vm.ActiveDocument); Assert.NotNull(vm.EditingPart);
        vm.Session.StartCommand(() => new BoxCommand());
        vm.BackToAssembly();
        Assert.Same(assembly, vm.ActiveDocument);
        Assert.Equal("assemble", vm.WorkspaceId); Assert.Equal("Assemble", vm.SelectedTab!.Title);
        Assert.Null(vm.EditingPart); Assert.Single(vm.Session.Components);
        vm.BackToAssembly(); Assert.Single(vm.Session.Components);
    }

    [AvaloniaFact(DisplayName = "UX-H006 | New Study brings the Simulation panel forward even when Properties was selected")]
    [Trait("TestId", "UX-H006")]
    public void NewStudySelectsItsPanel()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var bar = CreateBoxBody(s, Vec3.Zero, 10, 10, 100);
        vm.SetWorkspace("simulate");
        vm.SelectedRightPanel = "properties";
        s.SelectItems([SelItem.Of(bar)]);
        vm.NewStudy();
        Assert.NotNull(s.ActiveStudy);
        Assert.Equal("context", vm.SelectedRightPanel);
        Assert.True(vm.ShowSimPanel);
    }

    [AvaloniaFact(DisplayName = "UX-H002 | Browser connection badges follow actual grounding and persistent joints")]
    [Trait("TestId", "UX-H002")]
    public void ComponentStatusTracksConnections()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var first = s.MakePart(CreateBoxBody(s, Vec3.Zero, 2, 3, 4).Id);
        var second = s.MakePart(CreateBoxBody(s, new(10, 0, 0), 2, 3, 4).Id);
        var third = s.MakePart(CreateBoxBody(s, new(20, 0, 0), 2, 3, 4).Id);
        string Badge(SceneObject body) => vm.AllBrowserNodes().Single(n => n.IsObject && n.ObjectId == body.Id).Badge;
        Assert.Equal("Grounded", Badge(first)); Assert.Equal("Free", Badge(second));
        var frame = JointInfo.Pack(Xform.Identity);
        var joint = new JointInfo("test", "Connection", JointType.Rigid, first.Component!.Key, second.Component!.Key, frame, frame, false, 0, 0, 0, false, 0, 0, 0, 0);
        var marker = s.AddObject(Occ.Line(Vec3.Zero, Vec3.UnitZ), "Connection", ObjKind.Joint);
        s.Update(marker.Id, o => o.WithAsm(joint));
        Assert.Equal("Grounded · Jointed", Badge(first)); Assert.Equal("Jointed", Badge(second)); Assert.Equal("Free", Badge(third));
        var output = new CommandOutput(); output.Remove.Add(s.Find(marker.Id)!); s.Apply(output);
        Assert.Equal("Grounded", Badge(first)); Assert.Equal("Free", Badge(second));
    }

    [AvaloniaFact(DisplayName = "UX-H003 | Workspace/ribbon workflows expose modeling and relocate Tooling without losing inspection commands")]
    [Trait("TestId", "UX-H003")]
    public void WorkspacesAndRibbonsFollowTasks()
    {
        var vm = new MainViewModel();
        vm.SetWorkspace("assemble");
        foreach (var title in new[] { "Solid", "Surface", "Freeform", "NURBS", "Direct Modeling", "Sheet Metal", "Joints" })
            Assert.True(vm.Tabs.Single(t => t.Title == title) is { IsVisible: true } or { IsContextual: true }, title);
        Assert.Equal(new[] { "Materials", "Lighting", "Camera", "Render", "Output", "Share" }, vm.Tabs.Single(t => t.Title == "Render").Groups.Select(g => g.Title));
        Assert.Equal("Look\nAt", Assert.Single(vm.Tabs.Single(t => t.Title == "Render").Groups.Single(g => g.Title == "Camera").Items.OfType<RibbonButton>()).Label);
        Assert.DoesNotContain(vm.Tabs, t => t.Title == "Tooling");
        var inspect = vm.Tabs.Single(t => t.Title == "Mold").Groups.Single(g => g.Title == "Inspect");
        Assert.Equal(new[] { "Draft analysis", "Section analysis", "Check geometry" }, inspect.Items.OfType<RibbonButton>().Select(b => b.Label));
        foreach (var workspace in MainViewModel.WorkspaceDefs)
            Assert.All(workspace.Tabs, title => Assert.Contains(vm.Tabs, t => t.Title == title));
    }

    [AvaloniaFact(DisplayName = "UX-H004 | Independent operation leases handle background updates, cooperative stop and late messages")]
    [Trait("TestId", "UX-H004")]
    public async Task ProgressLeasesDoNotOverwriteOtherJobs()
    {
        var tracker = new OperationTracker(); int cancellations = 0;
        using var first = tracker.Begin("First", () => cancellations++);
        using var second = tracker.Begin("Second");
        await Task.Run(() => first.Report("Half complete", .5)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, first.Percent); Assert.False(first.IsIndeterminate); Assert.True(second.IsIndeterminate); Assert.False(second.CanCancel);
        first.CancelCommand.Execute(null); first.CancelCommand.Execute(null);
        Assert.Equal(1, cancellations); Assert.False(first.CanCancel); Assert.Contains("Stopping", first.Status);
        first.Dispose();
        await Task.Run(() => first.Report("Late result", 1)); Dispatcher.UIThread.RunJobs();
        Assert.Single(tracker.Items); Assert.Same(second, tracker.Items[0]); Assert.Contains("Stopping", first.Status);
        second.Dispose(); Assert.False(tracker.HasTasks);
    }

    [AvaloniaFact(DisplayName = "UX-H005 | Image rendering uses the common operation strip and cooperative stop")]
    [Trait("TestId", "UX-H005")]
    public async Task RenderProgressCanStopTheActualJob()
    {
        var session = new CadSession(); CreateBoxBody(session, Vec3.Zero, 2, 3, 4);
        var job = session.StartRender(new RenderSettings { Width = 64, Height = 64, Samples = 2048 });
        var progress = Assert.Single(session.Work.Items); Assert.Equal("Render image", progress.Title);
        progress.CancelCommand.Execute(null);
        for (int i = 0; i < 500 && session.Work.HasTasks; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Assert.True(job.Cts.IsCancellationRequested); Assert.False(session.Work.HasTasks); Assert.False(job.Running);
    }
}
