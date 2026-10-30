using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Prism.Core;
using Prism.Kernel;
using Silk.NET.OpenGL;
using Camera = Prism.Core.Camera;

namespace Prism.Rendering;

/// <summary>GPU copy of one <see cref="MeshData"/>: interleaved vertex buffer, index buffer and edge instance buffer.</summary>
sealed class GpuMesh
{
    public uint Vao, Vbo, Ebo, EdgeVbo, TriangleEdgeVbo;
    public int TriCount, SegCount, TriangleSegments, ScalarsVersion;
}

/// <summary>The 3D viewport: shaded B-rep display, edges, grid, overlays, picking and camera navigation.</summary>
public sealed unsafe class GlViewport : OpenGlControlBase
{
    public static readonly StyledProperty<CadSession?> SessionProperty =
        AvaloniaProperty.Register<GlViewport, CadSession?>(nameof(Session));
    public CadSession? Session { get => GetValue(SessionProperty); set => SetValue(SessionProperty, value); }

    public static readonly StyledProperty<int> CameraIndexProperty = AvaloniaProperty.Register<GlViewport, int>(nameof(CameraIndex));
    /// <summary>Which of the session's view cameras this viewport shows (0 = main).</summary>
    public int CameraIndex { get => GetValue(CameraIndexProperty); set => SetValue(CameraIndexProperty, value); }
    public Camera? Cam => Session?.Cameras[Math.Clamp(CameraIndex, 0, 3)];

    public Overlay LastOverlay { get; } = new();
    public event Action? FrameRendered;

    GL? _gl;
    uint _bg, _mesh, _line, _point, _grid, _flat, _img;
    readonly Dictionary<string, uint> _imgTex = [];
    uint _flatVao, _flatVbo;
    uint _emptyVao, _quadVbo, _lineVao, _dynLineVbo, _pointVao, _pointVbo, _gridVao, _gridVbo;
    uint _fbo, _rbColor, _rbDepth; int _fboW, _fboH;
    readonly Dictionary<(uint, string), int> _uloc = [];
    readonly Dictionary<MeshData, GpuMesh> _cache = [];
    readonly HashSet<MeshData> _used = [];
    CadSession? _hooked;

    static GlViewport()
    {
        SessionProperty.Changed.AddClassHandler<GlViewport>((v, e) => v.Hook(e.NewValue as CadSession));
        FocusableProperty.OverrideDefaultValue<GlViewport>(true);
    }

    void Hook(CadSession? s)
    {
        if (_hooked != null) _hooked.ViewChanged -= Redraw;
        _hooked = s;
        if (s != null) s.ViewChanged += Redraw;
        Redraw();
    }

    void Redraw() { if (Dispatcher.UIThread.CheckAccess()) RequestNextFrameRendering(); else Dispatcher.UIThread.Post(RequestNextFrameRendering); }

    // ================================================================= GL setup
    protected override void OnOpenGlInit(GlInterface gli)
    {
        _gl = GL.GetApi(gli.GetProcAddress);
        var gl = _gl;
        _bg = Program(Shaders.BgVert, Shaders.BgFrag);
        _mesh = Program(Shaders.MeshVert, Shaders.MeshFrag);
        _line = Program(Shaders.LineVert, Shaders.LineFrag);
        _point = Program(Shaders.PointVert, Shaders.PointFrag);
        _grid = Program(Shaders.GridVert, Shaders.GridFrag);
        _flat = Program(Shaders.FlatVert, Shaders.FlatFrag);
        _img = Program(Shaders.ImgVert, Shaders.ImgFrag);
        _flatVao = gl.GenVertexArray(); _flatVbo = gl.GenBuffer();

        _emptyVao = gl.GenVertexArray();

        // line quad (triangle strip: start/end x side)
        _quadVbo = gl.GenBuffer();
        float[] quad = [0, -1, 0, 1, 1, -1, 1, 1];
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        fixed (float* p = quad) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * 4), p, BufferUsageARB.StaticDraw);
        _lineVao = gl.GenVertexArray();
        gl.BindVertexArray(_lineVao);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 8, (void*)0);
        gl.EnableVertexAttribArray(1); gl.EnableVertexAttribArray(2);
        gl.VertexAttribDivisor(1, 1); gl.VertexAttribDivisor(2, 1);
        _dynLineVbo = gl.GenBuffer();

        _pointVao = gl.GenVertexArray(); _pointVbo = gl.GenBuffer();
        _gridVao = gl.GenVertexArray(); _gridVbo = gl.GenBuffer();
        gl.BindVertexArray(0);
    }

    protected override void OnOpenGlDeinit(GlInterface gli)
    {
        var gl = _gl; if (gl == null) return;
        foreach (var m in _cache.Values) FreeMesh(m);
        _cache.Clear();
        gl.DeleteProgram(_bg); gl.DeleteProgram(_mesh); gl.DeleteProgram(_line); gl.DeleteProgram(_point); gl.DeleteProgram(_grid); gl.DeleteProgram(_flat);
        DeleteTargets();
        _gl = null;
    }

    // ---- reference pictures (behind the model; they never write depth)
    unsafe uint ImageTexture(GL gl, Core.Assembly.ImageInfo info)
    {
        var key = info.Data.Length + ":" + info.Data.GetHashCode();
        if (_imgTex.TryGetValue(key, out var t)) return t;
        uint tex = 0;
        try
        {
            using var ms = new System.IO.MemoryStream(Convert.FromBase64String(info.Data));
            using var bmp = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(ms, Math.Min(Math.Max(info.PixelW, 1), 2048));
            int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
            var buf = new byte[w * h * 4];
            fixed (byte* p = buf) bmp.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), (IntPtr)p, buf.Length, w * 4);
            bool bgra = bmp.Format == Avalonia.Platform.PixelFormat.Bgra8888, premul = bmp.AlphaFormat == Avalonia.Platform.AlphaFormat.Premul;
            for (int i = 0; i < buf.Length; i += 4)
            {
                if (bgra) (buf[i], buf[i + 2]) = (buf[i + 2], buf[i]);
                if (premul && buf[i + 3] is > 0 and < 255) { int a = buf[i + 3]; buf[i] = (byte)Math.Min(255, buf[i] * 255 / a); buf[i + 1] = (byte)Math.Min(255, buf[i + 1] * 255 / a); buf[i + 2] = (byte)Math.Min(255, buf[i + 2] * 255 / a); }
            }
            tex = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, tex);
            fixed (byte* p = buf) gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, p);
            gl.GenerateMipmap(TextureTarget.Texture2D);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }
        catch (Exception) { tex = 0; }
        _imgTex[key] = tex;
        return tex;
    }

    void DrawImages(GL gl, CadSession s, Matrix4x4 vp)
    {
        bool editing = s.ActiveCommand is Commands.EditImageCommand or Commands.CalibrateImageCommand;
        if (!editing && s.ImageOverrides.Count > 0) s.ImageOverrides.Clear();
        foreach (var o in s.Objects)
        {
            if (!o.Visible || o.Asm is not Core.Assembly.ImageInfo info || o.SketchPlane is not { } pl) continue;
            if (s.ImageOverrides.TryGetValue(o.Id, out var ov)) { info = ov.info; pl = ov.plane; }
            uint tex = ImageTexture(gl, info); if (tex == 0) continue;
            gl.Enable(EnableCap.Blend); gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            gl.DepthMask(false); gl.Disable(EnableCap.CullFace);
            gl.UseProgram(_img);
            UM(_img, "uVP", vp);
            U3(_img, "uO", (float)pl.Origin.X, (float)pl.Origin.Y, (float)pl.Origin.Z);
            var u = pl.U.Normalized() * info.WidthMm; var v = pl.V.Normalized() * info.HeightMm;
            U3(_img, "uU", (float)u.X, (float)u.Y, (float)u.Z); U3(_img, "uV", (float)v.X, (float)v.Y, (float)v.Z);
            U2(_img, "uFlip", info.FlipH ? 1f : 0f, info.FlipV ? 1f : 0f);
            U1(_img, "uAlpha", (float)Math.Clamp(info.Opacity, 0, 1));
            gl.ActiveTexture(TextureUnit.Texture0); gl.BindTexture(TextureTarget.Texture2D, tex); U1(_img, "uTex", 0);
            gl.BindVertexArray(_emptyVao);
            gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            gl.BindVertexArray(0);
            gl.DepthMask(true);
        }
    }

    uint Program(string vs, string fs)
    {
        var gl = _gl!;
        uint Compile(ShaderType t, string src)
        {
            uint sh = gl.CreateShader(t);
            gl.ShaderSource(sh, src); gl.CompileShader(sh);
            gl.GetShader(sh, ShaderParameterName.CompileStatus, out int ok);
            if (ok == 0) throw new InvalidOperationException($"Shader compile failed ({t}): {gl.GetShaderInfoLog(sh)}");
            return sh;
        }
        uint v = Compile(ShaderType.VertexShader, vs), f = Compile(ShaderType.FragmentShader, fs);
        uint p = gl.CreateProgram();
        gl.AttachShader(p, v); gl.AttachShader(p, f); gl.LinkProgram(p);
        gl.GetProgram(p, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0) throw new InvalidOperationException("Program link failed: " + gl.GetProgramInfoLog(p));
        gl.DeleteShader(v); gl.DeleteShader(f);
        return p;
    }

    int U(uint prog, string name)
    {
        if (!_uloc.TryGetValue((prog, name), out int l)) _uloc[(prog, name)] = l = _gl!.GetUniformLocation(prog, name);
        return l;
    }
    void U1(uint p, string n, float v) => _gl!.Uniform1(U(p, n), v);
    void U1(uint p, string n, int v) => _gl!.Uniform1(U(p, n), v);
    void U2(uint p, string n, float x, float y) => _gl!.Uniform2(U(p, n), x, y);
    void U3(uint p, string n, Vector3 v) => _gl!.Uniform3(U(p, n), v.X, v.Y, v.Z);
    void U3(uint p, string n, float x, float y, float z) => _gl!.Uniform3(U(p, n), x, y, z);
    void U4(uint p, string n, float x, float y, float z, float w) => _gl!.Uniform4(U(p, n), x, y, z, w);
    void UM(uint p, string n, Matrix4x4 m)
    {
        float* a = stackalloc float[16];
        a[0] = m.M11; a[1] = m.M12; a[2] = m.M13; a[3] = m.M14; a[4] = m.M21; a[5] = m.M22; a[6] = m.M23; a[7] = m.M24;
        a[8] = m.M31; a[9] = m.M32; a[10] = m.M33; a[11] = m.M34; a[12] = m.M41; a[13] = m.M42; a[14] = m.M43; a[15] = m.M44;
        _gl!.UniformMatrix4(U(p, n), 1, false, a);
    }

    // ================================================================= render targets (4x MSAA)
    void EnsureTargets(int w, int h)
    {
        var gl = _gl!;
        if (_fbo != 0 && w == _fboW && h == _fboH) return;
        DeleteTargets();
        _fboW = w; _fboH = h;
        _fbo = gl.GenFramebuffer();
        _rbColor = gl.GenRenderbuffer(); _rbDepth = gl.GenRenderbuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _rbColor);
        gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, 4, InternalFormat.Rgba8, (uint)w, (uint)h);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _rbColor);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _rbDepth);
        gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, 4, InternalFormat.Depth24Stencil8, (uint)w, (uint)h);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _rbDepth);
    }

    void DeleteTargets()
    {
        var gl = _gl; if (gl == null || _fbo == 0) return;
        gl.DeleteFramebuffer(_fbo); gl.DeleteRenderbuffer(_rbColor); gl.DeleteRenderbuffer(_rbDepth);
        _fbo = _rbColor = _rbDepth = 0;
    }

    // ================================================================= mesh cache
    /// <summary>Feed the smooth curvature to the shader while the curvature map is on, and take it away again afterwards.</summary>
    static void SyncCurvature(MeshData m, bool on)
    {
        if (on)
        {
            if (m.ScalarsAreCurvature) return;
            if (m.Scalars != null) return;                           // another analysis owns the scalars
            m.Curvature ??= Meshing.MeshCurvature.MeanCurvature(m);
            m.Scalars = m.Curvature; m.ScalarsAreCurvature = true; m.ScalarsVersion++;
        }
        else if (m.ScalarsAreCurvature) { m.Scalars = null; m.ScalarsAreCurvature = false; m.ScalarsVersion++; }
    }

    static float[] Interleave(MeshData m)
    {
        int nv = m.VertexCount; var inter = new float[nv * 7];
        for (int i = 0; i < nv; i++)
        {
            inter[7 * i] = m.Positions[3 * i]; inter[7 * i + 1] = m.Positions[3 * i + 1]; inter[7 * i + 2] = m.Positions[3 * i + 2];
            inter[7 * i + 3] = m.Normals[3 * i]; inter[7 * i + 4] = m.Normals[3 * i + 1]; inter[7 * i + 5] = m.Normals[3 * i + 2];
            inter[7 * i + 6] = m.Scalars != null && i < m.Scalars.Length ? m.Scalars[i] : 0f;
        }
        return inter;
    }

    GpuMesh Upload(MeshData m)
    {
        _used.Add(m);
        if (_cache.TryGetValue(m, out var g))
        {
            if (g.ScalarsVersion != m.ScalarsVersion)
            {
                _gl!.BindBuffer(BufferTargetARB.ArrayBuffer, g.Vbo);
                var again = Interleave(m);
                fixed (float* p = again) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(again.Length * 4), p, BufferUsageARB.StaticDraw);
                g.ScalarsVersion = m.ScalarsVersion;
            }
            return g;
        }
        var gl = _gl!;
        g = new GpuMesh { TriCount = m.TriangleCount, SegCount = m.SegmentCount };
        int nv = m.VertexCount;
        var inter = Interleave(m); g.ScalarsVersion = m.ScalarsVersion;
        g.Vao = gl.GenVertexArray(); g.Vbo = gl.GenBuffer(); g.Ebo = gl.GenBuffer(); g.EdgeVbo = gl.GenBuffer();
        gl.BindVertexArray(g.Vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, g.Vbo);
        fixed (float* p = inter) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(inter.Length * 4), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0); gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, (void*)0);
        gl.EnableVertexAttribArray(1); gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 28, (void*)12);
        gl.EnableVertexAttribArray(2); gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 28, (void*)24);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, g.Ebo);
        fixed (int* p = m.Indices) gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(m.Indices.Length * 4), p, BufferUsageARB.StaticDraw);
        gl.BindVertexArray(0);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, g.EdgeVbo);
        fixed (float* p = m.EdgePoints) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(Math.Max(m.EdgePoints.Length, 1) * 4), p, BufferUsageARB.StaticDraw);
        _cache[m] = g;
        return g;
    }

    void FreeMesh(GpuMesh g)
    {
        var gl = _gl!;
        gl.DeleteVertexArray(g.Vao); gl.DeleteBuffer(g.Vbo); gl.DeleteBuffer(g.Ebo); gl.DeleteBuffer(g.EdgeVbo);
        if(g.TriangleEdgeVbo!=0)gl.DeleteBuffer(g.TriangleEdgeVbo);
    }

    void EnsureTriangleEdges(GL gl, MeshData mesh, GpuMesh gpu)
    {
        if (gpu.TriangleEdgeVbo != 0) return;
        var points = new float[checked(mesh.Indices.Length * 6)];
        int at=0;
        for(int i=0;i+2<mesh.Indices.Length;i+=3)
        for(int k=0;k<3;k++)
        {
            int a=mesh.Indices[i+k]*3,b=mesh.Indices[i+(k+1)%3]*3;
            for(int axis=0;axis<3;axis++)points[at++]=mesh.Positions[a+axis];
            for(int axis=0;axis<3;axis++)points[at++]=mesh.Positions[b+axis];
        }
        gpu.TriangleSegments=at/6;gpu.TriangleEdgeVbo=gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer,gpu.TriangleEdgeVbo);
        fixed(float* p=points)gl.BufferData(BufferTargetARB.ArrayBuffer,(nuint)(points.Length*4),p,BufferUsageARB.StaticDraw);
    }

    void Collect()
    {
        foreach (var k in _cache.Keys.Where(k => !_used.Contains(k)).ToList()) { FreeMesh(_cache[k]); _cache.Remove(k); }
        _used.Clear();
    }

    // ================================================================= frame
    sealed class Highlight { public bool Object; public HashSet<int> Faces = [], Edges = []; }

    static Dictionary<Guid, Highlight> Build(IEnumerable<SelItem> items)
    {
        var d = new Dictionary<Guid, Highlight>();
        foreach (var it in items)
        {
            if (!d.TryGetValue(it.Obj, out var h)) d[it.Obj] = h = new();
            switch (it.Kind) { case SelKind.Object: h.Object = true; break; case SelKind.Face: h.Faces.Add(it.Index); break; default: h.Edges.Add(it.Index); break; }
        }
        return d;
    }

    static (float r, float g, float b, float a) C(uint c) => (((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, ((c >> 24) & 255) / 255f);

    protected override void OnOpenGlRender(GlInterface gli, int fb)
    {
        var gl = _gl; if (gl == null) return;
        float scale = (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);
        int w = Math.Max((int)(Bounds.Width * scale), 2), h = Math.Max((int)(Bounds.Height * scale), 2);
        EnsureTargets(w, h);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        gl.Viewport(0, 0, (uint)w, (uint)h);

        var s = Session;
        gl.Disable(EnableCap.DepthTest); gl.Disable(EnableCap.Blend); gl.Disable(EnableCap.CullFace);
        gl.DepthMask(true);
        var gp = GridPreset.Current(Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light);
        gl.ClearColor((gp.Top.r + gp.Bottom.r) / 2, (gp.Top.g + gp.Bottom.g) / 2, (gp.Top.b + gp.Bottom.b) / 2, 1f);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit));

        // background
        gl.UseProgram(_bg);
        U3(_bg, "uTop", gp.Top.r, gp.Top.g, gp.Top.b); U3(_bg, "uBottom", gp.Bottom.r, gp.Bottom.g, gp.Bottom.b);
        gl.BindVertexArray(_emptyVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        if (s != null)
        {
            // draw through this viewport's own camera, then give the session back whichever camera was active
            var prev = s.Camera;
            if (Cam is { } mine) s.Camera = mine;
            try { DrawScene(gl, s, w, h, scale); }
            finally { s.Camera = prev; }
        }

        // resolve MSAA into Avalonia's framebuffer
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)fb);
        gl.BlitFramebuffer(0, 0, w, h, 0, 0, w, h, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        Collect();
        Dispatcher.UIThread.Post(() => FrameRendered?.Invoke());
    }

    void SetClip(GL gl, uint prog, CadSession s)
    {
        var c = s.Clip;
        U1(prog, "uClipOn", c == null ? 0 : 1);
        if (c != null)
        {
            var n = c.Normal.Normalized();
            gl.Uniform4(U(prog, "uClip"), (float)n.X, (float)n.Y, (float)n.Z, (float)Vec3.Dot(n, c.Origin));
        }
    }

    void DrawScene(GL gl, CadSession s, int w, int h, float scale)
    {
        var cam = s.Camera;
        cam.Width = Bounds.Width; cam.Height = Bounds.Height;
        var vp = cam.ViewProjection;
        var eye = cam.Eye; var eyeV = new Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);
        var right = V(cam.Right); var up = V(cam.Up); var toEye = V(cam.EyeDirection);

        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);

        // ---- grid
        if (s.ShowGrid) DrawGrid(gl, s, vp, scale);
        DrawImages(gl, s, vp);

        // ---- highlights
        var selItems = s.Selection.Concat(s.ActiveCommand?.Slots.SelectMany(x => x.Items) ?? []).ToList();
        var sel = Build(selItems);
        var hovItems = new List<SelItem>(); if (s.Hover is { } hh) hovItems.Add(hh);
        foreach (var gid in s.HighlightIds) hovItems.Add(new SelItem(gid, SelKind.Object, -1));
        var hov = hovItems.Count > 0 ? Build(hovItems) : [];

        var mode = s.Display;
        gl.UseProgram(_mesh);
        UM(_mesh, "uVP", vp);
        U3(_mesh, "uEye", eyeV); U3(_mesh, "uRight", right); U3(_mesh, "uUp", up); U3(_mesh, "uToEye", toEye);
        U1(_mesh, "uZebra", s.Zebra ? 1 : s.Analysis == CadSession.AnalysisKind.Curvature ? 2 : s.Analysis == CadSession.AnalysisKind.Draft ? 3 : 0);
        SetClip(gl, _mesh, s);
        U3(_mesh, "uPull", V(s.DraftDir));
        U1(_mesh, "uDraftSin", (float)Math.Sin(s.DraftMin * Math.PI / 180));
        U1(_mesh, "uCurvRange", (float)s.CurvRange);

        var visible = s.Objects.Where(o => o.Visible && !s.HiddenIds.Contains(o.Id) && o.Id != s.Result?.BodyId).ToList();
        bool fineShading = s.Zebra || s.Analysis == CadSession.AnalysisKind.Curvature;
        foreach (var vo in visible) SyncCurvature(fineShading && vo.Kind is not (ObjKind.Curve or ObjKind.Sketch) ? vo.FineMesh : vo.Mesh, s.Analysis == CadSession.AnalysisKind.Curvature && !s.Zebra);
        foreach (var pv in s.Preview.Concat(s.ContextItems)) SyncCurvature(pv.Mesh, s.Analysis == CadSession.AnalysisKind.Curvature && !s.Zebra);

        // ---- opaque faces (or, in X-ray, translucent ones that do not hide what is behind)
        bool xray = s.XRay;
        float faceAlpha = xray ? 0.26f : 1f;
        if (xray) { gl.Enable(EnableCap.Blend); gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); gl.DepthMask(false); }
        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(1.0f, 1.0f);
        gl.UseProgram(_mesh);
        foreach (var o in visible)
        {
            if (o.Kind is ObjKind.Curve or ObjKind.Sketch || o.IsAnnotation) continue;
            if (mode == DisplayMode.Wireframe) continue;
            _look = (o.Look.Metal, o.Look.Gloss);
            SetSurface(gl,o);
            if (o.Tri != null && s.Fields.TryGetValue(o.Id, out var fld) && ReferenceEquals(fld.Source, o.Tri))
            {
                U1(_mesh, "uField", fld.Mode); U1(_mesh, "uPalette", fld.Palette); gl.Uniform2(U(_mesh, "uFRange"), fld.Min, fld.Max);
                DrawFaces(gl, Upload(fld.Display), o.Color, o.Color, 1f, (0, 0, 0, 0), 0, -1);
                U1(_mesh, "uField", 0);
                continue;
            }
            DrawFaces(gl, Upload(fineShading ? o.FineMesh : o.Mesh), o.Color, o.Kind == ObjKind.Surface ? 0xFF8FC9DB : o.Color, faceAlpha, (0, 0, 0, 0), 0, -1);
        }
        // selection / hover tint (faces)
        gl.PolygonOffset(-0.5f, -0.5f);
        _look = (0f, 0.35f);
        U1(_mesh,"uPattern",(int)SurfacePattern.Plain);
        foreach (var (dict, col, amount) in new[] { (sel, 0xFFFF8A1Fu, 0.55f), (hov, 0xFF35B6FFu, 0.45f) })
        {
            foreach (var o in visible)
            {
                if (!dict.TryGetValue(o.Id, out var hl)) continue;
                if (o.Kind is ObjKind.Curve or ObjKind.Sketch || o.IsAnnotation) continue;
                if (mode == DisplayMode.Wireframe) continue;
                if (o.Tri != null && s.Fields.TryGetValue(o.Id, out var fld2) && ReferenceEquals(fld2.Source, o.Tri)) continue;
                var g = Upload(o.Mesh); var (r, gg, b, _) = C(col); _look = (o.Look.Metal, o.Look.Gloss);
                SetSurface(gl,o);
                if (hl.Object) DrawFaces(gl, g, o.Color, o.Color, faceAlpha, (r, gg, b, amount * 0.7f), 0, -1);
                foreach (var f in hl.Faces)
                    if (f + 1 < o.Mesh.FaceTriStart.Length) DrawFaces(gl, g, o.Color, o.Color, faceAlpha, (r, gg, b, amount), o.Mesh.FaceTriStart[f], o.Mesh.FaceTriStart[f + 1] - o.Mesh.FaceTriStart[f]);
            }
        }
        gl.Disable(EnableCap.PolygonOffsetFill);
        if (xray) gl.DepthMask(true);

        // ---- edges
        gl.UseProgram(_line);
        UM(_line, "uVP", vp); U2(_line, "uViewport", w, h);
        SetClip(gl, _line, s);
        gl.Enable(EnableCap.Blend); gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(false);
        foreach (var o in visible)
        {
            var m = o.Mesh; var g = Upload(m);
            switch (o.Kind)
            {
                case ObjKind.Solid or ObjKind.Surface:
                    if (mode == DisplayMode.Shaded) break;
                    DrawEdges(gl, g, 0, g.SegCount, mode == DisplayMode.Wireframe ? (0.75f, 0.86f, 1f, 1f) : (0.10f, 0.12f, 0.16f, 0.95f), mode == DisplayMode.Wireframe ? 1.6f * scale : 1.45f * scale, 0.00025f);
                    break;
                case ObjKind.Form or ObjKind.Mesh:
                    if(o.Kind==ObjKind.Form && s.ActiveCommand is Prism.Commands.EditFormCommand {ShowTriangles:false})break;
                    if (mode == DisplayMode.Shaded) break;
                    EnsureTriangleEdges(gl,m,g);
                    DrawEdges(gl,g,0,g.TriangleSegments,mode==DisplayMode.Wireframe?(0.75f,0.86f,1f,1f):(0.10f,0.12f,0.16f,0.55f),mode==DisplayMode.Wireframe?1.2f*scale:0.85f*scale,0.00025f,g.TriangleEdgeVbo);
                    break;
                case ObjKind.Curve:
                    DrawEdges(gl, g, 0, g.SegCount, C2(o.Color), 2.2f * scale, 0.0006f);
                    break;
                case ObjKind.Sketch:
                    DrawEdges(gl, g, 0, g.SegCount, C2(o.Color), 2.4f * scale, 0.0006f);
                    break;
            }
        }
        foreach (var (dict, col, wd) in new[] { (sel, 0xFFFF8A1Fu, 3.2f), (hov, 0xFF35B6FFu, 3.0f) })
            foreach (var o in visible)
            {
                if (!dict.TryGetValue(o.Id, out var hl)) continue;
                var g = Upload(o.Mesh); var m = o.Mesh;
                if (hl.Object)
                {
                    if (mode == DisplayMode.Wireframe && o.Kind is (ObjKind.Form or ObjKind.Mesh))
                    {
                        EnsureTriangleEdges(gl,m,g);var (red,green,blue,_) = C2(col);
                        DrawEdges(gl,g,0,g.TriangleSegments,(red,green,blue,0.85f),1.2f*scale,0.0012f,g.TriangleEdgeVbo);
                    }
                    else DrawEdges(gl, g, 0, g.SegCount, C2(col), wd * scale, 0.0012f);
                }
                foreach (var e in hl.Edges)
                    if (e + 1 < m.EdgeSegStart.Length) DrawEdges(gl, g, m.EdgeSegStart[e], m.EdgeSegStart[e + 1] - m.EdgeSegStart[e], C2(col), (wd + 0.8f) * scale, 0.0012f);
            }

        // ---- simulation result: the deformed, colored surface stands in for the body
        gl.UseProgram(_mesh);U1(_mesh,"uPattern",(int)SurfacePattern.Plain);
        if (s.Result is { } rv)
        {
            var gm = Upload(rv.Mesh);
            gl.UseProgram(_mesh);
            gl.Enable(EnableCap.PolygonOffsetFill); gl.PolygonOffset(1f, 1f);
            _look = (0f, 0.3f);
            if (rv.Kind != Simulation.ResultKind.MeshOnly) { U1(_mesh, "uField", 1); gl.Uniform2(U(_mesh, "uFRange"), rv.Min, rv.Max); }
            gl.DepthMask(true); gl.Disable(EnableCap.Blend);
            DrawFaces(gl, gm, 0xFFB9C6D6, 0xFFB9C6D6, 1f, (0, 0, 0, 0), 0, -1);
            U1(_mesh, "uField", 0);
            gl.Enable(EnableCap.Blend); gl.DepthMask(false);
            gl.Disable(EnableCap.PolygonOffsetFill);
            if (rv.ShowEdges || rv.Kind == Simulation.ResultKind.MeshOnly) { gl.UseProgram(_line); DrawEdges(gl, gm, 0, gm.SegCount, (0.08f, 0.10f, 0.14f, 0.55f), 1.0f * scale, 0.0004f); }
        }

        // ---- command preview
        foreach (var p in s.Preview.Concat(s.ContextItems))
        {
            var g = Upload(p.Mesh);
            if (p.Context)
            {
                // the rest of the assembly: faint and see-through, so the part can be modeled against it
                gl.UseProgram(_mesh);
                gl.Enable(EnableCap.PolygonOffsetFill); gl.PolygonOffset(1f, 1f);
                _look = (0f, 0.35f);
                gl.DepthMask(false);
                DrawFaces(gl, g, 0xFF9AA4B5, 0xFF9AA4B5, 0.22f, (0, 0, 0, 0), 0, -1);
                gl.Disable(EnableCap.PolygonOffsetFill);
                gl.UseProgram(_line);
                DrawEdges(gl, g, 0, g.SegCount, (0.60f, 0.65f, 0.72f, 0.45f), 1.1f * scale, 0.0003f);
                continue;
            }
            if (p.Ghost)
            {
                gl.UseProgram(_mesh);
                gl.Enable(EnableCap.PolygonOffsetFill); gl.PolygonOffset(-1f, -1f);
                _look = (0f, 0.35f);
                gl.DepthMask(false);
                DrawFaces(gl, g, 0xFFFF8A1F, 0xFFFF8A1F, 0.30f, (0, 0, 0, 0), 0, -1);
                gl.Disable(EnableCap.PolygonOffsetFill);
                gl.UseProgram(_line);
                DrawEdges(gl, g, 0, g.SegCount, (1f, 0.54f, 0.12f, 0.9f), 1.4f * scale, 0.0003f);
                continue;
            }
            if (p.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form or ObjKind.Mesh)
            {
                if (mode != DisplayMode.Wireframe)
                {
                    gl.UseProgram(_mesh);
                    gl.Enable(EnableCap.PolygonOffsetFill); gl.PolygonOffset(1f, 1f);
                    _look = (0f, 0.35f);
                    if (p.SolidColor is { } sc) { gl.DepthMask(true); gl.Disable(EnableCap.Blend); DrawFaces(gl, g, sc, p.Kind == ObjKind.Surface ? 0xFF8FC9DB : sc, 1f, (0, 0, 0, 0), 0, -1); gl.Enable(EnableCap.Blend); gl.DepthMask(false); }
                    else { gl.DepthMask(true); gl.Disable(EnableCap.Blend); DrawFaces(gl, g, 0xFF8FB4E6, 0xFF8FB4E6, 1f, (0, 0, 0, 0), 0, -1); gl.Enable(EnableCap.Blend); gl.DepthMask(false); }
                    gl.Disable(EnableCap.PolygonOffsetFill);
                }
                gl.UseProgram(_line);
                if(p.Kind==ObjKind.Form && s.ActiveCommand is Prism.Commands.EditFormCommand {ShowTriangles:false}) { }
                else if(p.Kind is ObjKind.Form or ObjKind.Mesh && mode!=DisplayMode.Shaded)
                { EnsureTriangleEdges(gl,p.Mesh,g);DrawEdges(gl,g,0,g.TriangleSegments,(0.75f,0.86f,1f,1f),1.2f*scale,0.0003f,g.TriangleEdgeVbo); }
                else if(mode!=DisplayMode.Shaded) DrawEdges(gl, g, 0, g.SegCount, p.SolidColor != null ? (0.10f, 0.12f, 0.16f, 0.85f) : (0.05f, 0.30f, 0.75f, 1f), 1.5f * scale, 0.0003f);
            }
            else DrawEdges(gl, g, 0, g.SegCount, (0.12f, 0.45f, 0.95f, 1f), 2.6f * scale, 0.0008f);
        }

        // ---- overlays (always on top)
        LastOverlay.Clear();
        s.BuildOverlay(LastOverlay);
        gl.Disable(EnableCap.DepthTest);
        DrawOverlay(gl, LastOverlay, vp, w, h, scale);

        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
    }

    static Vector3 V(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
    static (float, float, float, float) C2(uint c) => C(c);

    (float metal, float gloss) _look = (0f, 0.35f);

    void SetSurface(GL gl,SceneObject obj)
    {
        U1(_mesh,"uPattern",(int)MaterialSurface.Pattern(obj.Look));
        U2(_mesh,"uPatternParams",Math.Max(obj.Look.PatternScale,.1f),obj.Look.PatternAngle*(float)Math.PI/180);
        var min=obj.Mesh.Min; U3(_mesh,"uPatternOrigin",(float)min.X,(float)min.Y,(float)min.Z);
    }

    void DrawFaces(GL gl, GpuMesh g, uint color, uint back, float alpha, (float r, float g, float b, float a) tint, int triStart, int triCount)
    {
        if (g.TriCount == 0) return;
        gl.UseProgram(_mesh);
        var (r, gg, b, _) = C(color); var (br, bg, bb, _) = C(back);
        U3(_mesh, "uColor", r, gg, b); U3(_mesh, "uBack", br, bg, bb);
        U4(_mesh, "uTint", tint.r, tint.g, tint.b, tint.a);
        U1(_mesh, "uAlpha", alpha);
        gl.Uniform2(U(_mesh, "uMat"), _look.metal, _look.gloss);
        gl.BindVertexArray(g.Vao);
        int count = triCount < 0 ? g.TriCount : triCount;
        if (count <= 0) return;
        gl.DrawElements(PrimitiveType.Triangles, (uint)(count * 3), DrawElementsType.UnsignedInt, (void*)((long)triStart * 12));
        gl.BindVertexArray(0);
    }

    void DrawEdges(GL gl, GpuMesh g, int segStart, int segCount, (float r, float g, float b, float a) c, float width, float bias, uint edgeBuffer = 0)
    {
        if (segCount <= 0) return;
        gl.UseProgram(_line);
        U4(_line, "uColor", c.r, c.g, c.b, c.a); U1(_line, "uWidth", width); U1(_line, "uBias", bias * 0.1f);
        gl.BindVertexArray(_lineVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, edgeBuffer == 0 ? g.EdgeVbo : edgeBuffer);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, (void*)((long)segStart * 24));
        gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 24, (void*)((long)segStart * 24 + 12));
        gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)segCount);
        gl.BindVertexArray(0);
    }

    static Vector3 AxisColor(Vec3 a) =>
        Math.Abs(a.X) > 0.9 ? new(0.86f, 0.27f, 0.27f) : Math.Abs(a.Y) > 0.9 ? new(0.30f, 0.68f, 0.34f) : new(0.27f, 0.45f, 0.86f);

    /// <summary>The principal plane facing the camera when the view is (nearly) axis-aligned, else the ground plane.</summary>
    public static Plane3 ViewGridPlane(Camera cam)
    {
        var e = cam.EyeDirection; double ax = Math.Abs(e.X), ay = Math.Abs(e.Y), az = Math.Abs(e.Z);
        const double near = 0.95;       // within about 18 degrees of looking straight along an axis
        if (ax >= near && ax >= ay && ax >= az) return Plane3.YZ;
        if (ay >= near && ay >= ax && ay >= az) return Plane3.XZ;
        return Plane3.XY;
    }

    /// <summary>The sketch grid remains on its entry plane, independently of the active drawing frame.</summary>
    public static Plane3 DisplayGridPlane(CadSession s)
        => s.Sketch?.GridPlane ?? s.ActiveCommand?.WorkPlane ?? ViewGridPlane(s.Camera);

    void DrawGrid(GL gl, CadSession s, Matrix4x4 vp, float scale)
    {
        var cam = s.Camera;
        // The large reference grid must not swing through the viewport when a drawing plane or gizmo changes.
        // Outside a sketch, commands retain their work-plane grid and idle views use the camera-facing principal plane.
        var pl = DisplayGridPlane(s);
        double ext = Math.Max(cam.Distance * 30, 400);
        var center = pl.Origin + pl.U * Vec3.Dot(cam.Target - pl.Origin, pl.U) + pl.V * Vec3.Dot(cam.Target - pl.Origin, pl.V);
        Vec3[] c = [center - pl.U * ext - pl.V * ext, center + pl.U * ext - pl.V * ext, center + pl.U * ext + pl.V * ext, center - pl.U * ext + pl.V * ext];
        float[] data = new float[18];
        int[] order = [0, 1, 2, 0, 2, 3];
        for (int i = 0; i < 6; i++) { data[3 * i] = (float)c[order[i]].X; data[3 * i + 1] = (float)c[order[i]].Y; data[3 * i + 2] = (float)c[order[i]].Z; }
        gl.UseProgram(_grid);
        UM(_grid, "uVP", vp);
        U3(_grid, "uOrigin", V(pl.Origin)); U3(_grid, "uU", V(pl.U)); U3(_grid, "uV", V(pl.V));
        U1(_grid, "uSpacing", (float)GridMath.Spacing(cam)); U3(_grid, "uCenter", V(center)); U1(_grid, "uExtent", (float)ext);
        U3(_grid, "uAxisU", AxisColor(pl.U)); U3(_grid, "uAxisV", AxisColor(pl.V));
        var gp = GridPreset.Current(Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light);
        U3(_grid, "uMinorCol", gp.Minor.r, gp.Minor.g, gp.Minor.b); U3(_grid, "uMajorCol", gp.Major.r, gp.Major.g, gp.Major.b);
        gl.Uniform2(U(_grid, "uAlpha"), gp.MinorAlpha, gp.MajorAlpha);
        U1(_grid, "uDots", gp.Dots ? 1f : 0f); U1(_grid, "uGlow", gp.Glow); U1(_grid, "uFade", gp.Fade);
        gl.Enable(EnableCap.Blend); gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(false);
        gl.BindVertexArray(_gridVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);
        fixed (float* p = data) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * 4), p, BufferUsageARB.DynamicDraw);
        gl.EnableVertexAttribArray(0); gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, (void*)0);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        gl.BindVertexArray(0);
        gl.DepthMask(true);
    }

    void DrawOverlay(GL gl, Overlay o, Matrix4x4 vp, int w, int h, float scale)
    {
        if (o.Tris.Count > 0)
        {
            var arr = new float[o.Tris.Count * 3 * 7]; int i = 0;
            foreach (var t in o.Tris)
            {
                var c = C(t.Color);
                foreach (var p in new[] { t.A, t.B, t.C })
                { arr[i++] = (float)p.X; arr[i++] = (float)p.Y; arr[i++] = (float)p.Z; arr[i++] = c.r; arr[i++] = c.g; arr[i++] = c.b; arr[i++] = c.a; }
            }
            gl.UseProgram(_flat);
            UM(_flat, "uVP", vp);
            gl.BindVertexArray(_flatVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _flatVbo);
            fixed (float* p = arr) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * 4), p, BufferUsageARB.StreamDraw);
            gl.EnableVertexAttribArray(0); gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, (void*)0);
            gl.EnableVertexAttribArray(1); gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, (void*)12);
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(o.Tris.Count * 3));
            gl.BindVertexArray(0);
        }
        // lines, batched by style
        if (o.Lines.Count > 0)
        {
            gl.UseProgram(_line);
            UM(_line, "uVP", vp); U2(_line, "uViewport", w, h);
            gl.BindVertexArray(_lineVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _dynLineVbo);
            foreach (var grp in o.Lines.GroupBy(l => (l.Color, l.Width)))
            {
                var arr = new float[grp.Count() * 6]; int i = 0;
                foreach (var l in grp) { arr[i++] = (float)l.A.X; arr[i++] = (float)l.A.Y; arr[i++] = (float)l.A.Z; arr[i++] = (float)l.B.X; arr[i++] = (float)l.B.Y; arr[i++] = (float)l.B.Z; }
                fixed (float* p = arr) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * 4), p, BufferUsageARB.StreamDraw);
                gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, (void*)0);
                gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 24, (void*)12);
                var c = C(grp.Key.Color);
                U4(_line, "uColor", c.r, c.g, c.b, c.a); U1(_line, "uWidth", grp.Key.Width * scale); U1(_line, "uBias", 0f);
                gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)grp.Count());
            }
            gl.BindVertexArray(0);
        }
        if (o.Dots.Count > 0)
        {
            var arr = new float[o.Dots.Count * 8]; int i = 0;
            foreach (var d in o.Dots)
            {
                var c = C(d.Color);
                arr[i++] = (float)d.P.X; arr[i++] = (float)d.P.Y; arr[i++] = (float)d.P.Z;
                arr[i++] = c.r; arr[i++] = c.g; arr[i++] = c.b; arr[i++] = c.a; arr[i++] = d.Size;
            }
            gl.UseProgram(_point);
            UM(_point, "uVP", vp); U1(_point, "uScale", scale);
            gl.BindVertexArray(_pointVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _pointVbo);
            fixed (float* p = arr) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * 4), p, BufferUsageARB.StreamDraw);
            gl.EnableVertexAttribArray(0); gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 32, (void*)0);
            gl.EnableVertexAttribArray(1); gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 32, (void*)12);
            gl.EnableVertexAttribArray(2); gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 32, (void*)28);
            gl.DrawArrays(PrimitiveType.Points, 0, (uint)o.Dots.Count);
            gl.BindVertexArray(0);
        }
    }

    // ================================================================= input
    // The GL surface itself is not hit-testable, so input is taken from the (transparent) parent panel.
    Control? _host;
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host = Parent as Control;
        if (_host == null) return;
        _host.PointerPressed += OnPressed; _host.PointerMoved += OnMoved; _host.PointerReleased += OnReleased;
        _host.PointerWheelChanged += OnWheel; _host.PointerExited += OnExited;
    }

    enum Nav { None, Orbit, Pan }
    Nav _nav; Point _last, _downPos; bool _moved;

    PointerInfo Info(PointerEventArgs e, bool left, int clicks = 1)
    {
        var p = e.GetPosition(this);
        return new(p.X, p.Y, left, e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Control), e.KeyModifiers.HasFlag(KeyModifiers.Alt), clicks);
    }

    /// <summary>Right-button gestures for the marking menu: "hold" (button held still, menu opens), "tap" (a click that nothing else used), "move" and "release" while a held menu is open.</summary>
    public event Action<string, Point>? MarkingMenu;
    Avalonia.Threading.DispatcherTimer? _holdTimer; bool _menuHold;

    void StopHold() { _holdTimer?.Stop(); _holdTimer = null; }

    void OnPressed(object? _, PointerPressedEventArgs e)
    {
        Focus();
        var s = Session; if (s == null) return;
        if (Cam is { } mine) s.Camera = mine;
        var pt = e.GetCurrentPoint(this);
        _last = _downPos = pt.Position; _moved = false;
        var props = pt.Properties;
        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed || (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && s.Sketch == null))
        {
            _nav = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control) ? Nav.Pan : Nav.Orbit;
            if (props.IsMiddleButtonPressed && e.ClickCount == 2) { s.ZoomExtents(); _nav = Nav.None; }
            e.Pointer.Capture(_host);
            if (props.IsRightButtonPressed && CameraIndex == 0 && _nav == Nav.Orbit && MarkingMenu != null && AppSettings.Current.MarkingMenu)
            {
                // still for a moment: the marking menu. Moving first means orbit, as before.
                StopHold();
                _holdTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Clamp(AppSettings.Current.MarkingHoldMs, 80, 1000)) };
                _holdTimer.Tick += (_, _) => { StopHold(); if (_nav == Nav.Orbit && !_moved) { _menuHold = true; MarkingMenu?.Invoke("hold", _downPos); } };
                _holdTimer.Start();
            }
        }
        else if (props.IsLeftButtonPressed)
        {
            s.PointerDown(Info(e, true, e.ClickCount));
            e.Pointer.Capture(_host);
        }
        e.Handled = true;
    }

    void OnMoved(object? _, PointerEventArgs e)
    {
        var s = Session; if (s == null) return;
        if (Cam is { } mine) s.Camera = mine;
        var pos = e.GetPosition(this);
        var (cursorOrigin,cursorDirection)=s.Camera.Ray(pos.X,pos.Y);
        s.ViewportCursor=s.WorkPlane.Intersect(cursorOrigin,cursorDirection);
        if (_menuHold) { MarkingMenu?.Invoke("move", pos); return; }
        if (_nav != Nav.None)
        {
            double dx = pos.X - _last.X, dy = pos.Y - _last.Y;
            if (Math.Abs(pos.X - _downPos.X) + Math.Abs(pos.Y - _downPos.Y) > 3) { _moved = true; StopHold(); }
            // Allow Shift or Ctrl to be pressed after the mouse button, too.
            bool pan = _nav == Nav.Pan || e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (pan) StopHold();
            // a Top/Front/Right view must stay what it says: dragging to orbit pans it instead
            if (!pan && !s.Camera.Locked) s.Camera.Orbit(dx, dy); else s.Camera.Pan(dx, dy);
            s.SyncViews(s.Camera);
            _last = pos; s.Invalidate();
            return;
        }
        bool left = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (!left) s.UpdateHover(pos.X, pos.Y);
        UpdateCursorReadout(s, pos);
        s.PointerMove(Info(e, left));
    }

    void UpdateCursorReadout(CadSession s, Point pos)
    {
        if (s.ActiveTool != null) return; // tools publish their own snapped cursor
        var (o, d) = s.Camera.Ray(pos.X, pos.Y);
        if (s.WorkPlane.Intersect(o, d) is { } p) s.Cursor3 = p;
    }

    void OnReleased(object? _, PointerReleasedEventArgs e)
    {
        var s = Session; if (s == null) return;
        StopHold();
        if (_menuHold)
        {
            _menuHold = false; _nav = Nav.None; e.Pointer.Capture(null);
            MarkingMenu?.Invoke("release", e.GetPosition(this));
            return;
        }
        if (_nav != Nav.None)
        {
            bool rightClick = e.InitialPressMouseButton == MouseButton.Right && !_moved;
            _nav = Nav.None; e.Pointer.Capture(null);
            if (rightClick && !s.Key("RightClick") && CameraIndex == 0 && AppSettings.Current.MarkingMenu) MarkingMenu?.Invoke("tap", e.GetPosition(this));      // a tool that wanted the right click keeps it
            return;
        }
        if (e.InitialPressMouseButton == MouseButton.Left) { s.PointerUp(Info(e, false)); e.Pointer.Capture(null); }
    }

    void OnWheel(object? _, PointerWheelEventArgs e)
    {
        var s = Session; if (s == null) return;
        if (Cam is { } mine) s.Camera = mine;
        var p = e.GetPosition(this);
        s.Camera.ZoomAt(p.X, p.Y, Math.Pow(0.86, AppSettings.Current.ReverseScrollZoom ? -e.Delta.Y : e.Delta.Y));
        s.SyncViews(s.Camera);
        s.Invalidate();
        e.Handled = true;
    }

    void OnExited(object? _, PointerEventArgs e)
    {
        if(Session is {} session)session.ViewportCursor=null;
        if (Session is { Hover: not null } s) { s.Hover = null; s.Invalidate(); }
    }
}
