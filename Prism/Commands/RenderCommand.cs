using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Rendering;

namespace Prism.Commands;

/// <summary>Make a realistic picture of the scene (a built-in path tracer: soft shadows, reflections, studio or sky light).</summary>
public sealed class RenderCommand : Command
{
    readonly ChoiceParam _size, _env, _bg, _fit;
    readonly IntParam _samples, _bounces;
    readonly BoolParam _ground, _denoise;
    readonly NumberParam _exposure, _offsetX, _offsetY, _envRotation, _envIntensity;
    string? _backgroundImage, _environmentImage;
    readonly TextParam _save;
    public static readonly string[] Backgrounds = ["Environment (studio or sky)", "White", "Transparent", "Green screen (chroma key)", "Black", "Studio gradient", "Imported image"];
    public static readonly string[] Lightings = ["Studio (soft white boxes)", "Sky and sun", "Dark studio", "Only my lights", "HDR environment"];

    static readonly (string name, int w, int h)[] Sizes =
    [("Draft  960 × 540", 960, 540), ("HD  1280 × 720", 1280, 720), ("Full HD  1920 × 1080", 1920, 1080), ("4K  3840 × 2160", 3840, 2160), ("Square  1500 × 1500", 1500, 1500)];

    public RenderCommand()
    {
        _size = Choice("Picture size", 1, [.. Array.ConvertAll(Sizes, s => s.name)]);
        _samples = Add(new IntParam { Label = "Quality (samples per pixel)", Min = 1, Max = 4096, Value = 64 });
        _bounces = Add(new IntParam { Label = "Light bounces", Min = 1, Max = 12, Value = 5 });
        _env = Choice("Lighting", 0, Lightings);
        _bg = Choice("Background", 0, Backgrounds);
        _denoise = Check("Denoise final image", false);
        Add(new ActionParam { Label = "Import HDR environment…", IsAdvanced = true, Run = async () =>
        {
            if (S.PickHdrEnvironment == null) { S.Say("HDR browsing is unavailable in this host.", true); return; }
            var path = await S.PickHdrEnvironment(); if (path == null) return;
            try { _environmentImage = HdrEnvironment.Import(path); _env.Index = (int)RenderEnv.Hdr; OnChanged(); }
            catch (Exception e) { S.Say("Could not import HDR environment: " + e.Message, true); }
        } });
        Add(new ActionParam { Label = "Remove HDR environment", IsAdvanced = true, Run = () => { _environmentImage = null; if (_env.Index == (int)RenderEnv.Hdr) _env.Index = 0; OnChanged(); } });
        _envRotation = Add(new NumberParam { Label = "HDR rotation", Min = -360, Max = 360, Step = 5, Unit = "°", IsAdvanced = true });
        _envIntensity = Add(new NumberParam { Label = "HDR intensity", Value = 1, Min = 0, Max = 100, Step = .1, Unit = "×", IsAdvanced = true });
        Add(new ActionParam { Label = "Import background image…", Run = async () =>
        {
            if (S.PickRenderImage == null) { S.Say("Image browsing is unavailable in this host.", true); return; }
            var path = await S.PickRenderImage(); if (path == null) return;
            try { _backgroundImage = SurfaceImage.Import(path); _bg.Index = (int)RenderBg.Image; OnChanged(); }
            catch (Exception e) { S.Say("Could not import background: " + e.Message, true); }
        } });
        Add(new ActionParam { Label = "Remove background image", Run = () => { _backgroundImage = null; if (_bg.Index == (int)RenderBg.Image) _bg.Index = 0; OnChanged(); } });
        _fit = Add(new ChoiceParam { Label = "Image placement", Options = ["Fill (crop)", "Fit (white border)", "Stretch"], IsAdvanced = true });
        _offsetX = Add(new NumberParam { Label = "Image horizontal offset", Min = -1, Max = 1, Step = .05, Unit = "× width", IsAdvanced = true });
        _offsetY = Add(new NumberParam { Label = "Image vertical offset", Min = -1, Max = 1, Step = .05, Unit = "× height", IsAdvanced = true });
        _ground = Check("Ground plane with shadows", true);
        _exposure = Num("Exposure", 1, 0.1, 10, 0.1, "×");
        _save = Text("Save to (blank = Documents\\Prism\\Renders)", "", "full path of a .png file");
        Add(new ActionParam { Label = "Edit lights (sun, lamps)…", Run = () => S.StartCommand(() => new LightsCommand()) });
        Add(new ActionParam { Label = "Start render", Run = Start });
        Add(new ActionParam { Label = "Stop", Run = () => S.StopRender() });
        Add(new ActionParam { Label = "Save picture", Run = () => S.SaveRender(string.IsNullOrWhiteSpace(_save.Value) ? null : _save.Value) });
    }

    public override string Title => "Render";
    public override bool SupportsPreview => false;
    public override string Icon => "render";
    public override string OkText => "Done";
    public override string Hint => "Orbit to the view you want, choose size, quality, lighting and background (white, black, green screen, studio gradient, imported image or transparent), add your own sun or lamps, then press Start render. The picture appears over the viewport and sharpens as it goes; Stop whenever it looks good enough. Higher quality means less grain and takes longer.";

    protected override void OnBegin()
    {
        var setup = S.RenderSetup;
        _env.SetSilently((int)setup.Env); _bg.SetSilently((int)setup.Background); _ground.Value = setup.Ground; _exposure.Value = setup.Exposure;
        _environmentImage = setup.EnvironmentImage; _envRotation.Value = setup.EnvironmentRotation; _envIntensity.Value = setup.EnvironmentIntensity; _denoise.Value = setup.Denoise;
        _backgroundImage = setup.BackgroundImage; _fit.SetSilently((int)setup.ImageFit); _offsetX.Value = setup.ImageOffsetX; _offsetY.Value = setup.ImageOffsetY;
    }

    protected override CommandOutput? Build()
    {
        var setup = new RenderSetup { Env = (RenderEnv)_env.Index, Background = (RenderBg)_bg.Index, Ground = _ground.Value, Exposure = _exposure.Value, BackgroundImage = _backgroundImage, ImageFit = (BackgroundFit)_fit.Index, ImageOffsetX = _offsetX.Value, ImageOffsetY = _offsetY.Value, EnvironmentImage = _environmentImage, EnvironmentRotation = (float)_envRotation.Value, EnvironmentIntensity = (float)_envIntensity.Value, Denoise = _denoise.Value };
        if (!setup.Equals(S.RenderSetup)) { S.RenderSetup = setup; S.Dirty = true; }
        _fit.IsVisible = _offsetX.IsVisible = _offsetY.IsVisible = _bg.Index == (int)RenderBg.Image;
        _envRotation.IsVisible = _envIntensity.IsVisible = _env.Index == (int)RenderEnv.Hdr;
        Results.Clear();
        Results.Add(new ResultLine("HDR environment", _environmentImage == null ? "none imported" : "embedded · lights and reflections"));
        Results.Add(new ResultLine("Backdrop image", _backgroundImage == null ? "none imported" : "embedded · does not change lighting"));
        int on = S.Lights.Count(l => l.Enabled);
        Results.Add(new ResultLine("Your lights", S.Lights.Count == 0 ? "none: add a sun or lamp from the ribbon" : $"{on} of {S.Lights.Count} on"));
        return new CommandOutput { NoOp = true };
    }

    void Start()
    {
        var (_, w, h) = Sizes[Math.Clamp(_size.Index, 0, Sizes.Length - 1)];
        var s = new RenderSettings
        {
            Width = w, Height = h, Samples = _samples.Value, Bounces = _bounces.Value, Ground = _ground.Value, Exposure = _exposure.Value,
            Env = (RenderEnv)_env.Index, Background = (RenderBg)_bg.Index, BackgroundImage = _backgroundImage, ImageFit = (BackgroundFit)_fit.Index, ImageOffsetX = _offsetX.Value, ImageOffsetY = _offsetY.Value, EnvironmentImage = _environmentImage, EnvironmentRotation = (float)_envRotation.Value, EnvironmentIntensity = (float)_envIntensity.Value, Denoise = _denoise.Value,
        };
        try { S.StartRender(s); S.Say($"Rendering {w}×{h}…"); } catch (Exception e) { S.Say(e.Message, true); }
    }

}

/// <summary>Render the animation (and/or a turntable) as a folder of pictures, a GIF and, when ffmpeg is installed, an MP4.</summary>
public sealed class RenderAnimationCommand : Command
{
    ChoiceParam _anim = null!, _camera = null!, _size = null!, _env = null!, _bg = null!, _fps = null!;
    NumberParam _turns = null!, _length = null!, _exposure = null!;
    IntParam _samples = null!;
    BoolParam _ground = null!, _gif = null!, _mp4 = null!;
    TextParam _folder = null!;
    List<SceneObject> _animations = [];

    static readonly (string name, int w, int h)[] Sizes = [("Draft  640 × 360", 640, 360), ("HD  1280 × 720", 1280, 720), ("Full HD  1920 × 1080", 1920, 1080), ("Square  1080 × 1080", 1080, 1080)];
    static readonly int[] FpsValues = [12, 24, 30, 60];

    public override string Title => "Render Animation";
    public override bool SupportsPreview => false;
    public override string Icon => "render";
    public override string OkText => "Done";
    public override string Hint => "Renders every frame of the chosen animation (joint motion and exploded view) from the current view, optionally while the camera turns around the model. Frames are saved as PNG; a GIF is made too, and an MP4 if ffmpeg is installed. Use a low quality for a quick test, then raise it.";

    protected override void OnBegin()
    {
        _animations = S.Objects.Where(o => o.Asm is AnimInfo).ToList();
        _anim = Choice("Animation", _animations.Count > 0 ? 1 : 0, ["None (camera turntable only)", .. _animations.Select(a => a.Name)]);
        _camera = Choice("Camera", _animations.Count > 0 ? 0 : 1, "Fixed (the current view)", "Turntable (turns around the model)");
        _turns = Num("Turntable turns", 1, 0.25, 10, 0.25, "×");
        _length = Num("Length (when there is no animation)", 5, 1, 120, 1, "s");
        _fps = Choice("Frames per second", 1, "12", "24", "30", "60");
        _size = Choice("Picture size", 0, [.. Array.ConvertAll(Sizes, s => s.name)]);
        _samples = Add(new IntParam { Label = "Quality (samples per pixel)", Min = 1, Max = 1024, Value = 12 });
        _env = Choice("Lighting", (int)S.RenderSetup.Env, RenderCommand.Lightings);
        _bg = Choice("Background", (int)S.RenderSetup.Background, RenderCommand.Backgrounds);
        _ground = Check("Ground plane with shadows", true);
        _exposure = Num("Exposure", 1, 0.1, 10, 0.1, "×");
        _gif = Check("Also make a GIF", true);
        _mp4 = Check("Also make an MP4 (needs ffmpeg)", true);
        _folder = Text("Save to folder", Path.Combine(CadSession.RenderFolder, $"animation_{DateTime.Now:yyyyMMdd_HHmmss}"), "folder for the frames");
        Add(new ActionParam { Label = "Render animation", Run = Start });
        Add(new ActionParam { Label = "Stop", Run = () => S.StopRender() });
    }

    void Start()
    {
        var anim = _anim.Index > 0 ? _animations[_anim.Index - 1] : null;
        bool turntable = _camera.Index == 1;
        if (anim == null && !turntable) { S.Say("Pick an animation, or choose the turntable camera.", true); return; }
        var (_, w, h) = Sizes[Math.Clamp(_size.Index, 0, Sizes.Length - 1)];
        var opts = new CadSession.AnimRenderOptions
        {
            Animation = anim, Turntable = turntable, Turns = _turns.Value, Duration = _length.Value, Fps = FpsValues[Math.Clamp(_fps.Index, 0, FpsValues.Length - 1)],
            Gif = _gif.Value, Mp4 = _mp4.Value, Folder = _folder.Value,
            Settings = new RenderSettings { Width = w, Height = h, Samples = _samples.Value, Bounces = 4, Ground = _ground.Value, Exposure = _exposure.Value, Env = (RenderEnv)_env.Index, Background = (RenderBg)_bg.Index, BackgroundImage = S.RenderSetup.BackgroundImage, ImageFit = S.RenderSetup.ImageFit, ImageOffsetX = S.RenderSetup.ImageOffsetX, ImageOffsetY = S.RenderSetup.ImageOffsetY, EnvironmentImage = S.RenderSetup.EnvironmentImage, EnvironmentRotation = S.RenderSetup.EnvironmentRotation, EnvironmentIntensity = S.RenderSetup.EnvironmentIntensity, Denoise = S.RenderSetup.Denoise },
        };
        S.Say("Rendering animation…");
        _ = S.RenderAnimationAsync(opts);
    }

    protected override CommandOutput? Build() => new CommandOutput { NoOp = true };
}
