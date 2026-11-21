using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>Edit Distance (KeyCreator-style editing by dimension) on parts imported without history.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct Modeling guide, section 6 "Edit Distance".</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public sealed class EditDistanceTests
{
    static int FaceWhere(SceneObject body, Func<FaceInfo, ShapeProps, bool> pick) =>
        Enumerable.Range(0, body.Shape!.Count(SubKind.Face)).Single(i => { using var f = body.Shape.Sub(SubKind.Face, i); return pick(Occ.FaceInfoOf(f), f.Props()); });

    static EditDistanceCommand Start(CadSession session, SceneObject body, int move, int from)
    {
        session.SelectItems([]);
        session.StartCommand(() => new EditDistanceCommand());
        var c = Assert.IsType<EditDistanceCommand>(session.ActiveCommand);
        c.ConfigureInputs(() =>
        {
            Assert.True(c.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, move)));
            Assert.True(c.Slots[1].TryAdd(new SelItem(body.Id, SelKind.Face, from)));
        });
        c.TryBuild();   // a preview with nothing changed yet fills in the current distance
        return c;
    }

    static Shape Result(EditDistanceCommand c)
    {
        var output = c.TryBuild();
        Assert.True(output != null, c.Error);
        var shape = Assert.Single(output.Replace).New;
        AssertValidSolid(shape);
        return shape;
    }

    static double Distance(Command c) => c.Params.OfType<NumberParam>().Single(p => p.Label == "Distance").Value;

    [AvaloniaFact(DisplayName = "DIR-H106 | Edit Distance moves a hole from 20 mm to 35 mm off an edge and keeps its size")]
    [Trait("TestId", "DIR-H106")]
    public void HoleFromEdge()
    {
        using var plate = BoxAt(new Vec3(0, 0, 0), 100, 60, 10);
        using var drill = CylinderAt(new Vec3(20, 30, -1), 4, 12);
        using var part = Occ.Cut(plate, drill);
        var (session, body) = FeatureRecognitionTests.Imported(part);
        int hole = FeatureFinder.Features(body).Single(f => f.Kind == FeatureKind.Hole).Face;
        int edge = FaceWhere(body, (i, p) => i.Type == "Plane" && p.Max.X < 1e-6);
        var c = Start(session, body, hole, edge);
        AssertClose(20, Distance(c), 1e-6, "distance shown when picked");
        c.ConfigureInputs(() => SetNumber(c, "Distance", 35));
        using var result = Result(c);
        AssertClose(part.Props().Volume, result.Props().Volume, 1e-3, "volume (the hole only moved)");
        var check = session.AddObject(result, "Moved", ObjKind.Solid);
        var moved = FeatureFinder.Features(check).Single(f => f.Kind == FeatureKind.Hole);
        AssertClose(35, moved.Round!.Origin.X, 1e-4, "hole centre X");
        AssertClose(4, moved.Round.Radius, 1e-6, "hole radius");
    }

    [AvaloniaTheory(DisplayName = "DIR-H107 | Edit Distance between parallel faces: 40 mm to 30 mm moves the picked face, or both faces by 5 mm when Symmetric")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("TestId", "DIR-H107")]
    public void ParallelFaces(bool symmetric)
    {
        using var block = BoxAt(new Vec3(0, 0, 0), 40, 30, 10);
        var (session, body) = FeatureRecognitionTests.Imported(block);
        int right = FaceWhere(body, (i, p) => i.Type == "Plane" && p.Min.X > 40 - 1e-6);
        int left = FaceWhere(body, (i, p) => i.Type == "Plane" && p.Max.X < 1e-6);
        var c = Start(session, body, right, left);
        AssertClose(40, Distance(c), 1e-6, "distance shown when picked");
        c.ConfigureInputs(() => { SetNumber(c, "Distance", 30); c.Params.OfType<BoolParam>().Single().Value = symmetric; });
        using var result = Result(c);
        AssertClose(30 * 30 * 10, result.Props().Volume, 1e-3, "volume");
        AssertClose(symmetric ? 5 : 0, result.Props().Min.X, 1e-6, "left face");
        AssertClose(symmetric ? 35 : 30, result.Props().Max.X, 1e-6, "right face");
    }

    [AvaloniaFact(DisplayName = "DIR-H108 | Edit Distance sets the centre distance of two holes, and refuses faces that are not parallel")]
    [Trait("TestId", "DIR-H108")]
    public void HoleToHoleAndRefusal()
    {
        using var plate = BoxAt(new Vec3(0, 0, 0), 100, 60, 10);
        using var a = CylinderAt(new Vec3(20, 30, -1), 3, 12);
        using var b = CylinderAt(new Vec3(50, 30, -1), 3, 12);
        using var c1 = Occ.Cut(plate, a); using var part = Occ.Cut(c1, b);
        var (session, body) = FeatureRecognitionTests.Imported(part);
        var holes = FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Hole).OrderBy(f => f.Centre.X).ToList();
        var c = Start(session, body, holes[1].Face, holes[0].Face);
        AssertClose(30, Distance(c), 1e-6, "centre distance shown");
        c.ConfigureInputs(() => SetNumber(c, "Distance", 45));
        using var result = Result(c);
        var check = session.AddObject(result, "Moved", ObjKind.Solid);
        var xs = FeatureFinder.Features(check).Where(f => f.Kind == FeatureKind.Hole).Select(f => Math.Round(f.Round!.Origin.X, 4)).Order().ToArray();
        Assert.Equal(new[] { 20.0, 65.0 }, xs);
        session.CancelCommand();

        int top = FaceWhere(body, (i, p) => i.Type == "Plane" && p.Min.Z > 10 - 1e-6);
        int side = FaceWhere(body, (i, p) => i.Type == "Plane" && p.Max.X < 1e-6);
        var bad = Start(session, body, top, side);
        bad.ConfigureInputs(() => SetNumber(bad, "Distance", 5));
        Assert.Null(bad.TryBuild());
        Assert.Contains("not parallel", bad.Error);
    }
}
