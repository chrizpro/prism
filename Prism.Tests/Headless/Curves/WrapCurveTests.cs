using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Curves;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#5-imprint-and-wrap">0.4 modeling gaps, item 5 (IMP-P2 Wrap).</seealso>
[Trait("Feature", "Curves")]
[Trait("Layer", "Headless")]
public class WrapCurveTests
{
    static double Mm(string value) => double.Parse(value.Replace(" mm", ""), System.Globalization.CultureInfo.InvariantCulture);

    static (WrapCurveCommand command, CommandOutput output) Wrap(CadSession session, SceneObject curve, SceneObject target, bool build = true)
    {
        var command = Begin(session, () => new WrapCurveCommand());
        Pick(command, "Flat curves to wrap", curve);
        Pick(command, "Surface to wrap onto", target);
        return (command, build ? BuildOutput(command) : null!);
    }

    [AvaloniaFact(DisplayName = "CRV-H043 | Wrap lays a 31.4 mm line half way round a 10 mm cylinder with its length kept")]
    [Trait("TestId", "CRV-H043")]
    public void LineWrapsHalfWayRound()
    {
        var session = new CadSession();
        var drum = session.AddObject(CylinderAt(new(0, 0, 0), 10, 20), "Drum", ObjKind.Solid);
        double half = 5 * Math.PI;
        var line = session.AddObject(Occ.Polyline([new(-half, -12, 10), new(half, -12, 10)], false), "Line", ObjKind.Curve);
        var (command, output) = Wrap(session, line, drum);
        AssertClose(10 * Math.PI, Mm(Result(command, "Length before")), 1e-3, "Length before (mm)");
        AssertClose(10 * Math.PI, Mm(Result(command, "Length after")), 0.01, "Length after (mm)");
        var wrapped = Assert.Single(output.Add).Shape!;
        AssertClose(20, wrapped.Props().Size.X, 0.05, "Width round the drum (mm)");
        AssertClose(10, wrapped.Props().Size.Y, 0.05, "Depth of the half turn (mm)");
        AssertClose(0, Math.Abs(wrapped.Props().Size.Z), 1e-6, "Height change (mm)");
    }

    [AvaloniaFact(DisplayName = "CRV-H044 | Wrap onto a flat face keeps a square's size and drops it onto the face")]
    [Trait("TestId", "CRV-H044")]
    public void SquareDropsOntoAFlatFace()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, new(0, 0, 0), 20, 20, 10);
        var square = session.AddObject(Occ.Polyline([new(7, 7, 14), new(13, 7, 14), new(13, 13, 14), new(7, 13, 14)], true), "Square", ObjKind.Curve);
        var (_, output) = Wrap(session, square, box);
        var props = Assert.Single(output.Add).Shape!.Props();
        AssertClose(6, props.Size.X, 1e-3, "Width (mm)");
        AssertClose(6, props.Size.Y, 1e-3, "Depth (mm)");
        AssertClose(10, props.Min.Z, 1e-6, "Height of the wrapped curve (mm)");
        AssertClose(10, props.Max.Z, 1e-6, "Height of the wrapped curve (mm)");
    }

    [AvaloniaFact(DisplayName = "CRV-H045 | Wrap refuses a curve that is not flat")]
    [Trait("TestId", "CRV-H045")]
    public void RefusesACurveThatIsNotFlat()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, new(0, 0, 0), 20, 20, 10);
        var bent = session.AddObject(Occ.Polyline([new(5, 5, 14), new(10, 5, 18), new(15, 10, 14), new(10, 15, 20)], false), "Bent", ObjKind.Curve);
        var (command, _) = Wrap(session, bent, box, build: false);
        AssertBuildFails(command, "not flat");
    }

    static void WrapsOntoTheSphere(Vec3 from, Vec3 to)
    {
        var session = new CadSession();
        var ball = session.AddObject(Occ.Sphere(20), "Ball", ObjKind.Solid);
        var line = session.AddObject(Occ.Polyline([from, to], false), "Line", ObjKind.Curve);
        var (command, output) = Wrap(session, line, ball);
        AssertClose(20, Mm(Result(command, "Length before")), 1e-3, "Length before (mm)");
        // walked over the sphere, a 20 mm line is a 20 mm great-circle arc, 1 radian of it
        AssertClose(20, Mm(Result(command, "Length after")), 0.1, "Length after (mm)");
        var wrapped = Assert.Single(output.Add).Shape!;
        foreach (var point in Occ.CurveSamples(wrapped, 9, 1)[0].Select(s => s.p))
            AssertClose(20, point.Length, 0.02, "Distance of a wrapped point from the centre (mm)");
    }

    [AvaloniaFact(DisplayName = "CRV-H046 | Wrap onto a sphere keeps the walked distance and lands every point on the sphere")]
    [Trait("TestId", "CRV-H046")]
    public void WrapsOntoASphereByDistance() => WrapsOntoTheSphere(new(26, -10, 0), new(26, 10, 0));

    [AvaloniaFact(DisplayName = "CRV-H047 | Wrap onto a sphere works right over its pole too")]
    [Trait("TestId", "CRV-H047")]
    public void WrapsOverASpherePole() => WrapsOntoTheSphere(new(-10, 0, 26), new(10, 0, 26));
}
