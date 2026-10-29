using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Prism.Core;

/// <summary>One group of a customised ribbon tab: its title and the ids of the commands in it.</summary>
public sealed class GroupCfg
{
    public string Title { get; set; } = "";
    public List<string> Items { get; set; } = [];
}

public sealed class CustomTabCfg
{
    public string Title { get; set; } = "";
    public List<GroupCfg> Groups { get; set; } = [];
}

/// <summary>What the user changed about the ribbon. Anything not mentioned here stays as shipped, so new commands in an update still show up.</summary>
public sealed class RibbonLayout
{
    public List<string> Hidden { get; set; } = [];
    public Dictionary<string, List<GroupCfg>> Edited { get; set; } = [];
    public List<CustomTabCfg> Custom { get; set; } = [];
    public bool IsEmpty => Hidden.Count == 0 && Edited.Count == 0 && Custom.Count == 0;
}

/// <summary>Per-user preferences, kept in %LOCALAPPDATA%\Prism\settings.json.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";                 // Dark | Light | System
    public bool MarkingMenu { get; set; } = true;
    public bool ReverseScrollZoom { get; set; }
    /// <summary>The 3D view backdrop and grid look (see GridPreset).</summary>
    public string GridStyle { get; set; } = "classic";
    /// <summary>The folder parts are saved to and opened from by default. Null until the user picks one.</summary>
    public string? WorkspacePath { get; set; }
    /// <summary>The folder the parts library scans for STEP and Prism files. Null until the user picks one.</summary>
    public string? PartsLibraryPath { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public bool ShowHomeOnStart { get; set; } = true;
    public int MarkingHoldMs { get; set; } = 260;
    /// <summary>The variant each split ribbon button showed last (by button id), so a button comes back as you left it.</summary>
    public Dictionary<string, string> LastPicks { get; set; } = [];
    public RibbonLayout Ribbon { get; set; } = new();
    /// <summary>The floating toolbar over the 3D view: ribbon command ids, "|" for a divider. Null means the shipped set.</summary>
    public List<string>? QuickBar { get; set; }
    public bool ShowQuickBar { get; set; } = true;
    public bool HistoryExpanded { get; set; } = true;
    public double HistoryHeight { get; set; } = 210;
    /// <summary>What the status bar shows on the right (see MainViewModel.StatusCatalog). Null means the shipped set.</summary>
    public List<string>? StatusBar { get; set; }

    public static AppSettings Current { get; private set; } = Load();
    public static event Action? Changed;

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "settings.json");

    static AppSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception) { /* a damaged settings file is not worth failing to start for */ }
        return new();
    }

    public static void Save(bool notify = true)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
        if (notify) Changed?.Invoke();
    }
}
