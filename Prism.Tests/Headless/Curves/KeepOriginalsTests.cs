using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Curves;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#6-keep-the-originals-consistency">0.4 modeling gaps, item 6 (KEP-P1).</seealso>
[Trait("Feature", "Curves")]
[Trait("Layer", "Headless")]
public class KeepOriginalsTests
{
    static (CadSession session, SceneObject front, SceneObject side) TwoCurves()
    {
        var session = new CadSession();
        var front = session.AddObject(Occ.NurbsCurve([new(-60, -20, -35), new(-20, 45, -35), new(20, -45, -35), new(60, 20, -35)]), "Front profile", ObjKind.Curve, 0xFFF6B85B, new(new(0, 0, -35), Vec3.UnitX, Vec3.UnitY));
        var side = session.AddObject(Occ.NurbsCurve([new(-60, -50, 0), new(-20, -50, 65), new(20, -50, -25), new(60, -50, 40)]), "Side profile", ObjKind.Curve, 0xFFB697FF, new(new(0, -50, 0), Vec3.UnitX, Vec3.UnitZ));
        return (session, front, side);
    }

    static CommandOutput Build(CadSession session, SceneObject front, SceneObject side, bool? keep)
    {
        var command = StartCommand(session, front, () => new IntersectCurvesCommand());
        command.ConfigureInputs(() => { SetSelection(command, "First", front); SetSelection(command, "Second", side); });
        if (keep is { } k) command.Params.OfType<BoolParam>().Single(p => p.Label == "Keep the original curves").Value = k;
        command.OnChanged();
        return BuildOutput(command);
    }

    [AvaloniaFact(DisplayName = "CRV-H038 | Intersection Curve of two curves replaces them unless Keep the original curves is on")]
    [Trait("TestId", "CRV-H038")]
    public void IntersectionCurveRemovesItsSourcesByDefault()
    {
        var (session, front, side) = TwoCurves();
        var replaced = Build(session, front, side, null);
        Assert.Single(replaced.Add);
        Assert.Equal(new[] { front.Id, side.Id }.OrderBy(i => i), replaced.Remove.Select(o => o.Id).OrderBy(i => i));

        var (kept, f2, s2) = TwoCurves();
        Assert.Empty(Build(kept, f2, s2, true).Remove);
    }

    [AvaloniaFact(DisplayName = "CRV-H039 | Intersection Curve never removes a plane, face or body, and a step saved before the option keeps its curves")]
    [Trait("TestId", "CRV-H039")]
    public void ReferencesStayAndOldStepsKeepTheirCurves()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 4, 5, 6);
        var plane = session.AddObject(null, "Cut plane", ObjKind.Plane, plane: Plane3.XY with { Origin = new Vec3(0, 0, 3) });
        var command = StartCommand(session, box, () => new IntersectCurvesCommand());
        command.ConfigureInputs(() => { SetSelection(command, "First", box); SetSelection(command, "Second", plane); });
        Assert.Empty(BuildOutput(command).Remove);

        var (s2, front, side) = TwoCurves();
        var source = StartCommand(s2, front, () => new IntersectCurvesCommand());
        source.ConfigureInputs(() => { SetSelection(source, "First", front); SetSelection(source, "Second", side); });
        var saved = source.Capture()!;
        var old = saved with { Values = saved.Values.Take(2).ToList() };   // the value list before the option existed
        var reopened = StartCommand(s2, front, () => new IntersectCurvesCommand());
        reopened.Restore(old);
        Assert.True(reopened.Params.OfType<BoolParam>().Single(p => p.Label == "Keep the original curves").Value);
    }

    [AvaloniaTheory(DisplayName = "CRV-H040 | Thicken and Sew consume their surfaces unless Keep the originals is on")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("TestId", "CRV-H040")]
    public void ThickenAndSewHonourKeep(bool keep)
    {
        var session = new CadSession();
        var sheet = session.AddObject(Occ.NurbsSurface([new(0, 0, 0), new(10, 0, 0), new(0, 10, 0), new(10, 10, 0)], 2, 2, 1, 1), "Sheet", ObjKind.Surface);
        var thicken = StartCommand(session, sheet, () => new ThickenCommand());
        thicken.ConfigureInputs(() => SetSelection(thicken, "Surface", sheet));
        thicken.Params.OfType<BoolParam>().Single(p => p.Label == "Keep the original surface").Value = keep;
        Assert.Equal(keep ? 0 : 1, BuildOutput(thicken).Remove.Count);

        var sew = StartCommand(session, sheet, () => new SewCommand());
        sew.ConfigureInputs(() => SetSelection(sew, "Surfaces", sheet));
        sew.Params.OfType<BoolParam>().Single(p => p.Label == "Keep the originals").Value = keep;
        Assert.Equal(keep ? 0 : 1, BuildOutput(sew).Remove.Count);
    }
}
