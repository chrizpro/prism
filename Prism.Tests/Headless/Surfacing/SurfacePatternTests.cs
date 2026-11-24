using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <summary>Checks the geometry-based option using the four-face helmet gallery fixture.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/FlattenRefold.md"/>
[Trait("Feature", "Flatten")]
[Trait("Layer", "Headless")]
public sealed class SurfacePatternTests
{
    [AvaloniaFact(DisplayName = "FLT-H201 | Flatten a curved helmet into one pattern with visible panel boundaries")]
    [Trait("TestId", "FLT-H201")]
    public void HelmetHasCurvedOutlineAndJoinedFaceBoundaries()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "gallery", "models", "meridian-helmet.prism")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var session = new CadSession();
        session.Open(Path.Combine(directory.FullName, "gallery", "models", "meridian-helmet.prism"));
        var shell = Assert.Single(session.Objects, o => o.Name == "After - four-panel graphite shell");
        Assert.Equal(4, shell.Shape.Count(SubKind.Face));
        var command = Begin(session, () => new FlattenCommand());
        Pick(command, "Faces or a surface to flatten", shell);
        SetChoice(command, "Layout method", "Surface pattern with face boundaries");
        var output = BuildOutput(command);
        var pattern = Assert.Single(output.Add, o => o.NamePrefix == "Flat pattern");
        Assert.True(pattern.Shape!.IsValid);
        Assert.Equal(1, pattern.Shape.Count(SubKind.Wire));
        Assert.True(pattern.Shape.Count(SubKind.Edge) > 15);
        var bounds = pattern.Shape.Props();
        Assert.InRange(bounds.Area / (bounds.Size.X * bounds.Size.Y), .5, .9);
        var seams = Assert.Single(output.Add, o => o.NamePrefix == "Flat face boundaries");
        Assert.True(seams.Shape!.IsValid);
        Assert.True(seams.Shape.Count(SubKind.Edge) >= 3);
        Assert.Null(pattern.FlatMap);
    }
}
