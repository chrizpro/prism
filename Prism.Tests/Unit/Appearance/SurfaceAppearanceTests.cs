using System.Numerics;
using Prism.Core;
using Prism.Rendering;
using Xunit;

namespace Prism.UnitTests.Appearance;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/appearance-studio/README.md">Appearance studio specification.</seealso>
[Trait("Feature", "Appearance")]
[Trait("Layer", "Unit")]
public class SurfaceAppearanceTests
{
    [Theory(DisplayName = "APP-U001 | Exact color entry accepts equivalent HEX, RGB, HSL and HSV colors")]
    [Trait("TestId", "APP-U001")]
    [InlineData("#FF8000")]
    [InlineData("FF8000")]
    [InlineData("rgb(255,128,0)")]
    [InlineData("hsl(30.117647,100%,50%)")]
    [InlineData("hsv(30.117647,100%,100%)")]
    public void ColorFormatsAgree(string code)
    {
        Assert.True(ColorCode.TryParse(code, out uint c)); Assert.Equal(0xFFFF8000u, c);
        Assert.True(ColorCode.TryParse("#F80", out uint shortHex)); Assert.Equal(0xFFFF8800u, shortHex);
    }
    [Theory(DisplayName = "APP-U002 | Invalid and out-of-range colors are rejected")]
    [Trait("TestId", "APP-U002")]
    [InlineData("rgb(256,0,0)")][InlineData("hsv(20,101,0)")][InlineData("#12345678")][InlineData("hsl(NaN,0,0)")][InlineData("garbage")]
    public void InvalidColorsAreRejected(string code) => Assert.False(ColorCode.TryParse(code, out _));

    [Fact(DisplayName = "APP-U003 | Linear and freeform gradients reach their specified colors in model coordinates")]
    [Trait("TestId", "APP-U003")]
    public void GradientEndpointsAndCorners()
    {
        var s = new SurfaceStyle { Gradient = ColorGradient.Linear, GradientSize = 20, GradientOffset = 5, Color2 = 0xFF0000FF };
        Assert.Equal(Vector3.UnitX, SurfaceLayers.GradientColor(s, new(0, 0, 5), Vector3.UnitX));
        Assert.Equal(Vector3.UnitZ, SurfaceLayers.GradientColor(s, new(0, 0, 25), Vector3.UnitX));
        Assert.Equal(new Vector3(.5f, 0, .5f), SurfaceLayers.GradientColor(s, new(0, 0, 15), Vector3.UnitX));
        s = s with { Gradient = ColorGradient.Freeform, GradientOffset = 0, Color3 = 0xFF00FF00, Color4 = 0xFFFFFFFF };
        Assert.Equal(Vector3.UnitX, SurfaceLayers.GradientColor(s, Vector3.Zero, Vector3.UnitX));
        Assert.Equal(Vector3.UnitZ, SurfaceLayers.GradientColor(s, new(20, 0, 0), Vector3.UnitX));
        Assert.Equal(Vector3.UnitY, SurfaceLayers.GradientColor(s, new(0, 20, 0), Vector3.UnitX));
        Assert.Equal(Vector3.One, SurfaceLayers.GradientColor(s, new(20, 20, 0), Vector3.UnitX));
    }
    [Fact(DisplayName = "APP-U004 | Weathering coverage is deterministic, monotonic and can fade above the base")]
    [Trait("TestId", "APP-U004")]
    public void WeatheringCoverageAndPlacement()
    {
        var s = new SurfaceStyle { Weathering = WeatheringKind.Rust, PatchSize = 4, WeatherHeight = 20, Seed = 17 };
        var samples = Enumerable.Range(0, 200).Select(i => new Vector3(i * .71f, i % 13 * .8f, i % 7)).ToArray();
        var low = samples.Select(p => SurfaceLayers.WeatherMask(s with { Coverage = .2f }, p)).ToArray();
        var high = samples.Select(p => SurfaceLayers.WeatherMask(s with { Coverage = .8f }, p)).ToArray();
        Assert.True(low.Zip(high).All(pair => pair.Second >= pair.First));
        Assert.InRange(low.Average(), 0, .3); Assert.InRange(high.Average(), .7, 1);
        Assert.True(samples.Select(p => SurfaceLayers.Noise(p)).Distinct().Count() > 100);
        Assert.All(samples, p => Assert.InRange(SurfaceLayers.Noise(p), 0, 1));
        Assert.Equal(high, samples.Select(p => SurfaceLayers.WeatherMask(s with { Coverage = .8f }, p)).ToArray());
        Assert.Equal(0, SurfaceLayers.WeatherMask(s with { BottomOnly = true, Coverage = 1 }, new(1, 2, 25)));
        Assert.Equal(0, SurfaceLayers.WeatherMask(s with { Coverage = 0 }, Vector3.Zero));
    }
    [Fact(DisplayName = "APP-U005 | Rust changes metal response and roughness while disabled layers preserve the base")]
    [Trait("TestId", "APP-U005")]
    public void SurfaceResponseIsMoreThanColor()
    {
        var normal = Vector3.UnitZ; var color = new Vector3(.7f);
        var plain = new SurfaceLayers(null).Evaluate(new(1, 2, 3), normal, color, 1, .2f);
        Assert.Equal(color, plain.Color); Assert.Equal(normal, plain.Normal); Assert.Equal(1, plain.Metal); Assert.Equal(.2f, plain.Roughness);
        var rust = new SurfaceLayers(new() { Weathering = WeatheringKind.Rust, Coverage = 1 }).Evaluate(new(1, 2, 3), normal, color, 1, .2f);
        Assert.Equal(0, rust.Metal); Assert.Equal(.85f, rust.Roughness); Assert.NotEqual(color, rust.Color);
        var bumped = new SurfaceLayers(new() { Bump = 1 }).Evaluate(new(.41f, .92f, .53f), normal, color, 0, .5f);
        Assert.InRange(bumped.Normal.Length(), .999f, 1.001f); Assert.True(bumped.Normal.Z > 0); Assert.NotEqual(normal, bumped.Normal);
    }
    [Fact(DisplayName = "APP-U006 | Backplate fill, fit and stretch preserve placement and white borders")]
    [Trait("TestId", "APP-U006")]
    public void BackplatePlacement()
    {
        var red = new SurfaceImage(2, 1, [255, 0, 0, 255, 255, 0, 0, 255]);
        Assert.Equal(Vector3.One, BackgroundPlacement.Sample(red, 2, 0, 4, 4, BackgroundFit.Fit));
        Assert.Equal(Vector3.UnitX, BackgroundPlacement.Sample(red, 2, 1, 4, 4, BackgroundFit.Fit));
        Assert.Equal(Vector3.UnitX, BackgroundPlacement.Sample(red, 2, 0, 4, 4, BackgroundFit.Fill));
        Assert.Equal(Vector3.UnitX, BackgroundPlacement.Sample(red, 2, 0, 4, 4, BackgroundFit.Stretch));
        Assert.Equal(Vector3.One, BackgroundPlacement.Sample(red, 0, 0, 4, 4, BackgroundFit.Stretch, 1, 0));
        Assert.Equal(red.Sample(.1f, .3f), red.Sample(1.1f, -1.7f));
    }

    [Fact(DisplayName = "APP-U008 | Decal sampling keeps alpha, blends over the base and respects repeat clamping")]
    [Trait("TestId", "APP-U008")]
    public void DecalSampling()
    {
        var img = new SurfaceImage(2, 1, [255, 0, 0, 255, 0, 255, 0, 128]);
        var s = new SurfaceStyle { TextureSize = 10, TextureRepeat = false };
        var d = SurfaceLayers.SampleDecal(img, s, new Vector3(2.5f, 2.5f, 0));
        Assert.Equal(1f, d.W, 2); Assert.True(d.X > 0 && d.Y == 0);           // on the red texel: premultiplied rgb = red * alpha
        Assert.Equal(Vector4.Zero, SurfaceLayers.SampleDecal(img, s, new Vector3(15, 0, 0)));   // outside the image, repeat off: see-through
        var rep = SurfaceLayers.SampleDecal(img, s with { TextureRepeat = true }, new Vector3(15, 0, 0));
        Assert.True(rep.W > 0);                                              // repeat tiles the artwork again
        var half = img.Sample4(.75f, .5f);
        Assert.InRange(half.W, .5f, .8f);                                    // alpha interpolates between the opaque and half-alpha texels
    }

    [Theory(DisplayName = "APP-U007 | Clear-coat reflection is reciprocal and does not create energy in representative white-furnace integrations")]
    [Trait("TestId", "APP-U007")]
    [InlineData(0f, 0f)][InlineData(1f, 0f)][InlineData(0f, 60f)][InlineData(1f, 60f)]
    public void CoatRespectsEnergyAndReciprocity(float metal, float viewAngle)
    {
        var method = typeof(PathTracer).GetMethod("LayeredBsdf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var n = Vector3.UnitZ;
        var view = new Vector3(MathF.Sin(viewAngle * MathF.PI / 180), 0, MathF.Cos(viewAngle * MathF.PI / 180));
        Vector3 Evaluate(Vector3 v, Vector3 l)
        {
            object[] args = [n, v, l, Vector3.One, metal, .4f, 1f, .5f, 0f];
            var f = (Vector3)method.Invoke(null, args)!;
            Assert.True(float.IsFinite(f.X)); Assert.InRange((float)args[8], 0, float.MaxValue); return f;
        }
        var light = Vector3.Normalize(new Vector3(-.2f, .3f, .8f));
        Assert.InRange(Vector3.Distance(Evaluate(view, light), Evaluate(light, view)), 0, 1e-5f);
        var energy = Vector3.Zero; const int samples = 16384;
        for (int i = 0; i < samples; i++)
        {
            float z = (i + .5f) / samples, phi = i * 2.39996323f, r = MathF.Sqrt(1 - z * z);
            var direction = new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
            energy += Evaluate(view, direction) * (z * 2 * MathF.PI / samples);
        }
        Assert.InRange(energy.X, .1f, 1.03f);
    }
}
