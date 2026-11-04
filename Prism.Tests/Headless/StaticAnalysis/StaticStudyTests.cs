using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;
using Xunit;
using Xunit.Abstractions;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.StaticAnalysis;

/// <summary>Actual single-body static solves compared with axial-bar mechanics and equilibrium.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/tabs/static-analysis/README.md">Living structural study specification, benchmark assumptions and tolerances.</seealso>
[Trait("Layer", "Headless")]
[Trait("Feature", "StaticAnalysis")]
public class StaticStudyTests
{
    readonly ITestOutputHelper output;
    public StaticStudyTests(ITestOutputHelper output) => this.output = output;

    static (CadSession Session, SceneObject Body, StudyDef Study) CreateAxialBar(bool assignMaterial = true)
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, minimum: Vec3.Zero, width: 10, depth: 10, height: 100);
        if (assignMaterial)
        {
            session.Update(body.Id, obj => obj.WithMaterial(Materials.Find("6061")!));
            body = session.Find(body.Id)!;
        }
        int bottom = Enumerable.Range(0, body.Shape.Count(SubKind.Face))
            .MinBy(index => body.Shape.Sub(SubKind.Face, index).Props().Center.Z);
        int top = Enumerable.Range(0, body.Shape.Count(SubKind.Face))
            .MaxBy(index => body.Shape.Sub(SubKind.Face, index).Props().Center.Z);
        var study = new StudyDef { BodyId = body.Id, ElementSize = 5 };
        study.Fixtures.Add(new Fixture { Kind = FixtureKind.Fixed, Faces = [new FaceRef(body.Id, bottom)] });
        study.Loads.Add(new Load
        {
            Kind = LoadKind.Force, Value = 1000, Direction = Vec3.UnitZ, Total = true,
            Faces = [new FaceRef(body.Id, top)]
        });
        return (session, body, study);
    }

    static double MeanTipDisplacement(StudyResults result)
    {
        var topNodes = Enumerable.Range(0, result.Mesh.NodeCount)
            .Where(node => Math.Abs(result.Mesh.Nodes[3 * node + 2] - 100) < 1e-6).ToArray();
        Assert.NotEmpty(topNodes);
        return topNodes.Average(node => result.Solution.Displacement[3 * node + 2]);
    }

    static void AssertRelative(double expected, double actual, double tolerance, string quantity) =>
        Assert.True(double.IsFinite(actual) && Math.Abs(actual - expected) <= Math.Abs(expected) * tolerance,
            $"{quantity}: expected {expected:G12}, actual {actual:G12}, relative tolerance {tolerance:P1}.");

    [AvaloniaFact(DisplayName = "STA-H001 | A 1000 N axial bar agrees with FL/EA, F/A and support equilibrium")]
    [Trait("TestId", "STA-H001")]
    public void AxialTension_RealSolver_MatchesIndependentDisplacementStressAndReaction()
    {
        // Arrange: A=100 mm², L=100 mm, E=68,900 MPa, fixed lower end, +1000 N at upper end.
        var (session, body, study) = CreateAxialBar();
        AssertNear(68.9, body.Material!.Youngs, "Benchmark modulus (GPa)");

        var result = StudyRunner.Run(session, study);
        using var mesh = result.Mesh;

        // Assert: use mechanics independently of the solver's result summaries.
        const double expectedExtension = 1000.0 * 100 / (68900 * 100);
        AssertRelative(expectedExtension, MeanTipDisplacement(result), 0.05, "Mean end extension (mm)");
        var interiorNodes = Enumerable.Range(0, mesh.NodeCount)
            .Where(node => mesh.Nodes[3 * node + 2] > 40 && mesh.Nodes[3 * node + 2] < 60).ToArray();
        Assert.NotEmpty(interiorNodes);
        output.WriteLine($"Axial bar: expected extension {expectedExtension:G12} mm; measured {MeanTipDisplacement(result):G12} mm.");
        output.WriteLine($"Expected interior stress 10 MPa; measured {interiorNodes.Average(node => result.Solution.VonMises[node]):G12} MPa.");
        output.WriteLine($"Expected reaction Z -1000 N; measured {result.Solution.Reaction.Z:G12} N; mesh {mesh.NodeCount} nodes / {mesh.TetCount} tetrahedra.");
        AssertRelative(10, interiorNodes.Average(node => result.Solution.VonMises[node]), 0.05,
            "Mean interior von Mises stress (MPa)");
        Assert.InRange(Math.Abs(result.Solution.Reaction.Z + 1000), 0, 1);
        Assert.InRange(Math.Abs(result.Solution.Reaction.X), 0, 1);
        Assert.InRange(Math.Abs(result.Solution.Reaction.Y), 0, 1);
        Assert.All(result.Solution.Displacement, value => Assert.True(double.IsFinite(value)));
        Assert.All(result.Solution.VonMises, value => Assert.True(double.IsFinite(value)));
        Assert.Equal(mesh.NodeCount, result.SafetyFactor.Length);
        Assert.True(mesh.TetCount > 0 && mesh.NodeCount > 0);
    }

    [AvaloniaFact(DisplayName = "STA-H002 | Doubling the force on the same mesh doubles displacement and stress")]
    [Trait("TestId", "STA-H002")]
    public void LinearStaticSolve_DoubleLoadOnSameMesh_DoublesResponse()
    {
        var (session, _, study) = CreateAxialBar();
        var prepared = StudyRunner.Mesh(session, study);
        using var mesh = prepared.Mesh;

        var first = StudyRunner.Run(session, study, prepared);
        study.Loads.Single().Value = 2000;
        var doubled = StudyRunner.Run(session, study, prepared);

        AssertRelative(2 * MeanTipDisplacement(first), MeanTipDisplacement(doubled), 1e-6,
            "Linear end-displacement scaling");
        AssertRelative(2 * first.MaxVonMises, doubled.MaxVonMises, 1e-6, "Linear stress scaling");
        Assert.InRange(Math.Abs(doubled.Solution.Reaction.Z + 2000), 0, 1);
    }

    [AvaloniaFact(DisplayName = "STA-H003 | A study without fixtures is rejected before solving")]
    [Trait("TestId", "STA-H003")]
    public void MissingFixtures_ReportsSetupErrorAndPreservesBody()
    {
        var (session, body, study) = CreateAxialBar();
        study.Fixtures.Clear();

        var error = Assert.Throws<InvalidOperationException>(() => StudyRunner.Run(session, study));

        Assert.Contains("fixture", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(body.Id, Assert.Single(session.Objects).Id);
        AssertNear(10000, body.Shape.Props().Volume, "Unchanged bar volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "STA-H004 | A 10 MPa end pressure compresses the bar and balances 1000 N")]
    [Trait("TestId", "STA-H004")]
    public void EndPressure_RealSolver_HasExpectedCompressionAndReaction()
    {
        var (session, _, study) = CreateAxialBar();
        study.Loads.Single().Kind = LoadKind.Pressure;
        study.Loads.Single().Value = 10; // 10 N/mm² × 100 mm² = 1000 N inward.

        var result = StudyRunner.Run(session, study);
        using var mesh = result.Mesh;

        const double expectedCompression = -1000.0 * 100 / (68900 * 100);
        AssertRelative(expectedCompression, MeanTipDisplacement(result), 0.05, "Mean end compression (mm)");
        Assert.InRange(Math.Abs(result.Solution.Reaction.Z - 1000), 0, 1);
    }

    [AvaloniaFact(DisplayName = "STA-H005 | A structural study requires an assigned material")]
    [Trait("TestId", "STA-H005")]
    public void MissingMaterial_ReportsSetupErrorBeforeMeshingOrSolving()
    {
        var (session, body, study) = CreateAxialBar(assignMaterial: false);

        var error = Assert.Throws<InvalidOperationException>(() => StudyRunner.Run(session, study));

        Assert.Contains("material", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(body.Id, Assert.Single(session.Objects).Id);
    }
}
