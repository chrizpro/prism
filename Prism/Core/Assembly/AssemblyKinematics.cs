using System.Collections.Generic;
using System.Linq;

namespace Prism.Core.Assembly;

/// <summary>The joint solver on its own, away from the document: it positions every component outwards from the grounded ones.</summary>
public static class AssemblyKinematics
{
    /// <summary>
    /// Placement of every component for these joints. <paramref name="start"/> holds where each component sits now (kept for parts no joint reaches),
    /// <paramref name="grounded"/> the keys of the fixed components. Couplings (gears, rack and pinion) set their follower joints first.
    /// </summary>
    public static Dictionary<string, Xform> Solve(IReadOnlyDictionary<string, Xform> start, ISet<string> grounded, IEnumerable<JointInfo> joints,
        IEnumerable<CouplingInfo>? couplings, List<string> log)
    {
        var placement = start.ToDictionary(kv => kv.Key, kv => kv.Value);
        var jl = CouplingSolver.Apply(joints, couplings ?? [], log).Where(j => placement.ContainsKey(j.ParentKey) && placement.ContainsKey(j.ChildKey)).ToList();
        var placed = new HashSet<string>(grounded.Where(placement.ContainsKey));
        var used = new HashSet<string>();

        bool Step()
        {
            foreach (var j in jl)
            {
                if (used.Contains(j.Key)) continue;
                bool p = placed.Contains(j.ParentKey), c = placed.Contains(j.ChildKey);
                if (p && c) { used.Add(j.Key); log.Add($"{j.Name}: both parts are already positioned by other joints (a closed loop), so this joint is not enforced."); continue; }
                if (!p && !c) continue;
                var jm = j.Motion();
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
        return placement;
    }
}
