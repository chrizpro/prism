using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Ribbon;

namespace Prism.ViewModels;

/// <summary>A task-focused set of ribbon tabs that keeps related workflows together.</summary>
public sealed record WorkspaceDef(string Id, string Name, string Tip, string[] Tabs, string? MainTab = null);

public sealed partial class WorkspaceItem : ObservableObject
{
    public required WorkspaceDef Def { get; init; }
    public string Name => Def.Name;
    public string Tip => Def.Tip;
    public string Id => Def.Id;
    [ObservableProperty] public partial bool IsCurrent { get; set; }
}

public sealed partial class MainViewModel
{
    // Home, View and the File menu belong to every workspace; contextual tabs (Sketch, Materials) appear on their own.
    public static readonly WorkspaceDef[] WorkspaceDefs =
    [
        new("design", "Design", "Solid, surface, freeform, NURBS and sheet metal modeling.", ["Home", "Solid", "Surface", "Freeform", "NURBS", "Direct Modeling", "Sheet Metal", "Analyze", "View"]),
        new("ecad", "ECAD", "Electronic boards, component envelopes and enclosure integration.", ["Home", "ECAD", "Analyze", "View"], "ECAD"),
        new("scan", "Scan", "Meshes and reverse engineering. Print preparation is in CAM.", ["Home", "Mesh", "Solid", "Surface", "Direct Modeling", "Analyze", "View"], "Mesh"),
        new("simulate", "CAE", "Static structures, thermal conduction, natural frequencies and pipe-flow calculations.", ["Home", "Static", "Thermal", "Dynamics", "Fluids", "Analyze", "View"], "Static"),
        new("assemble", "Assemble", "Bottom-up assemblies: insert parts, joint them, test the motion and animate it.", ["Home", "Assemble", "Joints", "Animate", "Analyze", "View"], "Assemble"),
        new("render", "Render", "Make realistic pictures: lighting, background and quality.", ["Home", "Render", "View"], "Render"),
        new("make", "CAM", "Stock and work zero, milling, toolpath playback, external slicer paths and tooling inspection.", ["Home", "CAM", "Additive", "Tooling", "Analyze", "View"], "CAM"),
        new("script", "Script", "Write Python that builds and changes models. The editor sits beside the model; everything a script makes is one undo step.", ["Home", "Script", "View"], "Script"),
        new("all", "All tabs", "Show every tab.", []),
    ];

    public ObservableCollection<WorkspaceItem> Workspaces { get; } = [];
    [ObservableProperty] public partial string WorkspaceId { get; set; } = "design";

    static string UiFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "ui.txt");

    void InitWorkspaces()
    {
        foreach (var d in WorkspaceDefs) Workspaces.Add(new WorkspaceItem { Def = d });
        try { if (File.Exists(UiFile)) { var id = File.ReadAllText(UiFile).Trim(); if (id == "ar") id = "design"; if (WorkspaceDefs.Any(w => w.Id == id)) WorkspaceId = id; } } catch { }
        ApplyWorkspace();
    }

    public void SetWorkspace(string id)
    {
        if (id == "ar") id = "design"; // Existing automation links land on Home, where AR now lives.
        if (id == "cae") id = "simulate";
        if (id == "cam") id = "make";
        if (WorkspaceDefs.All(w => w.Id != id)) return;
        WorkspaceId = id;
        try { Directory.CreateDirectory(Path.GetDirectoryName(UiFile)!); File.WriteAllText(UiFile, id); } catch { }
        ApplyWorkspace();
        // land on the workspace's own tab so its tools are in front
        if (Current.MainTab is { } main && Tabs.FirstOrDefault(t => t.Title == main && t.IsVisible) is { } tab) SelectTab(tab);
    }

    WorkspaceDef Current => WorkspaceDefs.First(w => w.Id == WorkspaceId);

    /// <summary>Which regular tabs show: the workspace's set, plus tabs the document itself calls for (meshes in the scene bring the Mesh tab).</summary>
    bool TabShown(RibbonTab t)
    {
        if (t.IsFile || t.IsContextual) return true;
        if (t.UserHidden) return false;
        if (t.IsCustom || WorkspaceId == "all") return true;
        if (Current.Tabs.Contains(t.Title)) return true;
        return t.Title == "Mesh" && Session.Objects.Any(o => o.Tri != null);
    }

    void ApplyWorkspace()
    {
        foreach (var w in Workspaces) w.IsCurrent = w.Id == WorkspaceId;
        ModeName = Current.Name;
        ShowScriptPanel = WorkspaceId == "script";
        if (ShowScriptPanel) { Prism.Core.Scripting.ScriptPaths.Prepare(); EnsureScriptLoaded(); }
        UpdatePanels();
        foreach (var t in Tabs)
            if (!t.IsFile && !t.IsContextual) { bool show = TabShown(t); if (t.IsVisible != show) t.IsVisible = show; }
        if (SelectedTab is { IsVisible: false } && !(SelectedTab.IsContextual))
            SelectedTab = Tabs.FirstOrDefault(t => t.Title == "Home" && t.IsVisible) ?? Tabs.First(t => t.IsVisible && !t.IsFile);
    }

    /// <summary>Make a tab reachable (used by command search): switch to a workspace that shows it, or to All tabs.</summary>
    public void EnsureTabVisible(RibbonTab tab)
    {
        if (tab.IsVisible || tab.IsFile || tab.IsContextual) return;
        var home = WorkspaceDefs.FirstOrDefault(w => w.Tabs.Contains(tab.Title));
        SetWorkspace(home?.Id ?? "all");
    }
}
