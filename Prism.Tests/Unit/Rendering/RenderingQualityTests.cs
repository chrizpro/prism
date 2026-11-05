using System.Numerics;
using System.Text;
using Prism.Core;
using Prism.Rendering;
using Xunit;

namespace Prism.UnitTests.Rendering;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/rendering/README.md">Product rendering requirements and limitations.</seealso>
[Trait("Feature", "Rendering")]
[Trait("Layer", "Unit")]
public class RenderingQualityTests
{
    [Fact(DisplayName = "RND-U001 | Constant RGBE radiance and environment PDFs preserve linear light and sphere measure")]
    [Trait("TestId", "RND-U001")]
    public void ConstantHdrHasUniformSpherePdf()
    {
        var env = HdrEnvironment.Decode(ConstantHdr());
        for (int i = 0; i < 200; i++)
        {
            var d = env.Sample((i + .5f) / 200, .37f, .61f, out float pdf, 73);
            Assert.InRange(Math.Abs(pdf - 1 / (4 * MathF.PI)), 0, 1e-6f);
            Assert.InRange(Math.Abs(pdf - env.Pdf(d, 73)), 0, 1e-5f);
            Assert.Equal(new Vector3(2, 1, .5f), env.Radiance(d, 73));
            Assert.Equal(new Vector3(4, 2, 1), env.Radiance(d, 73, 2));
        }
    }
    public static string ConstantHdr()
    {
        using var stream = new MemoryStream(); stream.Write(Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 8\n"));
        for (int y = 0; y < 2; y++) stream.Write([2, 2, 0, 8, 136, 128, 136, 64, 136, 32, 136, 130]);
        return Convert.ToBase64String(stream.ToArray());
    }
    [Fact(DisplayName = "RND-U002 | Malformed and truncated HDR scanlines fail with explicit errors")]
    [Trait("TestId", "RND-U002")]
    public void BadHdrIsRejected()
    {
        var data = Convert.FromBase64String(ConstantHdr());
        Assert.Throws<ArgumentException>(() => HdrEnvironment.Decode(Convert.ToBase64String(data[..^2])));
        Assert.Throws<ArgumentException>(() => HdrEnvironment.Decode(Convert.ToBase64String(Encoding.ASCII.GetBytes("not an HDR"))));
        Assert.Throws<ArgumentException>(() => new HdrEnvironment(1, 1, [new(float.PositiveInfinity)]));
    }
    [Fact(DisplayName = "RND-U003 | Environment rotation moves a bright source and sampled directions match their PDFs")]
    [Trait("TestId", "RND-U003")]
    public void HotspotRotationAndImportanceSampling()
    {
        var pixels = Enumerable.Repeat(new Vector3(.01f), 32).ToArray(); pixels[8] = new(100);
        var env = new HdrEnvironment(8, 4, pixels); var direction = env.Sample(.1f, .5f, .5f, out _);
        int bright = 0;
        for (int i = 0; i < 1000; i++)
        {
            var d = env.Sample((i + .5f) / 1000, .3f, .7f, out float pdf);
            Assert.InRange(Math.Abs(pdf - env.Pdf(d)), 0, 1e-4f);
            if (env.Radiance(d).X > 10) bright++;
        }
        Assert.True(bright > 900);
        var rotated = new Vector3(-direction.Y, direction.X, direction.Z);
        Assert.InRange(Vector3.Distance(env.Radiance(direction), env.Radiance(rotated, 90)), 0, .001f);
        Assert.True(env.Radiance(rotated).X < env.Radiance(rotated, 90).X / 10);
    }
    [Fact(DisplayName = "RND-U004 | Geometry-guided denoising reduces flat-surface error and preserves material edges and fractional coverage")]
    [Trait("TestId", "RND-U004")]
    public void DenoisingReducesNoiseWithoutBleedingEdges()
    {
        const int w = 32, h = 12; var random = new Random(783);
        var color = new Vector3[w * h]; var expected = new Vector3[color.Length]; var coverage = Enumerable.Repeat(1f, color.Length).ToArray();
        var variance = Enumerable.Repeat(.015f, color.Length).ToArray(); var guides = new DenoiseGuide[color.Length];
        for (int i = 0; i < color.Length; i++)
        {
            int material = i % w < w / 2 ? 0 : 1; expected[i] = material == 0 ? new(.7f, .2f, .1f) : new(.1f, .2f, .7f);
            color[i] = expected[i] + new Vector3((float)(random.NextDouble() - .5) * .2f);
            guides[i] = new(Vector3.UnitZ, 10, material);
        }
        coverage[0] = .5f; var before = color.ToArray();
        var result = RenderDenoiser.Filter(color, coverage, variance, guides, w, h);
        float Error(Vector3[] image) => image.Zip(expected).Sum(pair => Vector3.DistanceSquared(pair.First, pair.Second));
        Assert.True(Error(result) < Error(color) * .5f); Assert.Equal(before, color); Assert.Equal(before[0], result[0]);
        Assert.True(result[w / 2 - 1].X > result[w / 2 - 1].Z * 3); Assert.True(result[w / 2].Z > result[w / 2].X * 3);
    }
    [Fact(DisplayName = "RND-U005 | Anisotropic reflection rotates its highlight and zero strength remains isotropic")]
    [Trait("TestId", "RND-U005")]
    public void DirectionalReflectionRotates()
    {
        var n = Vector3.UnitZ; var x = Vector3.Normalize(new Vector3(.5f, 0, 1)); var y = Vector3.Normalize(new Vector3(0, .5f, 1));
        Vector3 F(Vector3 l, float a, float angle) => SurfaceScattering.Opaque(n, n, l, Vector3.One, 1, .4f, a, angle, out _);
        Assert.InRange(Vector3.Distance(F(x, 0, 0), F(x, 0, 90)), 0, 1e-5f);
        Assert.True(F(x, .8f, 0).X > F(y, .8f, 0).X * 2);
        Assert.InRange(Vector3.Distance(F(x, .8f, 0), F(y, .8f, 90)), 0, 1e-4f);
        var view = Vector3.Normalize(new Vector3(.2f, -.1f, 1));
        var forward = SurfaceScattering.Opaque(n, view, x, Vector3.One, 1, .4f, .8f, 30, out _);
        var reverse = SurfaceScattering.Opaque(n, x, view, Vector3.One, 1, .4f, .8f, 30, out _);
        Assert.InRange(Vector3.Distance(forward, reverse), 0, 1e-5f);
    }
    [Fact(DisplayName = "RND-U006 | Dielectric Fresnel, Snell refraction and total internal reflection agree with analytic values")]
    [Trait("TestId", "RND-U006")]
    public void FresnelAndSnellLaw()
    {
        Assert.Equal(.04f, SurfaceScattering.Fresnel(1, 1, 1.5f), 5); Assert.Equal(1, SurfaceScattering.Fresnel(0, 1, 1.5f));
        Assert.Equal(0, SurfaceScattering.Fresnel(.2f, 1, 1)); Assert.Equal(1, SurfaceScattering.Fresnel(.5f, 1.5f, 1));
        var view = Vector3.Normalize(new Vector3(1, 0, 1));
        Assert.True(SurfaceScattering.Refract(view, Vector3.UnitZ, 1, 1.5f, out var ray));
        Assert.Equal(MathF.Sin(MathF.PI / 4) / 1.5f, Math.Abs(ray.X), 5); Assert.True(ray.Z < 0);
        Assert.False(SurfaceScattering.Refract(new(MathF.Sqrt(.75f), 0, .5f), Vector3.UnitZ, 1.5f, 1, out _));
    }
    [Fact(DisplayName = "RND-U007 | Smooth transmission through two interfaces cancels radiance scaling and rough samples remain finite")]
    [Trait("TestId", "RND-U007")]
    public void TransmissionSamplingIsConsistent()
    {
        var n = Vector3.UnitZ; var enter = SurfaceScattering.SampleDielectric(n, n, 1, 1.5f, .06f, .5f, .1f, .2f);
        var exit = SurfaceScattering.SampleDielectric(n, n, 1.5f, 1, .06f, .5f, .1f, .2f);
        Assert.True(enter.Transmitted && enter.Delta && exit.Transmitted); Assert.InRange(Vector3.Distance(enter.Weight * exit.Weight, Vector3.One), 0, 1e-6f);
        var random = new Random(37); var mean = Vector3.Zero;
        for (int i = 0; i < 10000; i++)
        {
            var sample = SurfaceScattering.SampleDielectric(n, n, 1, 1.5f, .3f, (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            Assert.True(float.IsFinite(sample.Weight.X)); Assert.True(sample.Weight.X >= 0);
            if (sample.Pdf > 0)
            {
                var f = SurfaceScattering.Dielectric(n, n, sample.Direction, 1, 1.5f, .3f, out float pdf);
                Assert.Equal(sample.Pdf, pdf, 5); Assert.InRange(Vector3.Distance(sample.Weight, f * (Math.Abs(sample.Direction.Z) / pdf)), 0, 1e-4f);
            }
            mean += sample.Weight / 10000;
        }
        Assert.InRange(mean.X, .35f, .6f);
    }
    [Fact(DisplayName = "RND-U008 | Absorption depends on physical path length and clear tint does not absorb")]
    [Trait("TestId", "RND-U008")]
    public void BeerAbsorptionUsesMillimeters()
    {
        var one = SurfaceScattering.Absorption(0xFF80FFFF, 10, 10); var two = SurfaceScattering.Absorption(0xFF80FFFF, 20, 10);
        Assert.Equal(MathF.Pow(128 / 255f, 2.2f), one.X, 5); Assert.InRange(Vector3.Distance(one * one, two), 0, 1e-6f);
        Assert.Equal(Vector3.One, SurfaceScattering.Absorption(0xFFFFFFFF, 500, 1)); Assert.Equal(Vector3.One, SurfaceScattering.Absorption(0xFF000000, 0, 10));
    }
    [Fact(DisplayName = "RND-U009 | Planar and cylindrical map placement honors offsets, axis and repeat/clamp")]
    [Trait("TestId", "RND-U009")]
    public void ProjectionControlsPlaceImageSamples()
    {
        var image = new SurfaceImage(2, 1, [255, 0, 0, 255, 0, 0, 255, 255]);
        var s = new SurfaceStyle { Projection = TextureProjection.Planar, TextureSize = 4 };
        Assert.Equal(Vector3.UnitX, SurfaceLayers.SampleImage(image, s, new(1, 1, 0), Vector3.UnitZ));
        Assert.Equal(Vector3.UnitX, SurfaceLayers.SampleImage(image, s with { TextureOffsetX = 4 }, new(1, 1, 0), Vector3.UnitZ));
        Assert.Equal(Vector3.UnitZ, SurfaceLayers.SampleImage(image, s with { TextureRepeat = false }, new(100, 1, 0), Vector3.UnitZ));
        s = s with { Projection = TextureProjection.Cylindrical };
        Assert.Equal(Vector3.UnitZ, SurfaceLayers.SampleImage(image, s, new(0, 4, 1), Vector3.UnitY));
        Assert.Equal(Vector3.UnitX, SurfaceLayers.SampleImage(image, s, new(0, -4, 1), -Vector3.UnitY));
        Assert.Equal(new Vector3(.5f, .75f, .25f), SurfaceLayers.MapPosition(s with { TextureAxis = GradientAxis.X }, new(1, 2, 3)));
    }
}
