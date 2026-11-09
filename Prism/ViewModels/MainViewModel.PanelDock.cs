using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Prism.ViewModels;

public sealed record RightPanelTab(string Id, string Title, bool IsSelected);

public sealed partial class MainViewModel
{
    public ObservableCollection<RightPanelTab> RightPanelTabs { get; } = [];
    readonly HashSet<string> _openRightPanels = ["properties"];
    [ObservableProperty] public partial string SelectedRightPanel { get; set; } = "context";
    [ObservableProperty] public partial bool ShowCommandPanel { get; set; }
    [ObservableProperty] public partial bool ShowGizmoPanel {get;set;}
    bool _syncingRightPanels;
    string? _lastPanelContext;
    string _panelBeforeOperation = "context";
    public string ContextPanelId => ActiveCommand != null ? "command" : IsSketching ? "sketch" : Session.FreeTool is Core.DatumPickTool || SelectedDatum != null ? "datum" : WorkspaceId == "simulate" && Session.ActiveStudy != null ? "simulation" : ShowScriptPanel && WorkspaceId == "script" ? "script" : "properties";
    string RightPanelContentId => SelectedRightPanel == "context" ? ContextPanelId : SelectedRightPanel;
    public bool IsRightPanelOpen(string id) => _openRightPanels.Contains(id) || ContextPanelId == id;
    public static string PanelTitle(string id) => id switch
    {
        "command" => "Command", "sketch" => "Sketch", "datum" => "Datum", "appearance" => "Appearance", "diagnostics" => "Diagnostics",
        "references" => "References", "parameters" => "Parameters", "script" => "Script", "data" => "Data", "simulation" => "Simulation", "library" => "Parts library", "gizmo"=>"Gizmo", _ => "Properties",
    };

    partial void OnSelectedRightPanelChanged(string value)
    {
        if(!_syncingRightPanels&&value!="gizmo"&&GizmoPositioning&&!GizmoIsDatum)CancelGizmoPlacement();
        if (!_syncingRightPanels) { UpdatePanels(); RefreshStudios(); }
    }

    public void SelectRightPanel(string id)
    {
        if (id != "context" && !_openRightPanels.Contains(id)) return;
        SelectedRightPanel = id;
    }

    public void SetRightPanelEnabled(string id, bool enabled)
    {
        if (id is not ("properties" or "appearance" or "diagnostics" or "references" or "parameters" or "script" or "data" or "library" or "gizmo")) return;
        if (id == "properties" && !enabled) return;
        if (enabled) _openRightPanels.Add(id); else _openRightPanels.Remove(id);
        if (enabled) SelectedRightPanel = ContextPanelId == id ? "context" : id;
        else if (SelectedRightPanel == id) SelectedRightPanel = _openRightPanels.LastOrDefault() ?? "context";
        UpdatePanels();
    }

    public void OpenRightPanel(string id) => SetRightPanelEnabled(id, true);
    public void CloseRightPanel(string? id = null) => SetRightPanelEnabled(id ?? SelectedRightPanel, false);

    void SyncRightPanels()
    {
        if (_syncingRightPanels) return;
        _syncingRightPanels = true;
        try
        {
            string context = ContextPanelId;
            if (_lastPanelContext != context)
            {
                string? previous = _lastPanelContext;
                _lastPanelContext = context;
                if (context is "sketch" or "command" or "datum")
                { _panelBeforeOperation = SelectedRightPanel; SelectedRightPanel = "context"; }
                else if (previous is "sketch" or "command" or "datum" && SelectedRightPanel == "context" && _openRightPanels.Contains(_panelBeforeOperation))
                    SelectedRightPanel = _panelBeforeOperation;
            }
            string content = RightPanelContentId;
            StudioPanel = content is "diagnostics" or "references" or "parameters" ? content : null;
            ShowCommandPanel = content == "command" && ActiveCommand != null;
            ShowGizmoPanel=content=="gizmo";
            ShowDatumPanel = content == "datum";
            if (ShowDatumPanel) RefreshDatumPanel();
            ShowSketchPanel = content == "sketch" && IsSketching;
            ShowMaterialsPanel = content == "appearance";
            ShowLibraryPanel = content == "library";
            ShowDiagnosticsStudio = content == "diagnostics";
            ShowReferencesStudio = content == "references";
            ShowParametersStudio = content == "parameters";
            ShowProperties = content == "properties";
            ShowSimPanel = content == "simulation";
            var tabs = new List<RightPanelTab> { new("context", PanelTitle(context), SelectedRightPanel == "context") };
            foreach (string id in new[] { "properties", "appearance", "diagnostics", "references", "parameters", "script", "data", "library", "gizmo" })
                if (_openRightPanels.Contains(id) && id != context) tabs.Add(new(id, PanelTitle(id), SelectedRightPanel == id));
            if (!RightPanelTabs.SequenceEqual(tabs)) { RightPanelTabs.Clear(); foreach (var tab in tabs) RightPanelTabs.Add(tab); }
            OnPropertyChanged(nameof(AnyStudioVisible)); OnPropertyChanged(nameof(ShowScriptEditor)); OnPropertyChanged(nameof(ShowDataEditor));
        }
        finally { _syncingRightPanels = false; }
    }
}
