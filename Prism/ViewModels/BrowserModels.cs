using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

public enum NodeKind { Folder, Object, Origin, Construction, Datum }

/// <summary>One row of the model browser: a folder (Origin, Bodies, Forms…), an object, or an origin plane/axis.</summary>
public sealed partial class BrowserNode : ObservableObject
{
    public NodeKind Kind { get; init; }
    public Guid ObjectId { get; init; }
    public int OriginIndex { get; init; } = -1;
    public ObjKind ObjKind { get; init; }
    /// <summary>For Datum rows: the saved datum's kind. ObjectId is the owning sketch (Guid.Empty for the sketch being edited) and OriginIndex its position in that sketch's datum list.</summary>
    public Core.Sketching.SketchDatumKind? DatumKind { get; init; }
    public string Icon { get; init; } = "box";
    public uint Color { get; init; }
    public ObservableCollection<BrowserNode> Children { get; } = [];
    public Action<BrowserNode>? VisibilityEdited { get; set; }
    public Action<BrowserNode>? RenameEdited { get; set; }

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Badge { get; set; } = "";
    [ObservableProperty] public partial bool IsExpanded { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsEditing { get; set; }
    [ObservableProperty] public partial string EditText { get; set; } = "";

    public bool IsFolder => Kind == NodeKind.Folder;
    public bool IsObject => Kind == NodeKind.Object;
    public bool HasSwatch => Kind == NodeKind.Object;
    public bool HasEye => Kind == NodeKind.Datum ? false : Kind != NodeKind.Folder || Children.Count > 0;
    public bool IsActiveSketchDatum => Kind == NodeKind.Datum && ObjectId == Guid.Empty;
    public bool CanRename => Kind == NodeKind.Object;
    public SelItem? SelectionItem => Kind switch
    {
        NodeKind.Object => new SelItem(ObjectId, SelKind.Object, -1),
        NodeKind.Origin => new SelItem(Guid.Empty, SelKind.Origin, OriginIndex),
        NodeKind.Datum when ObjectId != Guid.Empty => new SelItem(ObjectId, SelKind.Datum, OriginIndex),
        _ => null,
    };

    bool _silent;
    public void SetVisibleSilently(bool v) { _silent = true; IsVisible = v; _silent = false; }
    partial void OnIsVisibleChanged(bool value) { if (!_silent) VisibilityEdited?.Invoke(this); }

    public void BeginRename() { if (!CanRename) return; EditText = Title; IsEditing = true; }
    public void CommitRename() { if (!IsEditing) return; IsEditing = false; RenameEdited?.Invoke(this); }
    public void CancelRename() => IsEditing = false;
}

/// <summary>One state in the document history, as shown in the timeline bar and the history panel.</summary>
public sealed partial class HistoryItem : ObservableObject
{
    public HistoryNode Node { get; init; } = null!;
    public string Label { get; init; } = "";
    public string Icon { get; init; } = "box";
    public string TimeText { get; init; } = "";
    public string Chips { get; init; } = "";
    public string? Pin { get; init; }
    public int Depth { get; init; }
    public int BranchCount { get; init; }
    public bool IsStart { get; init; }
    public bool IsPinned => Pin != null;
    public bool HasBranches => BranchCount > 1;
    public bool HasChips => Chips.Length > 0;
    public string ShortLabel => Pin ?? Label;
    public Avalonia.Thickness Indent => new(Depth * 16, 0, 0, 0);
    public string ToolTipText { get; init; } = "";
    [ObservableProperty] public partial bool IsCurrent { get; set; }
    [ObservableProperty] public partial bool IsFuture { get; set; }
    [ObservableProperty] public partial bool OnPath { get; set; } = true;
}

/// <summary>A command (ribbon button) that matched the search box.</summary>
public sealed class CommandHit
{
    public required Ribbon.RibbonButton Button { get; init; }
    public required string Tab { get; init; }
    public string Label => Button.Label.Replace("\n", " ");
    public string Description => Button.Description;
    public string Icon => Button.Icon;
    public bool Enabled => Button.IsEnabled;
}
