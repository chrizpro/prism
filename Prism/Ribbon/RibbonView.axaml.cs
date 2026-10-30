using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Prism.ViewModels;

namespace Prism.Ribbon;

public partial class RibbonView : UserControl
{
    public RibbonView()
    {
        InitializeComponent();
        AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnRibbonPressed, RoutingStrategies.Tunnel, true);
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    void OnGroupScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        bool overflow = GroupScroll.Extent.Width > GroupScroll.Viewport.Width + 1;
        ScrollLeft.IsVisible = ScrollRight.IsVisible = overflow;
        ScrollLeft.IsEnabled = GroupScroll.Offset.X > 1;
        ScrollRight.IsEnabled = GroupScroll.Offset.X < GroupScroll.Extent.Width - GroupScroll.Viewport.Width - 1;
    }
    void ShiftGroups(double direction) => GroupScroll.Offset = new Avalonia.Vector(
        System.Math.Clamp(GroupScroll.Offset.X + direction * GroupScroll.Viewport.Width * .7, 0, System.Math.Max(0, GroupScroll.Extent.Width - GroupScroll.Viewport.Width)), 0);
    void OnScrollLeft(object? sender, RoutedEventArgs e) => ShiftGroups(-1);
    void OnScrollRight(object? sender, RoutedEventArgs e) => ShiftGroups(1);

    /// <summary>Tabs with many tools are wider than the window: the mouse wheel scrolls the ribbon sideways.</summary>
    void OnGroupsWheel(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv || sv.Extent.Width <= sv.Viewport.Width) return;
        double dx = -(e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X) * 80;
        sv.Offset = new Avalonia.Vector(System.Math.Clamp(sv.Offset.X + dx, 0, sv.Extent.Width - sv.Viewport.Width), 0);
        e.Handled = true;
    }

    void OnTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RibbonTab tab }) Vm?.SelectTab(tab);
    }

    /// <summary>Dropdown buttons open a menu of variants; picking one runs it and becomes the button's new face.</summary>
    double _pressX, _pressY, _pressH; bool _pressRight;
    Button? _pressedButton;

    void OnRibbonPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<Button>(true) is not { } c) return;
        _pressedButton = c;
        _pressX = e.GetPosition(c).X; _pressY = e.GetPosition(c).Y; _pressH = c.Bounds.Height; _pressRight = e.GetCurrentPoint(c).Properties.IsRightButtonPressed;
        // a split button: right-click opens its list straight away
        if (_pressRight && (c.DataContext as RibbonButton) is { IsSplit: true, IsEnabled: true }) { ShowMenu(c); e.Handled = true; }
    }

    void OnRibbonButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonButton rb } anchor) return;
        bool icon = !ReferenceEquals(_pressedButton, anchor) || (!_pressRight && (rb.IsSmall ? _pressX < 30 : _pressY < _pressH * 0.62));
        _pressedButton = null;
        if (!rb.HasMenu || (rb.IsSplit && icon)) rb.Invoke();
        else ShowMenu(anchor);
    }

    void ShowMenu(Control anchor)
    {
        if (anchor.DataContext is not RibbonButton { Menu: { } menu } rb) return;
        var items = new List<object>();
        foreach (var v in menu)
        {
            v.Refresh();
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new Prism.Controls.IconView { Icon = v.Icon, Width = 22, Height = 22 });
            row.Children.Add(new TextBlock { Text = v.Label.Replace("\n", " "), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var mi = new MenuItem { Header = row, IsEnabled = v.IsEnabled };
            ToolTip.SetTip(mi, v.Description);
            var variant = v;
            mi.Click += (_, _) =>
            {
                variant.Invoke();
                if (rb.IsSplit) { rb.Label = variant.Label.Replace("\n", " "); rb.Icon = variant.Icon; rb.Current = variant; rb.Picked?.Invoke(variant); }
                rb.Refresh();
            };
            items.Add(mi);
        }
        var flyout = new MenuFlyout { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.ShowAt(anchor);
    }

    void OnTabDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => Vm?.ToggleRibbon();
    void OnCollapseClick(object? sender, RoutedEventArgs e) => Vm?.ToggleRibbon();
}
