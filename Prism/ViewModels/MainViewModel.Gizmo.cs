using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Prism.Core;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    public string[] GizmoDisplayModes { get; } = ["Active tool only", "Combined handles", "Custom visibility"];
    public int GizmoDisplayMode { get => GizmoSettings.Current.DisplayMode; set => ChangeGizmo(g => g.DisplayMode = Math.Clamp(value, 0, 2)); }
    public bool GizmoCustomVisibility => GizmoDisplayMode == 2;
    public bool GizmoShowMove { get => GizmoSettings.Current.Move; set => ChangeGizmo(g => g.Move = value); }
    public bool GizmoShowRotate { get => GizmoSettings.Current.Rotate; set => ChangeGizmo(g => g.Rotate = value); }
    public bool GizmoShowScale { get => GizmoSettings.Current.Scale; set => ChangeGizmo(g => g.Scale = value); }
    public bool GizmoShowPlanes { get => GizmoSettings.Current.PlaneHandles; set => ChangeGizmo(g => g.PlaneHandles = value); }
    public bool GizmoShowLabels { get => GizmoSettings.Current.Labels; set => ChangeGizmo(g => g.Labels = value); }
    public bool GizmoShowWorkPlane { get => GizmoSettings.Current.WorkPlane; set => ChangeGizmo(g => g.WorkPlane = value); }
    public double GizmoScreenSize { get => GizmoSettings.Current.Pixels; set { if (double.IsFinite(value)) ChangeGizmo(g => g.ScreenSize = Math.Clamp(value, 56, 180)); } }
    public double GizmoLineWidth { get => GizmoSettings.Current.Stroke; set { if (double.IsFinite(value)) ChangeGizmo(g => g.LineWidth = Math.Clamp(value, 1, 5)); } }
    public double GizmoHitTolerance { get => GizmoSettings.Current.Tolerance; set { if (double.IsFinite(value)) ChangeGizmo(g => g.HitTolerance = Math.Clamp(value, 4, 18)); } }
    public bool GizmoSnapping { get => GizmoSettings.Current.Snap; set => ChangeGizmo(g => g.Snap = value); }
    public double GizmoMoveStep { get => GizmoSettings.Current.MoveStep; set { if (double.IsFinite(value)) ChangeGizmo(g => g.MoveStep = Math.Clamp(value, .001, 10000)); } }
    public double GizmoAngleStep { get => GizmoSettings.Current.AngleStep; set { if (double.IsFinite(value)) ChangeGizmo(g => g.AngleStep = Math.Clamp(value, .1, 180)); } }
    public double GizmoScaleStep { get => GizmoSettings.Current.ScaleStep; set { if (double.IsFinite(value)) ChangeGizmo(g => g.ScaleStep = Math.Clamp(value, .1, 100)); } }

    void ChangeGizmo(Action<GizmoSettings> change, [CallerMemberName] string property = "")
    {
        var settings = GizmoSettings.Current; string before = JsonSerializer.Serialize(settings);
        change(settings);
        if (before == JsonSerializer.Serialize(settings)) return;
        AppSettings.Save(notify: false);
        OnPropertyChanged(property); OnPropertyChanged(nameof(GizmoCustomVisibility));
        Session.Invalidate();
    }
    public void ResetGizmoDisplay()
    {
        AppSettings.Current.Gizmo = new(); AppSettings.Save(notify: false);
        foreach (string name in new[] { nameof(GizmoDisplayMode), nameof(GizmoCustomVisibility), nameof(GizmoShowMove), nameof(GizmoShowRotate), nameof(GizmoShowScale), nameof(GizmoShowPlanes), nameof(GizmoShowLabels), nameof(GizmoShowWorkPlane), nameof(GizmoScreenSize), nameof(GizmoLineWidth), nameof(GizmoHitTolerance), nameof(GizmoSnapping), nameof(GizmoMoveStep), nameof(GizmoAngleStep), nameof(GizmoScaleStep) }) OnPropertyChanged(name);
        Session.Invalidate();
    }
}
