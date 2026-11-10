using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;
using Prism.Commands;
using Prism.Core.Assembly;

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
                TimelineTimeText = $"{editor.Time.ToString("0.000", CultureInfo.InvariantCulture)} / {editor.Duration.ToString("0.###", CultureInfo.InvariantCulture)} s";
                TimelinePlayText = editor.IsPlaying ? "Ⅱ  Pause" : "▶  Play";
            };
            editor.TimelineChanged += _timelineHook;
            _timelineHook();
        }
    }

    [ObservableProperty] public partial string AnimationSummary { get; set; } = "";

    /// <summary>Open the timeline from anywhere (the Properties panel offers this when no animation command is running).</summary>
    public void OpenAnimationTimeline()
    {
        if (AnimationEditor == null && !IsSketching && ActiveCommand == null) Session.StartCommand(() => new AnimationCommand());
    }

    void UpdateAnimationSummary()
    {
        var anims = Session.Objects.Where(o => o.Asm is AnimInfo).ToList();
        AnimationSummary = anims.Count == 0 ? "No animations yet. Open the timeline to key joint values or the exploded view."
            : anims.Count == 1 ? $"{anims[0].Name}: {((AnimInfo)anims[0].Asm!).Duration:0.##} s, {((AnimInfo)anims[0].Asm!).Tracks.Count} animated value(s)."
            : $"{anims.Count} animations in this document.";
    }

    public void TimelineTogglePlay() => AnimationEditor?.TogglePlayback();
    public void TimelineToStart() { if (AnimationEditor is { } a) a.Time = 0; }
    public void TimelineKeyChanged() => AnimationEditor?.KeyChangedValues();
    public void TimelineRemoveKeys() => AnimationEditor?.RemoveKeysAtTime();
}
