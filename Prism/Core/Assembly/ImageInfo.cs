using System;

namespace Prism.Core.Assembly;

/// <summary>
/// A reference picture on a plane (a plane object carries it, so it can be hidden, selected, moved and saved like any other plane).
/// <see cref="Data"/> is the original file (PNG, JPG ...) as base64; the picture is drawn <see cref="WidthMm"/> wide, as tall as its shape needs.
/// </summary>
public sealed record ImageInfo(string Data, int PixelW, int PixelH, double WidthMm, double Opacity = 0.6, bool FlipH = false, bool FlipV = false, string Source = "") : AssemblyData
{
    public double HeightMm => WidthMm * PixelH / Math.Max(PixelW, 1);
}
