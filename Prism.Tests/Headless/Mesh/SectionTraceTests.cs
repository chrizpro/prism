using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <summary>
/// Section Sketch traces and fit quality (MSH-P06): no noise specks, one circle per hole, splines for freeform edges, tangency,
/// and the region border, silhouette and crease sources. The scan is a 70 x 42 x 20 mm plate with a straight bottom and sides,
/// an R10 corner, a freeform top edge and a 12 mm hole, remeshed to 0.6 mm triangles with 0.03 mm noise.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh guide, Section Sketch traces (MSH-P06).</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public sealed class SectionTraceTests(Xunit.Abstractions.ITestOutputHelper log)
{
    /// <summary>Area of the plate's outline with the hole removed (from the CAD part).</summary>
    static double _footprint;

    static readonly Lazy<TriMesh> Scan = new(() =>
    {
        double s = Math.Sin(Math.PI / 4), c = Math.Cos(Math.PI / 4);
        var edges = new List<Shape>
        {
            Occ.Line(new Vec3(0, 0, 0), new Vec3(60, 0, 0)),
            Occ.Arc3(new Vec3(60, 0, 0), new Vec3(60 + 10 * s, 10 - 10 * c, 0), new Vec3(70, 10, 0)),
            Occ.Line(new Vec3(70, 10, 0), new Vec3(70, 30, 0)),
            Occ.Spline([new Vec3(70, 30, 0), new Vec3(55, 42, 0), new Vec3(30, 34, 0), new Vec3(0, 40, 0)]),
            Occ.Line(new Vec3(0, 40, 0), new Vec3(0, 0, 0)),
        };
        using var face = Occ.Profile(edges, faces: true);
        using var plate = Occ.Extrude(face, new Vec3(0, 0, 20));
        using var hole = CylinderAt(new Vec3(30, 18, -5), 6, 30);
        using var part = Occ.Cut(plate, hole);
        _footprint = part.Props().Volume / 20;
        foreach (var e in edges) e.Dispose();
        return MeshGalleryStudies.FakeScan(part, 0.6, 0.03, seed: 11);
    });

    static (CadSession Session, SceneObject Scan, MeshSectionSketchCommand Command) Open()
    {
        var session = new CadSession();
        var scan = AddMesh(session, Scan.Value, "scan");
        var command = Begin(session, () => new MeshSectionSketchCommand());
        Pick(command, "Mesh", scan);
        return (session, scan, command);
    }

    static string Row(Command command, string label) => command.Results.FirstOrDefault(r => r.Label == label)?.Value ?? "";

    SketchModel Sketch(CommandOutput output)
    {
        var m = Assert.Single(output.Add).Sketch!;
        foreach (var e in m.Entities)
        {
            var pts = string.Join(" ", m.EndpointsOf(e).Select(id => $"({m.P(id).X:0.##},{m.P(id).Y:0.##})"));
            log.WriteLine($"{e.Type} {pts}{(e.Type is EntType.Arc or EntType.Circle ? $" r={Radius(m, e):0.##}" : "")}{(e.Type == EntType.Spline ? $" n={e.Pts.Count}" : "")}");
        }
        log.WriteLine(string.Join(", ", m.Constraints.GroupBy(c => c.Type).Select(g => $"{g.Count()} {g.Key}")));
        return m;
    }

    static int Count(SketchModel m, EntType type) => m.Entities.Count(e => e.Type == type && !e.Construction);

    static double Radius(SketchModel m, SkEntity e) => e.Type == EntType.Circle ? e.R : Math.Sqrt(Math.Pow(m.P(e.B).X - m.P(e.A).X, 2) + Math.Pow(m.P(e.B).Y - m.P(e.A).Y, 2));

    /// <summary>Area enclosed by the sketch, as Extrude would see it (outer loop minus the hole).</summary>
    static double ProfileArea(CommandOutput output)
    {
        var added = Assert.Single(output.Add);
        using var face = Occ.Profile(added.Sketch!.ToShapes(added.Plane!.Value), faces: true);
        return face.Props().Area;
    }

    [AvaloniaFact(DisplayName = "MSH-H156 | A mid-height section is three lines, the R10 corner arc, one spline and one circle, and it extrudes")]
    [Trait("TestId", "MSH-H156")]
    public void SectionFitsDesignCurves()
    {
        var (_, _, command) = Open();
        SetNumber(command, "Offset from the plane", 10);
        var output = BuildOutput(command);
        foreach (var r in command.Results) log.WriteLine($"{r.Label}: {r.Value}");
        var m = Sketch(output);
        Assert.Equal(1, Count(m, EntType.Circle));
        Assert.InRange(Radius(m, m.Entities.Single(e => e.Type == EntType.Circle)), 5.9, 6.1);
        var arcs = m.Entities.Where(e => e.Type == EntType.Arc).ToList();
        Assert.Contains(arcs, a => Radius(m, a) is > 9.5 and < 10.5);
        // no small arcs: their centres are the dots that sat next to the outline
        Assert.All(arcs, a => Assert.True(Radius(m, a) > 2, $"arc radius {Radius(m, a):0.##}"));
        Assert.InRange(Count(m, EntType.Spline), 1, 2);
        Assert.InRange(Count(m, EntType.Line), 3, 4);
        Assert.InRange(m.Entities.Count, 6, 9);
        Assert.Contains(m.Constraints, k => k.Type == CType.Tangent);
        AssertClose(_footprint, ProfileArea(output), 0.01 * _footprint, "profile area");
    }

    [AvaloniaFact(DisplayName = "MSH-H157 | A section inside the noise of the bottom face makes no specks and says how to trace that face")]
    [Trait("TestId", "MSH-H157")]
    public void GrazingSectionMakesNoSpecks()
    {
        var (_, _, command) = Open();
        SetNumber(command, "Offset from the plane", 0.01);
        var output = command.TryBuild();
        Assert.Contains("Border of the selected region", Row(command, "Noise") + Row(command, "Nothing here"));
        if (output != null) Assert.True(Sketch(output).Entities.Count <= 12, $"{Sketch(output).Entities.Count} entities");
    }

    [AvaloniaFact(DisplayName = "MSH-H158 | The border of the selected bottom face gives the plate's footprint on the face's own plane")]
    [Trait("TestId", "MSH-H158")]
    public void RegionBorderTracesFootprint()
    {
        var (session, scan, command) = Open();
        var mesh = scan.Tri!;
        var region = session.RegionFor(scan);
        region.Add(Enumerable.Range(0, mesh.TriCount).Where(t => mesh.FaceNormal(t).Z < -0.9 && mesh.P(mesh.T[3 * t]).Z < 1));
        session.RefreshRegion();
        SetChoice(command, "Trace", "Border of the selected region");
        var plane = command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Plane");
        Assert.Equal("Fitted to the selected region", plane.Options[plane.Index]);
        var output = BuildOutput(command);
        foreach (var r in command.Results) log.WriteLine($"{r.Label}: {r.Value}");
        var m = Sketch(output);
        Assert.Equal(1, Count(m, EntType.Circle));
        Assert.True(Math.Abs(Assert.Single(output.Add).Plane!.Value.Origin.Z) < 0.1, "the sketch lies on the bottom face");
        // the region stops where the scan rounds over the edge, so its border sits slightly inside the true outline
        AssertClose(_footprint, ProfileArea(output), 0.03 * _footprint, "footprint area");
    }

    [AvaloniaFact(DisplayName = "MSH-H159 | The silhouette from above is the plate's outline with its hole")]
    [Trait("TestId", "MSH-H159")]
    public void SilhouetteFromAbove()
    {
        var (_, _, command) = Open();
        SetChoice(command, "Trace", "Silhouette");
        var output = BuildOutput(command);
        foreach (var r in command.Results) log.WriteLine($"{r.Label}: {r.Value}");
        var m = Sketch(output);
        Assert.Equal(1, Count(m, EntType.Circle));
        Assert.InRange(m.Entities.Count, 6, 10);
        AssertClose(_footprint, ProfileArea(output), 0.015 * _footprint, "silhouette area");
    }

    [AvaloniaFact(DisplayName = "MSH-H160 | Sharp creases projected onto XY give the outline and the hole, without noise fragments")]
    [Trait("TestId", "MSH-H160")]
    public void CreasesProjectOntoPlane()
    {
        var (_, _, command) = Open();
        SetChoice(command, "Trace", "Sharp creases");
        var output = BuildOutput(command);
        foreach (var r in command.Results) log.WriteLine($"{r.Label}: {r.Value}");
        var m = Sketch(output);
        // top and bottom rims project onto the same outline: two copies of each loop
        Assert.InRange(Count(m, EntType.Circle), 1, 2);
        Assert.True(m.Entities.Count <= 24, $"{m.Entities.Count} entities");
        Assert.DoesNotContain(m.Entities, e => e.Type == EntType.Arc && Radius(m, e) < 2);
    }
}

