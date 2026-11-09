using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.ViewModels;

/// <summary>Options for the standalone Point, Axis and Plane datum tools: set before the click, and edit what was just placed.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] public partial bool ShowDatumPanel { get; set; }
    DatumPickTool? DatumTool => Session.FreeTool as DatumPickTool;
    /// <summary>The one datum that is selected in plain model view; any kind of datum is edited the same way.</summary>
    SketchDatum? SelectedDatum => Session.ActiveCommand == null && Session.Sketch == null && Session.Selection is [{ Kind: SelKind.Datum } only] ? Session.DatumOf(only) : null;

    public string DatumPanelTitle => (DatumTool?.Kind ?? SelectedDatum?.Kind) switch { SketchDatumKind.Point => "Datum point", SketchDatumKind.Axis => "Datum axis", SketchDatumKind.Plane => "Datum plane", SketchDatumKind.Gizmo => "Datum gizmo", _ => "Datum" };
    public bool DatumHasOrientation => DatumTool is { Kind: not SketchDatumKind.Point };
    public string[] DatumOrientations => DatumTool?.OrientationChoices ?? [];
    public string DatumHint => DatumTool == null ? "Selected datum. Drag the gizmo handles, type its location, or open the full gizmo settings." : DatumTool.Kind switch
    {
        SketchDatumKind.Point => "Click a face, edge or empty space, or move the pointer and press Enter. Keep going to add more.",
        SketchDatumKind.Axis => "Pick how the axis points before you click, or leave it on the pick. After a click a rotate gizmo appears so you can fix it by hand.",
        SketchDatumKind.Plane => "Pick how the plane faces before you click, or leave it on the pick. After a click a rotate gizmo appears so you can fix it by hand.",
        SketchDatumKind.Gizmo => "Click to place a gizmo, or move the pointer and press Enter. It stays selected so you can drag its handles. Esc finishes.",
        _ => "",
    };

    public int DatumOrientation
    {
        get => DatumTool?.Orientation ?? 0;
        set { if (DatumTool is { } t) { t.Orientation = value < 0 ? 0 : value; OnPropertyChanged(); } }
    }

    public double DatumRotationX { get => DatumTool?.Rotation.X ?? 0; set { if (DatumTool is { } t) { t.Rotation = new(value, t.Rotation.Y, t.Rotation.Z); OnPropertyChanged(); } } }
    public double DatumRotationY { get => DatumTool?.Rotation.Y ?? 0; set { if (DatumTool is { } t) { t.Rotation = new(t.Rotation.X, value, t.Rotation.Z); OnPropertyChanged(); } } }
    public double DatumRotationZ { get => DatumTool?.Rotation.Z ?? 0; set { if (DatumTool is { } t) { t.Rotation = new(t.Rotation.X, t.Rotation.Y, value); OnPropertyChanged(); } } }

    // The datum placed last stays editable by typed location, so a point can be nudged without a gizmo.
    SelItem? DatumLast => Session.Selection.Count == 1 && Session.Selection[0].Kind == SelKind.Datum ? Session.Selection[0] : Session.LastModelDatum();
    Vec3 DatumLocation => DatumLast is { } it && Session.DatumOf(it) is { } d ? d.Frame.Origin : Vec3.Zero;
    public bool DatumHasLast => DatumLast is { } it && Session.DatumOf(it) != null;
    public double DatumLocationX { get => DatumLocation.X; set => MoveLastDatum(new(value, DatumLocation.Y, DatumLocation.Z)); }
    public double DatumLocationY { get => DatumLocation.Y; set => MoveLastDatum(new(DatumLocation.X, value, DatumLocation.Z)); }
    public double DatumLocationZ { get => DatumLocation.Z; set => MoveLastDatum(new(DatumLocation.X, DatumLocation.Y, value)); }

    /// <summary>Take possession of the selected (or last placed) datum: bind the gizmo to it and show the full Gizmo panel, whatever the datum's kind.</summary>
    public void EditDatumWithGizmoPanel()
    {
        if (DatumLast is not { } it || Session.DatumOf(it) == null) return;
        Session.ShowDatumOrientation(it);
        OpenGizmoPanel();
    }

    void MoveLastDatum(Vec3 origin)
    {
        if (DatumLast is not { } it || Session.DatumOf(it) is not { } d) return;
        Session.MoveDatumTo(it, origin);
        RefreshDatumPanel();
    }

    void RefreshDatumPanel()
    {
        foreach (var name in new[] { nameof(DatumPanelTitle), nameof(DatumHasOrientation), nameof(DatumOrientations), nameof(DatumHint), nameof(DatumOrientation),
                     nameof(DatumRotationX), nameof(DatumRotationY), nameof(DatumRotationZ), nameof(DatumHasLast), nameof(DatumLocationX), nameof(DatumLocationY), nameof(DatumLocationZ) })
            OnPropertyChanged(name);
    }
}
