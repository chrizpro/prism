using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Automation;

namespace Prism.ViewModels;

public sealed partial class ChatMessage : ObservableObject
{
    public ChatRole Role { get; init; }
    [ObservableProperty] public partial string Text { get; set; } = "";
    /// <summary>Images attached to a user message (thumbnails in the chat).</summary>
    public IReadOnlyList<AssistantImage>? Images { get; init; }
    public bool HasImages => Images is { Count: > 0 };
    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public bool IsTool => Role == ChatRole.Tool;
    public bool IsNote => Role is ChatRole.Note;
    public bool IsError => Role == ChatRole.Error;
}

/// <summary>The AI assistant side panel.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<ChatMessage> ChatMessages { get; } = [];
    /// <summary>Images staged for the next message; cleared when it is sent.</summary>
    public ObservableCollection<AssistantImage> AssistantAttachments { get; } = [];
    public AssistantService? Assistant { get; private set; }

    public const int AssistantAttachmentLimit = 4;

    [ObservableProperty] public partial bool AssistantOpen { get; set; }
    [ObservableProperty] public partial bool AssistantSettingsOpen { get; set; }
    [ObservableProperty] public partial bool AssistantBusy { get; set; }
    [ObservableProperty] public partial string AssistantInput { get; set; } = "";
    [ObservableProperty] public partial int AssistantProviderIndex { get; set; }
    [ObservableProperty] public partial string AssistantModel { get; set; } = "";
    [ObservableProperty] public partial string AssistantKey { get; set; } = "";
    [ObservableProperty] public partial bool AssistantAllowFiles { get; set; }
    [ObservableProperty] public partial bool AssistantRemember { get; set; }
    [ObservableProperty] public partial int AssistantEffortIndex { get; set; }
    [ObservableProperty] public partial bool AssistantModelsBusy { get; set; }
    [ObservableProperty] public partial string AssistantModelsStatus { get; set; } = "";
    /// <summary>Model ids offered for the current provider ("" rows are the provider default).</summary>
    public ObservableCollection<string> AssistantModels { get; } = [];
    /// <summary>Effort labels for the current model; row 0 is always "Default".</summary>
    public ObservableCollection<string> AssistantEffortChoices { get; } = ["Default"];
    public bool AssistantHasEfforts => AssistantEffortChoices.Count > 1;
    // The provider dropdowns list Codex (login), Claude (API key) and OpenAI (API key) in enum order.
    AiProvider AssistantProvider => (AiProvider)(AssistantProviderIndex + 1);
    public bool AssistantNeedsKey => AssistantProvider != AiProvider.CodexCli;
    public bool AssistantNeedsLogin => Assistant?.NeedsLogin == true;
    /// <summary>Opens a terminal running `codex login` so the user can sign in again.</summary>
    public void CodexLogin() { try { Assistant?.LaunchCodexLogin(); AssistantNote("A sign-in window opened for Codex — finish it, then resend your message."); } catch (Exception e) { AssistantNote(e.Message); } OnPropertyChanged(nameof(AssistantNeedsLogin)); }
    /// <summary>True when the API provider rejected the key; the panel offers to open the key settings.</summary>
    public bool AssistantNeedsKeyFix => Assistant?.NeedsKeyFix == true;
    /// <summary>Opens the assistant's settings strip so the key can be fixed.</summary>
    public void FixAssistantKey() { AssistantSettingsOpen = true; Assistant?.ClearKeyFix(); OnPropertyChanged(nameof(AssistantNeedsKeyFix)); }
    public bool AssistantEmpty => ChatMessages.Count == 0;
    public string AssistantProviderHint => AssistantProvider switch
    {
        AiProvider.CodexCli => "Uses your Codex login (run codex login once). Experimental.",
        AiProvider.Anthropic => "Uses your own Anthropic API key. Billed to your API account.",
        _ => "Uses your own OpenAI API key — works for gpt-* and codex-* models. Billed to your API account.",
    };

    bool _assistantLoading;
    List<AiModelInfo> _modelCatalog = [];
    List<string> _efforts = [];

    public void InitAssistant(AssistantService s)
    {
        Assistant = s;
        s.Said += (role, text, images) => Dispatcher.UIThread.Post(() =>
        {
            ChatMessages.Add(new ChatMessage { Role = role, Text = text, Images = images });
            OnPropertyChanged(nameof(AssistantEmpty));
        });
        _assistantLoading = true;
        AssistantProviderIndex = (int)s.Settings.Provider - 1; LoadProviderFields();
        AssistantAllowFiles = s.Settings.AllowFiles; AssistantRemember = s.Settings.RememberKeys;
        _assistantLoading = false;
        _ = RefreshAssistantModels();
    }

    void LoadProviderFields()
    {
        var st = Assistant!.Settings;
        AssistantModel = AssistantService.ModelIdOf(AssistantProvider switch { AiProvider.Anthropic => st.AnthropicModel, AiProvider.OpenAi => st.OpenAiModel, _ => st.CliModel });
        AssistantKey = AssistantProvider switch { AiProvider.Anthropic => st.AnthropicKey, AiProvider.OpenAi => st.OpenAiKey, _ => "" };
        var effort = AssistantProvider switch { AiProvider.Anthropic => st.AnthropicEffort, AiProvider.OpenAi => st.OpenAiEffort, _ => st.CliEffort };
        OnPropertyChanged(nameof(AssistantNeedsKey)); OnPropertyChanged(nameof(AssistantProviderHint));
        _modelCatalog = []; RefreshEffortChoices(effort);
    }

    void StoreSettings()
    {
        if (Assistant == null || _assistantLoading) return;
        var st = Assistant.Settings;
        st.Provider = AssistantProvider;
        var model = AssistantService.ModelIdOf(AssistantModel);   // the picker rows can be "id   (label)" — only the id is stored and sent
        switch (st.Provider) { case AiProvider.Anthropic: st.AnthropicModel = model; st.AnthropicKey = AssistantKey; st.AnthropicEffort = CurrentEffort; break; case AiProvider.OpenAi: st.OpenAiModel = model; st.OpenAiKey = AssistantKey; st.OpenAiEffort = CurrentEffort; break; default: st.CliModel = model; st.CliEffort = CurrentEffort; break; }
        st.AllowFiles = AssistantAllowFiles; st.RememberKeys = AssistantRemember;
        Assistant.Save();
    }

    partial void OnAssistantProviderIndexChanged(int value)
    {
        if (_assistantLoading || Assistant == null) return;
        _assistantLoading = true; Assistant.Settings.Provider = AssistantProvider; LoadProviderFields(); _assistantLoading = false;
        Assistant.Reset(); Assistant.Save();
        _ = RefreshAssistantModels();
    }
    partial void OnAssistantModelChanged(string value) { StoreSettings(); RefreshEffortChoices(CurrentEffort); }
    partial void OnAssistantKeyChanged(string value) { StoreSettings(); _ = RefreshAssistantModels(); }
    partial void OnAssistantAllowFilesChanged(bool value) => StoreSettings();
    partial void OnAssistantRememberChanged(bool value) => StoreSettings();
    partial void OnAssistantEffortIndexChanged(int value) => StoreSettings();

    // ================================================================== model + effort pickers

    /// <summary>The effort token stored for the current provider ("" = the provider/model default).</summary>
    public string CurrentEffort => AssistantEffortIndex > 0 && AssistantEffortIndex - 1 < _efforts.Count ? _efforts[AssistantEffortIndex - 1] : "";

    /// <summary>Pick an effort by token ("low", "medium", ...); false when the current model does not offer it.</summary>
    public bool SetAssistantEffort(string token)
    {
        if (string.IsNullOrEmpty(token)) { AssistantEffortIndex = 0; return true; }
        var i = _efforts.IndexOf(token);
        if (i < 0) return false;
        AssistantEffortIndex = i + 1;
        return true;
    }

    /// <summary>Reloads the model list for the current provider (on open, provider/key change, or the ↻ button).</summary>
    public async Task RefreshAssistantModels(bool force = false)
    {
        if (Assistant == null) return;
        var provider = AssistantProvider;
        AssistantModelsBusy = true;
        try
        {
            var models = await Assistant.ModelsAsync(provider);
            if (provider != AssistantProvider) return;   // provider changed while loading
            _modelCatalog = models.ToList();
            AssistantModels.Clear();
            if (provider == AiProvider.CodexCli) AssistantModels.Add("");   // "" = the CLI's own default
            foreach (var m in models) AssistantModels.Add(m.Label is { Length: > 0 } l ? $"{m.Id}   ({l})" : m.Id);
            var id = AssistantService.ModelIdOf(AssistantModel);
            if (!string.IsNullOrEmpty(id) && !_modelCatalog.Any(m => m.Id == id)) AssistantModels.Add(id);   // keep a saved custom id
            // reselect the row matching the saved id so the "(label)" display text never becomes the stored id
            var row = AssistantModels.FirstOrDefault(r => AssistantService.ModelIdOf(r) == id);
            if (row != null && row != AssistantModel) { _assistantLoading = true; AssistantModel = row; _assistantLoading = false; }
            AssistantModelsStatus = models.Count > 0 ? $"{models.Count} models" : "Model list unavailable — the saved model is used.";
        }
        catch (Exception e) { AssistantModelsStatus = "Model list: " + e.Message; }
        finally { AssistantModelsBusy = false; RefreshEffortChoices(CurrentEffort); }
    }

    void RefreshEffortChoices(string keep)
    {
        var provider = AssistantProvider;
        _efforts = _modelCatalog.FirstOrDefault(m => m.Id == AssistantService.ModelIdOf(AssistantModel))?.Efforts?.ToList()
            ?? (provider == AiProvider.CodexCli ? ["low", "medium", "high"] : provider == AiProvider.Anthropic ? ["low", "medium", "high"] : AssistantService.IsReasoningModel(AssistantModel) ? ["minimal", "low", "medium", "high"] : []);
        var idx = _efforts.IndexOf(keep);
        _assistantLoading = true;
        AssistantEffortChoices.Clear(); AssistantEffortChoices.Add("Default");
        foreach (var e in _efforts) AssistantEffortChoices.Add(e);
        AssistantEffortIndex = idx >= 0 ? idx + 1 : 0;
        _assistantLoading = false;
        OnPropertyChanged(nameof(AssistantHasEfforts));
    }

    // ================================================================== attachments

    /// <summary>Stage an image for the next message; returns an error string or null.</summary>
    public string? AddAssistantAttachment(string name, byte[] data)
    {
        if (AssistantAttachments.Count >= AssistantAttachmentLimit) return $"Up to {AssistantAttachmentLimit} images per message.";
        if (data.Length > 10 * 1024 * 1024) return "Images are limited to 10 MB each.";
        var mime = Path.GetExtension(name).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif", ".bmp" => "image/bmp", _ => "image/png" };
        try { var probe = new AssistantImage(name, mime, data); _ = probe.Preview; }
        catch { return "That file is not a readable image (PNG, JPEG, WebP, GIF or BMP)."; }
        AssistantAttachments.Add(new AssistantImage(name, mime, data));
        return null;
    }

    public void RemoveAssistantAttachment(AssistantImage img) => AssistantAttachments.Remove(img);

    /// <summary>Adds a note row that never leaves the app (not sent to the provider).</summary>
    public void AssistantNote(string text) { ChatMessages.Add(new ChatMessage { Role = ChatRole.Note, Text = text }); OnPropertyChanged(nameof(AssistantEmpty)); }

    // ================================================================== entry points from other panels

    /// <summary>Script panel ✦ button: opens the assistant, sets the script context and sends a greeting so it asks what to make.</summary>
    public void AskAssistantForScript()
    {
        AssistantOpen = true;
        if (Assistant != null) Assistant.Context = "The user opened you from the Script Editor: they want a cadkit Python script written into the open editor with the script_editor tool (action=set replaces the whole script; the user can roll it back).";
        AssistantInput = "I'd like your help writing a cadkit Python script in the open editor — I'll describe what it should make next.";
        if (Assistant != null && !AssistantBusy) _ = SendAssistant();
        else AssistantNote("Describe the part and send — the assistant writes the whole script into the editor (script_editor). Use the editor's ⟲ button to roll back.");
    }

    /// <summary>Render workspace: point the assistant at generating a PNG background.</summary>
    public void GenerateBackgroundWithAi()
    {
        AssistantOpen = true;
        if (Assistant?.Settings.AllowFiles != true)
        {
            AssistantSettingsOpen = true;
            AssistantNote("Generating a background needs file access — turn on \"Let the assistant open, save and export files\" first.");
            return;
        }
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "generated");
        Assistant.Context = "The user opened you from the Render workspace: they want a renderer background image generated.";
        AssistantInput = "Generate a render background image. (1) Use run_script to write a PNG — a smooth studio backdrop, e.g. a dark gradient with a soft vignette, about 1600x900 — to '" + Path.Combine(dir, "background.png") + "' (create the folder first; pure standard-library code, zlib+struct is enough). (2) Call render_setup with action=set_background and that path. (3) Check render_setup action=state and tell me what you did.";
        AssistantNote("Prompt staged below — check it and send to have Codex build a background PNG and apply it.");
    }

    public void ToggleAssistant() => AssistantOpen = !AssistantOpen;

    public async Task SendAssistant()
    {
        var text = AssistantInput.Trim();
        if (Assistant == null || AssistantBusy || (text.Length == 0 && AssistantAttachments.Count == 0)) return;
        AssistantInput = "";
        var images = AssistantAttachments.ToList();
        AssistantAttachments.Clear();
        AssistantBusy = true;
        try { await Assistant.SendAsync(text, images); }
        finally { AssistantBusy = false; OnPropertyChanged(nameof(AssistantNeedsLogin)); OnPropertyChanged(nameof(AssistantNeedsKeyFix)); }
    }

    public void StopAssistant() => Assistant?.Cancel();
    public void NewAssistantChat() { Assistant?.Cancel(); Assistant?.Reset(); ChatMessages.Clear(); OnPropertyChanged(nameof(AssistantEmpty)); OnPropertyChanged(nameof(AssistantNeedsLogin)); OnPropertyChanged(nameof(AssistantNeedsKeyFix)); }
}
