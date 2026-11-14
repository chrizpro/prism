using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;
using static Prism.HeadlessTests.Mesh.MeshToolTests;

namespace Prism.HeadlessTests.Mesh;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh repair scope.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public class MeshRepairScopeTests
{
    static TriMesh OpenTube()
    {
        var box = BoxMesh(Vec3.Zero, 20, 20, 20);
        return new TriMesh(box.V, box.T[12..]); // Intentional bottom opening and an unwanted top opening.
    }

    static HashSet<int> TopRim(TriMesh mesh)
    {
        var neighbors = mesh.Topology().Neighbors;
        return Enumerable.Range(0, mesh.TriCount).Where(t => Enumerable.Range(0, 3).Any(k =>
            neighbors[3 * t + k] < 0 && mesh.P(mesh.T[3 * t + k]).Z == 20 && mesh.P(mesh.T[3 * t + (k + 1) % 3]).Z == 20)).ToHashSet();
    }

    static Vec3[] Triangle(TriMesh mesh, int t) => Enumerable.Range(0, 3).Select(k => mesh.P(mesh.T[3 * t + k])).ToArray();

    [AvaloniaFact(DisplayName = "MSH-H033 | Selected repair closes only a complete selected rim and previews its boundary")]
    [Trait("TestId", "MSH-H033")]
    public void SelectedRepairProtectsOtherOpeningAndOutsideFaces()
    {
        var session = new CadSession(); var source = OpenTube();
        var obj = AddMesh(session, source);
        var selected = TopRim(source);
        session.RegionFor(obj).Add(selected);
        var command = Begin(session, () => new MeshRepairCommand());
        Assert.Equal("Selected region", command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Repair scope").Selected);
        var repaired = ReplacedMesh(BuildOutput(command), obj);
        Assert.Equal(2, source.Topology().HoleLoops.Count);
        Assert.Single(repaired.Topology().HoleLoops);
        Assert.All(repaired.Topology().HoleLoops.Single(), v => Assert.Equal(0, repaired.P(v).Z));
        foreach (int t in Enumerable.Range(0, source.TriCount).Except(selected))
            Assert.Contains(Enumerable.Range(0, repaired.TriCount), i => Triangle(source, t).SequenceEqual(Triangle(repaired, i)));
        Assert.Contains("1 to fill", Result(command, "Hole preview"));
        Assert.Contains("1 outside", Result(command, "Hole preview"));
        var overlay = new Overlay(); command.BuildOverlay(session, overlay);
        Assert.Equal(4, overlay.Lines.Count);
        Assert.All(overlay.Lines, line => { Assert.Equal(20, line.A.Z); Assert.Equal(20, line.B.Z); });
        session.Apply(BuildOutput(command));
        session.Undo();
        Assert.Equal(source.T, session.Find(obj.Id)!.Tri!.T);
    }

    [AvaloniaFact(DisplayName = "MSH-H034 | Partial rim selection never fills the rest of an existing opening")]
    [Trait("TestId", "MSH-H034")]
    public void PartialRimRemainsOpen()
    {
        var session = new CadSession(); var source = OpenTube(); var obj = AddMesh(session, source);
        session.RegionFor(obj).Add(TopRim(source).Take(3));
        var command = Begin(session, () => new MeshRepairCommand());
        var repaired = ReplacedMesh(BuildOutput(command), obj);
        Assert.Equal(2, repaired.Topology().HoleLoops.Count);
        Assert.Contains("0 to fill", Result(command, "Hole preview"));
        var overlay = new Overlay(); command.BuildOverlay(session, overlay); Assert.Empty(overlay.Lines);
        SetChoice(command, "Repair scope", "Entire mesh");
        Assert.Empty(ReplacedMesh(BuildOutput(command), obj).Topology().HoleLoops);
    }

    [AvaloniaFact(DisplayName = "MSH-H035 | Missing, stale or wrong-mesh regions cannot fall back to global repair")]
    [Trait("TestId", "MSH-H035")]
    public void InvalidRegionIsRejected()
    {
        var session = new CadSession(); var obj = AddMesh(session, OpenTube());
        var command = Begin(session, () => new MeshRepairCommand());
        SetChoice(command, "Repair scope", "Selected region");
        Assert.Contains("Select Region", session.CommandError);
        session.RegionFor(obj).Add(TopRim(obj.Tri!)); command.OnChanged();
        Assert.Null(session.CommandError);
        var other = AddMesh(session, OpenTube(), "Other");
        Pick(command, "Meshes", other); command.OnChanged();
        Assert.Contains("Select Region", session.CommandError);
        Pick(command, "Meshes", obj);
        var replacement = new CommandOutput(); replacement.ReplaceMeshes.Add((obj, OpenTube())); session.Apply(replacement);
        command.OnChanged();
        Assert.Contains("Select Region", session.CommandError);
    }

    [AvaloniaFact(DisplayName = "MSH-H036 | Mesh ribbon exposes modification tools in workflow order")]
    [Trait("TestId", "MSH-H036")]
    public void RibbonExposesModifications()
    {
        var vm = new MainViewModel(); var tab = vm.Tabs.Single(t => t.Title == "Mesh");
        Assert.Equal(new[] { "Select", "Datum", "Import & Convert", "Check", "Repair", "Reshape", "Modify", "Print analysis", "Transform", "Inspect", "Appearance" }, tab.Groups.Select(g => g.Title));
        Assert.Equal(new[] { "Split", "Offset", "Mesh\nBoolean" }, tab.Groups.Single(g => g.Title == "Modify").Items.OfType<RibbonButton>().Select(b => b.Label));
        Assert.All(tab.Groups.Single(g => g.Title == "Modify").Items.OfType<RibbonButton>(), b => Assert.False(b.HasMenu));
    }
}
