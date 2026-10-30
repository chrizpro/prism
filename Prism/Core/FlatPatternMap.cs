using System;

namespace Prism.Core;

/// <summary>Persistent surface layout. Cylinders keep an analytic frame; general faces store source and reference B-reps.</summary>
public sealed record FlatPatternMap(Guid SourceId,int FaceIndex,double[] Cylinder,double FlatX,double FlatY)
{
    public string? SourceFaceBrep { get; init; }
    public string? ReferenceBrep { get; init; }
    public double LayoutWidth { get; init; }
    public double LayoutHeight { get; init; }
    public double Width=>Cylinder.Length == 14 ? Cylinder[9]*(Cylinder[11]-Cylinder[10]) : LayoutWidth;
    public double Height=>Cylinder.Length == 14 ? Cylinder[13]-Cylinder[12] : LayoutHeight;
}
