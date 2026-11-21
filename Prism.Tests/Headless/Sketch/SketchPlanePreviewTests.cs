using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sketch;

/// <summary>The highlighted preview plane of Create Sketch shows only when there is an offset to preview. Owner report 2026-11-16: it distracted at offset 0.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/README.md">Sketch drawing modes.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SketchPlanePreviewTests
{
    static (CadSession session, CreateSketchCommand command) Start(double offset)
    {
        var session = new CadSession();
        var command = Begin(session, () => new CreateSketchCommand());
        command.ConfigureInputs(() => SetNumber(command, "Offset", offset));
        return (session, command);
    }

    static Overlay Preview(CadSession session, CreateSketchCommand command)
    {
        var overlay = new Overlay();
        command.BuildOverlay(session, overlay);
        return overlay;
    }

    [AvaloniaFact(DisplayName = "SKT-H030 | At offset 0 no preview plane is drawn, with or without the pointer over a plane")]
    [Trait("TestId", "SKT-H030")]
    public void NoPreviewAtZero()
    {
        var (session, command) = Start(0);
        Assert.Empty(Preview(session, command).Tris);
        session.Hover = new SelItem(Guid.Empty, SelKind.Origin, 0);                  // pointer over the XY origin plane
        var hovering = Preview(session, command);
        Assert.Empty(hovering.Tris);
        Assert.Empty(hovering.Labels);
    }

    [AvaloniaFact(DisplayName = "SKT-H031 | With an offset the preview plane is drawn, 5 mm above the base plane, labelled with the offset")]
    [Trait("TestId", "SKT-H031")]
    public void PreviewAtAnOffset()
    {
        var (session, command) = Start(5);
        var overlay = Preview(session, command);
        Assert.NotEmpty(overlay.Tris);
        Assert.All(overlay.Tris, t => AssertNear(5, t.A.Z, "Preview plane height (mm)"));
        Assert.Contains(overlay.Labels, l => l.Text.Contains("+ 5 mm"));

        session.Hover = new SelItem(Guid.Empty, SelKind.Origin, 1);                  // pointing at the XZ plane previews the offset on that plane instead
        var hovering = Preview(session, command);
        Assert.NotEmpty(hovering.Tris);
        Assert.Contains(hovering.Labels, l => l.Text.StartsWith("XZ"));
    }
}
