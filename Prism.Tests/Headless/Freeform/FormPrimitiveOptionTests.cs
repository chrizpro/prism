using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Freeform;

/// <summary>Freeform primitives offer the smoothing level and sharp edges (owner request 2026-11-16), and Fill holes works at the selection (owner report 2026-11-16).</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/freeform/README.md">Freeform guide.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Headless")]
public class FormPrimitiveOptionTests
{
    const string SmoothnessLabel = "Smoothness (0 shows the plain cage)", SharpLabel = "Sharp edges (keep corners and rims crisp when smoothed)";

    static SubdMesh Make(FormCreateCommand command)
    {
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        return Assert.Single(output.Add).Form!;
    }

    [AvaloniaFact(DisplayName = "FRM-H130 | Every Freeform primitive starts as before (3 smoothing levels, no sharp edges) and offers Smoothness and Sharp edges")]
    [Trait("TestId", "FRM-H130")]
    public void DefaultsAreUnchanged()
    {
        foreach (Func<FormCreateCommand> make in new Func<FormCreateCommand>[] { () => new FormPlaneCommand(), () => new FormBoxCommand(), () => new FormSphereCommand(), () => new FormCylinderCommand(), () => new FormTorusCommand() })
        {
            var session = new CadSession();
            session.SelectItems([]);
            session.StartCommand(() => make());
            var command = Assert.IsAssignableFrom<FormCreateCommand>(session.ActiveCommand);
            Assert.Contains(command.Params, p => p.Label == SmoothnessLabel);
            Assert.Contains(command.Params, p => p.Label == SharpLabel);
            var mesh = Make(command);
            Assert.Equal(3, mesh.Level);
            Assert.Empty(mesh.Crease);
        }
    }

    [AvaloniaFact(DisplayName = "FRM-H131 | Smoothness sets the display level of the new form, from 0 (the plain cage) to 4")]
    [Trait("TestId", "FRM-H131")]
    public void SmoothnessSetsTheLevel()
    {
        foreach (int level in new[] { 0, 1, 4 })
        {
            var session = new CadSession();
            var box = Begin(session, () => new FormBoxCommand());
            SetInteger(box, SmoothnessLabel, level);
            Assert.Equal(level, Make(box).Level);
        }
    }

    [AvaloniaFact(DisplayName = "FRM-H132 | Sharp edges creases the 12 box edges (24 pieces with 2 faces per side) and the 16 cylinder rim edges, and nothing on a plane or sphere")]
    [Trait("TestId", "FRM-H132")]
    public void SharpEdges()
    {
        var session = new CadSession();
        var box = Begin(session, () => new FormBoxCommand());
        SetToggle(box, SharpLabel, true);
        Assert.Equal(24, Make(box).Crease.Count);                                  // the default 2 x 2 x 2 faces: each of the 12 edges is two edges long

        var oneEach = Begin(session, () => new FormBoxCommand());
        SetInteger(oneEach, "Faces X", 1); SetInteger(oneEach, "Faces Y", 1); SetInteger(oneEach, "Faces Z", 1);
        SetToggle(oneEach, SharpLabel, true);
        Assert.Equal(12, Make(oneEach).Crease.Count);

        var cylinder = Begin(session, () => new FormCylinderCommand());
        SetToggle(cylinder, SharpLabel, true);
        Assert.Equal(16, Make(cylinder).Crease.Count);                             // 8 sides: both rims, not the gently bending sides

        var plane = Begin(session, () => new FormPlaneCommand());
        SetToggle(plane, SharpLabel, true);
        Assert.Empty(Make(plane).Crease);
    }

    [AvaloniaFact(DisplayName = "FRM-H133 | The segment counts still set the number of faces: 1 and 3 faces per side of a box make 6 and 54 faces")]
    [Trait("TestId", "FRM-H133")]
    public void SegmentCountsStillWork()
    {
        var session = new CadSession();
        foreach (var (n, faces) in new[] { (1, 6), (3, 54) })
        {
            var box = Begin(session, () => new FormBoxCommand());
            SetInteger(box, "Faces X", n); SetInteger(box, "Faces Y", n); SetInteger(box, "Faces Z", n);
            Assert.Equal(faces, Make(box).F.Count);
        }
    }

    [AvaloniaFact(DisplayName = "FRM-H137 | Cylinder cap rings and a sphere's own counts per direction work from the panel, and the old limits are raised")]
    [Trait("TestId", "FRM-H137")]
    public void MoreSegments()
    {
        var session = new CadSession();
        var cylinder = Begin(session, () => new FormCylinderCommand());
        Assert.Equal(8 * 3 + 2, Make(cylinder).F.Count);                              // the defaults give what it always did
        SetInteger(cylinder, "Cap rings (0 = one face per cap)", 2);
        Assert.Equal(8 * 3 + 2 * (2 * 8 + 1), Make(cylinder).F.Count);
        SetToggle(cylinder, "Caps", false);
        Assert.False(cylinder.Params.Single(p => p.Label == "Cap rings (0 = one face per cap)").IsVisible);        // nothing to divide without caps
        Assert.Equal(8 * 3, Make(cylinder).F.Count);

        var sphere = Begin(session, () => new FormSphereCommand());
        Assert.Equal(6 * 3 * 3, Make(sphere).F.Count);
        SetInteger(sphere, "Faces X (0 = same as per side)", 6); SetInteger(sphere, "Faces Y (0 = same as per side)", 4);
        Assert.Equal(2 * (6 * 4 + 4 * 3 + 3 * 6), Make(sphere).F.Count);              // X = 6, Y = 4, Z falls back to the 3 per side

        var box = Begin(session, () => new FormBoxCommand());
        SetInteger(box, "Faces X", 40);                                                // above the old limit of 20
        Assert.Equal(40, box.Params.OfType<IntParam>().Single(p => p.Label == "Faces X").Value);
    }

    // ------------------------------------------------------------------ Fill holes at the selection

    static (CadSession session, EditFormCommand edit, SubdMesh mesh, Vec3 holeA, Vec3 holeB) TwoHoles()
    {
        var mesh = FormOps.Plane(60, 60, 6, 6);
        int Nearest(Vec3 p) => Enumerable.Range(0, mesh.F.Count).MinBy(i => mesh.FaceCenter(i).DistanceTo(new Vec3(p.X, p.Y, mesh.FaceCenter(i).Z)));
        int a = Nearest(new Vec3(-15, -15, 0)), b = Nearest(new Vec3(15, 15, 0));
        Vec3 holeA = mesh.FaceCenter(a), holeB = mesh.FaceCenter(b);
        FormOps.DeleteFaces(mesh, new HashSet<int> { a, b });
        var session = new CadSession();
        var form = session.AddObject(null, "Plate", ObjKind.Form, form: mesh);
        session.SelectItems([SelItem.Of(form)]);
        session.StartCommand(() => new EditFormCommand());
        return (session, (EditFormCommand)session.ActiveCommand!, mesh, holeA, holeB);
    }

    static void SelectPointsNear(EditFormCommand edit, Vec3 centre)
    {
        edit.SetSelectionMode(FormMode.Vertex);
        edit.ClearSelection();
        for (int i = 0; i < edit.Work!.V.Count; i++) if (edit.Work.V[i].DistanceTo(centre) < 10) edit.Verts.Add(i);
        Assert.NotEmpty(edit.Verts);
    }

    static void FillHoles(EditFormCommand edit) => edit.Params.OfType<ActionParam>().Single(a => a.Label == "Fill holes (cap open borders)").Run();

    [AvaloniaFact(DisplayName = "FRM-H134 | Fill holes with points selected at one hole fills that hole only, not the one far away")]
    [Trait("TestId", "FRM-H134")]
    public void FillsTheSelectedHoleOnly()
    {
        var (_, edit, mesh, holeA, holeB) = TwoHoles();
        int before = edit.Work!.F.Count;
        SelectPointsNear(edit, holeA);
        FillHoles(edit);
        Assert.Equal(before + 1, edit.Work.F.Count);                                                                // one cap, not two (and not the sheet's outer edge)
        Assert.Contains(edit.Work.F, f => f.All(v => edit.Work.V[v].DistanceTo(holeA) < 10));                      // a new face sits where hole A was
        Assert.DoesNotContain(edit.Work.F, f => f.Length == 4 && f.All(v => edit.Work.V[v].DistanceTo(holeB) < 10));    // hole B is still open
    }

    [AvaloniaFact(DisplayName = "FRM-H135 | Fill holes with a selection that has no hole at it changes nothing")]
    [Trait("TestId", "FRM-H135")]
    public void SelectionWithoutAHoleFillsNothing()
    {
        var (_, edit, _, _, _) = TwoHoles();
        int before = edit.Work!.F.Count;
        SelectPointsNear(edit, new Vec3(0, 0, 0));
        FillHoles(edit);
        Assert.Equal(before, edit.Work.F.Count);                                                                    // before the fix this capped every hole in the form
    }

    [AvaloniaFact(DisplayName = "FRM-H136 | Fill holes with nothing selected still fills every open border, as before")]
    [Trait("TestId", "FRM-H136")]
    public void NothingSelectedFillsAll()
    {
        var (_, edit, _, _, _) = TwoHoles();
        int before = edit.Work!.F.Count;
        edit.ClearSelection();
        FillHoles(edit);
        Assert.Equal(before + 3, edit.Work.F.Count);                                                                // the two holes and the sheet's outer edge
    }
}
