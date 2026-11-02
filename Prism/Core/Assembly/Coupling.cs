using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core.Assembly;

public enum CouplingKind { Gear, RackPinion }

/// <summary>
/// Ties the motion of one joint to another: a gear pair (two revolute joints) or a rack and pinion (a revolute joint and a slider).
/// The leader moves freely (or is driven); the follower is set from it: follower = Factor × leader, in degrees or millimetres.
/// </summary>
public sealed record CouplingInfo(string Key, string Name, CouplingKind Kind, string LeaderKey, string FollowerKey, double Ratio, bool Reverse) : AssemblyData
{
    /// <summary>Follower value per unit of leader value: negative when the two turn in opposite directions (meshing external gears).</summary>
    public double Factor => Reverse ? -Ratio : Ratio;
    public double Follow(double leaderValue) => Factor * leaderValue;

    /// <summary>Gear ratio from the size of each wheel (tooth counts or pitch diameters): the follower turns leader/follower times as far.</summary>
    public static double GearRatio(double leaderSize, double followerSize) => leaderSize / followerSize;
    /// <summary>Rack travel (mm) per degree of pinion turn for a pinion of this pitch diameter.</summary>
    public static double RackRatio(double pitchDiameter) => Math.PI * pitchDiameter / 360.0;
}

public static class CouplingSolver
{
    /// <summary>The joints with every coupled follower set from its leader (chains of couplings are followed). A coupling that closes a loop is left out and noted.</summary>
    public static List<JointInfo> Apply(IEnumerable<JointInfo> joints, IEnumerable<CouplingInfo> couplings, List<string>? notes = null)
    {
        var list = joints.ToList();
        var cs = couplings.ToList();
        if (cs.Count == 0) return list;
        var byKey = new Dictionary<string, int>();
        for (int i = 0; i < list.Count; i++) byKey[list[i].Key] = i;
        cs = cs.Where(c => byKey.ContainsKey(c.LeaderKey) && byKey.ContainsKey(c.FollowerKey) && c.LeaderKey != c.FollowerKey).ToList();
        // a follower with two leaders would be over-constrained: the first coupling wins
        var seen = new HashSet<string>();
        foreach (var c in cs.ToList())
            if (!seen.Add(c.FollowerKey)) { cs.Remove(c); notes?.Add($"{c.Name}: its second joint already follows another coupling, so this one is not enforced."); }
        // order so that a leader is always set before the joints that follow it
        var done = new HashSet<string>(); var ordered = new List<CouplingInfo>();
        var follows = cs.Select(c => c.FollowerKey).ToHashSet();
        for (int pass = 0; pass <= cs.Count && ordered.Count < cs.Count; pass++)
            foreach (var c in cs)
                if (!done.Contains(c.Key) && (!follows.Contains(c.LeaderKey) || ordered.Any(o => o.FollowerKey == c.LeaderKey)))
                { ordered.Add(c); done.Add(c.Key); }
        foreach (var c in cs.Where(c => !done.Contains(c.Key))) notes?.Add($"{c.Name}: the couplings form a loop, so this one is not enforced.");
        foreach (var c in ordered)
        {
            var leader = list[byKey[c.LeaderKey]];
            int f = byKey[c.FollowerKey];
            list[f] = list[f] with { Value = c.Follow(leader.Clamped().v1) };
        }
        return list;
    }

    /// <summary>The keys of the joints that follow a leader.</summary>
    public static HashSet<string> Followers(IEnumerable<CouplingInfo> couplings, IEnumerable<JointInfo> joints)
    {
        var keys = joints.Select(j => j.Key).ToHashSet();
        var res = new HashSet<string>();
        foreach (var c in couplings)
            if (keys.Contains(c.LeaderKey) && keys.Contains(c.FollowerKey) && c.LeaderKey != c.FollowerKey) res.Add(c.FollowerKey);
        return res;
    }
}
