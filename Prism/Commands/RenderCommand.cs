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
    readonly ChoiceParam _size, _env, _bg;
    readonly IntParam _samples, _bounces;
    readonly BoolParam _ground;
    readonly NumberParam _exposure;
    readonly TextParam _save;

    static readonly (string name, int w, int h)[] Sizes =
    [("Draft  960 × 540", 960, 540), ("HD  1280 × 720", 1280, 720), ("Full HD  1920 × 1080", 1920, 1080), ("4K  3840 × 2160", 3840, 2160), ("Square  1500 × 1500", 1500, 1500)];

    public RenderCommand()
    {
        _size = Choice("Picture size", 1, [.. Array.ConvertAll(Sizes, s => s.name)]);
        _samples = Add(new IntParam { Label = "Quality (samples per pixel)", Min = 1, Max = 4096, Value = 64 });
        _bounces = Add(new IntParam { Label = "Light bounces", Min = 1, Max = 12, Value = 5 });
        _env = Choice("Lighting", 0, "Studio (soft white boxes)", "Sky and sun", "Dark studio");
        _bg = Choice("Background", 0, "Environment", "White", "Transparent");
        _ground = Check("Ground plane with shadows", true);
        _exposure = Num("Exposure", 1, 0.1, 10, 0.1, "×");
        _save = Text("Save to (blank = Documents\\Prism\\Renders)", "", "full path of a .png file");
        Add(new ActionParam { Label = "Start render", Run = Start });
        Add(new ActionParam { Label = "Stop", Run = () => S.StopRender() });
        Add(new ActionParam { Label = "Save picture", Run = () => S.SaveRender(string.IsNullOrWhiteSpace(_save.Value) ? null : _save.Value) });
    }

    public override string Title => "Render";
    public override bool SupportsPreview => false;
    public override string Icon => "render";
    public override string OkText => "Done";
    public override string Hint => "Orbit to the view you want, choose size, quality and lighting, then press Start render. The picture appears over the viewport and sharpens as it goes; Stop whenever it looks good enough. Higher quality means less grain and takes longer.";

    void Start()
    {
        var (_, w, h) = Sizes[Math.Clamp(_size.Index, 0, Sizes.Length - 1)];
        var s = new RenderSettings
        {
            Width = w, Height = h, Samples = _samples.Value, Bounces = _bounces.Value, Ground = _ground.Value, Exposure = _exposure.Value,
            Env = (RenderEnv)_env.Index, Background = (RenderBg)_bg.Index,
        };
        try { S.StartRender(s); S.Say($"Rendering {w}×{h}…"); } catch (Exception e) { S.Say(e.Message, true); }
    }

    protected override CommandOutput? Build() => new CommandOutput { NoOp = true };
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
        _env = Choice("Lighting", 0, "Studio (soft white boxes)", "Sky and sun", "Dark studio");
        _bg = Choice("Background", 0, "Environment", "White", "Transparent (PNG only)");
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
            Settings = new RenderSettings { Width = w, Height = h, Samples = _samples.Value, Bounces = 4, Ground = _ground.Value, Exposure = _exposure.Value, Env = (RenderEnv)_env.Index, Background = (RenderBg)_bg.Index },
        };
        S.Say("Rendering animation…");
        _ = S.RenderAnimationAsync(opts);
    }

    protected override CommandOutput? Build() => new CommandOutput { NoOp = true };
}
