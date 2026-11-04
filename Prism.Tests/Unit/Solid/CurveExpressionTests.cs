using Prism.Core;
using Xunit;

namespace Prism.UnitTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/curves/README.md">Shared curve workflow.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Unit")]
public class CurveExpressionTests
{
    [Fact(DisplayName = "SOL-U004 | Curve formulas support implied multiplication and degree angles")]
    [Trait("TestId", "SOL-U004")]
    public void FormulaUsesCurveParameterInDegrees()
    {
        var expression = Expression.Parse("2t + 4 cos(180t)");
        Assert.Equal(4, expression.Eval(0), precision: 12);
        Assert.Equal(1, expression.Eval(0.5), precision: 12);
        Assert.Equal(-2, expression.Eval(1), precision: 12);
    }
}
