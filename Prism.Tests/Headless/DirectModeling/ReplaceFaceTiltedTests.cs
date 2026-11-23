using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>
/// Replace Face onto a plane tilted to the face, and Merge Faces on faces a hair out of plane (owner report 2026-11-23: a split piece turned about its
/// corner and set flush against the wall grew a step beside its long side, and the two side faces then would not merge).
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct Modeling guide.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public class ReplaceFaceTiltedTests
{
    static SceneObject Prism(CadSession s, (double x, double y)[] outline, double height, string name)
    {
        var loop = Occ.Polyline(outline.Select(p => new Vec3(p.x, p.y, 0)).ToList(), true);
        return s.AddObject(Occ.Extrude(Occ.Profile([loop], true), new Vec3(0, 0, height)), name, ObjKind.Solid);
    }

    static SelItem FaceWhere(SceneObject o, Func<FaceInfo, ShapeProps, bool> test) => new(o.Id, SelKind.Face,
        Enumerable.Range(0, o.Shape.Count(SubKind.Face)).Single(i => { using var f = o.Shape.Sub(SubKind.Face, i); return test(Occ.FaceInfoOf(f), f.Props()); }));

    static double Area((double x, double y)[] p) => Math.Abs(Enumerable.Range(0, p.Length).Sum(i => p[i].x * p[(i + 1) % p.Length].y - p[(i + 1) % p.Length].x * p[i].y)) / 2;

    [AvaloniaFact(DisplayName = "DIR-H202 | Replace Face sets the tilted end of a tapered bar onto a wall: the long sides stretch to the wall, with no step and no extra face")]
    [Trait("TestId", "DIR-H202")]
    public void TiltedEndStretchesItsSides()
    {
        var s = new CadSession();
        var wall = CreateBoxBody(s, new Vec3(0, -30, 0), 40, 60, 20);
        // a tapered bar ending at an angle short of the wall x = 0: its top side runs along y = 10, its bottom side slopes slightly
        (double x, double y)[] bar = [(-60, 10), (-60, 4), (-2.2, 5.4), (0, 10)];
        var piece = Prism(s, bar, 20, "Piece");
        var end = FaceWhere(piece, (i, p) => i.Type == "Plane" && i.Normal.X > 0.5);
        var target = FaceWhere(wall, (i, p) => i.Type == "Plane" && i.Normal.X < -0.99);
        var replace = Begin(s, () => new ReplaceFaceCommand());
        Pick(replace, "Flat faces to replace", end);
        Pick(replace, "Target plane or flat face", target);
        using var result = Assert.Single(BuildOutput(replace).Replace).New;
        Assert.True(result.IsValid);
        Assert.Equal(6, result.Count(SubKind.Face));                       // the end moved; no facet beside it
        // the bottom side, from (-60, 4) through (-2.2, 5.4), carried on to the wall
        double slope = (5.4 - 4) / (60 - 2.2), yAtWall = 4 + slope * 60;
        AssertClose(Area([(-60, 10), (-60, 4), (0, yAtWall), (0, 10)]) * 20, result.Props().Volume, 1e-3, "bar with its sides stretched to the wall (mm³)");
        AssertClose(0, result.Props().Max.X, 1e-6, "flush with the wall");
    }

    [AvaloniaFact(DisplayName = "DIR-H203 | Replace Face raises a step's top beside a taller block without spilling past the wall it meets")]
    [Trait("TestId", "DIR-H203")]
    public void StepBesideAWallStaysBounded()
    {
        var s = new CadSession();
        // an L in section: a 20 x 10 x 10 base with a 10 x 10 x 10 tower on its left half; the step's top meets the tower wall (a concave edge)
        var section = Occ.Polyline([new(0, 0, 0), new(20, 0, 0), new(20, 0, 10), new(10, 0, 10), new(10, 0, 20), new(0, 0, 20)], true);
        var body = s.AddObject(Occ.Extrude(Occ.Profile([section], true), new Vec3(0, 10, 0)), "Step", ObjKind.Solid);
        var stepTop = FaceWhere(body, (i, p) => i.Type == "Plane" && i.Normal.Z > 0.99 && Math.Abs(p.Center.Z - 10) < 1e-6);
        var replace = Begin(s, () => new ReplaceFaceCommand());
        Pick(replace, "Flat faces to replace", stepTop);
        Pick(replace, "Target plane or flat face", s.AddObject(Occ.Translate(Occ.Box(40, 40, 1), new Vec3(-10, -10, 14)), "Slab", ObjKind.Solid) is var slab
            ? FaceWhere(slab, (i, p) => i.Type == "Plane" && i.Normal.Z > 0.99) : default);
        using var result = Assert.Single(BuildOutput(replace).Replace).New;
        Assert.True(result.IsValid);
        AssertClose(3000 + 10 * 10 * 5, result.Props().Volume, 1e-3, "step raised 5 mm over its own 10 x 10 top (mm³)");
        AssertClose(20, result.Props().Max.X, 1e-6, "nothing grew past the step's outer side");
    }

    [AvaloniaFact(DisplayName = "DIR-H204 | Merge Faces joins two side faces that lie 0.00005 mm out of one plane")]
    [Trait("TestId", "DIR-H204")]
    public void MergesNearlyCoplanarFaces()
    {
        var s = new CadSession();
        var body = Prism(s, [(0, 0), (10, 0), (20, 0.00005), (20, 10), (0, 10)], 10, "Nearly flat side");
        var sides = Enumerable.Range(0, body.Shape.Count(SubKind.Face))
            .Where(i => { using var f = body.Shape.Sub(SubKind.Face, i); var info = Occ.FaceInfoOf(f); return info.Normal.Y < -0.99; })
            .Select(i => new SelItem(body.Id, SelKind.Face, i)).ToArray();
        Assert.Equal(2, sides.Length);
        var merge = Begin(s, () => new MergeFacesCommand());
        Pick(merge, "Faces to merge (neighbours on one surface)", sides);
        using var merged = Assert.Single(BuildOutput(merge).Replace).New;
        Assert.Equal(body.Shape.Count(SubKind.Face) - 1, merged.Count(SubKind.Face));
    }
}
