using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Simulation;

/// <summary>A pipe between two named points. SI units: metres.</summary>
public sealed record NetworkPipe(string From, string To, double Length, double Diameter, double MinorLoss = 0);

/// <summary>A pump lifting from one point to another. Its head falls from ShutoffHead at no flow to zero at MaxFlow (m, m³/s). It lets nothing run backward.</summary>
public sealed record NetworkPump(string From, string To, double ShutoffHead, double MaxFlow);

public sealed record NetworkFluid(double Density, double Viscosity, double Roughness = 0);

public sealed record PipeResult(NetworkPipe Pipe, double Flow, double Velocity, double Reynolds, double Friction, double HeadLoss, string Regime);
public sealed record PumpResult(NetworkPump Pump, double Flow, double Head, double Power);
public sealed record NetworkResult(IReadOnlyList<PipeResult> Pipes, IReadOnlyList<PumpResult> Pumps, IReadOnlyDictionary<string, double> Pressure, IReadOnlyDictionary<string, double> Head, int Iterations, double Imbalance, List<string> Notes);

/// <summary>
/// Steady, incompressible flow through a network of round pipes and pumps. The flows and the heads are solved together by Newton's method (the gradient method
/// used for water networks), with Darcy–Weisbach losses: 64/Re when laminar, Colebrook–White when turbulent, a blend across the 2000–4000 transition.
/// </summary>
public static class PipeNetwork
{
    const double Gravity = 9.80665;

    public static double Friction(double re, double relativeRoughness)
    {
        static double Laminar(double r) => 64 / Math.Max(r, 1e-9);
        double Turbulent(double r)
        {
            double f = 0.02;
            for (int i = 0; i < 40; i++) { double x = -2 * Math.Log10(relativeRoughness / 3.7 + 2.51 / (r * Math.Sqrt(f))); f = 1 / (x * x); }
            return f;
        }
        if (re <= 2000) return Laminar(re);
        if (re >= 4000) return Turbulent(re);
        double t = (re - 2000) / 2000;
        return Laminar(2000) * (1 - t) + Turbulent(4000) * t;
    }

    static double PipeLoss(NetworkPipe p, NetworkFluid fluid, double q)
    {
        double area = Math.PI * p.Diameter * p.Diameter / 4, v = Math.Abs(q) / area, re = fluid.Density * v * p.Diameter / fluid.Viscosity;
        double loss = (Friction(re, fluid.Roughness / p.Diameter) * p.Length / p.Diameter + p.MinorLoss) * v * v / (2 * Gravity);
        return Math.Sign(q) * loss;
    }

    static double PumpHead(NetworkPump p, double q) => p.ShutoffHead * (1 - q * q / (p.MaxFlow * p.MaxFlow));

    /// <param name="pressures">Points held at a known gauge pressure (Pa): tanks, the open air. At least one is needed.</param>
    /// <param name="drawn">Flow taken out at a point (m³/s), negative to put it in.</param>
    /// <param name="heights">Height of a point above the reference (m). Points not listed are at zero.</param>
    public static NetworkResult Solve(IReadOnlyList<NetworkPipe> pipes, IReadOnlyList<NetworkPump> pumps, NetworkFluid fluid, IReadOnlyDictionary<string, double> pressures,
        IReadOnlyDictionary<string, double>? drawn = null, IReadOnlyDictionary<string, double>? heights = null)
    {
        if (pipes.Count + pumps.Count == 0) throw new ArgumentException("Add at least one pipe.");
        if (pressures.Count == 0) throw new ArgumentException("Give at least one point a known pressure, such as a tank or the open air at 0.");
        if (pipes.Any(p => !(p.Length > 0) || !(p.Diameter > 0) || p.MinorLoss < 0 || p.From == p.To)) throw new ArgumentException("Each pipe needs two different ends and a positive length and diameter.");
        if (pumps.Any(p => !(p.ShutoffHead > 0) || !(p.MaxFlow > 0) || p.From == p.To)) throw new ArgumentException("Each pump needs two different ends and a positive head and flow.");
        if (!(fluid.Density > 0) || !(fluid.Viscosity > 0)) throw new ArgumentException("Use a positive fluid density and viscosity.");
        drawn ??= new Dictionary<string, double>(); heights ??= new Dictionary<string, double>();

        var names = pipes.SelectMany(p => new[] { p.From, p.To }).Concat(pumps.SelectMany(p => new[] { p.From, p.To })).Distinct().ToList();
        foreach (var key in pressures.Keys.Concat(drawn.Keys).Concat(heights.Keys)) if (!names.Contains(key)) throw new ArgumentException($"No pipe reaches {key}.");
        int n = names.Count, ne = pipes.Count + pumps.Count;
        var index = names.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
        var from = pipes.Select(p => index[p.From]).Concat(pumps.Select(p => index[p.From])).ToArray();
        var to = pipes.Select(p => index[p.To]).Concat(pumps.Select(p => index[p.To])).ToArray();

        // every point must lead to a known pressure, or its level is undefined
        var known = pressures.Keys.Select(k => index[k]).ToHashSet(); var reached = new HashSet<int>(known);
        for (bool grew = true; grew;)
        {
            grew = false;
            for (int e = 0; e < ne; e++) if (reached.Contains(from[e]) != reached.Contains(to[e])) { reached.Add(from[e]); reached.Add(to[e]); grew = true; }
        }
        if (reached.Count < n) throw new ArgumentException($"{names[Enumerable.Range(0, n).First(i => !reached.Contains(i))]} is not connected to any point with a known pressure.");

        double Z(int i) => heights.TryGetValue(names[i], out var z) ? z : 0;
        var head = new double[n]; var demand = new double[n];
        foreach (var (k, v) in drawn) demand[index[k]] = v;
        double mean = pressures.Average(kv => kv.Value / (fluid.Density * Gravity) + Z(index[kv.Key]));
        for (int i = 0; i < n; i++) head[i] = mean;
        foreach (var (k, v) in pressures) head[index[k]] = v / (fluid.Density * Gravity) + Z(index[k]);
        var q = new double[ne];
        for (int e = 0; e < pipes.Count; e++) q[e] = Math.PI * pipes[e].Diameter * pipes[e].Diameter / 4 * 0.5;
        for (int e = pipes.Count; e < ne; e++) q[e] = pumps[e - pipes.Count].MaxFlow * 0.5;

        double Loss(int e, double flow)
        {
            if (e < pipes.Count) return PipeLoss(pipes[e], fluid, flow);
            var pump = pumps[e - pipes.Count];
            return flow >= 0 ? -PumpHead(pump, flow) : -pump.ShutoffHead - flow * 1e7 / Math.Max(pump.MaxFlow, 1e-12);      // a check valve shuts against backflow
        }
        double Slope(int e, double flow)
        {
            double h = Math.Max(Math.Abs(flow) * 1e-4, 1e-10);
            return Math.Max((Loss(e, flow + h) - Loss(e, flow - h)) / (2 * h), 1e-6);
        }
        var r1 = new double[ne]; var r2 = new double[n];
        double Residual(double[] flow, double[] h)
        {
            Array.Clear(r2); double worst = 0;
            for (int e = 0; e < ne; e++) { r1[e] = Loss(e, flow[e]) - (h[from[e]] - h[to[e]]); worst = Math.Max(worst, Math.Abs(r1[e])); r2[from[e]] -= flow[e]; r2[to[e]] += flow[e]; }
            for (int i = 0; i < n; i++) { r2[i] -= demand[i]; if (!known.Contains(i)) worst = Math.Max(worst, Math.Abs(r2[i]) * 1e4); }
            return worst;
        }

        int solved = 0; var free = Enumerable.Range(0, n).Where(i => !known.Contains(i)).ToArray(); var slot = new int[n];
        for (int i = 0; i < n; i++) slot[i] = -1;
        for (int i = 0; i < free.Length; i++) slot[free[i]] = i;
        double error = Residual(q, head);
        for (int it = 0; it < 300 && error > 1e-9; it++)
        {
            solved = it + 1;
            int m = free.Length; var G = new double[m, m]; var rhs = new double[m]; var c = new double[ne];
            for (int e = 0; e < ne; e++)
            {
                c[e] = 1 / Slope(e, q[e]); int a = slot[from[e]], b = slot[to[e]];
                if (a >= 0) { G[a, a] += c[e]; rhs[a] += r2[from[e]] + c[e] * r1[e]; if (b >= 0) G[a, b] -= c[e]; }
                if (b >= 0) { G[b, b] += c[e]; rhs[b] += r2[to[e]] - c[e] * r1[e]; if (a >= 0) G[b, a] -= c[e]; }
            }
            var dh = m == 0 ? [] : Dense(G, rhs) ?? throw new InvalidOperationException("The network could not be solved. Check that no point is cut off.");
            var dq = new double[ne];
            for (int e = 0; e < ne; e++)
            {
                double da = slot[from[e]] >= 0 ? dh[slot[from[e]]] : 0, db = slot[to[e]] >= 0 ? dh[slot[to[e]]] : 0;
                dq[e] = c[e] * (da - db) - c[e] * r1[e];
            }
            for (double step = 1; ; step /= 2)                  // shorten the step until it really helps
            {
                var nq = new double[ne]; var nh = (double[])head.Clone();
                for (int e = 0; e < ne; e++) nq[e] = q[e] + step * dq[e];
                for (int i = 0; i < m; i++) nh[free[i]] += step * dh[i];
                double next = Residual(nq, nh);
                if (next < error || step < 1e-4) { q = nq; head = nh; error = next; break; }
            }
        }
        var notes = new List<string>();
        if (error > 1e-5) notes.Add($"The solution did not fully settle (head error {error:G3} m). Check for pumps working against each other.");

        var pipeResults = new List<PipeResult>();
        for (int e = 0; e < pipes.Count; e++)
        {
            var p = pipes[e]; double area = Math.PI * p.Diameter * p.Diameter / 4, v = Math.Abs(q[e]) / area, re = fluid.Density * v * p.Diameter / fluid.Viscosity;
            string regime = re < 2000 ? "laminar" : re < 4000 ? "transitional" : "turbulent";
            if (regime == "transitional") notes.Add($"{p.From}–{p.To} is in the transition range (Re {re:0}), where friction is uncertain. Its loss is a blend, not a reliable value.");
            pipeResults.Add(new PipeResult(p, q[e], v, re, Friction(re, fluid.Roughness / p.Diameter), Math.Abs(Loss(e, q[e])), regime));
        }
        var pumpResults = new List<PumpResult>();
        for (int e = pipes.Count; e < ne; e++)
        {
            var p = pumps[e - pipes.Count]; double flow = Math.Max(q[e], 0), lift = PumpHead(p, flow);
            if (q[e] <= 1e-12) notes.Add($"The pump {p.From}–{p.To} cannot beat the head against it, so nothing flows through it.");
            if (q[e] > p.MaxFlow) notes.Add($"The pump {p.From}–{p.To} is being pushed past its maximum flow.");
            pumpResults.Add(new PumpResult(p, flow, lift, fluid.Density * Gravity * flow * lift));
        }
        var pressure = new Dictionary<string, double>(); var heads = new Dictionary<string, double>();
        for (int i = 0; i < n; i++) { heads[names[i]] = head[i]; pressure[names[i]] = (head[i] - Z(i)) * fluid.Density * Gravity; }
        double balance = 0;
        Residual(q, head);
        foreach (int i in free) balance = Math.Max(balance, Math.Abs(r2[i]));
        return new NetworkResult(pipeResults, pumpResults, pressure, heads, solved, balance, notes);
    }

    static double[]? Dense(double[,] a, double[] b)
    {
        int n = b.Length; var x = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            if (Math.Abs(a[p, c]) < 1e-300) return null;
            if (p != c) { for (int k = 0; k < n; k++) (a[c, k], a[p, k]) = (a[p, k], a[c, k]); (x[c], x[p]) = (x[p], x[c]); }
            for (int r = c + 1; r < n; r++) { double f = a[r, c] / a[c, c]; if (f == 0) continue; for (int k = c; k < n; k++) a[r, k] -= f * a[c, k]; x[r] -= f * x[c]; }
        }
        for (int r = n - 1; r >= 0; r--) { double s = x[r]; for (int k = r + 1; k < n; k++) s -= a[r, k] * x[k]; x[r] = s / a[r, r]; }
        return x;
    }
}
