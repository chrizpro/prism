using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <summary>
/// Owner report 2026-11-16: an open tube patched with Boundary Surface, knitted and shelled did not shell. The default edge continuity (tangent)
/// ballooned the patches over a flat opening, and Shell then failed on them ("Weights values too small").
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/boundary-surface/README.md">Boundary Surface, Patch and Ruled Surface.</seealso>
[Trait("Feature", "BoundarySurface")]
[Trait("Layer", "Headless")]
public class BoundaryFlatOpeningTests
{
    static SceneObject OpenTube(CadSession session)
    {
        using var box = Occ.Box(60, 40, 20);
        var faces = new List<Shape>();
        for (int index = 0; index < box.Count(SubKind.Face); index++)
        {
            var face = box.Sub(SubKind.Face, index);
            if (Math.Abs(Occ.FaceInfoOf(face).Normal.Z) > 0.99) face.Dispose(); else faces.Add(face);
        }
        return session.AddObject(Occ.Sew(faces, 0.01, false), "Open tube", ObjKind.Surface);
    }

    [AvaloniaFact(DisplayName = "BND-H020 | Both openings of a flat-ended tube are capped flat with the default settings")]
    [Trait("TestId", "BND-H020")]
    public void FlatOpeningsAreCappedFlat()
    {
        var vm = new MainViewModel();
        var tube = OpenTube(vm.Session);
        var button = vm.Tabs.Single(t => t.Title == "Surface").Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct()
            .Single(b => b.Label.Replace('\n', ' ') == "Boundary Surface");
        button.Invoke();
        var command = Assert.IsType<BoundarySurfaceCommand>(vm.Session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Boundary", tube);
            SetToggle(command, "Join the patch to the boundary surfaces (closes into a solid when it can)", false);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        Assert.Equal(2, output.Add.Count);
        foreach (var patch in output.Add)
        {
            var props = patch.Shape!.Props();
            AssertNear(2400, props.Area, "Flat cap area (mm²)");
            AssertNear(0, props.Size.Z, "Cap thickness (mm)");
        }
    }

    [AvaloniaFact(DisplayName = "BND-H021 | A tube capped by Boundary Surface and knitted is a solid that Shell can hollow")]
    [Trait("TestId", "BND-H021")]
    public void CappedKnittedTubeShells()
    {
        var vm = new MainViewModel();
        var tube = OpenTube(vm.Session);
        var button = vm.Tabs.Single(t => t.Title == "Surface").Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct()
            .Single(b => b.Label.Replace('\n', ' ') == "Boundary Surface");
        button.Invoke();
        var fill = Assert.IsType<BoundarySurfaceCommand>(vm.Session.ActiveCommand);
        fill.ConfigureInputs(() =>
        {
            SetSelection(fill, "Boundary", tube);
            SetToggle(fill, "Join the patch to the boundary surfaces (closes into a solid when it can)", false);
        });
        var patches = fill.TryBuild();
        Assert.NotNull(patches);
        var added = patches.Add.Select(p => vm.Session.AddObject(p.Shape!, "Patch", ObjKind.Surface)).ToList();
        vm.Session.CancelCommand();

        var knit = Begin(vm.Session, () => new SewCommand());
        Pick(knit, "Surfaces", [tube, .. added]);
        var knitted = knit.TryBuild();
        Assert.True(knitted != null, knit.Error);
        var body = vm.Session.AddObject(Assert.Single(knitted.Add).Shape!, "Body", ObjKind.Solid);
        Assert.True(body.Shape.HasSolids);
        AssertNear(48000, body.Shape.Props().Volume, "Knitted volume (mm³)");
        vm.Session.CancelCommand();

        var shell = Begin(vm.Session, () => new ShellCommand());
        shell.ConfigureInputs(() => Assert.True(shell.Slots[0].TryAdd(SelItem.Of(body))));
        var hollow = shell.TryBuild();
        Assert.True(hollow != null, shell.Error);
        using var result = Assert.Single(hollow.Replace).New;
        AssertNear(48000 - 56 * 36 * 16, result.Props().Volume, "Hollow volume (mm³)");
    }
}
