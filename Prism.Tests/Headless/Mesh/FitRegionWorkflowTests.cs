using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <summary>
/// Fit Region to a solid (MSH-P07): fit each main face of a scan with "Extend past the region", close the space with Enclosed
/// Body, then fillet with the measured radius. The scan is the Section Sketch test plate (straight sides, an R10 corner, a
/// freeform wall, a Ø12 hole; 0.6 mm triangles, 0.03 mm noise).
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh guide, Fit Region to a solid (MSH-P07).</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public sealed class FitRegionWorkflowTests(Xunit.Abstractions.ITestOutputHelper log)
{
    const string Extend = "Extend past the region (to trim or enclose)";

    static TriMesh Scan()
    {
        var field = typeof(SectionTraceTests).GetField("Scan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return ((Lazy<TriMesh>)field.GetValue(null)!).Value;
    }

    static double Footprint() => (double)typeof(SectionTraceTests).GetField("_footprint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;

    /// <summary>The triangles of one face, picked as a smart click would (exact shapes) or by area (the freeform wall).</summary>
    static IEnumerable<int> Face(TriMesh m, Func<Vec3, Vec3, bool> keep) => Enumerable.Range(0, m.TriCount).Where(t =>
    {
        var (a, b, c) = m.Tri(t);
        return keep((m.P(a) + m.P(b) + m.P(c)) / 3, m.FaceNormal(t));
    });

    SceneObject Fit(CadSession session, SceneObject scan, IEnumerable<int> tris, bool extend = true)
    {
        var region = session.RegionFor(scan);
        region.Clear(); region.Add(tris); session.RefreshRegion();
        var command = Begin(session, () => new MeshFitSurfaceCommand());
        SetToggle(command, Extend, extend);
        SetToggle(command, "Add reference geometry (plane or axis)", false);
        command.Params.OfType<ActionParam>().Single(a => a.Label == "Preview result").Run();
        foreach (var r in command.Results) log.WriteLine($"  {r.Label}: {r.Value}");
        session.CommitCommand();
        return session.Objects.Last(o => o.Kind == ObjKind.Surface);
    }

    [AvaloniaFact(DisplayName = "MSH-H161 | Extended fits of a scan's main faces enclose the part, and a fillet with the measured radius rebuilds it")]
    [Trait("TestId", "MSH-H161")]
    public void ExtendedFitsEncloseThePart()
    {
        var session = new CadSession();
        var scan = AddMesh(session, Scan(), "scan");
        var m = scan.Tri!;
        var surfaces = new List<SceneObject>
        {
            Fit(session, scan, Face(m, (c, n) => n.Z < -0.95 && c.Z < 0.5)),                                    // bottom
            Fit(session, scan, Face(m, (c, n) => n.Z > 0.95 && c.Z > 19.5)),                                     // top
            Fit(session, scan, Face(m, (c, n) => n.X < -0.95 && c.X < 0.5)),                                     // left
            Fit(session, scan, Face(m, (c, n) => n.Y < -0.95 && c.Y < 0.5 && c.X < 59)),                         // front, short of the fillet
            Fit(session, scan, Face(m, (c, n) => n.X > 0.95 && c.X > 69.5 && c.Y is > 11 and < 29)),             // right, between fillet and corner
            Fit(session, scan, Face(m, (c, n) => Math.Abs(new Vec3(c.X - 30, c.Y - 18, 0).Length - 6) < 0.3 && c.Z is > 0.5 and < 19.5)),  // hole
            Fit(session, scan, Face(m, (c, n) => c.Y > 30.5 && c.X is > 0.6 and < 69.4 && c.Z is > 0.6 and < 19.4 && n.Y > 0.2)),           // freeform wall
        };
        Assert.Contains("(extended)", surfaces[^1].Name);
        var enclose = Begin(session, () => new BoundaryFillCommand());
        Pick(enclose, "Surfaces, planes and bodies that enclose the space", surfaces.ToArray());
        var spaces = BuildOutput(enclose);
        var volumes = enclose.Results.Where(r => r.Label.StartsWith("Space")).Select(r => double.Parse(r.Value.Split(' ')[0].Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture)).ToList();
        log.WriteLine("spaces: " + string.Join(", ", volumes));
        // the part with a sharp corner where the R10 fillet goes: footprint × 20 + (1 - π/4) × 10² × 20
        double sharp = Footprint() * 20 + (1 - Math.PI / 4) * 100 * 20;
        int part = volumes.FindIndex(v => Math.Abs(v - sharp) < 0.01 * sharp);
        Assert.True(part >= 0, $"one space is the sharp-cornered part ({sharp:0}); got {string.Join(", ", volumes.Select(v => v.ToString("0")))}");
        SetText(enclose, "Spaces to keep (blank = all)", (part + 1).ToString());
        session.CommitCommand();
        var body = Assert.Single(session.Objects, o => o.Kind == ObjKind.Solid);
        AssertValidSolid(body.Shape);

        int corner = Enumerable.Range(0, body.Shape.Count(SubKind.Edge)).Single(i =>
        {
            var e = Occ.EdgeInfoOf(body.Shape.Sub(SubKind.Edge, i));
            return Math.Abs(e.Start.X - 70) < 0.3 && Math.Abs(e.Start.Y) < 0.3 && Math.Abs(e.End.X - 70) < 0.3 && Math.Abs(e.End.Y) < 0.3;
        });
        var fillet = Begin(session, () => new FilletCommand());
        Pick(fillet, "Edges", new SelItem(body.Id, SelKind.Edge, corner));
        fillet.ConfigureInputs(() => SetNumber(fillet, "Radius", 10));
        session.CommitCommand();
        var rebuilt = Assert.Single(session.Objects, o => o.Kind == ObjKind.Solid);
        AssertValidSolid(rebuilt.Shape);
        AssertClose(Footprint() * 20, rebuilt.Shape.Props().Volume, 0.005 * Footprint() * 20, "rebuilt volume");
    }

    [AvaloniaFact(DisplayName = "MSH-H162 | Without Extend the fitted faces stop at their regions and enclose nothing")]
    [Trait("TestId", "MSH-H162")]
    public void TrimmedFitsLeaveGaps()
    {
        var session = new CadSession();
        var scan = AddMesh(session, Scan(), "scan");
        var m = scan.Tri!;
        var surfaces = new[]
        {
            Fit(session, scan, Face(m, (c, n) => n.Z < -0.95 && c.Z < 0.5), extend: false),
            Fit(session, scan, Face(m, (c, n) => n.Z > 0.95 && c.Z > 19.5), extend: false),
            Fit(session, scan, Face(m, (c, n) => n.X < -0.95 && c.X < 0.5), extend: false),
            Fit(session, scan, Face(m, (c, n) => n.Y < -0.95 && c.Y < 0.5 && c.X < 59), extend: false),
        };
        var enclose = Begin(session, () => new BoundaryFillCommand());
        Pick(enclose, "Surfaces, planes and bodies that enclose the space", surfaces);
        AssertBuildFails(enclose, "Extend past the region");
    }

    [AvaloniaFact(DisplayName = "MSH-H163 | Fit Region caps the points a smooth patch must follow at 150, where the fill still takes seconds")]
    [Trait("TestId", "MSH-H163")]
    public void GuidePointsAreCapped()
    {
        var command = new MeshFitSurfaceCommand();
        var points = command.Params.OfType<IntParam>().Single(p => p.Label == "Points the surface must follow");
        Assert.Equal(150, points.Max);
    }
}
