using System.Globalization;
using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.StaticAnalysis;

/// <summary>
/// Checks Prism's side of each solver hand-off without running the solve: the supports and loads given to the native
/// static solver, the input files written for CalculiX (thermal, natural frequencies, several bonded bodies) and the
/// reading of CalculiX results. The solvers themselves are trusted third-party or separately benchmarked code; these
/// tests make sure we ask them the right question in the right units and read the answer back correctly.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cae/static-analysis/README.md">Static analysis guide.</seealso>
[Trait("Feature", "StaticAnalysis")]
[Trait("Layer", "Headless")]
public class SolverInputTests
{
    const double Length = 100, Side = 10;

    // CalculiX C3D10 / DC3D10 face numbering (CalculiX manual, "Three-dimensional elements"): corner nodes per face.
    static readonly int[][] CalculixFaces = [[0, 1, 2], [0, 3, 1], [1, 3, 2], [2, 3, 0]];

    /// <summary>A 10 × 10 × 100 mm aluminium 6061 bar standing on Z = 0, meshed at 5 mm.</summary>
    sealed class Bar : IDisposable
    {
        public CadSession Session { get; } = new();
        public SceneObject Body { get; }
        public StudyRunner.Prepared Mesh { get; }
        public Bar()
        {
            var body = CreateBoxBody(Session, Vec3.Zero, Side, Side, Length);
            Session.Update(body.Id, obj => obj.WithMaterial(Materials.Find("6061")!));
            Body = Session.Find(body.Id)!;
            Mesh = StudyRunner.Mesh(Body, 5);
        }
        public int Face(Vec3 normal) => FaceFacing(Body, normal).Index;
        public FaceRef Ref(Vec3 normal) => new(Body.Id, Face(normal));
        public Vec3 Node(int n) => new(Mesh.Mesh.Nodes[3 * n], Mesh.Mesh.Nodes[3 * n + 1], Mesh.Mesh.Nodes[3 * n + 2]);
        public HashSet<int> NodesWhere(Func<Vec3, bool> where) => Enumerable.Range(0, Mesh.Mesh.NodeCount).Where(n => where(Node(n))).ToHashSet();
        public void Dispose() => Mesh.Mesh.Dispose();
    }

    static StudyDef Study(Bar bar, params Load[] loads)
    {
        var study = new StudyDef { BodyId = bar.Body.Id };
        study.Fixtures.Add(new Fixture { Name = "Base", Kind = FixtureKind.Fixed, Faces = [bar.Ref(-Vec3.UnitZ)] });
        study.Loads.AddRange(loads);
        return study;
    }

    /// <summary>The total force the tractions apply (N): traction × triangle area, summed.</summary>
    static Vec3 NetForce(StudyRunner.Prepared p, StudyRunner.Boundary bc) =>
        Enumerable.Range(0, bc.TracTri.Length).Aggregate(Vec3.Zero, (sum, i) =>
            sum + new Vec3(bc.TracVec[3 * i], bc.TracVec[3 * i + 1], bc.TracVec[3 * i + 2]) * p.BoundaryArea[bc.TracTri[i]]);

    /// <summary>The data lines under the first card that starts with <paramref name="card"/>, up to the next card.</summary>
    static List<string> Block(string deck, string card)
    {
        var lines = deck.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        int start = lines.FindIndex(l => l.StartsWith(card, StringComparison.OrdinalIgnoreCase));
        Assert.True(start >= 0, $"The input must contain {card}.");
        return lines.Skip(start + 1).TakeWhile(l => !l.StartsWith('*')).Where(l => l.Length > 0).ToList();
    }

    static double[] Numbers(string line) =>
        line.Split(',').Select(cell => double.Parse(cell, CultureInfo.InvariantCulture)).ToArray();

    // ------------------------------------------------------------------ native static solver: supports and loads

    [AvaloniaFact(DisplayName = "SIN-H001 | A fixed support locks all three directions of exactly the nodes on its face")]
    [Trait("TestId", "SIN-H001")]
    public void FixedSupportNodes()
    {
        using var bar = new Bar();
        var bc = StudyRunner.Conditions(bar.Mesh, Study(bar, new Load { Kind = LoadKind.Force, Value = 1, Direction = Vec3.UnitZ, Total = true, Faces = [bar.Ref(Vec3.UnitZ)] }), []);
        Assert.Equal(bar.NodesWhere(p => Math.Abs(p.Z) < 1e-9), bc.FixNode.ToHashSet());
        Assert.All(bc.FixMask, mask => Assert.Equal(7, mask));
    }

    [AvaloniaTheory(DisplayName = "SIN-H002 | Roller and directional supports lock only their directions")]
    [InlineData(FixtureKind.Slider, true, true, true, 4)]
    [InlineData(FixtureKind.Directional, true, false, true, 5)]
    [InlineData(FixtureKind.Directional, false, true, false, 2)]
    [Trait("TestId", "SIN-H002")]
    public void SupportDirections(FixtureKind kind, bool x, bool y, bool z, int mask)
    {
        using var bar = new Bar();
        var study = Study(bar, new Load { Kind = LoadKind.Force, Value = 1, Direction = Vec3.UnitZ, Total = true, Faces = [bar.Ref(Vec3.UnitZ)] });
        study.Fixtures[0] = new Fixture { Name = "Base", Kind = kind, LockX = x, LockY = y, LockZ = z, Faces = [bar.Ref(-Vec3.UnitZ)] };
        var bc = StudyRunner.Conditions(bar.Mesh, study, []);
        Assert.All(bc.FixMask, m => Assert.Equal(mask, m));
    }

    [AvaloniaTheory(DisplayName = "SIN-H003 | A total force becomes tractions that add up to that force")]
    [InlineData(1000, 0, 0, 1)]
    [InlineData(250, 1, 0, 0)]
    [Trait("TestId", "SIN-H003")]
    public void TotalForce(double force, double x, double y, double z)
    {
        using var bar = new Bar();
        var direction = new Vec3(x, y, z);
        var p = bar.Mesh;
        var bc = StudyRunner.Conditions(p, Study(bar, new Load { Kind = LoadKind.Force, Value = force, Direction = direction, Total = true, Faces = [bar.Ref(Vec3.UnitZ)] }), []);
        AssertPoint(direction * force, NetForce(p, bc), 1e-6, "Applied force (N)");
        Assert.All(bc.TracTri, t => AssertNear(Length, Enumerable.Range(0, 6).Select(k => bar.Node(p.Mesh.Boundary[6 * t + k]).Z).Average(), "Loaded triangles lie on the top face"));
    }

    [AvaloniaFact(DisplayName = "SIN-H004 | A per-face force applies its value on each picked face")]
    [Trait("TestId", "SIN-H004")]
    public void PerFaceForce()
    {
        using var bar = new Bar();
        var load = new Load { Kind = LoadKind.Force, Value = 100, Direction = Vec3.UnitX, Total = false, Faces = [bar.Ref(Vec3.UnitZ), bar.Ref(Vec3.UnitY)] };
        var bc = StudyRunner.Conditions(bar.Mesh, Study(bar, load), []);
        AssertPoint(new Vec3(200, 0, 0), NetForce(bar.Mesh, bc), 1e-6, "Two faces × 100 N");
    }

    [AvaloniaTheory(DisplayName = "SIN-H005 | Force along the normal and pressure both push into the loaded face")]
    [InlineData(LoadKind.Force, 300, 300)]
    [InlineData(LoadKind.Pressure, 2.5, 250)]
    [Trait("TestId", "SIN-H005")]
    public void LoadsIntoFace(LoadKind kind, double value, double expected)
    {
        using var bar = new Bar();
        var load = new Load { Kind = kind, Value = value, AlongNormal = true, Total = true, Faces = [bar.Ref(Vec3.UnitZ)] };
        var bc = StudyRunner.Conditions(bar.Mesh, Study(bar, load), []);
        // The 100 mm² top face points +Z, so "into the face" is -Z; pressure is MPa × mm² = N.
        AssertPoint(new Vec3(0, 0, -expected), NetForce(bar.Mesh, bc), 1e-6, "Net force into the top face (N)");
    }

    [AvaloniaFact(DisplayName = "SIN-H006 | A torque becomes tractions with no net force and the requested moment")]
    [Trait("TestId", "SIN-H006")]
    public void TorqueMoment()
    {
        using var bar = new Bar();
        var p = bar.Mesh;
        var bc = StudyRunner.Conditions(p, Study(bar, new Load { Kind = LoadKind.Torque, Value = 5000, Faces = [bar.Ref(Vec3.UnitZ)] }), []);
        AssertPoint(Vec3.Zero, NetForce(p, bc), 1e-6, "Net force of a pure torque (N)");
        var moment = Enumerable.Range(0, bc.TracTri.Length).Aggregate(Vec3.Zero, (sum, i) =>
        {
            int t = bc.TracTri[i];
            var centroid = (bar.Node(p.Mesh.Boundary[6 * t]) + bar.Node(p.Mesh.Boundary[6 * t + 1]) + bar.Node(p.Mesh.Boundary[6 * t + 2])) / 3;
            var force = new Vec3(bc.TracVec[3 * i], bc.TracVec[3 * i + 1], bc.TracVec[3 * i + 2]) * p.BoundaryArea[t];
            return sum + Vec3.Cross(centroid - new Vec3(Side / 2, Side / 2, Length), force);
        });
        AssertPoint(new Vec3(0, 0, 5000), moment, 1e-6, "Moment about the face normal (N·mm)");
    }

    [AvaloniaFact(DisplayName = "SIN-H007 | Gravity is passed as an acceleration of g × 9810 mm/s² along its direction")]
    [Trait("TestId", "SIN-H007")]
    public void GravityAcceleration()
    {
        using var bar = new Bar();
        var bc = StudyRunner.Conditions(bar.Mesh, Study(bar, new Load { Kind = LoadKind.Gravity, Value = 2, Direction = -Vec3.UnitZ }), []);
        Assert.Equal([0, 0, -19620], bc.Gravity!);
        Assert.Empty(bc.TracTri);
    }

    [AvaloniaFact(DisplayName = "SIN-H008 | Supports on another body are reported and nothing is sent to the solver")]
    [Trait("TestId", "SIN-H008")]
    public void SupportsOffTheBody()
    {
        using var bar = new Bar();
        var other = CreateBoxBody(bar.Session, new Vec3(50, 0, 0), 5, 5, 5);
        var study = Study(bar, new Load { Kind = LoadKind.Force, Value = 1, Direction = Vec3.UnitZ, Total = true, Faces = [bar.Ref(Vec3.UnitZ)] });
        study.Fixtures[0] = new Fixture { Name = "Elsewhere", Kind = FixtureKind.Fixed, Faces = [new FaceRef(other.Id, FaceFacing(other, Vec3.UnitZ).Index)] };
        var notes = new List<string>();
        var error = Assert.Throws<InvalidOperationException>(() => StudyRunner.Conditions(bar.Mesh, study, notes));
        Assert.Contains("not on any face", error.Message);
        Assert.Contains("Elsewhere: no face of this body is selected.", notes);
    }

    // ------------------------------------------------------------------ CalculiX input: natural frequencies

    [AvaloniaFact(DisplayName = "SIN-H009 | Natural-frequency input uses mm-N-s units, 10-node solids and locks the support nodes")]
    [Trait("TestId", "SIN-H009")]
    public void ModalInput()
    {
        using var bar = new Bar();
        var deck = CaeSolver.Input(bar.Mesh, new CaeSettings(CaeKind.Modal, [bar.Face(-Vec3.UnitZ)], [], Modes: 4));
        // 6061: E = 68.9 GPa = 68 900 MPa (N/mm²); ρ = 2.70 g/cm³ = 2.7e-9 t/mm³ (consistent with N, mm, s).
        Assert.Equal([68900, 0.33], Numbers(Assert.Single(Block(deck, "*ELASTIC"))));
        AssertClose(2.7e-9, Numbers(Assert.Single(Block(deck, "*DENSITY")))[0], 1e-21, "Density (t/mm³)");
        Assert.Equal(bar.Mesh.Mesh.TetCount, Block(deck, "*ELEMENT,TYPE=C3D10").Count);
        Assert.Equal(bar.Mesh.Mesh.NodeCount, Block(deck, "*NODE").Count);
        var locked = Block(deck, "*BOUNDARY").Select(line => line.Split(',')).ToList();
        Assert.All(locked, cells => Assert.Equal(["1", "3"], cells[1..]));
        Assert.Equal(bar.NodesWhere(p => Math.Abs(p.Z) < 1e-9), locked.Select(cells => int.Parse(cells[0]) - 1).ToHashSet());
        Assert.Equal("4", Assert.Single(Block(deck, "*FREQUENCY")));
    }

    // ------------------------------------------------------------------ CalculiX input: heat transfer

    [AvaloniaFact(DisplayName = "SIN-H010 | Steady thermal input converts properties to mm-J-kg-s and holds each face at its temperature")]
    [Trait("TestId", "SIN-H010")]
    public void SteadyThermalInput()
    {
        using var bar = new Bar();
        var deck = CaeSolver.Input(bar.Mesh, new CaeSettings(CaeKind.SteadyThermal, [bar.Face(-Vec3.UnitZ)], [bar.Face(Vec3.UnitZ)], 100, 20));
        // k = 167 W/(m·K) = 0.167 W/(mm·K); ρ = 2.70 g/cm³ = 2.7e-6 kg/mm³; c = 896 J/(kg·K) unchanged.
        AssertClose(0.167, Numbers(Assert.Single(Block(deck, "*CONDUCTIVITY")))[0], 1e-12, "Conductivity (W/mm·K)");
        AssertClose(2.7e-6, Numbers(Assert.Single(Block(deck, "*DENSITY")))[0], 1e-18, "Density (kg/mm³)");
        Assert.Equal(896, Numbers(Assert.Single(Block(deck, "*SPECIFIC HEAT")))[0]);
        Assert.Equal(bar.Mesh.Mesh.TetCount, Block(deck, "*ELEMENT,TYPE=DC3D10").Count);
        Assert.Contains("*HEAT TRANSFER,STEADY STATE", deck);
        var held = Block(deck, "*BOUNDARY,AMPLITUDE=HOLD").Select(Numbers).ToList();
        Assert.All(held, cells => Assert.Equal(11, cells[1]));
        Assert.Equal(bar.NodesWhere(p => Math.Abs(p.Z) < 1e-9), held.Where(c => c[3] == 100).Select(c => (int)c[0] - 1).ToHashSet());
        Assert.Equal(bar.NodesWhere(p => Math.Abs(p.Z - Length) < 1e-9), held.Where(c => c[3] == 20).Select(c => (int)c[0] - 1).ToHashSet());
    }

    [AvaloniaFact(DisplayName = "SIN-H011 | Heat input is spread as a flux that adds up to the power, on element faces of the heated face")]
    [Trait("TestId", "SIN-H011")]
    public void HeatFluxInput()
    {
        using var bar = new Bar();
        var p = bar.Mesh;
        var settings = new CaeSettings(CaeKind.SteadyThermal, [bar.Face(-Vec3.UnitZ)], [], 20, HeatFaces: [bar.Face(Vec3.UnitZ)], HeatPower: 10);
        var flux = Block(CaeSolver.Input(p, settings), "*DFLUX");
        double total = 0;
        foreach (var line in flux)
        {
            var cells = line.Split(',');
            int element = int.Parse(cells[0]) - 1, face = int.Parse(cells[1][1..]) - 1;
            var corners = CalculixFaces[face].Select(k => bar.Node(p.Mesh.Tets[10 * element + k])).ToArray();
            Assert.All(corners, c => AssertNear(Length, c.Z, "Flux face lies on the heated top face (mm)"));
            var area = Vec3.Cross(corners[1] - corners[0], corners[2] - corners[0]).Length / 2;
            total += double.Parse(cells[2], CultureInfo.InvariantCulture) * area;
        }
        AssertClose(10, total, 1e-6, "Heat input (W)");
    }

    [AvaloniaFact(DisplayName = "SIN-H012 | Cooling writes film coefficients in W/(mm²·K) and radiation with Stefan–Boltzmann in mm units")]
    [Trait("TestId", "SIN-H012")]
    public void CoolingInput()
    {
        using var bar = new Bar();
        var settings = new CaeSettings(CaeKind.SteadyThermal, [bar.Face(-Vec3.UnitZ)], [], 80, CoolFaces: [bar.Face(Vec3.UnitX)],
            FilmCoefficient: 50, Ambient: 25, Emissivity: 0.8);
        var deck = CaeSolver.Input(bar.Mesh, settings);
        Assert.All(Block(deck, "*FILM"), line => { var c = line.Split(','); Assert.StartsWith("F", c[1]); Assert.Equal([25, 50e-6], c[2..].Select(v => double.Parse(v, CultureInfo.InvariantCulture))); });
        Assert.All(Block(deck, "*RADIATE"), line => { var c = line.Split(','); Assert.StartsWith("R", c[1]); Assert.Equal([25, 0.8], c[2..].Select(v => double.Parse(v, CultureInfo.InvariantCulture))); });
        Assert.Contains("*PHYSICAL CONSTANTS,ABSOLUTE ZERO=-273.15,STEFAN BOLTZMANN=5.670374E-14", deck);
    }

    [AvaloniaFact(DisplayName = "SIN-H013 | Transient thermal input starts at the initial temperature and steps over the duration")]
    [Trait("TestId", "SIN-H013")]
    public void TransientInput()
    {
        using var bar = new Bar();
        var deck = CaeSolver.Input(bar.Mesh, new CaeSettings(CaeKind.TransientThermal, [bar.Face(-Vec3.UnitZ)], [], 100, InitialTemperature: 15, Duration: 200));
        Assert.Equal("ALLNODES,15", Assert.Single(Block(deck, "*INITIAL CONDITIONS,TYPE=TEMPERATURE")));
        // initial increment, total time, minimum and maximum increment
        Assert.Equal([10, 200, 0.002, 20], Numbers(Assert.Single(Block(deck, "*HEAT TRANSFER"))));
    }

    [AvaloniaTheory(DisplayName = "SIN-H014 | CalculiX input refuses a setup the solver could not solve")]
    [InlineData("overlap", "share mesh nodes")]
    [InlineData("no sink", "Heat needs somewhere to go")]
    [InlineData("no power", "Enter the heat power")]
    [InlineData("modal without support", "Pick boundary faces")]
    [Trait("TestId", "SIN-H014")]
    public void InputValidation(string setup, string message)
    {
        using var bar = new Bar();
        int top = bar.Face(Vec3.UnitZ), side = bar.Face(Vec3.UnitX);
        var settings = setup switch
        {
            "overlap" => new CaeSettings(CaeKind.SteadyThermal, [top], [side], 100, 20),
            "no sink" => new CaeSettings(CaeKind.SteadyThermal, [], [], HeatFaces: [top], HeatPower: 5),
            "no power" => new CaeSettings(CaeKind.SteadyThermal, [side], [], HeatFaces: [top], HeatPower: 0),
            _ => new CaeSettings(CaeKind.Modal, [], []),
        };
        var error = Assert.Throws<InvalidOperationException>(() => CaeSolver.Input(bar.Mesh, settings));
        Assert.Contains(message, error.Message);
    }

    // ------------------------------------------------------------------ CalculiX results

    [AvaloniaFact(DisplayName = "SIN-H015 | CalculiX temperature output is read back per node")]
    [Trait("TestId", "SIN-H015")]
    public void ParseTemperatures()
    {
        using var bar = new Bar();
        int nodes = bar.Mesh.Mesh.NodeCount;
        var text = " temperatures for set ALLNODES and time  0.1000000E+01\n\n" +
            string.Concat(Enumerable.Range(1, nodes).Select(n => $"{n,10}  {(20 + n * 0.5).ToString("E6", CultureInfo.InvariantCulture)}\n"));
        var outcome = CaeSolver.Parse(bar.Mesh, CaeKind.SteadyThermal, text, "folder");
        Assert.Equal(nodes, outcome.Temperatures.Length);
        AssertNear(20.5, outcome.Temperatures[0], "Node 1 (°C)");
        AssertNear(20 + nodes * 0.5, outcome.Temperatures[^1], "Last node (°C)");
    }

    [AvaloniaFact(DisplayName = "SIN-H016 | CalculiX eigenvalue output is read as frequencies in Hz with one mode shape each")]
    [Trait("TestId", "SIN-H016")]
    public void ParseFrequencies()
    {
        using var bar = new Bar();
        int nodes = bar.Mesh.Mesh.NodeCount;
        double[] hz = [123.5, 456.25];
        string Row(int mode, double f) => FormattableString.Invariant($"{mode,7}   {Math.Pow(2 * Math.PI * f, 2):E4}   {2 * Math.PI * f:E4}   {f:E4}   0.0000E+00\n");
        string Shape(int mode) => $" displacements (vx,vy,vz) for set ALLNODES and time  {mode}\n\n" +
            string.Concat(Enumerable.Range(1, nodes).Select(n => FormattableString.Invariant($"{n,10}  {mode * 0.001:E6}  0.000000E+00  0.000000E+00\n")));
        var text = "     E I G E N V A L U E   O U T P U T\n\n MODE NO    EIGENVALUE    FREQUENCY\n" + Row(1, hz[0]) + Row(2, hz[1]) +
            "\n     P A R T I C I P A T I O N   F A C T O R S\n" + Shape(1) + Shape(2);
        var outcome = CaeSolver.Parse(bar.Mesh, CaeKind.Modal, text, "folder");
        Assert.Equal(hz, outcome.Frequencies);
        Assert.Equal(2, outcome.Modes.Count);
        AssertNear(0.002, outcome.Modes[1][0], "Mode 2, node 1, X");
    }

    [AvaloniaFact(DisplayName = "SIN-H017 | Incomplete CalculiX output is rejected rather than read as zeros")]
    [Trait("TestId", "SIN-H017")]
    public void ParseRejectsIncompleteOutput()
    {
        using var bar = new Bar();
        var text = " temperatures for set ALLNODES and time  0.1000000E+01\n\n         1  2.000000E+01\n         2  2.000000E+01\n";
        Assert.Throws<InvalidDataException>(() => CaeSolver.Parse(bar.Mesh, CaeKind.SteadyThermal, text, "folder"));
        Assert.Throws<InvalidDataException>(() => CaeSolver.Parse(bar.Mesh, CaeKind.SteadyThermal, "", "folder"));
    }

    // ------------------------------------------------------------------ CalculiX input: several bonded bodies

    /// <summary>A 6061 block on a steel (A36) block of the same footprint, touching at Z = 20.</summary>
    static (CadSession Session, SceneObject Lower, SceneObject Upper, StudyRunner.Prepared Mesh) Stack()
    {
        var session = new CadSession();
        var lower = CreateBoxBody(session, Vec3.Zero, 20, 20, 20);
        var upper = CreateBoxBody(session, new Vec3(0, 0, 20), 20, 20, 20);
        session.Update(lower.Id, o => o.WithMaterial(Materials.Find("a36")!));
        session.Update(upper.Id, o => o.WithMaterial(Materials.Find("6061")!));
        lower = session.Find(lower.Id)!; upper = session.Find(upper.Id)!;
        return (session, lower, upper, CaeAssembly.Mesh([lower, upper], 6));
    }

    [AvaloniaFact(DisplayName = "SIN-H018 | Bonded-body static input gives each body its material, ties the contact and loads the right total")]
    [Trait("TestId", "SIN-H018")]
    public void BondedStaticInput()
    {
        var (_, lower, upper, p) = Stack();
        using var mesh = p.Mesh;
        var study = new StudyDef { BodyId = lower.Id };
        study.OtherBodies.Add(upper.Id);
        study.Fixtures.Add(new Fixture { Name = "Floor", Kind = FixtureKind.Fixed, Faces = [new FaceRef(lower.Id, FaceFacing(lower, -Vec3.UnitZ).Index)] });
        study.Loads.Add(new Load { Name = "Push", Kind = LoadKind.Force, Value = 1000, Direction = -Vec3.UnitZ, Total = true, Faces = [new FaceRef(upper.Id, FaceFacing(upper, Vec3.UnitZ).Index)] });
        var bc = StudyRunner.Conditions(p, study, []);
        var deck = CaeAssembly.StaticInput(p, bc, out var net);

        Assert.Single(p.Bonds);
        Assert.Contains("*TIE,NAME=BOND1", deck);
        // Body order follows the study: steel (200 GPa) first, aluminium (68.9 GPa) second.
        var lines = deck.Split('\n').Select(l => l.Trim()).ToList();
        var elastic = Enumerable.Range(0, lines.Count).Where(i => lines[i] == "*ELASTIC").Select(i => Numbers(lines[i + 1])[0]);
        Assert.Equal([200000, 68900], elastic);

        var loads = Block(deck, "*CLOAD").Select(Numbers).ToList();
        AssertClose(-1000, loads.Where(c => c[1] == 3).Sum(c => c[2]), 1e-6, "Z nodal loads (N)");
        AssertClose(0, loads.Where(c => c[1] != 3).Sum(c => Math.Abs(c[2])), 1e-9, "No sideways load (N)");
        AssertClose(1000, net.Length, 1e-6, "Net applied load (N)");

        var locked = Block(deck, "*BOUNDARY").Select(Numbers).ToList();
        Assert.Equal(bc.FixNode.Length * 3, locked.Count);
        Assert.All(locked.Select(c => (int)c[0] - 1), n => AssertNear(0, mesh.Nodes[3 * n + 2], "Locked node height (mm)"));
    }

    [AvaloniaFact(DisplayName = "SIN-H019 | Bonded-body gravity is applied to every body and counted in the load to balance")]
    [Trait("TestId", "SIN-H019")]
    public void BondedGravityInput()
    {
        var (_, lower, upper, p) = Stack();
        using var mesh = p.Mesh;
        var study = new StudyDef { BodyId = lower.Id };
        study.OtherBodies.Add(upper.Id);
        study.Fixtures.Add(new Fixture { Name = "Floor", Kind = FixtureKind.Fixed, Faces = [new FaceRef(lower.Id, FaceFacing(lower, -Vec3.UnitZ).Index)] });
        study.Loads.Add(new Load { Kind = LoadKind.Gravity, Value = 1, Direction = -Vec3.UnitZ });
        var deck = CaeAssembly.StaticInput(p, StudyRunner.Conditions(p, study, []), out var net);
        Assert.Equal(["BODY0,GRAV,9810,0,0,-1", "BODY1,GRAV,9810,0,0,-1"], Block(deck, "*DLOAD"));
        // Weight = volume (mm³) × density (t/mm³) × g (mm/s²) = N: 8000 mm³ each at 7.85 and 2.70 g/cm³.
        AssertClose(8000 * (7.85 + 2.70) * 1e-9 * 9810, net.Length, 1e-9, "Weight to balance (N)");
    }

    [AvaloniaFact(DisplayName = "SIN-H020 | CalculiX stress output is read from the fixed-width result file")]
    [Trait("TestId", "SIN-H020")]
    public void ReadStressFile()
    {
        var file = Path.GetTempFileName();
        try
        {
            string Row(int node, params double[] s) => " -1" + node.ToString().PadLeft(10) + string.Concat(s.Select(v => v.ToString("0.00000E+00", CultureInfo.InvariantCulture).PadLeft(12)));
            File.WriteAllLines(file,
            [
                "    1PSTEP", " -4  DISP        4    1", " -1         1 1.00000E-01 0.00000E+00 0.00000E+00", " -3",
                " -4  STRESS      6    1", Row(1, 10, -2, 0, 1.5, 0, 0), Row(2, 0, 0, 30, 0, 0, -4), " -3",
            ]);
            var stress = Prism.Simulation.CaeAssembly.ReadStress(file, 2);
            Assert.Equal([10, -2, 0, 1.5, 0, 0, 0, 0, 30, 0, 0, -4], stress);
        }
        finally { File.Delete(file); }
    }
}
