using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.StaticAnalysis;

/// <summary>Production vector operations underlying face-load area/direction calculations.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/tabs/static-analysis/README.md">Living static structural analysis specification.</seealso>
[Trait("Layer", "Unit")]
[Trait("Feature", "StaticAnalysis")]
public class StaticLoadMathTests
{
    [Fact(DisplayName = "STA-U001 | A 10 by 10 right-triangle boundary has area 50 mm²")]
    [Trait("TestId", "STA-U001")]
    public void BoundaryTriangle_CrossProduct_GivesExpectedLoadedArea()
    {
        var firstEdge = new Vec3(10, 0, 0);
        var secondEdge = new Vec3(0, 10, 0);

        double area = Vec3.Cross(firstEdge, secondEdge).Length * 0.5;

        Assert.Equal(50, area);
    }

    [Fact(DisplayName = "STA-U002 | A nonunit direction vector does not multiply a 1000 N force")]
    [Trait("TestId", "STA-U002")]
    public void NonunitForceDirection_Normalized_PreservesSpecifiedMagnitude()
    {
        var force = new Vec3(0, 0, 7).Normalized() * 1000;

        Assert.Equal(new Vec3(0, 0, 1000), force);
        Assert.Equal(1000, force.Length);
    }
}
