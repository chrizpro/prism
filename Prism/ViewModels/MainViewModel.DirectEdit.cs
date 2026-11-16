using System;
using System.Linq;
using Prism.Ribbon;

namespace Prism.ViewModels;

/// <summary>Direct Edit: a tab of its own for editing the geometry you already have, opened from the Solid tab like Sketch.</summary>
/// <summary>The kind of modeling a Direct Modeling session came from. It decides which tools of the tab are available.</summary>
public enum DirectWorkflow { Solid, Surface, Freeform, Nurbs }

public sealed partial class MainViewModel
{
    DirectWorkflow _directWorkflow = DirectWorkflow.Solid;
    public DirectWorkflow DirectWorkflow => _directWorkflow;

    /// <summary>True while the Direct Modeling tab is open for one of these workflows. Used to enable the tools that work on that kind of geometry.</summary>
    Func<bool> DirectFor(params DirectWorkflow[] workflows) => () => Array.IndexOf(workflows, _directWorkflow) >= 0;

    public bool IsDirectEditing => _directTab != null && _directTab.IsVisible;

    /// <summary>Show the Direct Modeling tab and switch to it. The tab you came from is remembered so Finish Editing can go back.</summary>
    public void EnterDirectEdit() => EnterDirectEdit(DirectWorkflow.Solid);

    /// <summary>Open Direct Modeling from the Solid, Surface, Freeform or NURBS tab. The tools that cannot work on that kind of geometry stay disabled.</summary>
    public void EnterDirectEdit(DirectWorkflow workflow)
    {
        if (_directTab == null || IsSketching) return;
        if (IsDirectEditing && workflow != _directWorkflow && Session.ActiveCommand != null) Session.CancelCommand();
        _directWorkflow = workflow;
        if (!_directTab.IsVisible)
        {
            if (SelectedTab != _directTab && SelectedTab != null && !SelectedTab.IsContextual) _lastNormalTab = SelectedTab;
            _directTab.IsVisible = true;
        }
        SelectedTab = _directTab;
        RibbonCollapsed = false;
        Session.DirectEditing = true;
        OnPropertyChanged(nameof(IsDirectEditing));
        StartDirectGizmo();
        RefreshRibbon();
    }

    /// <summary>Direct edits are plain undo steps unless a body tracks history. Turning it on records the edits made from then on as editable steps.</summary>
    public void SetTrackHistory(System.Collections.Generic.IEnumerable<System.Guid> ids, bool on)
    {
        var list = ids.ToList();
        foreach (var id in list) Session.Update(id, o => o.WithTracking(on), label: on ? "Track history" : "Stop tracking history");
        Session.Say(on ? "Tracking history: direct edits of this body can be edited again later." : "No longer tracking history: direct edits are plain undo steps.");
    }

    bool _startingGizmo;
    /// <summary>The gizmo is a command that stays open while the tab is open, so every click on a face puts handles on it. It comes back after each edit.</summary>
    void StartDirectGizmo()
    {
        if (!IsDirectEditing || _startingGizmo || Session.ActiveCommand != null || Session.Sketch != null) return;
        _startingGizmo = true;
        // Solid and surface faces take the gizmo; a freeform is edited by its cage and NURBS by their control points.
        try
        {
            switch (_directWorkflow)
            {
                case DirectWorkflow.Freeform: Session.StartCommand(() => new Commands.EditFormCommand()); break;
                case DirectWorkflow.Nurbs: Session.StartCommand(() => new Commands.EditPointsCommand()); break;
                default: Session.StartCommand(() => new Commands.DirectEditCommand()); break;
            }
        }
        finally { _startingGizmo = false; }
    }

    /// <summary>Hide the Direct Modeling tab, drop any half-finished tool, and go back to the tab you came from.</summary>
    public void ExitDirectEdit()
    {
        if (_directTab == null || !_directTab.IsVisible) return;
        _directTab.IsVisible = false;
        Session.DirectEditing = false;
        _directWorkflow = DirectWorkflow.Solid;
        if (SelectedTab == _directTab) SelectedTab = IsSketching ? _sketchTab : _lastNormalTab;
        OnPropertyChanged(nameof(IsDirectEditing));
        if (Session.ActiveCommand != null) Session.CancelCommand();
    }
}
