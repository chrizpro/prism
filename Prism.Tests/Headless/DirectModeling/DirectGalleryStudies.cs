using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>
/// Direct Modeling gallery studies: a body with no feature history (as it arrives from STEP) revised with face edits only.
/// Each step asserts its effect. Set PRISM_GALLERY_OUT to save the finished documents (gallery/README.md).
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct Modeling guide.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public sealed class DirectGalleryStudies
{
    static SelItem[] Faces(SceneObject body, Func<FaceInfo, ShapeProps, bool> pick) =>
        Enumerable.Range(0, body.Shape!.Count(SubKind.Face)).Where(i =>
        {
            using var face = body.Shape.Sub(SubKind.Face, i);
            return pick(Occ.FaceInfoOf(face), face.Props());
        }).Select(i => new SelItem(body.Id, SelKind.Face, i)).ToArray();

    static SceneObject Apply(CadSession session, SceneObject body, Command command)
    {
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        var edited = session.Find(body.Id)!;
        AssertValidSolid(edited.Shape!);
        return edited;
    }

    static void SaveForGallery(CadSession session, string id)
    {
        if (Environment.GetEnvironmentVariable("PRISM_GALLERY_OUT") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        session.Save(Path.Combine(folder, id + ".prism"));
    }

    [AvaloniaFact(DisplayName = "GAL-H203 | Motor mount revision: Move, Offset, Delete and Draft Face change an imported plate without history")]
    [Trait("TestId", "GAL-H203")]
    public void MotorMountRevision()
    {
        var session = new CadSession();
        // as imported: a 90 x 60 x 12 mm plate, a Ø30 bore, four Ø6 corner holes and a 40 x 40 x 8 mm pad on top
        using var plate = BoxAt(new Vec3(-45, -30, 0), 90, 60, 12);
        using var pad = BoxAt(new Vec3(-20, -20, 12), 40, 40, 8);
        using var body0 = Occ.Fuse(plate, pad);
        Shape shape = Occ.Cut(body0, CylinderAt(new Vec3(0, 0, -5), 15, 40));
        foreach (var (x, y) in new[] { (-37.0, -22.0), (37.0, -22.0), (-37.0, 22.0), (37.0, 22.0) })
            shape = Occ.Cut(shape, CylinderAt(new Vec3(x, y, -5), 3, 30));
        var original = session.AddObject(shape, "As imported", ObjKind.Solid, 0xFF9AA5B4);
        var body = session.AddObject(shape, "Revised motor mount", ObjKind.Solid, 0xFF2F8F9D);
        double v0 = shape.Props().Volume;

        // 1. lengthen the plate 15 mm toward +X: the corner holes on that end stay where they are
        var move = Begin(session, () => new MoveFaceCommand());
        Pick(move, "Flat faces to move", Faces(body, (f, p) => f.Type == "Plane" && f.Normal.X > 0.99 && p.Max.X > 44));
        SetNumber(move, "Move X", 15);
        body = Apply(session, body, move);
        AssertClose(105, body.Shape!.Props().Max.X - body.Shape.Props().Min.X, 1e-3, "plate length");

        // 2. open the bore from Ø30 to Ø32: offset its wall 1 mm into the material
        var offset = Begin(session, () => new OffsetFaceCommand());
        Pick(offset, "Faces to offset", Faces(body, (f, p) => f.Type == "Cylinder" && Math.Abs(f.R1 - 15) < 1e-3));
        SetNumber(offset, "Offset distance (positive moves faces out)", -1);
        double before = body.Shape!.Props().Volume;
        body = Apply(session, body, offset);
        double bore = Faces(body, (f, p) => f.Type == "Cylinder" && f.R1 > 14).Select(i => { using var face = body.Shape!.Sub(SubKind.Face, i.Index); return Occ.FaceInfoOf(face).R1; }).Single();
        if (Math.Abs(bore - 16) > 1e-3)
        {
            // the sign convention points the other way on a hole: undo and offset the other way
            session.Undo();
            body = session.Find(body.Id)!;
            offset = Begin(session, () => new OffsetFaceCommand());
            Pick(offset, "Faces to offset", Faces(body, (f, p) => f.Type == "Cylinder" && Math.Abs(f.R1 - 15) < 1e-3));
            SetNumber(offset, "Offset distance (positive moves faces out)", 1);
            body = Apply(session, body, offset);
        }
        AssertClose(before - Math.PI * (16 * 16 - 15 * 15) * 20, body.Shape!.Props().Volume, 1, "bore opened to Ø32");

        // 3. one corner hole is no longer needed: delete its wall and the plate closes over it
        var delete = Begin(session, () => new DeleteFaceCommand());
        Pick(delete, "Faces to remove (a hole, fillet, pocket...)", Faces(body, (f, p) => f.Type == "Cylinder" && Math.Abs(f.R1 - 3) < 1e-3 && p.Center.X < -30 && p.Center.Y > 15));
        before = body.Shape!.Props().Volume;
        body = Apply(session, body, delete);
        AssertClose(before + Math.PI * 9 * 12, body.Shape!.Props().Volume, 0.5, "hole filled");

        // 4. draft the pad's four walls 6° from the plate's top so it releases from a mould
        var draft = Begin(session, () => new DraftFaceCommand());
        var walls = Faces(body, (f, p) => f.Type == "Plane" && Math.Abs(f.Normal.Z) < 0.01 && p.Min.Z > 11.9);
        Assert.Equal(4, walls.Length);
        Pick(draft, "Faces to draft", walls);
        Pick(draft, "Neutral plane (a plane or flat face the walls hinge on)", Faces(body, (f, p) => f.Type == "Plane" && f.Normal.Z > 0.99 && Math.Abs(p.Max.Z - 12) < 1e-3));
        SetNumber(draft, "Draft angle", 6);
        before = body.Shape!.Props().Volume;
        body = Apply(session, body, draft);
        Assert.True(Math.Abs(body.Shape!.Props().Volume - before) > 50, "the draft changes the pad");
        Assert.True(Math.Abs(body.Shape.Props().Volume - v0) > 100);

        session.Update(original.Id, x => x.WithShape(Occ.Translate(x.Shape!, new Vec3(0, 75, 0))));
        SaveForGallery(session, "motor-mount-direct-edit");
    }
}
