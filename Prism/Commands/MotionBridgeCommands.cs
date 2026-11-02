using System;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;

namespace Prism.Commands;

/// <summary>
/// Turns what a joint held in a motion study into a load on a part for a Static study: pick a joint, a moment (its peak, or a time you choose) and the faces of the
/// part that take the force. The assembly is moved to that moment so the part sits as it did; a new Static study is made with the force on those faces.
/// </summary>
public sealed class MotionLoadToStaticCommand : AnalysisCommand
{
    MotionResult? _result;
    int[] _jointIndex = [];

    public override string Title => "Load from Motion";
    public override string Icon => "motion-force";
    public override string OkText => "Make the load";
    public override string Hint => "Take the force a joint held in the last Motion Dynamics study (a bearing, a piston pin) and apply it to the faces of a part in a new Static study, with the assembly moved to that moment. Pick the faces that take the load; then add supports where the part is held and run the study. The force alone is not balanced: the part needs supports (or the other joints' loads) to be in equilibrium.";

    ChoiceParam? _jointChoice, _whenChoice, _sideChoice; NumberParam? _timeParam; BoolParam? _moveParam; SelectionSlot? _faceSlot;

    protected override void OnBegin()
    {
        _result = S.LastMotion;
        var names = _result == null ? ["No motion study yet"] : Enumerable.Range(0, _result.ReactionJoints.Length)
            .Where(j => _result.ReactionForce[j].Any(f => f.Length > 1e-9)).Select(j => _result.ReactionJoints[j]).DefaultIfEmpty("No joint carries a force").ToArray();
        _jointIndex = _result == null ? [] : names.Select(n => Array.IndexOf(_result.ReactionJoints, n)).ToArray();
        _jointChoice = Choice("Joint", 0, names);
        _whenChoice = Choice("Moment", 0, "When the force is largest", "At a time I choose");
        _timeParam = Num("Time", 0, 0, 1e6, 0.1, "s");
        _sideChoice = Choice("Load on", 0, "The part the joint holds (its child)", "The part that holds it (its parent)");
        _faceSlot = Slot(new SelectionSlot { Label = "Faces the force acts on (on the part)", Filter = SelFilter.Face, Multi = true });
        _moveParam = Check("Move the assembly to that moment", true);
    }

    protected override void UpdateVisibility() { if (_timeParam != null) _timeParam.IsVisible = _whenChoice!.Index == 1; }

    (int joint, int sample, Vec3 force)? Plan()
    {
        if (_result == null || _jointIndex.Length == 0 || _jointIndex[0] < 0) return null;
        int j = _jointIndex[Math.Clamp(_jointChoice!.Index, 0, _jointIndex.Length - 1)]; if (j < 0) return null;
        var series = _result.ReactionForce[j].Select(f => f.Length).ToArray();
        int at = _whenChoice!.Index == 0 ? Array.IndexOf(series, series.Max()) : _result.SampleAt(_timeParam!.Value);
        var f = _result.ReactionForce[j][at];
        return (j, at, _sideChoice!.Index == 0 ? f : -f);
    }

    protected override void Analyze() { }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_result == null) { Row("Motion study", "Run Motion Dynamics first: its joint forces are what this turns into a load.", 2); return new CommandOutput { NoOp = true }; }
        var plan = Plan();
        if (plan == null) { Row("Joint", "No joint carried a force in the last study.", 2); return new CommandOutput { NoOp = true }; }
        var (j, at, force) = plan.Value;
        Row("Joint", _result.ReactionJoints[j]);
        Row("At", $"{_result.Time[at]:0.###} s");
        Row("Force", $"{force.Length:G4} N   (x {force.X:G3}, y {force.Y:G3}, z {force.Z:G3})");
        if (_faceSlot!.Items.Count == 0) { Row("Faces", "Pick the faces of the part that take this force.", 2); return new CommandOutput { NoOp = true }; }
        var bodies = _faceSlot.Items.Select(i => i.Obj).Distinct().ToList();
        if (bodies.Count != 1) throw new InvalidOperationException("Pick faces from one part.");
        var body = S.Find(bodies[0]) ?? throw new InvalidOperationException("That part no longer exists.");
        if (body.Kind != ObjKind.Solid) throw new InvalidOperationException("The load goes on a solid part.");
        var faces = _faceSlot.Items.Select(i => i.Index).Distinct().ToArray();
        var pose = _result.PoseAt(at); string jointName = _result.ReactionJoints[j]; double time = _result.Time[at]; bool move = _moveParam!.Value;
        var o = new CommandOutput { Label = "Load from motion", Icon = Icon };
        o.After = s =>
        {
            if (move) s.SetJointValues(pose.ToDictionary(kv => kv.Key, kv => kv.Value), false);
            var target = s.Find(body.Id)!;
            var study = s.NewStudy(target);
            study.Name = $"{jointName} at {time:0.###} s";
            study.Loads.Add(new Simulation.Load { Name = jointName + " force", Kind = Simulation.LoadKind.Force, Faces = [.. faces.Select(f => new FaceRef(target.Id, f))], Direction = force.Normalized(), Value = force.Length, Total = true });
            s.RaiseStudy();
            s.Say($"Made the Static study “{study.Name}” with {force.Length:G4} N on {faces.Length} face(s). Add supports, then run it.");
        };
        return o;
    }
}
