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
            SetSelection(command, "Splitting tool (plane, face, surface, sketch, curve or solid)", wall);
            SetChoice(command, "Result", "Separate surfaces");
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

    [AvaloniaFact(DisplayName = "SFC-H010 | Split Surface splits along a curve's projection; pieces are separate objects")]
    [Trait("TestId", "SFC-H010")]
    public void SplitSurfaceByCurve()
    {
        var vm = new MainViewModel();
        var sheet = Sheet(vm.Session);
        // A curve crossing the sheet in its own plane: the split follows the curve's projection on the surface.
        using var lineWire = Occ.Polyline([new(5, -5, 0), new(5, 15, 0)], false);
        var line = vm.Session.AddObject(lineWire, "Split line", ObjKind.Curve);
        var command = Start<SplitSurfaceCommand>(vm, "Surface");
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Surface to split", sheet);
            SetSelection(command, "Splitting tool (plane, face, surface, sketch, curve or solid)", line);
            SetChoice(command, "Result", "Separate surfaces");
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var kept = Assert.Single(output.Replace);
        var added = Assert.Single(output.Add);
        using var a = kept.New; using var b = added.Shape!;
        Assert.True(a.IsValid && b.IsValid);
        AssertNear(50, a.Props().Area, "First piece area (mm²)");
        AssertNear(50, b.Props().Area, "Second piece area (mm²)");
    }

    [AvaloniaFact(DisplayName = "SFC-H011 | Split Surface cuts along every curve of the tool when the curves cross")]
    [Trait("TestId", "SFC-H011")]
    public void SplitSurfaceByCrossingCurves()
    {
        var vm = new MainViewModel();
        var sheet = Sheet(vm.Session);
        using var across = Occ.Polyline([new(-5, 5, 0), new(15, 5, 0)], false);
        using var along = Occ.Polyline([new(5, -5, 0), new(5, 15, 0)], false);
        var cross = vm.Session.AddObject(Occ.Compound([across, along]), "Cross", ObjKind.Curve);
        var command = Start<SplitSurfaceCommand>(vm, "NURBS");
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Surface to split", sheet);
            SetSelection(command, "Splitting tool (plane, face, surface, sketch, curve or solid)", cross);
            SetChoice(command, "Result", "Separate surfaces");
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var pieces = output.Add.Select(a => a.Shape!).Append(Assert.Single(output.Replace).New).ToList();
        Assert.Equal(4, pieces.Count);
        foreach (var piece in pieces)
        {
            Assert.True(piece.IsValid);
            AssertNear(25, piece.Props().Area, "Quarter area (mm²)");
            piece.Dispose();
        }
    }

    [AvaloniaFact(DisplayName = "SFC-H012 | Split Surface keeps one surface with a face per piece by default")]
    [Trait("TestId", "SFC-H012")]
    public void SplitSurfaceIntoFaces()
    {
        var vm = new MainViewModel();
        var sheet = Sheet(vm.Session);
        using var lineWire = Occ.Polyline([new(5, -5, 0), new(5, 15, 0)], false);
        var line = vm.Session.AddObject(lineWire, "Split line", ObjKind.Curve);
        var command = Start<SplitSurfaceCommand>(vm, "NURBS");
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Surface to split", sheet);
            SetSelection(command, "Splitting tool (plane, face, surface, sketch, curve or solid)", line);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        Assert.Empty(output.Add);
        using var result = Assert.Single(output.Replace).New;
        Assert.True(result.IsValid);
        Assert.Equal(2, result.Count(SubKind.Face));
        Assert.Equal(7, result.Count(SubKind.Edge));       // the cut is one edge shared by both faces, not two coincident ones
        AssertNear(100, result.Props().Area, "Whole area (mm²)");
        using var half = result.Sub(SubKind.Face, 0);
        AssertNear(50, half.Props().Area, "Face area (mm²)");
    }

    [AvaloniaFact(DisplayName = "SFC-H013 | Delete on a selected face of a split surface removes that face only")]
    [Trait("TestId", "SFC-H013")]
    public void DeleteKeyRemovesSurfaceFace()
    {
        var vm = new MainViewModel();
        var sheet = Sheet(vm.Session);
        using var lineWire = Occ.Polyline([new(5, -5, 0), new(5, 15, 0)], false);
        var line = vm.Session.AddObject(lineWire, "Split line", ObjKind.Curve);
        var command = Start<SplitSurfaceCommand>(vm, "NURBS");
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Surface to split", sheet);
            SetSelection(command, "Splitting tool (plane, face, surface, sketch, curve or solid)", line);
        });
        Assert.True(command.TryBuild() != null, command.Error);
        vm.Session.CommitCommand();
        vm.Session.CancelCommand();
        Assert.Equal(2, vm.Session.Find(sheet.Id)!.Shape.Count(SubKind.Face));

        vm.Session.SetSelection(new SelItem(sheet.Id, SelKind.Face, 0), false);
        vm.Session.DeleteSelection();

        var left = vm.Session.Find(sheet.Id);
        Assert.NotNull(left);
        Assert.Equal(ObjKind.Surface, left.Kind);
        Assert.Equal(1, left.Shape.Count(SubKind.Face));
        AssertNear(50, left.Shape.Props().Area, "Remaining area (mm²)");
        Assert.NotNull(vm.Session.Find(line.Id));
    }

    [AvaloniaFact(DisplayName = "SFC-H014 | Subtract refuses an open surface as the tool and leaves both objects")]
    [Trait("TestId", "SFC-H014")]
    public void SubtractRefusesSurfaceTool()
    {
        var vm = new MainViewModel();
        var sheet = Sheet(vm.Session);
        using var wallEdge = Occ.Polyline([new(5, -5, -5), new(5, 15, -5), new(5, 15, 15), new(5, -5, 15)], true);
        var wall = vm.Session.AddObject(Occ.Profile([wallEdge], true), "Wall", ObjKind.Surface);
        var command = new BooleanCommand(BooleanCommand.Kind.Subtract);
        vm.Session.StartCommand(command);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Body to cut from", sheet);
            SetSelection(command, "Tool bodies", wall);
        });
        Assert.Null(command.TryBuild());
        Assert.Contains("no inside", command.Error);
        vm.Session.CancelCommand();
        Assert.NotNull(vm.Session.Find(wall.Id));
    }

    [AvaloniaFact(DisplayName = "SFC-H015 | Intersect of surfaces that do not meet fails instead of leaving an empty object")]
    [Trait("TestId", "SFC-H015")]
    public void IntersectRefusesEmptyResult()
    {
        var vm = new MainViewModel();
        var sheet = Sheet(vm.Session);
        using var boundary = Occ.Polyline([new(0, 0, 20), new(10, 0, 20), new(10, 10, 20), new(0, 10, 20)], true);
        var above = vm.Session.AddObject(Occ.Profile([boundary], true), "Above", ObjKind.Surface);
        var command = new BooleanCommand(BooleanCommand.Kind.Intersect);
        vm.Session.StartCommand(command);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Base body", sheet);
            SetSelection(command, "Other bodies", above);
        });
        Assert.Null(command.TryBuild());
        Assert.Contains("do not overlap", command.Error);
        vm.Session.CancelCommand();
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
