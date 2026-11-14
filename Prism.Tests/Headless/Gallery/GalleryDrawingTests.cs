using Avalonia.Headless.XUnit;
using Prism.Core;
using Xunit;

namespace Prism.HeadlessTests.Gallery;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/gallery/README.md">Startup gallery specification.</seealso>
[Trait("Feature", "Gallery")]
[Trait("Layer", "Headless")]
public sealed class GalleryDrawingTests
{
    [AvaloniaTheory(DisplayName = "GAL-H009 | Gallery drawings reopen with editable views, source bodies and measured dimensions")]
    [InlineData("wrench-drawing", 8)]
    [InlineData("serving-board-drawing", 18)]
    [InlineData("fold-stand-drawing", 140)]
    [Trait("TestId", "GAL-H009")]
    public void DrawingRetainsItsViewsAndDimensions(string id, double expectedDimension)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Prism.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string gallery = Path.Combine(root.FullName, "gallery");
        var catalog = GalleryCatalog.Parse(File.ReadAllText(Path.Combine(gallery, "catalog.json")));
        var entry = Assert.Single(catalog.Items, item => item.Id == id);
        Assert.Equal("Drawing", entry.Workflow);

        var session = new CadSession();
        session.Open(Path.Combine(gallery, "models", id + ".prism"));
        var sheet = Assert.Single(session.Drawings.Sheets);
        Assert.Equal("A3", sheet.Format);
        Assert.Equal(new[] { "top", "front", "iso" }, sheet.Views.Select(view => view.Orientation));
        Assert.All(sheet.Views, view =>
        {
            Assert.NotEmpty(view.Strokes);
            Assert.False(view.Unresolved);
            Assert.True(session.Find(Assert.Single(view.Sources))?.HasBrep);
        });
        Assert.Equal(3, sheet.Annotations.Count);
        Assert.All(sheet.Annotations, annotation => Assert.False(annotation.Unresolved));
        Assert.Contains(sheet.Annotations, annotation => Math.Abs(annotation.Measurement - expectedDimension) < 0.01);
    }
}
