using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Faces;

/// <summary>
/// Color, finish and hidden state of single faces, and how they follow a face through edits.
/// Specification: <see href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#faces-as-first-class-things-proposed-2026-11-13">Faces as first-class things</see>
/// (owner decisions 2026-11-13: color per face, Select Same, hide faces, solids and surfaces, loads follow faces).
/// </summary>
[Trait("Feature", "Faces")]
[Trait("Layer", "Headless")]
public class FaceStyleTests
{
    const uint Red = 0xFFE03030;

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

    static SceneObject Sheet(CadSession session)
    {
        using var boundary = Occ.Polyline([Vec3.Zero, new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)], true);
        return session.AddObject(Occ.Profile([boundary], true), "Sheet", ObjKind.Surface);
    }

    /// <summary>Split the sheet along x = 5 with the Split Surface command (default result: one surface, a face per piece).</summary>
    static SceneObject Split(MainViewModel vm, SceneObject sheet)
    {
        using var lineWire = Occ.Polyline([new(5, -5, 0), new(5, 15, 0)], false);
        var line = vm.Session.AddObject(lineWire, "Split line", ObjKind.Curve);
        var title = new SplitSurfaceCommand().Title;
        var button = vm.Tabs.Single(t => t.Title == "NURBS").Buttons.SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct().Single(b => b.Label.Replace('\n', ' ') == title);
        button.Invoke();
        var command = Assert.IsType<SplitSurfaceCommand>(vm.Session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Surface to split", sheet);
            SetSelection(command, "Splitting tool (plane, face, surface, sketch, curve or solid)", line);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        vm.Session.CancelCommand();
        vm.Session.Apply(output);
        return vm.Session.Find(sheet.Id)!;
    }

    [AvaloniaFact(DisplayName = "FACE-H001 | A color set with one face selected changes only that face, and Undo restores it")]
    [Trait("TestId", "FACE-H001")]
    public void ColorOneFace()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        uint bodyColor = box.Color;
        int top = FaceAtZ(box, box.Mesh.Max.Z);

        s.SetSelection(new SelItem(box.Id, SelKind.Face, top), false);
        Assert.True(vm.EditingFaces);
        vm.ApplyColor(Red);

        var after = s.Find(box.Id)!;
        Assert.Equal(bodyColor, after.Color);                             // the body keeps its color
        Assert.Equal(Red, after.FaceStyleOf(top)!.Color);
        for (int f = 0; f < after.Mesh.FaceCount; f++) if (f != top) Assert.Null(after.FaceStyleOf(f));
        Assert.Equal(Red, CadSession.EffectiveLook(after, top).color);

        s.Undo();
        Assert.Null(s.Find(box.Id)!.FaceStyles);
    }

    [AvaloniaFact(DisplayName = "FACE-H002 | A face keeps its color when the body is edited, and a body selected whole is still colored whole")]
    [Trait("TestId", "FACE-H002")]
    public void ColorFollowsEdit()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = s.AddObject(Occ.Box(20, 20, 20), "Box", ObjKind.Solid);
        int top = FaceAtZ(box, 20);
        s.SetSelection(new SelItem(box.Id, SelKind.Face, top), false);
        vm.ApplyColor(Red);

        s.Update(box.Id, o => o.WithShape(Occ.Box(30, 20, 20)), checkpoint: false);

        var edited = s.Find(box.Id)!;
        int newTop = FaceAtZ(edited, 20);
        Assert.Equal(Red, edited.FaceStyleOf(newTop)!.Color);
        Assert.Equal(1, edited.FaceStyles!.Count(x => x != null));
        Assert.Equal(0, edited.FaceStylesLost);

        s.SetSelection(SelItem.Of(edited), false);                         // whole body selected: the body color changes, faces stay as they are
        Assert.False(vm.EditingFaces);
        vm.ApplyColor(0xFF2060C0);
        var whole = s.Find(box.Id)!;
        Assert.Equal(0xFF2060C0, whole.Color);
        Assert.Equal(Red, whole.FaceStyleOf(newTop)!.Color);
    }

    [AvaloniaFact(DisplayName = "FACE-H003 | When a colored face is split, both pieces keep its color")]
    [Trait("TestId", "FACE-H003")]
    public void SplitPiecesInheritColor()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var sheet = Sheet(s);
        s.SetSelection(new SelItem(sheet.Id, SelKind.Face, 0), false);
        vm.ApplyColor(Red);

        var split = Split(vm, sheet);

        Assert.Equal(2, split.Mesh.FaceCount);
        Assert.Equal(Red, split.FaceStyleOf(0)!.Color);
        Assert.Equal(Red, split.FaceStyleOf(1)!.Color);
        Assert.Equal(0, split.FaceStylesLost);
    }

    [AvaloniaFact(DisplayName = "FACE-H004 | Coloring one piece of a split surface leaves the other; Reset returns it to the body")]
    [Trait("TestId", "FACE-H004")]
    public void ColorOnePieceThenReset()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var split = Split(vm, Sheet(s));
        s.SetSelection(new SelItem(split.Id, SelKind.Face, 1), false);
        vm.ApplyColor(Red);
        var colored = s.Find(split.Id)!;
        Assert.Null(colored.FaceStyleOf(0));
        Assert.Equal(Red, colored.FaceStyleOf(1)!.Color);

        vm.ResetAppearance();
        var reset = s.Find(split.Id)!;
        Assert.Null(reset.FaceStyles);                                     // nothing left to keep
        Assert.Equal(colored.Color, reset.Color);
    }

    [AvaloniaFact(DisplayName = "FACE-H005 | Finish edits on a face (metal, gloss) are the face's own and leave the body finish alone")]
    [Trait("TestId", "FACE-H005")]
    public void FullFinishPerFace()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        int top = FaceAtZ(box, box.Mesh.Max.Z);
        s.SetSelection(new SelItem(box.Id, SelKind.Face, top), false);

        vm.LookMetal = 1; vm.LookGloss = 0.9;

        var after = s.Find(box.Id)!;
        Assert.Equal(box.Look.Metal, after.Look.Metal);
        var face = after.FaceStyleOf(top)!;
        Assert.Equal(1f, face.Look!.Metal);
        Assert.Equal(0.9f, face.Look.Gloss, 3);
        Assert.Null(face.Color);                                          // only the finish changed; the face still follows the body color
    }

    [AvaloniaFact(DisplayName = "FACE-H006 | Hide removes selected faces from drawing and picking, is saved in the document, and Show All restores them")]
    [Trait("TestId", "FACE-H006")]
    public void HideAndShowFaces()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        int top = FaceAtZ(box, box.Mesh.Max.Z);
        s.SetSelection(new SelItem(box.Id, SelKind.Face, top), false);

        vm.HideSelected();

        var hidden = s.Find(box.Id)!;
        Assert.True(hidden.IsFaceHidden(top));
        Assert.True(hidden.Visible);                                      // the body is still there
        Assert.DoesNotContain(top, s.ShownFaces(hidden)!);
        Assert.Equal(5, s.ShownFaces(hidden)!.Count);
        Assert.False(s.ShownByIsolation(new SelItem(box.Id, SelKind.Face, top)));
        Assert.DoesNotContain(s.Selection, i => i.Kind == SelKind.Face && i.Index == top);

        var path = Path.Combine(Path.GetTempPath(), $"faces-{Guid.NewGuid():N}.prism");
        try
        {
            s.Save(path);
            var reopened = new CadSession(); reopened.Open(path);
            Assert.True(reopened.Objects.Single().IsFaceHidden(top));
        }
        finally { File.Delete(path); }

        vm.ShowAll();
        Assert.False(s.Find(box.Id)!.IsFaceHidden(top));
        Assert.Null(s.ShownFaces(s.Find(box.Id)!));
    }

    [AvaloniaFact(DisplayName = "FACE-H007 | Face color and finish are saved with the document and come back on reopening")]
    [Trait("TestId", "FACE-H007")]
    public void ColorSurvivesSaveAndReopen()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        int top = FaceAtZ(box, box.Mesh.Max.Z);
        s.SetSelection(new SelItem(box.Id, SelKind.Face, top), false);
        vm.ApplyColor(Red);
        vm.LookMetal = 1;

        var path = Path.Combine(Path.GetTempPath(), $"faces-{Guid.NewGuid():N}.prism");
        try
        {
            s.Save(path);
            var reopened = new CadSession(); reopened.Open(path);
            var body = reopened.Objects.Single();
            Assert.Equal(Red, body.FaceStyleOf(top)!.Color);
            Assert.Equal(1f, body.FaceStyleOf(top)!.Look!.Metal);
            Assert.Equal(1, body.FaceStyles!.Count(x => x != null));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact(DisplayName = "FACE-H008 | Select Same: same color, adjacent faces and linked faces")]
    [Trait("TestId", "FACE-H008")]
    public void SelectSame()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        int top = FaceAtZ(box, box.Mesh.Max.Z), bottom = FaceAtZ(box, box.Mesh.Min.Z);
        s.SelectItems([new SelItem(box.Id, SelKind.Face, top), new SelItem(box.Id, SelKind.Face, bottom)]);
        vm.ApplyColor(Red);

        s.SelectItems([new SelItem(box.Id, SelKind.Face, top)]);
        Assert.Equal(1, s.SelectSameFaces(CadSession.FaceMatch.SameColor));          // the bottom
        Assert.Contains(new SelItem(box.Id, SelKind.Face, bottom), s.Selection);

        s.SelectItems([new SelItem(box.Id, SelKind.Face, top)]);
        Assert.Equal(4, s.SelectSameFaces(CadSession.FaceMatch.Adjacent));            // the four sides, not the bottom
        Assert.DoesNotContain(new SelItem(box.Id, SelKind.Face, bottom), s.Selection);

        s.SelectItems([new SelItem(box.Id, SelKind.Face, top)]);
        s.SelectSameFaces(CadSession.FaceMatch.Linked);
        Assert.Equal(6, s.Selection.Count(i => i.Kind == SelKind.Face));
    }

    [AvaloniaFact(DisplayName = "FACE-H009 | A fixture or load on a face follows it when the surface is split")]
    [Trait("TestId", "FACE-H009")]
    public void LoadFollowsSplitFace()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var sheet = Sheet(s);
        var study = new Simulation.StudyDef { BodyId = sheet.Id };
        var load = new Simulation.Load { Faces = [new(sheet.Id, 0)] };
        study.Loads.Add(load);
        s.Studies.Add(study);

        Split(vm, sheet);

        Assert.Equal([0, 1], load.Faces.Select(f => f.Face).OrderBy(f => f));
    }
}
