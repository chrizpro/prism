using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.ViewModels;
using Prism.Views;
using Xunit;
using System.Net;
using System.Text;
using Prism.Kernel;

namespace Prism.HeadlessTests.Gallery;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/gallery/README.md">Startup gallery specification.</seealso>
[Trait("Feature", "Gallery")]
[Trait("Layer", "Headless")]
public class GalleryViewTests
{
    [AvaloniaFact(DisplayName = "GAL-H001 | Startup view loads gallery bindings alongside workspace and file actions")]
    [Trait("TestId", "GAL-H001")]
    public async Task HomeViewLoadsAndWorkflowSearchFiltersCards()
    {
        var vm = new MainViewModel();
        var view = new HomeView { DataContext = vm };
        Assert.NotNull(view.Content);
        var entry = new GalleryEntry("pendant", "Pendant shade", "Revolved shade", "Surface", "Prism", "MIT", "0.1.0",
            "https://github.com/a/model", "https://github.com/a/image", 1, new string('a', 64));
        string root = Path.Combine(Path.GetTempPath(), "prism-gallery-view-" + Guid.NewGuid());
        using var http = new HttpClient(new CatalogTransport(new GalleryCatalog(1, [entry, entry with { Id = "chair", Name = "Lounge chair", Workflow = "Freeform" }]).ToJson()));
        vm.GallerySource = new GalleryStore(http, root);
        try
        {
            await vm.LoadGallery();
            Assert.Equal(2, vm.GalleryCards.Count);
            vm.GallerySearch = "surface";
            var card = Assert.Single(vm.GalleryCards);
            Assert.Equal("Pendant shade", card.Name);
            Assert.Equal("Download & open", card.ActionLabel);
            Assert.True(card.CanOpen);
            vm.GallerySearch = "no match";
            Assert.Empty(vm.GalleryCards);
            Assert.True(vm.GalleryEmpty);
            vm.GallerySearch = "";
            Assert.Equal(2, vm.GalleryCards.Count);
            Assert.True(vm.GalleryAvailable);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    sealed class CatalogTransport(string catalog) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("catalog.json")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(catalog, Encoding.UTF8) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    [AvaloniaFact(DisplayName = "GAL-H003 | Each gallery section shows its first three models until expanded; searching shows every match")]
    [Trait("TestId", "GAL-H003")]
    public async Task SectionsShowThreeUntilExpanded()
    {
        var vm = new MainViewModel();
        var template = new GalleryEntry("m0", "Model 0", "A model", "Solid", "Prism", "MIT", "0.1.0", "https://github.com/a/model", "https://github.com/a/image", 1, new string('a', 64));
        var items = Enumerable.Range(0, 5).Select(i => template with { Id = "solid-" + i, Name = "Solid " + i }).Append(template with { Id = "cam-0", Name = "Board", Workflow = "Solid / CAM" })
            .Append(template with { Id = "cam-1", Name = "Router", Workflow = "CAM" }).ToList();
        string root = Path.Combine(Path.GetTempPath(), "prism-gallery-sections-" + Guid.NewGuid());
        using var http = new HttpClient(new CatalogTransport(new GalleryCatalog(1, items).ToJson()));
        vm.GallerySource = new GalleryStore(http, root);
        try
        {
            await vm.LoadGallery();
            Assert.Equal(["Solid", "CAM"], vm.GalleryGroups.Select(g => g.Title));
            var solid = vm.GalleryGroups[0];
            Assert.Equal(3, solid.Visible.Count());
            Assert.True(solid.CanExpand);
            Assert.Equal("Show all 6", solid.ToggleText);
            Assert.False(vm.GalleryGroups[1].CanExpand);
            solid.Toggle();
            Assert.Equal(6, vm.GalleryGroups[0].Visible.Count());
            Assert.Equal("Show fewer", vm.GalleryGroups[0].ToggleText);
            vm.GallerySearch = "no such thing";
            Assert.Empty(vm.GalleryGroups);
            vm.GallerySearch = "solid";
            Assert.Equal(6, vm.GalleryGroups[0].Visible.Count());
            vm.GallerySearch = "";
            Assert.Equal(6, vm.GalleryGroups[0].Visible.Count());
            vm.GalleryGroups[0].Toggle();
            Assert.Equal(3, vm.GalleryGroups[0].Visible.Count());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact(DisplayName = "GAL-H004 | A saved file reopens on the ribbon tab it was last worked in")]
    [Trait("TestId", "GAL-H004")]
    public void FileReopensOnItsSavedTab()
    {
        var vm = new MainViewModel();
        var tabs = vm.Tabs.Where(t => !t.IsFile && !t.IsContextual).ToList();
        var work = tabs[^1];
        string path = Path.Combine(Path.GetTempPath(), "prism-tab-" + Guid.NewGuid().ToString("N") + ".prism");
        try
        {
            vm.SelectTab(work);
            vm.Session.AddObject(Occ.Box(10, 20, 30), "Part", ObjKind.Solid);
            vm.Session.Save(path);
            vm.SelectTab(tabs[0]);
            vm.OpenPath(path);
            Assert.Equal(work.Title, vm.SelectedTab?.Title);
            Assert.Equal(work.Title, vm.Session.OpeningTab);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [AvaloniaFact(DisplayName = "GAL-H005 | The select_tab tool switches the ribbon tab and the document remembers it")]
    [Trait("TestId", "GAL-H005")]
    public async Task SelectTabToolSetsTheOpeningTab()
    {
        var vm = new MainViewModel();
        var api = new Prism.Automation.AutomationApi(vm, null);
        var target = vm.Tabs.Where(t => !t.IsFile && !t.IsContextual).Last();
        var result = await api.CallAsync("select_tab", (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"" + target.Title.ToLowerInvariant() + "\"}")!);
        Assert.Equal(target.Title, (string?)result!["tab"]);
        Assert.Same(target, vm.SelectedTab);
        Assert.Equal(target.Title, vm.Session.OpeningTab);
        await Assert.ThrowsAsync<ArgumentException>(() => api.CallAsync("select_tab", (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"nope\"}")!));
    }

    [AvaloniaFact(DisplayName = "GAL-H002 | Failed model download leaves the current work and startup screen intact")]
    [Trait("TestId", "GAL-H002")]
    public async Task FailedDownloadDoesNotReplaceUserDocument()
    {
        var vm = new MainViewModel();
        var original = vm.Session;
        var userPart = original.AddObject(Occ.Box(10, 20, 30), "Unsaved user part", ObjKind.Solid);
        vm.HomeOpen = true;
        string root = Path.Combine(Path.GetTempPath(), "prism-gallery-failure-" + Guid.NewGuid());
        using var http = new HttpClient(new CatalogTransport("{}"));
        vm.GallerySource = new GalleryStore(http, root);
        var card = new GalleryCard(new GalleryEntry("missing", "Missing example", "Unavailable", "Solid", "Prism", "MIT", "0.1.0",
            "https://github.com/a/model", "https://github.com/a/image", 1, new string('a', 64)));
        try
        {
            await vm.OpenGalleryCard(card);
            Assert.Same(original, vm.Session);
            Assert.Same(userPart, Assert.Single(original.Objects));
            Assert.True(vm.HomeOpen);
            Assert.False(vm.GalleryBusy);
            Assert.Contains("Could not open Missing example", vm.GalleryStatus);
            Assert.False(Directory.Exists(Path.Combine(root, "work")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
