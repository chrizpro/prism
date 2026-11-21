using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.StaticAnalysis;

/// <summary>
/// The shared direction input used by loads (owner request 2026-11-16: finer control over how a force acts on a face, and a vector other CAE tools can share).
/// The maths is checked against hand calculations; the command is checked for what it hands to the solver.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/cae/static-analysis/README.md">Static analysis guide.</seealso>
[Trait("Feature", "StaticAnalysis")]
[Trait("Layer", "Headless")]
public class VectorInputTests
{
    static readonly double S30 = Math.Sin(Math.PI / 6), C30 = Math.Cos(Math.PI / 6);

    [AvaloniaTheory(DisplayName = "VEC-H001 | A direction is made a unit vector, and one with no length or no number is refused")]
    [InlineData(3, 4, 0, 0.6, 0.8, 0)]
    [InlineData(0, 0, -7, 0, 0, -1)]
    [InlineData(1, 2, 2, 1.0 / 3, 2.0 / 3, 2.0 / 3)]
    [Trait("TestId", "VEC-H001")]
    public void UnitVectors(double x, double y, double z, double ex, double ey, double ez)
    {
        var u = VectorInput.Unit(new Vec3(x, y, z));
        AssertPoint(new Vec3(ex, ey, ez), u, 1e-12, "Unit vector");
        AssertNear(1, u.Length, "Length");
        Assert.Throws<FormatException>(() => VectorInput.Unit(Vec3.Zero));
        Assert.Throws<FormatException>(() => VectorInput.Unit(new Vec3(0, 1e-12, 0)));
        Assert.Throws<FormatException>(() => VectorInput.Unit(new Vec3(double.NaN, 0, 1)));
        Assert.Throws<FormatException>(() => VectorInput.Unit(new Vec3(double.PositiveInfinity, 0, 0)));
    }

    [AvaloniaFact(DisplayName = "VEC-H002 | A tilt from a face normal: 0 deg is straight in, 90 deg lies in the face, 30 deg is cos/sin of 30")]
    [Trait("TestId", "VEC-H002")]
    public void TiltFromTheFaceNormal()
    {
        var top = Vec3.UnitZ;                                                  // top face of a box, outward normal +Z; its in-face reference is +X
        AssertPoint(-Vec3.UnitZ, VectorInput.Tilted(top, 0, 0), 1e-12, "0 degrees");
        AssertPoint(Vec3.UnitX, VectorInput.Tilted(top, 90, 0), 1e-12, "90 degrees, turn 0");
        AssertPoint(Vec3.UnitY, VectorInput.Tilted(top, 90, 90), 1e-12, "90 degrees, turn 90 (counter-clockwise seen from outside)");
        AssertPoint(-Vec3.UnitX, VectorInput.Tilted(top, 90, 180), 1e-12, "90 degrees, turn 180");
        AssertPoint(new Vec3(S30, 0, -C30), VectorInput.Tilted(top, 30, 0), 1e-12, "30 degrees leaning toward +X");
        AssertPoint(new Vec3(0, S30, -C30), VectorInput.Tilted(top, 30, 90), 1e-12, "30 degrees leaning toward +Y");
        // a face on the +X side: straight in is -X, and the lean reference falls back to +Y (the part of +X in the face is nothing)
        AssertPoint(-Vec3.UnitX, VectorInput.Tilted(Vec3.UnitX, 0, 0), 1e-12, "side face, straight in");
        AssertPoint(new Vec3(-C30, S30, 0), VectorInput.Tilted(Vec3.UnitX, 30, 0), 1e-12, "side face, 30 degrees toward +Y");
        // every tilt is a unit vector, and its angle to "straight in" is the angle asked for
        foreach (double tilt in new[] { 0.0, 12.5, 45, 77, 90 })
            foreach (double turn in new[] { -200.0, 0, 33, 270 })
            {
                var d = VectorInput.Tilted(new Vec3(1, 2, 3), tilt, turn); var into = -new Vec3(1, 2, 3).Normalized();
                AssertNear(1, d.Length, "Length");
                AssertNear(tilt, Math.Acos(Math.Clamp(Vec3.Dot(d, into), -1, 1)) * 180 / Math.PI, $"Angle from straight in (tilt {tilt}, turn {turn})");
            }
    }

    [AvaloniaFact(DisplayName = "VEC-H003 | A 100 N force along (0.6, 0, -0.8) into a top face is 80 N pushing in and 60 N sliding")]
    [Trait("TestId", "VEC-H003")]
    public void PushAndSlide()
    {
        var (into, shear) = VectorInput.Split(new Vec3(0.6, 0, -0.8), Vec3.UnitZ, 100);
        AssertNear(80, into, "Pushing in (N)");
        AssertNear(60, shear, "Sliding (N)");
        var (pulling, slide) = VectorInput.Split(new Vec3(0, 0.6, 0.8), Vec3.UnitZ, 100);
        AssertNear(-80, pulling, "Pulling out is negative (N)");
        AssertNear(60, slide, "Sliding (N)");
        AssertNear(100, Math.Sqrt(into * into + shear * shear), "The parts add back to the force (N)");
    }

    sealed class Fixture : IDisposable
    {
        public CadSession Session = new();
        public SceneObject Body;
        public Fixture()
        {
            var body = CreateBoxBody(Session, Vec3.Zero, 10, 10, 100);
            Session.Update(body.Id, obj => obj.WithMaterial(Materials.Find("6061")!));
            Body = Session.Find(body.Id)!;
            Session.NewStudy(Body);
        }
        public SelItem Face(Vec3 normal) => FaceFacing(Body, normal);
        public void Dispose() { }
    }

    static LoadCommand StartForce(Fixture f, params SelItem[] faces)
    {
        var command = Begin(f.Session, () => new LoadCommand(LoadKind.Force));
        Pick(command, "Faces to load", faces);
        return command;
    }

    static string Line(Command command, string label) => command.Results.Single(r => r.Label == label).Value;

    [AvaloniaFact(DisplayName = "VEC-H004 | The Force command stores a custom vector as a unit direction and reports the force vector, push and slide")]
    [Trait("TestId", "VEC-H004")]
    public void CustomVectorOnAFace()
    {
        using var f = new Fixture();
        var command = StartForce(f, f.Face(Vec3.UnitZ));
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Direction", "Custom: type X, Y, Z");
            SetNumber(command, "Direction X", 3); SetNumber(command, "Direction Y", 0); SetNumber(command, "Direction Z", -4);
            SetNumber(command, "Force", 100);
        });
        Assert.NotNull(command.TryBuild());
        Assert.Equal("(0.6, 0, -0.8)", Line(command, "Direction"));
        Assert.Equal("(60, 0, -80) N", Line(command, "Force vector"));
        Assert.Equal("80 N", Line(command, "Pushing into the face"));
        Assert.Equal("60 N", Line(command, "Sliding across the face"));
    }

    [AvaloniaFact(DisplayName = "VEC-H005 | A zero custom vector is refused with a message")]
    [Trait("TestId", "VEC-H005")]
    public void ZeroCustomVectorIsRefused()
    {
        using var f = new Fixture();
        var command = StartForce(f, f.Face(Vec3.UnitZ));
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Direction", "Custom: type X, Y, Z");
            SetNumber(command, "Direction X", 0); SetNumber(command, "Direction Y", 0); SetNumber(command, "Direction Z", 0);
        });
        Assert.Null(command.TryBuild());
        Assert.Contains("no length", command.Error);
    }

    [AvaloniaFact(DisplayName = "VEC-H006 | Tilted from the face normal gives cos/sin of the angle, refuses faces that point different ways, and has a turning handle")]
    [Trait("TestId", "VEC-H006")]
    public void TiltOnAFlatFace()
    {
        using var f = new Fixture();
        var command = StartForce(f, f.Face(Vec3.UnitZ));
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Direction", "Tilted from the face normal");
            SetNumber(command, "Tilt from the face normal (0° straight in, 90° along the face)", 30);
            SetNumber(command, "Force", 1000);
        });
        Assert.NotNull(command.TryBuild());
        Assert.Equal($"({S30:0.####}, 0, {-C30:0.####})", Line(command, "Direction"));
        Assert.Equal($"{C30 * 1000:0.##} N", Line(command, "Pushing into the face"));
        Assert.Equal($"{S30 * 1000:0.##} N", Line(command, "Sliding across the face"));
        var handle = Assert.Single(command.Handles);                              // dragging it changes the tilt
        Assert.Equal("Tilt from the face normal (0° straight in, 90° along the face)", handle.Param.Label);
        AssertPoint(-Vec3.UnitZ, handle.Ref!.Value, 1e-9, "The arc starts at straight-in");
        AssertNear(0, Vec3.Dot(handle.Dir, handle.Ref.Value), "The turning axis is at right angles to the start direction");

        var two = StartForce(f, f.Face(Vec3.UnitZ), f.Face(Vec3.UnitY));
        two.ConfigureInputs(() => SetChoice(two, "Direction", "Tilted from the face normal"));
        Assert.Null(two.TryBuild());
        Assert.Contains("point the same way", two.Error);
    }

    [AvaloniaFact(DisplayName = "VEC-H007 | A direction can be taken from a straight edge or a face normal, and reversed")]
    [Trait("TestId", "VEC-H007")]
    public void DirectionFromGeometry()
    {
        using var f = new Fixture();
        int edge = Enumerable.Range(0, f.Body.Shape.Count(SubKind.Edge)).First(i =>
        {
            var info = Occ.EdgeInfoOf(f.Body.Shape.Sub(SubKind.Edge, i));
            return Math.Abs(Math.Abs(info.End.Z - info.Start.Z) - 100) < 1e-6;           // a 100 mm edge along Z
        });
        var along = Occ.EdgeInfoOf(f.Body.Shape.Sub(SubKind.Edge, edge)); var expected = (along.End - along.Start).Normalized();

        var command = StartForce(f, f.Face(Vec3.UnitX));
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Direction", "Along an edge, or a plane's normal");
            Pick(command, "Direction taken from (edge, plane or face)", new SelItem(f.Body.Id, SelKind.Edge, edge));
        });
        Assert.NotNull(command.TryBuild());
        Assert.Equal(VectorInput.Describe(expected), Line(command, "Direction"));

        command.ConfigureInputs(() => SetToggle(command, "Reverse the direction", true));
        Assert.NotNull(command.TryBuild());
        Assert.Equal(VectorInput.Describe(-expected), Line(command, "Direction"));

        command.ConfigureInputs(() =>
        {
            SetToggle(command, "Reverse the direction", false);
            Pick(command, "Direction taken from (edge, plane or face)", f.Face(Vec3.UnitY));
        });
        Assert.NotNull(command.TryBuild());
        AssertPoint(Vec3.UnitY, StudyDirection(command), 1e-9, "Direction from the +Y face");
    }

    static Vec3 StudyDirection(LoadCommand command)
    {
        var text = command.Results.Single(r => r.Label == "Direction").Value.Trim('(', ')').Split(',', StringSplitOptions.TrimEntries);
        return new Vec3(double.Parse(text[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(text[1], System.Globalization.CultureInfo.InvariantCulture), double.Parse(text[2], System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaFact(DisplayName = "VEC-H008 | Gravity takes the same direction input: a tilted custom vector gives the right acceleration")]
    [Trait("TestId", "VEC-H008")]
    public void GravityUsesTheSameInput()
    {
        using var f = new Fixture();
        var command = Begin(f.Session, () => new LoadCommand(LoadKind.Gravity));
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Direction", "Custom: type X, Y, Z");
            SetNumber(command, "Direction X", 1); SetNumber(command, "Direction Y", 0); SetNumber(command, "Direction Z", -1);
            SetNumber(command, "Gravity", 2);
        });
        Assert.NotNull(command.TryBuild());
        double r = Math.Sqrt(0.5);
        Assert.Equal($"({r * 19620:0.##}, 0, {-r * 19620:0.##}) mm/s²", Line(command, "Acceleration"));
        Assert.DoesNotContain(command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Direction").Options, o => o.Contains("face"));      // no face modes for gravity
        var plain = Begin(f.Session, () => new LoadCommand(LoadKind.Gravity));
        Assert.Equal("-Z", plain.Params.OfType<ChoiceParam>().Single(p => p.Label == "Direction").Selected);                                  // the old default
    }

    [AvaloniaFact(DisplayName = "VEC-H009 | The first Direction choices are the ones loads always had, in the same order")]
    [Trait("TestId", "VEC-H009")]
    public void OldChoicesKeepTheirPlaces()
    {
        var force = new LoadCommand(LoadKind.Force).Params.OfType<ChoiceParam>().Single(p => p.Label == "Direction");
        Assert.Equal(["Into the face (along its normal)", "+X", "+Y", "+Z", "-X", "-Y", "-Z"], force.Options.Take(7));
        Assert.Equal(0, force.Index);
        var gravity = new LoadCommand(LoadKind.Gravity).Params.OfType<ChoiceParam>().Single(p => p.Label == "Direction");
        Assert.Equal(["+X", "+Y", "+Z", "-X", "-Y", "-Z"], gravity.Options.Take(6));
        Assert.Equal(5, gravity.Index);
    }
}
