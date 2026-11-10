using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Simulation;

/// <summary>How a joint moves in a study. Ramp is a smooth move to a position; Table follows planned (time, value) samples such as the joints of a robot following a path.</summary>
public enum DriveMode { Hold, Constant, Oscillate, Free, Ramp, Table }

/// <summary>One motion value of a joint (its angle, or its travel) and what is done to it in a study.</summary>
public sealed class MotionDof
{
    public required string JointKey { get; init; }
    public required string JointName { get; init; }
    public required string Name { get; init; }
    public bool Second { get; init; }
    public bool Angular { get; init; }
    public bool Limited { get; init; }
    /// <summary>Limits in degrees or mm.</summary>
    public double Lo { get; init; }
    public double Hi { get; init; }
    /// <summary>Set from another joint by a gear or rack coupling: it has no motion of its own.</summary>
    public bool Follower { get; init; }
    /// <summary>Fixed by a closed loop (a crank, rod and piston): its value follows from the joints that are free to move. Set by the engine.</summary>
    public bool Dependent { get; set; }
    /// <summary>Where the joint starts (degrees or mm).</summary>
    public double Start { get; set; }
    public DriveMode Mode { get; set; }
    /// <summary>Constant: speed in °/s or mm/s. Free: the speed it starts with.</summary>
    public double Rate { get; set; }
    /// <summary>Oscillate: amplitude (° or mm) and frequency (Hz).</summary>
    public double Amplitude { get; set; }
    public double Frequency { get; set; }
    /// <summary>A constant torque (N·m) or force (N) acting on the joint, positive along its positive direction. Not used where the joint is driven.</summary>
    public double Effort { get; set; }
    /// <summary>Viscous friction in N·m·s/rad or N·s/m.</summary>
    public double Damping { get; set; }
    /// <summary>Ramp: the position to move to (° or mm), when the move starts and how long it takes (s).</summary>
    public double Target { get; set; }
    public double RampStart { get; set; }
    public double RampTime { get; set; } = 1;
    /// <summary>A motor on a free joint: stall torque or force (N·m or N) and the speed (°/s or mm/s) it reaches with no load. The push falls linearly from stall to zero.</summary>
    public double MotorStall { get; set; }
    public double MotorNoLoad { get; set; }
    /// <summary>Table: planned values (° or mm) at the given times.</summary>
    public CubicSpline? Table { get; private set; }
    public void SetTable(double[] times, double[] values) { Table = new CubicSpline(times, values); Mode = DriveMode.Table; }
    /// <summary>Internal units (radians or metres) per displayed unit (degrees or mm).</summary>
    public double Scale => Angular ? Math.PI / 180 : 1e-3;
    public string Unit => Angular ? "°" : "mm";
    public string EffortUnit => Angular ? "N·m" : "N";
}

public sealed record MotionSettings(double Duration, double Interval, bool Dynamic, Vec3 Gravity, double MaxStep = 0.002, bool Reactions = true);

/// <summary>Everything a study produced, per output time. Positions are degrees or mm; speeds and accelerations of parts are in m/s and m/s².</summary>
public sealed class MotionResult
{
    public required double[] Time { get; init; }
    public required MotionDof[] Dofs { get; init; }
    public required double[][] Pos { get; init; }
    public required double[][] Vel { get; init; }
    public required double[][] Acc { get; init; }
    /// <summary>The torque or force a driven joint needs at each time (dynamic studies; zeros elsewhere).</summary>
    public required double[][] Effort { get; init; }
    public required double[][] Power { get; init; }
    public required double[] Kinetic { get; init; }
    public required double[] Potential { get; init; }
    public required MotionBody[] Bodies { get; init; }
    /// <summary>Where each part's centre of mass is (mm, world) at each time.</summary>
    public required Vec3[][] Path { get; init; }
    public required double[][] Speed { get; init; }
    public required double[][] Accel { get; init; }
    public required double[][] AngSpeed { get; init; }
    public List<string> Events { get; } = [];
    public bool Dynamic { get; init; }
    /// <summary>Force elements (springs, actuators, loads) and the force or torque each one carried at every output time (N or N·m; a spring is positive when it pulls its ends together).</summary>
    public string[] ForceNames { get; init; } = [];
    public double[][] ForceValue { get; init; } = [];
    /// <summary>What each joint holds: the force (N) and moment (N·m, about the joint's origin) its parent exerts on its child in the directions the joint does not allow to move, in world axes, per output time.</summary>
    public string[] ReactionJoints { get; init; } = [];
    public Vec3[][] ReactionForce { get; init; } = [];
    public Vec3[][] ReactionMoment { get; init; } = [];

    public int Samples => Time.Length;
    /// <summary>Joint values at one output time, keyed by joint (first and second value, in degrees or mm), for showing the pose.</summary>
    public Dictionary<string, (double v1, double v2)> PoseAt(int sample)
    {
        var res = new Dictionary<string, (double v1, double v2)>();
        for (int d = 0; d < Dofs.Length; d++)
        {
            var dof = Dofs[d]; var cur = res.GetValueOrDefault(dof.JointKey);
            res[dof.JointKey] = dof.Second ? (cur.v1, Pos[d][sample]) : (Pos[d][sample], cur.v2);
        }
        return res;
    }
    public int SampleAt(double t) => Math.Clamp((int)Math.Round(t / Math.Max(Time.Length > 1 ? Time[1] - Time[0] : 1, 1e-12)), 0, Samples - 1);
}

/// <summary>
/// Kinematic and rigid-body motion of an assembly. Joint values are the coordinates; the pose of every part comes from the assembly's own joint solver,
/// and its derivatives (Jacobians, accelerations along a path) are taken numerically, so every joint type and coupling works without separate formulas.
/// </summary>
public sealed class MotionEngine
{
    readonly IReadOnlyDictionary<string, Xform> _start;
    readonly ISet<string> _grounded;
    readonly List<JointInfo> _joints;
    readonly MotionBody[] _bodies;
    readonly MotionDof[] _dofs;
    readonly int[] _mov;                 // dofs that are not followers
    int[] _ind = [];                     // the coordinates of the study: movable dofs not fixed by a loop
    int[] _dep = [];                     // dofs a closed loop fixes
    List<string> _closing = [];          // joints that close loops
    double[] _u = [];                    // last solved values of the dependent dofs (internal units): the next solve starts here, so a linkage stays on one branch
    readonly Dictionary<string, int> _jointIndex = [];
    readonly List<CouplingInfo> _couplings;
    readonly List<(MotionForce force, int a, int b)> _forces = [];

    public MotionEngine(IReadOnlyDictionary<string, Xform> start, ISet<string> grounded, IEnumerable<JointInfo> joints, IEnumerable<CouplingInfo> couplings, IEnumerable<MotionBody> bodies, IEnumerable<MotionDof> dofs,
        IEnumerable<MotionForce>? forces = null, IEnumerable<MotionPoint>? points = null)
    {
        _start = start; _grounded = grounded;
        // limits are handled here (a driven value holds at its limit, a free one stops there), so the pose itself must stay smooth past them
        _joints = joints.Select(j => j with { HasLimits = false }).ToList(); _couplings = couplings.ToList();
        // the parts, then marked points (payloads and tool points), then massless probes where forces act: all are tracked the same way
        var list = bodies.Where(b => start.ContainsKey(b.Key)).ToList();
        foreach (var p in points ?? []) if (start.ContainsKey(p.BodyKey)) list.Add(new MotionBody(p.BodyKey, p.Name, p.Mass, p.Local, new double[9]));
        int Probe(string? body, Vec3 local)
        {
            if (body == null || !start.ContainsKey(body)) return -1;
            list.Add(new MotionBody(body, "@" + body, 0, local, new double[9])); return list.Count - 1;
        }
        foreach (var f in forces ?? [])
            switch (f)
            {
                case SpringForce sp: _forces.Add((f, Probe(sp.BodyA, sp.PointA), Probe(sp.BodyB, sp.PointB))); break;
                case PointForce pf: _forces.Add((f, Probe(pf.Body, pf.Point), -1)); break;
                case BodyTorque bt: _forces.Add((f, Probe(bt.Body, Vec3.Zero), -1)); break;
            }
        _bodies = list.ToArray();
        _dofs = dofs.ToArray();
        for (int i = 0; i < _joints.Count; i++) _jointIndex[_joints[i].Key] = i;
        _mov = Enumerable.Range(0, _dofs.Length).Where(i => !_dofs[i].Follower).ToArray();
        ChooseCoordinates();
    }

    /// <summary>One entry per motion value of every joint that can move, set to start where the joint is now and to be held there.</summary>
    public static List<MotionDof> DofsOf(IEnumerable<JointInfo> joints, IEnumerable<CouplingInfo> couplings)
    {
        var js = joints.ToList(); var followers = CouplingSolver.Followers(couplings, js);
        var res = new List<MotionDof>();
        foreach (var j in js)
        {
            if (j.Type == JointType.Rigid) continue;
            var (c1, c2) = j.Clamped();
            res.Add(new MotionDof { JointKey = j.Key, JointName = j.Name, Name = j.HasSecond ? $"{j.Name} ({j.FirstLabel})" : j.Name, Angular = j.UsesAngle, Limited = j.HasLimits, Lo = j.Min, Hi = j.Max, Follower = followers.Contains(j.Key), Start = c1 });
            if (j.HasSecond)
                res.Add(new MotionDof { JointKey = j.Key, JointName = j.Name, Name = $"{j.Name} ({j.SecondLabel})", Second = true, Angular = j.SecondIsAngle, Limited = j.HasLimits, Lo = j.Min2, Hi = j.Max2, Start = c2 });
        }
        return res;
    }

    // ------------------------------------------------------------------ poses
    List<JointInfo> JointsWith(IReadOnlyList<int> dofIdx, IReadOnlyList<double> values)
    {
        var list = new List<JointInfo>(_joints);
        for (int k = 0; k < dofIdx.Count; k++)
        {
            var d = _dofs[dofIdx[k]]; int ji = _jointIndex[d.JointKey]; double display = values[k] / d.Scale;
            list[ji] = d.Second ? list[ji] with { Value2 = display } : list[ji] with { Value = display };
        }
        return CouplingSolver.Apply(list, _couplings);
    }

    List<JointInfo> JointsFor(double[] q, double[] u) => _dep.Length == 0 ? JointsWith(_ind, q) : JointsWith([.. _ind, .. _dep], [.. q, .. u]);

    Dictionary<string, Xform> Tree(List<JointInfo> js, IReadOnlyDictionary<string, Xform>? extra) => AssemblyKinematics.Solve(_start, _grounded, js, null, [], extra);

    const double RotationWeight = 100;          // mm per radian when a loop's position and angle errors are added
    const string CannotClose = "The linkage cannot close at this position. Check the joint picks, link lengths and limits, or try a smaller move.";

    double[] Phi(List<JointInfo> js, Dictionary<string, Xform> pl, IReadOnlyDictionary<string, Xform>? extra)
    {
        var res = new double[6 * _closing.Count];
        for (int c = 0; c < _closing.Count; c++)
        {
            var e = AssemblyKinematics.ClosureError(pl, js[_jointIndex[_closing[c]]], extra);
            for (int k = 0; k < 6; k++) res[6 * c + k] = e[k] * (k < 3 ? 1 : RotationWeight);
        }
        return res;
    }

    static double Norm(double[] v) { double s = 0; foreach (var x in v) s += x * x; return Math.Sqrt(s); }

    /// <summary>The joint values and part placements for these coordinates, with the loops closed (the dependent joints solved by damped Newton from where they last were).</summary>
    (List<JointInfo> Joints, Dictionary<string, Xform> Placement) Solved(double[] q, IReadOnlyDictionary<string, Xform>? extra = null)
    {
        if (_dep.Length == 0)
        {
            var j0 = JointsFor(q, []);
            var p0 = Tree(j0, extra);
            if (extra == null && _closing.Any(key => !AssemblyKinematics.IsClosed(p0, j0[_jointIndex[key]])))
                throw new InvalidOperationException(CannotClose);
            return (j0, p0);
        }
        int nd = _dep.Length; var u = (double[])_u.Clone();
        var sc = _dep.Select(i => _dofs[i].Angular ? 1.0 : 0.3).ToArray();
        var js = JointsFor(q, u); var pl = Tree(js, extra); var r = Phi(js, pl, extra); double err = Norm(r), lambda = 1e-6;
        for (int it = 0; it < 100 && err > 1e-10; it++)
        {
            var J = new double[r.Length, nd]; const double h = 1e-6;
            for (int c = 0; c < nd; c++)
            {
                var up = (double[])u.Clone(); var um = (double[])u.Clone(); up[c] += h * sc[c]; um[c] -= h * sc[c];
                var jp = JointsFor(q, up); var rp = Phi(jp, Tree(jp, extra), extra); var jm = JointsFor(q, um); var rm = Phi(jm, Tree(jm, extra), extra);
                for (int k = 0; k < r.Length; k++) J[k, c] = (rp[k] - rm[k]) / (2 * h);
            }
            bool accepted = false;
            for (int attempt = 0; attempt < 12 && !accepted; attempt++)
            {
                var A = new double[nd, nd]; var rhs = new double[nd];
                for (int a = 0; a < nd; a++)
                {
                    for (int c = 0; c < nd; c++) { double sum = 0; for (int k = 0; k < r.Length; k++) sum += J[k, a] * J[k, c]; A[a, c] = sum + (a == c ? lambda * Math.Max(sum, 1e-12) : 0); }
                    double g = 0; for (int k = 0; k < r.Length; k++) g += J[k, a] * r[k]; rhs[a] = -g;
                }
                var step = Solve(A, rhs); if (step == null) { lambda *= 10; continue; }
                var un = (double[])u.Clone(); for (int c = 0; c < nd; c++) un[c] += step[c] * sc[c];
                var jn = JointsFor(q, un); var pn = Tree(jn, extra); var rn = Phi(jn, pn, extra); double en = Norm(rn);
                if (en < err) { accepted = true; u = un; js = jn; pl = pn; r = rn; err = en; lambda = Math.Max(lambda / 10, 1e-12); } else lambda *= 10;
            }
            if (!accepted) break;
        }
        // releasing a locked direction (to measure a joint's reaction) may leave a loop with no exact solution; the best fit is what the measurement needs
        if (extra == null && (err > 1e-6 || _closing.Any(key => !AssemblyKinematics.IsClosed(pl, js[_jointIndex[key]]))))
            throw new InvalidOperationException(CannotClose);
        _u = u; return (js, pl);
    }

    Dictionary<string, Xform> Pose(double[] q, IReadOnlyDictionary<string, Xform>? extra = null) => Solved(q, extra).Placement;

    /// <summary>Every motion value (internal units) for these independent values, loops closed.</summary>
    double[] Full(double[] q, IReadOnlyDictionary<string, Xform>? extra = null) => FullOf(Solved(q, extra).Joints);

    double[] FullOf(List<JointInfo> js)
    {
        var res = new double[_dofs.Length];
        for (int i = 0; i < _dofs.Length; i++)
        {
            var d = _dofs[i]; var j = js[_jointIndex[d.JointKey]];
            res[i] = (d.Second ? j.Value2 : j.Value) * d.Scale;
        }
        return res;
    }

    static bool IsDriven(MotionDof d) => d.Mode is DriveMode.Constant or DriveMode.Oscillate or DriveMode.Ramp or DriveMode.Table;

    /// <summary>
    /// Find the loops (joints that join two parts the other joints already placed) and decide which joint values are the coordinates of the study.
    /// A loop removes as many joint values as it has independent constraints; the ones removed are chosen so that driven joints stay free.
    /// </summary>
    void ChooseCoordinates()
    {
        var joints = CouplingSolver.Apply(_joints, _couplings); var closing = new List<JointInfo>();
        AssemblyKinematics.Solve(_start, _grounded, joints, null, [], null, closing);
        _closing = closing.Select(j => j.Key).Distinct().ToList();
        if (_closing.Count == 0) { _ind = _mov; return; }
        int nm = _mov.Length; var qm = _mov.Select(i => _dofs[i].Start * _dofs[i].Scale).ToArray();
        double[] PhiAt(double[] v) { var js = JointsWith(_mov, v); return Phi(js, Tree(js, null), null); }
        var sc = _mov.Select(i => _dofs[i].Angular ? 1.0 : 0.3).ToArray();
        var cols = new double[nm][]; const double h = 1e-6;
        for (int c = 0; c < nm; c++)
        {
            var up = (double[])qm.Clone(); var um = (double[])qm.Clone(); up[c] += h * sc[c]; um[c] -= h * sc[c];
            var rp = PhiAt(up); var rm = PhiAt(um); cols[c] = rp.Zip(rm, (a, b) => (a - b) / (2 * h)).ToArray();
        }
        double maxNorm = cols.Select(Norm).DefaultIfEmpty(0).Max(); var basis = new List<double[]>(); var chosen = new List<int>();
        while (chosen.Count < nm)
        {
            int best = -1; double bestScore = 0; double[]? bestRes = null;
            for (int c = 0; c < nm; c++)
            {
                if (chosen.Contains(c)) continue;
                var res = (double[])cols[c].Clone();
                foreach (var b in basis) { double dot = 0; for (int k = 0; k < res.Length; k++) dot += b[k] * cols[c][k]; for (int k = 0; k < res.Length; k++) res[k] -= dot * b[k]; }
                double raw = Norm(res); if (raw < 1e-7 * Math.Max(maxNorm, 1e-12)) continue;
                double score = raw * (IsDriven(_dofs[_mov[c]]) ? 1e-3 : _dofs[_mov[c]].Mode == DriveMode.Free ? 1e-2 : 1);              // driven joints are the last to be given up, then free ones; held joints are the natural ones for a loop to fix
                if (score > bestScore) { bestScore = score; best = c; bestRes = res; }
            }
            if (best < 0) break;
            double nr = Norm(bestRes!); basis.Add(bestRes!.Select(x => x / nr).ToArray()); chosen.Add(best);
        }
        _dep = chosen.Select(c => _mov[c]).ToArray(); _ind = _mov.Where(i => !_dep.Contains(i)).ToArray();
        foreach (int i in _dep)
        {
            _dofs[i].Dependent = true;
            if (IsDriven(_dofs[i])) throw new InvalidOperationException($"{_dofs[i].Name} cannot be driven: the loop it belongs to already fixes it from the other joints. Drive one of the other joints of the loop instead.");
        }
        // start the dependent joints from the closed position that matches where the free ones start
        _u = _dep.Select(i => _dofs[i].Start * _dofs[i].Scale).ToArray();
        var start = Solved(_ind.Select(i => _dofs[i].Start * _dofs[i].Scale).ToArray());
        var full = FullOf(start.Joints);
        foreach (int i in _dep) _dofs[i].Start = full[i] / _dofs[i].Scale;
    }

    // ------------------------------------------------------------------ small linear algebra
    static Vec3 Vee(double[] a) => new((a[7] - a[5]) / 2, (a[2] - a[6]) / 2, (a[3] - a[1]) / 2);
    static double[] Mul(double[] a, double[] b)
    {
        var r = new double[9];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) r[3 * i + j] = a[3 * i] * b[j] + a[3 * i + 1] * b[3 + j] + a[3 * i + 2] * b[6 + j];
        return r;
    }
    static double[] Tr(double[] a) => [a[0], a[3], a[6], a[1], a[4], a[7], a[2], a[5], a[8]];
    static Vec3 Apply(double[] a, Vec3 v) => new(a[0] * v.X + a[1] * v.Y + a[2] * v.Z, a[3] * v.X + a[4] * v.Y + a[5] * v.Z, a[6] * v.X + a[7] * v.Y + a[8] * v.Z);

    /// <summary>Solve A x = b by Gaussian elimination with pivoting. Returns null when A is singular.</summary>
    static double[]? Solve(double[,] a, double[] b)
    {
        int n = b.Length; var m = (double[,])a.Clone(); var x = (double[])b.Clone();
        double scale = 0; for (int i = 0; i < n; i++) scale = Math.Max(scale, Math.Abs(m[i, i]));
        for (int c = 0; c < n; c++)
        {
            int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
            if (Math.Abs(m[p, c]) <= 1e-12 * Math.Max(scale, 1e-300)) return null;
            if (p != c) { for (int k = 0; k < n; k++) (m[c, k], m[p, k]) = (m[p, k], m[c, k]); (x[c], x[p]) = (x[p], x[c]); }
            for (int r = c + 1; r < n; r++)
            {
                double f = m[r, c] / m[c, c]; if (f == 0) continue;
                for (int k = c; k < n; k++) m[r, k] -= f * m[c, k];
                x[r] -= f * x[c];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            double s = x[r]; for (int k = r + 1; k < n; k++) s -= m[r, k] * x[k];
            x[r] = s / m[r, r];
        }
        return x;
    }

    // ------------------------------------------------------------------ kinematics of the bodies
    sealed class Kin
    {
        public Vec3[] Com = [];                 // world, mm
        public double[][] Rot = [];             // world rotation of each part
        public double[][] Jv = [], Jw = [];     // [body][3*n]: m (or rad) per unit of each independent coordinate
        public Vec3[] A0 = [], Alpha0 = [];     // acceleration of the centre and angular acceleration when the coordinates' accelerations are zero
        public Vec3[] V = [], W = [];
    }

    Vec3 ComOf(Dictionary<string, Xform> p, int b) => p[_bodies[b].Key].Apply(_bodies[b].Com);

    Kin Kinematics(double[] q, double[] qd)
    {
        int n = q.Length, nb = _bodies.Length;
        var kin = new Kin { Com = new Vec3[nb], Rot = new double[nb][], Jv = new double[nb][], Jw = new double[nb][], A0 = new Vec3[nb], Alpha0 = new Vec3[nb], V = new Vec3[nb], W = new Vec3[nb] };
        var p0 = Pose(q);
        for (int b = 0; b < nb; b++) { kin.Com[b] = ComOf(p0, b); kin.Rot[b] = p0[_bodies[b].Key].R; kin.Jv[b] = new double[3 * n]; kin.Jw[b] = new double[3 * n]; }
        const double eps = 1e-6;
        for (int k = 0; k < n; k++)
        {
            var qp = (double[])q.Clone(); var qm = (double[])q.Clone(); qp[k] += eps; qm[k] -= eps;
            var pp = Pose(qp); var pm = Pose(qm);
            for (int b = 0; b < nb; b++)
            {
                var dv = (ComOf(pp, b) - ComOf(pm, b)) * (1e-3 / (2 * eps));          // mm → m
                kin.Jv[b][k] = dv.X; kin.Jv[b][n + k] = dv.Y; kin.Jv[b][2 * n + k] = dv.Z;
                var rp = pp[_bodies[b].Key].R; var rm = pm[_bodies[b].Key].R; var diff = new double[9];
                for (int i = 0; i < 9; i++) diff[i] = (rp[i] - rm[i]) / (2 * eps);
                var w = Vee(Mul(diff, Tr(kin.Rot[b])));
                kin.Jw[b][k] = w.X; kin.Jw[b][n + k] = w.Y; kin.Jw[b][2 * n + k] = w.Z;
            }
        }
        for (int b = 0; b < nb; b++)
        {
            double vx = 0, vy = 0, vz = 0, wx = 0, wy = 0, wz = 0;
            for (int k = 0; k < n; k++) { vx += kin.Jv[b][k] * qd[k]; vy += kin.Jv[b][n + k] * qd[k]; vz += kin.Jv[b][2 * n + k] * qd[k]; wx += kin.Jw[b][k] * qd[k]; wy += kin.Jw[b][n + k] * qd[k]; wz += kin.Jw[b][2 * n + k] * qd[k]; }
            kin.V[b] = new Vec3(vx, vy, vz); kin.W[b] = new Vec3(wx, wy, wz);
        }
        // accelerations that come from the speeds alone: second differences along the straight line q + qd·t
        double reach = 0; for (int k = 0; k < n; k++) reach = Math.Max(reach, Math.Abs(qd[k]) * (_dofs[_ind[k]].Angular ? 1 : 50));
        if (reach > 1e-12)
        {
            double tau = 2e-3 / reach;
            var qa = new double[n]; var qb = new double[n];
            for (int k = 0; k < n; k++) { qa[k] = q[k] + tau * qd[k]; qb[k] = q[k] - tau * qd[k]; }
            var pa = Pose(qa); var pb = Pose(qb);
            for (int b = 0; b < nb; b++)
            {
                kin.A0[b] = (ComOf(pa, b) - kin.Com[b] * 2 + ComOf(pb, b)) * (1e-3 / (tau * tau));
                var ra = pa[_bodies[b].Key].R; var rb = pb[_bodies[b].Key].R; var rdd = new double[9];
                for (int i = 0; i < 9; i++) rdd[i] = (ra[i] - 2 * kin.Rot[b][i] + rb[i]) / (tau * tau);
                kin.Alpha0[b] = Vee(Mul(rdd, Tr(kin.Rot[b])));
            }
        }
        return kin;
    }

    /// <summary>Mass matrix and the force terms of the equations of motion (gravity, spinning and bending of the path) for these coordinates.</summary>
    (double[,] M, double[] H) Terms(Kin kin, int n, Vec3 gravity)
    {
        var M = new double[n, n]; var H = new double[n];
        for (int b = 0; b < _bodies.Length; b++)
        {
            var body = _bodies[b]; if (body.Mass <= 0) continue;
            var iw = Mul(Mul(kin.Rot[b], body.Inertia), Tr(kin.Rot[b]));
            var jv = kin.Jv[b]; var jw = kin.Jw[b];
            // I·J_w columns
            var ij = new double[3 * n];
            for (int k = 0; k < n; k++)
            {
                var col = Apply(iw, new Vec3(jw[k], jw[n + k], jw[2 * n + k]));
                ij[k] = col.X; ij[n + k] = col.Y; ij[2 * n + k] = col.Z;
            }
            var iwW = Apply(iw, kin.W[b]);
            var gyro = Vec3.Cross(kin.W[b], iwW) + Apply(iw, kin.Alpha0[b]);
            var lin = (kin.A0[b] - gravity) * body.Mass;
            for (int a = 0; a < n; a++)
            {
                for (int c = 0; c < n; c++)
                    M[a, c] += body.Mass * (jv[a] * jv[c] + jv[n + a] * jv[n + c] + jv[2 * n + a] * jv[2 * n + c])
                        + jw[a] * ij[c] + jw[n + a] * ij[n + c] + jw[2 * n + a] * ij[2 * n + c];
                H[a] += jv[a] * lin.X + jv[n + a] * lin.Y + jv[2 * n + a] * lin.Z + jw[a] * gyro.X + jw[n + a] * gyro.Y + jw[2 * n + a] * gyro.Z;
            }
        }
        return (M, H);
    }

    // ------------------------------------------------------------------ inverse kinematics
    public int IndependentCount => _ind.Length;
    public MotionDof IndependentDof(int k) => _dofs[_ind[k]];

    /// <summary>Where every independent joint starts (radians or metres).</summary>
    public double[] StartVector()
    {
        var q = new double[_ind.Length];
        for (int k = 0; k < q.Length; k++) q[k] = _dofs[_ind[k]].Start * _dofs[_ind[k]].Scale;
        return q;
    }

    /// <summary>Where a point of a part is (mm) and how the part is turned (a rotation, row-major) for these joint values.</summary>
    public (Vec3 Point, double[] Rotation) ToolPose(double[] q, string bodyKey, Vec3 local)
    {
        var p = Pose(q)[bodyKey];
        return (p.Apply(local), p.R);
    }

    public sealed record IkResult(double[] Q, double PositionError, double OrientationError, bool Converged);

    /// <summary>
    /// Joint values (radians or metres) that put a point of a part at a target (mm), and optionally keep the part turned a given way.
    /// Damped least squares from the start values, so a path followed point by point stays on one branch. Only the listed joints move.
    /// </summary>
    public IkResult InverseKinematics(double[] q0, IReadOnlyList<int> active, string bodyKey, Vec3 local, Vec3 target, double[]? holdRotation, double orientationWeight = 100, int maxIterations = 100)
    {
        var q = (double[])q0.Clone(); int na = active.Count, m = holdRotation == null ? 3 : 6;
        double[] Scale() { var sc = new double[na]; for (int i = 0; i < na; i++) sc[i] = _dofs[_ind[active[i]]].Angular ? 1 : 0.5; return sc; }
        var scale = Scale();
        double[] Residual(double[] qq)
        {
            var (pt, rot) = ToolPose(qq, bodyKey, local); var e = new double[m];
            e[0] = target.X - pt.X; e[1] = target.Y - pt.Y; e[2] = target.Z - pt.Z;
            if (holdRotation != null)
            {
                var w = Vee(Mul(holdRotation, Tr(rot)));
                e[3] = orientationWeight * w.X; e[4] = orientationWeight * w.Y; e[5] = orientationWeight * w.Z;
            }
            return e;
        }
        static double Norm(double[] v) { double s = 0; foreach (var x in v) s += x * x; return Math.Sqrt(s); }
        double[] Clamp(double[] qq)
        {
            var r = (double[])qq.Clone();
            foreach (int k in active) { var d = _dofs[_ind[k]]; if (d.Limited) r[k] = Math.Clamp(r[k], d.Lo * d.Scale, d.Hi * d.Scale); }
            return r;
        }
        double lambda = 2; var res = Residual(q); double err = Norm(res);
        for (int it = 0; it < maxIterations && err > 1e-7; it++)
        {
            var J = new double[m, na]; const double eps = 1e-6;
            for (int c = 0; c < na; c++)
            {
                var qp = (double[])q.Clone(); var qm = (double[])q.Clone(); qp[active[c]] += eps; qm[active[c]] -= eps;
                var ep = Residual(qp); var em = Residual(qm);
                for (int r = 0; r < m; r++) J[r, c] = (em[r] - ep[r]) / (2 * eps) * scale[c];       // d(position)/d(joint): the residual is target − position
            }
            bool accepted = false;
            for (int attempt = 0; attempt < 8 && !accepted; attempt++)
            {
                var A = new double[m, m];
                for (int r = 0; r < m; r++)
                    for (int c = 0; c < m; c++)
                    {
                        double sum = r == c ? lambda * lambda : 0; for (int k = 0; k < na; k++) sum += J[r, k] * J[c, k]; A[r, c] = sum;
                    }
                var y = Solve(A, res); if (y == null) { lambda *= 4; continue; }
                var next = (double[])q.Clone();
                for (int k = 0; k < na; k++) { double du = 0; for (int r = 0; r < m; r++) du += J[r, k] * y[r]; next[active[k]] += du * scale[k]; }
                next = Clamp(next); var rn = Residual(next); double en = Norm(rn);
                if (en < err) { accepted = true; bool tiny = err - en < 1e-12; q = next; res = rn; err = en; lambda = Math.Max(lambda / 3, 1e-4); if (tiny) it = maxIterations; }
                else lambda *= 4;
            }
            if (!accepted) break;
        }
        double pos = Math.Sqrt(res[0] * res[0] + res[1] * res[1] + res[2] * res[2]);
        double rot = m == 6 ? Math.Sqrt(res[3] * res[3] + res[4] * res[4] + res[5] * res[5]) / orientationWeight : 0;
        return new IkResult(q, pos, rot, pos < 1e-2);
    }

    // ------------------------------------------------------------------ prescribed motion
    (double q, double v, double a) Profile(MotionDof d, double t)
    {
        double s = d.Scale, q0 = d.Start * s;
        switch (d.Mode)
        {
            case DriveMode.Constant: return (q0 + d.Rate * s * t, d.Rate * s, 0);
            case DriveMode.Ramp:
            {
                double T = Math.Max(d.RampTime, 1e-9), u = (t - d.RampStart) / T, span = (d.Target - d.Start) * s;
                if (u <= 0) return (q0, 0, 0);
                if (u >= 1) return (q0 + span, 0, 0);
                double u2 = u * u, u3 = u2 * u;             // minimum-jerk blend: zero speed and acceleration at both ends
                return (q0 + span * (10 * u3 - 15 * u3 * u + 6 * u3 * u2), span * (30 * u2 - 60 * u3 + 30 * u3 * u) / T, span * (60 * u - 180 * u2 + 120 * u3) / (T * T));
            }
            case DriveMode.Table when d.Table is { } tab:
            {
                var (q, v, a) = tab.At(t); return (q * s, v * s, a * s);
            }
            case DriveMode.Oscillate:
                double w = 2 * Math.PI * d.Frequency, A = d.Amplitude * s;
                return (q0 + A * Math.Sin(w * t), A * w * Math.Cos(w * t), -A * w * w * Math.Sin(w * t));
            default: return (q0, 0, 0);
        }
    }

    /// <summary>Add the generalized force of every force element to Q; returns the force each one carries now.</summary>
    double[] Forces(Kin kin, double t, int n, double[] Q, Vec3[] fext, Vec3[] text)
    {
        var mags = new double[_forces.Count];
        void Push(int body, Vec3 f) { if (body >= 0) fext[body] += f; if (body >= 0) for (int k = 0; k < n; k++) Q[k] += kin.Jv[body][k] * f.X + kin.Jv[body][n + k] * f.Y + kin.Jv[body][2 * n + k] * f.Z; }
        for (int i = 0; i < _forces.Count; i++)
        {
            var (force, a, b) = _forces[i];
            switch (force)
            {
                case SpringForce sp when a >= 0:
                {
                    var pa = kin.Com[a]; var pb = b >= 0 ? kin.Com[b] : sp.PointB;
                    var d = pb - pa; double len = d.Length; if (len < 1e-9) break;
                    var u = d / len;
                    var rel = (b >= 0 ? kin.V[b] : Vec3.Zero) - kin.V[a];              // m/s
                    double lenRate = Vec3.Dot(u, rel) * 1000;                          // mm/s
                    double pull = sp.Stiffness * (len - sp.RestLength) + sp.Damping * lenRate - sp.Push;
                    Push(a, u * pull); if (b >= 0) Push(b, u * -pull);
                    mags[i] = pull; break;
                }
                case PointForce pf when a >= 0:
                {
                    var dir = pf.BodyFixed ? new Xform(kin.Rot[a], Vec3.Zero).Rotate(pf.Direction) : pf.Direction;
                    double mag = pf.Magnitude * pf.Profile.Factor(t);
                    Push(a, dir.Normalized() * mag); mags[i] = mag; break;
                }
                case BodyTorque bt when a >= 0:
                {
                    var axis = bt.BodyFixed ? new Xform(kin.Rot[a], Vec3.Zero).Rotate(bt.Axis) : bt.Axis;
                    double mag = bt.Magnitude * bt.Profile.Factor(t); var tq = axis.Normalized() * mag; text[a] += tq;
                    for (int k = 0; k < n; k++) Q[k] += kin.Jw[a][k] * tq.X + kin.Jw[a][n + k] * tq.Y + kin.Jw[a][2 * n + k] * tq.Z;
                    mags[i] = mag; break;
                }
            }
        }
        return mags;
    }

    // ------------------------------------------------------------------ joint reactions
    static double[] SmallTurn(int axis, double angle)
    {
        double c = Math.Cos(angle), sn = Math.Sin(angle); var k = new double[3]; k[axis] = 1;
        var r = new double[9];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) r[3 * i + j] = (i == j ? c : 0) + (1 - c) * k[i] * k[j];
        r[1] -= sn * k[2]; r[2] += sn * k[1]; r[3] += sn * k[2]; r[5] -= sn * k[0]; r[6] -= sn * k[1]; r[7] += sn * k[0];
        return r;
    }

    /// <summary>The directions (in the joint's own frame: x, y, z translation then x, y, z rotation) a joint lets its child move in: no force or moment is carried along those.</summary>
    static bool[] FreeDirections(JointInfo j) => j.Type switch
    {
        JointType.Revolute => [false, false, false, false, false, true],
        JointType.Slider => [false, false, true, false, false, false],
        JointType.Cylindrical => [false, false, true, false, false, true],
        JointType.Ball => [false, false, false, true, true, true],
        JointType.Planar => [true, true, false, false, false, false],
        _ => new bool[6],
    };

    /// <summary>
    /// What each joint holds. The joint is released one direction at a time by a tiny extra relative motion (with the loops kept closed and the coordinates fixed);
    /// the work the motion would do against the inertia, weight and applied forces of every part is the force or moment the joint must supply in that direction.
    /// </summary>
    (Vec3[] Force, Vec3[] Moment) Reactions(State st, Vec3 gravity)
    {
        int n = st.Q.Length, nb = _bodies.Length; var force = new Vec3[_joints.Count]; var moment = new Vec3[_joints.Count];
        var f = new Vec3[nb]; var g = new Vec3[nb];
        for (int b = 0; b < nb; b++)
        {
            var body = _bodies[b]; var kin = st.Kin;
            double ax = kin.A0[b].X, ay = kin.A0[b].Y, az = kin.A0[b].Z, wx = kin.Alpha0[b].X, wy = kin.Alpha0[b].Y, wz = kin.Alpha0[b].Z;
            for (int k = 0; k < n; k++)
            {
                ax += kin.Jv[b][k] * st.Qdd[k]; ay += kin.Jv[b][n + k] * st.Qdd[k]; az += kin.Jv[b][2 * n + k] * st.Qdd[k];
                wx += kin.Jw[b][k] * st.Qdd[k]; wy += kin.Jw[b][n + k] * st.Qdd[k]; wz += kin.Jw[b][2 * n + k] * st.Qdd[k];
            }
            f[b] = (new Vec3(ax, ay, az) - gravity) * body.Mass - st.Fext[b];
            if (body.Mass > 0)
            {
                var iw = Mul(Mul(kin.Rot[b], body.Inertia), Tr(kin.Rot[b]));
                g[b] = Apply(iw, new Vec3(wx, wy, wz)) + Vec3.Cross(kin.W[b], Apply(iw, kin.W[b]));
            }
            g[b] -= st.Text[b];
        }
        var basePose = Pose(st.Q);
        for (int ji = 0; ji < _joints.Count; ji++)
        {
            var joint = _joints[ji]; if (!basePose.TryGetValue(joint.ParentKey, out var parentPose)) continue;
            var lam = new double[6];
            for (int d = 0; d < 6; d++)
            {
                double h = d < 3 ? 1e-3 : 1e-6; var results = new (Dictionary<string, Xform> pl, double[] full)[2];
                for (int sgn = 0; sgn < 2; sgn++)
                {
                    double e = sgn == 0 ? h : -h; Xform eps;
                    if (d < 3) { var t = new double[3]; t[d] = e; eps = Xform.Translation(new Vec3(t[0], t[1], t[2])); }
                    else eps = new Xform(SmallTurn(d - 3, e), Vec3.Zero);
                    var extra = new Dictionary<string, Xform> { [joint.Key] = eps };
                    var (js, pl) = Solved(st.Q, extra); results[sgn] = (pl, FullOf(js));
                }
                double sum = 0;
                for (int b = 0; b < nb; b++)
                {
                    var key = _bodies[b].Key; if (!results[0].pl.ContainsKey(key)) continue;
                    var dv = (ComOf(results[0].pl, b) - ComOf(results[1].pl, b)) * (1 / (2 * h));
                    var rp = results[0].pl[key].R; var rm = results[1].pl[key].R; var diff = new double[9];
                    for (int i = 0; i < 9; i++) diff[i] = (rp[i] - rm[i]) / (2 * h);
                    var dw = Vee(Mul(diff, Tr(basePose[key].R)));
                    // translation ε is in mm: positions in mm give m/m; angles give rad per mm = 1000 rad/m. Rotation ε is in rad: positions in mm give 1e-3 m per rad.
                    double lin = d < 3 ? 1 : 1e-3, ang = d < 3 ? 1e3 : 1;
                    sum += Vec3.Dot(f[b], dv * lin) + Vec3.Dot(g[b], dw * ang);
                }
                for (int j = 0; j < _dofs.Length; j++)
                {
                    double dq = (results[0].full[j] - results[1].full[j]) / (2 * h) * (d < 3 ? 1e3 : 1);
                    sum -= dq * st.QfFull[j];
                }
                lam[d] = sum;
            }
            var free = FreeDirections(joint); for (int d = 0; d < 6; d++) if (free[d]) lam[d] = 0;
            var frame = new Xform(Mul(parentPose.R, joint.A.R), Vec3.Zero);
            force[ji] = frame.Rotate(new Vec3(lam[0], lam[1], lam[2])); moment[ji] = frame.Rotate(new Vec3(lam[3], lam[4], lam[5]));
        }
        return (force, moment);
    }

    // ------------------------------------------------------------------ the study
    sealed class State
    {
        public double[] Q = [], Qd = [], Qdd = [], Tau = [], QFull = [], QdFull = [], QddFull = [];
        public Kin Kin = new(); public double[,] M = new double[0, 0]; public double Ke; public double[] ForceNow = []; public double[] QfFull = []; public Vec3[] Fext = [], Text = [];
    }

    public MotionResult Run(MotionSettings s, CancellationToken ct = default, Action<string>? progress = null)
    {
        if (_ind.Length == 0) throw new InvalidOperationException("There is nothing to move. Create revolute, slider or other joints in the Joints tab first.");
        if (s.Duration <= 0 || s.Interval <= 0) throw new InvalidOperationException("Duration and output interval must be positive.");
        int n = _ind.Length, nb = _bodies.Length, nd = _dofs.Length, samples = (int)Math.Round(s.Duration / s.Interval) + 1;
        if (samples > 20001) throw new InvalidOperationException("That is more than 20,000 output times. Use a longer output interval.");
        var drv = _ind.Select(i => _dofs[i].Mode != DriveMode.Free || !s.Dynamic).ToArray();
        var free = Enumerable.Range(0, n).Where(k => !drv[k]).ToArray();
        var driven = Enumerable.Range(0, n).Where(k => drv[k]).ToArray();
        var q0 = new double[n]; for (int k = 0; k < n; k++) q0[k] = _dofs[_ind[k]].Start * _dofs[_ind[k]].Scale;
        _u = _dep.Select(i => _dofs[i].Start * _dofs[i].Scale).ToArray();
        // how every motion value depends on the independent ones: constant for gears, but it changes with position when a loop fixes some of them
        double[,] EOf(double[] q)
        {
            var E = new double[nd, n];
            for (int k = 0; k < n; k++)
            {
                const double e = 1e-6; var qp = (double[])q.Clone(); var qm = (double[])q.Clone(); qp[k] += e; qm[k] -= e;
                var fp = Full(qp); var fm = Full(qm);
                for (int j = 0; j < nd; j++) E[j, k] = (fp[j] - fm[j]) / (2 * e);
            }
            return E;
        }
        // acceleration of every motion value that comes from the speeds alone (second difference along the straight line q + qd·t); zero unless a loop is involved
        double[] Accel0Full(double[] q, double[] qd)
        {
            var a0 = new double[nd]; if (_dep.Length == 0) return a0;
            double reach = 0; for (int k = 0; k < n; k++) reach = Math.Max(reach, Math.Abs(qd[k]) * (_dofs[_ind[k]].Angular ? 1 : 50));
            if (reach < 1e-12) return a0;
            double tau = 2e-3 / reach; var qa = new double[n]; var qb = new double[n];
            for (int k = 0; k < n; k++) { qa[k] = q[k] + tau * qd[k]; qb[k] = q[k] - tau * qd[k]; }
            var fa = Full(qa); var f0 = Full(q); var fb = Full(qb);
            for (int j = 0; j < nd; j++) a0[j] = (fa[j] - 2 * f0[j] + fb[j]) / (tau * tau);
            return a0;
        }

        MotionResult? res = null;
        var limitHit = new HashSet<int>(); var askedBeyond = new HashSet<int>();

        State Evaluate(double t, double[] qFree, double[] qdFree)
        {
            var st = new State { Q = new double[n], Qd = new double[n], Qdd = new double[n], Tau = new double[n] };
            for (int i = 0; i < free.Length; i++) { st.Q[free[i]] = qFree[i]; st.Qd[free[i]] = qdFree[i]; }
            foreach (int k in driven)
            {
                var d = _dofs[_ind[k]]; var (qq, vv, aa) = Profile(d, t);
                if (d.Limited && (qq < d.Lo * d.Scale - 1e-12 || qq > d.Hi * d.Scale + 1e-12))
                {
                    qq = Math.Clamp(qq, d.Lo * d.Scale, d.Hi * d.Scale); vv = 0; aa = 0;
                    if (askedBeyond.Add(_ind[k])) res!.Events.Add($"{d.Name} is asked to go past its limit ({d.Lo:0.##} to {d.Hi:0.##} {d.Unit}) at about {t:0.###} s; the joint holds at the limit.");
                }
                st.Q[k] = qq; st.Qd[k] = vv; st.Qdd[k] = aa;
            }
            st.Kin = Kinematics(st.Q, st.Qd);
            var E = EOf(st.Q);
            if (!s.Dynamic) { st.QFull = Full(st.Q); st.QdFull = Mat(E, st.Qd); st.QddFull = Add(Mat(E, st.Qdd), Accel0Full(st.Q, st.Qd)); return st; }
            var (M, H) = Terms(st.Kin, n, s.Gravity);
            st.M = M;
            // forces on the motion values: constant effort and viscous friction (a driven value takes no effort of its own)
            var qdFull = Mat(E, st.Qd); var qf = new double[nd];
            for (int j = 0; j < nd; j++)
            {
                var d = _dofs[j]; bool drivenHere = !d.Follower && !d.Dependent && _dofs[j].Mode != DriveMode.Free;
                double motor = !drivenHere && d.MotorStall != 0 && d.MotorNoLoad != 0 ? d.MotorStall * (1 - qdFull[j] / (d.MotorNoLoad * d.Scale)) : 0;
                qf[j] = (drivenHere ? 0 : d.Effort) + motor - d.Damping * qdFull[j];
            }
            var Q = new double[n];
            for (int k = 0; k < n; k++) for (int j = 0; j < nd; j++) Q[k] += E[j, k] * qf[j];
            st.Fext = new Vec3[_bodies.Length]; st.Text = new Vec3[_bodies.Length]; st.QfFull = qf;
            st.ForceNow = Forces(st.Kin, t, n, Q, st.Fext, st.Text);
            if (free.Length > 0)
            {
                var A = new double[free.Length, free.Length]; var rhs = new double[free.Length];
                for (int i = 0; i < free.Length; i++)
                {
                    int a = free[i]; double r = Q[a] - H[a];
                    foreach (int dk in driven) r -= M[a, dk] * st.Qdd[dk];
                    rhs[i] = r;
                    for (int c = 0; c < free.Length; c++) A[i, c] = M[a, free[c]];
                }
                var sol = Solve(A, rhs) ?? throw new InvalidOperationException("A free joint has nothing with mass to move. Give the moving parts a material, or drive that joint.");
                for (int i = 0; i < free.Length; i++) st.Qdd[free[i]] = sol[i];
            }
            for (int a = 0; a < n; a++)
            {
                double acc = H[a]; for (int c = 0; c < n; c++) acc += M[a, c] * st.Qdd[c];
                st.Tau[a] = acc - Q[a];
            }
            st.QFull = Full(st.Q); st.QdFull = qdFull; st.QddFull = Add(Mat(E, st.Qdd), Accel0Full(st.Q, st.Qd));
            double ke = 0; for (int a = 0; a < n; a++) for (int c = 0; c < n; c++) ke += 0.5 * st.Qd[a] * M[a, c] * st.Qd[c];
            st.Ke = ke;
            return st;
        }

        res = new MotionResult
        {
            Time = new double[samples], Dofs = _dofs, Dynamic = s.Dynamic, Bodies = _bodies,
            Pos = Jag(nd, samples), Vel = Jag(nd, samples), Acc = Jag(nd, samples), Effort = Jag(nd, samples), Power = Jag(nd, samples),
            Kinetic = new double[samples], Potential = new double[samples],
            ForceNames = _forces.Select(f => f.force.Name).ToArray(), ForceValue = Jag(_forces.Count, samples),
            ReactionJoints = s.Dynamic && s.Reactions ? _joints.Select(j => j.Name).ToArray() : [],
            ReactionForce = s.Dynamic && s.Reactions ? _joints.Select(_ => new Vec3[samples]).ToArray() : [],
            ReactionMoment = s.Dynamic && s.Reactions ? _joints.Select(_ => new Vec3[samples]).ToArray() : [],
            Path = Enumerable.Range(0, nb).Select(_ => new Vec3[samples]).ToArray(), Speed = Jag(nb, samples), Accel = Jag(nb, samples), AngSpeed = Jag(nb, samples),
        };
        var qF = free.Select(k => q0[k]).ToArray(); var qdF = free.Select(k => _dofs[_ind[k]].Rate * _dofs[_ind[k]].Scale).ToArray();
        int sub = Math.Max(1, (int)Math.Ceiling(s.Interval / s.MaxStep - 1e-9)); double h = s.Interval / sub;

        for (int i = 0; i < samples; i++)
        {
            ct.ThrowIfCancellationRequested();
            double t = i * s.Interval; res.Time[i] = t;
            var st = Evaluate(t, qF, qdF);
            for (int j = 0; j < nd; j++)
            {
                var d = _dofs[j]; res.Pos[j][i] = st.QFull[j] / d.Scale; res.Vel[j][i] = st.QdFull[j] / d.Scale; res.Acc[j][i] = st.QddFull[j] / d.Scale;
            }
            if (s.Dynamic)
            {
                foreach (int k in driven)
                {
                    int j = _ind[k]; res.Effort[j][i] = st.Tau[k]; res.Power[j][i] = st.Tau[k] * st.Qd[k];
                }
                res.Kinetic[i] = st.Ke;
                for (int f = 0; f < _forces.Count; f++) res.ForceValue[f][i] = st.ForceNow[f];
                if (s.Reactions)
                {
                    var (rf, rm) = Reactions(st, s.Gravity);
                    for (int jn = 0; jn < _joints.Count; jn++) { res.ReactionForce[jn][i] = rf[jn]; res.ReactionMoment[jn][i] = rm[jn]; }
                }
                double pe = 0; for (int b = 0; b < nb; b++) pe -= _bodies[b].Mass * Vec3.Dot(s.Gravity, st.Kin.Com[b] * 1e-3);
                res.Potential[i] = pe;
            }
            for (int b = 0; b < nb; b++)
            {
                res.Path[b][i] = st.Kin.Com[b]; res.Speed[b][i] = st.Kin.V[b].Length; res.AngSpeed[b][i] = st.Kin.W[b].Length;
                double ax = st.Kin.A0[b].X, ay = st.Kin.A0[b].Y, az = st.Kin.A0[b].Z;
                for (int k = 0; k < n; k++) { ax += st.Kin.Jv[b][k] * st.Qdd[k]; ay += st.Kin.Jv[b][n + k] * st.Qdd[k]; az += st.Kin.Jv[b][2 * n + k] * st.Qdd[k]; }
                res.Accel[b][i] = Math.Sqrt(ax * ax + ay * ay + az * az);
            }
            if (i % 8 == 0) progress?.Invoke($"Solving: {t:0.##} of {s.Duration:0.##} s");
            if (i == samples - 1 || free.Length == 0) continue;
            // advance the free coordinates to the next output time with fixed-step RK4
            for (int step = 0; step < sub; step++)
            {
                double t0 = t + step * h; int m = free.Length;
                double[] Deriv(double tt, double[] x)
                {
                    var qa = x[..m]; var va = x[m..];
                    var ev = Evaluate(tt, qa, va); var d = new double[2 * m];
                    for (int a = 0; a < m; a++) { d[a] = va[a]; d[m + a] = ev.Qdd[free[a]]; }
                    return d;
                }
                var x0 = qF.Concat(qdF).ToArray();
                var k1 = Deriv(t0, x0);
                var k2 = Deriv(t0 + h / 2, Axpy(x0, k1, h / 2));
                var k3 = Deriv(t0 + h / 2, Axpy(x0, k2, h / 2));
                var k4 = Deriv(t0 + h, Axpy(x0, k3, h));
                var x1 = new double[2 * m];
                for (int a = 0; a < 2 * m; a++) x1[a] = x0[a] + h / 6 * (k1[a] + 2 * k2[a] + 2 * k3[a] + k4[a]);
                if (x1.Any(v => !double.IsFinite(v))) throw new InvalidOperationException($"The motion became unstable at {t0:0.###} s. Try a shorter output interval or a smaller time step.");
                for (int a = 0; a < m; a++)
                {
                    var d = _dofs[_ind[free[a]]]; double lo = d.Lo * d.Scale, hi = d.Hi * d.Scale;
                    if (d.Limited && x1[a] < lo) { x1[a] = lo; if (x1[m + a] < 0) { x1[m + a] = 0; if (limitHit.Add(_ind[free[a]])) res.Events.Add($"{d.Name} stopped at its lower limit ({d.Lo:0.##} {d.Unit}) at {t0 + h:0.###} s."); } }
                    if (d.Limited && x1[a] > hi) { x1[a] = hi; if (x1[m + a] > 0) { x1[m + a] = 0; if (limitHit.Add(_ind[free[a]])) res.Events.Add($"{d.Name} stopped at its upper limit ({d.Hi:0.##} {d.Unit}) at {t0 + h:0.###} s."); } }
                }
                for (int a = 0; a < m; a++) { qF[a] = x1[a]; qdF[a] = x1[m + a]; }
            }
        }
        return res;
    }

    static double[] Add(double[] a, double[] b) { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] + b[i]; return r; }
    static double[] Axpy(double[] x, double[] d, double h) { var r = new double[x.Length]; for (int i = 0; i < x.Length; i++) r[i] = x[i] + h * d[i]; return r; }
    static double[][] Jag(int a, int b) => Enumerable.Range(0, a).Select(_ => new double[b]).ToArray();
    static double[] Mat(double[,] E, double[] v)
    {
        int r = E.GetLength(0), c = E.GetLength(1); var res = new double[r];
        for (int i = 0; i < r; i++) for (int k = 0; k < c; k++) res[i] += E[i, k] * v[k];
        return res;
    }
}
