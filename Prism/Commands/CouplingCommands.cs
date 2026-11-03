using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// One joint follows another at a ratio: gears, belts and chains between turning joints; a rack, pulley or screw between a turning and a sliding joint;
/// a lever or cable between sliding joints. Make the two joints first; whatever moves the first moves the second.
/// </summary>
public sealed class CouplingCommand : Command
{
    ChoiceParam _leader = null!, _follower = null!, _travel = null!, _direction = null!;
    NumberParam _sizeA = null!, _sizeB = null!, _length = null!, _ratio = null!;
    readonly List<JointInfo> _joints = [];

    public override string Title => "Couple";
    public override string Icon => "joint-gear";
    public override string OkText => "Couple joints";
    public override string Hint => "Make one joint follow another at a ratio. Two turning joints work as gears, a belt or a chain. A turning and a sliding joint work as a rack, a pulley or a screw. Two sliding joints work as a lever or a cable. The link is ideal: no tooth contact, backlash, slip or friction.";

    JointInfo? Leader => _joints.Count == 0 ? null : _joints[Math.Clamp(_leader.Index, 0, _joints.Count - 1)];
    JointInfo? Follower => _joints.Count == 0 ? null : _joints[Math.Clamp(_follower.Index, 0, _joints.Count - 1)];

    protected override void OnBegin()
    {
        _joints.Clear();
        _joints.AddRange(S.JointObjects.Select(j => j.joint).Where(j => j.Type is JointType.Revolute or JointType.Cylindrical or JointType.Slider));
        string[] names = _joints.Count == 0 ? ["No suitable joints yet"] : _joints.Select(j => j.Name).ToArray();
        _leader = Choice("Leading joint", 0, names);
        _follower = Choice("Following joint", _joints.Count < 2 ? 0 : 1, names);
        _sizeA = Num("Leading size (teeth, or pulley or pitch diameter)", 20, 0.001, 1e6, 1, "");
        _sizeB = Num("Following size (same unit)", 40, 0.001, 1e6, 1, "");
        _travel = Choice("Travel set by", 0, "Pitch diameter (rack, pulley, winch)", "Lead per turn (screw)");
        _length = Num("Diameter or lead", 20, 0.001, 1e6, 1, "mm");
        _ratio = Num("Following travel per mm of leading travel", 1, 1e-6, 1e6, 0.1, "");
        _direction = Choice("Direction", Leader?.UsesAngle == true && Follower?.UsesAngle == true ? 1 : 0, "Same way (belt, chain, internal gear)", "Opposite way (external gears)");
        UpdateVisibility();
    }

    protected override void UpdateVisibility()
    {
        if (_direction == null || Leader is not { } a || Follower is not { } b) return;
        _sizeA.IsVisible = _sizeB.IsVisible = a.UsesAngle && b.UsesAngle;
        _travel.IsVisible = _length.IsVisible = a.UsesAngle != b.UsesAngle;
        _ratio.IsVisible = !a.UsesAngle && !b.UsesAngle;
    }

    double TravelPerDegree => _travel.Index == 0 ? CouplingInfo.RackRatio(_length.Value) : CouplingInfo.LeadRatio(_length.Value);

    public CouplingInfo? Plan()
    {
        if (Leader is not { } a || Follower is not { } b || _joints.Count < 2) return null;
        if (a.Key == b.Key) throw new FormatException("Pick two different joints.");
        if (S.Couplings.Any(c => c.FollowerKey == b.Key)) throw new FormatException($"{b.Name} already follows another joint.");
        if (S.Couplings.Any(c => c.FollowerKey == a.Key && c.LeaderKey == b.Key)) throw new FormatException("These two joints are already coupled the other way round.");
        var (kind, ratio) = (a.UsesAngle, b.UsesAngle) switch
        {
            (true, true) => (CouplingKind.Gear, CouplingInfo.GearRatio(_sizeA.Value, _sizeB.Value)),
            (true, false) => (CouplingKind.RackPinion, TravelPerDegree),
            (false, true) => (CouplingKind.RackPinion, 1 / TravelPerDegree),
            _ => (CouplingKind.Ratio, _ratio.Value),
        };
        return new CouplingInfo(CadSession.NewKey(), $"Coupling {S.Couplings.Count() + 1}", kind, a.Key, b.Key, ratio, _direction.Index == 1);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var info = Plan();
        if (info == null) { Results.Add(new ResultLine("Joints", "this needs two turning or sliding joints. Create them first.", 2)); return null; }
        var a = Leader!; var b = Follower!;
        Results.Add(new ResultLine("Between", $"{a.Name}  →  {b.Name}"));
        if (a.UsesAngle && b.UsesAngle)
            Results.Add(new ResultLine("Ratio", $"{_sizeA.Value:0.###} : {_sizeB.Value:0.###}  =  {_sizeB.Value / _sizeA.Value:0.###} : 1 {(_sizeB.Value > _sizeA.Value ? "(reduction)" : _sizeB.Value < _sizeA.Value ? "(speed up)" : "")}"));
        else if (a.UsesAngle != b.UsesAngle) Results.Add(new ResultLine("Travel", $"{TravelPerDegree * 360:0.###} mm per turn"));
        else Results.Add(new ResultLine("Ratio", $"{_ratio.Value:0.###} mm for each mm"));
        Results.Add(new ResultLine("Direction", info.Reverse ? "the following joint moves the opposite way" : "the following joint moves the same way"));
        string? fit = Fit(a, b);
        if (fit != null) Results.Add(new ResultLine("Check", fit, 2));
        var o = new CommandOutput { Label = info.Name, Icon = Icon };
        var frame = S.ComponentByKey(a.ParentKey)?.Component is { } pc ? (pc.Placement * a.A).T : Vec3.Zero;
        o.Add.Add(new(Occ.Line(frame, frame + new Vec3(0, 0, 0.01)), "Coupling", ObjKind.Joint, Asm: info));
        o.After = s => s.SolveAssembly();
        return o;
    }

    /// <summary>For two turning joints: a note when the shafts are not parallel, or how their spacing compares with the sizes.</summary>
    string? Fit(JointInfo a, JointInfo b)
    {
        if (!a.UsesAngle || !b.UsesAngle) return null;
        var pa = S.ComponentByKey(a.ParentKey)?.Component; var pb = S.ComponentByKey(b.ParentKey)?.Component;
        if (pa == null || pb == null) return null;
        var fa = pa.Placement * a.A; var fb = pb.Placement * b.A;
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
