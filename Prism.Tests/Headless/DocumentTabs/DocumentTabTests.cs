using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.DocumentTabs;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/document-tabs/README.md">Document tabs guide.</seealso>
[Trait("Feature", "DocumentTabs")]
[Trait("Layer", "Headless")]
public class DocumentTabTests
{
    [AvaloniaFact(DisplayName = "TAB-H003 | Close All But This preserves the target and reviews other pinned documents")]
    [Trait("TestId", "TAB-H003")]
    public async Task CloseAllButThisKeepsTargetAndReviewsPinned()
    {
        var vm = new MainViewModel(); var target = vm.ActiveDocument!;
        target.Session.AddObject(Occ.Box(1, 1, 1), "Target", ObjKind.Solid);
        var pinned = vm.NewDocumentTab(); pinned.IsPinned = true;
        pinned.Session.AddObject(Occ.Box(1, 1, 1), "Other", ObjKind.Solid);
        var asked = new List<DocumentTab>();
        Assert.False(await vm.CloseAllTabsAsync(true, tab => { asked.Add(tab); return Task.FromResult(DocumentCloseDecision.Cancel); }, target));
        Assert.Equal(new[] { pinned }, asked); Assert.Contains(target, vm.Documents); Assert.Contains(pinned, vm.Documents);
        Assert.True(await vm.CloseAllTabsAsync(true, _ => Task.FromResult(DocumentCloseDecision.Discard), target));
        Assert.Single(vm.Documents); Assert.Same(target, vm.Documents[0]); Assert.True(target.Session.Dirty);
    }
    [AvaloniaFact(DisplayName = "TAB-H002 | Close All saves dirty tabs and retains them when Save As is canceled")]
    [Trait("TestId", "TAB-H002")]
    public async Task CloseAllSavesOrStopsWhenSaveAsCanceled()
    {
        var vm = new MainViewModel(); var tab = vm.ActiveDocument!;
        tab.Session.AddObject(Occ.Box(1, 1, 1), "Dirty", ObjKind.Solid);
        vm.PickSaveFile = (_, _, _) => Task.FromResult<string?>(null);
        Assert.False(await vm.CloseAllTabsAsync(true, _ => Task.FromResult(DocumentCloseDecision.Save)));
        Assert.Contains(tab, vm.Documents); Assert.True(tab.Session.Dirty);
        string path = Path.Combine(Path.GetTempPath(), $"prism-tab-{Guid.NewGuid():N}.prism");
        try
        {
            vm.PickSaveFile = (_, _, _) => Task.FromResult<string?>(path);
            Assert.True(await vm.CloseAllTabsAsync(true, _ => Task.FromResult(DocumentCloseDecision.Save)));
            Assert.True(File.Exists(path)); Assert.DoesNotContain(tab, vm.Documents);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [AvaloniaFact(DisplayName = "TAB-H001 | Close All skips pinned tabs and stops at Cancel")]
    [Trait("TestId", "TAB-H001")]
    public async Task CloseAllSkipsPinnedAndHonorsCancel()
    {
        var vm = new MainViewModel();
        var pinned = vm.ActiveDocument!; pinned.IsPinned = true;
        pinned.Session.AddObject(Occ.Box(1, 1, 1), "Pinned", ObjKind.Solid);
        var dirty = vm.NewDocumentTab(); dirty.Session.AddObject(Occ.Box(1, 1, 1), "Dirty", ObjKind.Solid);
        var asked = new List<DocumentTab>();
        Assert.False(await vm.CloseAllTabsAsync(false, t => { asked.Add(t); return Task.FromResult(DocumentCloseDecision.Cancel); }));
        Assert.Equal(new[] { dirty }, asked); Assert.Contains(pinned, vm.Documents); Assert.Contains(dirty, vm.Documents);
        Assert.True(await vm.CloseAllTabsAsync(false, _ => Task.FromResult(DocumentCloseDecision.Discard)));
        Assert.Single(vm.Documents); Assert.Same(pinned, vm.Documents[0]);
        asked.Clear();
        Assert.False(await vm.CloseAllTabsAsync(true, t => { asked.Add(t); return Task.FromResult(DocumentCloseDecision.Cancel); }));
        Assert.Equal(new[] { pinned }, asked); Assert.Same(pinned, vm.Documents[0]);
        Assert.True(await vm.CloseAllTabsAsync(true, _ => Task.FromResult(DocumentCloseDecision.Discard)));
        Assert.DoesNotContain(pinned, vm.Documents); Assert.Single(vm.Documents);
    }
}
