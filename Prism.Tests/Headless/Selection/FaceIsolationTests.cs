using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Selection;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/selection/README.md#isolate-faces">Isolate faces.</seealso>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public class FaceIsolationTests
{
    /// <summary>The face of a box whose triangles all lie at the given height.</summary>
    static int FaceAtZ(SceneObject o, double z)
    {
        var m = o.Mesh;
        for (int f = 0; f < m.FaceCount; f++)
        {
            bool flat = true;
            for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1] && flat; t++)
                for (int k = 0; k < 3; k++) flat &= Math.Abs(m.Positions[m.Indices[3 * t + k] * 3 + 2] - z) < 1e-6;
            if (flat) return f;
        }
        throw new InvalidOperationException($"no face at z = {z}");
    }

    [AvaloniaFact(DisplayName = "SEL-H013 | Isolate with faces selected shows only those faces and their edges; Show All restores")]
    [Trait("TestId", "SEL-H013")]
    public void IsolateShowsOnlySelectedFaces()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        var other = Support.ModelingScenario.CreateBoxBody(s, new Vec3(50, 0, 0), 10, 10, 10);
        var curve = Support.ModelingScenario.CreateCurve(s, [new Vec3(0, 40, 0), new Vec3(10, 40, 0), new Vec3(20, 45, 0)]);
        int top = FaceAtZ(box, box.Mesh.Max.Z), bottom = FaceAtZ(box, box.Mesh.Min.Z);

        s.SetSelection(new SelItem(box.Id, SelKind.Face, top), false);
        vm.IsolateSelected();

        Assert.True(s.FacesIsolated);
        Assert.Equal([top], s.ShownFaces(box)!);
        Assert.Equal(4, s.ShownEdges(box)!.Count);                 // the four edges around the top face, not all twelve
        Assert.True(s.ShownByIsolation(new SelItem(box.Id, SelKind.Face, top)));
        Assert.False(s.ShownByIsolation(new SelItem(box.Id, SelKind.Face, bottom)));
        Assert.False(s.ShownByIsolation(other));                   // other bodies are hidden
        Assert.True(s.ShownByIsolation(curve));                    // curves and sketches stay as references
        Assert.True(box.Visible && other.Visible);                 // nothing in the document changed
        Assert.Equal(6, s.Find(box.Id)!.Shape.Count(SubKind.Face));

        vm.ShowAll();
        Assert.False(s.FacesIsolated);
        Assert.Null(s.ShownFaces(box));
        Assert.True(s.ShownByIsolation(other));
    }

    [AvaloniaFact(DisplayName = "SEL-H014 | A click passes through faces hidden by Isolate to the face behind")]
    [Trait("TestId", "SEL-H014")]
    public void ClickPassesThroughHiddenFaces()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        int bottom = FaceAtZ(box, box.Mesh.Min.Z);
        var c = (box.Mesh.Min + box.Mesh.Max) * 0.5;
        var px = s.Camera.Project(new Vec3(c.X, c.Y, box.Mesh.Min.Z))!.Value;       // the middle of the bottom, seen through the box

        var before = s.Pick(px.x, px.y, SelFilter.Face);
        Assert.NotEqual(new SelItem(box.Id, SelKind.Face, bottom), before!.Value.Item);   // a nearer face is in the way

        s.IsolateFaces([new SelItem(box.Id, SelKind.Face, bottom)]);
        var after = s.Pick(px.x, px.y, SelFilter.Face);
        Assert.Equal(new SelItem(box.Id, SelKind.Face, bottom), after!.Value.Item);
    }

    [AvaloniaFact(DisplayName = "SEL-H015 | A body that changes shape while isolated is shown whole again")]
    [Trait("TestId", "SEL-H015")]
    public void EditedBodyIsShownWhole()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        s.IsolateFaces([new SelItem(box.Id, SelKind.Face, 0)]);
        Assert.NotNull(s.ShownFaces(s.Find(box.Id)!));

        s.Update(box.Id, o => o.WithShape(Occ.Box(30, 20, 20)), checkpoint: false);

        Assert.Null(s.ShownFaces(s.Find(box.Id)!));                // face numbers no longer mean the same faces
    }
}
