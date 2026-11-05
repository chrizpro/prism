using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Prism.Rendering;

public readonly record struct DenoiseGuide(Vector3 Normal, float Depth, int Material);

/// <summary>Three-pass à-trous filter on covered linear radiance, guided by first-hit geometry and variance.
/// Alpha, background compositing and the raw accumulation are deliberately not filtered.</summary>
public static class RenderDenoiser
{
    static readonly float[] Kernel = [1, 4, 6, 4, 1];
    static float Luma(Vector3 c) => Vector3.Dot(c, new(.2126f, .7152f, .0722f));
    public static Vector3[] Filter(Vector3[] color, float[] coverage, float[] variance, DenoiseGuide[] guides, int width, int height, CancellationToken cancellation = default)
    {
        int count = checked(width * height);
        if (color.Length != count || coverage.Length != count || variance.Length != count || guides.Length != count) throw new ArgumentException("Denoising buffers do not match the image size.");
        var current = (Vector3[])color.Clone();
        for (int pass = 0; pass < 3; pass++)
        {
            int step = 1 << pass; var next = new Vector3[count];
            Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellation }, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x; var guide = guides[i];
                    // Keep partial silhouettes and missing primary guides untouched.
                    if (coverage[i] < .999f || guide.Material < 0) { next[i] = current[i]; continue; }
                    var sum = Vector3.Zero; float total = 0, center = Luma(current[i]);
                    for (int ky = -2; ky <= 2; ky++) for (int kx = -2; kx <= 2; kx++)
                    {
                        int px = x + kx * step, py = y + ky * step;
                        if (px < 0 || px >= width || py < 0 || py >= height) continue;
                        int j = py * width + px; var other = guides[j];
                        if (other.Material != guide.Material || coverage[j] < .999f) continue;
                        float nd = Math.Max(0, Vector3.Dot(guide.Normal, other.Normal));
                        float depthScale = Math.Max(.01f, Math.Abs(guide.Depth) * .01f) * step;
                        float colorScale = Math.Max(.015f, 3 * MathF.Sqrt(Math.Max(variance[i], variance[j]))) + .025f * Math.Abs(center);
                        float weight = Kernel[kx + 2] * Kernel[ky + 2] * MathF.Pow(nd, 64) *
                            MathF.Exp(-Math.Abs(guide.Depth - other.Depth) / depthScale - Vector3.Distance(current[i], current[j]) / colorScale);
                        sum += current[j] * weight; total += weight;
                    }
                    next[i] = total > 1e-10f ? sum / total : current[i];
                }
            });
            current = next;
        }
        return current;
    }
}
