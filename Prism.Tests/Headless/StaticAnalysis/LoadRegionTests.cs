using System.Text.Json;
using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;
using Xunit;
using Xunit.Abstractions;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.StaticAnalysis;

/// <summary>
/// A force or pressure on a circle or rectangle of one face (owner request 2026-11-16: a way to apply a vector on a certain area of a face).
/// Geometry of the region, what the solver is given, the command, saving, and a real solve against beam mechanics.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cae/static-analysis/README.md">Static analysis guide.</seealso>
[Trait("Feature", "StaticAnalysis")]
[Trait("Layer", "Headless")]
public class LoadRegionTests
{
    readonly ITestOutputHelper output;
    public LoadRegionTests(ITestOutputHelper output) => this.output = output;

    /// <summary>A 100 x 100 x 10 mm aluminium plate on Z = 0 with a fixed base, meshed at 4 mm: a top face big enough to place areas on.</summary>
    sealed class Plate : IDisposable
    {
        public CadSession Session { get; } = new();
        public SceneObject Body { get; }
        public StudyRunner.Prepared Mesh { get; }
        public FaceRef Top { get; }
        public Plate()
        {
            var body = CreateBoxBody(Session, Vec3.Zero, 100, 100, 10);
            Session.Update(body.Id, obj => obj.WithMaterial(Materials.Find("6061")!));
            Body = Session.Find(body.Id)!;
            Mesh = StudyRunner.Mesh(Body, 4);
            Top = new FaceRef(Body.Id, FaceFacing(Body, Vec3.UnitZ).Index);
        }
        public StudyDef Study(params Load[] loads)
        {
            var study = new StudyDef { BodyId = Body.Id };
            study.Fixtures.Add(new Fixture { Name = "Base", Kind = FixtureKind.Fixed, Faces = [new FaceRef(Body.Id, FaceFacing(Body, -Vec3.UnitZ).Index)] });
            study.Loads.AddRange(loads);
            return study;
        }
        public Vec3 Node(int n) => new(Mesh.Mesh.Nodes[3 * n], Mesh.Mesh.Nodes[3 * n + 1], Mesh.Mesh.Nodes[3 * n + 2]);
        public Vec3 Centroid(int t) => (Node(Mesh.Mesh.Boundary[6 * t]) + Node(Mesh.Mesh.Boundary[6 * t + 1]) + Node(Mesh.Mesh.Boundary[6 * t + 2])) / 3;
        public void Dispose() => Mesh.Mesh.Dispose();
    }

    static Vec3 NetForce(StudyRunner.Prepared p, StudyRunner.Boundary bc) =>
        Enumerable.Range(0, bc.TracTri.Length).Aggregate(Vec3.Zero, (sum, i) =>
            sum + new Vec3(bc.TracVec[3 * i], bc.TracVec[3 * i + 1], bc.TracVec[3 * i + 2]) * p.BoundaryArea[bc.TracTri[i]]);

    static double LoadedArea(StudyRunner.Prepared p, StudyRunner.Boundary bc) => bc.TracTri.Sum(t => p.BoundaryArea[t]);

    static Load Circle(Plate plate, double across, double up, double radius, double value = 500, LoadKind kind = LoadKind.Force)
    {
        // the plate's top face centre is (50, 50, 10); across is +X and up is +Y on a top face
        var load = new Load { Kind = kind, Value = value, Direction = -Vec3.UnitZ, Total = true, Faces = [plate.Top], Region = LoadRegionShape.Circle, RegionCentre = new Vec3(50 + across, 50 + up, 10), RegionU = Vec3.UnitX, RegionV = Vec3.UnitY, RegionA = radius, RegionB = radius };
        return load;
    }

    // ------------------------------------------------------------------ the region itself

    [AvaloniaFact(DisplayName = "REG-H001 | A circle and a turned rectangle contain exactly the points inside them, and know their area and outline")]
    [Trait("TestId", "REG-H001")]
    public void RegionGeometry()
    {
        var circle = new Load { Region = LoadRegionShape.Circle, RegionCentre = new Vec3(10, 20, 5), RegionU = Vec3.UnitX, RegionV = Vec3.UnitY, RegionA = 3, RegionB = 3 };
        Assert.True(circle.InRegion(new Vec3(12.9, 20, 5)));
        Assert.False(circle.InRegion(new Vec3(13.1, 20, 5)));
        Assert.True(circle.InRegion(new Vec3(10 + 2.1, 20 + 2.1, 5)));              // 2.97 from the centre
        Assert.False(circle.InRegion(new Vec3(10 + 2.2, 20 + 2.2, 5)));             // 3.11 from the centre
        AssertNear(Math.PI * 9, circle.RegionArea, "Circle area (mm²)");
        Assert.All(circle.RegionOutline(), p => AssertNear(3, p.DistanceTo(circle.RegionCentre), "Outline radius (mm)"));

        // a 20 x 10 rectangle turned 90 degrees: its long side now runs along Y
        var rect = new Load { Region = LoadRegionShape.Rectangle, RegionCentre = Vec3.Zero, RegionU = Vec3.UnitY, RegionV = -Vec3.UnitX, RegionA = 20, RegionB = 10 };
        Assert.True(rect.InRegion(new Vec3(4.9, 9.9, 0)));
        Assert.False(rect.InRegion(new Vec3(5.1, 0, 0)));
        Assert.False(rect.InRegion(new Vec3(0, 10.1, 0)));
        AssertNear(200, rect.RegionArea, "Rectangle area (mm²)");
        Assert.Equal(4, rect.RegionOutline().Count);
        Assert.True(new Load().InRegion(new Vec3(1e6, 1e6, 1e6)), "a whole-face load contains everything");
    }

    [AvaloniaFact(DisplayName = "REG-H002 | A load with a region survives saving as JSON, and one saved before regions existed opens as the whole face")]
    [Trait("TestId", "REG-H002")]
    public void RegionIsSaved()
    {
        var load = new Load { Kind = LoadKind.Pressure, Value = 2, Region = LoadRegionShape.Rectangle, RegionCentre = new Vec3(1, 2, 3), RegionU = Vec3.UnitY, RegionV = Vec3.UnitX, RegionA = 7, RegionB = 4 };
        var back = JsonSerializer.Deserialize<Load>(JsonSerializer.Serialize(load))!;
        Assert.Equal(LoadRegionShape.Rectangle, back.Region);
        AssertPoint(new Vec3(1, 2, 3), back.RegionCentre, 1e-12, "Centre");
        AssertPoint(Vec3.UnitY, back.RegionU, 1e-12, "U");
        AssertNear(7, back.RegionA, "Width"); AssertNear(4, back.RegionB, "Height");

        var old = JsonSerializer.Deserialize<Load>("{\"Kind\":0,\"Value\":100,\"Total\":true,\"AlongNormal\":true}")!;      // a load as an earlier build saved it
        Assert.Equal(LoadRegionShape.WholeFace, old.Region);
        Assert.Equal(100, old.Value);
    }

    // ------------------------------------------------------------------ what the solver is given

    [AvaloniaTheory(DisplayName = "REG-H003 | A force on a circle adds up to exactly the force, on mesh faces that all lie inside it, covering about its area")]
    [InlineData(0, 0, 20, 500)]
    [InlineData(15, -10, 12, 300)]
    [Trait("TestId", "REG-H003")]
    public void CircleLoadsOnlyItsArea(double across, double up, double radius, double force)
    {
        using var plate = new Plate();
        var load = Circle(plate, across, up, radius, force);
        var notes = new List<string>();
        var bc = StudyRunner.Conditions(plate.Mesh, plate.Study(load), notes);
        AssertPoint(new Vec3(0, 0, -force), NetForce(plate.Mesh, bc), 1e-6, "Applied force (N)");
        Assert.NotEmpty(bc.TracTri);
        Assert.All(bc.TracTri, t => Assert.True(load.InRegion(plate.Centroid(t)), "a loaded mesh face lies outside the circle"));
        double area = LoadedArea(plate.Mesh, bc);
        Assert.InRange(area, 0.85 * Math.PI * radius * radius, 1.15 * Math.PI * radius * radius);      // a 4 mm mesh follows a circle only roughly
        output.WriteLine($"Circle r={radius}: asked {Math.PI * radius * radius:0.#} mm², mesh loads {area:0.#} mm²; notes: {string.Join(" | ", notes)}");
    }

    [AvaloniaFact(DisplayName = "REG-H004 | A force on a turned rectangle adds up to the force and loads only mesh faces inside it")]
    [Trait("TestId", "REG-H004")]
    public void TurnedRectangle()
    {
        using var plate = new Plate();
        double t = 30 * Math.PI / 180;
        var load = new Load { Kind = LoadKind.Force, Value = 400, Direction = new Vec3(0.6, 0, -0.8), Total = true, Faces = [plate.Top], Region = LoadRegionShape.Rectangle, RegionCentre = new Vec3(40, 55, 10),
            RegionU = new Vec3(Math.Cos(t), Math.Sin(t), 0), RegionV = new Vec3(-Math.Sin(t), Math.Cos(t), 0), RegionA = 40, RegionB = 20 };
        var bc = StudyRunner.Conditions(plate.Mesh, plate.Study(load), []);
        AssertPoint(new Vec3(240, 0, -320), NetForce(plate.Mesh, bc), 1e-6, "Applied force vector (N)");
        Assert.All(bc.TracTri, tri => Assert.True(load.InRegion(plate.Centroid(tri))));
        Assert.InRange(LoadedArea(plate.Mesh, bc), 0.85 * 800, 1.15 * 800);
    }

    [AvaloniaFact(DisplayName = "REG-H005 | A pressure on a circle pushes with that pressure over the mesh faces it covers")]
    [Trait("TestId", "REG-H005")]
    public void PressureOnACircle()
    {
        using var plate = new Plate();
        var load = Circle(plate, 0, 0, 20, value: 2, kind: LoadKind.Pressure);
        var bc = StudyRunner.Conditions(plate.Mesh, plate.Study(load), []);
        double area = LoadedArea(plate.Mesh, bc);
        AssertPoint(new Vec3(0, 0, -2 * area), NetForce(plate.Mesh, bc), 1e-6, "Pressure force = 2 MPa x loaded area (N)");
        Assert.All(bc.TracTri, t => Assert.True(load.InRegion(plate.Centroid(t))));
    }

    [AvaloniaFact(DisplayName = "REG-H006 | A region partly off the face loads only the part on it, says so, and keeps the total force")]
    [Trait("TestId", "REG-H006")]
    public void RegionHangingOverTheEdge()
    {
        using var plate = new Plate();
        var load = Circle(plate, 45, 0, 20, 500);                      // centre 5 mm in from the +X edge, so about a third of the circle (a segment 15 mm deep) is off the face
        var notes = new List<string>();
        var bc = StudyRunner.Conditions(plate.Mesh, plate.Study(load), notes);
        AssertPoint(new Vec3(0, 0, -500), NetForce(plate.Mesh, bc), 1e-6, "Applied force (N)");
        Assert.True(LoadedArea(plate.Mesh, bc) < 0.8 * Math.PI * 400, "only the part on the face is loaded");
        Assert.Contains(notes, n => n.Contains("loads") && n.Contains("region"));
    }

    [AvaloniaFact(DisplayName = "REG-H007 | A region too small for the mesh is reported and loads nothing instead of failing quietly")]
    [Trait("TestId", "REG-H007")]
    public void RegionTooSmallForTheMesh()
    {
        using var plate = new Plate();
        var notes = new List<string>();
        var error = Assert.Throws<InvalidOperationException>(() => StudyRunner.Conditions(plate.Mesh, plate.Study(Circle(plate, 0, 0, 0.2)), notes));
        Assert.Contains("not on any face", error.Message);
        Assert.Contains(notes, n => n.Contains("too small"));
    }

    // ------------------------------------------------------------------ the command

    sealed class Setup : IDisposable
    {
        public CadSession Session = new();
        public SceneObject Body;
        public Setup()
        {
            var body = CreateBoxBody(Session, Vec3.Zero, 100, 100, 10);
            Session.Update(body.Id, obj => obj.WithMaterial(Materials.Find("6061")!));
            Body = Session.Find(body.Id)!;
            Session.NewStudy(Body);
        }
        public SelItem Face(Vec3 normal) => FaceFacing(Body, normal);
        public void Dispose() { }
    }

    static string Line(Command command, string label) => command.Results.Single(r => r.Label == label).Value;

    [AvaloniaFact(DisplayName = "REG-H008 | The Force command places a circle by offsets from the face centre, reports the area, and offers handles to drag")]
    [Trait("TestId", "REG-H008")]
    public void CommandPlacesACircle()
    {
        using var s = new Setup();
        var command = Begin(s.Session, () => new LoadCommand(LoadKind.Force));
        Pick(command, "Faces to load", s.Face(Vec3.UnitZ));
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Apply to", "A circle on the face");
            SetNumber(command, "Centre across the face", 15); SetNumber(command, "Centre up the face", -10);
            SetNumber(command, "Circle radius", 12); SetNumber(command, "Force", 300);
        });
        Assert.NotNull(command.TryBuild());
        Assert.Contains("452.39", Line(command, "Loaded area"));                                    // pi x 12²
        Assert.Contains("0.663", Line(command, "Average pressure on the area"));                   // 300 N / 452.39 mm²
        Assert.Equal(3, command.Handles.Count);                                                      // across, up and radius arrows
        s.Session.CommitCommand();
        var load = Assert.Single(s.Session.ActiveStudy!.Loads);
        Assert.Equal(LoadRegionShape.Circle, load.Region);
        AssertPoint(new Vec3(65, 40, 10), load.RegionCentre, 1e-6, "Region centre on the top face");
        AssertNear(12, load.RegionA, "Radius");
        Assert.Equal("a 24 mm circle on a face", MainViewModelText(load));
    }

    static string MainViewModelText(Load load) => Prism.ViewModels.MainViewModel.LoadWhere(load);

    [AvaloniaFact(DisplayName = "REG-H009 | A rectangle can be turned, a pressure area reports its force, and an area needs exactly one flat face")]
    [Trait("TestId", "REG-H009")]
    public void RectangleAndRefusals()
    {
        using var s = new Setup();
        var pressure = Begin(s.Session, () => new LoadCommand(LoadKind.Pressure));
        Pick(pressure, "Faces to load", s.Face(Vec3.UnitZ));
        pressure.ConfigureInputs(() =>
        {
            SetChoice(pressure, "Apply to", "A rectangle on the face");
            SetNumber(pressure, "Rectangle width", 20); SetNumber(pressure, "Rectangle height", 10); SetNumber(pressure, "Rectangle turn", 90);
            SetNumber(pressure, "Pressure", 3);
        });
        Assert.NotNull(pressure.TryBuild());
        Assert.Contains("200", Line(pressure, "Loaded area"));
        Assert.Contains("600", Line(pressure, "Force from the pressure"));                          // 3 MPa x 200 mm²
        Assert.Equal(4, pressure.Handles.Count);                                                      // across, up, width, height
        s.Session.CommitCommand();
        var load = s.Session.ActiveStudy!.Loads.Single();
        AssertPoint(Vec3.UnitY, load.RegionU, 1e-9, "A rectangle turned 90 degrees has its width along Y");

        var two = Begin(s.Session, () => new LoadCommand(LoadKind.Force));
        Pick(two, "Faces to load", s.Face(Vec3.UnitZ), s.Face(Vec3.UnitY));
        two.ConfigureInputs(() => SetChoice(two, "Apply to", "A circle on the face"));
        Assert.Null(two.TryBuild());
        Assert.Contains("exactly one face", two.Error);

        var whole = Begin(s.Session, () => new LoadCommand(LoadKind.Force));
        Pick(whole, "Faces to load", s.Face(Vec3.UnitZ), s.Face(Vec3.UnitY));
        Assert.NotNull(whole.TryBuild());                                                             // two faces are fine for the whole face, as always
    }

    [AvaloniaFact(DisplayName = "REG-H010 | A circle on a curved face is refused and nothing is added")]
    [Trait("TestId", "REG-H010")]
    public void CurvedFaceIsRefused()
    {
        var session = new CadSession();
        using var cylinder = Occ.Cylinder(20, 50);
        var body = session.AddObject(Occ.Translate(cylinder, Vec3.Zero), "Round", ObjKind.Solid);
        session.Update(body.Id, obj => obj.WithMaterial(Materials.Find("6061")!));
        body = session.Find(body.Id)!;
        session.NewStudy(body);
        int side = Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(i => Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, i)).Type != "Plane");
        var command = Begin(session, () => new LoadCommand(LoadKind.Force));
        Pick(command, "Faces to load", new SelItem(body.Id, SelKind.Face, side));
        command.ConfigureInputs(() => SetChoice(command, "Apply to", "A circle on the face"));
        Assert.Null(command.TryBuild());
        Assert.Contains("flat faces", command.Error);
        Assert.Empty(session.ActiveStudy!.Loads);
    }

    // ------------------------------------------------------------------ a real solve against beam mechanics

    [AvaloniaFact(DisplayName = "REG-H011 | 1000 N pulling on the outer half of a bar's end bends it as an offset force: slope = F e L / (E I)")]
    [Trait("TestId", "REG-H011")]
    public void OffsetPatchBendsTheBar()
    {
        // 10 x 10 x 100 mm bar, E = 68,900 MPa, fixed base. +1000 N over the half of the top face with Y from 5 to 10: its centre is 2.5 mm off the bar's axis,
        // so the bar is pulled by 1000 N (extension F L / EA) and bent by M = 1000 x 2.5 = 2500 N·mm about X (end slope M L / EI). Top-face height then rises with Y.
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 10, 10, 100);
        session.Update(body.Id, obj => obj.WithMaterial(Materials.Find("6061")!));
        body = session.Find(body.Id)!;
        var study = new StudyDef { BodyId = body.Id, ElementSize = 2.5 };
        study.Fixtures.Add(new Fixture { Kind = FixtureKind.Fixed, Faces = [new FaceRef(body.Id, FaceFacing(body, -Vec3.UnitZ).Index)] });
        study.Loads.Add(new Load
        {
            Kind = LoadKind.Force, Value = 1000, Direction = Vec3.UnitZ, Total = true, Faces = [new FaceRef(body.Id, FaceFacing(body, Vec3.UnitZ).Index)],
            Region = LoadRegionShape.Rectangle, RegionCentre = new Vec3(5, 7.5, 100), RegionU = Vec3.UnitX, RegionV = Vec3.UnitY, RegionA = 10, RegionB = 5,
        });
        var result = StudyRunner.Run(session, study);
        using var mesh = result.Mesh;

        Assert.InRange(Math.Abs(result.Solution.Reaction.Z + 1000), 0, 1);                      // the support still balances the whole 1000 N
        var top = Enumerable.Range(0, mesh.NodeCount).Where(n => Math.Abs(mesh.Nodes[3 * n + 2] - 100) < 1e-6).ToArray();
        double meanY = top.Average(n => mesh.Nodes[3 * n + 1]), meanZ = top.Average(n => result.Solution.Displacement[3 * n + 2]);
        double slope = top.Sum(n => (mesh.Nodes[3 * n + 1] - meanY) * (result.Solution.Displacement[3 * n + 2] - meanZ)) / top.Sum(n => Math.Pow(mesh.Nodes[3 * n + 1] - meanY, 2));

        const double e = 68900, i = 10.0 * 10 * 10 * 10 / 12, a = 100, length = 100;
        double axial = 1000 * length / (e * a), expectedSlope = 1000 * 2.5 * length / (e * i);
        output.WriteLine($"Offset patch: mean extension expected {axial:G6} mm, measured {meanZ:G6} mm; slope of top-face height along Y expected {expectedSlope:G6}, measured {slope:G6}.");
        Assert.InRange(meanZ / axial, 0.95, 1.05);
        Assert.InRange(slope / expectedSlope, 0.95, 1.05);                                         // 1.1% apart on one 2.5 mm mesh; 5% allowed, no convergence claim
    }
}
