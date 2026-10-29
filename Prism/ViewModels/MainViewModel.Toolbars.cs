using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;
using Prism.Ribbon;

namespace Prism.ViewModels;

/// <summary>A thin divider between groups of buttons in the floating toolbar.</summary>
public sealed class QuickSeparator { }

public sealed partial class MainViewModel
{
    // ------------------------------------------------------------ the floating toolbar over the 3D view
    /// <summary>Ribbon command ids, in order. "|" is a divider. Anything the ribbon can do can sit here.</summary>
    public static readonly string[] DefaultQuickBar =
    [
        "zoom-extents|Zoom Extents", "look-at|Look At", "view-iso|Isometric", "ortho|Orthographic", "|",
        "shaded|Shaded", "shaded-edges|Shaded + edges", "wireframe|Wireframe", "xray|X-ray", "|",
        "grid|Grid", "section|Section View", "measure|Measure",
    ];

    public ObservableCollection<object> QuickBar { get; } = [];
    [ObservableProperty] public partial bool HasQuickBar { get; set; }

    public void RebuildQuickBar()
    {
        QuickBar.Clear();
        var s = AppSettings.Current;
        foreach (var id in s.QuickBar ?? [.. DefaultQuickBar])
        {
            if (id == "|") { if (QuickBar.Count > 0 && QuickBar[^1] is not QuickSeparator) QuickBar.Add(new QuickSeparator()); }
            else if (_catalog.TryGetValue(id, out var b)) { b.Refresh(); QuickBar.Add(b); }
        }
        while (QuickBar.Count > 0 && QuickBar[^1] is QuickSeparator) QuickBar.RemoveAt(QuickBar.Count - 1);
        if(IsSketching)
        {
            var snap=Btn("Grid Snap","grid-snap",()=>{Session.SketchGridSnap=!Session.SketchGridSnap;Session.RaiseState();},"Snap to the active sketch grid.",RibbonSize.Small,()=>IsSketching,()=>Session.SketchGridSnap);
            snap.Refresh();QuickBar.Add(snap);
        }
        HasQuickBar = s.ShowQuickBar && QuickBar.Count > 0;
    }

    // ------------------------------------------------------------ the status bar
    /// <summary>What the right side of the status bar can show. The message on the left is always there.</summary>
    public static readonly (string Id, string Label)[] StatusCatalog =
    [
        ("workspace", "Workspace folder (click for the home screen)"),
        ("mode", "Mode: which ribbon tabs show (click to switch)"),
        ("selection", "What is selected"),
        ("cursor", "Cursor position (X, Y, Z)"),
        ("memory", "Memory in use"),
        ("scene", "Objects and triangles in the model"),
        ("modes", "Selection mode: Object, Face, Edge"),
        ("display", "Display style: shaded, edges, wireframe, X-ray"),
        ("fit", "Fit view button"),
        ("units", "Units (mm)"),
    ];
    public static readonly string[] DefaultStatusBar = ["workspace", "mode", "cursor", "memory", "modes", "fit", "units"];

    [ObservableProperty] public partial string MemoryText { get; set; } = "";
    [ObservableProperty] public partial string SceneText { get; set; } = "";
    [ObservableProperty] public partial string ModeName { get; set; } = "Design";
    System.Threading.Timer? _statusTimer;

    void StartStatusTimer()
    {
        AppSettings.Changed += () => Dispatcher.UIThread.Post(RebuildQuickBar);
        UpdateStatusText();
        _statusTimer = new System.Threading.Timer(_ => Dispatcher.UIThread.Post(UpdateStatusText), null, 2000, 2000);
    }

    void UpdateStatusText()
    {
        try
        {
            double mb = Process.GetCurrentProcess().WorkingSet64 / 1048576.0;
            MemoryText = mb >= 1024 ? $"RAM {mb / 1024:0.0} GB" : $"RAM {mb:0} MB";
            long tris = 0; int n = 0;
            foreach (var o in Session.Objects) { n++; tris += (o.Mesh?.Indices.Length ?? 0) / 3; }
            SceneText = $"{n} object{(n == 1 ? "" : "s")} · {tris:N0} triangles";
        }
        catch (Exception) { /* the readout is a nicety; never worth an error */ }
    }
}
