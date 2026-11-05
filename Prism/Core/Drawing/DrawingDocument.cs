using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prism.Core.Drawing;

public readonly record struct SheetPoint(double X, double Y)
{
    public double Distance(SheetPoint b) => Math.Sqrt(Math.Pow(X - b.X, 2) + Math.Pow(Y - b.Y, 2));
    public static SheetPoint operator +(SheetPoint a, SheetPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static SheetPoint operator -(SheetPoint a, SheetPoint b) => new(a.X - b.X, a.Y - b.Y);
}

/// <summary>Serializable drawing geometry in model millimetres, independent of paper scale.</summary>
public sealed class DrawingStroke
{
    public int Kind { get; set; }
    public List<SheetPoint> Points { get; set; } = [];
    [JsonIgnore] public string Signature => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Kind + ":" + string.Join(";", Points.Select(p => FormattableString.Invariant($"{p.X:F5},{p.Y:F5}"))))));
    public bool TryCircle(out SheetPoint center, out double radius)
    {
        center = default; radius = 0;
        return Points.Count >= 9 && Points[0].Distance(Points[^1]) <= .02 && TryArc(out center, out radius);
    }
    public double? AnalyticRadius { get; set; }
    public SheetPoint AnalyticCenter { get; set; }
    public bool TryArc(out SheetPoint center, out double radius)
    {
        center = AnalyticCenter; radius = AnalyticRadius ?? 0;
        if (AnalyticRadius == null || radius <= 0 || Points.Count < 5) return false;
        var origin = center; double r = radius;
        return Points.All(p => Math.Abs(p.Distance(origin) - r) < Math.Max(.00001, r * .000001));
    }

}

public sealed class DraftView
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Front";
    public string Orientation { get; set; } = "front";
    public string Kind { get; set; } = "base";
    public double[] Direction { get; set; } = [0, 1, 0];
    public double[] XDirection { get; set; } = [1, 0, 0];
    public Guid? ParentId { get; set; }
    public bool Aligned { get; set; } = true;
    public string AlignmentAxis { get; set; } = "";
    public bool HiddenLines { get; set; } = true;
    public List<Guid> Sources { get; set; } = [];
    public Dictionary<Guid, string> SourceRevisions { get; set; } = [];
    public List<DrawingStroke> Strokes { get; set; } = [];
    public List<List<SheetPoint>> HatchLoops { get; set; } = [];
    public double X { get; set; } = 100;
    public double Y { get; set; } = 90;
    public double Scale { get; set; } = 1;
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public bool CutVertical { get; set; } = true;
    public double CutOffset { get; set; }
    public SheetPoint DetailCenter { get; set; }
    public double DetailRadius { get; set; } = 10;
    public double DetailRatio { get; set; } = 2;
    public string Caption { get; set; } = "A";
    public bool Unresolved { get; set; }
    public SheetPoint ToPaper(SheetPoint p) => new(X + (p.X - (MinX + MaxX) / 2) * Scale, Y - (p.Y - (MinY + MaxY) / 2) * Scale);
    public SheetPoint ToModel(SheetPoint p) => new((p.X - X) / Scale + (MinX + MaxX) / 2, -(p.Y - Y) / Scale + (MinY + MaxY) / 2);
    public override string ToString() => Name + (Unresolved ? " — unresolved" : "");
}

public sealed class DraftAnnotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ViewId { get; set; }
    public string Type { get; set; } = "note";
    public SheetPoint A { get; set; }
    public SheetPoint B { get; set; }
    public SheetPoint C { get; set; }
    public string? RefA { get; set; }
    public string? RefB { get; set; }
    public string? RefC { get; set; }
    /// <summary>Paper mm relative to the owning view's center; moving the view carries the annotation.</summary>
    public SheetPoint Label { get; set; } = new(20, -20);
    public string Text { get; set; } = "";
    public string Tolerance { get; set; } = "";
    public string Symbol { get; set; } = "position";
    public string Datums { get; set; } = "";
    public int Precision { get; set; } = 2;
    public double Radius { get; set; }
    public string SuggestionKey { get; set; } = "";
    public bool Unresolved { get; set; }
    [JsonIgnore] public bool IsDimension => Type is "horizontal" or "vertical" or "aligned" or "angular" or "diameter" or "radius" or "ordinate-x" or "ordinate-y";
    [JsonIgnore] public double Measurement => Type switch
    {
        "horizontal" or "ordinate-x" => Math.Abs(B.X - A.X),
        "vertical" or "ordinate-y" => Math.Abs(B.Y - A.Y),
        "diameter" => Radius * 2,
        "radius" => Radius,
        "angular" => Angle(A, B, C),
        _ => A.Distance(B),
    };
    static double Angle(SheetPoint a, SheetPoint vertex, SheetPoint b)
    {
        var u = a - vertex; var v = b - vertex;
        double len = a.Distance(vertex) * b.Distance(vertex);
        if (len < 1e-10) throw new InvalidOperationException("Pick two distinct arms of the angle.");
        return Math.Acos(Math.Clamp((u.X * v.X + u.Y * v.Y) / len, -1, 1)) * 180 / Math.PI;
    }
    public string ValueText(string units)
    {
        if (Text.Length > 0) return Text;
        double value = Measurement / (units == "in" && Type != "angular" ? 25.4 : 1);
        return (Type == "diameter" ? "Ø" : Type == "radius" ? "R" : "") + value.ToString("F" + Math.Clamp(Precision, 0, 6), CultureInfo.InvariantCulture) + (Type == "angular" ? "°" : "");
    }
    public override string ToString() => Type + (Text.Length > 0 ? ": " + Text.Replace('\n', ' ') : "") + (Unresolved ? " — unresolved" : "");
}

public sealed class DrawingPartRow
{
    public Guid SourceId { get; set; }
    public string Name { get; set; } = "";
    public string Material { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public override string ToString() => $"{Name} × {Quantity}";
}

public sealed class DraftSheet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Sheet 1";
    public string Format { get; set; } = "A3";
    public bool Portrait { get; set; }
    public string Units { get; set; } = "mm";
    public bool ThirdAngle { get; set; } = true;
    public double Scale { get; set; } = 1;
    public string Title { get; set; } = "Part drawing";
    public string Number { get; set; } = "";
    public string Material { get; set; } = "";
    public string DrawnBy { get; set; } = "";
    public string Company { get; set; } = "";
    public string Revision { get; set; } = "A";
    public string Date { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string Notes { get; set; } = "";
    public string GeneralTolerance { get; set; } = "";
    public bool ShowParts { get; set; }
    public List<DrawingPartRow> Parts { get; set; } = [];
    public List<DraftView> Views { get; set; } = [];
    public List<DraftAnnotation> Annotations { get; set; } = [];
    [JsonIgnore] public double Width => Portrait ? SheetFormat.All[Format].H : SheetFormat.All[Format].W;
    [JsonIgnore] public double Height => Portrait ? SheetFormat.All[Format].W : SheetFormat.All[Format].H;
    public override string ToString() => Name;
    public HashSet<Guid> Dependents(Guid viewId)
    {
        var ids = new HashSet<Guid> { viewId };
        while (true) { int n = ids.Count; foreach (var v in Views.Where(v => v.ParentId.HasValue && ids.Contains(v.ParentId.Value))) ids.Add(v.Id); if (n == ids.Count) return ids; }
    }
    public void MoveView(Guid id, double dx, double dy)
    {
        var view = Views.Single(v => v.Id == id);
        if (view.Aligned && view.ParentId is { } parent && view.Kind == "base")
        {
            var p = Views.Single(v => v.Id == parent);
            if (view.AlignmentAxis == "x") { dx = p.X - view.X; }
            else if (view.AlignmentAxis == "y") { dy = p.Y - view.Y; }
        }
        view.X += dx; view.Y += dy;
        foreach (var child in Views.Where(v => v.ParentId == id && v.Aligned && v.Kind == "base"))
        {
            if (child.AlignmentAxis == "x") MoveView(child.Id, view.X - child.X, 0);
            else if (child.AlignmentAxis == "y") MoveView(child.Id, 0, view.Y - child.Y);
        }
    }
    public DraftSheet Duplicate()
    {
        var copy = DrawingDocument.Clone(this); copy.Id = Guid.NewGuid(); copy.Name += " copy";
        var ids = copy.Views.ToDictionary(v => v.Id, _ => Guid.NewGuid());
        foreach (var v in copy.Views) { v.Id = ids[v.Id]; if (v.ParentId is { } p) v.ParentId = ids[p]; }
        foreach (var a in copy.Annotations) { a.Id = Guid.NewGuid(); a.ViewId = ids[a.ViewId]; }
        return copy;
    }
    public void SetProjection(bool thirdAngle)
    {
        if (ThirdAngle == thirdAngle) return;
        var old = Views.ToDictionary(v => v.Id, v => new SheetPoint(v.X, v.Y));
        foreach (var v in Views.Where(v => v.Kind == "base" && v.ParentId.HasValue))
        {
            var parent = Views.Single(p => p.Id == v.ParentId); var before = old[v.Id]; var parentBefore = old[parent.Id];
            if (v.AlignmentAxis == "x" && v.Aligned) { v.X = parent.X; v.Y = parent.Y - (before.Y - parentBefore.Y); }
            if (v.AlignmentAxis == "y" && v.Aligned) { v.Y = parent.Y; v.X = parent.X - (before.X - parentBefore.X); }
        }
        ThirdAngle = thirdAngle;
    }
    public void SetViewScale(Guid viewId, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0 || scale > 100) throw new ArgumentException("Scale must be positive and at most 100.");
        var view = Views.Single(v => v.Id == viewId);
        if (view.ParentId is { } parentId && view.Kind == "base" && view.Aligned) scale = Views.Single(v => v.Id == parentId).Scale;
        view.Scale = scale;
        foreach (var child in Views.Where(v => v.ParentId == viewId))
        {
            if (child.Kind == "detail") SetViewScale(child.Id, scale * child.DetailRatio);
            else if (child.Kind == "base" && child.Aligned) SetViewScale(child.Id, scale);
        }
    }
}

/// <summary>Editable drawing data embedded in a Prism document. See docs/drawing/README.md.</summary>
public sealed class DrawingDocument
{
    public List<DraftSheet> Sheets { get; set; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);
    public static DrawingDocument Deserialize(string? json) => string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<DrawingDocument>(json, JsonOptions) ?? new();
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;
}
