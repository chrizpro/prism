using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

public sealed partial class GalleryCard(GalleryEntry entry) : ObservableObject
{
    public GalleryEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Workflow => Entry.Workflow;
    public string Credit => "Made with Prism MCP · " + Entry.Author + " · " + Entry.License;
    public bool Compatible => Entry.IsCompatible(AppInfo.Version);
    public string VersionHint => "Requires Prism " + Entry.MinimumVersion;
    public System.Collections.Generic.IReadOnlyList<GalleryExtra> Extras => Entry.Extras ?? [];
    public bool HasExtras => Extras.Count > 0;
    public bool NoExtras => !HasExtras;
    public bool CanOpen => Compatible && !Busy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanOpen))] public partial bool Busy { get; set; }
    [ObservableProperty] public partial Bitmap? Preview { get; set; }
    [ObservableProperty] public partial string ActionLabel { get; set; } = "Download & open";
}

/// <summary>One titled group of gallery cards; shows its first few until expanded (always all while searching).</summary>
public sealed partial class GallerySection(string title, System.Collections.Generic.List<GalleryCard> cards, bool searching, bool expanded, Action<GallerySection> toggled) : ObservableObject
{
    public string Title => title;
    public string Heading => title + "  ·  " + cards.Count;
    public System.Collections.Generic.List<GalleryCard> Cards => cards;
    public bool Expanded { get; private set; } = expanded;
    public bool CanExpand => !searching && cards.Count > GallerySections.Preview;
    public System.Collections.Generic.IEnumerable<GalleryCard> Visible => searching || Expanded ? cards : cards.Take(GallerySections.Preview);
    public string ToggleText => Expanded ? "Show fewer" : "Show all " + cards.Count;
    public void Toggle() { Expanded = !Expanded; toggled(this); }
}

public sealed partial class MainViewModel
{
    public ObservableCollection<GalleryCard> GalleryCards { get; } = [];
    public ObservableCollection<GallerySection> GalleryGroups { get; } = [];
    readonly System.Collections.Generic.HashSet<string> _expandedSections = new(StringComparer.OrdinalIgnoreCase);
    readonly ObservableCollection<GalleryCard> _galleryCards = [];
    public GalleryStore GallerySource { get; set; } = GalleryStore.Local;
    [ObservableProperty] public partial string GallerySearch { get; set; } = "";
    [ObservableProperty] public partial string GalleryStatus { get; set; } = "Examples built through Prism's MCP bridge — the same AI toolkit in Settings → AI Assistant. Download one to explore and make it your own.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(GalleryAvailable))] public partial bool GalleryBusy { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(GalleryAvailable))] public partial bool GalleryRefreshing { get; set; }
    public bool GalleryAvailable => !GalleryBusy && !GalleryRefreshing;
    public bool GalleryEmpty => GalleryCards.Count == 0 && _galleryInitialized;
    bool _galleryInitialized;
    string GalleryFallback()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Prism/Assets/gallery-catalog.json"));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    partial void OnGallerySearchChanged(string value) => FilterGallery();
    void FilterGallery()
    {
        GalleryCards.Clear();
        foreach (var card in _galleryCards.Where(c => (c.Name + " " + c.Description + " " + c.Workflow).Contains(GallerySearch.Trim(), StringComparison.OrdinalIgnoreCase)))
            GalleryCards.Add(card);
        BuildGallerySections();
        OnPropertyChanged(nameof(GalleryEmpty));
    }
    void BuildGallerySections()
    {
        bool searching = GallerySearch.Trim().Length > 0;
        GalleryGroups.Clear();
        foreach (var (name, entries) in GallerySections.Group(GalleryCards.Select(c => c.Entry)))
        {
            var cards = entries.Select(e => GalleryCards.First(c => c.Entry == e)).ToList();
            GalleryGroups.Add(new GallerySection(name, cards, searching, _expandedSections.Contains(name), section =>
            {
                if (section.Expanded) _expandedSections.Add(section.Title); else _expandedSections.Remove(section.Title);
                BuildGallerySections();
            }));
        }
    }
    public async Task LoadGallery(bool refresh = false)
    {
        if (GalleryBusy || GalleryRefreshing || (_galleryInitialized && !refresh)) return;
        GalleryRefreshing = true;
        try
        {
            if (!_galleryInitialized)
            {
                SetGallery(GallerySource.ReadCatalog(GalleryFallback()));
                _galleryInitialized = true;
                OnPropertyChanged(nameof(GalleryEmpty));
            }
            foreach (var card in _galleryCards)
            {
                card.ActionLabel = await Task.Run(() => GallerySource.IsCached(card.Entry)) ? "Open a copy" : "Download & open";
                if (card.Preview == null && await Task.Run(() => GallerySource.ReadPreview(card.Entry)) is { } cached)
                    SetPreview(card, cached);
            }
            try
            {
                var catalog = await GallerySource.Refresh();
                if (!catalog.Items.SequenceEqual(_galleryCards.Select(c => c.Entry))) SetGallery(catalog);
                GalleryStatus = "Examples built through Prism's MCP bridge — the same AI toolkit in Settings → AI Assistant. Download one to explore and make it your own.";
            }
            catch (Exception) { GalleryStatus = "Could not refresh the gallery. Downloaded examples still work offline. Use Refresh to retry."; }
            await Task.WhenAll(_galleryCards.Select(async card =>
            {
                card.ActionLabel = await Task.Run(() => GallerySource.IsCached(card.Entry)) ? "Open a copy" : "Download & open";
                try
                {
                    using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                    var data = await GallerySource.GetPreview(card.Entry, timeout.Token);
                    SetPreview(card, data);
                }
                catch (Exception) { /* Keep text and action available when an image cannot load. */ }
            }));
        }
        catch (Exception e) { GalleryStatus = "Could not load the gallery: " + e.Message; }
        finally { GalleryRefreshing = false; }
    }
    static void SetPreview(GalleryCard card, byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            var image = new Bitmap(stream);
            card.Preview?.Dispose();
            card.Preview = image;
        }
        catch (Exception) { /* A malformed preview must not hide model actions. */ }
    }
    void SetGallery(GalleryCatalog catalog)
    {
        foreach (var card in _galleryCards) card.Preview?.Dispose();
        _galleryCards.Clear();
        foreach (var entry in catalog.Items) _galleryCards.Add(new GalleryCard(entry));
        FilterGallery();
    }
    /// <summary>Saves a model's extra file (animation video, exploded view) into the workspace's Gallery folder.</summary>
    public async Task DownloadGalleryExtra(GalleryCard card, GalleryExtra extra)
    {
        if (GalleryBusy) return;
        GalleryBusy = true;
        GalleryStatus = "Downloading " + extra.Label + "…";
        try
        {
            string folder = Path.Combine(HasWorkspace ? WorkspaceFolder : DefaultWorkspacePath, "Gallery");
            string path = await GallerySource.SaveExtra(extra, folder);
            GalleryStatus = "Saved " + extra.Label + " of " + card.Name + " to " + path;
        }
        catch (Exception e) { GalleryStatus = "Could not download " + extra.Label + ": " + e.Message; }
        finally { GalleryBusy = false; }
    }
    public async Task OpenGalleryCard(GalleryCard card)
    {
        if (GalleryBusy) return;
        if (!card.Compatible) { GalleryStatus = card.VersionHint; return; }
        GalleryBusy = true;
        foreach (var item in _galleryCards) item.Busy = true;
        GalleryStatus = "Opening " + card.Name + "…";
        try
        {
            string folder = Path.Combine(HasWorkspace ? WorkspaceFolder : DefaultWorkspacePath, "Gallery");
            string path = await GallerySource.CreateWorkingCopy(card.Entry, folder);
            // Let open errors reach the gallery instead of reporting a successful open through OpenPath's guard.
            var document = OpenTarget();
            document.Open(path);
            AddRecent(path);
            CloseHome();
            ShowOpeningTab();
            card.ActionLabel = "Open a copy";
            GalleryStatus = "Opened an editable copy of " + card.Name + ".";
        }
        catch (Exception e) { GalleryStatus = "Could not open " + card.Name + ": " + e.Message + " Use Refresh or try again."; }
        finally { GalleryBusy = false; foreach (var item in _galleryCards) item.Busy = false; }
    }
}
