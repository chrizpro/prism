using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.StaticAnalysis;

/// <summary>
/// Characterizes the Simulate ribbon tools that set up and report a study: the three fixtures, the four loads, Probe,
/// Save / Recall Face Selection, Export CAE Results and the analytic Pipe Flow calculator. Solver accuracy itself is
/// covered by <see cref="StaticStudyTests"/>.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cae/static-analysis/README.md">Static analysis guide.</seealso>
[Trait("Feature", "StaticAnalysis")]
[Trait("Layer", "Headless")]
public class StudySetupToolTests
{
    static (CadSession Session, SceneObject Bar, StudyDef Study) Bar()
    {
        var session = new CadSession();
        var bar = CreateBoxBody(session, Vec3.Zero, 10, 10, 100);
        session.Update(bar.Id, obj => obj.WithMaterial(Materials.Find("6061")!));
        bar = session.Find(bar.Id)!;
        return (session, bar, session.NewStudy(bar));
    }

    // ------------------------------------------------------------------ fixtures

    [AvaloniaTheory(DisplayName = "CAE-H001 | Each fixture kind adds a named support with its locked directions")]
    [InlineData(FixtureKind.Fixed, "Fixed Support")]
    [InlineData(FixtureKind.Slider, "Roller / Symmetry")]
    [InlineData(FixtureKind.Directional, "Fixed Directions")]
    [Trait("TestId", "CAE-H001")]
    public void FixtureKinds(FixtureKind kind, string title)
    {
        var (session, bar, study) = Bar();
        var command = Begin(session, () => new FixtureCommand(kind));
        Assert.Equal(title, command.Title);
        Assert.Equal(kind == FixtureKind.Directional, command.Params.Single(p => p.Label == "Lock Y").IsVisible);
        Pick(command, "Faces to support", FaceFacing(bar, -Vec3.UnitZ));
        SetToggle(command, "Lock Y", false);
        Assert.True(BuildOutput(command).NoOp);
        session.CommitCommand();
        var fixture = Assert.Single(study.Fixtures);
        Assert.Equal(kind, fixture.Kind);
        Assert.Equal($"{title} 1", fixture.Name);
        Assert.Equal(FaceFacing(bar, -Vec3.UnitZ).Index, Assert.Single(fixture.Faces).Face);
        Assert.False(fixture.LockY);
    }

    [AvaloniaFact(DisplayName = "CAE-H002 | Fixtures and loads ask for a study first")]
    [Trait("TestId", "CAE-H002")]
    public void SetupNeedsStudy()
    {
        var session = new CadSession();
        var bar = CreateBoxBody(session, Vec3.Zero, 10, 10, 100);
        var fixture = Begin(session, () => new FixtureCommand(FixtureKind.Fixed));
        Pick(fixture, "Faces to support", FaceFacing(bar, -Vec3.UnitZ));
        Assert.Null(fixture.TryBuild());
        Assert.Equal("Create a study first (Simulate > New Study).", fixture.Error);
        session.CancelCommand();
        var load = Begin(session, () => new LoadCommand(LoadKind.Gravity));
        Assert.Null(load.TryBuild());
        Assert.Equal("Create a study first (Simulate > New Study).", load.Error);
    }

    [AvaloniaFact(DisplayName = "CAE-H003 | Fixtures ignore faces of bodies outside the study")]
    [Trait("TestId", "CAE-H003")]
    public void FixtureRejectsOtherBodies()
    {
        var (session, _, _) = Bar();
        var other = CreateBoxBody(session, new Vec3(50, 0, 0), 5, 5, 5);
        var command = Begin(session, () => new FixtureCommand(FixtureKind.Fixed));
        Pick(command, "Faces to support", FaceFacing(other, Vec3.UnitZ));
        Assert.Null(command.TryBuild());
        Assert.Equal("Pick faces of the study's bodies.", command.Error);
    }

    // ------------------------------------------------------------------ loads

    [AvaloniaTheory(DisplayName = "CAE-H004 | Force load direction: along the face normal or a world axis")]
    [InlineData("Into the face (along its normal)", true, 0, 0, 0)]
    [InlineData("+X", false, 1, 0, 0)]
    [InlineData("-Z", false, 0, 0, -1)]
    [Trait("TestId", "CAE-H004")]
    public void ForceDirection(string direction, bool alongNormal, double x, double y, double z)
    {
        var (session, bar, study) = Bar();
        var command = Begin(session, () => new LoadCommand(LoadKind.Force));
        Pick(command, "Faces to load", FaceFacing(bar, Vec3.UnitZ));
        SetNumber(command, "Force", 250);
        SetChoice(command, "Direction", direction);
        SetToggle(command, "The force is the total over all picked faces", false);
        BuildOutput(command);
        session.CommitCommand();
        var load = Assert.Single(study.Loads);
        Assert.Equal(LoadKind.Force, load.Kind);
        Assert.Equal(250, load.Value);
        Assert.Equal(alongNormal, load.AlongNormal);
        Assert.False(load.Total);
        if (!alongNormal) AssertPoint(new Vec3(x, y, z), load.Direction, 1e-12, "Force direction");
        Assert.Equal("Force 1", load.Name);
    }

    [AvaloniaTheory(DisplayName = "CAE-H005 | Pressure, torque and gravity loads store their value and options")]
    [InlineData(LoadKind.Pressure, "Pressure", 2.5)]
    [InlineData(LoadKind.Torque, "Torque", 5000)]
    [InlineData(LoadKind.Gravity, "Gravity", 2)]
    [Trait("TestId", "CAE-H005")]
    public void OtherLoads(LoadKind kind, string label, double value)
    {
        var (session, bar, study) = Bar();
        var command = Begin(session, () => new LoadCommand(kind));
        Assert.Equal(kind == LoadKind.Gravity, command.Slots.Count == 0);
        Assert.Equal(kind == LoadKind.Gravity, command.Params.Single(p => p.Label == "Direction").IsVisible);
        Assert.False(command.Params.Single(p => p.Label == "The force is the total over all picked faces").IsVisible);
        if (kind != LoadKind.Gravity) Pick(command, "Faces to load", FaceFacing(bar, Vec3.UnitZ));
        SetNumber(command, label, value);
        BuildOutput(command);
        session.CommitCommand();
        var load = Assert.Single(study.Loads);
        Assert.Equal(kind, load.Kind);
        Assert.Equal(value, load.Value);
        if (kind == LoadKind.Gravity) AssertPoint(-Vec3.UnitZ, load.Direction, 1e-12, "Default gravity direction");
    }

    [AvaloniaFact(DisplayName = "CAE-H006 | A load with no faces adds nothing")]
    [Trait("TestId", "CAE-H006")]
    public void LoadWithoutFaces()
    {
        var (session, _, _) = Bar();
        var command = Begin(session, () => new LoadCommand(LoadKind.Pressure));
        Assert.Null(command.TryBuild());
        Assert.Null(command.Error);
    }

    // ------------------------------------------------------------------ probe / named faces / export

    [AvaloniaFact(DisplayName = "CAE-H007 | Probe asks for a result before probing")]
    [Trait("TestId", "CAE-H007")]
    public void ProbeNeedsResult()
    {
        var session = new CadSession();
        var command = Begin(session, () => new ProbeCommand());
        Assert.True(BuildOutput(command).NoOp);
        Assert.Equal("run a study first", Result(command, "No result"));
    }

    [AvaloniaFact(DisplayName = "CAE-H008 | Save Face Selection stores named faces and Recall selects them again")]
    [Trait("TestId", "CAE-H008")]
    public void NamedFacesRoundTrip()
    {
        var (session, bar, _) = Bar();
        var top = FaceFacing(bar, Vec3.UnitZ);
        var bottom = FaceFacing(bar, -Vec3.UnitZ);
        var save = Begin(session, () => new NamedFacesCommand());
        Pick(save, "Faces", top, bottom);
        SetText(save, "Selection name", "Ends");
        session.CommitCommand();
        var set = Assert.Single(session.FaceSets);
        Assert.Equal("Ends", set.Name);
        Assert.Equal(new[] { top.Index, bottom.Index }.Order(), set.Faces.Order());

        session.ClearSelection();
        var recall = Begin(session, () => new RecallFacesCommand());
        Assert.Equal("Ends", recall.Params.OfType<ChoiceParam>().Single().Selected);
        session.CommitCommand();
        Assert.Equal(new[] { top, bottom }.OrderBy(i => i.Index), session.Selection.OrderBy(i => i.Index));
    }

    [AvaloniaFact(DisplayName = "CAE-H009 | Save Face Selection refuses faces from two bodies and a blank name")]
    [Trait("TestId", "CAE-H009")]
    public void NamedFacesValidation()
    {
        var (session, bar, _) = Bar();
        var other = CreateBoxBody(session, new Vec3(50, 0, 0), 5, 5, 5);
        var save = Begin(session, () => new NamedFacesCommand());
        Pick(save, "Faces", FaceFacing(bar, Vec3.UnitZ), FaceFacing(other, Vec3.UnitZ));
        AssertBuildFails(save, "one body");
        Pick(save, "Faces", FaceFacing(bar, Vec3.UnitZ));
        SetText(save, "Selection name", " ");
        AssertBuildFails(save, "Enter a selection name");
    }

    [AvaloniaFact(DisplayName = "CAE-H010 | Export CAE Results writes nodal CSV and study JSON for a solved study")]
    [Trait("TestId", "CAE-H010")]
    public void ExportResults()
    {
        var (session, bar, study) = Bar();
        study.ElementSize = 5;
        study.Fixtures.Add(new Fixture { Kind = FixtureKind.Fixed, Faces = [new FaceRef(bar.Id, FaceFacing(bar, -Vec3.UnitZ).Index)] });
        study.Loads.Add(new Load { Kind = LoadKind.Force, Value = 1000, Direction = Vec3.UnitZ, Total = true, Faces = [new FaceRef(bar.Id, FaceFacing(bar, Vec3.UnitZ).Index)] });
        study.Results = StudyRunner.Run(session, study);
        var folder = Path.Combine(Path.GetTempPath(), $"prism-cae-{Guid.NewGuid():N}");
        try
        {
            var command = Begin(session, () => new ExportStudyCommand());
            SetText(command, "Output folder", folder);
            command.Params.OfType<ActionParam>().Single(a => a.Label == "Export static results").Run();
            var lines = File.ReadAllLines(Path.Combine(folder, "nodes.csv"));
            Assert.Equal("node,x_mm,y_mm,z_mm,ux_mm,uy_mm,uz_mm,von_mises_MPa,yield_safety_factor", lines[0]);
            Assert.Equal(study.Results.Mesh.NodeCount + 1, lines.Length);
            Assert.Contains("\"MaxVonMises\"", File.ReadAllText(Path.Combine(folder, "study.json")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    // ------------------------------------------------------------------ Pipe Flow

    [AvaloniaFact(DisplayName = "CAE-H011 | Pipe Flow uses 64/Re and Darcy–Weisbach for laminar water flow")]
    [Trait("TestId", "CAE-H011")]
    public void PipeFlowLaminar()
    {
        var session = new CadSession();
        var command = Begin(session, () => new PipeFlowCommand());
        Assert.True(BuildOutput(command).NoOp);
        // Defaults: Ø10 mm, 1 m, 0.5 L/min of water (998.2 kg/m³, 1.002 mPa·s).
        double d = 0.01, area = Math.PI * d * d / 4, v = 0.5 / 60000 / area, re = 998.2 * v * d / 0.001002;
        double f = 64 / re, dp = f * (1 / d) * 998.2 * v * v / 2;
        Assert.Equal("Laminar", Result(command, "Regime"));
        Assert.Equal($"{re:G6}", Result(command, "Reynolds number"));
        Assert.Equal($"{f:G6}", Result(command, "Darcy friction factor"));
        Assert.Equal($"{dp:G6} Pa", Result(command, "Pressure drop"));
    }

    [AvaloniaFact(DisplayName = "CAE-H012 | Pipe Flow switches to Colebrook–White in turbulent flow and refuses the transition range")]
    [Trait("TestId", "CAE-H012")]
    public void PipeFlowTurbulentAndTransitional()
    {
        var session = new CadSession();
        var command = Begin(session, () => new PipeFlowCommand());
        SetNumber(command, "Volume flow", 10);
        BuildOutput(command);
        Assert.Equal("Turbulent (Colebrook–White)", Result(command, "Regime"));
        SetNumber(command, "Volume flow", 1.4);
        AssertBuildFails(command, "transitional flow");
    }
}
