using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prism.Rendering;

namespace Prism.Core;

/// <summary>One render in progress or finished: the picture so far and its state.</summary>
public sealed class RenderJob
{
    public byte[] Pixels { get; set; } = [];
    public int Width { get; init; }
    public int Height { get; init; }
    public bool Running { get; set; }
    public bool Finished { get; set; }
    public int Samples { get; set; }
    public int TotalSamples { get; init; }
    public double Seconds { get; set; }
    public string? SavedTo { get; set; }
    public string? Error { get; set; }
    public CancellationTokenSource Cts { get; } = new();
    public string? StatusOverride { get; set; }
    public string Status => Error != null ? $"Render failed: {Error}" : StatusOverride ?? (Running ? $"Rendering… {Samples}/{TotalSamples} samples  ({Seconds:0}s)" : Finished ? $"Done: {Width}×{Height}, {TotalSamples} samples in {Seconds:0.#}s" : $"Stopped at {Samples}/{TotalSamples} samples");
}

public sealed partial class CadSession
{
    public bool AppearancePreview { get; set; }
    public Func<Task<string?>>? PickHdrEnvironment { get; set; }
    public Func<Task<string?>>? PickRenderImage { get; set; }
    public RenderJob? Render { get; private set; }
    public event Action? RenderChanged;

    public static string RenderFolder
    {
        get { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "Renders"); Directory.CreateDirectory(d); return d; }
    }

    /// <summary>Start rendering the visible scene from the main view. Returns the job; progress arrives through RenderChanged.</summary>
    public RenderJob StartRender(RenderSettings settings)
    {
        StopRender();
        settings.Lights ??= [.. Lights.Select(l => l.Clone())];
        var tracer = new PathTracer(settings);
        if (!tracer.LoadScene(this)) throw new InvalidOperationException("There is nothing visible to render yet.");
        var cam = Cameras[0];
        var job = new RenderJob { Width = settings.Width, Height = settings.Height, TotalSamples = settings.Samples, Running = true, Pixels = new byte[settings.Width * settings.Height * 4] };
        Render = job; RenderChanged?.Invoke();
        // the camera the render sees is a snapshot: orbiting afterwards must not change a picture that is being made
        var snap = new Camera { Target = cam.Target, Distance = cam.Distance, Yaw = cam.Yaw, Pitch = cam.Pitch, Roll = cam.Roll, FovY = cam.FovY, Orthographic = cam.Orthographic, Width = settings.Width, Height = settings.Height };
        var started = DateTime.UtcNow;
        _ = Task.Run(() =>
        {
            try
            {
                tracer.Render(snap, (pix, w, h, samples, done) =>
                {
                    if (job.Cts.IsCancellationRequested) return;
                    job.Pixels = (byte[])pix.Clone(); job.Samples = samples; job.Seconds = (DateTime.UtcNow - started).TotalSeconds;
                    job.Finished = done; job.Running = !done; RenderChanged?.Invoke();
                }, job.Cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { job.Error = e.Message; if (ReferenceEquals(Render, job)) Say(job.Status, true); }
            finally { job.Running = false; job.Seconds = (DateTime.UtcNow - started).TotalSeconds; RenderChanged?.Invoke(); }
        });
        // sample counts come from the tracer's passes; mirror them for the status line
        return job;
    }

    /// <summary>Settings for rendering an animation (and/or a turntable) to a folder of pictures, a GIF and, if ffmpeg is installed, an MP4.</summary>
    public sealed class AnimRenderOptions
    {
        public SceneObject? Animation;
        public double Fps = 24, Duration = 5, Turns = 1;
        public bool Turntable, Gif = true, Mp4 = true;
        public string Folder = "";
        public RenderSettings Settings = new();
    }

    /// <summary>Render every frame: pose the assembly (and turn the camera), trace it, save it. Runs frame by frame so the app stays usable.</summary>
    public async System.Threading.Tasks.Task RenderAnimationAsync(AnimRenderOptions o)
    {
        StopRender();
        var anim = o.Animation?.Asm as Assembly.AnimInfo;
        double duration = anim?.Duration ?? o.Duration;
        int frames = Math.Max(2, (int)Math.Round(duration * o.Fps) + (o.Turntable ? 0 : 1));
        System.IO.Directory.CreateDirectory(o.Folder);
        var settings = o.Settings;
        settings.Lights ??= [.. Lights.Select(l => l.Clone())];
        var job = new RenderJob { Width = settings.Width, Height = settings.Height, TotalSamples = settings.Samples, Running = true, Pixels = new byte[settings.Width * settings.Height * 4] };
        Render = job; RenderChanged?.Invoke();
        // remember how things were, so the document is put back afterwards
        var original = JointObjects.ToDictionary(j => j.joint.Key, j => (j.joint.Value, j.joint.Value2));
        double originalExplode = ExplodeFactor;
        var cam0 = Cameras[0];
        var gifFrames = new List<byte[]>(); int gw = Math.Min(settings.Width, 640), gh = Math.Max(2, settings.Height * gw / settings.Width);
        var started = DateTime.UtcNow; int done = 0;
        try
        {
            for (int i = 0; i < frames && !job.Cts.IsCancellationRequested; i++)
            {
                double t = frames == 1 ? 0 : (o.Turntable && anim == null ? duration * i / frames : Math.Min(duration, i / o.Fps));
                if (anim != null)
                {
                    var rest = new Dictionary<string, (double v1, double v2)>();
                    foreach (var (k, v) in original) rest[k] = (v.Item1, v.Item2);
                    rest["@explode"] = (originalExplode, 0);
                    var pose = anim.Sample(t, rest);
                    var values = new Dictionary<string, (double v1, double v2)>();
                    foreach (var (k, v) in original) values[k] = (v.Item1, v.Item2);
                    foreach (var (k, v) in pose) if (k != "@explode") values[k] = v;
                    SetJointValues(values);
                    SetExplode(pose.TryGetValue("@explode", out var ex) ? ex.v1 : originalExplode);
                    SetPartPose(PartPoseFrom(pose));
                    ApplyParameterShapes(pose);
                }
                var tracer = new PathTracer(settings);
                if (!tracer.LoadScene(this)) throw new InvalidOperationException("There is nothing visible to render.");
                double turn = o.Turntable ? 2 * Math.PI * o.Turns * i / frames : 0;
                var snap = new Camera { Target = cam0.Target, Distance = cam0.Distance, Yaw = cam0.Yaw - turn, Pitch = cam0.Pitch, Roll = cam0.Roll, FovY = cam0.FovY, Orthographic = cam0.Orthographic, Width = settings.Width, Height = settings.Height };
                byte[] last = [];
                int frameNo = i + 1;
                job.StatusOverride = $"Animation frame {frameNo}/{frames}  ({(DateTime.UtcNow - started).TotalSeconds:0}s)";
                await System.Threading.Tasks.Task.Run(() => tracer.Render(snap, (pix, w, h, samples, fin) => { if (fin) last = (byte[])pix.Clone(); }, job.Cts.Token));
                if (last.Length == 0) break;
                job.Pixels = last; job.Samples = settings.Samples; RenderChanged?.Invoke();
                PngWriter.Save(System.IO.Path.Combine(o.Folder, $"frame_{frameNo:0000}.png"), last, settings.Width, settings.Height);
                if (o.Gif) gifFrames.Add(Shrink(last, settings.Width, settings.Height, gw, gh));
                done++;
            }
            string msg = $"{done} frame(s) in {o.Folder}";
            if (done == frames && o.Gif && gifFrames.Count > 0)
            {
                var gif = System.IO.Path.Combine(o.Folder, "animation.gif");
                GifWriter.Save(gif, gifFrames, gw, gh, Math.Max(2, (int)Math.Round(100 / o.Fps)), true); msg += ", animation.gif";
            }
            if (done == frames && o.Mp4 && FindFfmpeg() is { } ff)
            {
                string inPattern = System.IO.Path.Combine(o.Folder, "frame_%04d.png"), outFile = System.IO.Path.Combine(o.Folder, "animation.mp4");
                string fps = o.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var psi = new System.Diagnostics.ProcessStartInfo(ff, "-y -loglevel error -nostats -framerate " + fps + " -i \"" + inPattern + "\" -c:v libx264 -pix_fmt yuv420p -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" \"" + outFile + "\"") { CreateNoWindow = true, UseShellExecute = false };
                using var p = System.Diagnostics.Process.Start(psi)!;
                await p.WaitForExitAsync();
                msg += p.ExitCode == 0 ? ", animation.mp4" : " (ffmpeg could not make the movie)";
            }
            else if (done == frames && o.Mp4) msg += ". Install ffmpeg to also get an MP4";
            job.StatusOverride = job.Cts.IsCancellationRequested ? $"Stopped after {done}/{frames} frames. {msg}" : $"Done: {msg}";
            Say(job.StatusOverride);
        }
        catch (Exception e) { job.StatusOverride = $"Animation render failed: {e.Message}"; Say(job.StatusOverride, true); }
        finally
        {
            job.Running = false; job.Finished = true;
            try { SetPartPose(null); RestoreParameterShapes(); SetJointValues(original); SetExplode(originalExplode); } catch (Exception e) { Say($"Could not restore the pose: {e.Message}", true); }
            RenderChanged?.Invoke();
        }
    }

    static byte[] Shrink(byte[] src, int w, int h, int nw, int nh)
    {
        if (nw == w && nh == h) return src;
        var o = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                int sx = x * w / nw, sy = y * h / nh;
                Buffer.BlockCopy(src, (sy * w + sx) * 4, o, (y * nw + x) * 4, 4);
            }
        return o;
    }

    static string? FindFfmpeg()
    {
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
        {
            try { var f = System.IO.Path.Combine(dir.Trim('"'), exe); if (System.IO.File.Exists(f)) return f; } catch { }
        }
        return null;
    }

    public void StopRender() { if (Render is { Running: true } r) { r.Cts.Cancel(); r.Running = false; RenderChanged?.Invoke(); } }

    public void CloseRender() { StopRender(); Render = null; RenderChanged?.Invoke(); }

    public string? SaveRender(string? path = null)
    {
        if (Render is not { } r || r.Samples == 0 || r.Pixels.Length == 0) { Say("There are no completed render samples to save yet.", true); return null; }
        path ??= Path.Combine(RenderFolder, $"render_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        PngWriter.Save(path, r.Pixels, r.Width, r.Height);
        r.SavedTo = path; Say($"Saved {path}"); RenderChanged?.Invoke();
        return path;
    }
}
