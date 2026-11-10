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
    public FeedbackPreference Feedback { get; set; } = new();
    public GizmoSettings? Gizmo { get; set; } = new();
    public string AccentColor { get; set; } = "Blue";
    public string Theme { get; set; } = "Dark";                 // Dark | Light | System
    public bool MarkingMenu { get; set; } = true;
    public bool ReverseScrollZoom { get; set; }
    public bool ShowGridOnStart { get; set; } = true;    /// <summary>The 3D view backdrop and grid look (see GridPreset).</summary>
    public string GridStyle { get; set; } = "classic";
    /// <summary>The folder parts are saved to and opened from by default. Null until the user picks one.</summary>
    public string? WorkspacePath { get; set; }
    /// <summary>The folder the parts library scans for STEP and Prism files. Null until the user picks one.</summary>
    public string? PartsLibraryPath { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public List<uint> SavedAppearanceColors { get; set; } = [];
    public List<SavedAppearance> SavedAppearances { get; set; } = [];
    public List<Material> CustomMaterials { get; set; } = [];
    public bool ShowHomeOnStart { get; set; } = true;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public bool AutoRecoveryEnabled { get; set; }
    int autoRecoveryIntervalMinutes = 5;
    public int AutoRecoveryIntervalMinutes { get => autoRecoveryIntervalMinutes; set => autoRecoveryIntervalMinutes = Math.Clamp(value, 1, 120); }
    /// <summary>Workspace IDs pinned to the top bar; null uses the default selection.</summary>
    public List<string>? VisibleWorkspaces { get; set; }
    public int MarkingHoldMs { get; set; } = 260;
    double planeScale = 1.0;
    /// <summary>Display scale for origin and construction planes (1 = the model-relative default).</summary>
    public double PlaneScale { get => planeScale; set => planeScale = Math.Clamp(value, 0.25, 2.0); }
    /// <summary>The variant each split ribbon button showed last (by button id), so a button comes back as you left it.</summary>
    public Dictionary<string, string> LastPicks { get; set; } = [];
    public RibbonLayout Ribbon { get; set; } = new();
    /// <summary>The floating toolbar over the 3D view: ribbon command ids, "|" for a divider. Null means the shipped set.</summary>
    public List<string>? QuickBar { get; set; }
    public bool ShowQuickBar { get; set; } = true;
    public bool HistoryExpanded { get; set; } = true;
    public double HistoryHeight { get; set; } = 210;
    public string HelpContextLocation { get; set; } = "viewport";
    /// <summary>Base URL for per-tool online guides. Leave unset until the guide is published.</summary>
    public string? ToolDocumentationBaseUrl { get; set; }
    /// <summary>What the status bar shows on the right (see MainViewModel.StatusCatalog). Null means the shipped set.</summary>
    public List<string>? StatusBar { get; set; }
    /// <summary>Changed keyboard shortcuts by action id, written "gesture|scope". Actions not listed keep their shipped key (see Hotkeys).</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = [];
    /// <summary>Shortcuts the user added, each running one ribbon command.</summary>
    public List<CustomHotkey> CustomHotkeys { get; set; } = [];

    public static AppSettings Current { get; private set; } = Load();
    public static event Action? Changed;

    /// <summary>PRISM_SETTINGS_FILE points the settings somewhere else, so tests never touch the real ones.</summary>
    static string FilePath => Environment.GetEnvironmentVariable("PRISM_SETTINGS_FILE") is { Length: > 0 } custom ? custom : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "settings.json");

    static AppSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception) { /* a damaged settings file is not worth failing to start for */ }
        return new();
    }

    public static void Save(bool notify = true) => TrySave(notify);

    public static bool TrySave(bool notify = true)
    {
        string path = FilePath, temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception) { return false; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception) { } }
        if (notify) Changed?.Invoke();
        return true;
    }
}
