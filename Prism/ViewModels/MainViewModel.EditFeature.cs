using System.Linq;
using Prism.Core;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>The step to edit: the one that made the selected object, or (with nothing selected) the latest step.</summary>
    public HistoryNode? SelectedFeatureNode()
    {
        var sel = Session.SelectedObjects.ToList();
        if (sel.Count == 1) return Session.FeatureNodeFor(sel[0].Id);
        if (sel.Count == 0 && Session.Current.Recipe != null) return Session.Current;
        return null;
    }

    public bool CanEditFeature => !IsSketching && Session.ActiveCommand == null && SelectedFeatureNode() != null;

    public void EditSelectedFeature()
    {
        if (SelectedFeatureNode() is { } n) Session.EditFeature(n);
        else Session.Say("Select a body made with Extrude, Revolve, Fillet and the like, then edit its feature. (Or step to the feature in the history and edit it there.)", true);
    }
}
