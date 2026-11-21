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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasExample))] public partial string Icon { get; set; } = "";
    /// <summary>A dropdown: clicking opens a menu of variants and the button shows the last one chosen.</summary>
    public List<RibbonButton>? Menu { get; init; }
    public bool HasMenu => Menu != null;
    /// <summary>A split button: pressing the icon runs the variant last used, pressing the label (or right-clicking) opens the list of variants.</summary>
    // Every button with a list behaves the same (owner rule 2026-11-20): clicking it runs the action picked last (or a default, the first one that
    // can run), the arrow opens the list, and picking from the list runs that action and makes it the one the button runs next.
    readonly bool _split;
    public bool IsSplit { get => _split || Menu is { Count: > 0 }; init => _split = value; }
    /// <summary>Where a button's last pick is remembered between sessions (set by the app); null in tests.</summary>
    public static Action<RibbonButton, RibbonButton>? RememberPick { get; set; }
    /// <summary>The last pick saved for a button's id (set by the app), used to start a plain list button on what was used last time.</summary>
    public static Func<RibbonButton, string?>? RecallPick { get; set; }
    /// <summary>The action a click runs: the last pick, or the first item that can run.</summary>
    public RibbonButton? Active
    {
        get
        {
            if (Menu is not { Count: > 0 } menu) return null;
            if (Current == null && RecallPick?.Invoke(this) is { } saved) Current = menu.FirstOrDefault(v => v.Label.Replace("\n", " ") == saved);
            if (Current != null) return Current;
            foreach (var v in menu) { v.Refresh(); if (v.IsEnabled) return v; }
            return menu[0];
        }
    }
    /// <summary>Picking from the list: the button runs it now and runs it again on the next click.</summary>
    public void Pick(RibbonButton variant)
    {
        if (!_split) { Current = variant; }   // a list button keeps its own name; a split button shows the tool it will run
        else { Label = variant.Label.Replace("\n", " "); Icon = variant.Icon; Current = variant; }
        Picked?.Invoke(variant);
        RememberPick?.Invoke(this, variant);
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasExample)), NotifyPropertyChangedFor(nameof(ToolHelpIcon))]
    public partial RibbonButton? Current { get; set; }
    public Action<RibbonButton>? Picked { get; init; }
    /// <summary>Stable identity (icon + label as shipped) so a customised ribbon can refer to a command.</summary>
    public string Id { get; set; } = "";
    /// <summary>The command behind this button is marked <c>[Legacy]</c>: it is retired from the interface, so the ribbon leaves the button out.</summary>
    public bool IsLegacy { get; set; }
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
    string? _exampleIcon; bool _exampleResolved; Func<string?>? _exampleResolver;
    /// <summary>Finds the help topic on first use, so building the ribbon does not construct every command.</summary>
    public Func<string?>? ExampleResolver { get => _exampleResolver; set { _exampleResolver = value; _exampleResolved = false; } }
    public string? ExampleIcon
    {
        get { if (!_exampleResolved && _exampleResolver != null) { _exampleResolved = true; _exampleIcon ??= _exampleResolver(); } return _exampleIcon; }
        set { _exampleIcon = value; }
    }
    public string? ToolHelpIcon => IsSplit ? Current?.ExampleIcon : ExampleIcon;
    public bool HasExample => ToolHelpIcon != null;
    /// <summary>The ribbon this button was built on, used to link it to its section of the website's tool reference.</summary>
    public string TabTitle { get; set; } = "";
    public string DocumentationTab => IsSplit && Current != null ? Current.DocumentationTab : TabTitle;
    public string DocumentationLabel => IsSplit && Current != null ? Current.DocumentationLabel : Label.Replace("\n", " ");
    public string DocumentationId => IsSplit && Current != null ? Current.DocumentationId
        : Id.Length > 0 ? Id : Icon + "|" + Label.Replace("\n", " ");

    [ObservableProperty] public partial bool IsChecked { get; set; }
    [ObservableProperty] public partial bool IsEnabled { get; set; } = true;

    public bool FollowChecked { get; set; }

    public void Refresh()
    {
        if (CanExecute != null) IsEnabled = CanExecute();
        if (GetChecked != null) IsChecked = GetChecked();
        if (FollowChecked && Menu != null)
        {
            bool any = false;
            foreach (var v in Menu)
            {
                v.Refresh();
                if (any || !v.IsChecked) continue;
                Current = v; Label = v.Label.Replace("\n", " "); Icon = v.Icon; any = true;
            }
            IsChecked = any;
        }
    }

    public void Invoke()
    {
        if (!IsEnabled) return;
        if (IsSplit && Active is { } run) { run.Refresh(); run.Invoke(); } else Execute();
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
