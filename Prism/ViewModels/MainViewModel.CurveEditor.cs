using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;

namespace Prism.ViewModels;

public sealed record CurveEditorContext(string Subject, TimingCurveParam Curve, Action Apply);

public sealed partial class MainViewModel
{
    [ObservableProperty] public partial CurveEditorContext? CurveEditor { get; set; }
    [ObservableProperty] public partial bool ShowCurveEditor { get; set; }
    public void OpenCurveEditor(string subject, TimingCurveParam curve, Action apply)
    {
        CurveEditor = new(subject, curve, apply);
        OpenRightPanel("curve");
    }
    public void ApplyCurveEditor() => CurveEditor?.Apply();
}
