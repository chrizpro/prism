using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Prism.Commands;

/// <summary>A presentation group; parameter and recipe ordering remain unchanged.</summary>
public sealed partial class CommandSection(string title) : ObservableObject
{
    public string Title { get; } = title;
    public ObservableCollection<object> Items { get; } = [];
    [ObservableProperty] public partial string Number { get; set; } = "";
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty] public partial bool IsExpanded { get; set; } = title != "Advanced";
}
