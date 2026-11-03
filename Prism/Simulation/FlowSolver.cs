using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Simulation;

public enum FlowMode { Around, Through }

/// <param name="Surface">The closed surface of the body (mm).</param>
/// <param name="Direction">Around: which way the fluid flows.</param>
/// <param name="Speed">Around: the far-away speed. Through: the speed at the inlet (m/s).</param>
/// <param name="Density">kg/m³</param>
/// <param name="Viscosity">Dynamic viscosity, Pa·s.</param>
/// <param name="Length">The size the Reynolds number is based on (mm): the body's width across the flow, or the inlet's width.</param>
/// <param name="Cells">Cells along the longest side of the flow region.</param>
/// <param name="Inlet">Through: the inlet face's triangles. Its normal points out of the body.</param>
public sealed record FlowSetup(TriMesh Surface, FlowMode Mode, Vec3 Direction, double Speed, double Density, double Viscosity, double Length, int Cells, TriMesh? Inlet = null, TriMesh? Outlet = null);

/// <summary>A steady flow field on a block grid, in real units.</summary>
public sealed class FlowResult
{
    public required int Nx { get; init; }
    public required int Ny { get; init; }
    public required int Nz { get; init; }
    /// <summary>Cell size in mm, and the corner of the grid.</summary>
    public required double Dx { get; init; }
    public required Vec3 Origin { get; init; }
    /// <summary>Velocity (m/s) and gauge pressure (Pa) per cell; zero inside solid cells.</summary>
    public required float[] Ux { get; init; }
    public required float[] Uy { get; init; }
    public required float[] Uz { get; init; }
    public required float[] Pressure { get; init; }
    public required byte[] Kind { get; init; }
    /// <summary>Force of the fluid on the body (N).</summary>
    public Vec3 Force { get; init; }
    /// <summary>Drag along the flow (N), and the coefficient, against the frontal area (mm²).</summary>
    public double Drag { get; init; }
    public double DragCoefficient { get; init; }
    public double FrontalArea { get; init; }
    /// <summary>Pressure at the inlet minus pressure at the outlet (Pa), and flow in and out (L/min). Through only.</summary>
    public double PressureDrop { get; init; }
    public double FlowIn { get; init; }
    public double FlowOut { get; init; }
    public double Reynolds { get; init; }
    /// <summary>The Reynolds number the solver really ran at. Below Reynolds when the grid is too coarse for the flow.</summary>
    public double SolvedReynolds { get; init; }
    public int Steps { get; init; }
    public bool Settled { get; init; }
    public double MaxSpeed { get; init; }
    public int Index(int i, int j, int k) => i + Nx * (j + Ny * k);
    public bool IsFluid(int i, int j, int k) => i >= 0 && j >= 0 && k >= 0 && i < Nx && j < Ny && k < Nz && Kind[Index(i, j, k)] != FlowSolver.Solid;
    public Vec3 CellCentre(int i, int j, int k) => Origin + new Vec3((i + .5) * Dx, (j + .5) * Dx, (k + .5) * Dx);

    public double SpeedAt(int c) => Math.Sqrt(Ux[c] * Ux[c] + Uy[c] * Uy[c] + Uz[c] * Uz[c]);

    /// <summary>Velocity at a point by trilinear interpolation; null outside the fluid.</summary>
    public Vec3? VelocityAt(Vec3 p)
    {
        double x = (p.X - Origin.X) / Dx - .5, y = (p.Y - Origin.Y) / Dx - .5, z = (p.Z - Origin.Z) / Dx - .5;
        int i = (int)Math.Floor(x), j = (int)Math.Floor(y), k = (int)Math.Floor(z);
        if (!IsFluid((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(z))) return null;
        double fx = x - i, fy = y - j, fz = z - k, ax = 0, ay = 0, az = 0, sum = 0;
        for (int d = 0; d < 8; d++)
        {
            int a = i + (d & 1), b = j + (d >> 1 & 1), c = k + (d >> 2 & 1);
            if (!IsFluid(a, b, c)) continue;
            double w = ((d & 1) != 0 ? fx : 1 - fx) * ((d >> 1 & 1) != 0 ? fy : 1 - fy) * ((d >> 2 & 1) != 0 ? fz : 1 - fz);
            int id = Index(a, b, c); ax += w * Ux[id]; ay += w * Uy[id]; az += w * Uz[id]; sum += w;
        }
        return sum < 1e-9 ? null : new Vec3(ax / sum, ay / sum, az / sum);
    }

    /// <summary>A line that follows the flow from a point, with the speed along it.</summary>
    public List<(Vec3 p, double speed)> Streamline(Vec3 start, int steps = 900)
    {
        var line = new List<(Vec3, double)>(); var p = start; double h = Dx * 0.5;
        for (int s = 0; s < steps; s++)
        {
            if (VelocityAt(p) is not { } v || v.Length < 1e-9) break;
            line.Add((p, v.Length));
            if (VelocityAt(p + v.Normalized() * (h / 2)) is not { } mid || mid.Length < 1e-9) break;
            p += mid.Normalized() * h;
        }
        return line;
    }

    /// <summary>Mean pressure across a plane square to an axis (0 = x), at the given cell index, over the fluid cells.</summary>
    public double MeanPressure(int axis, int layer)
    {
        double sum = 0; int count = 0;
        for (int k = 0; k < Nz; k++) for (int j = 0; j < Ny; j++) for (int i = 0; i < Nx; i++)
        {
            if ((axis == 0 ? i : axis == 1 ? j : k) != layer) continue;
            int c = Index(i, j, k); if (Kind[c] != FlowSolver.Fluid) continue;
            sum += Pressure[c]; count++;
        }
        return count == 0 ? 0 : sum / count;
    }
}

/// <summary>
/// Basic 3D flow: a lattice Boltzmann solver (D3Q19, two-relaxation-time) on a block grid. It needs no fluid mesh, so any closed body works. Weakly compressible, with walls on the grid's cell faces.
/// Trustworthy for slow, smooth flow. When a grid cannot hold the real Reynolds number the fluid is run thicker, and FlowResult says so.
/// </summary>
public static class FlowSolver
{
    public const byte Fluid = 0, Solid = 1, Inlet = 2, Outlet = 3;

    static readonly int[] Cx = [0, 1, -1, 0, 0, 0, 0, 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0];
    static readonly int[] Cy = [0, 0, 0, 1, -1, 0, 0, 1, -1, -1, 1, 0, 0, 0, 0, 1, -1, 1, -1];
    static readonly int[] Cz = [0, 0, 0, 0, 0, 1, -1, 0, 0, 0, 0, 1, -1, -1, 1, 1, -1, -1, 1];
    static readonly int[] Back = [0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 12, 11, 14, 13, 16, 15, 18, 17];
    static readonly double[] W = [1.0 / 3, 1.0 / 18, 1.0 / 18, 1.0 / 18, 1.0 / 18, 1.0 / 18, 1.0 / 18, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36];

    const double LatticeSpeed = 0.05;                 // the flow speed in grid units: small enough to behave as incompressible
    const double SmallestViscosity = 0.004;           // grid-unit viscosity below which the scheme turns unstable
    const double Magic = 3.0 / 16;                    // the two-relaxation-time choice that puts walls where they should be
    public const int MaxCellCount = 12_000_000;

    /// <summary>Fills cells inside a closed surface by counting crossings along x from every (y, z) column.</summary>
    public static bool[] Inside(TriMesh m, Vec3 origin, double dx, int nx, int ny, int nz)
    {
        var hits = new List<float>[ny * nz];
        for (int t = 0; t < m.TriCount; t++)
        {
            var (ia, ib, ic) = m.Tri(t); var a = m.P(ia); var b = m.P(ib); var c = m.P(ic);
            int j0 = Math.Max((int)Math.Floor((Math.Min(a.Y, Math.Min(b.Y, c.Y)) - origin.Y) / dx - .5), 0), j1 = Math.Min((int)Math.Ceiling((Math.Max(a.Y, Math.Max(b.Y, c.Y)) - origin.Y) / dx - .5), ny - 1);
            int k0 = Math.Max((int)Math.Floor((Math.Min(a.Z, Math.Min(b.Z, c.Z)) - origin.Z) / dx - .5), 0), k1 = Math.Min((int)Math.Ceiling((Math.Max(a.Z, Math.Max(b.Z, c.Z)) - origin.Z) / dx - .5), nz - 1);
            double d = (b.Y - a.Y) * (c.Z - a.Z) - (b.Z - a.Z) * (c.Y - a.Y);
            if (Math.Abs(d) < 1e-14) continue;
            for (int k = k0; k <= k1; k++)
                for (int j = j0; j <= j1; j++)
                {
                    // nudged off the exact cell centre so a ray never runs along a mesh edge
                    double y = origin.Y + (j + .5) * dx + dx * 1.37e-4, z = origin.Z + (k + .5) * dx + dx * 2.71e-4;
                    double u = ((y - a.Y) * (c.Z - a.Z) - (z - a.Z) * (c.Y - a.Y)) / d, v = ((b.Y - a.Y) * (z - a.Z) - (b.Z - a.Z) * (y - a.Y)) / d;
                    if (u < 0 || v < 0 || u + v > 1) continue;
                    (hits[j + ny * k] ??= []).Add((float)(a.X + u * (b.X - a.X) + v * (c.X - a.X)));
                }
        }
        var inside = new bool[nx * ny * nz];
        Parallel.For(0, ny * nz, c =>
        {
            var l = hits[c]; if (l == null) return;
            l.Sort();
            for (int s = 0; s + 1 < l.Count; s += 2)
            {
                int i0 = Math.Max((int)Math.Ceiling((l[s] - origin.X) / dx - .5), 0), i1 = Math.Min((int)Math.Floor((l[s + 1] - origin.X) / dx - .5), nx - 1);
                for (int i = i0; i <= i1; i++) inside[i + nx * c] = true;
            }
        });
        return inside;
    }

    static double Area(TriMesh m, out Vec3 weighted)
    {
        weighted = Vec3.Zero; double area = 0;
        for (int t = 0; t < m.TriCount; t++)
        {
            var (a, b, c) = m.Tri(t); var w = Vec3.Cross(m.P(b) - m.P(a), m.P(c) - m.P(a)) * .5;
            weighted += w; area += w.Length;
        }
        return area;
    }

    public static FlowResult Solve(FlowSetup s, Action<string>? progress = null, CancellationToken token = default, int maxSteps = 0)
    {
        if (!(s.Speed > 0) || !(s.Density > 0) || !(s.Viscosity > 0) || !(s.Length > 0)) throw new ArgumentException("Use a positive speed, density and viscosity.");
        double reynolds = s.Density * s.Speed * s.Length * 1e-3 / s.Viscosity;
        var min = s.Surface.Min; var max = s.Surface.Max; var size = s.Surface.Size;
        var dir = s.Direction.Normalized();
        Vec3 origin; double dx; int nx, ny, nz; bool[] inside;

        if (s.Mode == FlowMode.Around)
        {
            // a wind tunnel around the body: most of the room is behind it
            double L = Math.Max(size.X, Math.Max(size.Y, size.Z));
            var lo = min - new Vec3(L, L, L); var hi = max + new Vec3(L, L, L);
            hi += new Vec3(Math.Max(dir.X, 0), Math.Max(dir.Y, 0), Math.Max(dir.Z, 0)) * (2 * L);
            lo += new Vec3(Math.Min(dir.X, 0), Math.Min(dir.Y, 0), Math.Min(dir.Z, 0)) * (2 * L);
            var box = hi - lo;
            dx = Math.Max(box.X, Math.Max(box.Y, box.Z)) / Math.Max(s.Cells, 16);
            origin = lo; nx = (int)Math.Ceiling(box.X / dx); ny = (int)Math.Ceiling(box.Y / dx); nz = (int)Math.Ceiling(box.Z / dx);
        }
        else
        {
            if (s.Inlet == null || s.Outlet == null) throw new ArgumentException("Pick the inlet and outlet faces.");
            dx = Math.Max(size.X, Math.Max(size.Y, size.Z)) / Math.Max(s.Cells, 16);
            origin = min - new Vec3(2 * dx, 2 * dx, 2 * dx); nx = (int)Math.Ceiling(size.X / dx) + 4; ny = (int)Math.Ceiling(size.Y / dx) + 4; nz = (int)Math.Ceiling(size.Z / dx) + 4;
        }
        long count = (long)nx * ny * nz;
        if (count > MaxCellCount) throw new InvalidOperationException($"That is {count:N0} cells. Use a lower detail: the limit is {MaxCellCount:N0}.");
        progress?.Invoke($"Filling {count:N0} cells…");
        inside = Inside(s.Surface, origin, dx, nx, ny, nz);
        int N = (int)count, nxy = nx * ny;
        var kind = new byte[N];
        int Id(int i, int j, int k) => i + nx * (j + ny * k);
        Vec3 Centre(int i, int j, int k) => origin + new Vec3((i + .5) * dx, (j + .5) * dx, (k + .5) * dx);

        double uLat = LatticeSpeed, lengthCells = s.Length / dx, nuWanted = uLat * lengthCells / reynolds, nuLat = Math.Max(nuWanted, SmallestViscosity);
        double solvedRe = uLat * lengthCells / nuLat;
        var inletVelocity = Vec3.Zero; var outletInward = (0, 0, 0); var inletLayer = new List<int>(); var outletLayer = new List<int>();

        if (s.Mode == FlowMode.Around)
        {
            inletVelocity = dir * uLat;
            for (int k = 0; k < nz; k++) for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
            {
                int c = Id(i, j, k);
                if (inside[c]) { kind[c] = Solid; continue; }
                bool edge = i == 0 || j == 0 || k == 0 || i == nx - 1 || j == ny - 1 || k == nz - 1;
                if (!edge) continue;
                // the upstream wall holds the free stream; the sides and the far end are open, so the tunnel does not squeeze the flow around the body
                bool upstream = (dir.X > .5 && i == 0) || (dir.X < -.5 && i == nx - 1) || (dir.Y > .5 && j == 0) || (dir.Y < -.5 && j == ny - 1) || (dir.Z > .5 && k == 0) || (dir.Z < -.5 && k == nz - 1);
                kind[c] = upstream ? Inlet : Outlet;
            }
            outletInward = ((int)-Math.Round(dir.X), (int)-Math.Round(dir.Y), (int)-Math.Round(dir.Z));
        }
        else
        {
            for (int c = 0; c < N; c++) kind[c] = inside[c] ? Fluid : Solid;
            Area(s.Inlet!, out var inWeighted); Area(s.Outlet!, out var outWeighted);
            var inNormal = inWeighted.Normalized(); var outNormal = outWeighted.Normalized();
            inletVelocity = -inNormal * uLat;
            var inBvh = s.Inlet!.Bvh; var outBvh = s.Outlet!.Bvh;
            for (int k = 0; k < nz; k++) for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
            {
                int c = Id(i, j, k); if (kind[c] != Fluid) continue;
                var p = Centre(i, j, k);
                if (inBvh.ClosestPoint(p) is { } a && (a.Point - p).Length < 1.1 * dx) { kind[c] = Inlet; inletLayer.Add(c); }
                else if (outBvh.ClosestPoint(p) is { } b && (b.Point - p).Length < 1.1 * dx) { kind[c] = Outlet; outletLayer.Add(c); }
            }
            if (inletLayer.Count == 0 || outletLayer.Count == 0) throw new InvalidOperationException("The grid is too coarse to find the inlet or outlet. Use a higher detail.");
            var o = -outNormal;
            outletInward = Math.Abs(o.X) >= Math.Abs(o.Y) && Math.Abs(o.X) >= Math.Abs(o.Z) ? (Math.Sign(o.X), 0, 0) : Math.Abs(o.Y) >= Math.Abs(o.Z) ? (0, Math.Sign(o.Y), 0) : (0, 0, Math.Sign(o.Z));
        }

        // ---- the distributions
        double tauPlus = 3 * nuLat + .5, wPlus = 1 / tauPlus, wMinus = 1 / (.5 + Magic / (tauPlus - .5));
        var f = new float[19 * N]; var g = new float[19 * N];
        var ux = new float[N]; var uy = new float[N]; var uz = new float[N]; var rho = new float[N];
        double[] Equilibrium(double r, double a, double b, double c)
        {
            var e = new double[19]; double uu = 1.5 * (a * a + b * b + c * c);
            for (int q = 0; q < 19; q++) { double cu = 3 * (Cx[q] * a + Cy[q] * b + Cz[q] * c); e[q] = W[q] * r * (1 + cu + .5 * cu * cu - uu); }
            return e;
        }
        bool around = s.Mode == FlowMode.Around;
        var start = Equilibrium(1, around ? inletVelocity.X : 0, around ? inletVelocity.Y : 0, around ? inletVelocity.Z : 0);
        Parallel.For(0, N, c =>
        {
            rho[c] = 1;
            if (kind[c] == Solid) return;
            if (around) { ux[c] = (float)inletVelocity.X; uy[c] = (float)inletVelocity.Y; uz[c] = (float)inletVelocity.Z; }
            for (int q = 0; q < 19; q++) f[19 * c + q] = (float)start[q];
        });
        var neighbour = new int[19]; for (int q = 0; q < 19; q++) neighbour[q] = Cx[q] + nx * (Cy[q] + ny * Cz[q]);
        // the flow needs time to cross the grid and, for viscosity, to spread across the body or duct
        double flowTime = Math.Max(nx, Math.Max(ny, nz)) / uLat, spreadTime = s.Mode == FlowMode.Through ? lengthCells * lengthCells / nuLat : 0, settleTime = Math.Max(flowTime, spreadTime);
        int limit = maxSteps > 0 ? maxSteps : (int)Math.Min(Math.Max(flowTime * 6, spreadTime * 4), 200000);
        int check = Math.Max(100, (int)(flowTime / 20));
        Vec3 force = Vec3.Zero; int step = 0; bool settled = false;
        var previous = new float[N]; var (oi, oj, ok) = outletInward;
        var inward0 = inletVelocity.Normalized();
        int inletStep = (int)Math.Round(inward0.X) + nx * ((int)Math.Round(inward0.Y) + ny * (int)Math.Round(inward0.Z));

        for (step = 1; step <= limit; step++)
        {
            token.ThrowIfCancellationRequested();
            bool measure = step % check == 0 || step == limit;
            var forces = new Vec3[nz];
            // collide and stream, pulling each direction from the cell it comes from; a wall sends it straight back
            Parallel.For(0, nz, k =>
            {
                Span<double> p = stackalloc double[19];
                var sum = Vec3.Zero;
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int c = i + nx * (j + ny * k);
                        if (kind[c] == Solid) continue;
                        double r = 0, mx = 0, my = 0, mz = 0;
                        for (int q = 0; q < 19; q++)
                        {
                            int from = c - neighbour[q];
                            bool off = i - Cx[q] < 0 || i - Cx[q] >= nx || j - Cy[q] < 0 || j - Cy[q] >= ny || k - Cz[q] < 0 || k - Cz[q] >= nz;
                            double v;
                            if (off || kind[from] == Solid)
                            {
                                v = f[19 * c + Back[q]];
                                if (measure && !off) { double push = 2 * v; sum += new Vec3(-Cx[q] * push, -Cy[q] * push, -Cz[q] * push); }
                            }
                            else v = f[19 * from + q];
                            p[q] = v; r += v; mx += v * Cx[q]; my += v * Cy[q]; mz += v * Cz[q];
                        }
                        double a = mx / r, b = my / r, d = mz / r, uu = 1.5 * (a * a + b * b + d * d);
                        ux[c] = (float)a; uy[c] = (float)b; uz[c] = (float)d; rho[c] = (float)r;
                        for (int q = 0; q < 19; q++)
                        {
                            int o = Back[q];
                            if (o < q) continue;
                            double cu = 3 * (Cx[q] * a + Cy[q] * b + Cz[q] * d), common = W[q] * r * (1 + .5 * cu * cu - uu), odd = W[q] * r * cu;
                            double plus = .5 * (p[q] + p[o]), minus = .5 * (p[q] - p[o]);
                            double nextPlus = plus - wPlus * (plus - common), nextMinus = minus - wMinus * (minus - odd);
                            g[19 * c + q] = (float)(nextPlus + nextMinus);
                            if (o != q) g[19 * c + o] = (float)(nextPlus - nextMinus);
                        }
                    }
                forces[k] = sum;
            });
            // boundaries: an inlet holds its velocity, an outlet holds its pressure and copies the flow arriving at it
            Parallel.For(0, N, c =>
            {
                byte t = kind[c];
                if (t == Inlet)
                {
                    // a duct's inlet holds its speed and lets pressure build behind it; a tunnel's open sides hold the free stream
                    double level = 1;
                    if (!around) { int inner = c + inletStep; if (inner >= 0 && inner < N && kind[inner] == Fluid) level = rho[inner]; }
                    double uu = 1.5 * Vec3.Dot(inletVelocity, inletVelocity);
                    for (int q = 0; q < 19; q++) { double cu = 3 * (Cx[q] * inletVelocity.X + Cy[q] * inletVelocity.Y + Cz[q] * inletVelocity.Z); g[19 * c + q] = (float)(W[q] * level * (1 + cu + .5 * cu * cu - uu)); }
                }
                else if (t == Outlet)
                {
                    int inner;
                    if (around) { int layerZ = c / nxy, layerY = (c - layerZ * nxy) / nx, layerX = c - nx * (layerY + ny * layerZ); inner = Id(Math.Clamp(layerX, 1, nx - 2), Math.Clamp(layerY, 1, ny - 2), Math.Clamp(layerZ, 1, nz - 2)); }
                    else { inner = c + oi + nx * (oj + ny * ok); if (inner < 0 || inner >= N || kind[inner] == Solid) inner = c; }
                    double a = ux[inner], b = uy[inner], d = uz[inner], uu = 1.5 * (a * a + b * b + d * d);
                    for (int q = 0; q < 19; q++) { double cu = 3 * (Cx[q] * a + Cy[q] * b + Cz[q] * d); g[19 * c + q] = (float)(W[q] * (1 + cu + .5 * cu * cu - uu)); }
                }
            });
            (f, g) = (g, f);
            if (!measure) continue;
            force = forces.Aggregate(Vec3.Zero, (x, y) => x + y);
            double change = 0, level = 0;
            for (int c = 0; c < N; c++)
            {
                if (kind[c] != Fluid) continue;
                double sp = Math.Abs(ux[c]) + Math.Abs(uy[c]) + Math.Abs(uz[c]); change += Math.Abs(sp - previous[c]); level += sp; previous[c] = (float)sp;
            }
            double relative = change / Math.Max(level, 1e-12);
            progress?.Invoke($"Step {step:N0} of up to {limit:N0}: changing by {relative * 100:0.###}%");
            if (double.IsNaN(level)) throw new InvalidOperationException("The flow blew up. Try a higher detail or a lower speed.");
            if (step > Math.Max(flowTime * 1.5, spreadTime * .75) && relative * settleTime / check < 2e-3) { settled = true; break; }
        }

        // ---- to real units
        double uScale = s.Speed / uLat, pScale = s.Density * uScale * uScale / 3, fScale = s.Density * uScale * uScale * dx * dx * 1e-6;
        var ox = new float[N]; var oy = new float[N]; var oz = new float[N]; var op = new float[N]; double top = 0;
        for (int c = 0; c < N; c++)
        {
            if (kind[c] == Solid) continue;
            ox[c] = (float)(ux[c] * uScale); oy[c] = (float)(uy[c] * uScale); oz[c] = (float)(uz[c] * uScale); op[c] = (float)((rho[c] - 1) * pScale);
            top = Math.Max(top, Math.Sqrt(ox[c] * ox[c] + oy[c] * oy[c] + oz[c] * oz[c]));
        }
        var real = force * fScale;
        double frontal = 0, drag = 0, cd = 0, drop = 0, inFlow = 0, outFlow = 0;
        if (around)
        {
            for (int t = 0; t < s.Surface.TriCount; t++)
            {
                var (a, b, c) = s.Surface.Tri(t);
                frontal += Math.Abs(Vec3.Dot(Vec3.Cross(s.Surface.P(b) - s.Surface.P(a), s.Surface.P(c) - s.Surface.P(a)), dir)) * .25;
            }
            drag = Vec3.Dot(real, dir); cd = drag / (.5 * s.Density * s.Speed * s.Speed * frontal * 1e-6);
        }
        else
        {
            // measured a cell inside each end, where the cells hold a real flow rather than a boundary value
            var inward = inletVelocity.Normalized();
            int di = (int)Math.Round(inward.X), dj = (int)Math.Round(inward.Y), dk = (int)Math.Round(inward.Z);
            var firstCells = inletLayer.Select(c => c + 2 * (di + nx * (dj + ny * dk))).Where(c => c >= 0 && c < N && kind[c] == Fluid).ToList();
            var lastCells = outletLayer.Select(c => c + 2 * (oi + nx * (oj + ny * ok))).Where(c => c >= 0 && c < N && kind[c] == Fluid).ToList();
            double Mean(List<int> l) => l.Count == 0 ? 0 : l.Average(c => (double)op[c]);
            double Rate(List<int> l, Vec3 n) { double sum = 0; foreach (int c in l) sum += ox[c] * n.X + oy[c] * n.Y + oz[c] * n.Z; return sum * dx * dx * 1e-6 * 60000; }
            drop = Mean(firstCells) - Mean(lastCells);
            inFlow = Rate(firstCells, new Vec3(di, dj, dk)); outFlow = Rate(lastCells, new Vec3(-oi, -oj, -ok));
        }
        return new FlowResult
        {
            Nx = nx, Ny = ny, Nz = nz, Dx = dx, Origin = origin, Ux = ox, Uy = oy, Uz = oz, Pressure = op, Kind = kind, Force = real, Drag = drag, DragCoefficient = cd, FrontalArea = frontal,
            PressureDrop = drop, FlowIn = inFlow, FlowOut = outFlow, Reynolds = reynolds, SolvedReynolds = solvedRe, Steps = Math.Min(step, limit), Settled = settled, MaxSpeed = top,
        };
    }
}
