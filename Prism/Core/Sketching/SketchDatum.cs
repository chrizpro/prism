using System;
using Prism.Kernel;

namespace Prism.Core.Sketching;

public enum SketchDatumKind { Point, Axis, Plane, Gizmo }
public sealed class SketchDatum
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Datum";
    public SketchDatumKind Kind { get; set; }
    public Plane3 Frame { get; set; } = Plane3.XY;
    public double Scale { get; set; } = 1;
    /// <summary>A plane's height as a multiple of its width (1 is square).</summary>
    public double Stretch { get; set; } = 1;
    public SketchDatum Clone() => new() { Id = Id, Name = Name, Kind = Kind, Frame = Frame, Scale = Scale, Stretch = Stretch };
}
