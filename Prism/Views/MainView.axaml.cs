using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Core;
using Prism.ViewModels;

namespace Prism.Views;

public partial class MainView : UserControl
{
    bool _syncing, _syncQueued;
    MainViewModel? _vm;
    HistoryItem? _pinTarget;
    Flyout? _pinFlyout;
    TextBox? _pinBox;

    public MainView()
    {
        InitializeComponent();
        HistoryGraph.Navigate += item => Vm?.GoToHistory(item);
        HistoryGraph.Edit += item => EditHistoryStep(item);
        HistoryGraph.Hover += item => Vm?.HoverHistory(item);
        HistoryGraph.StepContext += OnHistoryContext;
        CompactHistory.Navigate += item => Vm?.GoToHistory(item);
        CompactHistory.Edit += item => EditHistoryStep(item);
        CompactHistory.Hover += item => Vm?.HoverHistory(item);
        CompactHistory.StepContext += OnHistoryContext;
        DataContextChanged += (_, _) => Attach();
        AttachedToVisualTree += (_, _) => { Attach(); WindowControls.IsVisible = TopLevel.GetTopLevel(this) is MainWindow; };
        SearchContainer.LayoutUpdated += (_, _) => CenterSearch();
    }

    void UpdateContextLocation()
    {
        bool bottom = AppSettings.Current.HelpContextLocation == "bottom";
        BottomContext.IsVisible = bottom;
        ViewportContext.IsVisible = !bottom;
    }

    void CenterSearch()
    {
        if (SearchContainer.Parent is not Grid grid || grid.ColumnDefinitions.Count < 3) return;
        double available = grid.ColumnDefinitions[2].ActualWidth;
        double width = Math.Min(420, Math.Max(0, available));
        if (Math.Abs(SearchContainer.Width - width) > 0.1) SearchContainer.Width = width;
        var position = SearchContainer.TranslatePoint(new Point(0, 0), grid);
        if (position == null) return;
        var transform = SearchContainer.RenderTransform as Avalonia.Media.TranslateTransform;
        double current = transform?.X ?? 0;
        double left = position.Value.X - current;
        double shift = Math.Clamp(grid.Bounds.Width / 2 - (left + width / 2), -(available - width) / 2, (available - width) / 2);
        if (transform == null) SearchContainer.RenderTransform = new Avalonia.Media.TranslateTransform(shift, 0);
        else if (Math.Abs(current - shift) > 0.1) transform.X = shift;
    }

    void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not MainWindow window || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && (source is Button or TextBox || source.GetVisualAncestors().Any(v => v is Button or TextBox))) return;
        if (e.ClickCount == 2) ToggleMaximize(window);
        else window.BeginMoveDrag(e);
        e.Handled = true;
    }

    static void ToggleMaximize(Window window) => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void OnMinimize(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this) is Window window) window.WindowState = WindowState.Minimized; }
    void OnMaximize(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this) is Window window) ToggleMaximize(window); }
    void OnClose(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this) is Window window) window.Close(); }

    MainViewModel? Vm => DataContext as MainViewModel;
    void OnOpenStudio(object? sender, RoutedEventArgs e) => Vm?.OpenStudio((sender as Control)?.Tag is string panel && panel.Length > 0 ? panel : null);
    void OnPanelPickerClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control anchor || Vm is not { } vm) return;
        var choices = new List<object>();
        foreach (string id in new[] { "properties", "appearance", "diagnostics", "references", "parameters", "script" })
        {
            var choice = new MenuItem { Header = (vm.IsRightPanelOpen(id) ? "✓  " : "     ") + MainViewModel.PanelTitle(id), IsEnabled = id != "properties" && vm.ContextPanelId != id };
            choice.Click += (_, _) => vm.SetRightPanelEnabled(id, !vm.IsRightPanelOpen(id));
            choices.Add(choice);
        }
        var menu = new MenuFlyout { ItemsSource = choices, Placement = PlacementMode.BottomEdgeAlignedLeft };
        FlyoutBase.SetAttachedFlyout(anchor, menu); menu.ShowAt(anchor);
    }

    void OnRightPanelTab(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) Vm?.SelectRightPanel(id);
    }

    public void FocusSearch() { SearchBox.Focus(); SearchBox.SelectAll(); }

    System.ComponentModel.PropertyChangedEventHandler? _modeHandler;

    void HookSession(CadSession? old, CadSession s)
    {
        if (old != null) { old.SelectionChanged -= QueueSync; old.LayoutChanged -= ApplyLayout; old.StateChanged-=UpdateModeButtons; if (_modeHandler != null) old.PropertyChanged -= _modeHandler; }
        _modeHandler = (_, e) => { if (e.PropertyName is nameof(CadSession.Mode) or nameof(CadSession.Display) or nameof(CadSession.XRay)) UpdateModeButtons(); };
        s.SelectionChanged += QueueSync; s.LayoutChanged += ApplyLayout; s.PropertyChanged += _modeHandler;
        s.StateChanged+=UpdateModeButtons;
        ApplyLayout();
    }

    /// <summary>Place the four view cells for the document's layout: Single, Dual (model | top) or Quad (front, right / top, model).</summary>
    void ApplyLayout()
    {
        var s = Vm?.Session; if (s == null) return;
        Grid[] cells = [Cell0, Cell1, Cell2, Cell3];
        void Put(int i, int col, int row, int cs, int rs, bool on)
        {
            Grid.SetColumn(cells[i], col); Grid.SetRow(cells[i], row); Grid.SetColumnSpan(cells[i], cs); Grid.SetRowSpan(cells[i], rs);
            cells[i].IsVisible = on;
        }
        switch (s.Layout)
        {
            case ViewLayout.Single: Put(0, 0, 0, 2, 2, true); Put(1, 0, 0, 1, 1, false); Put(2, 1, 0, 1, 1, false); Put(3, 0, 1, 1, 1, false); break;
            case ViewLayout.Dual: Put(0, 0, 0, 1, 2, true); Put(3, 1, 0, 1, 2, true); Put(1, 0, 0, 1, 1, false); Put(2, 1, 0, 1, 1, false); break;
            default: Put(1, 0, 0, 1, 1, true); Put(2, 1, 0, 1, 1, true); Put(3, 0, 1, 1, 1, true); Put(0, 1, 1, 1, 1, true); break;
        }
        bool multi = s.Layout != ViewLayout.Single;
        Label0.IsVisible = multi;
        SepV.IsVisible = multi; Grid.SetColumn(SepV, 1);
        SepH.IsVisible = s.Layout == ViewLayout.Quad; Grid.SetRow(SepH, 1);
        Vm!.RefreshRibbon();
        // once the cells have their real sizes, fit the model to them
        if (multi) Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(Vm?.Session, s)) return;
            for (int i = 0; i < 4; i++) { s.Cameras[i].Width = cells[i].Bounds.Width; s.Cameras[i].Height = cells[i].Bounds.Height; }
            s.ZoomExtents();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Snapshot the viewport of the tab being left; it is what the tab shows when hovered.</summary>
    void CaptureTabPreview(DocumentTab tab)
    {
        tab.RefreshDetail();
        try
        {
            if (TopLevel.GetTopLevel(this) is not Window window) return;
            var origin = Viewport.TranslatePoint(new Point(0, 0), window);
            if (origin is not { } o || Viewport.Bounds.Width < 8 || Viewport.Bounds.Height < 8) return;
            double scale = window.RenderScaling;
            if (Prism.Automation.WindowCapture.CaptureThumbnail(window, (int)(o.X * scale), (int)(o.Y * scale), (int)(Viewport.Bounds.Width * scale), (int)(Viewport.Bounds.Height * scale), 320) is not var (pixels, w, h)) return;
            var bmp = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
            {
                int row = w * 4;
                if (fb.RowBytes == row) System.Runtime.InteropServices.Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
                else for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(pixels, y * row, fb.Address + y * fb.RowBytes, row);
            }
            tab.Preview = bmp;
        }
        catch { /* a missing preview is fine */ }
    }

    void OnBackToAssembly(object? s, RoutedEventArgs e) => Vm?.BackToAssembly();
    void OnTimelineStart(object? s, RoutedEventArgs e) => Vm?.TimelineToStart();
    void OnTimelinePlay(object? s, RoutedEventArgs e) => Vm?.TimelineTogglePlay();
    void OnTimelineKey(object? s, RoutedEventArgs e) => Vm?.TimelineKeyChanged();
    void OnTimelineRemoveKeys(object? s, RoutedEventArgs e) => Vm?.TimelineRemoveKeys();

    void OnDocTabPressed(object? s, Avalonia.Input.PointerPressedEventArgs e) { if (s is Control { Tag: DocumentTab t } && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Vm?.ActivateDocument(t); }
    void OnDocTab(object? s, RoutedEventArgs e) { if (s is Control { Tag: DocumentTab t }) Vm?.ActivateDocument(t); }
    void OnDocClose(object? s, RoutedEventArgs e) { if (s is Control { Tag: DocumentTab t }) Vm?.CloseDocument(t); e.Handled = true; }
    void OnDocNew(object? s, RoutedEventArgs e) => Vm?.NewDocumentTab();

    void Attach()
    {
        if (Vm is not { } vm || ReferenceEquals(vm, _vm)) return;
        _vm = vm;
        vm.BrowserRebuilt += () => { EmptyHint.IsVisible = vm.Session.Objects.Count == 0 && string.IsNullOrWhiteSpace(vm.BrowserFilter); QueueSync(); };
        vm.HistoryRebuilt += OnHistoryRebuilt;
        vm.DocumentLeaving += CaptureTabPreview;
        HookSession(null, vm.Session);
        vm.SessionSwapped += (old, s) => { HookSession(old, s); UpdateModeButtons(); EmptyHint.IsVisible = s.Objects.Count == 0 && string.IsNullOrWhiteSpace(vm.BrowserFilter); QueueSync(); };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.ShowHistoryPanel)) UpdateTabs(); };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(MainViewModel.ShowScriptPanel) or nameof(MainViewModel.ShowScriptEditor) or nameof(MainViewModel.AnyStudioVisible) or nameof(MainViewModel.ShowMaterialsPanel) or nameof(MainViewModel.ShowLibraryPanel) or nameof(MainViewModel.ActiveCommand) or nameof(MainViewModel.ShowGizmoPanel)) WorkGrid.ColumnDefinitions[4].Width = new GridLength(vm.ShowScriptEditor ? 540 : vm.AnyStudioVisible || vm.ShowMaterialsPanel || vm.ShowLibraryPanel || vm.ShowGizmoPanel || vm.ActiveCommand != null ? 394 : 312); };
        WorkGrid.ColumnDefinitions[4].Width = new GridLength(vm.ShowScriptEditor ? 540 : vm.AnyStudioVisible || vm.ShowMaterialsPanel || vm.ShowLibraryPanel || vm.ShowGizmoPanel || vm.ActiveCommand != null ? 394 : 312);
        BuildStatusBar(); UpdateContextLocation(); AppSettings.Changed += BuildStatusBar; AppSettings.Changed += UpdateContextLocation;
        HistoryList.DoubleTapped += (_, _) => { if (HistoryList.SelectedItem is HistoryItem hi) EditHistoryStep(hi); };
        OnHistoryRebuilt();
        UpdateModeButtons(); UpdateTabs();
        vm.OpenTraditionalMenu += () => Avalonia.Threading.Dispatcher.UIThread.Post(ShowTraditionalMenu);
        vm.RequestSettings = () => OnSettings(null, new RoutedEventArgs());
        vm.OpenMarkingMenu += (x, y) => { vm.PrepareMarkingContext(); Marking.Open(new Point(x, y), vm.BuildMarkingItems(), false); };
        Viewport.MarkingMenu += (kind, pos) =>
        {
            switch (kind)
            {
                case "hold": case "tap":
                    vm.PrepareMarkingContext();
                    Marking.Open(pos, vm.BuildMarkingItems(), kind == "hold");
                    break;
                case "move": Marking.MoveTo(pos); break;
                case "release": Marking.Release(pos); break;
            }
        };
        EmptyHint.IsVisible = vm.Session.Objects.Count == 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.InputOpen) && vm.InputOpen)
                Dispatcher.UIThread.Post(() => { InputBox.Focus(); InputBox.SelectAll(); }, DispatcherPriority.Input);
        };

        vm.PickOpenFile = async (title, patterns) =>
        {
            var top = TopLevel.GetTopLevel(this); if (top == null) return null;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title, AllowMultiple = false, SuggestedStartLocation = await StartFolder(top),
                FileTypeFilter = [new FilePickerFileType("Supported files") { Patterns = patterns }],
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        };
        vm.PickFolder = async title =>
        {
            var top = TopLevel.GetTopLevel(this); if (top == null) return null;
            var f = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = await StartFolder(top) });
            return f.FirstOrDefault()?.TryGetLocalPath();
        };
        vm.PickSaveFile = async (title, suggested, ext) =>
        {
            var top = TopLevel.GetTopLevel(this); if (top == null) return null;
            var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title, SuggestedFileName = suggested, DefaultExtension = ext, SuggestedStartLocation = await StartFolder(top),
                FileTypeChoices = [new FilePickerFileType(ext.ToUpper()) { Patterns = ["*." + ext] }],
            });
            return f?.TryGetLocalPath();
        };
    }

    /// <summary>File dialogs open in the workspace folder (when one is set and still there).</summary>
    static async System.Threading.Tasks.Task<IStorageFolder?> StartFolder(TopLevel top) =>
        AppSettings.Current.WorkspacePath is { Length: > 0 } p && System.IO.Directory.Exists(p) ? await top.StorageProvider.TryGetFolderFromPathAsync(p) : null;

    // ================================================================= browser tree
    void QueueSync()
    {
        if (_syncQueued) return;
        _syncQueued = true;
        Dispatcher.UIThread.Post(() => { _syncQueued = false; SyncTree(); }, DispatcherPriority.Background);
    }

    void SyncTree()
    {
        if (Vm is not { } vm) return;
        _syncing = true;
        try
        {
            var want = vm.Session.Selection.ToHashSet();
            var nodes = vm.AllBrowserNodes().Where(n => n.SelectionItem is { } it && want.Contains(it)).ToList();
            var have = Tree.SelectedItems?.OfType<BrowserNode>().Where(n => n.SelectionItem != null).ToList() ?? [];
            if (nodes.Count == have.Count && nodes.All(have.Contains)) return;
            Tree.SelectedItems?.Clear();
            foreach (var n in nodes) Tree.SelectedItems?.Add(n);
        }
        finally { _syncing = false; }
    }

    List<BrowserNode> SelectedNodes() => Tree.SelectedItems?.OfType<BrowserNode>().ToList() ?? [];

    void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Vm is not { } vm) return;
        var selectedNodes = SelectedNodes();
        if (selectedNodes.Count == 1 && selectedNodes[0].IsActiveSketchDatum) { vm.UseDatumNode(selectedNodes[0]); return; }
        var items = selectedNodes.Select(n => n.SelectionItem).Where(i => i != null).Select(i => i!.Value).ToList();
        if (vm.Session.ActiveCommand is { } cmd && cmd.ActiveSlot != null)
        {
            foreach (var it in items) if (!cmd.ActiveSlot.Items.Contains(it)) cmd.Pick(it, true);
            QueueSync();
            return;
        }
        vm.Session.SelectItems(items);
    }

    void OnTreeContext(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm is not { } vm) return;
        // right-click selects the row under the pointer first (unless it is already part of the selection)
        var row = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        if (row?.DataContext is BrowserNode hit && !SelectedNodes().Contains(hit)) { Tree.SelectedItem = hit; }
        var nodes = SelectedNodes();
        if (nodes.Count == 0) { e.Handled = true; return; }

        var objs = nodes.Where(n => n.IsObject).ToList();
        var items = new List<object>();
        MenuItem Mi(string header, Action act, bool enabled = true) { var m = new MenuItem { Header = header, IsEnabled = enabled }; m.Click += (_, _) => act(); return m; }

        if (nodes.Count == 1 && nodes[0].Kind == NodeKind.Origin && nodes[0].OriginIndex < 3)
            items.Add(Mi("Create sketch on this plane", () => vm.SketchOnPlane(nodes[0])));
        if (nodes.Count == 1 && nodes[0].Kind == NodeKind.Datum)
        {
            if (nodes[0].IsActiveSketchDatum) items.Add(Mi("Use this reference", () => vm.UseDatumNode(nodes[0])));
            else if (nodes[0].DatumKind == Core.Sketching.SketchDatumKind.Plane) items.Add(Mi("Create sketch on this plane", () => vm.UseDatumNode(nodes[0]), !vm.IsSketching));
        }
        if (objs.Count == 1 && objs[0].ObjKind == ObjKind.Sketch) items.Add(Mi("Edit sketch", () => vm.EditSketchNode(objs[0])));
        if (objs.Count == 1 && vm.Session.FeatureNodeFor(objs[0].ObjectId) is { } feature)
            items.Add(Mi("Edit feature…", () => vm.Session.EditFeature(feature), !vm.IsSketching));
        if (objs.Count == 1) items.Add(Mi("Rename            F2", () => objs[0].BeginRename()));
        if (objs.Count > 0)
        {
            items.Add(Mi("Zoom to", () => vm.ZoomToNodes(objs)));
            items.Add(Mi("Isolate", () => vm.Isolate(objs)));
        }
        items.Add(Mi("Show all", vm.ShowAll));
        if (objs.Count == 1 && objs[0].ObjKind == ObjKind.Form) { items.Add(new Separator()); items.Add(Mi("Convert to NURBS", () => vm.ConvertNodeToNurbs(objs[0]))); }
        if (objs.Count > 0)
        {
            items.Add(new Separator());
            items.Add(Mi("Duplicate", () => vm.DuplicateNodes(objs)));
            items.Add(Mi("Delete            Del", () => vm.DeleteNodes(objs)));
        }
        Tree.ContextMenu = new ContextMenu { ItemsSource = items };
    }

    void OnTreeKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || Vm is not { } vm) return;
        var objs = SelectedNodes().Where(n => n.IsObject).ToList();
        if (e.Key == Key.F2 && objs.Count == 1) { objs[0].BeginRename(); e.Handled = true; }
        else if (e.Key == Key.Delete && objs.Count > 0) { vm.DeleteNodes(objs); e.Handled = true; }
    }

    void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is TextBox || Vm is not { } vm) return;
        // double-clicking a name renames it (the Origin and its folders cannot be renamed); double-clicking anywhere else on the row keeps its old action
        if (e.Source is TextBlock { DataContext: BrowserNode named } label && named.CanRename && label.Text == named.Title) { named.BeginRename(); e.Handled = true; return; }
        var node = SelectedNodes().FirstOrDefault();
        if (node == null) return;
        if (node.IsObject && node.ObjKind == ObjKind.Sketch) vm.EditSketchNode(node);
        else if (node.IsObject) vm.ZoomToNodes([node]);
        else if (node.Kind == NodeKind.Origin && node.OriginIndex < 3) vm.SketchOnPlane(node);
        else if (node.Kind == NodeKind.Datum) vm.UseDatumNode(node);
    }

    void OnRenameBoxInit(object? sender, EventArgs e)
    {
        if (sender is not TextBox tb) return;
        tb.PropertyChanged += (_, a) =>
        {
            if (a.Property == IsVisibleProperty && tb.IsVisible) Dispatcher.UIThread.Post(() => { tb.Focus(); tb.SelectAll(); }, DispatcherPriority.Input);
        };
    }

    void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if ((sender as Control)?.DataContext is not BrowserNode n) return;
        if (e.Key == Key.Enter) { n.CommitRename(); e.Handled = true; Tree.Focus(); }
        else if (e.Key == Key.Escape) { n.CancelRename(); e.Handled = true; Tree.Focus(); }
    }

    void OnRenameLost(object? sender, RoutedEventArgs e) { if ((sender as Control)?.DataContext is BrowserNode n) n.CommitRename(); }

    void OnCollapseAll(object? s, RoutedEventArgs e) { if (Vm != null) foreach (var n in Vm.AllBrowserNodes()) n.IsExpanded = false; }

    // ================================================================= left tabs
    void OnShowBrowser(object? s, RoutedEventArgs e) { if (Vm != null) Vm.ShowHistoryPanel = false; }
    void OnShowHistory(object? s, RoutedEventArgs e) { if (Vm != null) Vm.ShowHistoryPanel = true; }
    void OnToggleHistory(object? s, RoutedEventArgs e) { if (Vm != null) Vm.ShowHistoryPanel = !Vm.ShowHistoryPanel; }
    void UpdateTabs()
    {
        bool h = Vm?.ShowHistoryPanel == true;
        TabBrowser.Classes.Set("on", !h); TabHistory.Classes.Set("on", h);
    }

    // ================================================================= history
    void OnCollapseHistory(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.HistoryExpanded = !vm.HistoryExpanded; }
    void OnHistoryResize(object? sender, VectorEventArgs e) { if (Vm is { } vm) vm.HistoryHeight = Math.Clamp(vm.HistoryHeight - e.Vector.Y, 100, 360); }
    void OnBranchHistory(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        vm.Session.RequestInput("Name the design alternative", "Alternative", name => { if (name != null) vm.Session.BranchHistory(name); });
    }
    void EditHistoryStep(HistoryItem item)
    {
        if (Vm is not { } vm) return;
        if (item.Node.Recipe != null) vm.Session.EditFeature(item.Node);
        else
        {
            var sketch = item.Node.Diff().Added.Concat(item.Node.Diff().Modified).FirstOrDefault(o => o.Kind == ObjKind.Sketch && o.SketchData != null);
            if (sketch != null) vm.Session.EditSketch(sketch, item.Node);
        }
    }
    void OnHistoryRebuilt()
    {
        if (Vm is not { } vm) return;
        Dispatcher.UIThread.Post(() =>
        {
            _syncing = true;
            try { HistoryList.SelectedItem = vm.HistoryRows.FirstOrDefault(r => r.IsCurrent); if (HistoryList.SelectedItem != null) HistoryList.ScrollIntoView(HistoryList.SelectedItem); }
            finally { _syncing = false; }
            HistoryGraph.SetHistory(vm.Session, vm.HistoryRows.ToList());
            CompactHistory.SetHistory(vm.Session, vm.HistoryRows.ToList());
        }, DispatcherPriority.Background);
    }

    void OnHistorySelection(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Vm is not { } vm) return;
        if (HistoryList.SelectedItem is HistoryItem it) vm.GoToHistory(it);
    }

    void OnTimelineExit(object? sender, PointerEventArgs e) => Vm?.HoverHistory(null);
    void OnHistoryRowEnter(object? sender, PointerEventArgs e) { if ((sender as Control)?.DataContext is HistoryItem it) Vm?.HoverHistory(it); }

    void OnHistoryContext(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm is not { } vm) return;
        HistoryItem? it = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext as HistoryItem
                          ?? (sender as Control)?.DataContext as HistoryItem
                          ?? (e.Source as Control)?.FindAncestorOfType<Button>()?.DataContext as HistoryItem;
        if (it == null) { e.Handled = true; return; }
        var items = new List<object>();
        MenuItem Mi(string header, Action act) { var m = new MenuItem { Header = header }; m.Click += (_, _) => act(); return m; }

        if (it.Node.Recipe != null) items.Add(Mi("Edit this feature…", () => vm.Session.EditFeature(it.Node)));
        else if (it.Node.Diff().Added.Concat(it.Node.Diff().Modified).Any(o => o.Kind == ObjKind.Sketch && o.SketchData != null))
            items.Add(Mi("Edit sketch…", () => EditHistoryStep(it)));
        items.Add(Mi("Go to this step", () => vm.GoToHistory(it)));
        if (!it.IsStart) items.Add(Mi(it.IsPinned ? "Rename checkpoint…" : "Name as checkpoint…", () => ShowPinFlyout(it)));
        if (it.IsPinned) items.Add(Mi("Remove checkpoint", () => vm.PinStep(it, "")));

        var restorable = vm.RestorableAt(it).Take(10).ToList();
        if (restorable.Count > 0)
        {
            var sub = new MenuItem { Header = "Restore one object from here" };
            sub.ItemsSource = restorable.Select(r => Mi(r.Name, () => vm.RestoreObjectFrom(it, r.Id))).Cast<object>().ToList();
            items.Add(new Separator()); items.Add(sub);
        }
        var siblings = it.Node.Parent?.Children.Where(c => c != it.Node).ToList() ?? [];
        if (siblings.Count > 0)
        {
            var sub = new MenuItem { Header = "Other branches from here" };
            sub.ItemsSource = siblings.Select(s => Mi($"{s.Label}  ({s.Time:HH:mm})", () => vm.Session.GoTo(s))).Cast<object>().ToList();
            items.Add(sub);
        }
        var menu = new ContextMenu { ItemsSource = items };
        (sender as Control)!.ContextMenu = menu;
    }

    void OnPinCheckpoint(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var cur = Vm.TimelineBar.FirstOrDefault(t => t.IsCurrent);
        if (cur != null) ShowPinFlyout(cur, sender as Control);
    }

    void ShowPinFlyout(HistoryItem it, Control? anchor = null)
    {
        _pinTarget = it;
        if (_pinFlyout == null)
        {
            _pinBox = new TextBox { Width = 220, Watermark = "Checkpoint name" };
            var ok = new Button { Content = "Save", Padding = new Thickness(14, 5) };
            ok.Classes.Add("primary");
            ok.Click += (_, _) => { if (_pinTarget != null) Vm?.PinStep(_pinTarget, _pinBox!.Text ?? ""); _pinFlyout!.Hide(); };
            _pinBox.KeyDown += (_, k) => { if (k.Key == Key.Enter) { if (_pinTarget != null) Vm?.PinStep(_pinTarget, _pinBox.Text ?? ""); _pinFlyout!.Hide(); } };
            _pinFlyout = new Flyout { Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Name this moment", FontWeight = Avalonia.Media.FontWeight.SemiBold }, _pinBox, ok } } };
        }
        _pinBox!.Text = it.Pin ?? "";
        _pinFlyout.ShowAt(anchor ?? HistoryGraph);
    }

    // ================================================================= command search
    void OnSearchFocus(object? sender, GotFocusEventArgs e) => Vm?.UpdateSearchResults();

    void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        switch (e.Key)
        {
            case Key.Down: if (vm.SearchResults.Count > 0) vm.SearchIndex = Math.Min(vm.SearchIndex + 1, vm.SearchResults.Count - 1); e.Handled = true; break;
            case Key.Up: vm.SearchIndex = Math.Max(vm.SearchIndex - 1, 0); e.Handled = true; break;
            case Key.Enter: vm.RunSearchHit(vm.SearchResults.ElementAtOrDefault(vm.SearchIndex)); e.Handled = true; Focus(); break;
            case Key.Escape: vm.SearchText = ""; vm.SearchOpen = false; e.Handled = true; Focus(); break;
        }
    }

    void OnSearchListClick(object? sender, PointerReleasedEventArgs e)
    {
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext is CommandHit hit) Vm?.RunSearchHit(hit);
    }

    void OnPendingCreate(object? sender, RoutedEventArgs e) => Vm?.Session.ResolvePending(true);
    void OnPendingDiscard(object? sender, RoutedEventArgs e) => Vm?.Session.ResolvePending(false);
    void OnPendingKeep(object? sender, RoutedEventArgs e) => Vm?.Session.KeepPending();

    // ================================================================= value entry
    void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Key == Key.Enter) { e.Handled = true; vm.CommitInput(); Focus(); }
        else if (e.Key == Key.Escape) { e.Handled = true; vm.CancelInput(); Focus(); }
    }
    void OnInputLost(object? sender, RoutedEventArgs e) { if (Vm is { InputOpen: true } vm && !InputBox.IsKeyboardFocusWithin) vm.CancelInput(); }

    // ================================================================= view / status
    Button? _bObj, _bFace, _bEdge, _bVertex, _bShaded, _bEdges, _bWire, _bXray;

    void UpdateModeButtons()
    {
        var d = Vm?.Session.Display ?? DisplayMode.ShadedEdges;
        _bObj?.Classes.Set("checked", Vm?.IsSelectionMode(SelMode.Object)==true);
        _bFace?.Classes.Set("checked", Vm?.IsSelectionMode(SelMode.Face)==true);
        _bEdge?.Classes.Set("checked", Vm?.IsSelectionMode(SelMode.Edge)==true);
        _bVertex?.Classes.Set("checked", Vm?.IsVertexMode==true);
        _bShaded?.Classes.Set("checked", d == DisplayMode.Shaded);
        _bEdges?.Classes.Set("checked", d == DisplayMode.ShadedEdges);
        _bWire?.Classes.Set("checked", d == DisplayMode.Wireframe);
        _bXray?.Classes.Set("checked", Vm?.Session.XRay == true);
    }

    /// <summary>The right side of the status bar, built from the user's list (Settings > Toolbars).</summary>
    void BuildStatusBar()
    {
        if (Vm is not { } vm) return;
        StatusRight.Children.Clear();
        _bObj = _bFace = _bEdge = _bVertex = _bShaded = _bEdges = _bWire = _bXray = null;
        Button Mk(string text, Action click, string? tip = null) { var b = new Button { Content = text }; b.Classes.Add("statusbtn"); b.Click += (_, _) => click(); if (tip != null) ToolTip.SetTip(b, tip); return b; }
        TextBlock Txt(string path, bool mono = false, double margin = 12)
        {
            var tb = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(6, 0, margin, 0), FontSize = mono ? 11.5 : 12 };
            tb.Classes.Add("soft"); if (mono) tb.FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono,Consolas,monospace");
            tb.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(path)); return tb;
        }
        foreach (var id in AppSettings.Current.StatusBar ?? [.. MainViewModel.DefaultStatusBar])
        {
            switch (id)
            {
                case "workspace":
                {
                    var b = Mk("", () => vm.OpenHome());
                    b.Bind(ContentControl.ContentProperty, new Avalonia.Data.Binding("WorkspaceLabel"));
                    ToolTip.SetTip(b, "Your workspace is the folder your parts are saved in. Click to open the home screen and change it.");
                    StatusRight.Children.Add(b); break;
                }
                case "mode":
                {
                    var b = Mk("", () => { });
                    b.Bind(ContentControl.ContentProperty, new Avalonia.Data.Binding("ModeName") { StringFormat = "{0} ▾" });
                    ToolTip.SetTip(b, "The mode decides which ribbon tabs are shown (Design, Scan & Print, Simulate...). Click to change it.");
                    b.Click += (_, _) =>
                    {
                        var items = vm.Workspaces.Select(w =>
                        {
                            var mi = new MenuItem { Header = (w.IsCurrent ? "✓  " : "     ") + w.Name }; ToolTip.SetTip(mi, w.Tip);
                            mi.Click += (_, _) => vm.SetWorkspace(w.Id); return (object)mi;
                        }).ToList();
                        new MenuFlyout { ItemsSource = items, Placement = PlacementMode.TopEdgeAlignedLeft }.ShowAt(b);
                    };
                    StatusRight.Children.Add(b); break;
                }
                case "selection": StatusRight.Children.Add(Txt("SelectionTitle")); break;
                case "cursor": StatusRight.Children.Add(Txt("CursorText", true, 14)); break;
                case "memory": StatusRight.Children.Add(Txt("MemoryText")); break;
                case "scene": StatusRight.Children.Add(Txt("SceneText")); break;
                case "modes":
                    StatusRight.Children.Add(_bObj = Mk("Object", () => vm.SetSelectionMode(SelMode.Object)));
                    StatusRight.Children.Add(_bFace = Mk("Face", () => vm.SetSelectionMode(SelMode.Face)));
                    StatusRight.Children.Add(_bEdge = Mk("Edge", () => vm.SetSelectionMode(SelMode.Edge)));
                    StatusRight.Children.Add(_bVertex = Mk("Vertex", vm.SelectVertices, "Select cage vertices (Freeform)"));
                    break;
                case "display":
                    StatusRight.Children.Add(_bShaded = Mk("Shaded", () => vm.Session.Display = DisplayMode.Shaded));
                    StatusRight.Children.Add(_bEdges = Mk("Edges", () => vm.Session.Display = DisplayMode.ShadedEdges));
                    StatusRight.Children.Add(_bWire = Mk("Wire", () => vm.Session.Display = DisplayMode.Wireframe));
                    StatusRight.Children.Add(_bXray = Mk("X-ray", () => vm.Session.XRay = !vm.Session.XRay, "See through bodies"));
                    break;
                case "fit": StatusRight.Children.Add(Mk("Fit", () => vm.Session.ZoomExtents(), "Zoom to extents")); break;
                case "units": { var u = new TextBlock { Text = "mm", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }; u.Classes.Add("faint"); StatusRight.Children.Add(u); break; }
            }
            if (id != "units" && id != "cursor") { var div = new Border { Width = 1, Height = 14, Margin = new Thickness(4, 0) }; div[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line"); StatusRight.Children.Add(div); }
        }
        // no divider after the last item
        if (StatusRight.Children.Count > 0 && StatusRight.Children[^1] is Border) StatusRight.Children.RemoveAt(StatusRight.Children.Count - 1);
        UpdateModeButtons();
    }

    void OnToggleAssistant(object? s, RoutedEventArgs e) => Vm?.ToggleAssistant();
    void OnRenderStop(object? s, RoutedEventArgs e) => Vm?.StopRender();
    void OnRenderSave(object? s, RoutedEventArgs e) => Vm?.SaveRender();
    void OnRenderClose(object? s, RoutedEventArgs e) => Vm?.CloseRender();
    void OnLookAt(object? s, RoutedEventArgs e) => Vm?.Session.LookAtSelection();
    void OnFit(object? s, RoutedEventArgs e) => Vm?.Session.ZoomExtents();
    void OnIso(object? s, RoutedEventArgs e) { if (Vm == null) return; Vm.Session.Cameras[0].SetView(-35, 28); Vm.Session.Invalidate(); }
    void OnToggleProjection(object? s, RoutedEventArgs e) { if (Vm != null) Vm.Session.Ortho = !Vm.Session.Ortho; }
    void OnCycleDisplay(object? s, RoutedEventArgs e) { if (Vm != null) Vm.Session.Display = (DisplayMode)(((int)Vm.Session.Display + 1) % 3); }
    void OnSave(object? s, RoutedEventArgs e) => Vm?.SaveFile();
    void OnWorkspaceMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control anchor || Vm is not { } vm) return;
        var choices = new List<object>();
        foreach (var workspace in vm.Workspaces)
        {
            var w = workspace;
            var item = new MenuItem { Header = (w.IsCurrent ? "✓  " : "     ") + w.Name };
            ToolTip.SetTip(item, w.Tip);
            item.Click += (_, _) => vm.SetWorkspace(w.Id);
            choices.Add(item);
        }
        choices.Add(new Separator());
        var customize = new MenuItem { Header = "Show in workspace bar" };
        foreach (var workspace in vm.Workspaces)
        {
            var w = workspace;
            var item = new MenuItem { Header = (w.IsPinned ? "✓  " : "     ") + w.Name };
            item.Click += (_, _) => vm.SetWorkspacePinned(w.Id, !w.IsPinned);
            customize.Items.Add(item);
        }
        choices.Add(customize);
        new MenuFlyout { ItemsSource = choices, Placement = PlacementMode.BottomEdgeAlignedLeft }.ShowAt(anchor);
    }

    void OnWorkspace(object? s, RoutedEventArgs e) { if (s is Button { Tag: string id }) Vm?.SetWorkspace(id); }
    void OnEditSketchGeometry(object? sender, RoutedEventArgs e) => Vm?.Session.Sketch?.ToggleGeometryEditing(Vm.Session);

    void OnToolOption(object? s, RoutedEventArgs e) { if (s is Button { Tag: int i }) Vm?.PickToolOption(i); }
    void OnToolStep(object? s, RoutedEventArgs e) { if (s is Button { Tag: string t } && int.TryParse(t, out var d)) Vm?.StepTool(d); }
    void OnHome(object? s, RoutedEventArgs e) { if (Vm is { } vm) { if (vm.HomeOpen) vm.CloseHome(); else vm.OpenHome(); } }

    void OnHelp(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control anchor) return;
        var version = new MenuItem { Header = "Prism v0.1", IsEnabled = false };
        var guide = new MenuItem { Header = "Open Prism guide" };
        guide.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://prism-cad-guide.christopherbfwilliam.chatgpt.site") { UseShellExecute = true }); }
            catch (Exception ex) { Vm?.Session.Say("Could not open the guide: " + ex.Message, true); }
        };
        new MenuFlyout { ItemsSource = new object[] { version, new Separator(), guide }, Placement = PlacementMode.BottomEdgeAlignedRight }.ShowAt(anchor);
    }

    SettingsWindow? _settings;

    void OnSettings(object? s, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (_settings != null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(vm);
        _settings.Closed += (_, _) => _settings = null;
        if (TopLevel.GetTopLevel(this) is Window owner) _settings.Show(owner); else _settings.Show();
    }

    /// <summary>The ordinary menu behind the marking menu's 6 o'clock entry.</summary>
    void ShowTraditionalMenu()
    {
        if (Vm is not { } vm) return;
        MenuItem Make(MenuSpec m)
        {
            var mi = new MenuItem { Header = m.Label, IsEnabled = m.Enabled, InputGesture = m.Gesture != null ? Avalonia.Input.KeyGesture.Parse(m.Gesture.Replace("Del", "Delete")) : null };
            if (m.Sub != null) foreach (var c in m.Sub) mi.Items.Add(c.IsSeparator ? new Separator() : Make(c));
            else if (m.Run != null) { var run = m.Run; mi.Click += (_, _) => { try { run(); } catch (Exception ex) when (ex is not OutOfMemoryException) { vm.Session.Say(ex.Message, true); } }; }
            return mi;
        }
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        foreach (var m in vm.BuildTraditionalMenu()) menu.Items.Add(m.IsSeparator ? new Separator() : Make(m));
        menu.Open(Viewport);
    }

    void OnUndo(object? s, RoutedEventArgs e) => Vm?.Session.Undo();
    void OnRedo(object? s, RoutedEventArgs e) => Vm?.Session.Redo();
}
