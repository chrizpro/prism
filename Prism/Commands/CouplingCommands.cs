using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Gear pair or rack and pinion: ties the motion of two existing joints together. Set the two shafts up as revolute joints first
/// (a rack as a slider), then couple them here; whatever drives the first joint drives the second.
/// </summary>
public sealed class CouplingCommand : Command
{
    readonly bool _rack;
    ChoiceParam _leader = null!, _follower = null!, _mesh = null!;
    NumberParam _sizeA = null!, _sizeB = null!;
    readonly List<JointInfo> _leaders = [], _followers = [];

    public CouplingCommand(bool rack) => _rack = rack;

    public override string Title => _rack ? "Rack and Pinion" : "Gear Pair";
    public override string Icon => _rack ? "joint-rack" : "joint-gear";
    public override string OkText => "Couple joints";
    public override string Hint => _rack
        ? "Make a rack follow a pinion. First create a revolute joint for the pinion and a slider joint for the rack, then pick them here with the pinion's pitch diameter. Turning the pinion (or driving it in Kinematics or Motion Dynamics) slides the rack π × diameter per turn."
        : "Make one shaft follow another through gears. First create a revolute joint for each gear, then pick them here with the tooth counts (or pitch diameters) of the two gears. The driven gear turns driving ÷ driven times as far, opposite way for external gears. The gears are ideal: no tooth contact, backlash or friction.";

    protected override void OnBegin()
    {
        _leaders.Clear(); _followers.Clear();
        foreach (var (_, j) in S.JointObjects)
        {
            if (j.Type is JointType.Revolute or JointType.Cylindrical) _leaders.Add(j);
            if (_rack ? j.Type is JointType.Slider or JointType.Cylindrical : j.Type is JointType.Revolute or JointType.Cylindrical) _followers.Add(j);
        }
        string[] Names(List<JointInfo> l) => l.Count == 0 ? ["No suitable joints yet"] : l.Select(j => j.Name).ToArray();
        _leader = Choice(_rack ? "Pinion (revolute joint)" : "Driving gear (revolute joint)", 0, Names(_leaders));
        _follower = Choice(_rack ? "Rack (slider joint)" : "Driven gear (revolute joint)", _rack || _followers.Count < 2 ? 0 : 1, Names(_followers));
        _mesh = Choice(_rack ? "Rack moves along its +z when the pinion turns" : "Meshing", 0, _rack ? ["Positively (+)", "Negatively (−)"] : ["External gears (opposite directions)", "Internal gear or belt (same direction)"]);
        _sizeA = Num(_rack ? "Pinion pitch diameter" : "Driving gear size (teeth or pitch diameter)", 20, 0.001, 1e6, 1, _rack ? "mm" : "");
        if (!_rack) _sizeB = Num("Driven gear size (same unit)", 40, 0.001, 1e6, 1, "");
    }

    public CouplingInfo? Plan()
    {
        if (_leaders.Count == 0 || _followers.Count == 0 || _leader == null) return null;
        var a = _leaders[Math.Clamp(_leader.Index, 0, _leaders.Count - 1)];
        var b = _followers[Math.Clamp(_follower.Index, 0, _followers.Count - 1)];
        if (a.Key == b.Key) throw new FormatException("Pick two different joints.");
        if (S.Couplings.Any(c => c.FollowerKey == b.Key)) throw new FormatException($"{b.Name} already follows another joint.");
        if (S.Couplings.Any(c => c.FollowerKey == a.Key && c.LeaderKey == b.Key)) throw new FormatException("These two joints are already coupled the other way round.");
        string name = _rack ? $"Rack {S.Couplings.Count(c => c.Kind == CouplingKind.RackPinion) + 1}" : $"Gear pair {S.Couplings.Count(c => c.Kind == CouplingKind.Gear) + 1}";
        return _rack
            ? new CouplingInfo(CadSession.NewKey(), name, CouplingKind.RackPinion, a.Key, b.Key, CouplingInfo.RackRatio(_sizeA.Value), _mesh.Index == 1)
            : new CouplingInfo(CadSession.NewKey(), name, CouplingKind.Gear, a.Key, b.Key, CouplingInfo.GearRatio(_sizeA.Value, _sizeB.Value), _mesh.Index == 0);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var info = Plan();
        if (info == null) { Results.Add(new ResultLine("Joints", "create the revolute joints (or a slider for a rack) in the Joints tab first.", 2)); return null; }
        var a = _leaders.First(j => j.Key == info.LeaderKey); var b = _followers.First(j => j.Key == info.FollowerKey);
        Results.Add(new ResultLine("Between", $"{a.Name}  →  {b.Name}"));
        if (_rack) Results.Add(new ResultLine("Rack travel", $"{Math.Abs(info.Ratio) * 360:0.###} mm per turn of the pinion"));
        else
        {
            Results.Add(new ResultLine("Ratio", $"{_sizeA.Value:0.###} : {_sizeB.Value:0.###}  =  {_sizeB.Value / _sizeA.Value:0.###} : 1 {(_sizeB.Value > _sizeA.Value ? "(reduction)" : _sizeB.Value < _sizeA.Value ? "(speed up)" : "")}"));
            Results.Add(new ResultLine("Direction", info.Reverse ? "driven gear turns the opposite way" : "driven gear turns the same way"));
        }
        string? fit = Fit(a, b);
        if (fit != null) Results.Add(new ResultLine("Check", fit, 2));
        var o = new CommandOutput { Label = info.Name, Icon = Icon };
        var frame = S.ComponentByKey(a.ParentKey)?.Component is { } pc ? (pc.Placement * a.A).T : Vec3.Zero;
        o.Add.Add(new(Occ.Line(frame, frame + new Vec3(0, 0, 0.01)), _rack ? "Rack and pinion" : "Gear pair", ObjKind.Joint, Asm: info));
        o.After = s => s.SolveAssembly();
        return o;
    }

    /// <summary>A hint when the two shafts cannot sensibly mesh (not parallel, for gears) or sit at a distance that does not match the sizes.</summary>
    string? Fit(JointInfo a, JointInfo b)
    {
        var pa = S.ComponentByKey(a.ParentKey)?.Component; var pb = S.ComponentByKey(b.ParentKey)?.Component;
        if (pa == null || pb == null) return null;
        var fa = pa.Placement * a.A; var fb = pb.Placement * b.A;
        if (_rack) return null;
        double parallel = Math.Abs(Vec3.Dot(fa.AxisZ.Normalized(), fb.AxisZ.Normalized()));
        if (parallel < 0.999) return "The two shafts are not parallel, so these are not spur gears.";
        var d = fb.T - fa.T; d -= fa.AxisZ * Vec3.Dot(d, fa.AxisZ);
        double dist = d.Length;
        if (dist < 1e-6) return null;
        if (_sizeA.Value > 1 && _sizeB.Value > 1)
        {
            double module = dist / ((_sizeA.Value + _sizeB.Value) / 2);
            return $"Shaft spacing {dist:0.###} mm. With these sizes as tooth counts the module is {module:0.###} mm; as pitch diameters the spacing would be {(_sizeA.Value + _sizeB.Value) / 2:0.###} mm.";
        }
        return $"Shaft spacing {dist:0.###} mm.";
    }
}
