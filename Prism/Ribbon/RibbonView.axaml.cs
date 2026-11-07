using System.Collections.Generic;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Automation;
using Prism.Help;
using Prism.Controls;
using Prism.ViewModels;

namespace Prism.Ribbon;

public partial class RibbonView : UserControl
{
    readonly DispatcherTimer _showHelp = new() { Interval = TimeSpan.FromMilliseconds(450) };
    readonly DispatcherTimer _hideHelp = new() { Interval = TimeSpan.FromMilliseconds(700) };
    Control? _helpAnchor;
    RibbonButton? _helpTool;
    MenuFlyout? _activeToolMenu;
    public RibbonView()
    {
        InitializeComponent();
        AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnRibbonPressed, RoutingStrategies.Tunnel, true);
        _showHelp.Tick += (_, _) => { _showHelp.Stop(); ShowHoverHelp(); };
        _hideHelp.Tick += (_, _) => { _hideHelp.Stop(); CloseHoverHelp(); };
        DetachedFromVisualTree += (_, _) => CloseHoverHelp();
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    void OnToolEntered(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (Vm is { } vm && sender is Control { DataContext: RibbonButton rb })
        {
            BeginToolHover((Control)sender, rb);
        }
    }
    void BeginToolHover(Control anchor, RibbonButton tool)
    {
        if (Vm is not { } vm) return;
        vm.HoveredToolHelpIcon = tool.ToolHelpIcon;
        _hideHelp.Stop();
        _showHelp.Stop();
        _helpAnchor = anchor;
        _helpTool = tool;
        _showHelp.Start();
    }
    void OnToolExited(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (Vm is { } vm) vm.HoveredToolHelpIcon = null;
        _showHelp.Stop();
        _hideHelp.Start();
    }

    void CloseHoverHelp()
    {
        _showHelp.Stop(); _hideHelp.Stop();
        ToolHoverPopup.IsOpen = false;
        _helpAnchor = null; _helpTool = null;
    }

    void ShowHoverHelp()
    {
        if (_helpAnchor is not { } anchor || _helpTool is not { } tool || !anchor.IsAttachedToVisualTree()) return;
        var body = new StackPanel { Width = 330, Spacing = 8 };
        body.Children.Add(new TextBlock { Text = tool.ToolTipTitle, FontWeight = FontWeight.SemiBold });
        var topic = ToolHelp.Find(tool.ToolHelpIcon);
        body.Children.Add(new TextBlock { Text = topic?.Purpose ?? tool.Description, TextWrapping = TextWrapping.Wrap });
        if (topic != null)
        {
            // App actions and view modes (files, scripts, camera presets) are explained in words only.
            if (!topic.NoPreview)
            try
            {
                var example = ToolHelp.Example(topic);
                var images = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
                var before = new StackPanel { Spacing = 5 };
                before.Children.Add(new GeometryPreview { Source = example.Before, Height = 100 });
                before.Children.Add(new TextBlock { Text = topic.BeforeLabel, FontSize = 11, TextWrapping = TextWrapping.Wrap });
                var after = new StackPanel { Spacing = 5 };
                after.Children.Add(new GeometryPreview { Source = example.After, Height = 100 });
                after.Children.Add(new TextBlock { Text = topic.AfterLabel, FontSize = 11, TextWrapping = TextWrapping.Wrap });
                images.Children.Add(before); Grid.SetColumn(after, 1); images.Children.Add(after);
                body.Children.Add(images);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { body.Children.Add(new TextBlock { Text = "Example image unavailable.", TextWrapping = TextWrapping.Wrap }); }
            if (topic.Steps.Length > 0 && topic.Steps != topic.Purpose)
                body.Children.Add(new TextBlock { Text = topic.Steps, TextWrapping = TextWrapping.Wrap });
            if (topic.Tip.Length > 0)
                body.Children.Add(new TextBlock { Text = topic.Tip, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        }
        var notice = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap };
        var link = new Button
        {
            Content = new TextBlock { Text = "Learn More", TextDecorations = TextDecorations.Underline },
            Background = Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0), Padding = new Avalonia.Thickness(0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        link[!Button.ForegroundProperty] = new DynamicResourceExtension("Accent");
        link.Click += async (_, e) =>
        {
            e.Handled = true;
            var uri = ToolDocumentation.LinkFor(tool.DocumentationId);
            if (uri == null)
            {
                notice.Text = "Online tool guides are coming soon.";
                notice.IsVisible = true;
                return;
            }
            try
            {
                if (TopLevel.GetTopLevel(anchor) is { } top && await top.Launcher.LaunchUriAsync(uri)) CloseHoverHelp();
                else { notice.Text = "The guide could not be opened."; notice.IsVisible = true; }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { notice.Text = "The guide could not be opened."; notice.IsVisible = true; }
        };
        body.Children.Add(link);
        body.Children.Add(notice);
        var card = new Border
        {
            Child = body, Padding = new Avalonia.Thickness(12), CornerRadius = new Avalonia.CornerRadius(6),
            BorderThickness = new Avalonia.Thickness(1)
        };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBg");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("Line");
        card.PointerEntered += (_, _) => _hideHelp.Stop();
        card.PointerExited += (_, _) => _hideHelp.Start();
        ToolHoverPopup.IsOpen = false;
        ToolHoverPopup.PlacementTarget = anchor;
        ToolHoverPopup.Child = card;
        ToolHoverPopup.IsOpen = true;
    }

    public bool ShowExample(Control anchor, string? icon)
    {
        var tool = icon == null ? null : Vm?.Tabs.SelectMany(t => t.Buttons).FirstOrDefault(b => b.ExampleIcon == icon);
        if (tool == null) return false;
        return ShowHelp(anchor, tool);
    }

    /// <summary>Open the tool's help card on demand (command search, F1): the same card the ribbon shows on hover, with its steps and the Learn More link.</summary>
    public bool ShowHelp(Control anchor, RibbonButton tool)
    {
        _showHelp.Stop(); _hideHelp.Stop();
        _helpAnchor = anchor; _helpTool = tool;
        ShowHoverHelp();
        return true;
    }

    public bool DismissExample()
    {
        bool open = ToolHoverPopup.IsOpen;
        CloseHoverHelp();
        return open;
    }

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
        if (c.DataContext is RibbonButton) CloseHoverHelp();
        _pressedButton = c;
        _pressX = e.GetPosition(c).X; _pressY = e.GetPosition(c).Y; _pressH = c.Bounds.Height; _pressRight = e.GetCurrentPoint(c).Properties.IsRightButtonPressed;
        // a split button: right-click opens its list straight away
        if (_pressRight && (c.DataContext as RibbonButton) is { IsSplit: true, IsEnabled: true }) { ShowMenu(c); e.Handled = true; }
        else if (_pressRight && c.DataContext is RibbonButton { HasMenu: false, HasExample: true } tool)
        {
            var help = new MenuItem { Header = "Show tool example" };
            help.Click += (_, _) => ShowExample(c, tool.ToolHelpIcon);
            new MenuFlyout { ItemsSource = new[] { help } }.ShowAt(c);
            e.Handled = true;
        }
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
            // Keep the menu row pointer-accessible so unavailable commands can explain themselves.
            // The command remains disabled in the model and its click handler below enforces that.
            var mi = new MenuItem { Header = row, DataContext = v, Focusable = v.IsEnabled };
            if (!v.IsEnabled) { row.Opacity = .5; AutomationProperties.SetName(mi, v.Label.Replace("\n", " ") + ", unavailable"); }
            mi.PointerEntered += OnToolEntered;
            mi.PointerExited += OnToolExited;
            mi.KeyDown += (_, e) =>
            {
                if (e.Key != Avalonia.Input.Key.F1 || !v.HasExample) return;
                ShowExample(mi, v.ToolHelpIcon);
                e.Handled = true;
            };
            var variant = v;
            mi.Click += (_, _) =>
            {
                CloseHoverHelp();
                if (!variant.IsEnabled) return;
                variant.Invoke();
                if (rb.IsSplit) { rb.Label = variant.Label.Replace("\n", " "); rb.Icon = variant.Icon; rb.Current = variant; rb.Picked?.Invoke(variant); }
                rb.Refresh();
            };
            items.Add(mi);
        }
        var flyout = new MenuFlyout { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedLeft };
        _activeToolMenu = flyout;
        flyout.Closed += (_, _) =>
        {
            CloseHoverHelp();
            if (ReferenceEquals(_activeToolMenu, flyout)) _activeToolMenu = null;
        };
        flyout.ShowAt(anchor);
    }

    void OnTabDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => Vm?.ToggleRibbon();
    void OnCollapseClick(object? sender, RoutedEventArgs e) => Vm?.ToggleRibbon();
}
