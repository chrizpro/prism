using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.Simulation;

namespace Prism.Commands;

/// <summary>
/// Kinematics and motion dynamics of the assembly. The parts, joints, gears and grounding come from the Assemble workspace; this adds what moves them:
/// a motor (constant speed or oscillation), a free joint that swings under gravity, loads and friction. Results are the motion of every joint, the speed
/// and acceleration of every part, the torque or force the drives need and where parts collide.
/// </summary>
public enum MotionKind { Kinematics, Dynamics, Robot }

public sealed class MotionStudyCommand : Command
{
    sealed class Row
    {
        public required MotionDof Dof;
        public ChoiceParam? Mode; public NumberParam? Start, Rate, StartSpeed, Amplitude, Frequency, Effort, Damping, Target, RampStart, RampTime, MotorStall, MotorNoLoad;
        public BoolParam? Use;
    }

    readonly MotionKind _kind;
    DriveMode[] _modeMap = [];
    SelectionSlot? _tool, _pathSlot;
    NumberParam? _speed, _accel, _payload;
    BoolParam? _hold, _torques;
    bool _dynamic => _kind == MotionKind.Dynamics || _kind == MotionKind.Robot && (_torques?.Value ?? true);
    bool Robot => _kind == MotionKind.Robot;
    readonly List<Row> _rows = [];
    NumberParam _duration = null!, _interval = null!, _time = null!;
    ChoiceParam? _gravity;
    BoolParam _collide = null!, _paths = null!, _loop = null!;
    TextParam _file = null!;
    DispatcherTimer? _timer; DateTime _last; bool _playing;
    MotionResult? _result;
    List<ResultLine> _summary = [];
    string _fingerprint = "", _error = "";
    readonly List<string> _collisions = [];
    static readonly string[] GravityNames = ["Off", "Down (−Z)", "−Y", "−X", "Up (+Z)", "+Y", "+X"];
    static readonly Vec3[] GravityDirs = [Vec3.Zero, -Vec3.UnitZ, -Vec3.UnitY, -Vec3.UnitX, Vec3.UnitZ, Vec3.UnitY, Vec3.UnitX];
    public const double G = 9.80665;

    public MotionStudyCommand(bool dynamic) : this(dynamic ? MotionKind.Dynamics : MotionKind.Kinematics) { }
    public MotionStudyCommand(MotionKind kind) => _kind = kind;

    public bool IsDynamic => _kind == MotionKind.Dynamics;
    public bool IsRobot => Robot;
    public MotionResult? Result => _result;
    public override bool SupportsPreview => false;
    public override string Title => _kind switch { MotionKind.Dynamics => "Motion Dynamics", MotionKind.Robot => "Robot Path", _ => "Kinematics" };
    public override string Icon => _kind switch { MotionKind.Dynamics => "motion-dynamics", MotionKind.Robot => "motion-robot", _ => "motion-kinematics" };
    public override string OkText => "Done";
    public override string Hint => Robot
        ? "Move a robot arm's tool along a path. Pick a point on the arm's last link (the tool), then the curve to follow (a sketch, a curve or edges). The joint motion is worked out for you by inverse kinematics along a speed profile with rounded corners, then played back. With joint torques on it also reports what each motor must supply, with a payload at the tool. Build the arm in the Assemble workspace first; each joint you leave ticked is used to reach the path."
        : _dynamic
        ? "Rigid-body motion of the assembly. Choose for each joint whether a motor drives it (constant speed or oscillation) or it is free to move under gravity, a torque or force and friction. Gears and racks made in the Assemble workspace carry the motion on. Results include the torque a motor needs, power, energy, and the speed and acceleration of every part. Each part needs a material for its mass. Parts are rigid and joints and gears ideal: no flexing, tooth contact, backlash or collisions between moving parts."
        : "Move the assembly through a prescribed motion: drive joints at a constant speed or oscillate them, and see every joint's position, speed and acceleration, the paths and speeds of the parts, and where they collide. Joints, gears and the grounded part come from the Assemble workspace. No masses or forces are needed; use Motion Dynamics for those.";

    protected override void OnBegin()
    {
        var joints = S.JointObjects.Select(j => j.joint).ToList();
        var couplings = S.Couplings.ToList();
        var dofs = MotionEngine.DofsOf(joints, couplings);
        T Run<T>(T p) where T : Param { p.Section = "Run and review"; return p; }
        if (Robot)
        {
            _tool = Slot(new SelectionSlot { Label = "Tool point (a face, circle or edge on the arm's last link)", Filter = SelFilter.Face | SelFilter.Edge });
            _pathSlot = Slot(new SelectionSlot { Label = "Path to follow (a curve, sketch or edges, in order)", Filter = SelFilter.Edge | SelFilter.CurveObj | SelFilter.SketchObj, Multi = true, Ordered = true });
            _speed = Add(new NumberParam { Label = "Tool speed", Value = 100, Min = 0.01, Max = 1e6, Step = 10, Unit = "mm/s" });
            _accel = Add(new NumberParam { Label = "Acceleration along the path", Value = 500, Min = 0.01, Max = 1e7, Step = 50, Unit = "mm/s²" });
            _interval = Add(new NumberParam { Label = "Output interval", Value = 0.02, Min = 0.0005, Max = 60, Step = 0.01, Unit = "s" });
            _hold = Check("Keep the tool pointing the same way (needs six joints)", true);
            _torques = Check("Work out the joint torques (every part needs a material)", true);
            _payload = Add(new NumberParam { Label = "Payload carried at the tool", Value = 0, Min = 0, Max = 1e6, Step = 0.5, Unit = "kg" });
            _gravity = Choice("Gravity", 1, GravityNames);
        }
        else
        {
            _duration = Add(new NumberParam { Label = "Duration", Value = 5, Min = 0.01, Max = 3600, Step = 0.5, Unit = "s" });
            _interval = Add(new NumberParam { Label = "Output interval", Value = 0.02, Min = 0.0005, Max = 60, Step = 0.01, Unit = "s" });
            if (_dynamic) _gravity = Choice("Gravity", 1, GravityNames);
        }
        _modeMap = _dynamic ? [DriveMode.Hold, DriveMode.Constant, DriveMode.Oscillate, DriveMode.Free, DriveMode.Ramp] : [DriveMode.Hold, DriveMode.Constant, DriveMode.Oscillate, DriveMode.Ramp];
        string[] modeNames = _dynamic ? ["Hold", "Constant speed", "Oscillate", "Free (moves under load and gravity)", "Move to a position (smooth)"] : ["Hold", "Constant speed", "Oscillate", "Move to a position (smooth)"];
        foreach (var d in dofs)
        {
            var row = new Row { Dof = d };
            string sect = d.Follower ? d.Name + " (follows a gear or rack)" : d.Name;
            T Sec<T>(T p) where T : Param { p.Section = sect; return p; }
            if (!d.Follower && Robot)
            {
                row.Use = Sec(Add(new BoolParam { Label = "Use " + d.Name + " to move the arm", Value = true }));
                row.Start = Sec(Add(new NumberParam { Label = "Starting position", Value = Math.Round(d.Start, 4), Min = -1e6, Max = 1e6, Step = d.Angular ? 5 : 1, Unit = d.Unit }));
            }
            else if (!d.Follower)
            {
                row.Mode = Sec(Add(new ChoiceParam { Label = d.Name + " — motion", Options = modeNames, Index = 0 }));
                row.Start = Sec(Add(new NumberParam { Label = "Starting position", Value = Math.Round(d.Start, 4), Min = -1e6, Max = 1e6, Step = d.Angular ? 5 : 1, Unit = d.Unit }));
                row.Rate = Sec(Add(new NumberParam { Label = "Speed", Value = d.Angular ? 90 : 20, Min = -1e6, Max = 1e6, Step = d.Angular ? 10 : 5, Unit = d.Unit + "/s" }));
                row.StartSpeed = Sec(Add(new NumberParam { Label = "Starting speed", Value = 0, Min = -1e6, Max = 1e6, Step = d.Angular ? 10 : 5, Unit = d.Unit + "/s" }));
                row.Amplitude = Sec(Add(new NumberParam { Label = "Amplitude", Value = d.Angular ? 45 : 20, Min = 0, Max = 1e6, Step = d.Angular ? 5 : 1, Unit = d.Unit }));
                row.Frequency = Sec(Add(new NumberParam { Label = "Frequency", Value = 0.5, Min = 0.001, Max = 1000, Step = 0.1, Unit = "Hz" }));
                row.Target = Sec(Add(new NumberParam { Label = "Move to", Value = Math.Round(d.Start + (d.Angular ? 90 : 50), 4), Min = -1e6, Max = 1e6, Step = d.Angular ? 5 : 1, Unit = d.Unit }));
                row.RampStart = Sec(Add(new NumberParam { Label = "Move starts at", Value = 0, Min = 0, Max = 1e6, Step = 0.1, Unit = "s" }));
                row.RampTime = Sec(Add(new NumberParam { Label = "Move takes", Value = 1, Min = 0.001, Max = 1e6, Step = 0.1, Unit = "s" }));
            }
            if (_dynamic)
            {
                if (!d.Follower && !Robot)
                {
                    row.MotorStall = Sec(Add(new NumberParam { Label = d.Angular ? "Motor stall torque (0 = no motor)" : "Motor stall force (0 = no motor)", Value = 0, Min = 0, Max = 1e9, Step = 0.1, Unit = d.EffortUnit }));
                    row.MotorNoLoad = Sec(Add(new NumberParam { Label = "Motor no-load speed", Value = d.Angular ? 1000 : 100, Min = 0.001, Max = 1e9, Step = 10, Unit = d.Unit + "/s" }));
                }
                if (!Robot) row.Effort = Sec(Add(new NumberParam { Label = d.Follower ? "Load" : "Applied " + (d.Angular ? "torque" : "force"), Value = 0, Min = -1e9, Max = 1e9, Step = d.Angular ? 0.1 : 1, Unit = d.EffortUnit }));
                row.Damping = Sec(Add(new NumberParam { Label = d.Angular ? "Viscous friction (N·m·s/rad)" : "Viscous friction (N·s/m)", Value = 0, Min = 0, Max = 1e9, Step = 0.001, Unit = "" }));
            }
            _rows.Add(row);
        }
        if (!Robot) _collide = Check("Look for collisions along the motion", false);
        else _collide = new BoolParam { Label = "", Value = false };
        _paths = Check("Show the paths the parts take", S.ShowMotionPaths);
        _paths.Changed = () => { S.ShowMotionPaths = _paths.Value; OnChanged(); };
        Run(Add(new ActionParam { Label = "Run study", Run = RunStudy }));
        _time = Run(Add(new NumberParam { Label = "Show the motion at", Value = 0, Min = 0, Max = 100, Step = 1, Unit = "%" }));
        _time.Changed = () => { if (_result != null) S.SetMotionSample(_result.SampleAt(Seconds)); OnChanged(); };
        _loop = Run(Check("Loop the playback", true));
        Run(Add(new ActionParam { Label = "Play  /  Stop", Run = TogglePlay }));
        _file = Run(Text("Save results as CSV", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "Motion", Robot ? "robot-path.csv" : _dynamic ? "motion-dynamics.csv" : "kinematics.csv")));
        Run(Add(new ActionParam { Label = "Save results", Run = SaveCsv }));
    }

    protected override void UpdateVisibility()
    {
        foreach (var r in _rows)
        {
            if (r.Mode == null) continue;
            var mode = _modeMap[Math.Clamp(r.Mode.Index, 0, _modeMap.Length - 1)];
            r.Rate!.IsVisible = mode == DriveMode.Constant; r.StartSpeed!.IsVisible = mode == DriveMode.Free;
            r.Amplitude!.IsVisible = r.Frequency!.IsVisible = mode == DriveMode.Oscillate;
            r.Target!.IsVisible = r.RampStart!.IsVisible = r.RampTime!.IsVisible = mode == DriveMode.Ramp;
            if (r.Effort != null) r.Effort.IsVisible = mode == DriveMode.Free;
            if (r.MotorStall != null) r.MotorStall.IsVisible = r.MotorNoLoad!.IsVisible = mode == DriveMode.Free;
        }
        if (_time != null) _time.IsVisible = _result != null;
    }

    /// <summary>The time the viewport shows: the slider is a share of the run, so it always fits whatever the duration.</summary>
    double Seconds => _result == null ? 0 : _time.Value / 100 * _result.Time[^1];

    public override void OnEnd() { StopPlay(); S.ShowMotion(null); }

    // ------------------------------------------------------------------ running
    string Fingerprint() => string.Join(",", Slots.Select(x => string.Join(";", x.Items.Select(i => $"{i.Obj}{i.Kind}{i.Index}")))) + "|" + string.Join("|", Params.Where(p => p != _time && p != _loop && p != _paths && p != _file).Select(p => p switch
    {
        NumberParam n => n.Label + n.Section + n.Value.ToString("R", CultureInfo.InvariantCulture), ChoiceParam c => c.Label + c.Index, BoolParam b => b.Label + b.Value, _ => "",
    }));

    void Apply()
    {
        foreach (var r in _rows)
        {
            var d = r.Dof;
            if (r.Mode != null)
            {
                d.Mode = _modeMap[Math.Clamp(r.Mode.Index, 0, _modeMap.Length - 1)];
                d.Start = r.Start!.Value; d.Rate = d.Mode == DriveMode.Free ? r.StartSpeed!.Value : r.Rate!.Value; d.Amplitude = r.Amplitude!.Value; d.Frequency = r.Frequency!.Value;
                d.Target = r.Target!.Value; d.RampStart = r.RampStart!.Value; d.RampTime = r.RampTime!.Value;
            }
            else if (r.Start != null) { d.Start = r.Start.Value; d.Mode = DriveMode.Hold; }
            d.Effort = r.Effort?.Value ?? 0; d.Damping = r.Damping?.Value ?? 0;
            d.MotorStall = r.MotorStall?.Value ?? 0; d.MotorNoLoad = r.MotorNoLoad?.Value ?? 0;
        }
    }

    void RunStudy()
    {
        try
        {
            StopPlay(); _error = ""; _collisions.Clear();
            if (_rows.Count == 0) throw new InvalidOperationException("There are no joints that can move. Build the mechanism in the Assemble workspace first: insert the parts, ground one, and add revolute, slider or other joints (and gears).");
            Apply();
            var gravity = _dynamic && _gravity != null ? GravityDirs[_gravity.Index] * G : Vec3.Zero;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            MotionEngine engine; List<string> notes; List<MotionBody> bodies; MotionResult result;
            var extra = new List<string>();
            if (Robot)
            {
                var (points, used, tool, local, pts) = RobotSetup();
                var planner = S.BuildMotionEngine(_rows.Select(r => r.Dof), false, out notes, out bodies, points);
                // the joints that move the arm, as coordinates of the engine (a loop may fix some of the ticked joints)
                var active = Enumerable.Range(0, planner.IndependentCount).Where(k => used.Contains(planner.IndependentDof(k))).ToList();
                if (active.Count == 0) throw new InvalidOperationException("The joints you ticked are all fixed by closed loops; tick one that is free to move.");
                bool hold = _hold!.Value && active.Count >= 6;
                if (_hold.Value && !hold) extra.Add($"The arm has {active.Count} usable joints; keeping the tool's direction needs six, so only its position follows the path.");
                var plan = RobotPath.Follow(planner, active, tool, local, pts, _speed!.Value, _accel!.Value, _interval.Value, hold);
                int ai = 0;
                foreach (int k in active) planner.IndependentDof(k).SetTable(plan.Times, plan.JointValues[ai++]);
                engine = S.BuildMotionEngine(_rows.Select(r => r.Dof), _dynamic, out notes, out bodies, points);
                result = engine.Run(new MotionSettings(plan.Times[^1], plan.Times[1] - plan.Times[0], _dynamic, gravity));
                extra.Add(plan.Unreached == 0 ? $"Reached all {plan.Times.Length} points of the {plan.PathLength:0.#} mm path (worst miss {plan.MaxPositionError:G2} mm) in {plan.Times[^1]:0.###} s." : $"{plan.Unreached} of {plan.Times.Length} points could not be reached.");
                foreach (var n in plan.Notes) extra.Add(n);
            }
            else
            {
                engine = S.BuildMotionEngine(_rows.Select(r => r.Dof), _dynamic, out notes, out bodies);
                result = engine.Run(new MotionSettings(_duration.Value, _interval.Value, _dynamic, gravity));
            }
            _reach = extra;
            _result = result; S.RememberMotion(result);
            if (_collide.Value) CheckCollisions(result);
            _summary = Summarize(result, bodies, notes, clock.Elapsed.TotalSeconds);
            _fingerprint = Fingerprint();
            _time.Value = 0;
            S.ShowMotion(result, 0);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { _error = e.Message; _result = null; _summary = []; S.ShowMotion(null); S.Say(e.Message, true); }
        OnChanged();
    }

    List<string> _reach = [];

    /// <summary>The tool point, the arm's joints and the path polyline for a robot study.</summary>
    (List<MotionPoint> points, List<MotionDof> used, string tool, Vec3 local, List<Vec3> path) RobotSetup()
    {
        if (_tool!.Items.Count == 0) throw new InvalidOperationException("Pick the tool point: a face, circle or edge on the arm's last link.");
        if (_pathSlot!.Items.Count == 0) throw new InvalidOperationException("Pick the path to follow: a curve, a sketch or edges.");
        var comp = MotionPicks.ComponentOf(S, _tool.Items[0]) ?? throw new InvalidOperationException("The tool point must be on a component of the assembly.");
        var world = MotionPicks.PointOf(S, _tool.Items[0]);
        var local = new Vec3(0, 0, 0); { var l = MotionPicks.Local(comp, world); local = new Vec3(l[0], l[1], l[2]); }
        var points = new List<MotionPoint> { new("Tool", comp.Component!.Key, local, 0) };
        if (_payload!.Value > 0) points.Add(new("Payload", comp.Component.Key, local, _payload.Value));
        var pieces = _pathSlot.Items.Select(i => MotionPicks.CurveOf(S, i)).Where(p => p.Count >= 2).ToList();
        var path = MotionPicks.Join(pieces);
        if (path.Count < 2) throw new InvalidOperationException("The picked path has no length.");
        var used = _rows.Where(r => !r.Dof.Follower && r.Use!.Value).Select(r => r.Dof).ToList();
        if (used.Count == 0) throw new InvalidOperationException("Tick at least one joint to move the arm.");
        return (points, used, comp.Component.Key, local, path);
    }

    static double Z(double v) => Math.Abs(v) < 5e-4 ? 0 : v;       // no "-0"

    List<ResultLine> Summarize(MotionResult r, List<MotionBody> bodies, List<string> notes, double seconds)
    {
        var res = new List<ResultLine>();
        res.Add(new("Solved", $"{r.Samples:N0} times over {r.Time[^1]:0.###} s  ({seconds:0.##} s to compute)", 1));
        foreach (var line in _reach) res.Add(new(line.StartsWith("Reached") ? "Path" : "Note", line, line.StartsWith("Reached") ? 1 : 2));
        double total = bodies.Sum(b => b.Mass) + r.Bodies.Where(b => b.Name == "Payload").Sum(b => b.Mass);
        if (_dynamic) res.Add(new("Moving mass", $"{total * 1000:0.###} g in {bodies.Count} part{(bodies.Count == 1 ? "" : "s")}"));
        for (int j = 0; j < r.Dofs.Length; j++)
        {
            var d = r.Dofs[j]; string u = d.Unit;
            double lo = r.Pos[j].Min(), hi = r.Pos[j].Max(), vmax = r.Vel[j].Select(Math.Abs).Max(), amax = r.Acc[j].Select(Math.Abs).Max();
            string role = d.Follower ? "follows" : d.Dependent ? "set by the loop" : d.Mode switch { DriveMode.Free when _dynamic => "free", DriveMode.Constant or DriveMode.Oscillate or DriveMode.Ramp or DriveMode.Table => "driven", _ => "held" };
            if (role == "held" && hi - lo < 1e-9) continue;
            res.Add(new($"{d.Name} ({role})", $"{Z(lo):0.###} to {Z(hi):0.###} {u}   ·   peak speed {vmax:0.###} {u}/s   ·   peak acceleration {amax:0.###} {u}/s²"));
            if (_dynamic && !d.Follower && d.Mode is DriveMode.Constant or DriveMode.Oscillate or DriveMode.Ramp or DriveMode.Table && r.Effort[j].Any(e => e != 0))
            {
                var e = r.Effort[j]; var p = r.Power[j];
                double peak = e.Select(Math.Abs).Max(), rms = Math.Sqrt(e.Select(x => x * x).Average());
                double work = 0; for (int i = 1; i < r.Samples; i++) work += (Math.Max(p[i], 0) + Math.Max(p[i - 1], 0)) / 2 * (r.Time[i] - r.Time[i - 1]);
                res.Add(new($"  drive {(d.Angular ? "torque" : "force")}", $"peak {peak:G4} {d.EffortUnit}   ·   rms {rms:G4} {d.EffortUnit}   ·   peak power {p.Select(Math.Abs).Max():G4} W   ·   energy put in {work:G4} J", 1));
            }
        }
        for (int f = 0; f < r.ForceNames.Length; f++)
        {
            double peak = r.ForceValue[f].Select(Math.Abs).Max(); if (peak < 1e-12) continue;
            res.Add(new($"{r.ForceNames[f]}", $"peak {peak:G4} (N or N·m){(r.ForceValue[f].Min() < 0 && r.ForceValue[f].Max() > 0 ? "   ·   changes sign" : "")}"));
        }
        // the parts that move fastest and that accelerate hardest
        var moving = Enumerable.Range(0, r.Bodies.Length).Where(b => !r.Bodies[b].Name.StartsWith('@') && (r.Speed[b].Max() > 1e-9 || r.AngSpeed[b].Max() > 1e-9)).ToList();
        foreach (int b in moving.OrderByDescending(b => r.Speed[b].Max()).Take(4))
        {
            double a = r.Accel[b].Max(); string force = _dynamic && r.Bodies[b].Mass > 0 ? $"   ·   inertial force {a * r.Bodies[b].Mass:0.###} N" : "";
            res.Add(new(r.Bodies[b].Name, $"peak {r.Speed[b].Max():0.####} m/s   ·   {a:0.###} m/s²   ·   turning {r.AngSpeed[b].Max() * 180 / Math.PI:0.###} °/s{force}"));
        }
        if (r.ReactionJoints.Length > 0)
        {
            var rows = Enumerable.Range(0, r.ReactionJoints.Length).Select(j => (j, f: r.ReactionForce[j].Max(v => v.Length), m: r.ReactionMoment[j].Max(v => v.Length)))
                .Where(x => x.f > 1e-9 || x.m > 1e-9).OrderByDescending(x => x.f).Take(8).ToList();
            foreach (var (j, f, m) in rows)
            {
                int at = Array.IndexOf(r.ReactionForce[j].Select(v => v.Length).ToArray(), f);
                res.Add(new($"Reaction at {r.ReactionJoints[j]}", $"force peaks at {f:G4} N ({r.Time[at]:0.###} s)   ·   moment {m:G3} N·m   (what the joint holds, apart from the turning or sliding it allows)"));
            }
        }
        if (_dynamic)
        {
            double e0 = r.Kinetic[0] + r.Potential[0], e1 = r.Kinetic[^1] + r.Potential[^1];
            bool anyDrive = r.Dofs.Any(d => !d.Follower && d.Mode is DriveMode.Constant or DriveMode.Oscillate or DriveMode.Ramp or DriveMode.Table), anyLoss = r.Dofs.Any(d => d.Damping > 0 || d.Effort != 0 || d.MotorStall != 0) || r.ForceNames.Length > 0;
            res.Add(new("Kinetic + potential energy", $"{e0:0.####} J at the start, {e1:0.####} J at the end{(anyDrive || anyLoss ? " (a motor, a load or friction adds or removes energy)" : "")}"));
        }
        foreach (var ev in r.Events) res.Add(new("Limit", ev, 2));
        foreach (var n in notes.Distinct()) res.Add(new("Note", n, 2));
        if (_collide.Value) res.Add(_collisions.Count == 0 ? new("Collisions", "none found at the sampled times", 1) : new("Collisions", $"{_collisions.Count} pair(s) overlap", 3));
        foreach (var c in _collisions) res.Add(new("  overlap", c, 3));
        if (_dynamic && r.Dofs.Any(d => !d.Follower && d.Mode == DriveMode.Free) && r.Time[^1] > 0) res.Add(new("Step", "Free joints are integrated with fixed 2 ms steps (4th-order Runge–Kutta)."));
        return res;
    }

    // ------------------------------------------------------------------ collisions along the motion
    void CheckCollisions(MotionResult r)
    {
        var comps = S.Components.ToList();
        if (comps.Count < 2) return;
        // parts that share a joint (or a gear coupling) touch by design
        var joined = new HashSet<(string, string)>();
        foreach (var (_, j) in S.JointObjects) { joined.Add((j.ParentKey, j.ChildKey)); joined.Add((j.ChildKey, j.ParentKey)); }
        var byJoint = S.JointObjects.ToDictionary(j => j.joint.Key, j => j.joint);
        foreach (var c in S.Couplings)
            if (byJoint.TryGetValue(c.LeaderKey, out var a) && byJoint.TryGetValue(c.FollowerKey, out var b))
                foreach (var pa in new[] { a.ParentKey, a.ChildKey }) foreach (var pb in new[] { b.ParentKey, b.ChildKey }) { joined.Add((pa, pb)); joined.Add((pb, pa)); }
        int frames = Math.Min(r.Samples, 36);
        var found = new Dictionary<(string, string), double>();
        for (int f = 0; f < frames; f++)
        {
            int sample = frames == 1 ? 0 : (int)Math.Round(f * (r.Samples - 1) / (double)(frames - 1));
            var pose = r.PoseAt(sample);
            var overrides = S.JointObjects.ToDictionary(j => j.joint.Key, j => pose.TryGetValue(j.joint.Key, out var v) ? j.joint with { Value = v.v1, Value2 = v.v2 } : j.joint);
            var shapes = S.PoseShapes(overrides, 0, out _);
            Shape ShapeOf(SceneObject o) => shapes.TryGetValue(o.Id, out var s) ? s : o.Shape;
            for (int i = 0; i < comps.Count; i++)
                for (int k = i + 1; k < comps.Count; k++)
                {
                    var ka = comps[i].Component!.Key; var kb = comps[k].Component!.Key;
                    if (joined.Contains((ka, kb)) || found.ContainsKey(Order(ka, kb))) continue;
                    try
                    {
                        var sa = ShapeOf(comps[i]); var sb = ShapeOf(comps[k]);
                        var ma = sa.Tessellate(); var mb = sb.Tessellate();
                        if (ma.Max.X < mb.Min.X || mb.Max.X < ma.Min.X || ma.Max.Y < mb.Min.Y || mb.Max.Y < ma.Min.Y || ma.Max.Z < mb.Min.Z || mb.Max.Z < ma.Min.Z) continue;
                        var common = Occ.Common(sa, sb);
                        if (common.HasSolids && common.Props().Volume > 0.01) { found[Order(ka, kb)] = r.Time[sample]; _collisions.Add($"{comps[i].Name} and {comps[k].Name} at {r.Time[sample]:0.###} s"); }
                    }
                    catch (KernelException) { }
                }
        }
    }
    static (string, string) Order(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    // ------------------------------------------------------------------ playback and output
    void TogglePlay()
    {
        if (_playing) { StopPlay(); OnChanged(); return; }
        if (_result == null) { S.Say("Run the study first, then play it.", true); return; }
        _playing = true; _last = DateTime.UtcNow;
        if (_time.Value >= 100 - 1e-9) _time.Value = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow; double dt = (now - _last).TotalSeconds; _last = now;
            double end = Math.Max(_result!.Time[^1], 1e-6); double t = Seconds + dt;
            if (t >= end) { if (_loop.Value) t %= end; else { _time.Value = 100; StopPlay(); return; } }
            _time.Value = t / end * 100;
        };
        _timer.Start();
    }

    void StopPlay() { _timer?.Stop(); _timer = null; _playing = false; }

    void SaveCsv()
    {
        try
        {
            var r = _result ?? throw new InvalidOperationException("Run the study first.");
            if (string.IsNullOrWhiteSpace(_file.Value)) throw new InvalidOperationException("Enter a file name.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file.Value))!);
            string F(double v) => double.IsFinite(v) ? v.ToString("G10", CultureInfo.InvariantCulture) : "";
            var head = new List<string> { "time_s" };
            for (int j = 0; j < r.Dofs.Length; j++)
            {
                var d = r.Dofs[j]; string n = d.Name.Replace(',', ';');
                head.Add($"{n} position_{d.Unit}"); head.Add($"{n} speed_{d.Unit}/s"); head.Add($"{n} acceleration_{d.Unit}/s2");
                if (r.Dynamic) { head.Add($"{n} drive_{d.EffortUnit}"); head.Add($"{n} power_W"); }
            }
            if (r.Dynamic) { head.Add("kinetic_J"); head.Add("potential_J"); foreach (var fn in r.ForceNames) head.Add($"{fn.Replace(',', ';')} force_N_or_Nm"); }
            for (int j = 0; j < r.ReactionJoints.Length; j++)
            { string n = r.ReactionJoints[j].Replace(',', ';'); foreach (var c in new[] { "fx_N", "fy_N", "fz_N", "mx_Nm", "my_Nm", "mz_Nm" }) head.Add($"{n} reaction {c}"); }
            var shown = Enumerable.Range(0, r.Bodies.Length).Where(b => !r.Bodies[b].Name.StartsWith('@')).ToList();
            foreach (int b in shown)
            { string n = r.Bodies[b].Name.Replace(',', ';'); head.Add($"{n} x_mm"); head.Add($"{n} y_mm"); head.Add($"{n} z_mm"); head.Add($"{n} speed_m/s"); head.Add($"{n} acceleration_m/s2"); }
            var lines = new List<string> { string.Join(",", head) };
            for (int i = 0; i < r.Samples; i++)
            {
                var cells = new List<string> { F(r.Time[i]) };
                for (int j = 0; j < r.Dofs.Length; j++)
                {
                    cells.Add(F(r.Pos[j][i])); cells.Add(F(r.Vel[j][i])); cells.Add(F(r.Acc[j][i]));
                    if (r.Dynamic) { cells.Add(F(r.Effort[j][i])); cells.Add(F(r.Power[j][i])); }
                }
                if (r.Dynamic) { cells.Add(F(r.Kinetic[i])); cells.Add(F(r.Potential[i])); for (int f = 0; f < r.ForceNames.Length; f++) cells.Add(F(r.ForceValue[f][i])); }
                for (int j = 0; j < r.ReactionJoints.Length; j++) { var fv = r.ReactionForce[j][i]; var mv = r.ReactionMoment[j][i]; cells.Add(F(fv.X)); cells.Add(F(fv.Y)); cells.Add(F(fv.Z)); cells.Add(F(mv.X)); cells.Add(F(mv.Y)); cells.Add(F(mv.Z)); }
                foreach (int b in shown) { var p = r.Path[b][i]; cells.Add(F(p.X)); cells.Add(F(p.Y)); cells.Add(F(p.Z)); cells.Add(F(r.Speed[b][i])); cells.Add(F(r.Accel[b][i])); }
                lines.Add(string.Join(",", cells));
            }
            File.WriteAllLines(_file.Value, lines);
            S.Say("Saved the motion results to " + _file.Value);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { S.Say(e.Message, true); }
    }

    // ------------------------------------------------------------------ preview of the pose at the shown time
    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_rows.Count == 0) { Results.Add(new ResultLine("Mechanism", "no joints that can move. Build it in the Assemble workspace: insert the parts, ground one, add joints (and gears), then come back.", 2)); return new CommandOutput { NoOp = true }; }
        if (_error.Length > 0) Results.Add(new ResultLine("Could not run", _error, 3));
        if (_result == null) { if (_error.Length == 0) Results.Add(new ResultLine("Setup", Robot ? "Pick the tool point and the path, tick the joints that move the arm, then press Run study." : "Set how each joint moves, then press Run study.")); return new CommandOutput { NoOp = true }; }
        if (_fingerprint != Fingerprint()) Results.Add(new ResultLine("Settings changed", "These results are from the earlier settings. Press Run study again.", 2));
        Results.Add(new ResultLine("Showing", $"{Seconds:0.###} s of {_result.Time[^1]:0.###} s"));
        foreach (var l in _summary) Results.Add(l);
        var o = new CommandOutput { NoOp = true, ReplaceIsMove = true, Label = Title, Icon = Icon };
        int sample = _result.SampleAt(Seconds);
        var pose = _result.PoseAt(sample);
        var overrides = S.JointObjects.ToDictionary(j => j.joint.Key, j => pose.TryGetValue(j.joint.Key, out var v) ? j.joint with { Value = v.v1, Value2 = v.v2 } : j.joint);
        foreach (var (id, shape) in S.PoseShapes(overrides, 0, out _)) if (S.Find(id) is { } comp) o.Replace.Add((comp, shape));
        return o;
    }
}
