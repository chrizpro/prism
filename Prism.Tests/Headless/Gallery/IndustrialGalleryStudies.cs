using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Prism.Rendering;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Gallery;

/// <summary>Industrial gallery fixtures. Set PRISM_INDUSTRIAL_OUT to save, reopen and render each finished document.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/gallery/README.md">Gallery guide.</seealso>
[Trait("Feature", "Gallery")]
[Trait("Layer", "Headless")]
public class IndustrialGalleryStudies
{
    const uint Casting = 0xFF788089, Machined = 0xFFB9BEC3;
    static string? Folder => Environment.GetEnvironmentVariable("PRISM_INDUSTRIAL_OUT");

    static void Publish(CadSession session, string id, double yaw, double pitch)
    {
        if (Folder is not { } folder) return;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, id + ".prism");
        session.Save(path);
        var reopened = new CadSession();
        reopened.Open(path);
        Assert.Equal(session.Objects.Count, reopened.Objects.Count);
        var meshes = reopened.Objects.Where(x => x.Visible).Select(x => x.Mesh).ToList();
        var lo = new Vec3(meshes.Min(m => m.Min.X), meshes.Min(m => m.Min.Y), meshes.Min(m => m.Min.Z));
        var hi = new Vec3(meshes.Max(m => m.Max.X), meshes.Max(m => m.Max.Y), meshes.Max(m => m.Max.Z));
        var camera = reopened.Camera;
        camera.Width = 810; camera.Height = 504;
        camera.SetView(yaw, pitch);
        camera.Fit(lo, hi);
        camera.Orthographic = true;
        camera.Distance *= 0.70;
        var tracer = new PathTracer(new RenderSettings
        {
            Width = 810,
            Height = 504,
            Samples = 36,
            Bounces = 4,
            Background = RenderBg.White,
            Denoise = true
        });
        Assert.True(tracer.LoadScene(reopened));
        tracer.Render(camera, (pixels, w, h, _, done) =>
        {
            if (done) PngWriter.Save(Path.Combine(folder, id + ".png"), pixels, w, h);
        }, CancellationToken.None);
    }

    static Shape Profile(Vec3[] points)
    {
        using var loop = Occ.Polyline(points, true);
        return Occ.Profile([loop]);
    }

    [AvaloniaFact(DisplayName = "GAL-H210 | A STEP cold plate grows from one pilot boss into a 24-pin heat sink")]
    [Trait("TestId", "GAL-H210")]
    public void PinFinColdPlate()
    {
        using var slab = BoxAt(new(-34, -24, 0), 68, 48, 6);
        var blank = Occ.Fillet(slab, Enumerable.Range(0, slab.Count(SubKind.Edge)).ToArray(), 1.5);
        foreach (int x in new[] { -29, 29 })
            foreach (int y in new[] { -19, 19 })
                blank = Occ.Cut(blank, CylinderAt(new(x, y, -1), 2, 8));
        blank = Occ.Fuse(blank, CylinderAt(new(-20, -12, 6), 3, 4));
        string step = Path.Combine(Folder ?? Path.GetTempPath(), Folder == null ? $"{Guid.NewGuid()}.step" : "pin-fin-source.step");
        Directory.CreateDirectory(Path.GetDirectoryName(step)!);
        Shape imported;
        try { Occ.ExportStep([blank], step); imported = Occ.ImportStep(step); }
        finally { if (Folder == null) File.Delete(step); }
        var session = new CadSession();
        var body = session.AddObject(imported, "After - 24 cooling pins", ObjKind.Solid, Machined);
        SelItem[] Pins() => FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Boss && f.Round != null)
            .Select(f => new SelItem(body.Id, SelKind.Face, f.Face)).ToArray();
        var grow = Begin(session, () => new DirectEditCommand());
        Pick(grow, grow.Slots[0].Label, Assert.Single(Pins()));
        SetNumber(grow, "Move Z", 20);
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        body = session.Find(body.Id)!;
        void CopyPins(int n, int dx, int dy)
        {
            var copy = Begin(session, () => new FeatureEditCommand());
            copy.Preset(Pins(), 2);
            copy.ConfigureInputs(() =>
            {
                SetNumber(copy, "Number of copies", n);
                SetNumber(copy, "Move X", dx); SetNumber(copy, "Move Y", dy);
            });
            session.CommitCommand();
            Assert.Null(session.ActiveCommand);
            body = session.Find(body.Id)!;
        }
        CopyPins(5, 8, 0);
        CopyPins(3, 0, 8);
        Assert.Equal(24, Pins().Length);
        AssertValidSolid(body.Shape);
        AssertClose(imported.Props().Volume + Math.PI * 9 * (24 * 24 - 4), body.Shape.Props().Volume, 0.05, "pin field volume");
        session.AddObject(Occ.Translate(imported, new(-79, 0, 0)), "Before - STEP blank", ObjKind.Solid, Machined);
        Publish(session, "pin-fin-cold-plate", -18, 32);
    }

    [AvaloniaFact(DisplayName = "GAL-H211 | Inline-four block has four open liners, a relieved crankcase and drilled deck")]
    [Trait("TestId", "GAL-H211")]
    public void FourCylinderBlock()
    {
        var session = new CadSession();
        // Side walls flare into the sump rail. Each web has a clearance arch for the crankshaft.
        using var end = Profile([new(-90,-32,0),new(-90,32,0),new(-90,32,12),new(-90,25,24),
            new(-90,25,58),new(-90,-25,58),new(-90,-25,24),new(-90,-32,12)]);
        Shape block = Occ.Extrude(end, new(180, 0, 0));
        block = Occ.Fuse(block, BoxAt(new(-92, -34, 0), 184, 68, 5));
        block = Occ.Fuse(block, BoxAt(new(-91, -28, 56), 182, 56, 5));
        foreach (int x in new[] { -63, -21, 21, 63 })
        {
            block = Occ.Cut(block, BoxAt(new(x - 17, -22, -1), 34, 44, 27));
            block = Occ.Cut(block, CylinderAt(new(x, 0, -1), 17, 64));
            block = Occ.Cut(block, CylinderAt(new(x, 0, 58), 18.8, 5));
            using var liner = Occ.Cut(CylinderAt(new(x, 0, 58), 18.7, 3), CylinderAt(new(x, 0, 57), 17, 5));
            session.AddObject(Occ.Translate(liner, Vec3.Zero), $"Cylinder liner {(x + 63) / 42 + 1}", ObjKind.Solid, Machined);
            // Side core plugs sit in recessed seats in the casting.
            Shape SidePlug(double r, double h, double y) => Occ.Translate(Occ.Rotate(Occ.Cylinder(r, h), Vec3.Zero, Vec3.UnitX, 90), new(x, y, 38));
            block = Occ.Cut(block, SidePlug(9, 8, -22));
            session.AddObject(Occ.Cut(SidePlug(8.7, 2, -22), SidePlug(7.5, 2, -23)), $"Core plug {(x + 63) / 42 + 1}", ObjKind.Solid, 0xFF93999E);
        }
        foreach (int x in new[] { -84, -42, 0, 42, 84 })
            foreach (int side in new[] { -1, 1 })
            {
                block = Occ.Cut(block, CylinderAt(new(x, side * 21, 42), 2.6, 21));
                using var rib = Profile([new(x - 2, side * 24, 52), new(x - 2, side * 31, 11), new(x - 2, side * 24, 11)]);
                block = Occ.Fuse(block, Occ.Extrude(rib, new(4, 0, 0)));
            }
        using var tunnel = Occ.Translate(Occ.Rotate(Occ.Cylinder(12, 190), Vec3.Zero, Vec3.UnitY, 90), new(-95, 0, 0));
        block = Occ.Cut(block, tunnel);
        AssertValidSolid(block);
        session.AddObject(block, "Inline-four crankcase", ObjKind.Solid, Casting);
        Assert.Equal(9, session.Objects.Count);
        Publish(session, "inline-four-crankcase", 28, 32);
    }

    static Shape Pulley()
    {
        var section = new List<Vec3> { new(7, 0, 0), new(14, 0, 0), new(14, 0, 3), new(32, 0, 3) };
        for (int z = 3; z < 27; z += 6)
            section.AddRange([new(32, 0, z + 1), new(27, 0, z + 3), new(27, 0, z + 4), new(32, 0, z + 6)]);
        section.AddRange([new(32, 0, 28), new(14, 0, 28), new(14, 0, 32), new(7, 0, 32)]);
        using var profile = Profile(section.ToArray());
        var pulley = Occ.Revolve(profile, Vec3.Zero, Vec3.UnitZ, 360);
        for (int i = 0; i < 6; i++)
        {
            double a = i * Math.PI / 3;
            pulley = Occ.Cut(pulley, CylinderAt(new(20 * Math.Cos(a), 20 * Math.Sin(a), 0), 4.5, 33));
        }
        return pulley;
    }

    static Shape Impeller()
    {
        var rotor = Occ.Fuse(CylinderAt(Vec3.Zero, 39, 3), Occ.Cone(12, 8, 17));
        using var vane = Profile([new(9,-3,2),new(20,-1,2),new(30,5,2),new(37,13,2),
            new(35.5,14,2),new(29,7,2),new(19,1,2),new(9,-1,2)]);
        using var blade = Occ.Extrude(vane, new(0, 0, 12));
        for (int i = 0; i < 10; i++) rotor = Occ.Fuse(rotor, Occ.Rotate(blade, Vec3.Zero, Vec3.UnitZ, 36 * i));
        return Occ.Cut(rotor, CylinderAt(new(0, 0, -1), 4, 20));
    }

    [AvaloniaTheory(DisplayName = "GAL-H212 | Imported pulley and impeller meshes survive reduction or hole repair as closed parts")]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("TestId", "GAL-H212")]
    public void IndustrialMeshes(bool pulley)
    {
        using var solid = pulley ? Pulley() : Impeller();
        var source = MeshOps.Weld(MeshAnalysis.FromMeshData(solid.Tessellate(0.02)), 1e-6);
        Assert.True(MeshAnalysis.Report(source).Closed);
        // Simulate one missing scan facet on the backing disk.
        if (!pulley) source = new TriMesh(source.V, source.T[3..]);
        var name = pulley ? "multi-groove-sheave" : "centrifugal-impeller";
        var session = new CadSession();
        string path = Mesh.MeshGalleryStudies.WriteObj(source, name);
        try
        {
            if (Folder is { } folder)
            {
                Directory.CreateDirectory(folder);
                File.Copy(path, Path.Combine(folder, name + "-source.obj"), true);
            }
            var scan = Mesh.MeshGalleryStudies.Import(session, path, 100);
            int original = scan.Tri!.TriCount;
            if (pulley)
            {
                var reduce = Begin(session, () => new MeshRemeshCommand());
                SetChoice(reduce, "Mode", "Reduce Faces");
                SetNumber(reduce, "Keep", 55);
            }
            else
            {
                Assert.False(MeshAnalysis.Report(scan.Tri).Closed);
                Begin(session, () => new MeshRepairCommand(holesOnly: true));
            }
            session.CommitCommand();
            Assert.Null(session.ActiveCommand);
            var result = session.Find(scan.Id)!.Tri!;
            Assert.True(MeshAnalysis.Report(result).Closed);
            if (pulley) Assert.InRange(result.TriCount, original / 2, original * 6 / 10);
            else Assert.Equal(original + 1, result.TriCount);
            AssertClose(solid.Props().Volume, MeshAnalysis.Report(result).Volume, solid.Props().Volume * 0.015, "mesh volume");
            session.Update(scan.Id, o => o.WithName(pulley ? "Four-groove drive sheave" : "Ten-vane centrifugal impeller").WithColor(Machined));
            Publish(session, name, 25, pulley ? 25 : 43);
        }
        finally { File.Delete(path); }
    }
}
