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
    readonly List<(string key, string label, NumberParam p)> _partRows = [];
    readonly List<(string key, string name, string unit, NumberParam p)> _paramRows = [];
    ChoiceParam? _rampParam; NumberParam? _rampFrom, _rampTo, _rampStart, _rampLength;
    NumberParam _time = null!, _duration = null!, _explode = null!;
    BoolParam _loop = null!, _ease = null!;
    ActionParam _play = null!;
    DispatcherTimer? _timer;
    DateTime _last;
    bool _syncing, _playing;
    Dictionary<string, (double v1, double v2)> _rest = [];
    bool _createNew;
    readonly bool _startNew;
    NumberParam _keyTime = null!, _keyValue = null!;
    ActionParam _applyKey = null!;
    public AnimTrack? SelectedTrack { get; private set; }
    public AnimKey? SelectedKey { get; private set; }

    public AnimationCommand() { }
    public AnimationCommand(bool startNew) => _startNew = startNew;

    /// <summary>The joints can be dragged in the viewport to set the pose at this time.</summary>
    public override IReadOnlyList<HandleSpec> Handles => JointHandles.Build(S, _rows);

    public override string Title => _startNew ? "New Animation" : "Animation";
    public override string Icon => "animation";
    public override string OkText => "Save animation";
    public override string Hint => "Set a pose and press Add Key. Select a key to edit its time and value in the right panel. Save animation stores the clip with the document.";

    protected override void OnBegin()
    {
        if (!_startNew)
            _animObj = S.SelectedObjects.FirstOrDefault(o => o.Asm is AnimInfo) ?? S.Objects.FirstOrDefault(o => o.Asm is AnimInfo);
        _createNew = _animObj == null;
        if (_animObj?.Asm is AnimInfo a) _anim = a with { Tracks = a.Tracks.Select(t => t with { Keys = [.. t.Keys] }).ToList() };
        _rest = S.JointObjects.ToDictionary(j => j.joint.Key, j => (j.joint.Value, j.joint.Value2));
        _rest[ExplodeKey] = (S.ExplodeFactor, 0);

        _keyTime = Add(new NumberParam { Section = "Selected key", Label = "Key time", Min = 0, Max = 600, Step = .001, Unit = "s", IsVisible = false });
        _keyValue = Add(new NumberParam { Section = "Selected key", Label = "Key value", Step = .001, Unit = "", IsVisible = false });
        _keyTime.Changed = _keyValue.Changed = null;
        _applyKey = Add(new ActionParam { Section = "Selected key", Label = "Apply key", IsVisible = false, Run = ApplyKey });
        _duration = Add(new NumberParam { Section = "Clip", Label = "Length", Value = _anim.Duration, Min = 0.5, Max = 600, Step = 0.001, Unit = "s" });
        _time = Add(new NumberParam { Section = "Clip", Label = "Time", Value = 0, Min = 0, Max = 600, Step = 0.001, Unit = "s" });
        _loop = Add(new BoolParam { Section = "Clip", Label = "Loop", Value = _anim.Loop });
        _ease = Add(new BoolParam { Section = "Clip", Label = "Ease in and out", Value = _anim.Ease });
        _duration.Changed = () => { _time.Value = Math.Min(_time.Value, _duration.Value); OnChanged(); TimelineChanged?.Invoke(); };
        _time.Changed = () => { if (!_syncing) { if (_time.Value > _duration.Value) { _time.Value = _duration.Value; return; } PullPose(); } OnChanged(); TimelineChanged?.Invoke(); };

        foreach (var (obj, j) in S.JointObjects)
        {
            if (j.Type == JointType.Rigid) continue;
            double lo = j.HasLimits ? j.Min : (j.UsesAngle ? -360 : -500), hi = j.HasLimits ? j.Max : (j.UsesAngle ? 360 : 500);
            var p1 = Add(new NumberParam { Section = "Joint values", Label = $"{j.Name}  ({j.FirstLabel})", Value = Math.Clamp(j.Value, lo, hi), Min = lo, Max = hi, Step = j.UsesAngle ? 5 : 1, Unit = j.UsesAngle ? "°" : "mm" });
            NumberParam? p2 = null;
            if (j.HasSecond)
            {
                double lo2 = j.HasLimits ? j.Min2 : (j.SecondIsAngle ? -180 : -500), hi2 = j.HasLimits ? j.Max2 : (j.SecondIsAngle ? 180 : 500);
                p2 = Add(new NumberParam { Section = "Joint values", Label = $"{j.Name}  ({j.SecondLabel})", Value = Math.Clamp(j.Value2, lo2, hi2), Min = lo2, Max = hi2, Step = j.SecondIsAngle ? 5 : 1, Unit = j.SecondIsAngle ? "°" : "mm" });
            }
            _rows.Add((j, p1, p2));
        }
        // free parts (no joint, not grounded) can be moved and turned directly
        foreach (var comp in S.FreeComponents)
        {
            string ck = comp.Component!.Key;
            foreach (var (channel, what, unit, step, lim) in new[] { ("x", "move X", "mm", 5.0, 2000.0), ("y", "move Y", "mm", 5.0, 2000.0), ("z", "move Z", "mm", 5.0, 2000.0), ("rz", "turn", "°", 5.0, 360.0) })
            {
                string key = CadSession.PartKey(ck, channel); _rest[key] = (0, 0);
                _partRows.Add((key, $"{comp.Name}  ({what})", Add(new NumberParam { Section = "Component offsets", Label = $"{comp.Name}  ({what})", Value = 0, Min = -lim, Max = lim, Step = step, Unit = unit })));
            }
        }
        // document parameters that are plain numbers can be animated too: the model is rebuilt with the value of each moment
        var animatable = S.AnimatableParameters();
        if (animatable.Count > 0)
            _rampParam = Add(new ChoiceParam { Section = "Document parameter", Label = "Parameter to animate", Options = [.. animatable.Select(a => a.Name)] });
        foreach (var (name, value, unit) in animatable)
        {
            string key = CadSession.ParamKey(name); _rest[key] = (value, 0);
            _paramRows.Add((key, name, unit, Add(new NumberParam { Section = "Document parameter", Label = $"{name}  (parameter)", Value = value, Min = -1e5, Max = 1e5, Step = .001, Unit = unit, IsVisible = _paramRows.Count == 0 })));
        }
        if (_rampParam != null)
        {
            _rampFrom = Add(new NumberParam { Section = "Document parameter", Label = "From", Value = animatable[0].Value, Min = -1e5, Max = 1e5, Step = .001, Unit = "" });
            _rampTo = Add(new NumberParam { Section = "Document parameter", Label = "To", Value = animatable[0].Value == 0 ? 10 : animatable[0].Value * 2, Min = -1e5, Max = 1e5, Step = .001, Unit = "" });
            _rampStart = Add(new NumberParam { Section = "Document parameter", Label = "Starts at", Value = 0, Min = 0, Max = 600, Step = .001, Unit = "s" });
            _rampLength = Add(new NumberParam { Section = "Document parameter", Label = "Takes", Value = 2, Min = .001, Max = 600, Step = .001, Unit = "s" });
            var changed = _rampParam.Changed;
            _rampParam.Changed = () =>
            {
                var a = animatable[Math.Clamp(_rampParam.Index, 0, animatable.Count - 1)];
                foreach (var row in _paramRows) row.p.IsVisible = row.name == a.Name;
                _rampFrom!.Caption = a.Unit.Length == 0 ? "From" : $"From ({a.Unit})";
                _rampTo!.Caption = a.Unit.Length == 0 ? "To" : $"To ({a.Unit})";
                _rampFrom!.Value = a.Value; _rampTo!.Value = a.Value == 0 ? 10 : a.Value * 2;
                changed?.Invoke();
            };
            _rampFrom.Caption = animatable[0].Unit.Length == 0 ? "From" : $"From ({animatable[0].Unit})";
            _rampTo.Caption = animatable[0].Unit.Length == 0 ? "To" : $"To ({animatable[0].Unit})";
            Add(new ActionParam { Section = "Document parameter", Label = "Create parameter ramp", Run = AddRamp });
        }
        _explode = Add(new NumberParam { Section = "Assembly", Label = "Exploded view", Value = S.ExplodeFactor, Min = 0, Max = 1, Step = 0.05, Unit = "" });
        Add(new ActionParam { Label = "Key the changed values at this time", Run = KeyChanged });
        Add(new ActionParam { Label = "Remove keys at this time", Run = RemoveKeys });
        _play = Add(new ActionParam { Label = "Play / Pause", Run = TogglePlay });
        PullPose();
    }

    // ------------------------------------------------------------ keys
    Dictionary<string, (double v1, double v2)> Pose(double t) => (_anim with { Ease = _ease.Value }).Sample(t, _rest);

    /// <summary>Fill the value boxes with the animated values at the current time.</summary>
    void PullPose() => ConfigureInputs(() =>
    {
        _syncing = true;
        var pose = Pose(_time.Value);
        foreach (var r in _rows)
        {
            var v = pose.TryGetValue(r.joint.Key, out var p) ? p : _rest.GetValueOrDefault(r.joint.Key);
            r.v1.Value = Math.Clamp(v.v1, r.v1.Min, r.v1.Max);
            if (r.v2 != null) r.v2.Value = Math.Clamp(v.v2, r.v2.Min, r.v2.Max);
        }
        foreach (var pr in _partRows) pr.p.Value = Math.Clamp((pose.TryGetValue(pr.key, out var pv) ? pv : (0, 0)).v1, pr.p.Min, pr.p.Max);
        foreach (var pr in _paramRows) pr.p.Value = Math.Clamp((pose.TryGetValue(pr.key, out var qv) ? qv : _rest[pr.key]).v1, pr.p.Min, pr.p.Max);
        _explode.Value = Math.Clamp((pose.TryGetValue(ExplodeKey, out var e) ? e : _rest[ExplodeKey]).v1, 0, 1);
        _syncing = false;
    });

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
        foreach (var pr in _partRows) Key(pr.key, false, pr.p.Value, (pose.TryGetValue(pr.key, out var pv) ? pv : (0, 0)).v1);
        foreach (var pr in _paramRows) Key(pr.key, false, pr.p.Value, (pose.TryGetValue(pr.key, out var qv) ? qv : _rest[pr.key]).v1);
        var ex = pose.TryGetValue(ExplodeKey, out var ev) ? ev : _rest[ExplodeKey];
        Key(ExplodeKey, false, _explode.Value, ex.v1);
        S.Say(keyed == 0 ? "Nothing changed at this time, so nothing was keyed." : $"Keyed {keyed} value(s) at {t:0.###} s.");
        OnChanged(); TimelineChanged?.Invoke();
    }

    /// <summary>Animate one parameter from a start value to an end value over a time: two keys, so the length of time sets the speed.</summary>
    void AddRamp()
    {
        if (_rampParam == null || _paramRows.Count == 0) return;
        var row = _paramRows.First(r => r.name == _rampParam.Selected);
        double start = Math.Round(_rampStart!.Value, 3), end = Math.Round(start + _rampLength!.Value, 3);
        if (end > _duration.Max)
        {
            S.Say($"The ramp must end at or before {_duration.Max:G} s.", true);
            return;
        }
        _anim.Tracks.RemoveAll(t => t.JointKey == row.key);
        var keys = new List<AnimKey>();
        if (start > 1e-6) keys.Add(new AnimKey(0, _rampFrom!.Value));          // hold the first value until the ramp begins
        keys.Add(new AnimKey(start, _rampFrom!.Value)); keys.Add(new AnimKey(end, _rampTo!.Value));
        _anim.Tracks.Add(new AnimTrack(row.key, false, keys));
        if (end > _duration.Value) _duration.Value = Math.Min(end, _duration.Max);
        double speed = Math.Abs(_rampTo!.Value - _rampFrom.Value) / Math.Max(_rampLength.Value, 1e-9);
        S.Say($"{row.name} goes from {_rampFrom.Value:0.##} to {_rampTo.Value:0.##} {row.unit} between {start:0.##} s and {end:0.##} s ({speed:0.##} {row.unit}/s).");
        PullPose(); OnChanged(); TimelineChanged?.Invoke();
    }

    void RemoveKeys()
    {
        double t = Math.Round(_time.Value, 3); int n = 0;
        foreach (var tr in _anim.Tracks) n += tr.Keys.RemoveAll(k => Math.Abs(k.T - t) < 1e-6 && k.T > 1e-6);
        _anim.Tracks.RemoveAll(tr => tr.Keys.Count < 2);
        S.Say(n == 0 ? "There are no keys at this time (the keys at 0 s are the starting position)." : $"Removed {n} key(s).");
        PullPose(); OnChanged(); TimelineChanged?.Invoke();
    }

    // ------------------------------------------------------------ timeline view
    /// <summary>One row of the timeline: a joint value (or the exploded view) and its keys, if it has any.</summary>
    public sealed record TimelineRow(string Group, string Label, string JointKey, bool Second, AnimTrack? Track, NumberParam Value);

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
            rows.Add(new("Joints", r.v2 == null ? name : $"{name} ({r.joint.FirstLabel})", r.joint.Key, false, _anim.Tracks.FirstOrDefault(t => t.JointKey == r.joint.Key && !t.SecondValue), r.v1));
            if (r.v2 != null) rows.Add(new("Joints", $"{name} ({r.joint.SecondLabel})", r.joint.Key, true, _anim.Tracks.FirstOrDefault(t => t.JointKey == r.joint.Key && t.SecondValue), r.v2));
        }
        foreach (var pr in _partRows) rows.Add(new("Free components", pr.label, pr.key, false, _anim.Tracks.FirstOrDefault(t => t.JointKey == pr.key && !t.SecondValue), pr.p));
        foreach (var pr in _paramRows) rows.Add(new("Parameters", pr.name, pr.key, false, _anim.Tracks.FirstOrDefault(t => t.JointKey == pr.key && !t.SecondValue), pr.p));
        rows.Add(new("Assembly", "Exploded view", ExplodeKey, false, _anim.Tracks.FirstOrDefault(t => t.JointKey == ExplodeKey && !t.SecondValue), _explode));
        return rows;
    }

    /// <summary>Put a key on a row at time t, holding whatever value the animation has there now (or the current box value if the row has no keys yet).</summary>
    public void AddKey(TimelineRow row, double t)
    {
        t = Math.Round(Math.Clamp(t, 0, _duration.Value), 3);
        var pose = Pose(t);
        double Current()
        {
            if (row.JointKey == ExplodeKey) return _anim.Tracks.Any(x => x.JointKey == ExplodeKey) ? (pose.TryGetValue(ExplodeKey, out var e) ? e.v1 : _explode.Value) : _explode.Value;
            if (_partRows.FirstOrDefault(x => x.key == row.JointKey) is { key: not null } part)
                return _anim.Tracks.Any(x => x.JointKey == part.key) && pose.TryGetValue(part.key, out var pp) ? pp.v1 : part.p.Value;
            if (_paramRows.FirstOrDefault(x => x.key == row.JointKey) is { key: not null } prm)
                return _anim.Tracks.Any(x => x.JointKey == prm.key) && pose.TryGetValue(prm.key, out var qq) ? qq.v1 : prm.p.Value;
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

    public void SelectKey(AnimTrack? track, AnimKey? key)
    {
        SelectedTrack = track;
        SelectedKey = key;
        _keyValue.IsVisible = _applyKey.IsVisible = key != null;
        _keyTime.IsVisible = key is { T: > 0 };
        if (key != null && Rows().FirstOrDefault(r => ReferenceEquals(r.Track, track)) is { } row)
        {
            _keyTime.Value = key.T;
            _keyValue.Value = key.V;
            _keyValue.Caption = row.Value.Unit.Length == 0 ? $"{row.Label} value" : $"{row.Label} ({row.Value.Unit})";
        }
        RefreshWorkflow();
        TimelineChanged?.Invoke();
    }

    void ApplyKey()
    {
        if (SelectedTrack is { } track && SelectedKey is { } key)
        {
            EditKey(track, key, _keyTime.Value, _keyValue.Value);
            if (SelectedKey is { } applied) Time = applied.T;
        }
    }

    public void MoveKey(AnimTrack track, AnimKey key, double newTime)
        => EditKey(track, key, newTime, key.V);

    void EditKey(AnimTrack track, AnimKey key, double newTime, double value)
    {
        int i = track.Keys.IndexOf(key);
        if (i < 0 || Rows().FirstOrDefault(r => ReferenceEquals(r.Track, track)) is not { } row) return;
        newTime = key.T < 1e-6 ? 0 : Math.Round(Math.Clamp(newTime, .001, _duration.Value), 3);
        if (track.Keys.Any(k => !ReferenceEquals(k, key) && Math.Abs(k.T - newTime) < 1e-6))
        {
            S.Say("This track already has a key at that time. Choose a different time.", true);
            return;
        }
        if (value != key.V && (value < row.Value.Min || value > row.Value.Max))
        {
            S.Say($"Use a key value from {row.Value.Min:G} to {row.Value.Max:G} {row.Value.Unit}.", true);
            return;
        }
        var replacement = key with { T = newTime, V = value };
        if (replacement == key) return;
        track.Keys[i] = replacement;
        track.Keys.Sort((a, b) => a.T.CompareTo(b.T));
        SelectKey(track, replacement);
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
        if (!_startNew && _animObj == null && _rows.Count == 0 && !S.Components.Any() && _paramRows.Count == 0) { Results.Add(new ResultLine("Joints", "none yet. Create revolute, slider or cylindrical joints first (Joints tab).")); return null; }
        var sampled = Pose(_time.Value);
        var edited = _rows.Where(r =>
        {
            var baseline = sampled.GetValueOrDefault(r.joint.Key, _rest[r.joint.Key]);
            return Math.Abs(r.v1.Value - Math.Clamp(baseline.v1, r.v1.Min, r.v1.Max)) > 1e-9 ||
                r.v2 != null && Math.Abs(r.v2.Value - Math.Clamp(baseline.v2, r.v2.Min, r.v2.Max)) > 1e-9;
        }).Select(r => r.joint.Key);
        var over = S.ResolveAnimationJoints(_rows.ToDictionary(r => r.joint.Key, r => r.joint with { Value = r.v1.Value, Value2 = r.v2?.Value ?? r.joint.Value2 }), _anim, edited);
        var partPose = CadSession.PartPoseFrom(_partRows.ToDictionary(r => r.key, r => (r.p.Value, 0.0)));
        var shapes = S.PoseShapes(over, _explode.Value, out var notes, partPose);
        var o = new CommandOutput { Label = "Animation", Icon = "animation", ReplaceIsMove = true };
        foreach (var (id, shape) in shapes) if (S.Find(id) is { } comp) o.Replace.Add((comp, shape));
        // bodies driven by a parameter that is animated are shown rebuilt for this moment (in the preview only: saving keeps the real values)
        var animated = _paramRows.Where(r => _anim.Tracks.Any(t => t.JointKey == r.key)).ToDictionary(r => r.name, r => r.p.Value);
        if (animated.Count > 0 && !Committing && S.ParameterPoseShapes(animated) is { } rebuilt)
            foreach (var (id, shape) in rebuilt) if (S.Find(id) is { } body) o.Replace.Add((body, shape));
        foreach (var r in _paramRows)
            if (_anim.Tracks.FirstOrDefault(t => t.JointKey == r.key && t.Keys.Count >= 2) is { } track)
            {
                var ks = track.Keys.OrderBy(k => k.T).ToList(); var (first, last) = (ks[0], ks[^1]);
                // the speed is the change over the time it takes to happen, not counting the wait before it starts
                var from = ks.Last(k => Math.Abs(k.V - first.V) < 1e-9); var to = ks.First(k => Math.Abs(k.V - last.V) < 1e-9);
                double span = to.T > from.T ? to.T - from.T : Math.Max(last.T - first.T, 1e-9), speed = Math.Abs(last.V - first.V) / span;
                Results.Add(new ResultLine(r.name, $"{first.V:0.##} → {last.V:0.##} {r.unit} over {span:0.##} s  ({speed:0.##} {r.unit}/s)", 1));
            }
        var values = over.ToDictionary(pair => pair.Key, pair => (pair.Value.Value, pair.Value.Value2));
        double explode = _explode.Value;
        var final = _anim with { Duration = _duration.Value, Loop = _loop.Value, Ease = _ease.Value };
        bool create = _createNew; var existing = _animObj;
        o.After = s =>
        {
            if (create) s.AddAnimation(final);
            else s.Update(existing!.Id, x => x.WithAsm(final), false);
            s.SetJointValues(values);
            s.SetExplode(explode);
            s.SetPartPose(null);              // the parts go back to where they are placed; the animation carries their motion
        };
        var keys = _anim.Tracks.Sum(t => t.Keys.Count);
        Results.Add(new ResultLine("Keys", $"{keys} on {_anim.Tracks.Count} value(s)"));
        Results.Add(new ResultLine("Length", $"{_duration.Value:0.##} s"));
        foreach (var n in notes.Distinct()) Results.Add(new ResultLine("Note", n, 1));
        return o;
    }
}
