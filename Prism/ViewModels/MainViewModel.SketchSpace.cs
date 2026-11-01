using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    public void StartFluidSketch()
    {
        bool nurbs = SelectedTab?.Title == "NURBS";
        if (Session.ActiveCommand != null) return;
        if (Session.Sketch == null)
        {
            var existing = nurbs ? Session.Objects.LastOrDefault(o => o.Kind == ObjKind.Sketch && (o.SketchData?.SharedNurbsCurves == true || o.Name.StartsWith("NURBS Curves", StringComparison.Ordinal)) && o.SketchData != null) : null;
            if (existing != null) { if (!Session.EditSketch(existing)) return; }
            else { Session.EnterSketch(Plane3.XY); if (nurbs && Session.Sketch is { } created) { created.SuggestedName = "NURBS Curves"; created.Model.SharedNurbsCurves = true; created.FreeEditing = true; } }
            if (existing == null) Session.Sketch?.SetDrawingMode(Session, flat: nurbs);
        }
        if (Session.Sketch is { } sketch) { sketch.SetTool(Session, new SplineTool(false)); }
    }

    [ObservableProperty] public partial double SketchDatumPlaneOffset { get; set; }
    public ObservableCollection<string> SketchPlaneChoices { get; } = [];
    public string[] SketchGizmoOperations { get; } = ["Move (W)", "Rotate (R)", "Scale (S)"];
    public string[] SketchGizmoPlanes { get; } = ["XY", "XZ", "YZ"];
    public string[] SketchGizmoAxes { get; } = ["X", "Y", "Z"];
    public ObservableCollection<string> SketchDatumChoices { get; } = [];
    [ObservableProperty] public partial int SketchDatumChoice { get; set; }
    [ObservableProperty] public partial string SketchDatumName { get; set; } = "";
    [ObservableProperty] public partial int SketchGizmoAxis { get; set; } = 2;
    partial void OnSketchGizmoAxisChanged(int value) { if (!_refreshSketchSpace && Session.Sketch is { } sk) { sk.SelectedGizmoAxis = Math.Clamp(value,0,2); Session.Invalidate(); } }
    partial void OnSketchDatumChoiceChanged(int value)
    {
        if (!_refreshSketchSpace && Session.Sketch is { } sk)
            sk.UseDatum(Session,value > 0 && value <= sk.Datums.Count ? sk.Datums[value-1].Id : null);
    }
    [ObservableProperty] public partial int SketchGizmoPlane { get; set; }
    [ObservableProperty] public partial bool SketchGizmoFollows { get; set; } = true;
    [ObservableProperty] public partial string SketchNormalLabel { get; set; } = "Look normal to XY (N)";
    partial void OnSketchGizmoPlaneChanged(int value) { if (!_refreshSketchSpace) Session.Sketch?.SelectGizmoPlane(Session, value); }
    partial void OnSketchGizmoFollowsChanged(bool value) { if (!_refreshSketchSpace && Session.Sketch is { } sk) sk.GizmoFollowsSketch = value; }
    public string[] SketchConstraintChoices { get; } = ["Horizontal", "Vertical", "Coincident", "Parallel", "Perpendicular", "Tangent", "Equal", "Midpoint", "Concentric", "Collinear", "Symmetric", "Fix"];
    [ObservableProperty] public partial int SketchPlaneChoice { get; set; }
    [ObservableProperty] public partial int SketchGizmoOperation { get; set; }
    [ObservableProperty] public partial int SketchConstraintChoice { get; set; }
    [ObservableProperty] public partial bool SketchGizmoEditing { get; set; }
    [ObservableProperty] public partial bool SketchGeometryEditing { get; set; }
    [ObservableProperty] public partial bool SketchFreeEditing { get; set; }
    partial void OnSketchFreeEditingChanged(bool value) { if (!_refreshSketchSpace && Session.Sketch is { } sk) { sk.FreeEditing = value; Session.RaiseState(); } }
    [ObservableProperty] public partial bool SketchGizmoTargetSetup { get; set; }
    [ObservableProperty] public partial bool Sketch3DEnabled { get; set; }
    public bool SketchDrawFlat { get => !Sketch3DEnabled; set { if (value) Sketch3DEnabled = false; } }
    partial void OnSketch3DEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(SketchDrawFlat));
        if (!_refreshSketchSpace) Session.Sketch?.SetDrawingMode(Session, flat: !value);
    }
    [ObservableProperty] public partial bool SketchSnapOtherCurves { get; set; }
    [ObservableProperty] public partial bool SketchProjectOtherCurves { get; set; }
    [ObservableProperty] public partial string SketchSpaceStatus { get; set; } = "";
    [ObservableProperty] public partial string SketchTransformHelp { get; set; } = "";
    [ObservableProperty] public partial string SketchTransformTitle { get; set; } = "Gizmo transform";
    [ObservableProperty] public partial string SketchResetLabel { get; set; } = "Reset gizmo: location 0 · rotation 0 · scale 1";
    [ObservableProperty] public partial string SketchOriginX { get; set; } = "0";
    [ObservableProperty] public partial string SketchOriginY { get; set; } = "0";
    [ObservableProperty] public partial string SketchOriginZ { get; set; } = "0";
    [ObservableProperty] public partial string SketchRotateX { get; set; } = "0";
    [ObservableProperty] public partial string SketchRotateY { get; set; } = "0";
    [ObservableProperty] public partial string SketchRotateZ { get; set; } = "0";
    [ObservableProperty] public partial string SketchDisplaySize { get; set; } = "1";
    bool _refreshSketchSpace;

    partial void OnSketchPlaneChoiceChanged(int value) { if (!_refreshSketchSpace && Session.Sketch is { GizmoEditing:true } sk) sk.SelectPlane(Session, value); }
    partial void OnSketchGizmoOperationChanged(int value)
    {
        if (!_refreshSketchSpace && Session.Sketch is { } sk && value is >= 0 and <= 2)
        { sk.GizmoOperation = (Core.SketchGizmoOperation)value; Session.Invalidate(); }
    }
    partial void OnSketchGizmoEditingChanged(bool value) { if (!_refreshSketchSpace) Session.Sketch?.SetGizmoEditing(Session, value); }
    partial void OnSketchSnapOtherCurvesChanged(bool value) { if (!_refreshSketchSpace && Session.Sketch is { } sk) { sk.SnapOtherCurves = value; Session.Invalidate(); } }
    partial void OnSketchProjectOtherCurvesChanged(bool value) { if (!_refreshSketchSpace && Session.Sketch is { } sk) { sk.ProjectOtherCurves = value; Session.Invalidate(); } }

    void RefreshSketchSpace()
    {
        if (Session.Sketch is not { } sk) return;
        _refreshSketchSpace = true;
        try
        {
            if (SketchPlaneChoices.Count != sk.DrawingPlanes.Count)
            {
                SketchPlaneChoices.Clear();
                foreach (var section in sk.DrawingPlanes) SketchPlaneChoices.Add(section.Name);
            }
            SketchPlaneChoice = sk.ActivePlane; SketchGizmoOperation = (int)sk.GizmoOperation;
            Sketch3DEnabled = !sk.IsDrawingFlat;
            SketchGizmoPlane = sk.GizmoPlaneIndex; SketchGizmoFollows = sk.GizmoFollowsSketch;
            SketchGizmoAxis = sk.SelectedGizmoAxis;
            bool editingSelection=sk.GeometryEditing&&!sk.GizmoEditing&&sk.HasGeometrySelection;
            SketchGeometryEditing=sk.GeometryEditing;SketchFreeEditing=sk.FreeEditing;
            SketchGizmoTargetSetup=sk.GeometryEditing&&sk.GizmoEditing;
            SketchTransformTitle=SketchGizmoTargetSetup?"Gizmo setup":editingSelection?"Selection transform":"Gizmo transform";
            SketchResetLabel=editingSelection?"Reset selection: location 0 · rotation 0 · scale 1":"Reset gizmo: location 0 · rotation 0 · scale 1";
            SketchTransformHelp=SketchGizmoTargetSetup?"These fields reposition or orient only the gizmo. Select geometry and confirm its target with Enter before transforming it.":editingSelection?"Location, rotation and scale transform the selected geometry about the active datum. Constraints remain satisfied.":"Location and rotation position the drawing planes. Scale changes only the plane display size. Existing geometry keeps its position and dimensions.";
            var datums = new[] { sk.Is3DEnabled ? "Live gizmo" : "No reference" }.Concat(sk.Datums.Select(d => $"{d.Kind}: {d.Name}")).ToList();
            if (!SketchDatumChoices.SequenceEqual(datums)) { SketchDatumChoices.Clear(); foreach(var datum in datums)SketchDatumChoices.Add(datum); }
            SketchDatumChoice = sk.Datums.Select((d,i)=>(d,i)).Where(p=>p.d.Id==sk.ActiveDatumId).Select(p=>p.i+1).FirstOrDefault();
            SketchNormalLabel = $"Look normal to {sk.GizmoPlaneName} (N)";
            SketchGizmoEditing = sk.GizmoEditing; SketchSnapOtherCurves = sk.SnapOtherCurves; SketchProjectOtherCurves = sk.ProjectOtherCurves;
            string F(double value) => (Math.Abs(value) < .00005 ? 0 : value).ToString("0.####", CultureInfo.InvariantCulture);
            SketchOriginX = F(sk.GizmoFrame.Origin.X); SketchOriginY = F(sk.GizmoFrame.Origin.Y); SketchOriginZ = F(sk.GizmoFrame.Origin.Z); SketchDisplaySize = F(editingSelection?sk.GeometryScale:sk.GizmoSize);
            var rotation = sk.GizmoRotationDegrees;
            SketchRotateX = F(rotation.X); SketchRotateY = F(rotation.Y); SketchRotateZ = F(rotation.Z);
            SketchSpaceStatus = SketchGizmoTargetSetup ? "Gizmo setup: select points or curves (Shift adds). Enter locks the gizmo at the selection center. Reorienting its handles leaves geometry unchanged." : sk.GeometryEditing ? (sk.GizmoTargetConfirmed ? "Target locked. Arrows, rings and scale now transform geometry. Press G to reposition or reorient only the gizmo." : "Drag corner points to reshape, or box-select a shape and drag an edge to move it. G sets up the gizmo; Enter confirms its target.") : sk.GizmoEditing ? "Drag arrows to move the gizmo, rings to rotate, or the corner square to scale the planes." : "Hover an axis to draw along it. Press G to choose a drawing plane.";
        }
        finally { _refreshSketchSpace = false; }
    }

    bool TrySketchVector(string x, string y, string z, out Vec3 vector)
    {
        bool Number(string text, out double value) => double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        if (Number(x, out double a) && Number(y, out double b) && Number(z, out double c)) { vector = new(a, b, c); return true; }
        vector = default; Session.Say("Enter finite numbers for X, Y and Z.", true); return false;
    }

    public void SketchSpaceAction(string action)
    {
        if (Session.Sketch is not { } sk) return;
        if (!sk.Is3DEnabled && !sk.PlanarGizmoActive && action is "gizmo" or "xy" or "xz" or "yz" or "new" or "origin" or "rotate" or "size" or "axis-free")
        { Session.Say("Enable 3D sketch to use spatial drawing controls."); return; }
        if (action == "new" && !sk.GizmoEditing) { Session.Say("Press G to edit the gizmo before adding a drawing plane."); return; }
        switch (action)
        {
            case "gizmo": Session.Key("SketchGizmo"); break;
            case "confirm-target": sk.ConfirmGizmoTarget(Session); break;
            case "edit": sk.ToggleGeometryEditing(Session); break;
            case "select": sk.SetTool(Session, new SelectTool()); break;
            case "spline-points": sk.SetTool(Session, new SketchGeometryEditTool(1)); break;
            case "normal": sk.LookNormal(Session); break;
            case "xy": sk.PresetPlane(Session, 0); break;
            case "xz": sk.PresetPlane(Session, 1); break;
            case "yz": sk.PresetPlane(Session, 2); break;
            case "new": sk.SetDrawingPlane(Session, sk.Plane, forceNew: true); break;
            case "axis-free": sk.ReleaseDrawingAxis(Session); break;
            case "axis": sk.LockSelectedGizmoAxis(Session); break;
            case "save-plane": sk.SaveDatum(Session,SketchDatumKind.Plane,SketchDatumName,normalOffset:SketchDatumPlaneOffset); SketchDatumName=""; break;
            case "save-gizmo": sk.SaveDatum(Session,SketchDatumKind.Gizmo,SketchDatumName); SketchDatumName=""; break;
            case "save-axis": sk.SaveDatum(Session,SketchDatumKind.Axis,SketchDatumName,sk.SelectedGizmoAxis); SketchDatumName=""; break;
            case "save-point": sk.SaveDatum(Session,SketchDatumKind.Point,SketchDatumName); SketchDatumName=""; break;
            case "update-datum": sk.UpdateDatum(Session); break;
            case "origin": if (TrySketchVector(SketchOriginX, SketchOriginY, SketchOriginZ, out var origin)) sk.MoveDrawingPlane(Session, origin); break;
            case "rotate": if (TrySketchVector(SketchRotateX, SketchRotateY, SketchRotateZ, out var degrees)) sk.SetGizmoTransform(Session, sk.GizmoFrame.Origin, degrees); break;
            case "reset-location": sk.ResetGizmo(Session, location: true, rotation: false, scale: false); break;
            case "reset-rotation": sk.ResetGizmo(Session, location: false, rotation: true, scale: false); break;
            case "reset-scale": sk.ResetGizmo(Session, location: false, rotation: false, scale: true); break;
            case "reset-gizmo": sk.ResetGizmo(Session); break;
            case "size":
                if (double.TryParse(SketchDisplaySize, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)) sk.SetGizmoSize(Session, size);
                else Session.Say("Enter a plane display size from 0.25 to 8.", true);
                break;
            case "line": sk.SetTool(Session, new LineTool()); break;
            case "rectangle": sk.SetTool(Session, new RectTool(RectMode.TwoCorner)); break;
            case "circle": sk.SetTool(Session, new EllipseTool(EllipseMode.Center,circleLock:true)); break;
            case "spline": sk.SetTool(Session, new SplineTool(false)); break;
            case "dimension": sk.SetTool(Session, new DimensionTool(DimMode.Smart)); break;
            case "constrain":
                CType[] types = [CType.Horizontal, CType.Vertical, CType.Coincident, CType.Parallel, CType.Perpendicular, CType.Tangent, CType.Equal, CType.Midpoint, CType.Concentric, CType.Collinear, CType.Symmetric, CType.Fixed];
                sk.SetTool(Session, new ConstraintTool(types[Math.Clamp(SketchConstraintChoice, 0, types.Length - 1)])); break;
        }
        RefreshSketchSpace();
    }
}
