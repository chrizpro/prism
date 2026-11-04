using Prism.Core.Form;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Freeform;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/freeform/README.md">Freeform Bridge options.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Unit")]
public class CageBridgeTests
{
    [Theory(DisplayName = "FRM-U003 | Bridge adds one face for open edges and four for closed loops")]
    [InlineData(false, 3)]
    [InlineData(true, 6)]
    [Trait("TestId", "FRM-U003")]
    public void BridgeHandlesOpenAndClosedBoundaries(bool closed, int faceCount)
    {
        var cage = TwoCaps();
        List<int> lower = closed ? [0, 1, 2, 3] : [0, 1];
        List<int> upper = closed ? [6, 5, 4, 7] : [5, 4];
        FormOps.Bridge(cage, lower, closed, upper, closed);
        Assert.Equal(faceCount, cage.F.Count);
        Assert.Equal(8, cage.V.Count);
        Assert.Equal(closed, cage.IsClosed());
        Assert.All(cage.F.Skip(2), face =>
        {
            Assert.Equal(4, face.Length);
            Assert.Equal(2, face.Count(vertex => vertex < 4));
        });
    }

    [Theory(DisplayName = "FRM-U004 | Bridge rejects incompatible selections before changing the cage")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("TestId", "FRM-U004")]
    public void InvalidBridgeLeavesCageUnchanged(bool mixedClosure)
    {
        var cage = TwoCaps();
        var before = cage.F.Select(face => face.ToArray()).ToArray();
        List<int> upper = mixedClosure ? [4, 5, 6, 7] : [4, 5, 6];
        Assert.Throws<InvalidOperationException>(() => FormOps.Bridge(cage, [0, 1, 2, 3], true, upper, !mixedClosure));
        Assert.Equal(before.Length, cage.F.Count);
        for (int index = 0; index < before.Length; index++)
            Assert.Equal(before[index], cage.F[index]);
    }

    static SubdMesh TwoCaps()
    {
        var cage = new SubdMesh();
        cage.V.AddRange([Vec3.Zero, new(2, 0, 0), new(2, 2, 0), new(0, 2, 0),
            new(0, 0, 3), new(2, 0, 3), new(2, 2, 3), new(0, 2, 3)]);
        cage.F.AddRange([[3, 2, 1, 0], [4, 5, 6, 7]]);
        return cage;
    }
}
