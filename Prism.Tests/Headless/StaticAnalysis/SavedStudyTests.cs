using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;
using Xunit;

namespace Prism.HeadlessTests.StaticAnalysis;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/cae/static-analysis/README.md">Static structural study.</seealso>
[Trait("Feature", "StaticAnalysis")]
[Trait("Layer", "Headless")]
public class SavedStudyTests
{
    [AvaloniaFact(DisplayName = "STA-H020 | A study saved with a filleted, cut body still matches the body after the file is reopened")]
    [Trait("TestId", "STA-H020")]
    public void SavedStudyStaysValidAfterReopen()
    {
        var session = new CadSession();
        using var box = Occ.Box(60, 20, 8);
        using var hole = Occ.Translate(Occ.Cylinder(4, 20), new Vec3(30, 10, -6));
        var drilled = Occ.Cut(box, hole);
        var body = session.AddObject(Occ.Fillet(drilled, [1], 2), "Plate", ObjKind.Solid);
        drilled.Dispose();
        var study = session.NewStudy(body);
        string path = Path.Combine(Path.GetTempPath(), "prism-study-" + Guid.NewGuid().ToString("N") + ".prism");
        try
        {
            session.Save(path);
            var reopened = new CadSession();
            reopened.Open(path);
            var again = reopened.Objects.First(o => o.Kind == ObjKind.Solid);
            var saved = Assert.Single(reopened.Studies);
            Assert.Equal(study.ShapeSignature, StudySetup.Signature(body));
            StudySetup.Validate(again, saved.ShapeSignature, []);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
