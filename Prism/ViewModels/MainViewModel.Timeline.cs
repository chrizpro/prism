using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;

namespace Prism.ViewModels;

/// <summary>The animation timeline dock: shown while the Animation command is open, beside its panel of joint values.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowAnimationTimeline))] public partial AnimationCommand? AnimationEditor { get; set; }
    [ObservableProperty] public partial string TimelineTimeText { get; set; } = "";
    [ObservableProperty] public partial string TimelinePlayText { get; set; } = "▶  Play";
    public bool ShowAnimationTimeline => AnimationEditor != null;
    System.Action? _timelineHook;

    partial void OnActiveCommandChanged(Command? value)
    {
        if (AnimationEditor != null && _timelineHook != null) AnimationEditor.TimelineChanged -= _timelineHook;
        _timelineHook = null;
        AnimationEditor = value as AnimationCommand;
        if (AnimationEditor is { } editor)
        {
            _timelineHook = () =>
            {
                TimelineTimeText = $"{editor.Time.ToString("0.00", CultureInfo.InvariantCulture)} / {editor.Duration.ToString("0.##", CultureInfo.InvariantCulture)} s";
                TimelinePlayText = editor.IsPlaying ? "■  Stop" : "▶  Play";
            };
            editor.TimelineChanged += _timelineHook;
            _timelineHook();
        }
    }

    public void TimelineTogglePlay() => AnimationEditor?.TogglePlayback();
    public void TimelineToStart() { if (AnimationEditor is { } a) a.Time = 0; }
    public void TimelineKeyChanged() => AnimationEditor?.KeyChangedValues();
    public void TimelineRemoveKeys() => AnimationEditor?.RemoveKeysAtTime();
}
