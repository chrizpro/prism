using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Automation;

namespace Prism.ViewModels;

public sealed partial class ChatMessage : ObservableObject
{
    public ChatRole Role { get; init; }
    [ObservableProperty] public partial string Text { get; set; } = "";
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
    public AssistantService? Assistant { get; private set; }

    [ObservableProperty] public partial bool AssistantOpen { get; set; }
    [ObservableProperty] public partial bool AssistantSettingsOpen { get; set; }
    [ObservableProperty] public partial bool AssistantBusy { get; set; }
    [ObservableProperty] public partial string AssistantInput { get; set; } = "";
    [ObservableProperty] public partial int AssistantProviderIndex { get; set; }
    [ObservableProperty] public partial string AssistantModel { get; set; } = "";
    [ObservableProperty] public partial string AssistantKey { get; set; } = "";
    [ObservableProperty] public partial bool AssistantAllowFiles { get; set; }
    [ObservableProperty] public partial bool AssistantRemember { get; set; }
    public bool AssistantNeedsKey => AssistantProviderIndex >= 2;
    public bool AssistantEmpty => ChatMessages.Count == 0;
    public string AssistantProviderHint => AssistantProviderIndex switch
    {
        0 => "Uses your Claude Code login (run claude once in a terminal to sign in). No key needed.",
        1 => "Uses your Codex login (run codex login once). Experimental.",
        2 => "Uses your own Anthropic API key. Billed to your API account.",
        _ => "Uses your own OpenAI API key. Billed to your API account.",
    };

    bool _assistantLoading;

    public void InitAssistant(AssistantService s)
    {
        Assistant = s;
        s.Said += (role, text) => Dispatcher.UIThread.Post(() =>
        {
            ChatMessages.Add(new ChatMessage { Role = role, Text = text });
            OnPropertyChanged(nameof(AssistantEmpty));
        });
        _assistantLoading = true;
        AssistantProviderIndex = (int)s.Settings.Provider; LoadProviderFields();
        AssistantAllowFiles = s.Settings.AllowFiles; AssistantRemember = s.Settings.RememberKeys;
        _assistantLoading = false;
    }

    void LoadProviderFields()
    {
        var st = Assistant!.Settings;
        AssistantModel = AssistantProviderIndex switch { 2 => st.AnthropicModel, 3 => st.OpenAiModel, _ => st.CliModel };
        AssistantKey = AssistantProviderIndex switch { 2 => st.AnthropicKey, 3 => st.OpenAiKey, _ => "" };
        OnPropertyChanged(nameof(AssistantNeedsKey)); OnPropertyChanged(nameof(AssistantProviderHint));
    }

    void StoreSettings()
    {
        if (Assistant == null || _assistantLoading) return;
        var st = Assistant.Settings;
        st.Provider = (AiProvider)AssistantProviderIndex;
        switch (AssistantProviderIndex) { case 2: st.AnthropicModel = AssistantModel; st.AnthropicKey = AssistantKey; break; case 3: st.OpenAiModel = AssistantModel; st.OpenAiKey = AssistantKey; break; default: st.CliModel = AssistantModel; break; }
        st.AllowFiles = AssistantAllowFiles; st.RememberKeys = AssistantRemember;
        Assistant.Save();
    }

    partial void OnAssistantProviderIndexChanged(int value)
    {
        if (_assistantLoading || Assistant == null) return;
        _assistantLoading = true; Assistant.Settings.Provider = (AiProvider)value; LoadProviderFields(); _assistantLoading = false;
        Assistant.Reset(); Assistant.Save();
    }
    partial void OnAssistantModelChanged(string value) => StoreSettings();
    partial void OnAssistantKeyChanged(string value) => StoreSettings();
    partial void OnAssistantAllowFilesChanged(bool value) => StoreSettings();
    partial void OnAssistantRememberChanged(bool value) => StoreSettings();

    public void ToggleAssistant() => AssistantOpen = !AssistantOpen;

    public async Task SendAssistant()
    {
        var text = AssistantInput.Trim();
        if (Assistant == null || AssistantBusy || text.Length == 0) return;
        AssistantInput = "";
        AssistantBusy = true;
        try { await Assistant.SendAsync(text); }
        finally { AssistantBusy = false; }
    }

    public void StopAssistant() => Assistant?.Cancel();
    public void NewAssistantChat() { Assistant?.Cancel(); Assistant?.Reset(); ChatMessages.Clear(); OnPropertyChanged(nameof(AssistantEmpty)); }
}
