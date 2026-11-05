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
    // The provider dropdowns list Codex (login), Claude (API key) and OpenAI (API key) in enum order.
    AiProvider AssistantProvider => (AiProvider)(AssistantProviderIndex + 1);
    public bool AssistantNeedsKey => AssistantProvider != AiProvider.CodexCli;
    public bool AssistantEmpty => ChatMessages.Count == 0;
    public string AssistantProviderHint => AssistantProvider switch
    {
        AiProvider.CodexCli => "Uses your Codex login (run codex login once). Experimental.",
        AiProvider.Anthropic => "Uses your own Anthropic API key. Billed to your API account.",
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
        AssistantProviderIndex = (int)s.Settings.Provider - 1; LoadProviderFields();
        AssistantAllowFiles = s.Settings.AllowFiles; AssistantRemember = s.Settings.RememberKeys;
        _assistantLoading = false;
    }

    void LoadProviderFields()
    {
        var st = Assistant!.Settings;
        AssistantModel = AssistantProvider switch { AiProvider.Anthropic => st.AnthropicModel, AiProvider.OpenAi => st.OpenAiModel, _ => st.CliModel };
        AssistantKey = AssistantProvider switch { AiProvider.Anthropic => st.AnthropicKey, AiProvider.OpenAi => st.OpenAiKey, _ => "" };
        OnPropertyChanged(nameof(AssistantNeedsKey)); OnPropertyChanged(nameof(AssistantProviderHint));
    }

    void StoreSettings()
    {
        if (Assistant == null || _assistantLoading) return;
        var st = Assistant.Settings;
        st.Provider = AssistantProvider;
        switch (st.Provider) { case AiProvider.Anthropic: st.AnthropicModel = AssistantModel; st.AnthropicKey = AssistantKey; break; case AiProvider.OpenAi: st.OpenAiModel = AssistantModel; st.OpenAiKey = AssistantKey; break; default: st.CliModel = AssistantModel; break; }
        st.AllowFiles = AssistantAllowFiles; st.RememberKeys = AssistantRemember;
        Assistant.Save();
    }

    partial void OnAssistantProviderIndexChanged(int value)
    {
        if (_assistantLoading || Assistant == null) return;
        _assistantLoading = true; Assistant.Settings.Provider = AssistantProvider; LoadProviderFields(); _assistantLoading = false;
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
