using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Prism.Kernel;

/// <summary>Raw P/Invoke surface of native/sim.cpp (tetrahedral meshing and finite-element solving).</summary>
internal static unsafe class SimNative
{
    const string Lib = "prism_sim";

    [DllImport(Lib)] public static extern IntPtr sim_last_error();
    [DllImport(Lib)] public static extern IntPtr sim_netgen_mesh(float* tris, int nTris, double maxh, double grading, int optimizeSteps);
    [DllImport(Lib)] public static extern void sim_mesh_sizes(IntPtr h, int* nNodes, int* nCorner, int* nTets, int* nBtris);
    [DllImport(Lib)] public static extern void sim_mesh_fill(IntPtr h, double* nodes, int* tets10, int* btris6);
    [DllImport(Lib)] public static extern void sim_mesh_free(IntPtr h);
    [DllImport(Lib)] public static extern void sim_mesh_quality(IntPtr h, double* minQ, double* meanQ, double* minVol);
    [DllImport(Lib)] public static extern IntPtr sim_topo_error();
    [DllImport(Lib)] public static extern IntPtr sim_topo_create(IntPtr h, double* mat, int nFix, int* fixNode, int* fixMask, int nTrac, int* tracTri, double* tracVec, double* gravity,
        double volfrac, double penal, double rmin, double keepRadius, int* nElems);
    [DllImport(Lib)] public static extern int sim_topo_step(IntPtr ctx, double* x, double* info);
    [DllImport(Lib)] public static extern void sim_topo_volumes(IntPtr ctx, double* vol);
    [DllImport(Lib)] public static extern void sim_topo_free(IntPtr ctx);
    [DllImport(Lib)] public static extern int sim_static(IntPtr h, double* mat, int nFix, int* fixNode, int* fixMask, double* fixVal,
        int nForce, int* forceNode, double* forceVec, int nTrac, int* tracTri, double* tracVec, double* gravity,
        double* u, double* stress, double* vm, double* info);
}

/// <summary>A quadratic tetrahedral mesh (10-node tets, 6-node boundary triangles), owned by the native library.</summary>
public sealed unsafe class FemMesh : IDisposable
{
    static readonly object MeshGate = new(); // Netgen has process-global initialization/state.
    readonly object _gate = new();
    IntPtr _h;
    public int NodeCount { get; }
    public int CornerCount { get; }
    public int TetCount { get; }
    public int BoundaryCount { get; }
    /// <summary>x,y,z per node (corner nodes first).</summary>
    public double[] Nodes { get; }
    /// <summary>10 node ids per tet: 4 corners then mid-edge nodes of edges 01,12,02,03,13,23.</summary>
    public int[] Tets { get; }
    /// <summary>6 node ids per boundary triangle: 3 corners then mid-edge nodes of edges 01,12,20.</summary>
    public int[] Boundary { get; }
    public double MinQuality { get; }
    public double MeanQuality { get; }

    FemMesh(IntPtr h)
    {
        _h = h;
        int nn, nc, nt, nb; SimNative.sim_mesh_sizes(h, &nn, &nc, &nt, &nb);
        NodeCount = nn; CornerCount = nc; TetCount = nt; BoundaryCount = nb;
        Nodes = new double[nn * 3]; Tets = new int[nt * 10]; Boundary = new int[nb * 6];
        fixed (double* pn = Nodes) fixed (int* pt = Tets) fixed (int* pb = Boundary) SimNative.sim_mesh_fill(h, pn, pt, pb);
        double mq, aq, mv; SimNative.sim_mesh_quality(h, &mq, &aq, &mv);
        MinQuality = mq; MeanQuality = aq;
    }

    FemMesh(IReadOnlyList<FemMesh> parts)
    {
        NodeCount = parts.Sum(p => p.NodeCount); CornerCount = parts.Sum(p => p.CornerCount); TetCount = parts.Sum(p => p.TetCount); BoundaryCount = parts.Sum(p => p.BoundaryCount);
        Nodes = parts.SelectMany(p => p.Nodes).ToArray();
        var tets = new List<int>(TetCount * 10); var boundary = new List<int>(BoundaryCount * 6); int offset = 0;
        foreach (var p in parts) { tets.AddRange(p.Tets.Select(n => n + offset)); boundary.AddRange(p.Boundary.Select(n => n + offset)); offset += p.NodeCount; }
        Tets = [.. tets]; Boundary = [.. boundary];
        MinQuality = parts.Min(p => p.MinQuality); MeanQuality = parts.Average(p => p.MeanQuality);
    }

    /// <summary>Several meshes as one list of nodes and elements, for a solver that takes each body's own material. It holds no native mesh, so SolveStatic and StartTopology do not apply.</summary>
    public static FemMesh Merge(IReadOnlyList<FemMesh> parts) => new(parts);

    static string LastError() => Marshal.PtrToStringUTF8(SimNative.sim_last_error()) ?? "Unknown error.";

    /// <summary>Mesh the volume enclosed by a closed triangle surface (9 floats per triangle). maxh = target element size in mm.</summary>
    public static FemMesh FromSurface(float[] triSoup, double maxh, double grading = 0.3, int optimizeSteps = 3)
    {
        ArgumentNullException.ThrowIfNull(triSoup);
        if (triSoup.Length == 0 || triSoup.Length % 9 != 0)
            throw new ArgumentException("Expected nine coordinates per triangle.", nameof(triSoup));
        lock (MeshGate)
        {
        fixed (float* p = triSoup)
        {
            var h = SimNative.sim_netgen_mesh(p, triSoup.Length / 9, maxh, grading, optimizeSteps);
            if (h == IntPtr.Zero) throw new KernelException(LastError());
            return new FemMesh(h);
        }
        }
    }

    static void ValidateArrays(int[] fixNode, int[] fixMask, int[] tracTri, double[] tracVec, double[]? gravity)
    {
        ArgumentNullException.ThrowIfNull(fixNode);
        ArgumentNullException.ThrowIfNull(fixMask);
        ArgumentNullException.ThrowIfNull(tracTri);
        ArgumentNullException.ThrowIfNull(tracVec);
        if (fixMask.Length != fixNode.Length || tracVec.LongLength != 3L * tracTri.Length ||
            (gravity != null && gravity.Length != 3))
            throw new ArgumentException("Fixture, traction, or gravity arrays have inconsistent lengths.");
    }

    public sealed record StaticResult(double[] Displacement, double[] Stress, double[] VonMises, double Iterations, double ReactionSum, Vec3 Reaction, double Applied)
    {
        public double AssembleSeconds { get; init; }
        public double SolveSeconds { get; init; }
    }

    /// <summary>
    /// Linear static analysis. Material: E (MPa), Poisson ratio, density (tonne/mm3). Fixed nodes use a mask (1=x, 2=y, 4=z) with optional prescribed values.
    /// Loads: nodal forces (N), tractions per boundary triangle (N/mm2) and a gravity vector (mm/s2).
    /// </summary>
    public StaticResult SolveStatic(double E, double nu, double density, int[] fixNode, int[] fixMask, double[]? fixVal,
        int[] forceNode, double[] forceVec, int[] tracTri, double[] tracVec, double[]? gravity)
    {
        lock (_gate)
        {
            if (_h == IntPtr.Zero) throw new InvalidOperationException("A merged mesh has more than one material, so it is solved through CalculiX.");
            ValidateArrays(fixNode, fixMask, tracTri, tracVec, gravity);
            ArgumentNullException.ThrowIfNull(forceNode);
            ArgumentNullException.ThrowIfNull(forceVec);
            if (forceVec.LongLength != 3L * forceNode.Length || (fixVal != null && fixVal.LongLength != 3L * fixNode.Length))
                throw new ArgumentException("Force or prescribed displacement arrays have inconsistent lengths.");
            var mat = new[] { E, nu, density };
            var u = new double[NodeCount * 3]; var s = new double[NodeCount * 6]; var vm = new double[NodeCount]; var info = new double[8];
            int ok;
            fixed (double* pm = mat) fixed (int* pfn = fixNode) fixed (int* pfm = fixMask) fixed (double* pfv = fixVal) fixed (int* pfo = forceNode) fixed (double* pfv2 = forceVec)
            fixed (int* ptt = tracTri) fixed (double* ptv = tracVec) fixed (double* pg = gravity) fixed (double* pu = u) fixed (double* ps = s) fixed (double* pvm = vm) fixed (double* pi = info)
                ok = SimNative.sim_static(_h, pm, fixNode.Length, pfn, pfm, pfv, forceNode.Length, pfo, pfv2, tracTri.Length, ptt, ptv, gravity == null ? null : pg, pu, ps, pvm, pi);
            if (ok == 0) throw new KernelException(LastError());
            return new StaticResult(u, s, vm, info[0], info[1], new Vec3(info[2], info[3], info[4]), info[5]) { AssembleSeconds = info[6], SolveSeconds = info[7] };
        }
    }

    /// <summary>Start a generative-design (topology optimization) run on this mesh. Loads: tractions on boundary triangles. Densities start at the target volume fraction.</summary>
    public TopologyRun StartTopology(double E, double nu, double density, int[] fixNode, int[] fixMask, int[] tracTri, double[] tracVec, double[]? gravity,
        double volumeFraction, double penalty, double filterRadius, double keepRadius)
    {
        lock (_gate)
        {
            if (_h == IntPtr.Zero) throw new InvalidOperationException("Topology optimization requires an active native mesh.");
            ValidateArrays(fixNode, fixMask, tracTri, tracVec, gravity);
            var mat = new[] { E, nu, density }; int ne;
            IntPtr ctx;
            fixed (double* pm = mat) fixed (int* pfn = fixNode) fixed (int* pfm = fixMask) fixed (int* ptt = tracTri) fixed (double* ptv = tracVec) fixed (double* pg = gravity)
                ctx = SimNative.sim_topo_create(_h, pm, fixNode.Length, pfn, pfm, tracTri.Length, ptt, ptv, gravity == null ? null : pg, volumeFraction, penalty, filterRadius, keepRadius, &ne);
            if (ctx == IntPtr.Zero) throw new KernelException(Marshal.PtrToStringUTF8(SimNative.sim_topo_error()) ?? "Could not start the optimization.");
            return new TopologyRun(ctx, ne);
        }
    }

    public void Dispose() { lock (_gate) { if (_h != IntPtr.Zero) { SimNative.sim_mesh_free(_h); _h = IntPtr.Zero; } } GC.SuppressFinalize(this); }
    ~FemMesh() { Dispose(); }
}

/// <summary>One generative-design run: call Step repeatedly; each call is one optimization iteration.</summary>
public sealed unsafe class TopologyRun : IDisposable
{
    readonly object _gate = new();
    IntPtr _ctx;
    public int ElementCount { get; }
    public double[] Density { get; }
    public double[] Volume { get; }
    public int Iteration { get; private set; }
    public double Compliance { get; private set; }
    public double VolumeFraction { get; private set; }
    public double Change { get; private set; }
    public double StepSeconds { get; private set; }
    public double FirstCompliance { get; private set; }

    internal TopologyRun(IntPtr ctx, int ne)
    {
        _ctx = ctx; ElementCount = ne; Density = new double[ne]; Volume = new double[ne];
        fixed (double* v = Volume) SimNative.sim_topo_volumes(ctx, v);
    }

    public void Step()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_ctx == IntPtr.Zero, this);
            var info = new double[8];
            int ok;
            fixed (double* x = Density) fixed (double* pi = info) ok = SimNative.sim_topo_step(_ctx, x, pi);
            if (ok == 0) throw new KernelException(Marshal.PtrToStringUTF8(SimNative.sim_topo_error()) ?? "The optimization failed.");
            Iteration++; Compliance = info[0]; VolumeFraction = info[1]; Change = info[2]; StepSeconds = info[3];
            if (Iteration == 1) FirstCompliance = Compliance;
        }
    }

    public void Dispose() { lock (_gate) { if (_ctx != IntPtr.Zero) { SimNative.sim_topo_free(_ctx); _ctx = IntPtr.Zero; } } GC.SuppressFinalize(this); }
    ~TopologyRun() { Dispose(); }
}
