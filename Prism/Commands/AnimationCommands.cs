using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// The animation editor: scrub a timeline, set joint values (and the exploded view) at a time, key them, and play the result.
/// Between keys the values ease in and out (or move linearly).
/// </summary>
public sealed class AnimationCommand : Command
{
    public const string ExplodeKey = "@explode";

    SceneObject? _animObj;
    AnimInfo _anim = new(5, [], false);
    readonly List<(JointInfo joint, NumberParam v1, NumberParam? v2)> _rows = [];
    NumberParam _time = null!, _duration = null!, _explode = null!;
    BoolParam _loop = null!, _ease = null!;
    ActionParam _play = null!;
    DispatcherTimer? _timer;
    DateTime _last;
    bool _syncing, _playing;
    Dictionary<string, (double v1, double v2)> _rest = [];
    bool _createNew;

    public override string Title => "Animation";
    public override string Icon => "animation";
    public override string OkText => "Save animation";
    public override string Hint => "Scrub to a time, change the joint values or the exploded view, and press 'Key the changed values'. Play to see it move. Save animation stores the keys with the document. Render Animation (Render tab) turns it into pictures or a movie.";

    protected override void OnBegin()
    {
        _animObj = S.SelectedObjects.FirstOrDefault(o => o.Asm is AnimInfo) ?? S.Objects.FirstOrDefault(o => o.Asm is AnimInfo);
        _createNew = _animObj == null;
        if (_animObj?.Asm is AnimInfo a) _anim = a with { Tracks = a.Tracks.Select(t => t with { Keys = [.. t.Keys] }).ToList() };
        _rest = S.JointObjects.ToDictionary(j => j.joint.Key, j => (j.joint.Value, j.joint.Value2));
        _rest[ExplodeKey] = (S.ExplodeFactor, 0);

        _duration = Add(new NumberParam { Label = "Length", Value = _anim.Duration, Min = 0.5, Max = 600, Step = 0.5, Unit = "s" });
        _time = Add(new NumberParam { Label = "Time", Value = 0, Min = 0, Max = 600, Step = 0.1, Unit = "s" });
        _loop = Add(new BoolParam { Label = "Loop", Value = _anim.Loop });
        _ease = Add(new BoolParam { Label = "Ease in and out", Value = _anim.Ease });
        _duration.Changed = () => { _time.Value = Math.Min(_time.Value, _duration.Value); OnChanged(); TimelineChanged?.Invoke(); };
        _time.Changed = () => { if (!_syncing) { if (_time.Value > _duration.Value) { _time.Value = _duration.Value; return; } PullPose(); } OnChanged(); TimelineChanged?.Invoke(); };

        foreach (var (obj, j) in S.JointObjects)
        {
            if (j.Type == JointType.Rigid) continue;
            double lo = j.HasLimits ? j.Min : (j.UsesAngle ? -360 : -500), hi = j.HasLimits ? j.Max : (j.UsesAngle ? 360 : 500);
            var p1 = Add(new NumberParam { Label = $"{j.Name}  ({(j.UsesAngle ? "angle" : "travel")})", Value = Math.Clamp(j.Value, lo, hi), Min = lo, Max = hi, Step = j.UsesAngle ? 5 : 1, Unit = j.UsesAngle ? "°" : "mm" });
            NumberParam? p2 = null;
            if (j.Type == JointType.Cylindrical)
            {
                double lo2 = j.HasLimits ? j.Min2 : -500, hi2 = j.HasLimits ? j.Max2 : 500;
                p2 = Add(new NumberParam { Label = $"{j.Name}  (travel)", Value = Math.Clamp(j.Value2, lo2, hi2), Min = lo2, Max = hi2, Step = 1, Unit = "mm" });
            }
            _rows.Add((j, p1, p2));
        }
        _explode = Add(new NumberParam { Label = "Exploded view", Value = S.ExplodeFactor, Min = 0, Max = 1, Step = 0.05, Unit = "" });
        Add(new ActionParam { Label = "Key the changed values at this time", Run = KeyChanged });
        Add(new ActionParam { Label = "Remove keys at this time", Run = RemoveKeys });
        _play = Add(new ActionParam { Label = "Play  /  Stop", Run = TogglePlay });
        PullPose();
    }

    // ------------------------------------------------------------ keys
    Dictionary<string, (double v1, double v2)> Pose(double t) => (_anim with { Ease = _ease.Value }).Sample(t, _rest);

    /// <summary>Fill the value boxes with the animated values at the current time.</summary>
    void PullPose()
    {
        _syncing = true;
        var pose = Pose(_time.Value);
        foreach (var r in _rows)
        {
            var v = pose.TryGetValue(r.joint.Key, out var p) ? p : _rest.GetValueOrDefault(r.joint.Key);
            r.v1.Value = Math.Clamp(v.v1, r.v1.Min, r.v1.Max);
            if (r.v2 != null) r.v2.Value = Math.Clamp(v.v2, r.v2.Min, r.v2.Max);
        }
        _explode.Value = Math.Clamp((pose.TryGetValue(ExplodeKey, out var e) ? e : _rest[ExplodeKey]).v1, 0, 1);
        _syncing = false;
    }

    void KeyChanged()
    {
        double t = Math.Round(_time.Value, 3);
        var pose = Pose(t);
        int keyed = 0;
        void Key(string joint, bool second, double value, double before)
        {
            if (Math.Abs(value - before) < 1e-6) return;
            var tr = _anim.Tracks.FirstOrDefault(x => x.JointKey == joint && x.SecondValue == second);
            if (tr == null)
            {
                tr = new AnimTrack(joint, second, []);
                // a new track starts from the resting value so the motion begins where it was
                tr.Keys.Add(new AnimKey(0, second ? _rest[joint].v2 : _rest[joint].v1));
                _anim.Tracks.Add(tr);
            }
            tr.Keys.RemoveAll(k => Math.Abs(k.T - t) < 1e-6);
            tr.Keys.Add(new AnimKey(t, value)); keyed++;
        }
        foreach (var r in _rows)
        {
            var now = pose.TryGetValue(r.joint.Key, out var p) ? p : _rest.GetValueOrDefault(r.joint.Key);
            Key(r.joint.Key, false, r.v1.Value, now.v1);
            if (r.v2 != null) Key(r.joint.Key, true, r.v2.Value, now.v2);
        }
        var ex = pose.TryGetValue(ExplodeKey, out var ev) ? ev : _rest[ExplodeKey];
        Key(ExplodeKey, false, _explode.Value, ex.v1);
        S.Say(keyed == 0 ? "Nothing changed at this time, so nothing was keyed." : $"Keyed {keyed} value(s) at {t:0.##} s.");
        OnChanged(); TimelineChanged?.Invoke();
    }

    void RemoveKeys()
    {
        double t = Math.Round(_time.Value, 3); int n = 0;
        foreach (var tr in _anim.Tracks) n += tr.Keys.RemoveAll(k => Math.Abs(k.T - t) < 1e-3 && k.T > 1e-6);
        _anim.Tracks.RemoveAll(tr => tr.Keys.Count < 2);
        S.Say(n == 0 ? "There are no keys at this time (the keys at 0 s are the starting position)." : $"Removed {n} key(s).");
        PullPose(); OnChanged(); TimelineChanged?.Invoke();
    }

    // ------------------------------------------------------------ timeline view
    /// <summary>One row of the timeline: a joint value (or the exploded view) and its keys, if it has any.</summary>
    public sealed record TimelineRow(string Label, string JointKey, bool Second, AnimTrack? Track);

    /// <summary>Raised whenever the time, length, keys or playback change, so the timeline can redraw.</summary>
    public event Action? TimelineChanged;

    public double Duration => _duration.Value;
    public double Time { get => _time.Value; set => _time.Value = Math.Clamp(value, 0, _duration.Value); }
    public bool IsPlaying => _playing;
    public void TogglePlayback() { TogglePlay(); TimelineChanged?.Invoke(); }
    public void KeyChangedValues() => KeyChanged();
    public void RemoveKeysAtTime() => RemoveKeys();

    public IReadOnlyList<TimelineRow> Rows()
    {
        var rows = new List<TimelineRow>();
        foreach (var r in _rows)
        {
            string name = r.joint.Name;
            rows.Add(new(r.v2 == null ? name : $"{name} (angle)", r.joint.Key, false, _anim.Tracks.FirstOrDefault(t => t.JointKey == r.joint.Key && !t.SecondValue)));
            if (r.v2 != null) rows.Add(new($"{name} (travel)", r.joint.Key, true, _anim.Tracks.FirstOrDefault(t => t.JointKey == r.joint.Key && t.SecondValue)));
        }
        rows.Add(new("Exploded view", ExplodeKey, false, _anim.Tracks.FirstOrDefault(t => t.JointKey == ExplodeKey && !t.SecondValue)));
        return rows;
    }

    /// <summary>Put a key on a row at time t, holding whatever value the animation has there now (or the current box value if the row has no keys yet).</summary>
    public void AddKey(TimelineRow row, double t)
    {
        t = Math.Round(Math.Clamp(t, 0, _duration.Value), 2);
        var pose = Pose(t);
        double Current()
        {
            if (row.JointKey == ExplodeKey) return _anim.Tracks.Any(x => x.JointKey == ExplodeKey) ? (pose.TryGetValue(ExplodeKey, out var e) ? e.v1 : _explode.Value) : _explode.Value;
            var box = _rows.First(r => r.joint.Key == row.JointKey);
            bool hasTrack = _anim.Tracks.Any(x => x.JointKey == row.JointKey && x.SecondValue == row.Second);
            if (hasTrack && pose.TryGetValue(row.JointKey, out var p)) return row.Second ? p.v2 : p.v1;
            return row.Second ? box.v2!.Value : box.v1.Value;
        }
        double value = Current();
        var tr = _anim.Tracks.FirstOrDefault(x => x.JointKey == row.JointKey && x.SecondValue == row.Second);
        if (tr == null)
        {
            tr = new AnimTrack(row.JointKey, row.Second, []);
            var rest = _rest.GetValueOrDefault(row.JointKey);
            tr.Keys.Add(new AnimKey(0, row.Second ? rest.v2 : rest.v1));
            _anim.Tracks.Add(tr);
        }
        tr.Keys.RemoveAll(k => Math.Abs(k.T - t) < 1e-6 && t > 1e-6);
        if (t > 1e-6) tr.Keys.Add(new AnimKey(t, value));
        tr.Keys.Sort((a, b) => a.T.CompareTo(b.T));
        PullPose(); OnChanged(); TimelineChanged?.Invoke();
    }

    /// <summary>Slide a key to another time. The starting key at 0 s stays put.</summary>
    public void MoveKey(AnimTrack track, AnimKey key, double newTime)
    {
        int i = track.Keys.IndexOf(key); if (i < 0 || key.T < 1e-6) return;
        newTime = Math.Round(Math.Clamp(newTime, 0.01, _duration.Value), 2);
        if (Math.Abs(newTime - key.T) < 1e-9) return;
        track.Keys.RemoveAll(k => !ReferenceEquals(k, key) && Math.Abs(k.T - newTime) < 1e-6);
        i = track.Keys.IndexOf(key);
        track.Keys[i] = key with { T = newTime };
        track.Keys.Sort((a, b) => a.T.CompareTo(b.T));
        PullPose(); OnChanged(); TimelineChanged?.Invoke();
    }

    public void DeleteKey(AnimTrack track, AnimKey key)
    {
        if (key.T < 1e-6) { S.Say("The key at 0 s is the starting position and cannot be removed.", true); return; }
        track.Keys.Remove(key);
        if (track.Keys.Count < 2) _anim.Tracks.Remove(track);
        PullPose(); OnChanged(); TimelineChanged?.Invoke();
    }

    // ------------------------------------------------------------ playback
    void TogglePlay()
    {
        if (_playing) { StopPlay(); return; }
        if (_anim.Tracks.Count == 0) { S.Say("Key some values first, then press play.", true); return; }
        _playing = true; _last = DateTime.UtcNow;
        if (_time.Value >= _duration.Value - 1e-6) _time.Value = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow; double dt = (now - _last).TotalSeconds; _last = now;
            double t = _time.Value + dt;
            if (t >= _duration.Value) { if (_loop.Value) t %= Math.Max(_duration.Value, 0.1); else { _time.Value = _duration.Value; StopPlay(); TimelineChanged?.Invoke(); return; } }
            _time.Value = t;
        };
        _timer.Start();
    }

    void StopPlay() { _timer?.Stop(); _timer = null; _playing = false; }

    public override void OnEnd() => StopPlay();

    // ------------------------------------------------------------ result
    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_rows.Count == 0 && !S.Components.Any()) { Results.Add(new ResultLine("Joints", "none yet. Create revolute, slider or cylindrical joints first (Joints tab).")); return null; }
        var over = _rows.ToDictionary(r => r.joint.Key, r => r.joint with { Value = r.v1.Value, Value2 = r.v2?.Value ?? r.joint.Value2 });
        var shapes = S.PoseShapes(over, _explode.Value, out var notes);
        var o = new CommandOutput { Label = "Animation", Icon = "animation", ReplaceIsMove = true };
        foreach (var (id, shape) in shapes) if (S.Find(id) is { } comp) o.Replace.Add((comp, shape));
        var values = _rows.ToDictionary(r => r.joint.Key, r => (r.v1.Value, r.v2?.Value ?? r.joint.Value2));
        double explode = _explode.Value;
        var final = _anim with { Duration = _duration.Value, Loop = _loop.Value, Ease = _ease.Value };
        bool create = _createNew; var existing = _animObj;
        o.After = s =>
        {
            if (create) s.AddAnimation(final);
            else s.Update(existing!.Id, x => x.WithAsm(final), false);
            s.SetJointValues(values);
            s.SetExplode(explode);
        };
        var keys = _anim.Tracks.Sum(t => t.Keys.Count);
        Results.Add(new ResultLine("Keys", $"{keys} on {_anim.Tracks.Count} value(s)"));
        Results.Add(new ResultLine("Length", $"{_duration.Value:0.##} s"));
        foreach (var n in notes.Distinct()) Results.Add(new ResultLine("Note", n, 1));
        return o;
    }
}
