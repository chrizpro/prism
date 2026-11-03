using System.Linq;
using Prism.Ribbon;

namespace Prism.ViewModels;

/// <summary>Direct Edit: a tab of its own for editing the geometry you already have, opened from the Solid tab like Sketch.</summary>
public sealed partial class MainViewModel
{
    public bool IsDirectEditing => _directTab != null && _directTab.IsVisible;

    /// <summary>Show the Direct Modeling tab and switch to it. The tab you came from is remembered so Finish Editing can go back.</summary>
    public void EnterDirectEdit()
    {
        if (_directTab == null || IsSketching) return;
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
        try { Session.StartCommand(() => new Commands.DirectEditCommand()); }
        finally { _startingGizmo = false; }
    }

    /// <summary>Hide the Direct Modeling tab, drop any half-finished tool, and go back to the tab you came from.</summary>
    public void ExitDirectEdit()
    {
        if (_directTab == null || !_directTab.IsVisible) return;
        _directTab.IsVisible = false;
        Session.DirectEditing = false;
        if (SelectedTab == _directTab) SelectedTab = IsSketching ? _sketchTab : _lastNormalTab;
        OnPropertyChanged(nameof(IsDirectEditing));
        if (Session.ActiveCommand != null) Session.CancelCommand();
    }
}
