using Avalonia;
using Avalonia.Controls;

namespace Prism.Controls;

/// <summary>One shared visual vocabulary for ordered inputs across modeling and studio panels.</summary>
public partial class PanelStepHeader : UserControl
{
    public static readonly StyledProperty<string> StepProperty = AvaloniaProperty.Register<PanelStepHeader, string>(nameof(Step), "");
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<PanelStepHeader, string>(nameof(Title), "");
    public string Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public PanelStepHeader() => InitializeComponent();
}
