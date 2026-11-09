using Prism.Core;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.SheetMetal;

/// <summary>The arithmetic behind Fold: bend allowance and where a region goes after a bend. Needs no kernel.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sheet-metal/README.md">Living guide.</seealso>
[Trait("Feature", "SheetMetal")]
[Trait("Layer", "Unit")]
public class SheetFoldTests
{
    const double T = 2, R = 3, K = 0.44;

    [Theory(DisplayName = "FLD-U001 | Bend allowance is theta times the inside radius plus K times the thickness")]
    [InlineData(90, 6.0947)]
    [InlineData(70, 4.7403)]
    [InlineData(180, 12.1894)]
    [Trait("TestId", "FLD-U001")]
    public void BendAllowanceFormula(double degrees, double expected) =>
        Assert.Equal(expected, SheetFold.BendAllowance(degrees * Math.PI / 180, R, K, T), 3);

    [Fact(DisplayName = "FLD-U002 | A quarter turn up puts the child's first flat point a neutral radius to the side and above")]
    [Trait("TestId", "FLD-U002")]
    public void QuarterTurnUp()
    {
        // bend line along Y at x = 10: the parent is the metal at smaller x, the child lies beyond; metal z in [0, 2]
        var n = Vec3.UnitZ; var d = new Vec3(0, -1, 0); var u = Vec3.Cross(n, d);
        double allowance = SheetFold.BendAllowance(Math.PI / 2, R, K, T), rho = R + K * T;
        var child = SheetFold.Child(Rigid.Identity, new Vec3(10, 0, 0), u, d, n, Math.PI / 2, +1, R, K, T, 0);
        Assert.Equal(1, u.X, 9);
        // the child's first flat point on its neutral surface (x = 10 + allowance / 2, z = 2 - K t) lands on the end of the bend arc
        var landing = child.Apply(new Vec3(10 + allowance / 2, 0, T - K * T));
        Assert.Equal(10 - allowance / 2 + rho, landing.X, 9);
        Assert.Equal(T - K * T + rho, landing.Z, 9);
        // beyond it the child runs straight up
        var up = child.Apply(new Vec3(10 + allowance / 2 + 20, 0, T - K * T));
        Assert.Equal(landing.X, up.X, 9);
        Assert.Equal(landing.Z + 20, up.Z, 9);
    }

    [Theory(DisplayName = "FLD-U003 | A chain of equal adjacent bends is the same as one bend through the total angle")]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(24)]
    [Trait("TestId", "FLD-U003")]
    public void ChainOfBendsEqualsOneArc(int count)
    {
        var n = Vec3.UnitZ; var d = new Vec3(0, -1, 0); var u = Vec3.Cross(n, d);
        double total = 120 * Math.PI / 180, r = 80;
        var one = SheetFold.Child(Rigid.Identity, new Vec3(30 + SheetFold.BendAllowance(total, r, K, T) / 2, 0, 0), u, d, n, total, +1, r, K, T, 0);
        var frame = Rigid.Identity; double x = 30; double step = total / count, zone = SheetFold.BendAllowance(step, r, K, T);
        for (int i = 0; i < count; i++) { frame = SheetFold.Child(frame, new Vec3(x + zone / 2, 0, 0), u, d, n, step, +1, r, K, T, 0); x += zone; }
        double end = 30 + SheetFold.BendAllowance(total, r, K, T) + 30;
        var a = one.Apply(new Vec3(end, 7, 1)); var b = frame.Apply(new Vec3(end, 7, 1));
        Assert.True((a - b).Length < 1e-9, $"tip positions differ by {(a - b).Length}");
    }
}
