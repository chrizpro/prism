using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/FlattenRefold.md">Flatten and Refold guide</seealso>
[Trait("Feature", "Flatten / Refold")]
[Trait("Layer", "Headless")]
public class RefoldStressTests
{
    [AvaloniaFact(DisplayName = "Refold keeps near-seam holes and narrow webs on a tilted cylinder")]
    [Trait("TestId", "REF-H101")]
    public void TiltedPerforatedCylinder()
    {
        var session = new CadSession();
        const double radius = 18, height = 35, holeRadius = 2;
        using var cylinder = Occ.Cylinder(radius, height);
        using var tilted = Occ.Rotate(cylinder, Vec3.Zero, Vec3.UnitY, 41);
        var source = session.AddObject(Occ.Translate(tilted, new(80,-32,16)), "Tilted sleeve", ObjKind.Solid);
        int side = Enumerable.Range(0,3).Single(i => Occ.CylinderFrame(source.Shape.Sub(SubKind.Face,i)) != null);
        var flatten = Begin(session, () => new FlattenCommand());
        Pick(flatten, "Faces or a surface to flatten", new SelItem(source.Id, SelKind.Face, side));
        session.CommitCommand();
        var flat = session.Objects.Single(o => o.FlatMap != null && o.Kind == ObjKind.Surface);
        var map = flat.FlatMap!;
        double[] centers = [2.05, 6.1, map.Width / 2, map.Width - 2.05];
        var cutters = centers.Select(x => CylinderAt(new(map.FlatX + x, map.FlatY + height/2, -1), holeRadius, 2)).ToList();
        using var tool = Occ.Compound(cutters);
        var cut = Occ.Cut(flat.Shape, tool);
        session.Update(flat.Id, o => o.WithShape(cut));
        var refold = Begin(session, () => new RefoldCommand());
        Pick(refold, "Original flat pattern", session.Find(flat.Id)!);
        var result = Assert.Single(BuildOutput(refold).Add).Shape!;
        Assert.True(result.IsValid);
        Assert.Equal(5, result.Count(SubKind.Wire));
        var frame = map.Cylinder;
        var origin = new Vec3(frame[0], frame[1], frame[2]);
        var axis = new Vec3(frame[3], frame[4], frame[5]);
        var u = new Vec3(frame[6], frame[7], frame[8]);
        var v = Vec3.Cross(axis, u);
        double Clearance(double arc)
        {
            double angle = frame[10] + arc / radius;
            var position = origin + axis * (frame[12] + height / 2)
                + (u * Math.Cos(angle) + v * Math.Sin(angle)) * radius;
            using var point = Occ.Vertex(position);
            return Occ.Distance(result, point).Distance;
        }
        foreach (double center in centers)
            Assert.InRange(Clearance(center), 1.99, 2.001);
        Assert.InRange(Clearance((centers[0] + centers[1]) / 2), 0, .0001);
        double expectedArea = 2*Math.PI*radius*height - centers.Length*Math.PI*holeRadius*holeRadius;
        Assert.InRange(Math.Abs(result.Props().Area - expectedArea), 0, .001);
    }

    [AvaloniaFact(DisplayName = "A solid edge and its extracted curve guide the same solid around a spatial inflection")]
    [Trait("TestId", "REF-H102")]
    public void SolidEdgeAsCurveReference()
    {
        var session = new CadSession();
        var rail = CreateBoxBody(session, Vec3.Zero, 100, 6, 4);
        int index = Enumerable.Range(0, rail.Shape.Count(SubKind.Edge)).First(i => rail.Shape.Sub(SubKind.Edge,i).Props().Size.X > 99);
        var edge = rail.Shape.Sub(SubKind.Edge, index);
        var separate = session.AddObject(edge, "Detached reference", ObjKind.Curve);
        var target = session.AddObject(Occ.Spline([new(0,0,0), new(30,15,5), new(60,-5,10), new(100,20,20)]), "Spatial path", ObjKind.Curve);
        var part = CreateBoxBody(session, edge.Props().Center - new Vec3(10,1,1), 20,2,2);
        Shape Wrap(SelItem reference)
        {
            var cmd = Begin(session, () => new RefoldCommand());
            Pick(cmd, "Original flat pattern", reference);
            Pick(cmd, "Target face (optional)", target);
            Pick(cmd, "Sketches or curves to refold", part);
            SetNumber(cmd, "Curve tolerance", .01);
            var shape = Assert.Single(BuildOutput(cmd).Add).Shape!;
            session.CancelCommand();
            return shape;
        }
        using var control = Wrap(SelItem.Of(separate));
        using var actual = Wrap(new SelItem(rail.Id, SelKind.Edge, index));
        Assert.True(actual.IsValid && actual.HasSolids);
        Assert.InRange(Math.Abs(control.Props().Volume - actual.Props().Volume), 0, .0001);
        Assert.InRange((actual.Props().Center - control.Props().Center).Length, 0, .0001);
    }
}
