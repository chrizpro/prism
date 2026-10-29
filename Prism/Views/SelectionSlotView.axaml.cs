using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Commands;
using Prism.Core;

namespace Prism.Views;

/// <summary>A bounded selection editor shared by all command tools.</summary>
public partial class SelectionSlotView : UserControl
{
    public sealed record SelectionRow(SelItem Item, string Name, string Icon, int Number, bool Ordered);
    SelectionSlot? _slot;
    SelItem? _highlighted, _dragItem;
    Point _dragStart;
    bool _syncing, _dragging;
    CadSession? _session;
    CommandPanel? Panel => this.FindAncestorOfType<CommandPanel>();
    CadSession? Session => _session ?? Panel?.Session;

    public SelectionSlotView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
    }

    void Detach()
    {
        ClearHighlight();
        if (_session != null) _session.PropertyChanged -= OnSessionChanged;
        _session = null;
        if (_slot == null) return;
        _slot.Items.CollectionChanged -= OnItemsChanged;
        _slot.PropertyChanged -= OnSlotChanged;
        _slot = null;
    }

    void Attach()
    {
        Detach();
        _slot = DataContext as SelectionSlot;
        if (_slot == null) return;
        _session = Panel?.Session;
        if (_session != null) _session.PropertyChanged += OnSessionChanged;
        _slot.Items.CollectionChanged += OnItemsChanged;
        _slot.PropertyChanged += OnSlotChanged;
        RefreshRows();
    }

    void OnSlotChanged(object? sender, PropertyChangedEventArgs e) => RefreshState();
    void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CadSession.Hover) || _syncing || _slot is not { IsExpanded: true } || Session?.Hover is not { } item || !_slot.Items.Contains(item)) return;
        _syncing = true;
        Rows.SelectedItem = Rows.Items.OfType<SelectionRow>().FirstOrDefault(row => row.Item == item);
        _syncing = false;
        RefreshState();
        if (Rows.SelectedItem != null) Rows.ScrollIntoView(Rows.SelectedItem);
    }
    void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var reveal = e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0 ? (SelItem?)e.NewItems[0] : null;
        RefreshRows(reveal);
    }

    void RefreshRows(SelItem? reveal = null)
    {
        if (_slot == null) return;
        var selected = reveal ?? (Rows.SelectedItem as SelectionRow)?.Item;
        _syncing = true;
        var rows = _slot.Items.Select((item, i) => new SelectionRow(item, Session?.Describe(item) ?? item.Kind.ToString(),
            item.Kind switch { SelKind.Face => "sel-face", SelKind.Edge => "curve-through", SelKind.Origin => "construction", _ => ObjectIcon(item) }, i + 1, _slot.Ordered)).ToList();
        Rows.ItemsSource = rows;
        Rows.SelectedItem = rows.FirstOrDefault(r => r.Item == selected);
        _syncing = false;
        if (_highlighted is { } highlighted && !_slot.Items.Contains(highlighted)) ClearHighlight();
        RefreshState();
        if (reveal != null && Rows.SelectedItem is { } row)
            Dispatcher.UIThread.Post(() => { if (Rows.Items.Contains(row)) Rows.ScrollIntoView(row); }, DispatcherPriority.Loaded);
    }

    string ObjectIcon(SelItem item) => Session?.Find(item.Obj)?.Kind switch
    {
        ObjKind.Solid => "box", ObjKind.Surface => "loft-surface", ObjKind.Curve => "curve-through", ObjKind.Plane => "construction", _ => "sketch",
    };

    void RefreshState()
    {
        if (_slot == null) return;
        ExpandButton.Content = _slot.IsExpanded ? "⌄" : "›";
        SelectionBorder.BorderBrush = this.FindResource(_slot.IsActive ? "Accent" : "FieldLine") as IBrush;
        PickButton.Content = !_slot.CanAdd ? "Selection limit reached" : _slot.Items.Count > 0 && !_slot.Multi ? "↺  Replace selection" : "+  Pick geometry";
        PickButton.IsEnabled = _slot.CanAdd;
        PickHint.Text = !_slot.CanAdd ? $"Maximum {_slot.MaxCount} selections." : _slot.IsActive ? "Picking in viewport…" : _slot.Optional ? "Optional" : "";
        PickHint.IsVisible = PickHint.Text.Length > 0;
        MoveUpButton.IsEnabled = _slot.Ordered && Rows.SelectedIndex > 0;
        MoveDownButton.IsEnabled = _slot.Ordered && Rows.SelectedIndex >= 0 && Rows.SelectedIndex < _slot.Items.Count - 1;
    }

    void Activate()
    {
        if (_slot == null || Session?.ActiveCommand is not { } command) return;
        _slot.IsExpanded = true;
        foreach (var slot in command.Slots) slot.IsActive = slot == _slot;
        Session.Invalidate();
    }

    void OnStartPicking(object? sender, RoutedEventArgs e) { Activate(); e.Handled = true; }
    void OnToggleExpanded(object? sender, RoutedEventArgs e) { if (_slot != null) _slot.IsExpanded = !_slot.IsExpanded; e.Handled = true; }
    void OnClear(object? sender, RoutedEventArgs e) { _slot?.Items.Clear(); Activate(); e.Handled = true; }
    void OnRemove(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SelectionRow row) _slot?.Items.Remove(row.Item);
        e.Handled = true;
    }

    void Highlight(SelItem item)
    {
        if (Session is not { } session) return;
        _highlighted = item; session.Hover = item; session.Invalidate();
    }
    void ClearHighlight()
    {
        if (_highlighted != null && Session is { } session && session.Hover == _highlighted)
        { session.Hover = null; session.Invalidate(); }
        _highlighted = null;
    }
    void OnRowEnter(object? sender, PointerEventArgs e) { if ((sender as Control)?.DataContext is SelectionRow row) Highlight(row.Item); }
    void OnRowExit(object? sender, PointerEventArgs e) => ClearHighlight();
    void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        RefreshState();
        if (Rows.SelectedItem is SelectionRow row) Highlight(row.Item);
    }

    void MoveSelected(int offset)
    {
        if (_slot is not { Ordered: true } || Rows.SelectedItem is not SelectionRow row) return;
        int from = _slot.Items.IndexOf(row.Item), to = from + offset;
        if (from < 0 || to < 0 || to >= _slot.Items.Count) return;
        _slot.Items.Move(from, to);
        Reveal(row.Item);
    }
    void Reveal(SelItem item)
    {
        Rows.SelectedItem = Rows.Items.OfType<SelectionRow>().FirstOrDefault(r => r.Item == item);
        if (Rows.SelectedItem != null) Rows.ScrollIntoView(Rows.SelectedItem);
        Rows.Focus();
    }
    void OnMoveUp(object? sender, RoutedEventArgs e) { MoveSelected(-1); e.Handled = true; }
    void OnMoveDown(object? sender, RoutedEventArgs e) { MoveSelected(1); e.Handled = true; }
    void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down)
        { MoveSelected(e.Key == Key.Up ? -1 : 1); e.Handled = true; }
        else if (e.Key is Key.Delete or Key.Back && Rows.SelectedItem is SelectionRow row)
        { _slot?.Items.Remove(row.Item); e.Handled = true; }
    }

    void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_slot is not { Ordered: true } || (sender as Control)?.DataContext is not SelectionRow row || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragItem = row.Item; _dragStart = e.GetPosition(Rows); _dragging = false;
        Rows.SelectedItem = row;
        e.Pointer.Capture(sender as Control); e.Handled = true;
    }
    void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem == null) return;
        var delta = e.GetPosition(Rows) - _dragStart;
        if (delta.X * delta.X + delta.Y * delta.Y > 16) _dragging = true;
        e.Handled = true;
    }
    void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _dragItem is { } item && _slot != null)
        {
            for (int i = 0; i < Rows.ItemCount; i++)
                if (Rows.ContainerFromIndex(i) is { } container && new Rect(container.Bounds.Size).Contains(e.GetPosition(container)))
                {
                    int from = _slot.Items.IndexOf(item);
                    if (from >= 0 && from != i) _slot.Items.Move(from, i);
                    break;
                }
            Reveal(item);
        }
        _dragItem = null; _dragging = false; e.Pointer.Capture(null); e.Handled = true;
    }
    void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) { _dragItem = null; _dragging = false; }
}
