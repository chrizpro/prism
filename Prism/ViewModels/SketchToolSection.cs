using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Prism.ViewModels;

/// <summary>A stable presentation group shared by every sketch tool and mode.</summary>
public sealed partial class SketchToolSection(string title) : ObservableObject
{
    public string Title { get; } = title;
    public ObservableCollection<ToolControlRow> Controls { get; } = [];
    public ObservableCollection<ToolOptionItem> Options { get; } = [];
    [ObservableProperty] public partial string Number { get; set; } = "";
    [ObservableProperty] public partial string Guidance { get; set; } = "";
    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;
    public bool HasGuidance => Guidance.Length > 0;
    partial void OnGuidanceChanged(string value) => OnPropertyChanged(nameof(HasGuidance));
}
