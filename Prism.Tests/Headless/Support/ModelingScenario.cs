using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;

namespace Prism.HeadlessTests.Support;

/// <summary>Fixture mechanics only. Expected dimensions and volumes stay in the named tests.</summary>
internal static class ModelingScenario
{
    public static SceneObject CreateCurve(CadSession session, IReadOnlyList<Vec3> points, bool closed = false) =>
        session.AddObject(Occ.Polyline(points.ToList(), closed), "Test curve", ObjKind.Curve);

    public static void SetSelection(Command command, string label, params SceneObject[] objects)
    {
        var slot = command.Slots.Single(item => item.Label == label);
        slot.Items.Clear();
        foreach (var obj in objects) Assert.True(slot.TryAdd(SelItem.Of(obj)), $"'{label}' must accept '{obj.Name}'.");
    }

    public static SceneObject CreateBoxBody(CadSession session, Vec3 minimum,
        double width, double depth, double height)
    {
        using var box = Occ.Box(width, depth, height);
        // Primitives do not all share an origin convention; place the minimum explicitly.
        var positioned = Occ.Translate(box, minimum - box.Props().Min);
        return session.AddObject(positioned, "Reference body", ObjKind.Solid);
    }

    public static SceneObject CreateRectangleSketch(CadSession session, Plane3 plane,
        double width, double height, double startU = 0, double startV = 0, bool closed = true)
    {
        var sketch = new SketchModel();
        int bottomLeft = sketch.AddPoint(startU, startV);
        int bottomRight = sketch.AddPoint(startU + width, startV);
        int topRight = sketch.AddPoint(startU + width, startV + height);
        int topLeft = sketch.AddPoint(startU, startV + height);
        sketch.AddLine(bottomLeft, bottomRight);
        sketch.AddLine(bottomRight, topRight);
        sketch.AddLine(topRight, topLeft);
        if (closed) sketch.AddLine(topLeft, bottomLeft);

        session.EnterSketch(plane, sketch);
        session.ExitSketch(keep: true);
        return session.Objects.Last(obj => obj.Kind == ObjKind.Sketch);
    }

    public static T StartCommand<T>(CadSession session, SceneObject profile, Func<T> create) where T : Command
    {
        session.SelectItems([SelItem.Of(profile)]);
        session.StartCommand(() => create());
        var command = Assert.IsType<T>(session.ActiveCommand);
        if (command.Params.OfType<ChoiceParam>().Any(param => param.Label == "Operation"))
            SetChoice(command, "Operation", "New body");
        return command;
    }

    public static void SetNumber(Command command, string label, double value) =>
        command.Params.OfType<NumberParam>().Single(param => param.Label == label).Value = value;

    public static void SetChoice(Command command, string label, string option)
    {
        var parameter = command.Params.OfType<ChoiceParam>().Single(param => param.Label == label);
        int index = parameter.Options.ToList().IndexOf(option);
        Assert.True(index >= 0, $"'{label}' must offer '{option}'.");
        parameter.Index = index;
    }

    /// <summary>Start a command with nothing pre-selected, so each slot is filled explicitly by the test.</summary>
    public static T Begin<T>(CadSession session, Func<T> create) where T : Command
    {
        session.SelectItems([]);
        session.StartCommand(() => create());
        return Assert.IsType<T>(session.ActiveCommand);
    }

    /// <summary>Fill a slot with whole objects or sub-items, replacing whatever it held.</summary>
    public static void Pick(Command command, string label, params SelItem[] items) =>
        command.ConfigureInputs(() =>
        {
            var slot = command.Slots.Single(item => item.Label == label);
            slot.Items.Clear();
            foreach (var item in items) Assert.True(slot.TryAdd(item), $"'{label}' must accept {item.Kind} {item.Index}.");
        });

    public static void Pick(Command command, string label, params SceneObject[] objects) =>
        Pick(command, label, objects.Select(SelItem.Of).ToArray());

    public static void SetInteger(Command command, string label, int value) =>
        command.Params.OfType<IntParam>().Single(param => param.Label == label).Value = value;

    public static void SetText(Command command, string label, string value) =>
        command.Params.OfType<TextParam>().Single(param => param.Label == label).Value = value;

    public static CommandOutput BuildOutput(Command command)
    {
        var output = command.TryBuild();
        Assert.True(output != null, $"Command must build: {command.Error}");
        return output;
    }

    /// <summary>The command must refuse to build and explain why with a message containing the fragment.</summary>
    public static void AssertBuildFails(Command command, string fragment)
    {
        var output = command.TryBuild();
        Assert.Null(output);
        Assert.NotNull(command.Error);
        Assert.Contains(fragment, command.Error, StringComparison.OrdinalIgnoreCase);
    }

    public static string Result(Command command, string label) =>
        Assert.Single(command.Results, line => line.Label == label).Value;

    /// <summary>A single added curve's total length (mm).</summary>
    public static double Length(Shape curve) => Occ.CurveLength(curve);

    /// <summary>For quantities that come from approximations (spline fits, tessellation, sampled curves).</summary>
    public static void AssertClose(double expected, double actual, double tolerance, string quantity) =>
        Assert.True(double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance,
            $"{quantity}: expected {expected:G12}, actual {actual:G12}, tolerance {tolerance}.");

    public static void AssertPoint(Vec3 expected, Vec3 actual, double tolerance, string quantity) =>
        Assert.True(expected.DistanceTo(actual) <= tolerance, $"{quantity}: expected {expected}, actual {actual}, tolerance {tolerance}.");

    /// <summary>The single planar face of an object whose outward normal points along the given direction.</summary>
    public static SelItem FaceFacing(SceneObject source, Vec3 normal)
    {
        int index = Enumerable.Range(0, source.Shape.Count(SubKind.Face)).Single(i =>
        {
            using var face = source.Shape.Sub(SubKind.Face, i);
            return Vec3.Dot(Occ.FaceInfoOf(face).Normal, normal) > 0.99;
        });
        return new SelItem(source.Id, SelKind.Face, index);
    }

    /// <summary>Every edge of an object whose bounding box satisfies the predicate.</summary>
    public static SelItem[] EdgesWhere(SceneObject source, Func<ShapeProps, bool> predicate) =>
        Enumerable.Range(0, source.Shape.Count(SubKind.Edge)).Where(i =>
        {
            using var edge = source.Shape.Sub(SubKind.Edge, i);
            return predicate(edge.Props());
        }).Select(i => new SelItem(source.Id, SelKind.Edge, i)).ToArray();

    /// <summary>Start and end point of a curve or wire.</summary>
    public static (Vec3 Start, Vec3 End) Ends(Shape curve)
    {
        var points = Occ.CurveDivide(curve, 1).points;
        return (points[0], points[^1]);
    }

    public static void SetToggle(Command command, string label, bool value) =>
        command.Params.OfType<BoolParam>().Single(param => param.Label == label).Value = value;

    public static Shape BuildSingleAddedShape(Command command)
    {
        var output = command.TryBuild();
        Assert.True(output != null, $"Command must build: {command.Error}");
        var added = Assert.Single(output.Add);
        Assert.NotNull(added.Shape);
        return added.Shape;
    }

    public static void AssertValidSolid(Shape shape)
    {
        Assert.True(shape.IsValid, "OCCT must report valid topology.");
        Assert.Equal(1, shape.Count(SubKind.Solid));
    }

    public static void AssertNear(double expected, double actual, string quantity)
    {
        const double tolerance = 0.0001;
        Assert.True(double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance,
            $"{quantity}: expected {expected:G12}, actual {actual:G12}, tolerance {tolerance}.");
    }

    public static void AssertBounds(Shape shape, Vec3 expectedMin, Vec3 expectedMax)
    {
        var bounds = shape.Props();
        AssertNear(expectedMin.X, bounds.Min.X, "Minimum X (mm)");
        AssertNear(expectedMin.Y, bounds.Min.Y, "Minimum Y (mm)");
        AssertNear(expectedMin.Z, bounds.Min.Z, "Minimum Z (mm)");
        AssertNear(expectedMax.X, bounds.Max.X, "Maximum X (mm)");
        AssertNear(expectedMax.Y, bounds.Max.Y, "Maximum Y (mm)");
        AssertNear(expectedMax.Z, bounds.Max.Z, "Maximum Z (mm)");
    }
}
