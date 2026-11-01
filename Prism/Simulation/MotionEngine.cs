using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Simulation;

public enum DriveMode { Hold, Constant, Oscillate, Free }

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
    /// <summary>Internal units (radians or metres) per displayed unit (degrees or mm).</summary>
    public double Scale => Angular ? Math.PI / 180 : 1e-3;
    public string Unit => Angular ? "°" : "mm";
    public string EffortUnit => Angular ? "N·m" : "N";
}

public sealed record MotionSettings(double Duration, double Interval, bool Dynamic, Vec3 Gravity, double MaxStep = 0.002);

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
    readonly int[] _ind;                 // dofs that are not followers
    readonly Dictionary<string, int> _jointIndex = [];
    readonly List<CouplingInfo> _couplings;

    public MotionEngine(IReadOnlyDictionary<string, Xform> start, ISet<string> grounded, IEnumerable<JointInfo> joints, IEnumerable<CouplingInfo> couplings, IEnumerable<MotionBody> bodies, IEnumerable<MotionDof> dofs)
    {
        _start = start; _grounded = grounded;
        // limits are handled here (a driven value holds at its limit, a free one stops there), so the pose itself must stay smooth past them
        _joints = joints.Select(j => j with { HasLimits = false }).ToList(); _couplings = couplings.ToList();
        _bodies = bodies.Where(b => start.ContainsKey(b.Key)).ToArray();
        _dofs = dofs.ToArray();
        for (int i = 0; i < _joints.Count; i++) _jointIndex[_joints[i].Key] = i;
        _ind = Enumerable.Range(0, _dofs.Length).Where(i => !_dofs[i].Follower).ToArray();
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
    List<JointInfo> JointsFor(double[] q)
    {
        var list = new List<JointInfo>(_joints);
        for (int k = 0; k < _ind.Length; k++)
        {
            var d = _dofs[_ind[k]]; int ji = _jointIndex[d.JointKey]; double display = q[k] / d.Scale;
            list[ji] = d.Second ? list[ji] with { Value2 = display } : list[ji] with { Value = display };
        }
        return CouplingSolver.Apply(list, _couplings);
    }

    Dictionary<string, Xform> Pose(double[] q) => AssemblyKinematics.Solve(_start, _grounded, JointsFor(q), null, []);

    /// <summary>Every motion value (internal units) for these independent values.</summary>
    double[] Full(double[] q)
    {
        var js = JointsFor(q); var res = new double[_dofs.Length];
        for (int i = 0; i < _dofs.Length; i++)
        {
            var d = _dofs[i]; var j = js[_jointIndex[d.JointKey]];
            res[i] = (d.Second ? j.Value2 : j.Value) * d.Scale;
        }
        return res;
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

    // ------------------------------------------------------------------ prescribed motion
    (double q, double v, double a) Profile(MotionDof d, double t)
    {
        double s = d.Scale, q0 = d.Start * s;
        switch (d.Mode)
        {
            case DriveMode.Constant: return (q0 + d.Rate * s * t, d.Rate * s, 0);
            case DriveMode.Oscillate:
                double w = 2 * Math.PI * d.Frequency, A = d.Amplitude * s;
                return (q0 + A * Math.Sin(w * t), A * w * Math.Cos(w * t), -A * w * w * Math.Sin(w * t));
            default: return (q0, 0, 0);
        }
    }

    // ------------------------------------------------------------------ the study
    sealed class State
    {
        public double[] Q = [], Qd = [], Qdd = [], Tau = [], QFull = [], QdFull = [], QddFull = [];
        public Kin Kin = new(); public double[,] M = new double[0, 0]; public double Ke;
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
        // how every motion value depends on the independent ones (constant: couplings are linear)
        var q0 = new double[n]; for (int k = 0; k < n; k++) q0[k] = _dofs[_ind[k]].Start * _dofs[_ind[k]].Scale;
        var E = new double[nd, n];
        for (int k = 0; k < n; k++)
        {
            double e = 1e-4; var qp = (double[])q0.Clone(); var qm = (double[])q0.Clone(); qp[k] += e; qm[k] -= e;
            var fp = Full(qp); var fm = Full(qm);
            for (int j = 0; j < nd; j++) E[j, k] = (fp[j] - fm[j]) / (2 * e);
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
            if (!s.Dynamic) { st.QFull = Full(st.Q); st.QdFull = Mat(E, st.Qd); st.QddFull = Mat(E, st.Qdd); return st; }
            var (M, H) = Terms(st.Kin, n, s.Gravity);
            st.M = M;
            // forces on the motion values: constant effort and viscous friction (a driven value takes no effort of its own)
            var qdFull = Mat(E, st.Qd); var qf = new double[nd];
            for (int j = 0; j < nd; j++)
            {
                var d = _dofs[j]; bool drivenHere = !d.Follower && _dofs[j].Mode != DriveMode.Free;
                qf[j] = (drivenHere ? 0 : d.Effort) - d.Damping * qdFull[j];
            }
            var Q = new double[n];
            for (int k = 0; k < n; k++) for (int j = 0; j < nd; j++) Q[k] += E[j, k] * qf[j];
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
            st.QFull = Full(st.Q); st.QdFull = qdFull; st.QddFull = Mat(E, st.Qdd);
            double ke = 0; for (int a = 0; a < n; a++) for (int c = 0; c < n; c++) ke += 0.5 * st.Qd[a] * M[a, c] * st.Qd[c];
            st.Ke = ke;
            return st;
        }

        res = new MotionResult
        {
            Time = new double[samples], Dofs = _dofs, Dynamic = s.Dynamic, Bodies = _bodies,
            Pos = Jag(nd, samples), Vel = Jag(nd, samples), Acc = Jag(nd, samples), Effort = Jag(nd, samples), Power = Jag(nd, samples),
            Kinetic = new double[samples], Potential = new double[samples],
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

    static double[] Axpy(double[] x, double[] d, double h) { var r = new double[x.Length]; for (int i = 0; i < x.Length; i++) r[i] = x[i] + h * d[i]; return r; }
    static double[][] Jag(int a, int b) => Enumerable.Range(0, a).Select(_ => new double[b]).ToArray();
    static double[] Mat(double[,] E, double[] v)
    {
        int r = E.GetLength(0), c = E.GetLength(1); var res = new double[r];
        for (int i = 0; i < r; i++) for (int k = 0; k < c; k++) res[i] += E[i, k] * v[k];
        return res;
    }
}
