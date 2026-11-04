using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Revolve;

/// <summary>Managed axis/vector operations used by revolve and its direction handle.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/revolve/README.md">Canonical living Revolve specification and variant boundaries.</seealso>
[Trait("Layer", "Unit")]
[Trait("Feature", "Revolve")]
public class RevolveAxisMathTests
{
    [Fact(DisplayName = "REV-U001 | A scaled rotation axis normalizes to unit Z")]
    [Trait("TestId", "REV-U001")]
    public void ScaledRotationAxis_Normalized_IsUnitZ()
    {
        var pickedAxis = new Vec3(0, 0, 8);

        Assert.Equal(Vec3.UnitZ, pickedAxis.Normalized());
    }

    [Fact(DisplayName = "REV-U002 | Z axis crossed with positive radial X gives positive tangent Y")]
    [Trait("TestId", "REV-U002")]
    public void PositiveZAxis_CrossRadialX_ProducesPositiveTangentY()
    {
        var tangent = Vec3.Cross(Vec3.UnitZ, new Vec3(5, 0, 0)).Normalized();

        Assert.Equal(Vec3.UnitY, tangent);
        Assert.Equal(0, Vec3.Dot(tangent, Vec3.UnitZ));
    }

    [Fact(DisplayName = "REV-U003 | Reversing the rotation axis reverses the tangent")]
    [Trait("TestId", "REV-U003")]
    public void NegativeZAxis_CrossRadialX_ProducesNegativeTangentY()
    {
        var tangent = Vec3.Cross(-Vec3.UnitZ, new Vec3(5, 0, 0)).Normalized();

        Assert.Equal(-Vec3.UnitY, tangent);
    }

    [Fact(DisplayName = "REV-U004 | Projecting onto the rotation axis preserves signed axial distance")]
    [Trait("TestId", "REV-U004")]
    public void PointBelowAxisOrigin_DotUnitAxis_ReturnsNegativeAxialDistance()
    {
        var pointRelativeToAxisOrigin = new Vec3(5, 0, -8);

        var axialDistance = Vec3.Dot(pointRelativeToAxisOrigin, Vec3.UnitZ);

        Assert.Equal(-8, axialDistance);
    }
}
