using System;

namespace Prism.Core;

/// <summary>Analytic, length-preserving cylinder UV layout; retained across save/open.</summary>
public sealed record FlatPatternMap(Guid SourceId,int FaceIndex,double[] Cylinder,double FlatX,double FlatY)
{
    public double Width=>Cylinder[9]*(Cylinder[11]-Cylinder[10]);
    public double Height=>Cylinder[13]-Cylinder[12];
}
