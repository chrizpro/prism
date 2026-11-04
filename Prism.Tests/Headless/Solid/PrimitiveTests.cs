using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/primitives/README.md">Living guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class PrimitiveTests
{
    [AvaloniaFact(DisplayName = "SOL-H001 | The 40 × 30 × 20 box has its analytic volume")]
    [Trait("TestId", "SOL-H001")]
    public void BoxHasAnalyticVolume()
    {
        var session = new CadSession();
        session.StartCommand(() => new BoxCommand());
        var command = Assert.IsType<BoxCommand>(session.ActiveCommand);
        using var shape = BuildSingleAddedShape(command);
        AssertValidSolid(shape);
        AssertNear(40 * 30 * 20, shape.Props().Volume, "Primitive volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H002 | The radius-12 height-30 cylinder has its analytic volume")]
    [Trait("TestId", "SOL-H002")]
    public void CylinderHasAnalyticVolume()
    {
        var session = new CadSession();
        session.StartCommand(() => new CylinderCommand());
        var command = Assert.IsType<CylinderCommand>(session.ActiveCommand);
        using var shape = BuildSingleAddedShape(command);
        AssertValidSolid(shape);
        AssertNear(Math.PI * 12 * 12 * 30, shape.Props().Volume, "Primitive volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H003 | The radius-18 sphere has its analytic volume")]
    [Trait("TestId", "SOL-H003")]
    public void SphereHasAnalyticVolume()
    {
        var session = new CadSession();
        session.StartCommand(() => new SphereCommand());
        var command = Assert.IsType<SphereCommand>(session.ActiveCommand);
        using var shape = BuildSingleAddedShape(command);
        AssertValidSolid(shape);
        AssertNear(4.0 / 3 * Math.PI * Math.Pow(18, 3), shape.Props().Volume, "Primitive volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H004 | The radius-16 to radius-4 frustum has its analytic volume")]
    [Trait("TestId", "SOL-H004")]
    public void ConeFrustumHasAnalyticVolume()
    {
        var session = new CadSession();
        session.StartCommand(() => new ConeCommand());
        var command = Assert.IsType<ConeCommand>(session.ActiveCommand);
        using var shape = BuildSingleAddedShape(command);
        AssertValidSolid(shape);
        AssertNear(Math.PI * 30 / 3 * (16 * 16 + 16 * 4 + 4 * 4), shape.Props().Volume, "Primitive volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H005 | The major-radius-20 minor-radius-5 torus has its analytic volume")]
    [Trait("TestId", "SOL-H005")]
    public void TorusHasAnalyticVolume()
    {
        var session = new CadSession();
        session.StartCommand(() => new TorusCommand());
        var command = Assert.IsType<TorusCommand>(session.ActiveCommand);
        using var shape = BuildSingleAddedShape(command);
        AssertValidSolid(shape);
        AssertNear(2 * Math.PI * Math.PI * 20 * 5 * 5, shape.Props().Volume, "Primitive volume (mm³)");
    }

}
