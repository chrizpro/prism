using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Prism.Rendering;
using Silk.NET.OpenGL;
using SkiaSharp;

/// <summary>
/// GL surface for <see cref="GlViewport"/> in the browser. Avalonia.Browser has no OpenGlControlBase support, so the scene
/// is drawn with WebGL from inside Avalonia's own render pass (on the context Skia already uses), into an offscreen
/// texture that Skia then composites like an image.
/// </summary>
sealed class BrowserGlHost : Control, IGlViewportHost
{
    [DllImport("prism_gl")] static extern IntPtr prism_gl_proc([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    const uint Texture2D = 0x0DE1, Rgba8 = 0x8058;

    readonly GlViewport _owner;
    GL? _gl;
    bool _inited, _dirty = true;
    uint _fbo, _tex;
    int _w, _h;

    public BrowserGlHost(GlViewport owner) { _owner = owner; }

    public Control Visual => this;

    public void RequestFrame() { _dirty = true; InvalidateVisual(); }

    public override void Render(DrawingContext context) => context.Custom(new Op(this, new Rect(Bounds.Size)));

    sealed class Op(BrowserGlHost host, Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context) => host.Draw(context, bounds);
    }

    unsafe void Draw(ImmediateDrawingContext context, Rect bounds)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature == null) return;
        using var lease = feature.Lease();
        var gr = lease.GrContext;
        if (gr == null) return;

        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int w = Math.Max((int)(bounds.Width * scale), 2), h = Math.Max((int)(bounds.Height * scale), 2);

        gr.ResetContext();
        _gl ??= GL.GetApi(name => prism_gl_proc(name));
        var gl = _gl;
        if (!_inited) { _inited = true; _owner.GlInit(gl); }
        if (_dirty || w != _w || h != _h)
        {
            EnsureTarget(gl, w, h);
            _dirty = false;
            _owner.GlRender((int)_fbo);
        }
        gr.ResetContext();

        using var backend = new GRBackendTexture(w, h, false, new GRGlTextureInfo(Texture2D, _tex, Rgba8));
        using var image = SKImage.FromTexture(gr, backend, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        if (image != null) lease.SkCanvas.DrawImage(image, new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
    }

    unsafe void EnsureTarget(GL gl, int w, int h)
    {
        if (_fbo != 0 && w == _w && h == _h) return;
        if (_fbo != 0) { gl.DeleteFramebuffer(_fbo); gl.DeleteTexture(_tex); }
        _w = w; _h = h;
        _tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _tex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _tex, 0);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }
}
