using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/solid-details/README.md#emboss-on-a-curved-face">Emboss on a curved face.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class EmbossSurfaceTests
{
    const double Radius = 20, Length = 40, Width = 12, Tall = 10;

    /// <summary>The 12 mm wide patch of the drum's front, thickened along the surface normals, is a ring sector: volume = length * half the angle * the difference of the squared radii.</summary>
    static double Sector(double inner, double outer) => Tall * Math.Asin(Width / 2 / Radius) * (outer * outer - inner * inner);

    static (CadSession session, SceneObject drum, SceneObject sketch) Drum()
    {
        var session = new CadSession();
        var drum = session.AddObject(CylinderAt(new(0, 0, 0), Radius, Length), "Drum", ObjKind.Solid);
        var sketch = CreateRectangleSketch(session, Plane3.XZ with { Origin = new Vec3(0, -30, 0) }, Width, Tall, startU: -Width / 2, startV: 15);
        return (session, drum, sketch);
    }

    static EmbossCommand Start(CadSession session, SceneObject sketch, string style, double height, string method = "Automatic")
    {
        var command = Begin(session, () => new EmbossCommand());
        Pick(command, "Sketch (text or shape)", sketch);
        SetChoice(command, "Style", style); SetNumber(command, "Height / depth", height); SetChoice(command, "Method", method);
        return command;
    }

    [AvaloniaFact(DisplayName = "SOL-H170 | Emboss on a cylinder lifts the patch under the shape by the height along the surface: only the front, to the exact volume")]
    [Trait("TestId", "SOL-H170")]
    public void RaisesFollowingTheSurface()
    {
        var (session, drum, sketch) = Drum();
        double before = drum.Shape.Props().Volume, h = 1.5;
        var command = Start(session, sketch, "Emboss (raised)", h);
        var (_, result) = Assert.Single(BuildOutput(command).Replace);
        Assert.True(result.IsValid);
        double expected = Sector(Radius, Radius + h);
        AssertClose(expected, result.Props().Volume - before, expected * .02, "Volume added (mm³)");
        // nothing on the back, and the top follows the surface: every point of the new material is within the band radius R to R + h
        var box = result.Props();
        Assert.True(box.Max.Y <= Radius + 1e-6 && box.Min.Y >= -(Radius + h) - 1e-6, "the raised letters stand out of the front only");
        AssertClose(-(Radius + h), box.Min.Y, 0.05, "Front of the raised patch (mm)");
        Assert.Contains("patch", Result(command, "Follows the surface"));
    }

    [AvaloniaFact(DisplayName = "SOL-H171 | Engrave on a cylinder sinks the patch by the depth along the surface, to the exact volume")]
    [Trait("TestId", "SOL-H171")]
    public void EngravesFollowingTheSurface()
    {
        var (session, drum, sketch) = Drum();
        double before = drum.Shape.Props().Volume, depth = 1.5;
        var (_, result) = Assert.Single(BuildOutput(Start(session, sketch, "Engrave (cut in)", depth)).Replace);
        Assert.True(result.IsValid);
        double expected = Sector(Radius - depth, Radius);
        AssertClose(expected, before - result.Props().Volume, expected * .02, "Volume removed (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H172 | Limiting Emboss to one face gives the same result, and Follow the surface on a flat face matches a straight extrude")]
    [Trait("TestId", "SOL-H172")]
    public void FaceLimitAndFlatFaces()
    {
        var (session, drum, sketch) = Drum();
        double before = drum.Shape.Props().Volume;
        int side = Enumerable.Range(0, drum.Shape.Count(SubKind.Face)).First(i => { using var f = drum.Shape.Sub(SubKind.Face, i); return Occ.FaceInfoOf(f).Type == "Cylinder"; });
        var limited = Start(session, sketch, "Emboss (raised)", 1);
        Pick(limited, "Face to emboss onto (optional)", new SelItem(drum.Id, SelKind.Face, side));
        double added = Assert.Single(BuildOutput(limited).Replace).Item2.Props().Volume - before;
        Assert.InRange(added, 1, 1e4);

        var flat = new CadSession();
        var block = CreateBoxBody(flat, Vec3.Zero, 10, 10, 10);
        var profile = CreateRectangleSketch(flat, Plane3.XY with { Origin = new Vec3(0, 0, 10) }, 2, 3, 2, 2);
        var command = Begin(flat, () => new EmbossCommand());
        Pick(command, "Sketch (text or shape)", profile);
        SetChoice(command, "Method", "Follow the surface");
        AssertClose(1006, Assert.Single(BuildOutput(command).Replace).Item2.Props().Volume, 0.05, "Followed flat face (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H173 | Follow the surface says so when the shape does not fall on any face")]
    [Trait("TestId", "SOL-H173")]
    public void NothingUnderTheShape()
    {
        var session = new CadSession();
        session.AddObject(CylinderAt(new(0, 0, 0), Radius, Length), "Drum", ObjKind.Solid);
        var sketch = CreateRectangleSketch(session, Plane3.XZ with { Origin = new Vec3(0, -30, 0) }, 12, 10, startU: 100, startV: 15);      // off to the side of the drum
        AssertBuildFails(Start(session, sketch, "Emboss (raised)", 1, "Follow the surface"), "does not fall");
    }

    [AvaloniaFact(DisplayName = "SOL-H174 | A shape with a hole, like the letter O, leaves the hole bare: outer patch minus inner patch")]
    [Trait("TestId", "SOL-H174")]
    public void ShapeWithAHole()
    {
        var session = new CadSession();
        var drum = session.AddObject(CylinderAt(new(0, 0, 0), Radius, Length), "Drum", ObjKind.Solid);
        var model = new Prism.Core.Sketching.SketchModel();
        void Square(double half, double cz)
        {
            int a = model.AddPoint(-half, cz - half), b = model.AddPoint(half, cz - half), c = model.AddPoint(half, cz + half), d = model.AddPoint(-half, cz + half);
            model.AddLine(a, b); model.AddLine(b, c); model.AddLine(c, d); model.AddLine(d, a);
        }
        Square(6, 20); Square(3, 20);
        session.EnterSketch(Plane3.XZ with { Origin = new Vec3(0, -30, 0) }, model);
        session.ExitSketch(keep: true);
        var sketch = session.Objects.Last(o => o.Kind == ObjKind.Sketch);
        double before = drum.Shape.Props().Volume, h = 1;
        var (_, result) = Assert.Single(BuildOutput(Start(session, sketch, "Emboss (raised)", h)).Replace);
        Assert.True(result.IsValid);
        double band = (Radius + h) * (Radius + h) - Radius * Radius;
        double expected = 12 * Math.Asin(6 / Radius) * band - 6 * Math.Asin(3 / Radius) * band;     // outer 12 mm patch less the inner 6 mm one: length * half-angle * band
        AssertClose(expected, result.Props().Volume - before, expected * .03, "Frame volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H175 | Emboss follows a sphere too: a valid solid with the surface rising by the height at the middle of the shape")]
    [Trait("TestId", "SOL-H175")]
    public void FollowsASphere()
    {
        var session = new CadSession();
        var ball = session.AddObject(Occ.Sphere(Radius), "Ball", ObjKind.Solid);
        var sketch = CreateRectangleSketch(session, Plane3.XZ with { Origin = new Vec3(0, -30, 0) }, 10, 10, startU: -5, startV: -5);
        double before = ball.Shape.Props().Volume, h = 1;
        var (_, result) = Assert.Single(BuildOutput(Start(session, sketch, "Emboss (raised)", h)).Replace);
        Assert.True(result.IsValid);
        double flat = 10 * 10 * h;
        Assert.InRange(result.Props().Volume - before, flat * .95, flat * 1.3);       // a little more than the flat patch: it fans out as it rises
        AssertClose(-(Radius + h), result.Props().Min.Y, 0.05, "Front of the raised patch (mm)");
    }
}
