using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core.Assembly;

/// <summary>The joint solver on its own, away from the document: it positions every component outwards from the grounded ones.</summary>
public static class AssemblyKinematics
{
    internal static bool IsClosed(IReadOnlyDictionary<string, Xform> placement, JointInfo j)
    {
        var wanted = placement[j.ParentKey] * j.A * j.Motion();
        var actual = placement[j.ChildKey] * j.B;
        if (wanted.T.DistanceTo(actual.T) > 1e-6) return false;
        for (int i = 0; i < wanted.R.Length; i++)
            if (Math.Abs(wanted.R[i] - actual.R[i]) > 1e-8) return false;
        return true;
    }

    /// <summary>How far a loop-closing joint is from being satisfied: translation (mm) then rotation (radians) of where the child is against where the joint says it should be.</summary>
    public static double[] ClosureError(IReadOnlyDictionary<string, Xform> placement, JointInfo j, IReadOnlyDictionary<string, Xform>? extra = null)
    {
        var jm = j.Motion();
        if (extra != null && extra.TryGetValue(j.Key, out var small)) jm = small * jm;
        var want = placement[j.ParentKey] * j.A * jm * j.B.Inverse();
        var e = want.Inverse() * placement[j.ChildKey];
        var r = e.R;
        return [e.T.X, e.T.Y, e.T.Z, (r[7] - r[5]) / 2, (r[2] - r[6]) / 2, (r[3] - r[1]) / 2];
    }

    /// <summary>
    /// Placement of every component for these joints. <paramref name="start"/> holds where each component sits now (kept for parts no joint reaches),
    /// <paramref name="grounded"/> the keys of the fixed components. Couplings (gears, rack and pinion) set their follower joints first.
    /// </summary>
    public static Dictionary<string, Xform> Solve(IReadOnlyDictionary<string, Xform> start, ISet<string> grounded, IEnumerable<JointInfo> joints,
        IEnumerable<CouplingInfo>? couplings, List<string> log, IReadOnlyDictionary<string, Xform>? extra = null, List<JointInfo>? closing = null)
    {
        var placement = start.ToDictionary(kv => kv.Key, kv => kv.Value);
        var jl = CouplingSolver.Apply(joints, couplings ?? [], log).Where(j => placement.ContainsKey(j.ParentKey) && placement.ContainsKey(j.ChildKey)).ToList();
        var placed = new HashSet<string>(grounded.Where(placement.ContainsKey));
        var used = new HashSet<string>();
        var loopJoints = new List<JointInfo>();

        bool Step()
        {
            foreach (var j in jl)
            {
                if (used.Contains(j.Key)) continue;
                bool p = placed.Contains(j.ParentKey), c = placed.Contains(j.ChildKey);
                if (p && c) { used.Add(j.Key); loopJoints.Add(j); continue; }
                if (!p && !c) continue;
                var jm = j.Motion();
                if (extra != null && extra.TryGetValue(j.Key, out var small)) jm = small * jm;          // a tiny extra relative motion in the joint's parent frame
                if (p) placement[j.ChildKey] = placement[j.ParentKey] * j.A * jm * j.B.Inverse();
                else placement[j.ParentKey] = placement[j.ChildKey] * j.B * jm.Inverse() * j.A.Inverse();
                placed.Add(p ? j.ChildKey : j.ParentKey); used.Add(j.Key);
                return true;
            }
            return false;
        }
        while (true)
        {
            while (Step()) { }
            // a group with nothing grounded: hold its first part where it is
            var seed = jl.FirstOrDefault(j => !used.Contains(j.Key));
            if (seed == null) break;
            placed.Add(seed.ParentKey);
        }
        if (closing != null) closing.AddRange(loopJoints);
        else if (extra == null)
            foreach (var j in loopJoints.Where(j => !IsClosed(placement, j)))
                log.Add($"{j.Name}: the linkage does not close at this position.");
        return placement;
    }
}
