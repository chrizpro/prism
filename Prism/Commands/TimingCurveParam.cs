using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core.Assembly;

namespace Prism.Commands;

public sealed partial class TimingCurveParam : Param
{
    [ObservableProperty] public partial double X1 { get; set; } = .25;
    [ObservableProperty] public partial double Y1 { get; set; } = .1;
    [ObservableProperty] public partial double X2 { get; set; } = .25;
    [ObservableProperty] public partial double Y2 { get; set; } = 1;

    public AnimCurve Curve => new(X1, Y1, X2, Y2);
    public void Load(AnimCurve curve) { X1 = curve.X1; Y1 = curve.Y1; X2 = curve.X2; Y2 = curve.Y2; }
}
