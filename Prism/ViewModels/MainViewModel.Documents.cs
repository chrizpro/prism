using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

/// <summary>One open document shown as a tab above the viewport (like documents in Affinity).</summary>
public sealed partial class DocumentTab : ObservableObject
{
    public CadSession Session { get; }
    public int Number { get; }
    [ObservableProperty] public partial string Title { get; set; } = "Untitled";
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty] public partial bool IsPinned { get; set; }
    /// <summary>The name shown while the part has not been saved (a part made from inside an assembly starts with the name you gave it).</summary>
    public string? DisplayName { get; set; }
    [ObservableProperty] public partial bool IsDirty { get; set; }
    /// <summary>The tab has unsaved changes and the close button was pressed once.</summary>
    [ObservableProperty] public partial bool ConfirmClose { get; set; }
    /// <summary>A snapshot of the viewport taken when the tab was last left, shown when hovering the tab.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPreview))] public partial Avalonia.Media.Imaging.Bitmap? Preview { get; set; }
    public bool HasPreview => Preview != null;
    public void RefreshDetail() => OnPropertyChanged(nameof(Detail));
    public string Detail => $"{Session.Objects.Count} object{(Session.Objects.Count == 1 ? "" : "s")}{(Session.Sketch != null ? " · sketching" : "")}{(IsDirty ? " · unsaved changes" : "")}";

    public DocumentTab(CadSession session, int number)
    {
        Session = session; Number = number;
        Refresh();
        session.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(CadSession.FilePath) or nameof(CadSession.Dirty)) Refresh(); };
    }

    public void Refresh()
    {
        Title = Session.FilePath is { } p ? Path.GetFileNameWithoutExtension(p) : DisplayName ?? (Number <= 1 ? "Untitled" : $"Untitled {Number}");
        IsDirty = Session.Dirty;
        if (!IsDirty) ConfirmClose = false;
    }

    public bool IsPristine => Session.FilePath == null && !Session.Dirty && Session.Objects.Count == 0 && !Session.CanUndo;
}

/// <summary>Several documents open at once, each with its own scene, history, camera and studies.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<DocumentTab> Documents { get; } = [];
    [ObservableProperty] public partial DocumentTab? ActiveDocument { get; set; }
    int _docCounter;
    public event Action<CadSession?, CadSession>? SessionSwapped;

    // the subscriptions to a session, kept as fields so they can be removed again when another document takes over
    Action? _onScene, _onSelection, _onView, _onStudy, _onPartInfo;
    Action<CadSession.InputRequest>? _onInput;
    PropertyChangedEventHandler? _onProp;

    public SceneObject? SelectedComponent => Session.SelectedObjects.FirstOrDefault(o => o.Component != null);
    void ToggleGround()
    {
        var c = SelectedComponent;
        if (c?.Component == null) { Session.Say("Select a component first.", true); return; }
        Session.SetGround(c.Component.Key, !c.Component.Grounded);
        Session.Say(c.Component.Grounded ? $"{c.Name} is free again." : $"{c.Name} is grounded (fixed in place).");
        RefreshRibbon();
    }

    void InitDocuments()
    {
        CadSession.DocumentProvider = () => Documents.Select(d => (d.Title, d.Session)).ToList();
        CadSession.CreatePartHandler = CreatePartInAssembly;
        var t = new DocumentTab(Session, ++_docCounter) { IsActive = true };
        Documents.Add(t); _activeDocField = t; ActiveDocument = t;
    }

    DocumentTab? _activeDocField;

    /// <summary>Raised just before another tab takes over, so the view can snapshot the viewport for the tab being left.</summary>
    public event Action<DocumentTab>? DocumentLeaving;

    partial void OnActiveDocumentChanged(DocumentTab? oldValue, DocumentTab? newValue)
    {
        if (oldValue != null && newValue != null && !ReferenceEquals(oldValue, newValue)) DocumentLeaving?.Invoke(oldValue);
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue == null) return;
        newValue.IsActive = true; newValue.ConfirmClose = false;
        OnPropertyChanged(nameof(ShowEditBanner));
        if (!ReferenceEquals(newValue.Session, Session)) SwapSession(newValue.Session);
    }

    /// <summary>Everything the UI listens to on the current document.</summary>
    void AttachSession(CadSession s)
    {
        s.ProtectPendingCommand = true;
        _onScene = () => { RebuildBrowser(); UpdateSelectionInfo(); UpdateTitle(); RefreshRibbon(); ApplyWorkspace(); SyncLook(); InvalidateStudios(); };
        _onSelection = () => { UpdateSelectionInfo(); RefreshRibbon(); SyncLook(); RefreshStudios(); };
        _onView = () => { CommandError = s.CommandError;RefreshGizmoPanel(); };
        _onStudy = RefreshSim;
        _onInput = r => { _input = r; InputPrompt = r.Prompt; InputText = r.Initial; InputOpen = true; };
        _onProp = (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(CadSession.Cursor3): CursorText = $"X {s.Cursor3.X:0.00}   Y {s.Cursor3.Y:0.00}   Z {s.Cursor3.Z:0.00}"; break;
                case nameof(CadSession.FilePath) or nameof(CadSession.Dirty): UpdateTitle(); break;
                case nameof(CadSession.Display) or nameof(CadSession.Zebra) or nameof(CadSession.Analysis) or nameof(CadSession.ShowBounds) or nameof(CadSession.ShowGrid) or nameof(CadSession.Ortho) or nameof(CadSession.Mode) or nameof(CadSession.XRay): RefreshRibbon(); break;
                case nameof(CadSession.CommandError): CommandError = s.CommandError; break;
                case nameof(CadSession.Status): RefreshStudioActivity(); OnPropertyChanged(nameof(AssistanceStatus)); break;
                case nameof(CadSession.StatusIsError): OnPropertyChanged(nameof(AssistanceStatus)); break;
            }
        };
        s.SceneChanged += _onScene; s.SelectionChanged += _onSelection; s.StateChanged += OnStateChanged; s.TimelineChanged += RebuildHistory;
        s.LogChanged += RefreshStudioActivity;
        HookRender(s);
        _onPartInfo = SyncPartInfo; s.PartInfoChanged += _onPartInfo;
        s.ViewChanged += _onView; s.StudyChanged += _onStudy; s.InputRequested += _onInput; s.SketchChanged += RebuildSketchPanel; s.PropertyChanged += _onProp;
    }

    void DetachSession(CadSession s)
    {
        s.LogChanged -= RefreshStudioActivity;
        if (_onScene != null) s.SceneChanged -= _onScene;
        if (_onSelection != null) s.SelectionChanged -= _onSelection;
        if (_onView != null) s.ViewChanged -= _onView;
        if (_onStudy != null) s.StudyChanged -= _onStudy;
        if (_onPartInfo != null) s.PartInfoChanged -= _onPartInfo;
        if (_onInput != null) s.InputRequested -= _onInput;
        if (_onProp != null) s.PropertyChanged -= _onProp;
        UnhookRender(s);
        s.StateChanged -= OnStateChanged; s.TimelineChanged -= RebuildHistory; s.SketchChanged -= RebuildSketchPanel;
    }

    CadSession _session = new();
    public CadSession Session { get => _session; private set => SetProperty(ref _session, value); }

    void SwapSession(CadSession s)
    {
        var old = Session;
        DetachSession(old);
        InputOpen = false; _input = null;
        Session = s;
        AttachSession(s);
        // bring every panel in line with the document that is now in front
        IsSketching = s.Sketch != null;
        ActiveCommand = s.ActiveCommand;
        OnStateChanged();
        RebuildBrowser(); RebuildHistory(); UpdateSelectionInfo(); UpdateTitle();
        RebuildSketchPanel(); RefreshSim(); SyncLook(); ApplyWorkspace(); RefreshRibbon(); UpdateRender();
        RefreshStudios();
        CommandError = s.CommandError;
        s.Invalidate();
        SessionSwapped?.Invoke(old, s);
    }

    // ------------------------------------------------------------ tab commands
    /// <summary>A new, empty document in its own tab.</summary>
    public DocumentTab NewDocumentTab()
    {
        var t = new DocumentTab(new CadSession(), ++_docCounter);
        Documents.Add(t);
        ActiveDocument = t;
        return t;
    }

    /// <summary>Run an action on a fresh document: the current tab if it is still empty and untouched, otherwise a new tab.</summary>
    public CadSession OpenTarget()
    {
        if (ActiveDocument is { IsPristine: true } cur) return cur.Session;
        return NewDocumentTab().Session;
    }

    public void ActivateDocument(DocumentTab t) { if (Documents.Contains(t)) ActiveDocument = t; }

    public void CloseDocument(DocumentTab t)
    {
        if (!Documents.Contains(t)) return;
        if (t.IsDirty && !t.ConfirmClose)
        {
            t.ConfirmClose = true;
            Session.Say($"'{t.Title}' has unsaved changes. Save it first (Ctrl+S), or press × again to discard them.", true);
            return;
        }
        if (EditingPart != null && (ReferenceEquals(EditingPart.PartTab, t) || ReferenceEquals(EditingPart.AssemblyTab, t))) { EditingPart.PartTab.Session.SetContext([]); EditingPart = null; }
        int i = Documents.IndexOf(t);
        bool wasActive = ReferenceEquals(t, ActiveDocument);
        if (Documents.Count == 1)
        {
            // never leave the window without a document: replace the last one with a fresh empty one
            var fresh = new DocumentTab(new CadSession(), ++_docCounter);
            Documents.Add(fresh); Documents.Remove(t); ActiveDocument = fresh;
            return;
        }
        Documents.Remove(t);
        if (wasActive) ActiveDocument = Documents[Math.Min(i, Documents.Count - 1)];
    }

    public void NextDocument(int step)
    {
        if (Documents.Count < 2 || ActiveDocument == null) return;
        int i = (Documents.IndexOf(ActiveDocument) + step + Documents.Count) % Documents.Count;
        ActiveDocument = Documents[i];
    }
}
