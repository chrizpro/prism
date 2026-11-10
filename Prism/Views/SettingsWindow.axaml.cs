using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Prism.Core;
using Prism.ViewModels;

namespace Prism.Views;

/// <summary>Settings: theme, the right-click menu, and a ribbon editor (hide tabs, rearrange commands, make new tabs, reset).</summary>
public partial class SettingsWindow : Window
{
    sealed record Row(int G, int I, string Text) { public override string ToString() => Text; }
    sealed record Entry(string Id, string Text) { public override string ToString() => Text; }

    readonly MainViewModel _vm;
    readonly ComboBox _tabBox = new() { MinWidth = 180 };
    readonly CheckBox _showTab = new() { Content = "Show this tab", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    readonly TextBlock _tabNote = new() { Classes = { "faint" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    readonly TextBox _name = new() { Watermark = "Name for a new tab or group", MinWidth = 220 };
    readonly Button _deleteTab = new() { Content = "Delete tab" }, _resetTab = new() { Content = "Reset this tab" }, _resetAll = new() { Content = "Reset the whole ribbon" };
    string _tab = "Home";
    List<GroupCfg> _cfg = [];
    bool _loading;
    bool _confirmReset;

    public SettingsWindow() : this(null!) { }

    public SettingsWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        if (vm == null) return;
        _loading = true;
        var s = AppSettings.Current;
        HelpContextLocation.SelectedIndex = s.HelpContextLocation == "bottom" ? 1 : 0;
        AccentChoice.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "Blue", "Teal", "Purple", "Orange", "Rose" }, s.AccentColor));
        ThemeDark.IsChecked = s.Theme == "Dark"; ThemeLight.IsChecked = s.Theme == "Light"; ThemeSystem.IsChecked = s.Theme == "System";
        MarkingOn.IsChecked = s.MarkingMenu; HoldMs.Value = s.MarkingHoldMs; HoldLabel.Text = $"Hold time: {s.MarkingHoldMs} ms";
        PlaneScale.Value = s.PlaneScale * 100; PlaneScaleLabel.Text = $"Plane size: {(int)PlaneScale.Value} %";
        ReverseScrollZoom.IsChecked = s.ReverseScrollZoom;
        ShowGridOnStart.IsChecked = s.ShowGridOnStart;
        BuildGridSwatches();
        ShowWorkspace(); WsHome.IsChecked = s.ShowHomeOnStart;
        StartupUpdates.IsChecked = s.CheckForUpdatesOnStartup;
        RecoveryEnabled.IsChecked = s.AutoRecoveryEnabled;
        RecoveryInterval.Value = s.AutoRecoveryIntervalMinutes;
        RecoveryInterval.IsEnabled = s.AutoRecoveryEnabled;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.WorkspaceFolder)) ShowWorkspace(); };
        BuildRibbonEditor();
        BuildToolbarEditors();
        BuildKeyboardPage();
        _loading = false;
    }

    // ------------------------------------------------------------ keyboard shortcuts
    const string NewKey = "new:key";          // the key button of the "add a shortcut" form
    string? _capturing;                       // the action (or NewKey) waiting for a key press
    string _keyNote = "";
    string _shortcutSearch = "";
    readonly HashSet<string> _openNodes = ["scope:" + Hotkeys.Global];
    TreeView? _shortcutTree;
    TextBlock? _shortcutCount, _noteText, _newInfo;
    Border? _addForm;
    TextBox? _cmdBox;
    ListBox? _cmdResults;
    ComboBox? _newScopeBox;
    Button? _newKeyButton;
    string _newGesture = "", _newTarget = "", _newName = "";

    /// <summary>Press a key while an action is waiting for one: set it, clear it (Backspace or Delete) or give up (Escape).</summary>
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (_capturing == null) { base.OnKeyDown(e); return; }
        e.Handled = true;
        if (HotkeyInput.Gesture(e) is not { } gesture) return;             // only a modifier so far
        bool plain = e.KeyModifiers == Avalonia.Input.KeyModifiers.None;
        if (_capturing == NewKey)
        {
            if (!(e.Key == Avalonia.Input.Key.Escape && plain)) _newGesture = e.Key is Avalonia.Input.Key.Back or Avalonia.Input.Key.Delete && plain ? "" : gesture;
            _capturing = null; BuildKeyboardPage(); return;
        }
        var action = Hotkeys.Find(_capturing)!;
        if (e.Key == Avalonia.Input.Key.Escape && plain) _keyNote = "";
        else if (e.Key is Avalonia.Input.Key.Back or Avalonia.Input.Key.Delete && plain) { Hotkeys.Set(action.Id, ""); _keyNote = $"“{action.Name}” has no shortcut now."; }
        else
        {
            var clash = Hotkeys.FindConflict(action.Id, gesture, Hotkeys.Effective(action).Scope);
            if (clash != null) _keyNote = $"{Hotkeys.Display(gesture)} is already used by “{clash.Name}” in this context. Clear that one first, or choose another key.";
            else { Hotkeys.Set(action.Id, gesture); _keyNote = $"“{action.Name}” is now {Hotkeys.Display(gesture)}."; }
        }
        _capturing = null; BuildKeyboardPage();
    }

    static int ScopeOrder(string scope) => scope == Hotkeys.Global ? 0 : scope == Hotkeys.Sketch ? 1 : scope == Hotkeys.Command ? 2 : scope == Hotkeys.Freeform ? 3 : 4;

    /// <summary>Whether every word the user typed appears in the shortcut's name, group, note, key or context.</summary>
    static bool ShortcutMatches(HotkeyAction a, string[] terms)
    {
        if (terms.Length == 0) return true;
        var (gesture, scope) = Hotkeys.Effective(a);
        string hay = $"{a.Name} {a.Category} {a.Note} {Hotkeys.Display(gesture)} {gesture} {Hotkeys.ScopeName(scope)}";
        return terms.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    void BuildKeyboardPage()
    {
        if (_shortcutTree == null) BuildKeyboardChrome();
        _noteText!.Text = _keyNote;
        RefreshAddForm();
        RefreshShortcutTree();
    }

    /// <summary>The parts of the page that stay put while the list is rebuilt: search box, buttons and the add form (so typing keeps its focus).</summary>
    void BuildKeyboardChrome()
    {
        var host = KeyboardHost; host.Children.Clear();
        host.Children.Add(new TextBlock { Text = "Keyboard", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        host.Children.Add(new TextBlock { Text = "Every shortcut, grouped by where it works. Search by name, key or group. Click a key to change it, then press the new combination (Escape gives up, Backspace or Delete clears it). The same key can mean different things in different places: while a command is open or while sketching, that meaning wins over the everyday one. You can also add a shortcut for any ribbon command.", Classes = { "faint" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 0, 0, 8) });

        var search = new TextBox { Watermark = "Search shortcuts: a name, a key such as Ctrl+S, a group or a context", Text = _shortcutSearch };
        search.TextChanged += (_, _) => { _shortcutSearch = search.Text ?? ""; RefreshShortcutTree(); };
        host.Children.Add(search);

        var tools = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 4) };
        var expand = new Button { Content = "Expand all" };
        expand.Click += (_, _) => SetAllExpanded(true);
        var collapse = new Button { Content = "Collapse all" };
        collapse.Click += (_, _) => SetAllExpanded(false);
        var add = new Button { Content = "Add a shortcut…" };
        add.Click += (_, _) => { _addForm!.IsVisible = !_addForm.IsVisible; if (_addForm.IsVisible) _cmdBox?.Focus(); };
        var resetAll = new Button { Content = "Reset built-in shortcuts" };
        resetAll.Click += (_, _) => { Hotkeys.ResetAll(); _capturing = null; _keyNote = "All built-in shortcuts are back to their shipped keys."; BuildKeyboardPage(); };
        _shortcutCount = new TextBlock { Classes = { "faint" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        foreach (var b in new Control[] { expand, collapse, add, resetAll, _shortcutCount }) tools.Children.Add(b);
        host.Children.Add(tools);
        _noteText = new TextBlock { Classes = { "faint" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 0, 0, 4) };
        host.Children.Add(_noteText);

        BuildAddForm();
        host.Children.Add(_addForm!);

        _shortcutTree = new TreeView { Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        host.Children.Add(_shortcutTree);
        host.Children.Add(new TextBlock { Text = "Typing numbers into a command or sketch, Tab to the next field and + / - are not shortcuts and cannot be changed.", Classes = { "faint" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 14, 0, 0) });
    }

    void BuildAddForm()
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "Add a shortcut", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Pick the ribbon command it should run, choose where it works, then set the key.", Classes = { "faint" } });
        _cmdBox = new TextBox { Watermark = "Type a command, e.g. Fillet, Extrude, Look At" };
        _cmdResults = new ListBox { MaxHeight = 170, IsVisible = false };
        _cmdBox.TextChanged += (_, _) => RefreshCommandResults();
        _cmdResults.SelectionChanged += (_, _) =>
        {
            if (_cmdResults.SelectedItem is not CommandHit hit) return;
            _newTarget = MainViewModel.CommandTarget(hit); _newName = hit.Label;
            _cmdResults.IsVisible = false; _cmdResults.SelectedItem = null;
            if (_cmdBox != null) _cmdBox.Text = hit.Label;
            RefreshAddForm();
        };
        panel.Children.Add(_cmdBox); panel.Children.Add(_cmdResults);
        _newInfo = new TextBlock { Classes = { "faint" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        panel.Children.Add(_newInfo);
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        _newScopeBox = new ComboBox { Width = 230 };
        foreach (var (scope, name) in Hotkeys.ScopeChoices(custom: true)) _newScopeBox.Items.Add(new ComboBoxItem { Content = name, Tag = scope });
        _newScopeBox.SelectedIndex = 0;
        _newKeyButton = new Button { Width = 160, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        _newKeyButton.Click += (_, _) => { _capturing = NewKey; _keyNote = ""; BuildKeyboardPage(); Focus(); };
        var create = new Button { Content = "Add shortcut" };
        create.Click += (_, _) => CreateCustomShortcut();
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => _addForm!.IsVisible = false;
        foreach (var c in new Control[] { _newScopeBox, _newKeyButton, create, close }) row.Children.Add(c);
        panel.Children.Add(row);
        _addForm = new Border { Child = panel, IsVisible = false, Padding = new Avalonia.Thickness(12), CornerRadius = new Avalonia.CornerRadius(6), BorderThickness = new Avalonia.Thickness(1), BorderBrush = Avalonia.Media.Brushes.Gray, Margin = new Avalonia.Thickness(0, 4) };
    }

    void RefreshCommandResults()
    {
        var q = (_cmdBox?.Text ?? "").Trim();
        if (_cmdResults == null) return;
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // typing again after choosing starts over
        if (q != _newName) { _newTarget = ""; _newName = ""; RefreshAddForm(); }
        if (terms.Length == 0) { _cmdResults.IsVisible = false; return; }
        var hits = _vm.AllCommandHits()
            .Where(h => terms.All(t => $"{h.Label} {h.Tab} {h.Path} {h.Description}".Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(h => h.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase)).ThenBy(h => h.Label).Take(8).ToList();
        _cmdResults.ItemsSource = hits;
        _cmdResults.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CommandHit>((h, _) =>
        {
            var line = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
            line.Children.Add(new TextBlock { Text = h?.Label ?? "" });
            line.Children.Add(new TextBlock { Text = h?.Path ?? "", Classes = { "faint" } });
            return line;
        });
        _cmdResults.IsVisible = hits.Count > 0;
    }

    void RefreshAddForm()
    {
        if (_newInfo == null || _newKeyButton == null) return;
        _newKeyButton.Content = _capturing == NewKey ? "Press the keys…" : _newGesture.Length > 0 ? Hotkeys.Display(_newGesture) : "Set the key";
        if (_capturing == NewKey) _newKeyButton.Classes.Add("accent"); else _newKeyButton.Classes.Remove("accent");
        _newInfo.Text = _newTarget.Length == 0 ? "No command chosen yet." : "Runs: " + _newTarget.Replace("|", " › ");
    }

    void CreateCustomShortcut()
    {
        string scope = (_newScopeBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? Hotkeys.Global;
        if (_newTarget.Length == 0) { _keyNote = "Choose the command this shortcut should run."; BuildKeyboardPage(); return; }
        if (_newGesture.Length == 0) { _keyNote = "Click “Set the key” and press the combination you want."; BuildKeyboardPage(); return; }
        var clash = Hotkeys.FindConflict("", _newGesture, scope);
        if (clash != null) { _keyNote = $"{Hotkeys.Display(_newGesture)} is already used by “{clash.Name}” {Hotkeys.ScopeName(scope).ToLowerInvariant()}. Choose another key or context."; BuildKeyboardPage(); return; }
        var made = Hotkeys.AddCustom(_newName, _newTarget, scope, _newGesture);
        _keyNote = $"{Hotkeys.Display(_newGesture)} now runs “{_newName}” {Hotkeys.ScopeName(scope).ToLowerInvariant()}.";
        _openNodes.Add("scope:" + scope); _openNodes.Add($"cat:{scope}:{made.Category}");
        _newGesture = ""; _newTarget = ""; _newName = ""; if (_cmdBox != null) _cmdBox.Text = "";
        BuildKeyboardPage();
    }

    void SetAllExpanded(bool open)
    {
        void Walk(ItemCollection items)
        {
            foreach (var item in items.OfType<TreeViewItem>()) { if (item.Tag is string key) { if (open) _openNodes.Add(key); else _openNodes.Remove(key); } item.IsExpanded = open; Walk(item.Items); }
        }
        if (_shortcutTree == null) return;
        Walk(_shortcutTree.Items);
    }

    TreeViewItem Node(string key, string title, bool searching, bool bold)
    {
        var item = new TreeViewItem
        {
            Tag = key, IsExpanded = searching || _openNodes.Contains(key),
            Header = new TextBlock { Text = title, FontWeight = bold ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal },
        };
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property != TreeViewItem.IsExpandedProperty || _shortcutSearch.Trim().Length > 0) return;     // while searching everything is open and says nothing about the user's choice
            if (item.IsExpanded) _openNodes.Add(key); else _openNodes.Remove(key);
        };
        return item;
    }

    void RefreshShortcutTree()
    {
        if (_shortcutTree == null) return;
        var all = Hotkeys.All.ToList();
        var terms = _shortcutSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool searching = terms.Length > 0;
        var shown = all.Where(a => ShortcutMatches(a, terms)).ToList();
        if (_shortcutCount != null) _shortcutCount.Text = searching ? $"{shown.Count} of {all.Count} shortcuts" : $"{all.Count} shortcuts";
        _shortcutTree.Items.Clear();
        foreach (var byScope in shown.GroupBy(a => Hotkeys.Effective(a).Scope).OrderBy(g => ScopeOrder(g.Key)).ThenBy(g => Hotkeys.ScopeName(g.Key)))
        {
            var scopeNode = Node("scope:" + byScope.Key, $"{Hotkeys.ScopeName(byScope.Key)}  ({byScope.Count()})", searching, bold: true);
            foreach (var byCategory in byScope.GroupBy(a => a.Category))
            {
                var categoryNode = Node($"cat:{byScope.Key}:{byCategory.Key}", $"{byCategory.Key}  ({byCategory.Count()})", searching, bold: false);
                foreach (var action in byCategory) categoryNode.Items.Add(new TreeViewItem { Header = ShortcutRow(action) });
                scopeNode.Items.Add(categoryNode);
            }
            _shortcutTree.Items.Add(scopeNode);
        }
        if (shown.Count == 0) _shortcutTree.Items.Add(new TextBlock { Text = "No shortcut matches. Try a shorter word, or a key such as Ctrl+S.", Classes = { "faint" }, Margin = new Avalonia.Thickness(6) });
    }

    Control ShortcutRow(HotkeyAction action)
    {
        var (gesture, scope) = Hotkeys.Effective(action);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,200,150,70"), Margin = new Avalonia.Thickness(0, 2) };
        var label = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = action.Name, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (action.Note.Length > 0) label.Children.Add(new TextBlock { Text = action.Note, Classes = { "faint" } });
        row.Children.Add(label);
        Control where;
        if (Hotkeys.CanChangeScope(action))
        {
            var box = new ComboBox { Width = 190 };
            var choices = Hotkeys.ScopeChoices(action.Custom);
            foreach (var (value, name) in choices) box.Items.Add(new ComboBoxItem { Content = name, Tag = value });
            box.SelectedIndex = Math.Max(0, choices.ToList().FindIndex(c => c.Scope == scope));
            box.SelectionChanged += (_, _) =>
            {
                if (_loading || box.SelectedItem is not ComboBoxItem { Tag: string chosen } || chosen == Hotkeys.Effective(action).Scope) return;
                var clash = Hotkeys.FindConflict(action.Id, Hotkeys.Effective(action).Gesture, chosen);
                if (clash != null) _keyNote = $"{Hotkeys.Display(Hotkeys.Effective(action).Gesture)} is already used by “{clash.Name}” {Hotkeys.ScopeName(chosen).ToLowerInvariant()}. Change one of them first.";
                else Hotkeys.Set(action.Id, Hotkeys.Effective(action).Gesture, chosen);
                BuildKeyboardPage();
            };
            where = box;
        }
        else where = new TextBlock { Text = Hotkeys.ScopeName(scope), Classes = { "soft" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Grid.SetColumn(where, 1); row.Children.Add(where);
        bool waiting = _capturing == action.Id;
        var key = new Button { Tag = action.Id, Content = waiting ? "Press the keys…" : Hotkeys.Display(gesture), Width = 140, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        if (waiting) key.Classes.Add("accent");
        key.Click += (_, _) => { _capturing = action.Id; _keyNote = ""; BuildKeyboardPage(); Focus(); };
        Grid.SetColumn(key, 2); row.Children.Add(key);
        var end = action.Custom ? new Button { Content = "Delete" } : new Button { Content = "Reset", IsEnabled = Hotkeys.IsChanged(action) };
        end.Click += (_, _) =>
        {
            if (action.Custom) { Hotkeys.RemoveCustom(action.Id); _keyNote = $"Removed the shortcut for “{action.Name}”."; }
            else { Hotkeys.Reset(action.Id); _keyNote = ""; }
            _capturing = null; BuildKeyboardPage();
        };
        Grid.SetColumn(end, 3); row.Children.Add(end);
        return row;
    }

    void ShowWorkspace() => WsPath.Text = _vm.HasWorkspace ? _vm.WorkspaceFolder : "No workspace chosen yet";
    void OnHelpContextLocation(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || HelpContextLocation.SelectedIndex < 0) return;
        AppSettings.Current.HelpContextLocation = HelpContextLocation.SelectedIndex == 1 ? "bottom" : "viewport";
        AppSettings.Save();
    }
    void OnWsChange(object? s, RoutedEventArgs e) => _vm.ChooseWorkspaceFolder();
    void OnWsDefault(object? s, RoutedEventArgs e) => _vm.UseDefaultWorkspace();
    void OnWsShow(object? s, RoutedEventArgs e) => _vm.ShowWorkspaceInExplorer();
    void OnWsHome(object? s, RoutedEventArgs e) { if (!_loading) _vm.ShowHomeOnStart = WsHome.IsChecked == true; }
    void OnStartupUpdates(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var settings = AppSettings.Current;
        bool previous = settings.CheckForUpdatesOnStartup;
        settings.CheckForUpdatesOnStartup = StartupUpdates.IsChecked == true;
        if (AppSettings.TrySave(false)) return;
        settings.CheckForUpdatesOnStartup = previous;
        StartupUpdates.IsChecked = previous;
        _vm.Session.Say("Could not save the startup update preference.", true);
    }
    void OnRecoveryEnabled(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.AutoRecoveryEnabled = RecoveryEnabled.IsChecked == true;
        RecoveryInterval.IsEnabled = AppSettings.Current.AutoRecoveryEnabled;
        AppSettings.Save();
    }
    void OnRecoveryInterval(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.AutoRecoveryIntervalMinutes = (int)(RecoveryInterval.Value ?? 5);
        AppSettings.Save();
    }
    async void OnRecoverWork(object? sender, RoutedEventArgs e)
    {
        var path = await new RecoveryFilesWindow(RecoveryStore.Local).ShowDialog<string?>(this);
        if (path != null && _vm.RestoreRecoveryCopy(path)) Close();
    }

    // ------------------------------------------------------------ grid looks
    void BuildGridSwatches()
    {
        GridHost.Children.Clear();
        foreach (var gp in Prism.Rendering.GridPreset.All)
        {
            var p = gp.LightVariant != null && Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? gp.LightVariant : gp;
            Avalonia.Media.Color C((float r, float g, float b) c) => Avalonia.Media.Color.FromRgb((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255));
            var canvas = new Canvas { Width = 128, Height = 76, ClipToBounds = true };
            canvas.Background = new Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
                GradientStops = [new(C(p.Top), 0), new(C(p.Bottom), 1)],
            };
            Avalonia.Controls.Shapes.Path P(string d, Avalonia.Media.Color col, double w, double op, bool round = false) => new()
            {
                Data = Avalonia.Media.Geometry.Parse(d), Stroke = new Avalonia.Media.SolidColorBrush(col), StrokeThickness = w, Opacity = op,
                StrokeLineCap = round ? Avalonia.Media.PenLineCap.Round : Avalonia.Media.PenLineCap.Flat,
            };
            double minor = System.Math.Clamp(p.MinorAlpha * 2.6, 0.3, 1), major = System.Math.Clamp(p.MajorAlpha * 1.5, 0.45, 1);
            if (p.Dots)
            {
                string d = "";
                foreach (var y in new[] { 36, 44, 54, 66 }) for (int i = -3; i <= 3; i++) d += $"M{64 + i * (y - 22) * 0.62:0} {y} h0.1 ";
                canvas.Children.Add(P(d, C(p.Minor), 2.4, major, true));
            }
            else
            {
                string h = "M0 38 H128 M0 46 H128 M0 56 H128 M0 69 H128", v = "M64 28 L-70 80 M64 28 L-20 80 M64 28 L22 80 M64 28 L64 80 M64 28 L106 80 M64 28 L148 80 M64 28 L198 80";
                if (p.Glow > 0) { canvas.Children.Add(P(h + v, C(p.Major), 4.5, 0.18 * p.Glow + 0.06)); }
                canvas.Children.Add(P(v + h, C(p.Minor), 1, minor));
                canvas.Children.Add(P("M0 56 H128 M64 28 L22 80 M64 28 L106 80", C(p.Major), 1.3, major));
            }
            bool on = (AppSettings.Current.GridStyle ?? "classic") == gp.Id;
            var frame = new Border { CornerRadius = new Avalonia.CornerRadius(8), ClipToBounds = true, BorderThickness = new Avalonia.Thickness(2), Child = canvas };
            frame[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(on ? "Accent" : "Line");
            var btn = new Button
            {
                Margin = new Avalonia.Thickness(0, 0, 12, 12), Padding = new Avalonia.Thickness(0), Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0),
                Content = new StackPanel { Spacing = 5, Children = { frame, new TextBlock { Text = gp.Name, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, FontWeight = on ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal } } },
            };
            var id = gp.Id;
            btn.Click += (_, _) => { AppSettings.Current.GridStyle = id; AppSettings.Save(); BuildGridSwatches(); };
            GridHost.Children.Add(btn);
        }
    }

    // ------------------------------------------------------------ pages
    void OnNav(object? sender, SelectionChangedEventArgs e)
    {
        if (PageAppearance == null) return;
        int i = Nav.SelectedIndex;
        PageAppearance.IsVisible = i == 0; PageInteraction.IsVisible = i == 1; PageWorkspace.IsVisible = i == 2; PageToolbars.IsVisible = i == 3; PageRibbon.IsVisible = i == 4; PageAssistant.IsVisible = i == 5; PageKeyboard.IsVisible = i == 6;
        if (i == 5) FillMcp();
    }

    void FillMcp()
    {
        if (McpConfig == null) return;
        McpConfig.Text = "── Claude (claude_desktop_config.json or .mcp.json) ──\n" + Prism.Automation.AssistantService.McpSnippet("claude") +
            "\n── Codex (~/.codex/config.toml) ──\n" + Prism.Automation.AssistantService.McpSnippet("codex");
    }

    async void OnCopyMcp(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        var text = Prism.Automation.AssistantService.McpSnippet(kind);
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is { } clip) await clip.SetTextAsync(text);
        if (McpConfig != null) McpConfig.Text = text;
        if (sender is Button b) { var was = b.Content; b.Content = "Copied ✓"; await Task.Delay(1200); b.Content = was; }
    }

    void OnInstallMcp(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        var note = Prism.Automation.AssistantService.InstallMcpConfig(kind);
        if (McpNote != null) McpNote.Text = note;
    }

    /// <summary>Open a page by id (used by the settings MCP tool and the search box).</summary>
    public void OpenPage(string? page)
    {
        Nav.SelectedIndex = page switch { "appearance" => 0, "interaction" => 1, "workspace" => 2, "toolbars" => 3, "ribbon" => 4, "assistant" => 5, "keyboard" => 6, _ => 0 };
    }

    // ------------------------------------------------------------ settings search

    /// <summary>One searchable entry: what shows, which page, extra words it should match, and the control to focus.</summary>
    sealed record SettingHit(string Title, int Page, string Keys, string? Control)
    {
        public override string ToString() => Title;
    }

    static readonly SettingHit[] SettingIndex =
    [
        new("Theme (Dark, Light, Match Windows)", 0, "theme dark light system windows appearance night color mode", "ThemeDark"),
        new("Interface color", 0, "accent interface color blue teal purple orange rose highlight", "AccentChoice"),
        new("3D view backdrop and grid look", 0, "grid backdrop view colors preset swatch scene", "ShowGridOnStart"),
        new("Show the grid when Prism starts", 0, "grid show start startup", "ShowGridOnStart"),
        new("Help context location", 1, "help context hints status bar viewport tips", "HelpContextLocation"),
        new("Reverse scroll-wheel zoom", 1, "scroll wheel zoom reverse navigation mouse direction", "ReverseScrollZoom"),
        new("Use the right-click menu (marking menu)", 1, "right click marking menu ring radial pie gesture", "MarkingOn"),
        new("Hold time for the right-click menu", 1, "hold time marking menu delay milliseconds", "HoldMs"),
        new("Reference plane size", 1, "reference origin construction plane size scale sketch pick clutter small", "PlaneScale"),
        new("Workspace folder", 2, "workspace folder parts files documents directory location", "WsPath"),
        new("Check for updates at startup", 2, "update check launch startup notification", "StartupUpdates"),
        new("Show the home screen when Prism starts", 2, "home screen start startup open recent", "WsHome"),
        new("Autosave and recovery copies", 2, "autosave recovery backup crash restore unfinished", "RecoveryEnabled"),
        new("Recovery copy interval", 2, "recovery minutes interval autosave every frequency", "RecoveryInterval"),
        new("Recover autosaved work", 2, "recover autosaved work restore lost", "RecoveryEnabled"),
        new("Toolbars over the 3D view", 3, "quickbar toolbar floating buttons in-graphics history panel", null),
        new("Status bar contents", 3, "status bar bottom contents coordinates", null),
        new("Ribbon tabs and commands", 4, "ribbon tabs groups commands customize hide move reorder", "Filter"),
        new("AI provider (Codex, Claude, OpenAI)", 5, "ai assistant provider codex claude anthropic openai chatgpt", "AiProvider"),
        new("AI model", 5, "model models gpt claude sonnet opus codex list refresh", "AiModel"),
        new("AI reasoning effort", 5, "effort reasoning thinking level low medium high", "AiEffort"),
        new("Assistant API key", 5, "api key anthropic openai secret token", "AiKey"),
        new("Remember the API key", 5, "remember key save store persist", "AiRemember"),
        new("Let the assistant open, save and export files", 5, "file files save export allow permission assistant", "AiFiles"),
        new("MCP — use Prism from an external agent", 5, "mcp claude codex agent client setup bridge config json toml", "McpConfig"),
        new("Keyboard shortcuts", 6, "keyboard shortcuts hotkeys keys bindings assign", null),
        new("Add a custom shortcut", 6, "add custom shortcut command ribbon gesture", null),
    ];

    List<SettingHit> _hits = [];

    void OnSearch(object? sender, TextChangedEventArgs e)
    {
        var q = (Search.Text ?? "").Trim();
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _hits = terms.Length == 0 ? [] : SettingIndex.Where(h => terms.All(t => $"{h.Title} {h.Keys}".Contains(t, StringComparison.OrdinalIgnoreCase))).Take(9).ToList();
        SearchResults.ItemsSource = _hits;
        SearchResults.IsVisible = _hits.Count > 0;
        SearchResults.SelectedIndex = _hits.Count > 0 ? 0 : -1;
    }

    void OnSearchKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape) { Search.Text = ""; SearchResults.IsVisible = false; e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Enter && _hits.Count > 0) { GoTo(_hits[Math.Max(0, SearchResults.SelectedIndex)]); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Down && SearchResults.IsVisible) { SearchResults.SelectedIndex = Math.Min(SearchResults.SelectedIndex + 1, _hits.Count - 1); SearchResults.Focus(); e.Handled = true; }
    }

    void OnSearchPick(object? sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is SettingHit h) GoTo(h);
    }

    void GoTo(SettingHit h)
    {
        Nav.SelectedIndex = h.Page;
        Search.Text = ""; SearchResults.IsVisible = false;
        if (h.Control != null)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<Control>(h.Control)?.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    void OnAiRefresh(object? sender, RoutedEventArgs e) { if (_vm != null) _ = _vm.RefreshAssistantModels(force: true); }

    void OnTheme(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.Theme = ThemeLight.IsChecked == true ? "Light" : ThemeSystem.IsChecked == true ? "System" : "Dark";
        AppSettings.Save();
    }

    void OnAccent(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || AccentChoice?.SelectedItem is not ComboBoxItem { Content: string color }) return;
        AppSettings.Current.AccentColor = color;
        AppSettings.Save();
    }

    void OnMarking(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.MarkingMenu = MarkingOn.IsChecked == true; AppSettings.Save();
    }

    void OnShowGridOnStart(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.ShowGridOnStart = ShowGridOnStart.IsChecked == true;
        _vm.Session.ShowGrid = AppSettings.Current.ShowGridOnStart;
        AppSettings.Save();
    }

    void OnReverseScrollZoom(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.ReverseScrollZoom = ReverseScrollZoom.IsChecked == true;
        AppSettings.Save();
    }

    void OnHold(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading || HoldLabel == null) return;
        AppSettings.Current.MarkingHoldMs = (int)HoldMs.Value; HoldLabel.Text = $"Hold time: {(int)HoldMs.Value} ms"; AppSettings.Save();
    }

    void OnPlaneScale(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading || PlaneScaleLabel == null) return;
        AppSettings.Current.PlaneScale = PlaneScale.Value / 100; PlaneScaleLabel.Text = $"Plane size: {(int)PlaneScale.Value} %"; AppSettings.Save();
    }

    // ------------------------------------------------------------ ribbon editor
    Button Btn(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Margin = new Avalonia.Thickness(0, 0, 8, 6) };
        if (tip != null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => { _confirmReset = false; _resetAll.Content = "Reset the whole ribbon"; try { click(); } catch (Exception ex) when (ex is not OutOfMemoryException) { _vm.Session.Say(ex.Message, true); } };
        return b;
    }

    void BuildRibbonEditor()
    {
        RibbonBar.Children.Add(new TextBlock { Text = "Tab", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        RibbonBar.Children.Add(_tabBox); RibbonBar.Children.Add(_showTab); RibbonBar.Children.Add(_tabNote);
        RibbonBar2.Children.Add(_name);
        RibbonBar2.Children.Add(Btn("New tab", () => { _tab = _vm.AddCustomTab(_name.Text ?? ""); _name.Text = ""; FillTabs(); }, "Make an empty tab of your own, then add commands to it."));
        RibbonBar2.Children.Add(_resetTab); RibbonBar2.Children.Add(_deleteTab); RibbonBar2.Children.Add(_resetAll);
        _resetTab.Click += (_, _) => { _vm.ResetTab(_tab); FillTabs(); };
        _deleteTab.Click += (_, _) => { _vm.DeleteCustomTab(_tab); _tab = "Home"; FillTabs(); };
        _resetAll.Click += (_, _) =>
        {
            if (!_confirmReset) { _confirmReset = true; _resetAll.Content = "Click again to confirm"; return; }
            _confirmReset = false; _resetAll.Content = "Reset the whole ribbon"; _vm.ResetRibbon(); _tab = "Home"; FillTabs();
        };
        ToolTip.SetTip(_resetTab, "Put this tab back the way it shipped (and show it again).");
        ToolTip.SetTip(_resetAll, "Undo every ribbon change: shipped tabs return, tabs you made are removed.");
        _tabBox.SelectionChanged += (_, _) => { if (_loading || _tabBox.SelectedItem is not string t) return; _tab = t; LoadTab(); };
        _showTab.Click += (_, _) => { if (_loading) return; _vm.SetTabHidden(_tab, _showTab.IsChecked != true); FillTabs(); };

        RowButtons.Children.Add(Btn("Move up", () => Move(-1)));
        RowButtons.Children.Add(Btn("Move down", () => Move(1)));
        RowButtons.Children.Add(Btn("Remove", Remove, "Take the selected command (or the whole group) out of this tab. It stays in All commands."));
        RowButtons.Children.Add(Btn("New group", NewGroup));
        RowButtons.Children.Add(Btn("Rename group", RenameGroup));
        CatButtons.Children.Add(Btn("Add to the selected group", AddSelected, "Or double-click a command."));
        Rows.SelectionMode = SelectionMode.Single;
        FillCatalog();
        FillTabs();
    }

    void FillTabs()
    {
        _loading = true;
        var tabs = _vm.EditableTabs;
        if (!tabs.Contains(_tab)) _tab = tabs.FirstOrDefault() ?? "Home";
        _tabBox.ItemsSource = tabs; _tabBox.SelectedItem = _tab;
        _loading = false;
        LoadTab();
    }

    void LoadTab()
    {
        _loading = true;
        bool custom = _vm.IsCustomTab(_tab), pinned = _tab is "Home" or "View";
        _cfg = _vm.GetTabCfg(_tab);
        _showTab.IsChecked = !_vm.IsTabHidden(_tab); _showTab.IsEnabled = !pinned;
        _tabNote.Text = custom ? "A tab you made." : pinned ? "Always shown." : _vm.IsTabChanged(_tab) ? "Changed from the shipped version." : "As shipped.";
        _deleteTab.IsVisible = custom; _resetTab.IsVisible = !custom; _resetTab.IsEnabled = _vm.IsTabChanged(_tab);
        _loading = false;
        FillRows(0);
    }

    void FillRows(int select)
    {
        var rows = new List<Row>();
        for (int g = 0; g < _cfg.Count; g++)
        {
            rows.Add(new Row(g, -1, "▾  " + _cfg[g].Title));
            for (int i = 0; i < _cfg[g].Items.Count; i++) rows.Add(new Row(g, i, "        " + _vm.LabelOf(_cfg[g].Items[i])));
        }
        Rows.ItemsSource = rows;
        if (rows.Count > 0) Rows.SelectedIndex = Math.Clamp(select, 0, rows.Count - 1);
    }

    void Commit(int select = -1)
    {
        int keep = select >= 0 ? select : Rows.SelectedIndex;
        _vm.SetTabCfg(_tab, _cfg);
        _loading = true; _resetTab.IsEnabled = _vm.IsTabChanged(_tab); _tabNote.Text = _vm.IsCustomTab(_tab) ? "A tab you made." : "Changed from the shipped version."; _loading = false;
        FillRows(keep);
    }

    int RowIndexOf(int g, int i) => Rows.ItemsSource is List<Row> rows ? rows.FindIndex(r => r.G == g && r.I == i) : 0;

    void Move(int dir)
    {
        if (Rows.SelectedItem is not Row r) return;
        if (r.I < 0)
        {
            int to = r.G + dir; if (to < 0 || to >= _cfg.Count) return;
            (_cfg[r.G], _cfg[to]) = (_cfg[to], _cfg[r.G]);
            Commit(RowIndexOf(to, -1)); return;
        }
        var items = _cfg[r.G].Items; int j = r.I + dir;
        if (j >= 0 && j < items.Count) { (items[r.I], items[j]) = (items[j], items[r.I]); Commit(RowIndexOf(r.G, j)); return; }
        int tg = r.G + dir;                                                   // off the end of the group: into the neighbouring one
        if (tg < 0 || tg >= _cfg.Count) return;
        var id = items[r.I]; items.RemoveAt(r.I);
        var dest = _cfg[tg].Items; int at = dir < 0 ? dest.Count : 0; dest.Insert(at, id);
        Commit(RowIndexOf(tg, at));
    }

    void Remove()
    {
        if (Rows.SelectedItem is not Row r) return;
        int idx = Rows.SelectedIndex;
        if (r.I < 0) _cfg.RemoveAt(r.G); else _cfg[r.G].Items.RemoveAt(r.I);
        Commit(Math.Max(0, idx - 1));
    }

    void NewGroup()
    {
        string name = (_name.Text ?? "").Trim(); if (name.Length == 0) name = $"Group {_cfg.Count + 1}";
        int at = Rows.SelectedItem is Row r ? r.G + 1 : _cfg.Count;
        _cfg.Insert(at, new GroupCfg { Title = name }); _name.Text = "";
        Commit(RowIndexOf(at, -1));
    }

    void RenameGroup()
    {
        if (Rows.SelectedItem is not Row r || string.IsNullOrWhiteSpace(_name.Text)) return;
        _cfg[r.G].Title = _name.Text.Trim(); _name.Text = "";
        Commit(RowIndexOf(r.G, -1));
    }

    void FillCatalog()
    {
        string q = (Filter.Text ?? "").Trim();
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Catalog.ItemsSource = _vm.CatalogEntries
            .Where(c => terms.All(t => c.Label.Contains(t, StringComparison.OrdinalIgnoreCase) || c.Tab.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .Select(c => new Entry(c.Id, $"{c.Label}    ·  {c.Tab}")).ToList();
    }

    void OnFilter(object? sender, TextChangedEventArgs e) { if (Catalog != null && _vm != null) FillCatalog(); }
    void OnCatalogDouble(object? sender, Avalonia.Input.TappedEventArgs e) => AddSelected();

    void AddSelected()
    {
        if (Catalog.SelectedItem is not Entry en) return;
        if (_cfg.Count == 0) _cfg.Add(new GroupCfg { Title = "Group 1" });
        var row = Rows.SelectedItem as Row ?? new Row(_cfg.Count - 1, -1, "");
        var items = _cfg[row.G].Items;
        int at = row.I < 0 ? items.Count : row.I + 1;
        items.Insert(at, en.Id);
        Commit(RowIndexOf(row.G, at));
    }

    // ------------------------------------------------------------ toolbars
    void BuildToolbarEditors()
    {
        var show = new CheckBox { Content = "Show the toolbar over the 3D view", IsChecked = AppSettings.Current.ShowQuickBar };
        show.Click += (_, _) => { AppSettings.Current.ShowQuickBar = show.IsChecked == true; AppSettings.Save(); };
        ToolbarHost.Children.Add(show);
        ToolbarHost.Children.Add(ListEditor(
            "In-graphics toolbar", "The floating toolbar at the top of the 3D view. Any ribbon command can go here; add a divider to group buttons.",
            () => [.. AppSettings.Current.QuickBar ?? [.. MainViewModel.DefaultQuickBar]],
            l => { AppSettings.Current.QuickBar = l; AppSettings.Save(); },
            [("|", "—  divider  —"), .. _vm.CatalogEntries.Select(c => (c.Id, $"{c.Label}    ·  {c.Tab}"))],
            id => id == "|" ? "—  divider  —" : _vm.LabelOf(id)));
        ToolbarHost.Children.Add(ListEditor(
            "Status bar", "What the bottom bar shows on its right-hand side (the message on the left is always there).",
            () => [.. AppSettings.Current.StatusBar ?? [.. MainViewModel.DefaultStatusBar]],
            l => { AppSettings.Current.StatusBar = l; AppSettings.Save(); },
            [.. MainViewModel.StatusCatalog.Select(c => (c.Id, c.Label))],
            id => MainViewModel.StatusCatalog.FirstOrDefault(c => c.Id == id).Label ?? id));
    }

    /// <summary>An ordered list the user can rearrange, with a catalog to add from and a way back to the shipped list.</summary>
    Control ListEditor(string title, string help, Func<List<string>> current, Action<List<string>?> save, IReadOnlyList<(string Id, string Text)> catalog, Func<string, string> label)
    {
        var items = current();
        var shown = new ListBox { Height = 230, SelectionMode = SelectionMode.Single };
        var cat = new ListBox { Height = 196, SelectionMode = SelectionMode.Single };
        var filter = new TextBox { Watermark = "Search" };
        foreach (var l in new[] { shown, cat }) { l[!ListBox.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FieldBg"); l.BorderThickness = new Avalonia.Thickness(1); l[!ListBox.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line"); }
        void FillShown(int select)
        {
            shown.ItemsSource = items.Select(label).ToList();
            if (items.Count > 0) shown.SelectedIndex = Math.Clamp(select, 0, items.Count - 1);
        }
        void FillCat()
        {
            var terms = (filter.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            cat.ItemsSource = catalog.Where(c => terms.All(t => c.Text.Contains(t, StringComparison.OrdinalIgnoreCase))).Select(c => new Entry(c.Id, c.Text)).ToList();
        }
        void Commit(int select) { save([.. items]); FillShown(select); }
        void Add()
        {
            if (cat.SelectedItem is not Entry en) return;
            int at = shown.SelectedIndex >= 0 ? shown.SelectedIndex + 1 : items.Count;
            items.Insert(at, en.Id); Commit(at);
        }
        void Move(int dir)
        {
            int i = shown.SelectedIndex, j = i + dir; if (i < 0 || j < 0 || j >= items.Count) return;
            (items[i], items[j]) = (items[j], items[i]); Commit(j);
        }
        Button B(string text, Action a) { var b = new Button { Content = text, Margin = new Avalonia.Thickness(0, 6, 8, 0) }; b.Click += (_, _) => a(); return b; }
        filter.TextChanged += (_, _) => FillCat();
        cat.DoubleTapped += (_, _) => Add();
        FillShown(0); FillCat();

        var left = new StackPanel { Children = { new TextBlock { Text = "Shown, in order", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 0, 0, 6) }, shown,
            new WrapPanel { Children = { B("Move up", () => Move(-1)), B("Move down", () => Move(1)),
                B("Remove", () => { int i = shown.SelectedIndex; if (i < 0) return; items.RemoveAt(i); Commit(Math.Max(0, i - 1)); }),
                B("Back to the shipped list", () => { save(null); items = current(); FillShown(0); }) } } } };
        var right = new StackPanel { Children = { new TextBlock { Text = "Available", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 0, 0, 6) }, filter, cat,
            new WrapPanel { Children = { B("Add after the selected one", Add) } } } };
        right.Children[1].Margin = new Avalonia.Thickness(0, 0, 0, 6);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        grid.Children.Add(left); Grid.SetColumn(right, 2); grid.Children.Add(right);
        return new StackPanel { Spacing = 6, Children = { new TextBlock { Text = title, FontSize = 16, FontWeight = Avalonia.Media.FontWeight.SemiBold },
            new TextBlock { Text = help, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "soft" } }, grid } };
    }
}
