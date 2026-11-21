using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#3-merge-surface">0.4 modeling gaps, item 3 (MRG-P1).</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class SewMergeTests
{
    static SceneObject Sheet(CadSession session, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        using var boundary = Occ.Polyline([a, b, c, d], true);
        return session.AddObject(Occ.Profile([boundary], true), "Sheet", ObjKind.Surface);
    }

    static Shape Sew(CadSession session, string result, params SceneObject[] sheets)
    {
        var command = Begin(session, () => new SewCommand());
        Pick(command, "Surfaces", sheets);
        SetChoice(command, "Result", result);
        return Assert.Single(BuildOutput(command).Add).Shape!;
    }

    const string Stitch = "Stitch (keep each face)", Merge = "Merge into one patch where they lie on the same surface";

    [AvaloniaFact(DisplayName = "SRF-H032 | Sew with Merge fuses two coplanar halves into one face and leaves a crease alone")]
    [Trait("TestId", "SRF-H032")]
    public void SewMergeFusesCoplanarFacesOnly()
    {
        var session = new CadSession();
        var halves = new[]
        {
            Sheet(session, new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)),
            Sheet(session, new(10, 0, 0), new(20, 0, 0), new(20, 10, 0), new(10, 10, 0)),
        };
        Assert.Equal(2, Sew(session, Stitch, halves).Count(SubKind.Face));
        var merged = Sew(session, Merge, halves);
        Assert.Equal(1, merged.Count(SubKind.Face));
        AssertClose(200, merged.Props().Area, 1e-3, "Merged area (mm²)");

        var bent = new CadSession();
        var floor = Sheet(bent, new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0));
        var wall = Sheet(bent, new(10, 0, 0), new(10, 0, 10), new(10, 10, 10), new(10, 10, 0));
        Assert.Equal(2, Sew(bent, Merge, floor, wall).Count(SubKind.Face));
    }
}
