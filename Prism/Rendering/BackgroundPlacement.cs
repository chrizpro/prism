using System;
using System.Numerics;

namespace Prism.Rendering;

public enum BackgroundFit { Fill, Fit, Stretch }
public static class BackgroundPlacement
{
    /// <summary>Display-space backplate, independent of lighting/exposure. Fit uses white letterboxing.</summary>
    public static Vector3 Sample(SurfaceImage image, int x, int y, int width, int height, BackgroundFit fit, float offsetX = 0, float offsetY = 0)
    {
        float sx = (float)width / image.Width, sy = (float)height / image.Height;
        float scale = fit == BackgroundFit.Fit ? Math.Min(sx, sy) : Math.Max(sx, sy);
        float w = fit == BackgroundFit.Stretch ? width : image.Width * scale;
        float h = fit == BackgroundFit.Stretch ? height : image.Height * scale;
        float u = (x + .5f - (width - w) / 2 - offsetX * width) / w, v = (y + .5f - (height - h) / 2 - offsetY * height) / h;
        return u < 0 || u > 1 || v < 0 || v > 1 ? Vector3.One : image.Sample(u, v, false);
    }
}
