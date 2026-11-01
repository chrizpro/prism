using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    public string? HoveredToolHelpIcon { get; set; }
    public bool HasActiveToolExample => Help.ToolHelp.ForCommand(ActiveCommand) != null;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasActiveAssistance)), NotifyPropertyChangedFor(nameof(AssistanceStatus))]
    public partial string ActiveToolInstruction { get; set; } = "";
    public bool HasActiveAssistance => ActiveToolInstruction.Length > 0;
    public string AssistanceStatus => ActiveCommand != null && !string.IsNullOrEmpty(CommandError) ? CommandError
        : Session.StatusIsError || !HasActiveAssistance ? Session.Status : "";

    partial void OnActiveCommandChanged(Command? oldValue, Command? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnAssistanceChanged;
            foreach (var slot in oldValue.Slots) slot.PropertyChanged -= OnAssistanceChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnAssistanceChanged;
            foreach (var slot in newValue.Slots) slot.PropertyChanged += OnAssistanceChanged;
        }
        OnPropertyChanged(nameof(HasActiveToolExample));
        UpdateActiveToolInstruction();
    }

    void OnAssistanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SelectionSlot || e.PropertyName is nameof(Command.PanelHint) or nameof(Command.DisplayTitle) or nameof(Command.HasGizmoOptions))
            UpdateActiveToolInstruction();
    }

    void UpdateActiveToolInstruction()
    {
        var command = ActiveCommand;
        if (command?.HasPanelStep == true) { ActiveToolInstruction = command.PanelHint; return; }
        if (Session.ActiveTool is { } tool) { ActiveToolInstruction = tool.Prompt; return; }
        if (command == null) { ActiveToolInstruction = ""; return; }
        var slots = command.Slots.Where(s => s.IsVisible && !s.IsResult).ToArray();
        var active = command.ActiveSlot;
        var missing = slots.FirstOrDefault(s => !s.Optional && s.Items.Count == 0);
        if (active is { Optional: true, IsVisible: true })
            ActiveToolInstruction = $"Select {active.DisplayLabel.ToLowerInvariant()} in the viewport, or leave this optional selection empty.";
        else if (missing != null)
            ActiveToolInstruction = command switch
            {
                LoftCommand => "Select the first outline. Then select another outline on a different plane.",
                ExtrudeCommand when missing == slots.FirstOrDefault() => "Select an outline or flat face to extend. Then set the distance and direction.",
                SweepCommand when missing == slots.FirstOrDefault() => "Select the outline to carry along the path.",
                SweepCommand => "Select the path curve that the outline should follow.",
                _ => $"Select {missing.DisplayLabel.ToLowerInvariant()} in the viewport."
            };
        else if (command is LoftCommand && command.PreviewEnabled && Session.Preview.Count == 0)
            ActiveToolInstruction = "Select another outline on a different plane. Select outlines in the order the shape should pass through them.";
        else if (slots.Length > 0 && Session.Preview.Count > 0)
            ActiveToolInstruction = $"Review the preview and adjust the settings. Choose {command.CommitText} when ready.";
        else if (command is ExtrudeCommand or SweepCommand or LoftCommand)
            ActiveToolInstruction = $"Adjust the settings and choose {command.CommitText} when ready.";
        else ActiveToolInstruction = command.PanelHint;
    }
}
