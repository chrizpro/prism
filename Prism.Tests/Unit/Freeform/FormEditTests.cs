using Prism.Core.Form;
using Prism.Kernel;
using Xunit;

namespace Prism.UnitTests.Freeform;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/freeform/README.md#cage-edit-checks">Freeform cage edit checks.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Unit")]
public class FormEditTests
{
    /// <summary>Volume enclosed by a closed cage, from its faces (positive when the faces run counter-clockwise seen from outside).</summary>
    static double Volume(SubdMesh m)
    {
        double sum = 0;
        foreach (var f in m.F)
            for (int i = 1; i + 1 < f.Length; i++)
                sum += Vec3.Dot(m.V[f[0]], Vec3.Cross(m.V[f[i]], m.V[f[i + 1]])) / 6;
        return sum;
    }

    /// <summary>Every edge is shared by exactly two faces that run opposite ways, no face repeats a point, and every point is used.</summary>
    static void AssertClosedManifold(SubdMesh m)
    {
        var directed = new Dictionary<(int, int), int>();
        foreach (var f in m.F)
        {
            Assert.True(f.Length >= 3 && f.Distinct().Count() == f.Length, "a face repeats a point or has fewer than three");
            for (int i = 0; i < f.Length; i++) directed[(f[i], f[(i + 1) % f.Length])] = directed.GetValueOrDefault((f[i], f[(i + 1) % f.Length])) + 1;
        }
        foreach (var (edge, count) in directed)
        {
            Assert.Equal(1, count);
            Assert.True(directed.ContainsKey((edge.Item2, edge.Item1)), $"edge {edge} has no partner running the other way");
        }
        Assert.True(m.IsClosed());
        Assert.Equal(m.V.Count, m.F.SelectMany(f => f).Distinct().Count());
    }

    static int Euler(SubdMesh m) => m.V.Count - m.Edges().Count() + m.F.Count;

    static int TopFace(SubdMesh m) => Enumerable.Range(0, m.F.Count).First(i => m.FaceNormal(i).Z > .99 && m.FaceCenter(i).Z > 0);

    static SubdMesh Cube() => FormOps.Box(2, 2, 2, 1, 1, 1);

    [Fact(DisplayName = "FRM-U010 | A box cage is a closed manifold of volume 8 and Euler number 2")]
    [Trait("TestId", "FRM-U010")]
    public void BoxIsAClosedManifold()
    {
        var cube = Cube();
        AssertClosedManifold(cube);
        Assert.Equal(8, cube.V.Count); Assert.Equal(6, cube.F.Count);
        Assert.Equal(8, Volume(cube), 9);
        Assert.Equal(2, Euler(cube));
    }

    [Fact(DisplayName = "FRM-U011 | Extruding the top face of a cube 1 mm adds four walls and 4 mm³")]
    [Trait("TestId", "FRM-U011")]
    public void ExtrudeAFace()
    {
        var cube = Cube();
        FormOps.ExtrudeFaces(cube, new HashSet<int> { TopFace(cube) }, 1);
        AssertClosedManifold(cube);
        Assert.Equal(10, cube.F.Count); Assert.Equal(12, cube.V.Count);
        Assert.Equal(12, Volume(cube), 9);
        Assert.Equal(2, Euler(cube));
    }

    [Fact(DisplayName = "FRM-U012 | Extruding a 2 x 2 region of the top moves the inner point with it and leaves no stray points")]
    [Trait("TestId", "FRM-U012")]
    public void ExtrudeARegion()
    {
        var block = FormOps.Box(2, 2, 2, 2, 2, 1);
        var top = Enumerable.Range(0, block.F.Count).Where(i => block.FaceNormal(i).Z > .99 && block.FaceCenter(i).Z > 0).ToHashSet();
        Assert.Equal(4, top.Count);
        FormOps.ExtrudeFaces(block, top, 1.5);
        AssertClosedManifold(block);
        Assert.Equal(8 + 4 * 1.5, Volume(block), 9);
        Assert.Equal(2, Euler(block));
    }

    [Fact(DisplayName = "FRM-U013 | An edge loop through a cube adds four faces and leaves the volume alone")]
    [Trait("TestId", "FRM-U013")]
    public void InsertAnEdgeLoop()
    {
        var cube = Cube();
        var (a, b) = cube.Edges().First(e => Math.Abs(cube.V[e.a].Z - cube.V[e.b].Z) > 1);      // a vertical edge: the loop runs round the walls
        var added = FormOps.InsertEdgeLoop(cube, a, b);
        AssertClosedManifold(cube);
        Assert.Equal(4, added.Count);
        Assert.Equal(10, cube.F.Count);
        Assert.Equal(8, Volume(cube), 9);
        Assert.All(added, v => Assert.Equal(0, Math.Abs(cube.V[v].Z - 1), 9));
        Assert.Equal(2, Euler(cube));
    }

    [Fact(DisplayName = "FRM-U014 | Subdividing a cube's top face into 2 x 2 gives nine faces and the same volume")]
    [Trait("TestId", "FRM-U014")]
    public void SubdivideAFace()
    {
        var cube = Cube();
        Assert.True(FormOps.SubdivideFaces(cube, new HashSet<int> { TopFace(cube) }, 2, 2) > 0);
        AssertClosedManifold(cube);
        Assert.Equal(9, cube.F.Count);
        Assert.Equal(8, Volume(cube), 9);
        Assert.Equal(2, Euler(cube));
    }

    [Fact(DisplayName = "FRM-U015 | Inset adds a ring of four faces; with depth the inner face rises by that depth")]
    [Trait("TestId", "FRM-U015")]
    public void InsetAFace()
    {
        var cube = Cube();
        FormOps.InsetFaces(cube, new HashSet<int> { TopFace(cube) }, .5);
        AssertClosedManifold(cube);
        Assert.Equal(10, cube.F.Count);
        Assert.Equal(8, Volume(cube), 9);

        var raised = Cube();
        FormOps.InsetFaces(raised, new HashSet<int> { TopFace(raised) }, .5, depth: .5);
        AssertClosedManifold(raised);
        // the amount is how far each corner moves toward the middle along the diagonal, so 0.5 leaves a ring 0.354 mm wide and a 1.293 mm inner square
        double inner = 2 * (1 - .5 / Math.Sqrt(2)), frustum = .5 / 3 * (4 + inner * inner + 2 * inner);
        Assert.Equal(8 + frustum, Volume(raised), 9);
    }

    [Fact(DisplayName = "FRM-U016 | Bridging the facing faces of two stacked cubes closes one cage with the gap's volume added")]
    [Trait("TestId", "FRM-U016")]
    public void BridgeTwoFaces()
    {
        var both = Cube(); var upper = Cube();
        int offset = both.V.Count;
        both.V.AddRange(upper.V.Select(v => v + new Vec3(0, 0, 3)));        // the second cube stands 1 mm above the first
        both.F.AddRange(upper.F.Select(f => f.Select(i => i + offset).ToArray()));
        int lowerTop = Enumerable.Range(0, 6).First(i => both.FaceNormal(i).Z > .99 && both.FaceCenter(i).Z > 0);
        int upperBottom = Enumerable.Range(6, 6).First(i => both.FaceNormal(i).Z < -.99);
        FormOps.BridgeFaces(both, lowerTop, upperBottom);
        AssertClosedManifold(both);
        Assert.Equal(6 + 6 - 2 + 4, both.F.Count);
        Assert.Equal(8 + 8 + 4 * 1, Volume(both), 9);
        Assert.Equal(2, Euler(both));      // two spheres joined by a tube are still one sphere
    }

    [Fact(DisplayName = "FRM-U017 | Mirror and weld doubles a half-plane across its edge and welds the seam")]
    [Trait("TestId", "FRM-U017")]
    public void MirrorAndWeld()
    {
        var half = FormOps.Plane(2, 2, 1, 1);                               // spans x from -1 to 1
        FormOps.MirrorWeld(half, new Vec3(1, 0, 0), new Vec3(1, 0, 0));      // mirror across x = 1
        Assert.Equal(2, half.F.Count);
        Assert.Equal(6, half.V.Count);
        var (min, max) = half.Bounds();
        Assert.Equal(4, max.X - min.X, 9);
    }

    [Fact(DisplayName = "FRM-U018 | Merging two neighbouring faces gives one six-sided face")]
    [Trait("TestId", "FRM-U018")]
    public void MergeFaces()
    {
        var strip = FormOps.Plane(2, 1, 2, 1);
        Assert.True(FormOps.MergeFaces(strip, new HashSet<int> { 0, 1 }));
        Assert.Single(strip.F);
        Assert.Equal(6, strip.F[0].Length);
    }

    [Fact(DisplayName = "FRM-U019 | Collapsing one cube edge leaves a closed cage of seven points")]
    [Trait("TestId", "FRM-U019")]
    public void CollapseAnEdge()
    {
        var cube = Cube();
        Assert.True(FormOps.CollapseEdges(cube, [cube.Edges().First()]) > 0);
        AssertClosedManifold(cube);
        Assert.Equal(7, cube.V.Count);
        Assert.Equal(2, Euler(cube));
    }

    [Fact(DisplayName = "FRM-U020 | Splitting a quad between opposite corners makes two triangles")]
    [Trait("TestId", "FRM-U020")]
    public void SplitAFace()
    {
        var quad = FormOps.Plane(1, 1, 1, 1);
        Assert.True(FormOps.SplitFace(quad, 0, quad.F[0][0], quad.F[0][2]));
        Assert.Equal(2, quad.F.Count);
        Assert.All(quad.F, f => Assert.Equal(3, f.Length));
    }

    [Fact(DisplayName = "FRM-U021 | Deleting the top of a cube opens it; filling the hole closes it again")]
    [Trait("TestId", "FRM-U021")]
    public void DeleteThenFill()
    {
        var cube = Cube();
        FormOps.DeleteFaces(cube, new HashSet<int> { TopFace(cube) });
        Assert.Equal(5, cube.F.Count);
        Assert.False(cube.IsClosed());
        Assert.True(FormOps.FillHoles(cube) > 0);
        AssertClosedManifold(cube);
        Assert.Equal(6, cube.F.Count);
        Assert.Equal(8, Math.Abs(Volume(cube)), 9);
    }

    [Fact(DisplayName = "FRM-U022 | Flipping normals turns the volume inside out")]
    [Trait("TestId", "FRM-U022")]
    public void FlipNormals()
    {
        var cube = Cube();
        FormOps.FlipNormals(cube);
        Assert.Equal(-8, Volume(cube), 9);
        AssertClosedManifold(cube);
    }

    [Fact(DisplayName = "FRM-U023 | Chamfering the four top edges of a cube cuts a strip along each; one edge on its own does nothing")]
    [Trait("TestId", "FRM-U023")]
    public void ChamferAnEdgeLoop()
    {
        var cube = Cube();
        var top = cube.Edges().Where(e => cube.V[e.a].Z > 0 && cube.V[e.b].Z > 0).ToList();
        Assert.Equal(4, top.Count);
        Assert.True(FormOps.ChamferEdges(cube, top, .5) > 0);
        AssertClosedManifold(cube);
        Assert.Equal(10, cube.F.Count);      // six faces and a strip along each of the four edges
        // the width is how far each new point moves from the old corner toward the middle of its faces, not the length of the cut's legs,
        // so 0.5 takes off about 2 mm³ here rather than the 1 mm³ a 0.5 mm leg would
        Assert.InRange(Volume(cube), 5.9, 6.1);
        Assert.Equal(2, Euler(cube));

        // a single edge ends in two points where nothing else is cut, so there is no strip to build there
        var alone = Cube();
        Assert.Equal(0, FormOps.ChamferEdges(alone, [alone.Edges().First()], .5));
        Assert.Equal(6, alone.F.Count);
    }

    [Fact(DisplayName = "FRM-U024 | Solidify gives a 2 x 2 plane a wall: volume 4 x thickness")]
    [Trait("TestId", "FRM-U024")]
    public void SolidifyAPlane()
    {
        var plane = FormOps.Plane(2, 2, 1, 1);
        FormOps.Solidify(plane, .5);
        AssertClosedManifold(plane);
        Assert.Equal(2, Math.Abs(Volume(plane)), 9);
    }

    [Fact(DisplayName = "FRM-U025 | Flatten presses a raised point of the top onto the plane of the others, and Smooth relaxes a bump")]
    [Trait("TestId", "FRM-U025")]
    public void FlattenAndSmooth()
    {
        var cube = Cube();
        int bump = cube.V.Select((v, i) => (v, i)).First(p => p.v.Z > 0).i;
        cube.V[bump] = cube.V[bump] + new Vec3(0, 0, .8);
        var top = Enumerable.Range(0, cube.V.Count).Where(i => cube.V[i].Z > 0).ToHashSet();
        Assert.True(FormOps.FlattenVertices(cube, top));
        var p = top.Select(i => cube.V[i]).ToArray();      // the four are coplanar when the tetrahedron they span has no volume
        Assert.Equal(0, Vec3.Dot(p[1] - p[0], Vec3.Cross(p[2] - p[0], p[3] - p[0])), 9);

        var sheet = FormOps.Plane(4, 4, 4, 4);
        int centre = sheet.V.Select((v, i) => (v, i)).First(p => Math.Abs(p.v.X) < 1e-9 && Math.Abs(p.v.Y) < 1e-9).i;
        sheet.V[centre] = new Vec3(0, 0, 1);
        FormOps.SmoothVertices(sheet, new HashSet<int> { centre }, 1, 1);
        Assert.True(sheet.V[centre].Z < .2, $"the bump should relax toward its neighbours, was {sheet.V[centre].Z}");
    }

    [Fact(DisplayName = "FRM-U026 | One Catmull-Clark step on a cube gives 24 faces and 26 points and keeps it closed")]
    [Trait("TestId", "FRM-U026")]
    public void RefineACube()
    {
        var refined = Cube().Refine(out _);
        AssertClosedManifold(refined);
        Assert.Equal(24, refined.F.Count); Assert.Equal(26, refined.V.Count);
        Assert.True(Volume(refined) < 8 && Volume(refined) > 3, "the smoothed cube shrinks but stays a solid blob");
    }

    [Fact(DisplayName = "FRM-U027 | A crease holds for as many smoothing levels as its sharpness: 3 keeps a cube sharp through 3 levels, 1 does not")]
    [Trait("TestId", "FRM-U027")]
    public void CreasedCubeStaysSharp()
    {
        var cube = Cube();
        var soft = Cube();
        FormOps.SetCrease(cube, cube.Edges(), 3);
        FormOps.SetCrease(soft, soft.Edges(), 1);
        for (int i = 0; i < 3; i++) { cube = cube.Refine(out _); soft = soft.Refine(out _); }
        AssertClosedManifold(cube);
        Assert.Equal(8, Volume(cube), 6);
        Assert.True(Volume(soft) < 7, $"a sharpness of 1 holds only the first level, so three levels round the cube off (volume {Volume(soft):0.##})");
    }

    [Fact(DisplayName = "FRM-U030 | A plane through the middle of a cube cuts its four walls: 4 new points, 10 faces, volume 8")]
    [Trait("TestId", "FRM-U030")]
    public void KnifeThroughTheMiddle()
    {
        var cube = Cube();
        var added = FormOps.CutWithPlane(cube, new Vec3(0, 0, 1), Vec3.UnitZ, out int cut);
        AssertClosedManifold(cube);
        Assert.Equal(4, added.Count); Assert.Equal(4, cut);
        Assert.Equal(10, cube.F.Count);
        Assert.Equal(8, Volume(cube), 9);
        Assert.All(added, v => Assert.Equal(1, cube.V[v].Z, 9));
        Assert.Equal(2, Euler(cube));
    }

    [Fact(DisplayName = "FRM-U031 | A plane through two opposite edges splits the top and bottom along their diagonals without adding points")]
    [Trait("TestId", "FRM-U031")]
    public void KnifeThroughExistingPoints()
    {
        var cube = Cube();
        var added = FormOps.CutWithPlane(cube, Vec3.Zero + new Vec3(0, 0, 1), new Vec3(1, 1, 0), out int cut);
        AssertClosedManifold(cube);
        Assert.Empty(added); Assert.Equal(2, cut);
        Assert.Equal(8, cube.V.Count); Assert.Equal(8, cube.F.Count);
        Assert.Equal(8, Volume(cube), 9);
    }

    [Fact(DisplayName = "FRM-U032 | A slanted plane through a quad-ball keeps it closed and puts every new point on the plane")]
    [Trait("TestId", "FRM-U032")]
    public void KnifeAcrossASphere()
    {
        var ball = FormOps.Sphere(10, 4);
        double before = Volume(ball); int facesBefore = ball.F.Count;
        var plane = (origin: new Vec3(1, 0.5, 0.2), normal: new Vec3(.3, .5, .8));
        var added = FormOps.CutWithPlane(ball, plane.origin, plane.normal, out int cut);
        AssertClosedManifold(ball);
        Assert.NotEmpty(added); Assert.True(cut > 0);
        Assert.Equal(facesBefore + cut, ball.F.Count);
        Assert.Equal(before, Volume(ball), before * 1e-3);      // the quads are not flat, so cutting one changes how it is triangulated a little
        Assert.All(added, v => Assert.Equal(0, Vec3.Dot(ball.V[v] - plane.origin, plane.normal.Normalized()), 9));
        Assert.Equal(2, Euler(ball));
    }

    [Fact(DisplayName = "FRM-U033 | A plane that misses the cage changes nothing; a crease option sharpens the cut")]
    [Trait("TestId", "FRM-U033")]
    public void KnifeMissesAndCreases()
    {
        var cube = Cube();
        Assert.Empty(FormOps.CutWithPlane(cube, new Vec3(0, 0, 5), Vec3.UnitZ, out int none));
        Assert.Equal(0, none); Assert.Equal(6, cube.F.Count); Assert.Equal(8, cube.V.Count);

        FormOps.CutWithPlane(cube, new Vec3(0, 0, 1), Vec3.UnitZ, out _, crease: 2);
        Assert.Equal(4, cube.Crease.Count(c => c.Value == 2));
    }
}
