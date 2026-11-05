using System.Numerics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Rendering;
using Prism.ViewModels;
using Prism.Views;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Rendering;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/rendering/README.md">Product rendering and appearance placement.</seealso>
[Trait("Feature", "Rendering")]
[Trait("Layer", "Headless")]
public class RenderingQualityTests
{
    static string WhiteHdr() => Convert.ToBase64String([.. Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 1\n"), 128, 128, 128, 129]);
    static RenderSettings Settings() => new() { Env = RenderEnv.Hdr, EnvironmentImage = WhiteHdr(), Ground = false, Bounces = 8 };
    [AvaloniaFact(DisplayName = "RND-H001 | Closed transparent solid absorption depends on actual thickness and preserves engineering data")]
    [Trait("TestId", "RND-H001")]
    public void SolidThicknessControlsAbsorption()
    {
        Vector3 Through(float thickness)
        {
            var s = new CadSession(); var body = CreateBoxBody(s, new(-10, -10, 0), 20, 20, thickness);
            s.Update(body.Id, o => o.WithMaterial(Materials.Find("6061")!)); var material = s.Find(body.Id)!.Material;
            s.Update(body.Id, o => o.WithLook(o.Look with { Metal = 0, Gloss = 1, Surface = new() { Transmission = 1, Ior = 1, AbsorptionColor = 0xFF80FFFF, AbsorptionDistance = 2 } }));
            Assert.Equal(material, s.Find(body.Id)!.Material);
            var tracer = new PathTracer(Settings()); Assert.True(tracer.LoadScene(s));
            return tracer.ProbeRadiance(new(0, 0, 20), -Vector3.UnitZ, 8);
        }
        var thin = Through(2); var thick = Through(4);
        Assert.InRange(thin.X, .215f, .225f); Assert.InRange(thick.X, .045f, .055f);
        Assert.InRange(Math.Abs(thick.X - thin.X * thin.X), 0, .001f); Assert.Equal(1, thick.Y, 5); Assert.Equal(1, thick.Z, 5);
    }
    [AvaloniaFact(DisplayName = "RND-H002 | Transparent glass reveals scene geometry rather than fading an opaque color")]
    [Trait("TestId", "RND-H002")]
    public void GlassRevealsRedBodyBehindIt()
    {
        var s = new CadSession(); var plate = CreateBoxBody(s, new(-4, -4, 4), 8, 8, 2);
        var red = CreateBoxBody(s, new(-20, -20, 0), 40, 40, 1);
        s.Update(red.Id, o => o.WithColor(0xFFFF0808).WithLook(new(Metal: 0, Gloss: .1f)));
        s.Update(plate.Id, o => o.WithColor(0xFFAAAAAA).WithLook(new(Metal: 0, Gloss: 1)));
        Vector3 Probe()
        {
            var tracer = new PathTracer(Settings()); Assert.True(tracer.LoadScene(s)); return tracer.ProbeRadiance(new(0, 0, 20), -Vector3.UnitZ, 2048);
        }
        var opaque = Probe(); Assert.InRange(Math.Abs(opaque.X - opaque.Y), 0, .02f);
        s.Update(plate.Id, o => o.WithLook(o.Look with { Surface = new() { Transmission = 1, Ior = 1.5f } }));
        var glass = Probe(); Assert.True(glass.X > glass.Y * 2, $"Expected red geometry through glass, got {glass}.");
    }
    [AvaloniaFact(DisplayName = "RND-H003 | Imported HDR intensity scales rendered linear radiance independently of exposure")]
    [Trait("TestId", "RND-H003")]
    public void EnvironmentLightsTheObject()
    {
        var s = new CadSession(); var body = CreateBoxBody(s, new(-10, -10, 0), 20, 20, 2);
        s.Update(body.Id, o => o.WithColor(0xFFFFFFFF).WithLook(new(Metal: 0, Gloss: .1f)));
        Vector3 Probe(float intensity)
        {
            var settings = Settings(); settings.EnvironmentIntensity = intensity; settings.Exposure = 10;
            var tracer = new PathTracer(settings); Assert.True(tracer.LoadScene(s)); return tracer.ProbeRadiance(new(0, 0, 20), -Vector3.UnitZ, 1024);
        }
        var one = Probe(1); var two = Probe(2);
        Assert.InRange(one.X, .5f, 1.1f); Assert.InRange(Vector3.Distance(one * 2, two), 0, 1e-5f); Assert.Equal(Vector3.Zero, Probe(0));
    }
    [AvaloniaFact(DisplayName = "RND-H004 | HDR, denoising, glass, anisotropy and mapping settings survive save and reopen")]
    [Trait("TestId", "RND-H004")]
    public void RenderingSettingsRoundtrip()
    {
        var vm = new MainViewModel(); var s = vm.Session; var body = CreateBoxBody(s, new(0, 0, 0), 10, 10, 10);
        s.SelectItems([SelItem.Of(body)]); vm.ApplyMaterial(Materials.Find("304")!); vm.SurfaceTransmission = .7; vm.SurfaceIor = 1.6; vm.SurfaceAnisotropy = .8;
        vm.AnisotropyAngle = 45; vm.TextureProjectionMode = 2; vm.TextureProjectionAxis = 1; vm.TextureOffsetX = 3; vm.TextureAspect = 2; vm.TextureRepeat = false;
        var expected = s.Find(body.Id)!.Look;
        s.RenderSetup = new() { Env = RenderEnv.Hdr, EnvironmentImage = WhiteHdr(), EnvironmentRotation = 90, EnvironmentIntensity = 2, Denoise = true };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".prism");
        try
        {
            s.Save(path); var restored = new CadSession(); restored.Open(path);
            Assert.Equal(expected, Assert.Single(restored.Objects).Look); Assert.Equal(s.RenderSetup, restored.RenderSetup);
        }
        finally { File.Delete(path); }
    }
    [AvaloniaFact(DisplayName = "RND-H005 | Final denoising preserves transparent alpha and malformed environments report an error")]
    [Trait("TestId", "RND-H005")]
    public void DenoisingPreservesAlpha()
    {
        var s = new CadSession(); CreateBoxBody(s, new(0, 0, 0), 10, 10, 10); s.Camera.Width = s.Camera.Height = 32; s.Camera.Fit(new(0, 0, 0), new(10, 10, 10));
        byte[] Render(bool denoise)
        {
            var settings = Settings(); settings.Width = settings.Height = 32; settings.Samples = 8; settings.Background = RenderBg.Transparent; settings.Denoise = denoise;
            var tracer = new PathTracer(settings); Assert.True(tracer.LoadScene(s)); byte[] image = [];
            tracer.Render(s.Camera, (p, _, _, _, _) => image = p.ToArray(), CancellationToken.None); return image;
        }
        var raw = Render(false); var clean = Render(true);
        Assert.Equal(Enumerable.Range(0, 1024).Select(i => raw[i * 4 + 3]), Enumerable.Range(0, 1024).Select(i => clean[i * 4 + 3]));
        var missing = Settings(); missing.EnvironmentImage = null;
        Assert.Contains("HDR", Assert.Throws<ArgumentException>(() => new PathTracer(missing).LoadScene(s)).Message);
    }
    [AvaloniaFact(DisplayName = "RND-H006 | Glass preset is grouped and undoable, advanced controls start collapsed and gradient dependencies refresh")]
    [Trait("TestId", "RND-H006")]
    public void AdvancedAppearanceRemainsCompactAndUndoable()
    {
        var vm = new MainViewModel(); var s = vm.Session; var a = CreateBoxBody(s, new(0, 0, 0), 10, 10, 10); var b = CreateBoxBody(s, new(20, 0, 0), 10, 10, 10);
        s.SelectItems([SelItem.Of(a), SelItem.Of(b)]); vm.ApplyMaterial(Materials.Find("6061")!); var previous = s.Current;
        vm.ApplyTransparentPreset(false);
        Assert.All(s.Objects, o => { Assert.Equal(1, o.Look.Surface!.Transmission); Assert.Equal("6061", o.Material!.Id); });
        s.Undo(); Assert.Same(previous, s.Current);
        var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName); vm.GradientMode = 4;
        Assert.Contains(nameof(vm.HasColorGradient), changed); Assert.True(vm.HasFreeformGradient);
        var unchanged = s.Current; vm.SurfaceIor = vm.SurfaceIor; vm.TextureProjectionMode = vm.TextureProjectionMode; Assert.Same(unchanged, s.Current);
        var panel = new MaterialsPanel { DataContext = vm }; Assert.False(panel.FindControl<Expander>("TransmissionOptions")!.IsExpanded);
    }
}
