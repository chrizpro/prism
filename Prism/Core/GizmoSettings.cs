using System;

namespace Prism.Core;

/// <summary>Application-wide display preferences; these never change document geometry.</summary>
public sealed class GizmoSettings
{
    public int DisplayMode { get; set; } // Active tool, Combined, Custom
    public bool Move { get; set; } = true;
    public bool Rotate { get; set; } = true;
    public bool Scale { get; set; } = true;
    public bool PlaneHandles { get; set; } = true;
    public bool Labels { get; set; } = true;
    public bool WorkPlane { get; set; } = true;
    public bool AxisX {get;set;}=true;
    public bool AxisY {get;set;}=true;
    public bool AxisZ {get;set;}=true;
    public bool AxisVisible(int axis)=>axis==0?AxisX:axis==1?AxisY:AxisZ;
    public double ScreenSize { get; set; } = 96;
    public double LineWidth { get; set; } = 2.5;
    public double HitTolerance { get; set; } = 9;
    /// <summary>How long the pointer rests on a plane square before the plane is offered for locking (0.5 to 2 seconds).</summary>
    public double PlaneCueDelay { get; set; } = 1.5;
    public bool Snap { get; set; }
    public double MoveStep { get; set; } = 1;
    public double AngleStep { get; set; } = 15;
    public double ScaleStep { get; set; } = 10;
    public static GizmoSettings Current => AppSettings.Current.Gizmo ??= new();
    public bool Allows(int operation, int active) => DisplayMode switch
    {
        1 => true,
        2 => operation == 0 ? Move : operation == 1 ? Rotate : Scale,
        _ => operation == active,
    };
    public double Pixels => double.IsFinite(ScreenSize) ? Math.Clamp(ScreenSize, 56, 180) : 96;
    public float Stroke => double.IsFinite(LineWidth) ? (float)Math.Clamp(LineWidth, 1, 5) : 2.5f;
    public double PlaneCueSeconds => double.IsFinite(PlaneCueDelay) ? Math.Clamp(PlaneCueDelay, 0.5, 2) : 1.5;
    public double Tolerance => double.IsFinite(HitTolerance) ? Math.Clamp(HitTolerance, 4, 18) : 9;
}
