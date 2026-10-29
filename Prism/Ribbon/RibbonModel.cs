using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Prism.Ribbon;

public enum RibbonSize { Large, Small }

public abstract partial class RibbonItem : ObservableObject { }

/// <summary>A ribbon command button: large (icon above label) or small (icon beside label, three per column).</summary>
public sealed partial class RibbonButton : RibbonItem
{
    [ObservableProperty] public partial string Label { get; set; } = "";
    [ObservableProperty] public partial string Icon { get; set; } = "";
    /// <summary>A dropdown: clicking opens a menu of variants and the button shows the last one chosen.</summary>
    public List<RibbonButton>? Menu { get; init; }
    public bool HasMenu => Menu != null;
    /// <summary>A split button: pressing the icon runs the variant last used, pressing the label (or right-clicking) opens the list of variants.</summary>
    public bool IsSplit { get; init; }
    public RibbonButton? Current { get; set; }
    public Action<RibbonButton>? Picked { get; init; }
    /// <summary>Stable identity (icon + label as shipped) so a customised ribbon can refer to a command.</summary>
    public string Id { get; set; } = "";
    public string Caption => HasMenu ? Label + " ▾" : Label;
    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(Caption));
    public RibbonSize Size { get; init; } = RibbonSize.Large;
    public string Description { get; init; } = "";
    public string Shortcut { get; init; } = "";
    public bool IsToggle { get; init; }
    public Action Execute { get; init; } = () => { };
    public Func<bool>? CanExecute { get; init; }
    /// <summary>For toggle buttons: reads the current state.</summary>
    public Func<bool>? GetChecked { get; init; }
    public bool IsLarge => Size == RibbonSize.Large;
    public bool IsSmall => Size == RibbonSize.Small;
    public string ToolTipTitle => Shortcut.Length > 0 ? $"{Label.Replace("\n", " ")}  ({Shortcut})" : Label.Replace("\n", " ");

    [ObservableProperty] public partial bool IsChecked { get; set; }
    [ObservableProperty] public partial bool IsEnabled { get; set; } = true;

    public void Refresh()
    {
        if (CanExecute != null) IsEnabled = CanExecute();
        if (GetChecked != null) IsChecked = GetChecked();
    }

    public void Invoke()
    {
        if (!IsEnabled) return;
        if (IsSplit && Current != null) { Current.Refresh(); Current.Invoke(); } else Execute();
    }
    System.Windows.Input.ICommand? _click;
    public System.Windows.Input.ICommand ClickCommand => _click ??= new CommunityToolkit.Mvvm.Input.RelayCommand(Invoke);
}

public sealed class RibbonGroup
{
    public required string Title { get; init; }
    public List<RibbonItem> Items { get; init; } = [];
}

public sealed partial class RibbonTab : ObservableObject
{
    public required string Title { get; init; }
    public List<RibbonGroup> Groups { get; init; } = [];
    public bool IsFile { get; init; }
    /// <summary>A tab the user made themselves (it shows in every workspace).</summary>
    public bool IsCustom { get; init; }
    /// <summary>The user chose to hide this tab (Settings, Ribbon).</summary>
    public bool UserHidden { get; set; }
    /// <summary>Contextual tabs (e.g. Sketch) get a coloured header and only appear when relevant.</summary>
    public string? ContextLabel { get; init; }
    public string AccentColor { get; init; } = "#1F6FEB";
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    public bool IsContextual => ContextLabel != null;
    public Avalonia.Media.IBrush ContextBrush => IsContextual ? Avalonia.Media.Brush.Parse(AccentColor) : Avalonia.Media.Brushes.Transparent;

    /// <summary>Every button, including the variants inside dropdown menus.</summary>
    public IEnumerable<RibbonButton> Buttons => Groups.SelectMany(g => g.Items).OfType<RibbonButton>().SelectMany(b => b.Menu == null ? new[] { b } : new[] { b }.Concat(b.Menu));
}
