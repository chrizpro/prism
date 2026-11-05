using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Rendering;

public enum RenderEnv { Studio, Sky, DarkStudio, LightsOnly, Hdr }
/// <summary>What is seen behind the model. Everything but Environment is composited in the picture: the ground then only catches shadows.</summary>
public enum RenderBg { Environment, White, Transparent, Green, Black, Gradient, Image }

public sealed class RenderSettings
{
    public int Width = 1280, Height = 720;
    public int Samples = 64;
    public int Bounces = 5;
    public RenderEnv Env = RenderEnv.Studio;
    public RenderBg Background = RenderBg.Environment;
    public string? BackgroundImage;
    public BackgroundFit ImageFit = BackgroundFit.Fill;
    public double ImageOffsetX, ImageOffsetY;
    public string? EnvironmentImage;
    public float EnvironmentRotation, EnvironmentIntensity = 1;
    public bool Denoise;
    public bool Ground = true;
    public double Exposure = 1.0;
    /// <summary>Lights dropped into the scene (null: none).</summary>
    public List<RenderLight>? Lights;
}

/// <summary>
/// A small CPU path tracer (no extra dependencies): soft shadows, reflections, ambient light from a studio or sky environment.
/// It renders in passes so the picture can be shown while it refines, and it can be stopped at any time.
/// </summary>
public sealed class PathTracer
{
    // ---------------------------------------------------------------- scene
    struct Mat { public Vector3 Albedo, Origin; public float Metal, Rough, Scale, Angle; public uint Color; public SurfacePattern Pattern; public SurfaceLayers Layers; }
    readonly List<Mat> _mats = [];
    float[] _p = [];        // 9 floats per triangle (3 vertices)
    float[] _n = [];        // 9 floats per triangle (3 vertex normals)
    int[] _m = [];          // material per triangle
    int _tris;

    // BVH (flattened)
    struct Node { public Vector3 Min, Max; public int Left, Count; }  // Count > 0: leaf with triangles [Left, Left+Count) in _order
    Node[] _nodes = [];
    int[] _order = [];
    int _nodeCount;

    Vector3 _sceneMin, _sceneMax;
    float _groundZ;

    // lights: caps of the sky sphere
    readonly List<(Vector3 dir, float cosMax, Vector3 rad)> _lights = [];
    Vector3 _skyZenith, _skyHorizon;

    // user lights, converted to the tracer's units
    struct Lamp { public LightKind Kind; public Vector3 Pos, Dir, Color; public float Radius, CosOuter, CosInner, CosSun; }
    readonly List<Lamp> _lamps = [];
    bool Catcher => _s.Ground && _s.Background != RenderBg.Environment;

    SurfaceImage? _backplate;
    HdrEnvironment? _environment;
    readonly RenderSettings _s;
    public PathTracer(RenderSettings s) { _s = s; }

    public int TriangleCount => _tris;

    public bool LoadScene(CadSession session)
    {
        if (!float.IsFinite(_s.EnvironmentRotation) || !float.IsFinite(_s.EnvironmentIntensity) || _s.EnvironmentIntensity < 0 || _s.EnvironmentIntensity > 100)
            throw new ArgumentException("HDR intensity must be 0–100 and rotation must be finite.");
        _backplate = _s.Background == RenderBg.Image ? SurfaceImage.Decode(_s.BackgroundImage ?? throw new ArgumentException("Import a background image first.")) : null;
        _environment = _s.Env == RenderEnv.Hdr ? HdrEnvironment.Decode(_s.EnvironmentImage ?? throw new ArgumentException("Import a Radiance HDR environment first.")) : null;
        _mats.Clear();
        var P = new List<float>(); var N = new List<float>(); var M = new List<int>();
        _sceneMin = new Vector3(float.MaxValue); _sceneMax = new Vector3(float.MinValue);
        foreach (var o in session.Objects)
        {
            if (!o.Visible || o.IsAnnotation || session.HiddenIds.Contains(o.Id)) continue;
            MeshData md;
            try { md = o.Mesh; } catch { continue; }
            if (md.Indices.Length == 0) continue;
            var (r, g, b) = Palette.Unpack(o.Color);
            // sRGB colour to linear light
            Vector3 albedo = new((float)Math.Pow(r, 2.2), (float)Math.Pow(g, 2.2), (float)Math.Pow(b, 2.2));
            int mid = _mats.Count;
            _mats.Add(new Mat { Albedo = albedo, Metal = Math.Clamp(o.Look.Metal, 0f, 1f), Rough = Math.Clamp(1f - o.Look.Gloss, 0.06f, 1f),
                Color=o.Color,Origin=new((float)md.Min.X,(float)md.Min.Y,(float)md.Min.Z),Pattern=MaterialSurface.Pattern(o.Look),Scale=o.Look.PatternScale,Angle=o.Look.PatternAngle, Layers = new SurfaceLayers(o.Look.Surface) });
            for (int t = 0; t + 2 < md.Indices.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int vi = md.Indices[t + k] * 3;
                    P.Add(md.Positions[vi]); P.Add(md.Positions[vi + 1]); P.Add(md.Positions[vi + 2]);
                    N.Add(md.Normals[vi]); N.Add(md.Normals[vi + 1]); N.Add(md.Normals[vi + 2]);
                }
                M.Add(mid);
            }
            _sceneMin = Vector3.Min(_sceneMin, new Vector3((float)md.Min.X, (float)md.Min.Y, (float)md.Min.Z));
            _sceneMax = Vector3.Max(_sceneMax, new Vector3((float)md.Max.X, (float)md.Max.Y, (float)md.Max.Z));
        }
        _p = [.. P]; _n = [.. N]; _m = [.. M]; _tris = M.Count;
        if (_tris == 0) return false;
        _groundZ = _sceneMin.Z - 0.0005f * (_sceneMax - _sceneMin).Length();
        BuildBvh();
        SetupEnvironment();
        SetupLamps();
        return true;
    }

    // ---------------------------------------------------------------- BVH
    void BuildBvh()
    {
        int n = _tris;
        var cen = new Vector3[n]; var bmin = new Vector3[n]; var bmax = new Vector3[n];
        for (int t = 0; t < n; t++)
        {
            var a = V(t, 0); var b = V(t, 1); var c = V(t, 2);
            bmin[t] = Vector3.Min(a, Vector3.Min(b, c)); bmax[t] = Vector3.Max(a, Vector3.Max(b, c));
            cen[t] = (a + b + c) / 3f;
        }
        _order = Enumerable.Range(0, n).ToArray();
        _nodes = new Node[n + 16]; _right = new int[_nodes.Length];
        _nodeCount = 0;
        BuildNode(0, n, cen, bmin, bmax);
    }

    int BuildNode(int lo, int hi, Vector3[] cen, Vector3[] bmin, Vector3[] bmax)
    {
        int id = _nodeCount++;
        if (id >= _nodes.Length) { Array.Resize(ref _nodes, _nodes.Length * 2); Array.Resize(ref _right, _nodes.Length); }
        var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
        var cmn = new Vector3(float.MaxValue); var cmx = new Vector3(float.MinValue);
        for (int i = lo; i < hi; i++) { int t = _order[i]; mn = Vector3.Min(mn, bmin[t]); mx = Vector3.Max(mx, bmax[t]); cmn = Vector3.Min(cmn, cen[t]); cmx = Vector3.Max(cmx, cen[t]); }
        _nodes[id].Min = mn; _nodes[id].Max = mx;
        int count = hi - lo;
        var ext = cmx - cmn; int axis = ext.X > ext.Y ? (ext.X > ext.Z ? 0 : 2) : (ext.Y > ext.Z ? 1 : 2);
        float span = axis == 0 ? ext.X : axis == 1 ? ext.Y : ext.Z;
        if (count <= 4 || span < 1e-9f) { _nodes[id].Left = lo; _nodes[id].Count = count; return id; }

        // binned surface-area heuristic along the widest axis
        const int B = 16;
        var bc = new int[B]; var bmn = new Vector3[B]; var bmx = new Vector3[B];
        for (int b = 0; b < B; b++) { bmn[b] = new Vector3(float.MaxValue); bmx[b] = new Vector3(float.MinValue); }
        float lo0 = axis == 0 ? cmn.X : axis == 1 ? cmn.Y : cmn.Z;
        int Bin(int t) { float c = axis == 0 ? cen[t].X : axis == 1 ? cen[t].Y : cen[t].Z; return Math.Clamp((int)((c - lo0) / span * B), 0, B - 1); }
        for (int i = lo; i < hi; i++) { int t = _order[i]; int b = Bin(t); bc[b]++; bmn[b] = Vector3.Min(bmn[b], bmin[t]); bmx[b] = Vector3.Max(bmx[b], bmax[t]); }
        static float Area(Vector3 a, Vector3 b) { var d = b - a; return d.X < 0 ? 0 : 2 * (d.X * d.Y + d.Y * d.Z + d.Z * d.X); }
        var leftA = new float[B]; var leftN = new int[B]; var rightA = new float[B]; var rightN = new int[B];
        var amn = new Vector3(float.MaxValue); var amx = new Vector3(float.MinValue); int cn = 0;
        for (int b = 0; b < B; b++) { amn = Vector3.Min(amn, bmn[b]); amx = Vector3.Max(amx, bmx[b]); cn += bc[b]; leftA[b] = Area(amn, amx); leftN[b] = cn; }
        amn = new Vector3(float.MaxValue); amx = new Vector3(float.MinValue); cn = 0;
        for (int b = B - 1; b >= 0; b--) { amn = Vector3.Min(amn, bmn[b]); amx = Vector3.Max(amx, bmx[b]); cn += bc[b]; rightA[b] = Area(amn, amx); rightN[b] = cn; }
        int best = -1; float bestCost = float.MaxValue;
        for (int b = 0; b < B - 1; b++)
        {
            if (leftN[b] == 0 || rightN[b + 1] == 0) continue;
            float cost = leftA[b] * leftN[b] + rightA[b + 1] * rightN[b + 1];
            if (cost < bestCost) { bestCost = cost; best = b; }
        }
        int mid;
        if (best < 0) mid = (lo + hi) / 2;
        else
        {
            int i = lo, j = hi - 1;
            while (i <= j) { if (Bin(_order[i]) <= best) i++; else { (_order[i], _order[j]) = (_order[j], _order[i]); j--; } }
            mid = i;
            if (mid == lo || mid == hi) mid = (lo + hi) / 2;
        }
        BuildNode(lo, mid, cen, bmin, bmax);                    // the left child always directly follows its parent
        int r = BuildNode(mid, hi, cen, bmin, bmax);
        _nodes[id].Count = 0; _right[id] = r;
        return id;
    }

    int[] _right = [];
    Vector3 V(int t, int k) => new(_p[9 * t + 3 * k], _p[9 * t + 3 * k + 1], _p[9 * t + 3 * k + 2]);
    Vector3 Nrm(int t, int k) => new(_n[9 * t + 3 * k], _n[9 * t + 3 * k + 1], _n[9 * t + 3 * k + 2]);

    // ---------------------------------------------------------------- environment
    void SetupEnvironment()
    {
        _lights.Clear();
        switch (_s.Env)
        {
            case RenderEnv.Sky:
                _skyZenith = new Vector3(0.18f, 0.38f, 0.85f); _skyHorizon = new Vector3(0.75f, 0.85f, 0.95f);
                _lights.Add((Vector3.Normalize(new Vector3(-0.45f, -0.55f, 0.7f)), MathF.Cos(0.06f), new Vector3(220, 205, 180)));
                break;
            case RenderEnv.DarkStudio:
                _skyZenith = new Vector3(0.015f); _skyHorizon = new Vector3(0.05f);
                _lights.Add((Vector3.Normalize(new Vector3(-0.6f, -0.5f, 0.6f)), MathF.Cos(0.45f), new Vector3(7, 7, 7.5f)));
                _lights.Add((Vector3.Normalize(new Vector3(0.7f, 0.6f, 0.4f)), MathF.Cos(0.35f), new Vector3(5, 5.5f, 7)));
                break;
            case RenderEnv.Hdr:
            case RenderEnv.LightsOnly:
                _skyZenith = _skyHorizon = Vector3.Zero;
                break;
            default:
                _skyZenith = new Vector3(0.42f, 0.44f, 0.48f); _skyHorizon = new Vector3(0.26f, 0.27f, 0.30f);
                _lights.Add((Vector3.Normalize(new Vector3(-0.5f, -0.6f, 0.75f)), MathF.Cos(0.5f), new Vector3(3.0f, 2.95f, 2.85f)));
                _lights.Add((Vector3.Normalize(new Vector3(0.8f, 0.3f, 0.35f)), MathF.Cos(0.35f), new Vector3(1.0f, 1.05f, 1.2f)));
                break;
        }
    }

    void SetupLamps()
    {
        _lamps.Clear();
        foreach (var l in _s.Lights ?? [])
        {
            if (!l.Enabled || l.Intensity <= 0) continue;
            static Vector3 V3(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
            var rgb = Photometry.KelvinToRgb(l.Kelvin);
            var lamp = new Lamp { Kind = l.Kind, Pos = V3(l.Position) };
            if (l.Kind == LightKind.Sun)
            {
                lamp.Dir = V3(l.TowardLight);
                lamp.Color = rgb * (float)(l.Intensity / Photometry.LuxPerUnit);
                lamp.CosSun = MathF.Cos((float)(Math.Clamp(l.Size, 0, 20) * Math.PI / 360));
            }
            else
            {
                lamp.Dir = V3(l.Axis);
                lamp.Radius = (float)Math.Max(l.Size, 0);
                double candela = l.Kind == LightKind.Spot ? Photometry.SpotCandela(l.Intensity, l.ConeAngle) : Photometry.PointCandela(l.Intensity);
                lamp.Color = rgb * (float)(candela / Photometry.LuxPerUnit);     // candela as tracer units; divided by distance² in metres when shading
                double half = Math.Clamp(l.ConeAngle, 1, 175) * Math.PI / 360;
                lamp.CosOuter = (float)Math.Cos(half);
                lamp.CosInner = (float)Math.Cos(half * (1 - Math.Clamp(l.Softness, 0, 1)));
            }
            _lamps.Add(lamp);
        }
    }

    Vector3 Sky(Vector3 d)
    {
        if (_environment != null) return _environment.Radiance(d, _s.EnvironmentRotation, _s.EnvironmentIntensity);
        float t = Math.Clamp(d.Z * 0.5f + 0.5f, 0, 1);
        t = MathF.Pow(t, 0.7f);
        return Vector3.Lerp(_skyHorizon, _skyZenith, t);
    }

    Vector3 Env(Vector3 d, bool withLights)
    {
        var c = Sky(d);
        if (withLights) foreach (var (dir, cosMax, rad) in _lights) if (Vector3.Dot(d, dir) >= cosMax) c += rad;
        return c;
    }

    // ---------------------------------------------------------------- ray queries
    bool Hit(Vector3 o, Vector3 d, float tMax, out float tHit, out int tri, out float bu, out float bv, bool anyHit)
    {
        tHit = tMax; tri = -1; bu = bv = 0;
        var inv = new Vector3(1f / (d.X == 0 ? 1e-30f : d.X), 1f / (d.Y == 0 ? 1e-30f : d.Y), 1f / (d.Z == 0 ? 1e-30f : d.Z));
        Span<int> stack = stackalloc int[64];
        int sp = 0; stack[sp++] = 0;
        while (sp > 0)
        {
            int id = stack[--sp];
            ref var nd = ref _nodes[id];
            var t0 = (nd.Min - o) * inv; var t1 = (nd.Max - o) * inv;
            float tmin = MathF.Max(MathF.Max(MathF.Min(t0.X, t1.X), MathF.Min(t0.Y, t1.Y)), MathF.Min(t0.Z, t1.Z));
            float tmax = MathF.Min(MathF.Min(MathF.Max(t0.X, t1.X), MathF.Max(t0.Y, t1.Y)), MathF.Max(t0.Z, t1.Z));
            if (tmax < MathF.Max(tmin, 0) || tmin > tHit) continue;
            if (nd.Count > 0)
            {
                for (int i = nd.Left; i < nd.Left + nd.Count; i++)
                {
                    int t = _order[i];
                    var a = V(t, 0); var e1 = V(t, 1) - a; var e2 = V(t, 2) - a;
                    var pv = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, pv);
                    if (MathF.Abs(det) < 1e-12f) continue;
                    float idet = 1f / det; var tv = o - a;
                    float u = Vector3.Dot(tv, pv) * idet; if (u < 0 || u > 1) continue;
                    var qv = Vector3.Cross(tv, e1); float v = Vector3.Dot(d, qv) * idet; if (v < 0 || u + v > 1) continue;
                    float tt = Vector3.Dot(e2, qv) * idet;
                    if (tt > 1e-4f && tt < tHit) { tHit = tt; tri = t; bu = u; bv = v; if (anyHit) return true; }
                }
            }
            else
            {
                int l = id + 1, r = _right[id];
                stack[sp++] = r; stack[sp++] = l;
            }
        }
        return tri >= 0;
    }

    // ---------------------------------------------------------------- sampling helpers
    struct Rng
    {
        ulong _s;
        public Rng(ulong seed) { _s = seed * 0x9E3779B97F4A7C15UL + 0x1234567UL; Next(); Next(); }
        public float Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return (_s >> 40) * (1f / (1 << 24)); }
    }

    static void Basis(Vector3 n, out Vector3 t, out Vector3 b)
    {
        t = MathF.Abs(n.Z) < 0.999f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, n)) : Vector3.UnitX;
        b = Vector3.Cross(n, t);
    }

    static Vector3 CosineHemisphere(Vector3 n, ref Rng rng)
    {
        float u1 = rng.Next(), u2 = rng.Next();
        float r = MathF.Sqrt(u1), ph = 2 * MathF.PI * u2;
        Basis(n, out var t, out var b);
        return Vector3.Normalize(t * (r * MathF.Cos(ph)) + b * (r * MathF.Sin(ph)) + n * MathF.Sqrt(MathF.Max(0, 1 - u1)));
    }

    static Vector3 RandomInCap(Vector3 axis, float cosMax, ref Rng rng)
    {
        float u1 = rng.Next(), u2 = rng.Next();
        float cosT = 1 - u1 * (1 - cosMax), sinT = MathF.Sqrt(MathF.Max(0, 1 - cosT * cosT)), ph = 2 * MathF.PI * u2;
        Basis(axis, out var t, out var b);
        return t * (sinT * MathF.Cos(ph)) + b * (sinT * MathF.Sin(ph)) + axis * cosT;
    }

    // ---------------------------------------------------------------- shading
    static float Smith(float cosine,float alpha)
        => 2*cosine/(cosine+MathF.Sqrt(alpha*alpha+(1-alpha*alpha)*cosine*cosine));
    static Vector3 Bsdf(Vector3 n,Vector3 view,Vector3 light,Vector3 albedo,float metal,float rough,out float pdf)
        => SurfaceScattering.Opaque(n, view, light, albedo, metal, rough, 0, 0, out pdf);
    static Vector3 LayeredBsdf(Vector3 n,Vector3 view,Vector3 light,Vector3 albedo,float metal,float rough,float coat,float coatRough,out float pdf)
        => SurfaceScattering.Coated(n, view, light, albedo, metal, rough, 0, 0, coat, coatRough, out pdf);
    static Vector3 EvaluateSurface(Vector3 n, Vector3 view, Vector3 light, Vector3 albedo, float metal, float rough, SurfaceStyle style, float etaI, float etaT, float transmission, out float pdf)
    {
        var opaque = SurfaceScattering.Coated(n, view, light, albedo, metal, rough, style.Anisotropy, style.AnisotropyAngle, style.Coat, style.CoatRoughness, out float opaquePdf);
        var glass = Vector3.Zero; float glassPdf = 0;
        if (transmission > 0) glass = SurfaceScattering.Dielectric(n, view, light, etaI, etaT, rough, out glassPdf);
        pdf = (1 - transmission) * opaquePdf + transmission * glassPdf;
        return (1 - transmission) * opaque + transmission * glass;
    }
    static Vector3 SampleGgx(Vector3 n,Vector3 view,float rough,ref Rng rng)
    {
        float a=MathF.Max(rough*rough,.0036f),u=rng.Next();
        float cosine=MathF.Sqrt((1-u)/(1+(a*a-1)*u)),sine=MathF.Sqrt(MathF.Max(0,1-cosine*cosine)),phi=2*MathF.PI*rng.Next();
        Basis(n,out var tangent,out var bitangent);
        var h=tangent*(sine*MathF.Cos(phi))+bitangent*(sine*MathF.Sin(phi))+n*cosine;
        return -view+2*Vector3.Dot(view,h)*h;
    }
    static float PowerWeight(float first,float second) => first*first/(first*first+second*second+1e-30f);

    static readonly SurfaceStyle DefaultSurface = new();
    static float Luma(Vector3 c) => .2126f*c.X+.7152f*c.Y+.0722f*c.Z;

    // mode 0: normal. 1: the lit ground only (shadow catcher). 2: the same ground with the model removed, i.e. what it would look like unshadowed.
    Vector3 Trace(Vector3 o, Vector3 d, ref Rng rng, out float alpha, int mode = 0)
    {
        alpha=1;var throughput=Vector3.One;var radiance=Vector3.Zero;float previousPdf=0;
        Span<int> media = stackalloc int[16]; int mediumCount = 0;
        float size=(_sceneMax-_sceneMin).Length();
        for(int depth=0;depth<=_s.Bounces;depth++)
        {
            float distance=0,u=0,v=0;int triangle=-1;
            bool hit=mode!=2&&Hit(o,d,float.MaxValue,out distance,out triangle,out u,out v,false);
            float groundDistance=float.MaxValue;
            if(_s.Ground&&d.Z< -1e-5f){groundDistance=(_groundZ-o.Z)/d.Z;if(groundDistance<=1e-4f)groundDistance=float.MaxValue;}
            bool ground=groundDistance<(hit?distance:float.MaxValue);
            if(!hit&&!ground)
            {
                if(depth==0)
                {
                    if(_s.Background!=RenderBg.Environment){alpha=0;return Vector3.Zero;}
                    return Sky(d)*(_s.Env==RenderEnv.Studio?1.5f:1f);
                }
                radiance+=throughput*Sky(d)*(_environment == null || previousPdf == 0 ? 1 : PowerWeight(previousPdf, _environment.Pdf(d, _s.EnvironmentRotation)));
                foreach(var (direction,cosMax,light) in _lights)
                    if(Vector3.Dot(d,direction)>=cosMax)
                        radiance+=throughput*light*PowerWeight(previousPdf,1/(2*MathF.PI*(1-cosMax)));
                break;
            }
            Vector3 position,n,albedo,geometric=Vector3.UnitZ;float metal,rough; int materialIndex = -1;
            SurfaceStyle style = DefaultSurface;
            if(ground)
            {
                position=o+d*groundDistance;n=Vector3.UnitZ;metal=0;rough=.9f;
                albedo=new Vector3(_s.Env==RenderEnv.DarkStudio?.12f:.65f);
            }
            else
            {
                position=o+d*distance;
                var normal=Nrm(triangle,0)*(1-u-v)+Nrm(triangle,1)*u+Nrm(triangle,2)*v;
                if(normal.LengthSquared()<1e-12f)normal=Vector3.Cross(V(triangle,1)-V(triangle,0),V(triangle,2)-V(triangle,0));
                n=Vector3.Normalize(normal);
                geometric = Vector3.Normalize(Vector3.Cross(V(triangle,1)-V(triangle,0),V(triangle,2)-V(triangle,0)));
                if (Vector3.Dot(geometric,n)<0) geometric=-geometric;
                materialIndex = _m[triangle];
                var material=_mats[_m[triangle]];metal=material.Metal;rough=material.Rough;
                float variation=MaterialSurface.Variation(material.Pattern,position,material.Origin,material.Scale,material.Angle);
                albedo=material.Pattern==SurfacePattern.Plain?material.Albedo:MaterialSurface.LinearColor(material.Color,variation);
                if(material.Pattern==SurfacePattern.Cast)rough=MathF.Max(rough,.65f);
                var surface=material.Layers.Evaluate(position-material.Origin,n,SurfaceLayers.Srgb(material.Color)*variation,metal,rough);
                albedo=new(MathF.Pow(surface.Color.X,2.2f),MathF.Pow(surface.Color.Y,2.2f),MathF.Pow(surface.Color.Z,2.2f));
                metal=surface.Metal;rough=surface.Roughness;n=surface.Normal;
                style=material.Layers.Style;
            }
            if(depth==0&&ground&&mode==0&&Catcher)
            {
                // Shadow catcher: the ground is not drawn, it only darkens the background by as much as the model shadows it.
                var lit=rng;var unlit=rng;
                float shadowed=Luma(Trace(o,d,ref lit,out _,1)),open=Luma(Trace(o,d,ref unlit,out _,2));
                rng=lit;
                alpha=open>1e-6f?1-Math.Clamp(shadowed/open,0,1):0;
                return Vector3.Zero;
            }
            bool entering = Vector3.Dot(geometric, d) < 0;
            var boundaryNormal = entering ? geometric : -geometric;
            if(Vector3.Dot(n,d)>0)n=-n;
            float transmission = style.Transmission * (1 - metal);
            float etaI = mediumCount > 0 ? _mats[media[mediumCount - 1]].Layers.Style.Ior : (!entering && transmission > 0 ? style.Ior : 1);
            float etaT = entering ? style.Ior : mediumCount > 1 ? _mats[media[mediumCount - 2]].Layers.Style.Ior : 1;
            if (mediumCount > 0 || (!entering && transmission > 0))
            {
                var medium = mediumCount > 0 ? _mats[media[mediumCount - 1]].Layers.Style : style;
                throughput *= SurfaceScattering.Absorption(medium.AbsorptionColor, ground ? groundDistance : distance, medium.AbsorptionDistance);
            }
            var view=-d;float epsilon=1e-5f*MathF.Max(size,1)+.001f;var origin=position+boundaryNormal*epsilon;
            // Sample the softboxes/sun for both dielectric and metal BRDFs. MIS also
            // weights BSDF paths that see those lights, avoiding double-counting.
            if (_environment != null)
            {
                var wi = _environment.Sample(rng.Next(), rng.Next(), rng.Next(), out float lightPdf, _s.EnvironmentRotation);
                float nl = Math.Abs(Vector3.Dot(n, wi));
                if (nl > 0 && !Hit(Vector3.Dot(n,wi)<0?position-boundaryNormal*epsilon:origin, wi, float.MaxValue, out _, out _, out _, out _, true))
                {
                    var f = EvaluateSurface(n, view, wi, albedo, metal, rough, style, etaI, etaT, transmission, out float bsdfPdf);
                    radiance += throughput * f * Sky(wi) * (nl / lightPdf * PowerWeight(lightPdf, bsdfPdf));
                }
            }
            foreach(var (direction,cosMax,light) in _lights)
            {
                var wi=RandomInCap(direction,cosMax,ref rng);float nl=Math.Abs(Vector3.Dot(n,wi));
                if(nl<=0||Hit(Vector3.Dot(n,wi)<0?position-boundaryNormal*epsilon:origin,wi,float.MaxValue,out _,out _,out _,out _,true))continue;
                var f=EvaluateSurface(n,view,wi,albedo,metal,rough,style,etaI,etaT,transmission,out float bsdfPdf);
                float lightPdf=1/(2*MathF.PI*(1-cosMax));
                radiance+=throughput*f*light*(nl/lightPdf*PowerWeight(lightPdf,bsdfPdf));
            }
            foreach(var lamp in _lamps)
            {
                Vector3 wi,irradiance;float reach=float.MaxValue;
                if(lamp.Kind==LightKind.Sun){wi=RandomInCap(lamp.Dir,lamp.CosSun,ref rng);irradiance=lamp.Color;}
                else
                {
                    // a glowing sphere: pick a point on it so the shadow edge softens with its size
                    float zc=2*rng.Next()-1,az=2*MathF.PI*rng.Next(),rr=MathF.Sqrt(MathF.Max(0,1-zc*zc));
                    var toLamp=lamp.Pos+new Vector3(rr*MathF.Cos(az),rr*MathF.Sin(az),zc)*lamp.Radius-origin;
                    float dist=toLamp.Length();if(dist<1e-6f)continue;
                    wi=toLamp/dist;reach=dist-1e-3f;
                    // inverse-square law, distance in metres (model units are millimetres); clamped inside the lamp's own radius
                    float metres=MathF.Max(dist,lamp.Radius)*(float)Photometry.MetresPerModelUnit;
                    irradiance=lamp.Color/(metres*metres);
                    if(lamp.Kind==LightKind.Spot)
                    {
                        float spot=Photometry.SpotFalloff(Vector3.Dot(-wi,lamp.Dir),lamp.CosOuter,lamp.CosInner);
                        if(spot<=0)continue;irradiance*=spot;
                    }
                }
                float nl=Math.Abs(Vector3.Dot(n,wi));
                if(nl<=0||mode!=2&&Hit(Vector3.Dot(n,wi)<0?position-boundaryNormal*epsilon:origin,wi,reach,out _,out _,out _,out _,true))continue;
                // delta-like lights have no MIS partner: reflected radiance = f * E * cos
                radiance+=throughput*EvaluateSurface(n,view,wi,albedo,metal,rough,style,etaI,etaT,transmission,out _)*irradiance*nl;
            }
            bool transmitted = false, delta = false; Vector3 next;
            ScatteredRay sample = default;
            if (transmission > 0 && rng.Next() < transmission)
            {
                sample = SurfaceScattering.SampleDielectric(n, view, etaI, etaT, rough, rng.Next(), rng.Next(), rng.Next());
                if (sample.Pdf <= 0 || sample.Direction.LengthSquared() < .5f) break;
                next = sample.Direction; transmitted = sample.Transmitted; delta = sample.Delta;
            }
            else next = SurfaceScattering.SampleOpaque(n, view, metal, rough, style.Anisotropy, style.AnisotropyAngle, style.Coat, style.CoatRoughness, rng.Next(), rng.Next(), rng.Next());
            float cosine=Math.Abs(Vector3.Dot(n,next));if(cosine<=0)break;
            if (transmitted != (Vector3.Dot(boundaryNormal, next) < 0)) break;
            if (delta) { throughput *= sample.Weight; previousPdf = 0; }
            else
            {
                var bsdf=EvaluateSurface(n,view,next,albedo,metal,rough,style,etaI,etaT,transmission,out float pdf);if(pdf<1e-15f)break;
                throughput*=bsdf*(cosine/pdf);previousPdf=pdf;
            }
            if (transmitted)
            {
                if (entering) { if (mediumCount == media.Length) break; media[mediumCount++] = materialIndex; }
                else if (mediumCount > 0) mediumCount--;
                origin = position - boundaryNormal * epsilon;
            }
            if(!float.IsFinite(throughput.X)||!float.IsFinite(throughput.Y)||!float.IsFinite(throughput.Z))break;
            if(depth>=2){float survival=Math.Clamp(MathF.Max(throughput.X,MathF.Max(throughput.Y,throughput.Z)),.1f,.95f);if(rng.Next()>survival)break;throughput/=survival;}
            o=origin;d=Vector3.Normalize(next);
        }
        return radiance;
    }

    /// <summary>Mean linear radiance (before tone mapping and exposure) along one ray. Used to check the lighting numerically.</summary>
    public Vector3 ProbeRadiance(Vector3 origin, Vector3 direction, int samples)
    {
        var rng = new Rng(12345); var sum = Vector3.Zero;
        for (int i = 0; i < samples; i++) sum += Trace(origin, Vector3.Normalize(direction), ref rng, out _);
        return sum / samples;
    }

    // ---------------------------------------------------------------- the render loop
    /// <summary>Progressive render. onPass receives the picture (BGRA bytes, premultiplied for transparency off) after each pass.</summary>
    public void Render(Camera cam, Action<byte[], int, int, int, bool> onPass, CancellationToken ct)
    {
        int W = _s.Width, H = _s.Height;
        var acc = new Vector3[W * H]; var accA = new float[W * H];
        var squares = _s.Denoise ? new float[W * H] : [];
        var guides = _s.Denoise ? new DenoiseGuide[W * H] : [];
        var pix = new byte[W * H * 4];
        double aspect = (double)W / H, tanH = Math.Tan(cam.FovY / 2);
        var eye = cam.Eye; var F = cam.Forward; var R = cam.Right; var U = cam.Up;
        Vector3 Vf(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
        var vEye = Vf(eye); var vF = Vf(F); var vR = Vf(R); var vU = Vf(U);
        float hh = (float)(cam.Distance * tanH);
        int passes = Math.Max(1, _s.Samples);
        // passes get bigger as the picture settles (1,1,2,4...), so something appears at once
        int done = 0, step = 1;
        while (done < passes && !ct.IsCancellationRequested)
        {
            int n = Math.Min(step, passes - done);
            int baseSeed = done;
            Parallel.For(0, H, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, y =>
            {
                var rng = new Rng((ulong)(y * 7919 + baseSeed * 104729 + 17));
                for (int x = 0; x < W; x++)
                {
                    int pixel = y * W + x;
                    if (_s.Denoise && done == 0)
                    {
                        float gx = (float)((x + .5) / W * 2 - 1), gy = (float)(1 - (y + .5) / H * 2);
                        var go = cam.Orthographic ? vEye + vR * (gx * hh * (float)aspect) + vU * (gy * hh) : vEye;
                        var gd = cam.Orthographic ? vF : Vector3.Normalize(vF + vR * (gx * (float)(tanH * aspect)) + vU * (gy * (float)tanH));
                        guides[pixel] = PrimaryGuide(go, gd);
                    }
                    var sum = Vector3.Zero; float sa = 0, square = 0;
                    for (int s = 0; s < n; s++)
                    {
                        float px = x + rng.Next(), py = y + rng.Next();
                        float nx = (float)(px / W * 2 - 1), ny = (float)(1 - py / H * 2);
                        Vector3 o, d;
                        if (cam.Orthographic) { o = vEye + vR * (nx * hh * (float)aspect) + vU * (ny * hh); d = vF; }
                        else { o = vEye; d = Vector3.Normalize(vF + vR * (nx * (float)(tanH * aspect)) + vU * (ny * (float)tanH)); }
                        var c = Trace(o, d, ref rng, out float a);
                        // tame rare very bright samples ("fireflies")
                        float mx = MathF.Max(c.X, MathF.Max(c.Y, c.Z)); if (_environment == null && mx > 12f) c *= 12f / mx;
                        sum += c; sa += a; if (_s.Denoise) { float l = Luma(c); square += l * l; }
                    }
                    acc[pixel] += sum; accA[pixel] += sa; if (_s.Denoise) squares[pixel] += square;
                }
            });
            done += n; step = Math.Min(step * 2, 16);
            Vector3[]? filtered = null;
            if (_s.Denoise && done >= passes && done > 1)
            {
                var mean = new Vector3[W * H]; var coverage = new float[W * H]; var variance = new float[W * H];
                for (int i = 0; i < mean.Length; i++)
                {
                    coverage[i] = accA[i] / done; mean[i] = accA[i] > 0 ? acc[i] / accA[i] : Vector3.Zero;
                    float l = Luma(acc[i] / done); variance[i] = Math.Max(0, squares[i] / done - l * l) / done;
                }
                filtered = RenderDenoiser.Filter(mean, coverage, variance, guides, W, H, ct);
            }
            Resolve(acc, accA, done, pix, W, H, filtered);
            onPass(pix, W, H, done, done >= passes);
        }
    }

    DenoiseGuide PrimaryGuide(Vector3 origin, Vector3 direction)
    {
        bool hit=Hit(origin, direction, float.MaxValue, out float t, out int tri, out float u, out float v, false);
        float ground=_s.Ground && direction.Z < -1e-5f ? (_groundZ-origin.Z)/direction.Z : float.MaxValue;
        if (ground>1e-4f && ground<(hit?t:float.MaxValue)) return new(Vector3.UnitZ, ground, _mats.Count);
        if (!hit) return new(Vector3.Zero, 0, -1);
        var n = Nrm(tri, 0) * (1 - u - v) + Nrm(tri, 1) * u + Nrm(tri, 2) * v;
        if (n.LengthSquared() < 1e-12f) return new(Vector3.Zero, t, -1);
        return new(Vector3.Normalize(n), t, _m[tri]);
    }
    void Resolve(Vector3[] acc, float[] accA, int samples, byte[] pix, int W, int H, Vector3[]? filtered = null)
    {
        float ex = (float)_s.Exposure;
        for (int i = 0; i < W * H; i++)
        {
            float a = accA[i] / samples;
            // Accumulation includes zero for uncovered samples. Resolve the covered colour
            // before tone mapping so alpha is not applied twice at antialiased edges.
            var c = (filtered != null ? filtered[i] : a > 0 ? acc[i] / (samples * a) : Vector3.Zero) * ex;
            // ACES-like filmic curve, then sRGB
            c = new Vector3(Aces(c.X), Aces(c.Y), Aces(c.Z));
            byte B(float v) => (byte)Math.Clamp((int)(MathF.Pow(Math.Clamp(v, 0, 1), 1f / 2.2f) * 255f + 0.5f), 0, 255);
            // premultiplied alpha, as the bitmap expects; every backdrop except Transparent is composited here
            var (br, bgn, bb) = Backdrop(i % W, i / W, W, H);
            float behind = 1 - a;
            pix[4 * i] = (byte)(B(c.Z) * a + bb * behind + .5f); pix[4 * i + 1] = (byte)(B(c.Y) * a + bgn * behind + .5f); pix[4 * i + 2] = (byte)(B(c.X) * a + br * behind + .5f);
            pix[4 * i + 3] = _s.Background == RenderBg.Transparent ? (byte)(a * 255f + 0.5f) : (byte)255;
        }
    }

    /// <summary>sRGB colour (0-255) behind the model at a pixel. Green is chroma-key green; the gradient is a soft studio sweep, lighter in the middle.</summary>
    (float r, float g, float b) Backdrop(int x, int y, int W, int H)
    {
        switch (_s.Background)
        {
            case RenderBg.Image:
                var c=BackgroundPlacement.Sample(_backplate!,x,y,W,H,_s.ImageFit,(float)_s.ImageOffsetX,(float)_s.ImageOffsetY)*255;
                return (c.X,c.Y,c.Z);
            case RenderBg.White: return (255, 255, 255);
            case RenderBg.Green: return (0, 177, 64);
            case RenderBg.Gradient:
            {
                float u = (x + .5f) / W - .5f, v = (y + .5f) / H - .5f;
                float t = Math.Clamp(MathF.Sqrt(u * u * (float)W / H + v * v * (float)H / W) * 1.15f, 0, 1);
                t = t * t * (3 - 2 * t);
                return (222 + (60 - 222) * t, 226 + (64 - 226) * t, 232 + (74 - 232) * t);
            }
            default: return (0, 0, 0);       // black, or transparent where the colour does not matter
        }
    }

    static float Aces(float x) => Math.Clamp((x * (2.51f * x + 0.03f)) / (x * (2.43f * x + 0.59f) + 0.14f), 0, 1);
}
