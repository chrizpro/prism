using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/surface-and-nurbs/README.md">Surface and NURBS workflows.</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class SurfaceOptionTests
{
    [AvaloniaTheory(DisplayName = "SFC-H001 | Signed offset keeps a planar face area and moves it to the specified height")]
    [InlineData("Surface", 3)]
    [InlineData("Surface", -3)]
    [InlineData("NURBS", 3)]
    [InlineData("NURBS", -3)]
    [Trait("TestId", "SFC-H001")]
    public void OffsetPlaneFromRibbon(string tab, int distance)
    {
        var vm = new MainViewModel();
        var face = Sheet(vm.Session);
        var command = Start<OffsetSurfaceCommand>(vm, tab);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Surface or face", face);
            SetNumber(command, "Offset distance", distance);
        });
        using var result = BuildSingleAddedShape(command);
        Assert.True(result.IsValid);
        Assert.Equal(0, result.Count(SubKind.Solid));
        AssertNear(100, result.Props().Area, "Offset area (mm²)");
        AssertNear(distance, result.Props().Center.Z, "Offset elevation (mm)");
    }

    [AvaloniaTheory(DisplayName = "SFC-H002 | Signed thickness creates a solid on the chosen side of the sheet")]
    [InlineData("Surface", 2)]
    [InlineData("Surface", -2)]
    [InlineData("NURBS", 2)]
    [InlineData("NURBS", -2)]
    [Trait("TestId", "SFC-H002")]
    public void ThickenSheetFromRibbon(string tab, int thickness)
    {
        var vm = new MainViewModel();
        var face = Sheet(vm.Session);
        var command = Start<ThickenCommand>(vm, tab);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Surface", face);
            SetNumber(command, "Thickness", thickness);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        Assert.Equal(face.Id, Assert.Single(output.Remove).Id);
        using var result = Assert.Single(output.Add).Shape!;
        AssertValidSolid(result);
        AssertNear(200, result.Props().Volume, "Volume (mm³)");
        AssertBounds(result, new Vec3(0, 0, Math.Min(0, thickness)), new Vec3(10, 10, Math.Max(0, thickness)));
    }

    [AvaloniaTheory(DisplayName = "SFC-H003 | Isocurve direction and count produce straight, distinct 10 mm sections")]
    [InlineData("Surface", "U lines (constant U)", 1)]
    [InlineData("Surface", "V lines (constant V)", 3)]
    [InlineData("NURBS", "U lines (constant U)", 3)]
    [InlineData("NURBS", "V lines (constant V)", 1)]
    [Trait("TestId", "SFC-H003")]
    public void IsocurveCountAndDirection(string tab, string direction, int count)
    {
        var vm = new MainViewModel();
        var grid = Occ.NurbsSurface([Vec3.Zero, new(10, 0, 0), new(0, 10, 0), new(10, 10, 0)], 2, 2, 1, 1);
        var face = vm.Session.AddObject(grid, "UV grid", ObjKind.Surface);
        var command = Start<IsoCurveCommand>(vm, tab);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Surface or face", face);
            SetChoice(command, "Direction", direction);
            SetNumber(command, "Position (%)", 25);
            command.Params.OfType<IntParam>().Single(p => p.Label == "How many").Value = count;
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        Assert.Equal(count, output.Add.Count);
        bool constantU = direction.StartsWith("U lines", StringComparison.Ordinal);
        var offsets = new List<double>();
        foreach (var item in output.Add)
        {
            using var curve = item.Shape!;
            AssertNear(10, Occ.CurveLength(curve), "Section length (mm)");
            var props = curve.Props();
            AssertNear(0, constantU ? props.Size.Y : props.Size.X, "Constant parameter axis span (mm)");
            offsets.Add(constantU ? props.Center.Y : props.Center.X);
        }
        offsets.Sort();
        for (int index = 0; index < count; index++)
            AssertNear(count == 1 ? 2.5 : 10.0 * (index + 1) / (count + 1), offsets[index], "Section offset (mm)");
    }

    [AvaloniaTheory(DisplayName = "SFC-H004 | Control Grid honors the reference plane, size, offset and degree inputs")]
    [InlineData("XY (ground)", 2, 2, 1)]
    [InlineData("XZ (front)", 4, 5, 3)]
    [InlineData("YZ (side)", 6, 6, 5)]
    [Trait("TestId", "SFC-H004")]
    public void ControlGridCreatesFlatPatchInSelectedPlane(string option, int columns, int rows, int degree)
    {
        var vm = new MainViewModel();
        var command = Start<NurbsPatchCommand>(vm, "NURBS");
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Plane", option);
            SetNumber(command, "Width", 20);
            SetNumber(command, "Height", 10);
            SetNumber(command, "Plane offset", 7);
            SetNumber(command, "Initial relief", 0);
            command.Params.OfType<IntParam>().Single(p => p.Label == "Points across").Value = columns;
            command.Params.OfType<IntParam>().Single(p => p.Label == "Points up").Value = rows;
            command.Params.OfType<IntParam>().Single(p => p.Label == "Degree").Value = degree;
        });
        using var patch = BuildSingleAddedShape(command);
        Assert.True(patch.IsValid);
        Assert.Equal(1, patch.Count(SubKind.Face));
        using var face = patch.Sub(SubKind.Face, 0);
        var net = Occ.FaceNet(face);
        Assert.Equal(rows, net.Rows);
        Assert.Equal(columns, net.Cols);
        Assert.Equal(degree, net.DegU);
        Assert.Equal(degree, net.DegV);
        AssertNear(200, patch.Props().Area, "Patch area (mm²)");
        var plane = option switch
        {
            "XZ (front)" => Plane3.XZ,
            "YZ (side)" => Plane3.YZ,
            _ => Plane3.XY,
        };
        AssertNear(7, Vec3.Dot(patch.Props().Center, plane.Normal), "Patch offset (mm)");
    }

    // NURBS exposes the same finishing and boolean tools as the other modeling tabs; Freeform stays out because
    // Catmull-Clark cages do not survive exact booleans (they convert to NURBS first).
    [AvaloniaFact(DisplayName = "SFC-H005 | Enclosed Body launches from the NURBS Finish group")]
    [Trait("TestId", "SFC-H005")]
    public void EnclosedBodyFromNurbsRibbon()
    {
        var vm = new MainViewModel();
        Assert.IsType<BoundaryFillCommand>(Start<BoundaryFillCommand>(vm, "NURBS"));
    }

    [AvaloniaTheory(DisplayName = "SFC-H006 | Union, Subtract and Intersect run from every modeling tab")]
    [InlineData("Home")]
    [InlineData("Solid")]
    [InlineData("Surface")]
    [InlineData("NURBS")]
    [InlineData("Sheet Metal")]
    [Trait("TestId", "SFC-H006")]
    public void CombineOnModelingTabs(string tab)
    {
        var vm = new MainViewModel();
        var buttons = vm.Tabs.Single(t => t.Title == tab).Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct().ToList();
        var labels = buttons.Select(b => b.Label.Replace('\n', ' ')).ToList();
        Assert.Contains("Union", labels);
        Assert.Contains("Subtract", labels);
        Assert.Contains("Intersect", labels);
        buttons.Single(b => b.Menu == null && b.Label.Replace('\n', ' ') == "Union").Invoke();
        var command = Assert.IsType<BooleanCommand>(vm.Session.ActiveCommand);
        Assert.Equal("Union", command.Title);
        vm.Session.CancelCommand();
    }

    [AvaloniaFact(DisplayName = "SFC-H007 | Freeform omits CAD booleans: Catmull-Clark cages convert to NURBS first")]
    [Trait("TestId", "SFC-H007")]
    public void FreeformOmitsCombine()
    {
        var vm = new MainViewModel();
        var labels = vm.Tabs.Single(t => t.Title == "Freeform").Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct()
            .Select(b => b.Label.Replace('\n', ' ')).ToList();
        Assert.DoesNotContain(labels, label => label is "Union" or "Subtract" or "Intersect");
    }

    [AvaloniaTheory(DisplayName = "SFC-H008 | Split Surface cuts a sheet into separate surfaces from Surface and NURBS")]
    [InlineData("Surface")]
    [InlineData("NURBS")]
    [Trait("TestId", "SFC-H008")]
    public void SplitSurfaceFromRibbon(string tab)
    {
        var vm = new MainViewModel();
        var sheet = Sheet(vm.Session);
        using var wallEdge = Occ.Polyline([new(5, -5, -5), new(5, 15, -5), new(5, 15, 15), new(5, -5, 15)], true);
        var wall = vm.Session.AddObject(Occ.Profile([wallEdge], true), "Wall", ObjKind.Surface);
        var command = Start<SplitSurfaceCommand>(vm, tab);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Surface to split", sheet);
            SetSelection(command, "Splitting tool (plane, face, surface or solid)", wall);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var kept = Assert.Single(output.Replace);
        var added = Assert.Single(output.Add);
        using var a = kept.New; using var b = added.Shape!;
        Assert.True(a.IsValid && b.IsValid);
        AssertNear(50, a.Props().Area, "First piece area (mm²)");       // halves of the 10×10 sheet
        AssertNear(50, b.Props().Area, "Second piece area (mm²)");
    }

    [AvaloniaTheory(DisplayName = "SFC-H009 | Mirror launches from the shared Transform group on modeling tabs")]
    [InlineData("Solid")]
    [InlineData("Surface")]
    [InlineData("NURBS")]
    [InlineData("Freeform")]
    [Trait("TestId", "SFC-H009")]
    public void MirrorOnModelingTabs(string tab)
    {
        var vm = new MainViewModel();
        var buttons = vm.Tabs.Single(t => t.Title == tab).Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct().ToList();
        var button = buttons.Single(b => b.Menu == null && b.Label.Replace('\n', ' ') == "Mirror");
        button.Invoke();
        var command = Assert.IsType<TransformCommand>(vm.Session.ActiveCommand);
        Assert.Equal("Mirror", command.Title);
        vm.Session.CancelCommand();
    }

    static SceneObject Sheet(CadSession session)
    {
        using var boundary = Occ.Polyline([Vec3.Zero, new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)], true);
        return session.AddObject(Occ.Profile([boundary], true), "Sheet", ObjKind.Surface);
    }

    static T Start<T>(MainViewModel vm, string tab) where T : Command, new()
    {
        var title = new T().Title;
        var button = vm.Tabs.Single(t => t.Title == tab).Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct()
            .Single(b => b.Label.Replace('\n', ' ') == title || (title == "Offset Surface" && b.Label == "Offset"));
        button.Invoke();
        return Assert.IsType<T>(vm.Session.ActiveCommand);
    }
}
