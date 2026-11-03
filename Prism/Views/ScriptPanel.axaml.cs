using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Prism.Core.Scripting;
using Prism.ViewModels;

namespace Prism.Views;

/// <summary>The script editor: code, run/stop, a console, and shortcuts to VS Code and PyCharm.</summary>
public partial class ScriptPanel : UserControl
{
    readonly StringBuilder _console = new();
    ScriptSession? _bound;
    bool _loading;

    public ScriptPanel()
    {
        InitializeComponent();
        Editor.TextChanged += (_, _) => OnEditorChanged();
        ActualThemeVariantChanged += (_, _) => Highlight();
        DataContextChanged += (_, _) => Bind();
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    void Bind()
    {
        if (Vm?.Script is not { } s || ReferenceEquals(s, _bound)) return;
        _bound = s;
        s.Output += (text, err) => Dispatcher.UIThread.Post(() => Append(text, err));
        s.StateChanged += () => Dispatcher.UIThread.Post(Refresh);
        s.Loaded += () => Dispatcher.UIThread.Post(() => { _loading = true; Editor.Text = s.Text; _loading = false; UpdateGutter(); if (s.ErrorLine == 0) Editor.CaretIndex = 0; Refresh(); });
        Refresh();
    }

    void OnEditorChanged()
    {
        UpdateGutter(); Highlight();
        if (_loading || _bound == null) return;
        _bound.Text = Editor.Text ?? ""; _bound.Dirty = true; Refresh();
    }

    void UpdateGutter()
    {
        int lines = (Editor.Text ?? "").Split('\n').Length;
        Gutter.Text = string.Join("\n", Enumerable.Range(1, lines));
    }

    IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    /// <summary>Redraw the colored copy of the text that sits under the editor. A very long file stays plain, so typing never lags.</summary>
    void Highlight()
    {
        var text = Editor.Text ?? "";
        var plain = Brush("Ink");
        var inlines = new InlineCollection();
        void Add(string segment, IBrush brush)
        {
            var lines = segment.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) inlines.Add(new LineBreak());
                if (lines[i].Length > 0) inlines.Add(new Run(lines[i]) { Foreground = brush });
            }
        }
        if (text.Length > 200_000) Add(text, plain);
        else foreach (var span in PythonHighlighter.Tokenize(text))
            Add(text.Substring(span.Start, span.Length), span.Kind == SyntaxKind.Text ? plain : Brush("Syntax" + span.Kind));
        Code.Inlines = inlines;
    }

    void Refresh()
    {
        if (_bound is not { } s) return;
        RunBtn.IsEnabled = !s.Running; StopBtn.IsEnabled = s.Running;
        NameText.Text = s.DisplayName + (s.Dirty ? " •" : "");
        var lang = s.Language;
        StatusText.Text = s.Running ? "Running..." : lang.IsAvailable(out var d) ? $"{lang.Name}: {d}" + (s.ErrorLine > 0 ? $"   |   error on line {s.ErrorLine}: {s.ErrorText}" : "") : d;
        if (!s.Running && s.ErrorLine > 0) GoToLine(s.ErrorLine);
    }

    void Append(string text, bool error)
    {
        _console.Append(error ? "! " : "").Append(text).Append('\n');
        if (_console.Length > 60000) _console.Remove(0, 20000);
        Console.Text = _console.ToString();
        Console.CaretIndex = Console.Text.Length;
    }

    void GoToLine(int line)
    {
        var text = Editor.Text ?? ""; int idx = 0;
        for (int i = 1; i < line && idx >= 0; i++) { idx = text.IndexOf('\n', idx); if (idx >= 0) idx++; }
        if (idx < 0) return;
        Editor.CaretIndex = idx; Editor.Focus();
    }

    // ------------------------------------------------------------ toolbar
    async void OnRun(object? sender, RoutedEventArgs e) { if (_bound == null) return; _bound.Text = Editor.Text ?? ""; await _bound.RunAsync(); }
    void OnStop(object? sender, RoutedEventArgs e) => _bound?.Stop();
    void OnClose(object? sender, RoutedEventArgs e) { if (Vm != null) Vm.ShowScriptPanel = false; }
    void OnClear(object? sender, RoutedEventArgs e) { _console.Clear(); Console.Text = ""; }
    void OnNew(object? sender, RoutedEventArgs e) => _bound?.New();

    void OnEditInMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control anchor) return;
        MenuItem Item(string header, Action run) { var item = new MenuItem { Header = header }; item.Click += (_, _) => run(); return item; }
        new MenuFlyout
        {
            Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft,
            ItemsSource = new object[] { Item("VS Code", () => Vm?.OpenScriptInEditor("vscode")), Item("PyCharm", () => Vm?.OpenScriptInEditor("pycharm")), new Separator(), Item("Scripts folder", () => Vm?.OpenScriptsFolder()) },
        }.ShowAt(anchor);
    }

    async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync();

    async System.Threading.Tasks.Task SaveAsync()
    {
        if (_bound == null || Vm == null) return;
        _bound.Text = Editor.Text ?? "";
        try
        {
            if (_bound.Path != null) { _bound.Save(); return; }
            if (Vm.PickSaveFile == null) return;
            ScriptPaths.Prepare();
            var ext = _bound.Language.Extension.TrimStart('.');
            var path = await Vm.PickSaveFile("Save script", "my_script." + ext, ext);
            if (path != null) _bound.Save(path);
        }
        catch (Exception ex) { Vm.Session.Say("Could not save the script: " + ex.Message, true); }
    }

    void OnOpenMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control anchor || _bound == null) return;
        var items = new System.Collections.Generic.List<object>();
        var dir = ScriptPaths.Prepare();
        foreach (var f in Directory.GetFiles(dir, "*" + _bound.Language.Extension).Where(f => !Path.GetFileName(f).StartsWith('_')).OrderBy(f => f))
        {
            var path = f; var mi = new MenuItem { Header = Path.GetFileName(f) };
            mi.Click += (_, _) => { try { _bound.Open(path); } catch (Exception ex) { Vm?.Session.Say("Could not open it: " + ex.Message, true); } };
            items.Add(mi);
        }
        var other = new MenuItem { Header = "Browse..." };
        other.Click += async (_, _) =>
        {
            if (Vm?.PickOpenFile == null) return;
            var p = await Vm.PickOpenFile("Open script", ScriptLanguages.All.Select(l => "*" + l.Extension).ToArray());
            if (p != null) { try { _bound.Open(p); } catch (Exception ex) { Vm.Session.Say("Could not open it: " + ex.Message, true); } }
        };
        items.Add(new Separator()); items.Add(other);
        new MenuFlyout { ItemsSource = items, Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft }.ShowAt(anchor);
    }

    // ------------------------------------------------------------ editing niceties
    async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { OnRun(sender, e); e.Handled = true; return; }
        if (e.Key == Key.F5) { OnStop(sender, e); e.Handled = true; return; }
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { await SaveAsync(); e.Handled = true; return; }
        var text = Editor.Text ?? ""; int caret = Editor.CaretIndex;
        if (e.Key == Key.Tab && Editor.SelectionStart == Editor.SelectionEnd)
        {
            Editor.SelectedText = "    "; e.Handled = true; return;   // typed in place, so Undo still works
        }
        if (e.Key == Key.Enter && Editor.SelectionStart == Editor.SelectionEnd)
        {
            // keep the indentation of the line, and add a level after a colon
            int ls = text.LastIndexOf('\n', Math.Max(caret - 1, 0)); ls = caret == 0 ? 0 : ls + 1;
            var line = text[ls..caret]; var indent = new string(line.TakeWhile(c => c == ' ').ToArray());
            if (line.TrimEnd().EndsWith(':')) indent += "    ";
            Editor.SelectedText = "\n" + indent; e.Handled = true;
        }
    }
}
