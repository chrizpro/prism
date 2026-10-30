using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;
using Prism.Core;
using Prism.Rendering;

namespace Prism.ViewModels;

/// <summary>A tile in the parts library.</summary>
public sealed partial class LibraryItem : ObservableObject
{
    public required LibraryPart Part { get; init; }
    public string Name => Part.Name;
    public string Category => Part.Category;
    public string Source => Part.Source;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasThumb))] public partial Bitmap? Thumb { get; set; }
    public bool HasThumb => Thumb != null;
    /// <summary>The part number, shown under the name when the part file carries one.</summary>
    public string Subtitle => Part.Info?.PartNumber ?? "";
    public bool HasSubtitle => Subtitle.Length > 0;
    public string ToolTipText
    {
        get
        {
            string info = Part.Info is { IsEmpty: false } i ? "\n" + i.Summary() : "";
            return Part.IsBuiltIn ? $"{Part.Name}\nBuilt-in parametric part. Double-click to insert." : $"{Part.Name}{info}\n{Part.Path}\nDouble-click to insert.";
        }
    }
}

public sealed class LibraryGroup
{
    public required string Title { get; init; }
    public ObservableCollection<LibraryItem> Items { get; } = [];
    public string Header => $"{Title}  ·  {Items.Count}";
}

/// <summary>The parts library: a folder of STEP / Prism files plus built-in common parts, shown as a picture grid.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<LibraryGroup> LibraryGroups { get; } = [];
    [ObservableProperty] public partial bool ShowLibraryPanel { get; set; }
    [ObservableProperty] public partial string LibraryFilter { get; set; } = "";
    [ObservableProperty] public partial string LibraryFolderText { get; set; } = "No folder chosen. Built-in parts are shown below.";
    [ObservableProperty] public partial string LibraryStatus { get; set; } = "";
    [ObservableProperty] public partial bool LibraryShowBuiltIn { get; set; } = true;
    [ObservableProperty] public partial LibraryItem? SelectedLibraryItem { get; set; }

    readonly List<LibraryItem> _libraryItems = [];
    int _libraryScanId;

    partial void OnLibraryFilterChanged(string value) => RebuildLibraryGroups();
    partial void OnLibraryShowBuiltInChanged(bool value) => RebuildLibraryGroups();

    public void OpenLibrary()
    {
        OpenRightPanel("library");
        if (_libraryItems.Count == 0) RescanLibrary();
    }

    public void SetLibraryFolder(string? folder)
    {
        AppSettings.Current.PartsLibraryPath = string.IsNullOrWhiteSpace(folder) ? null : folder;
        AppSettings.Save(notify: false);
        RescanLibrary();
    }

    public void RescanLibrary()
    {
        int scan = ++_libraryScanId;
        string? folder = AppSettings.Current.PartsLibraryPath;
        LibraryFolderText = folder == null ? "No folder chosen. Built-in parts are shown below." : folder;
        _libraryItems.Clear();
        foreach (var part in PartsLibrary.Scan(folder)) _libraryItems.Add(new LibraryItem { Part = part });
        int files = _libraryItems.Count;
        foreach (var part in PartsLibrary.BuiltIn()) _libraryItems.Add(new LibraryItem { Part = part });
        LibraryStatus = folder == null ? "Choose a folder of STEP or Prism parts to fill the library." : files == 0 ? "That folder has no STEP (.step, .stp) or Prism files yet." : $"{files} part file(s) found.";
        RebuildLibraryGroups();
        StartThumbnails(scan);
    }

    void RebuildLibraryGroups()
    {
        string filter = LibraryFilter.Trim();
        LibraryGroups.Clear();
        var shown = _libraryItems.Where(i => (LibraryShowBuiltIn || !i.Part.IsBuiltIn) && (filter.Length == 0 || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || i.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) || i.Part.Info?.Matches(filter) == true));
        foreach (var g in shown.GroupBy(i => i.Part.IsBuiltIn ? "Built-in · " + i.Category : i.Category))
        {
            var group = new LibraryGroup { Title = g.Key };
            foreach (var item in g) group.Items.Add(item);
            LibraryGroups.Add(group);
        }
    }

    // ------------------------------------------------------------------ thumbnails (made one at a time off the UI thread, cached on disk)
    static string CacheFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "library-cache");

    static string CacheKey(LibraryPart part)
    {
        string raw = part.Path != null ? $"{part.Path}|{File.GetLastWriteTimeUtc(part.Path).Ticks}|v1" : $"builtin|{part.Category}|{part.Name}|v1";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw)))[..20] + ".png";
    }

    const int ThumbSize = 112;

    void StartThumbnails(int scan)
    {
        var work = _libraryItems.ToList();
        Task.Run(() =>
        {
            foreach (var item in work)
            {
                if (scan != _libraryScanId) return;
                try
                {
                    string cache = Path.Combine(CacheFolder, CacheKey(item.Part));
                    byte[]? png = File.Exists(cache) ? File.ReadAllBytes(cache) : null;
                    if (png == null)
                    {
                        var (shape, color, _) = PartsLibrary.Load(item.Part);
                        var pixels = PartsLibrary.RenderThumbnail(shape, ThumbSize, color: 0xFFB4BCC8);
                        png = PngWriter.Encode(pixels, ThumbSize, ThumbSize);
                        Directory.CreateDirectory(CacheFolder);
                        File.WriteAllBytes(cache, png);
                    }
                    var data = png;
                    Dispatcher.UIThread.Post(() => { if (scan == _libraryScanId) item.Thumb = new Bitmap(new MemoryStream(data)); });
                }
                catch (Exception) { /* a file that cannot be read simply keeps its placeholder */ }
            }
        });
    }

    // ------------------------------------------------------------------ inserting
    public void InsertLibraryItem(LibraryItem? item)
    {
        item ??= SelectedLibraryItem;
        if (item == null) { Session.Say("Pick a part in the library first.", true); return; }
        if (IsSketching) { Session.Say("Finish the sketch before inserting a part.", true); return; }
        Session.StartCommand(InsertComponentCommand.ForLibrary(item.Part));
    }
}
