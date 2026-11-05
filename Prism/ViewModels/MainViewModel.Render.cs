using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

/// <summary>The rendered picture, shown over the viewport while it refines.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] public partial WriteableBitmap? RenderBitmap { get; set; }
    [ObservableProperty] public partial bool RenderVisible { get; set; }
    [ObservableProperty] public partial bool RenderRunning { get; set; }
    [ObservableProperty] public partial string RenderStatus { get; set; } = "";

    Action? _onRender;

    void HookRender(CadSession s)
    {
        s.PickHdrEnvironment = async () => PickOpenFile == null ? null : await PickOpenFile("Choose Radiance HDR environment", ["*.hdr", "*.rgbe"]);
        s.PickRenderImage = async () => PickOpenFile == null ? null : await PickOpenFile("Choose render background", ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"]);
        _onRender = () => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(s, Session)) UpdateRender(); });
        s.RenderChanged += _onRender;
    }

    void UnhookRender(CadSession s) { if (_onRender != null) s.RenderChanged -= _onRender; }

    void UpdateRender()
    {
        var job = Session.Render;
        if (job == null) { RenderVisible = false; RenderBitmap = null; RenderRunning = false; return; }
        RenderVisible = true; RenderRunning = job.Running;
        RenderStatus = job.Status + (job.SavedTo != null ? $"   ·   saved: {job.SavedTo}" : "");
        if (job.Pixels.Length != job.Width * job.Height * 4) return;
        var bmp = new WriteableBitmap(new PixelSize(job.Width, job.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            int row = job.Width * 4;
            if (fb.RowBytes == row) Marshal.Copy(job.Pixels, 0, fb.Address, job.Pixels.Length);
            else for (int y = 0; y < job.Height; y++) Marshal.Copy(job.Pixels, y * row, fb.Address + y * fb.RowBytes, row);
        }
        RenderBitmap = bmp;
    }

    public void StopRender() => Session.StopRender();
    public void SaveRender() { var p = Session.SaveRender(); UpdateRender(); }
    public void CloseRender() => Session.CloseRender();
}
