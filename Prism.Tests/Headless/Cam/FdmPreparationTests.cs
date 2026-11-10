using System.IO.Compression;
using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Export;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Cam;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/cam/README.md#3d-fdm">3D FDM workflow.</seealso>
[Trait("Feature", "CAM")]
[Trait("Layer", "Headless")]
public sealed class FdmPreparationTests
{
    [AvaloniaFact(DisplayName = "CAM-H008 | FDM ribbon exposes the preparation and slicer handoff workflow")]
    [Trait("TestId", "CAM-H008")]
    public void RibbonHasFourPrimaryControls()
    {
        var vm = new MainViewModel();
        var tab = vm.Tabs.Single(tab => tab.Title == "3D FDM");
        var buttons = tab.Groups.SelectMany(group => group.Items).OfType<RibbonButton>().ToArray();
        Assert.Equal(new[] { "Place on bed", "FDM check", "Troubleshoot geometry", "Export for printing" },
            buttons.Select(button => button.Label));
        Assert.Equal(5, buttons[2].Menu!.Count);
        Assert.False(buttons[2].IsSplit);
        buttons[0].Invoke();
        Assert.IsType<PlaceOnBedCommand>(vm.Session.ActiveCommand);
    }

    [AvaloniaTheory(DisplayName = "CAM-H009 | A selected planar face lands at Z zero without resizing the part")]
    [InlineData(1, 0, 0, 10)]
    [InlineData(0, 0, 1, 4)]
    [InlineData(0, 0, -1, 4)]
    [Trait("TestId", "CAM-H009")]
    public void PlaceSolidOnBed(int x, int y, int z, int expectedHeight)
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new(10, 20, 30), 10, 8, 4);
        var target = new Vec3(x, y, z);
        var face = Enumerable.Range(0, body.Shape.Count(SubKind.Face))
            .Select(i => new SelItem(body.Id, SelKind.Face, i))
            .Single(item => Vec3.Dot(session.FaceFrame(item)!.Value.normal, target) > .99);
        var command = Begin(session, () => new PlaceOnBedCommand());
        Pick(command, "Face to place on bed", face);
        Assert.Null(command.Error);
        session.CommitCommand();
        var placed = session.Find(body.Id)!;
        var bounds = placed.Shape.Props();
        Assert.Equal(0, bounds.Min.Z, 5);
        Assert.Equal(expectedHeight, bounds.Max.Z, 5);
        Assert.Equal(320, bounds.Volume, 5);
    }

    [AvaloniaTheory(DisplayName = "CAM-H010 | Print export review follows selection and the 3MF preserves separate named bodies")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("TestId", "CAM-H010")]
    public void ExportReviewAndThreeMfUseTheSameBodyScope(bool resin)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 8, 4);
        var cylinder = session.AddObject(CylinderAt(new(30, 0, 0), 6, 7), "Curved body", ObjKind.Solid);
        var command = StartCommand(session, box, () => new PrintExportCommand(resin));
        var folder = Path.Combine(Path.GetTempPath(), "prism-fdm-test-" + Guid.NewGuid());
        SetText(command, "Folder", folder);
        command.Params.OfType<ActionParam>().Single(p => p.Label == "Export").Run();
        Assert.False(Directory.Exists(folder));
        Assert.Contains(command.Results, row => row.Label == box.Name && row.Value == "10 × 8 × 4 mm");
        Assert.DoesNotContain(command.Results, row => row.Label == cylinder.Name);

        session.SelectItems([SelItem.Of(cylinder)]);
        command.Params.OfType<ActionParam>().Single(p => p.Label == "Export").Run();
        Assert.False(Directory.Exists(folder));
        Assert.Contains(command.Results, row => row.Label == cylinder.Name);
        Assert.DoesNotContain(command.Results, row => row.Label == box.Name);
        try
        {
            var path = Assert.Single(PrintExport.Export(session,
                new(folder, "two-bodies", PrintFormat.ThreeMf, false, false, 1)));
            using var zip = ZipFile.OpenRead(path);
            using var modelFile = zip.GetEntry("3D/3dmodel.model")!.Open();
            var model = XDocument.Load(modelFile).Root!;
            var ns = model.Name.Namespace;
            Assert.Equal("millimeter", (string?)model.Attribute("unit"));
            Assert.Equal(new[] { box.Name, cylinder.Name },
                model.Descendants(ns + "object").Select(o => (string?)o.Attribute("name")));
            Assert.Equal(2, model.Descendants(ns + "item").Count());
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
