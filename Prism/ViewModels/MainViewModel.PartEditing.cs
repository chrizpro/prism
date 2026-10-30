using System;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;
using Prism.Core;

namespace Prism.ViewModels;

/// <summary>A part being modeled or edited from inside an assembly, and where to return to.</summary>
public sealed record PartEdit(DocumentTab AssemblyTab, DocumentTab PartTab, string? ComponentKey, string Name, string ReturnWorkspace);

/// <summary>
/// The hybrid way of working: from an assembly, start a new part or open one of its components for editing. The part opens in its own tab
/// in the Design workspace (where Solid, Surface, Freeform, NURBS and Sheet Metal all live); Back to assembly puts the result in the assembly.
/// </summary>
public sealed partial class MainViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEditBanner)), NotifyPropertyChangedFor(nameof(EditBannerText))] public partial PartEdit? EditingPart { get; set; }

    /// <summary>Shown only while the part being edited is the one in front.</summary>
    public bool ShowEditBanner => EditingPart != null && ReferenceEquals(ActiveDocument, EditingPart.PartTab);
    public string EditBannerText => EditingPart == null ? "" : $"Editing {EditingPart.Name}  ·  in {EditingPart.AssemblyTab.Title}";

    /// <summary>Where each kind of new part starts: the workspace and the ribbon tab.</summary>
    static (string workspace, string tab) LandingFor(int kind) => kind switch
    {
        1 => ("design", "Sheet Metal"), 2 => ("design", "Surface"), 3 => ("design", "Freeform"), 4 => ("design", "NURBS"), 5 => ("scan", "Mesh"), _ => ("design", "Solid"),
    };

    void GoTo(string workspace, string tab)
    {
        SetWorkspace(workspace);
        if (Tabs.FirstOrDefault(t => t.Title == tab && t.IsVisible) is { } t) SelectTab(t);
    }

    /// <summary>Start a new part for the assembly in front: a new tab, in the Design workspace, on the tab for what is being made.</summary>
    public void CreatePartInAssembly(string name, int kind)
    {
        if (ActiveDocument is not { } assembly) return;
        string back = WorkspaceId;
        var part = NewDocumentTab();
        part.DisplayName = name; part.Refresh();
        part.Session.SetPartInfo(new PartInfo(Description: name)); SyncPartInfo();
        var (workspace, tab) = LandingFor(kind);
        GoTo(workspace, tab);
        part.Session.SetContext(assembly.Session.ContextGhostsFor(null));
        EditingPart = new PartEdit(assembly, part, null, name, back);
        Session.Say($"Model {name} here. When you are done, press Back to assembly.");
    }

    /// <summary>Open the part behind the selected component for editing in the Design workspace.</summary>
    public void EditComponentPart(SceneObject? component = null)
    {
        component ??= SelectedComponent;
        if (component?.Component is not { } ci || ActiveDocument is not { } assembly) { Session.Say("Select a component first.", true); return; }
        DocumentTab? part = ci.SourceDoc is { } id ? Documents.FirstOrDefault(d => d.Session.DocId == id) : null;
        if (part == null && ci.SourcePath is { } path && File.Exists(path))
        {
            part = Documents.FirstOrDefault(d => d.Session.FilePath == path);
            if (part == null) { OpenPath(path); part = ActiveDocument; }
        }
        if (part == null || ReferenceEquals(part, assembly)) { Session.Say($"{ci.PartName} is an embedded copy with no part file or open tab to edit. Open the part itself, then insert it again.", true); return; }
        string back = WorkspaceId;
        ActivateDocument(part);
        var (workspace, tab) = LandingFor(part.Session.Objects.Any(o => o.Form != null) ? 3 : 0);
        GoTo(workspace, tab);
        part.Session.SetContext(assembly.Session.ContextGhostsFor(ci.Key));
        EditingPart = new PartEdit(assembly, part, ci.Key, component.Name, back);
        Session.Say($"Editing {component.Name}. Press Back to assembly to bring the changes in.");
    }

    /// <summary>Finish the part and return to the assembly: a new part is placed in it, an edited one is brought up to date.</summary>
    public void BackToAssembly()
    {
        if (EditingPart is not { } edit) return;
        EditingPart = null;
        edit.PartTab.Session.SetContext([]);
        if (!Documents.Contains(edit.AssemblyTab)) { Session.Say("The assembly was closed, so there is nothing to go back to."); return; }
        var part = edit.PartTab.Session;
        ActivateDocument(edit.AssemblyTab);
        var assembly = edit.AssemblyTab.Session;
        if (edit.ComponentKey == null)
        {
            if (CadSession.PartGeometry(part) == null) assembly.Say($"{edit.Name} has no solid body yet, so nothing was added. Use Create Part to start again.", true);
            else { assembly.StartCommand(InsertComponentCommand.ForDocument(part, edit.Name)); assembly.CommitCommand(); assembly.Say($"{edit.Name} is in the assembly."); }
        }
        else if (assembly.RefreshComponentFromDocument(edit.ComponentKey, part)) assembly.Say($"{edit.Name} is up to date.");
        else assembly.Say($"{edit.Name} has no solid body to bring back.", true);
        SetWorkspace(edit.ReturnWorkspace);
    }
}
