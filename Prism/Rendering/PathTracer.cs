using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Rendering;

public enum RenderEnv { Studio, Sky, DarkStudio }
public enum RenderBg { Environment, White, Transparent }

public sealed class RenderSettings
{
    public int Width = 1280, Height = 720;
    public int Samples = 64;
    public int Bounces = 5;
    public RenderEnv Env = RenderEnv.Studio;
    public RenderBg Background = RenderBg.Environment;
    public bool Ground = true;
    public double Exposure = 1.0;
}

/// <summary>
/// A small CPU path tracer (no extra dependencies): soft shadows, reflections, ambient light from a studio or sky environment.
/// It renders in passes so the picture can be shown while it refines, and it can be stopped at any time.
/// </summary>
public sealed class PathTracer
{
    // ---------------------------------------------------------------- scene
    struct Mat { public Vector3 Albedo, Origin; public float Metal, Rough, Scale, Angle; public uint Color; public SurfacePattern Pattern; }
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

    readonly RenderSettings _s;
    public PathTracer(RenderSettings s) { _s = s; }

    public int TriangleCount => _tris;

    public bool LoadScene(CadSession session)
    {
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
                Color=o.Color,Origin=new((float)md.Min.X,(float)md.Min.Y,(float)md.Min.Z),Pattern=MaterialSurface.Pattern(o.Look),Scale=o.Look.PatternScale,Angle=o.Look.PatternAngle });
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
            default:
                _skyZenith = new Vector3(0.42f, 0.44f, 0.48f); _skyHorizon = new Vector3(0.26f, 0.27f, 0.30f);
                _lights.Add((Vector3.Normalize(new Vector3(-0.5f, -0.6f, 0.75f)), MathF.Cos(0.5f), new Vector3(3.0f, 2.95f, 2.85f)));
                _lights.Add((Vector3.Normalize(new Vector3(0.8f, 0.3f, 0.35f)), MathF.Cos(0.35f), new Vector3(1.0f, 1.05f, 1.2f)));
                break;
        }
    }

    Vector3 Sky(Vector3 d)
    {
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
    {
        pdf=0;float nv=Vector3.Dot(n,view),nl=Vector3.Dot(n,light);
        if(nv<=0||nl<=0)return Vector3.Zero;
        var sum=view+light;if(sum.LengthSquared()<1e-12f)return Vector3.Zero;
        var h=Vector3.Normalize(sum);float nh=MathF.Max(Vector3.Dot(n,h),0),vh=MathF.Max(Vector3.Dot(view,h),1e-6f);
        float alpha=MathF.Max(rough*rough,.0036f),a2=alpha*alpha,den=nh*nh*(a2-1)+1;
        float distribution=a2/(MathF.PI*den*den);
        var f0=Vector3.Lerp(new Vector3(.04f),albedo,metal);
        var fresnel=f0+(Vector3.One-f0)*MathF.Pow(1-vh,5);
        float specProbability=.2f+.8f*metal;
        pdf=specProbability*distribution*nh/(4*vh)+(1-specProbability)*nl/MathF.PI;
        // Transmission through the dielectric interface on both legs prevents
        // grazing-angle diffuse + specular energy gain in the layered model.
        float transmitView=.96f*(1-MathF.Pow(1-nv,5)),transmitLight=.96f*(1-MathF.Pow(1-nl,5));
        var diffuse=albedo*((1-metal)*transmitView*transmitLight/MathF.PI);
        return diffuse+fresnel*(distribution*Smith(nv,alpha)*Smith(nl,alpha)/(4*nv*nl));
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

    Vector3 Trace(Vector3 o, Vector3 d, ref Rng rng, out float alpha)
    {
        alpha=1;var throughput=Vector3.One;var radiance=Vector3.Zero;float previousPdf=0;
        float size=(_sceneMax-_sceneMin).Length();
        for(int depth=0;depth<=_s.Bounces;depth++)
        {
            bool hit=Hit(o,d,float.MaxValue,out float distance,out int triangle,out float u,out float v,false);
            float groundDistance=float.MaxValue;
            if(_s.Ground&&d.Z< -1e-5f){groundDistance=(_groundZ-o.Z)/d.Z;if(groundDistance<=1e-4f)groundDistance=float.MaxValue;}
            bool ground=groundDistance<(hit?distance:float.MaxValue);
            if(!hit&&!ground)
            {
                if(depth==0)
                {
                    if(_s.Background is RenderBg.White or RenderBg.Transparent){alpha=0;return Vector3.Zero;}
                    return Sky(d)*(_s.Env==RenderEnv.Studio?1.5f:1f);
                }
                radiance+=throughput*Sky(d);
                foreach(var (direction,cosMax,light) in _lights)
                    if(Vector3.Dot(d,direction)>=cosMax)
                        radiance+=throughput*light*PowerWeight(previousPdf,1/(2*MathF.PI*(1-cosMax)));
                break;
            }
            Vector3 position,n,albedo;float metal,rough;
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
                var material=_mats[_m[triangle]];metal=material.Metal;rough=material.Rough;
                float variation=MaterialSurface.Variation(material.Pattern,position,material.Origin,material.Scale,material.Angle);
                albedo=material.Pattern==SurfacePattern.Plain?material.Albedo:MaterialSurface.LinearColor(material.Color,variation);
                if(material.Pattern==SurfacePattern.Cast)rough=MathF.Max(rough,.65f);
            }
            if(Vector3.Dot(n,d)>0)n=-n;
            var view=-d;var origin=position+n*(1e-5f*MathF.Max(size,1)+.001f);
            // Sample the softboxes/sun for both dielectric and metal BRDFs. MIS also
            // weights BSDF paths that see those lights, avoiding double-counting.
            foreach(var (direction,cosMax,light) in _lights)
            {
                var wi=RandomInCap(direction,cosMax,ref rng);float nl=Vector3.Dot(n,wi);
                if(nl<=0||Hit(origin,wi,float.MaxValue,out _,out _,out _,out _,true))continue;
                var f=Bsdf(n,view,wi,albedo,metal,rough,out float bsdfPdf);
                float lightPdf=1/(2*MathF.PI*(1-cosMax));
                radiance+=throughput*f*light*(nl/lightPdf*PowerWeight(lightPdf,bsdfPdf));
            }
            var next=rng.Next()<.2f+.8f*metal?SampleGgx(n,view,rough,ref rng):CosineHemisphere(n,ref rng);
            float cosine=Vector3.Dot(n,next);if(cosine<=0)break;
            var bsdf=Bsdf(n,view,next,albedo,metal,rough,out float pdf);if(pdf<1e-15f)break;
            throughput*=bsdf*(cosine/pdf);previousPdf=pdf;
            if(!float.IsFinite(throughput.X)||!float.IsFinite(throughput.Y)||!float.IsFinite(throughput.Z))break;
            if(depth>=2){float survival=Math.Clamp(MathF.Max(throughput.X,MathF.Max(throughput.Y,throughput.Z)),.1f,.95f);if(rng.Next()>survival)break;throughput/=survival;}
            o=origin;d=Vector3.Normalize(next);
        }
        return radiance;
    }

    // ---------------------------------------------------------------- the render loop
    /// <summary>Progressive render. onPass receives the picture (BGRA bytes, premultiplied for transparency off) after each pass.</summary>
    public void Render(Camera cam, Action<byte[], int, int, int, bool> onPass, CancellationToken ct)
    {
        int W = _s.Width, H = _s.Height;
        var acc = new Vector3[W * H]; var accA = new float[W * H];
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
                    var sum = Vector3.Zero; float sa = 0;
                    for (int s = 0; s < n; s++)
                    {
                        float px = x + rng.Next(), py = y + rng.Next();
                        float nx = (float)(px / W * 2 - 1), ny = (float)(1 - py / H * 2);
                        Vector3 o, d;
                        if (cam.Orthographic) { o = vEye + vR * (nx * hh * (float)aspect) + vU * (ny * hh); d = vF; }
                        else { o = vEye; d = Vector3.Normalize(vF + vR * (nx * (float)(tanH * aspect)) + vU * (ny * (float)tanH)); }
                        var c = Trace(o, d, ref rng, out float a);
                        // tame rare very bright samples ("fireflies")
                        float mx = MathF.Max(c.X, MathF.Max(c.Y, c.Z)); if (mx > 12f) c *= 12f / mx;
                        sum += c; sa += a;
                    }
                    acc[y * W + x] += sum; accA[y * W + x] += sa;
                }
            });
            done += n; step = Math.Min(step * 2, 16);
            Resolve(acc, accA, done, pix, W, H);
            onPass(pix, W, H, done, done >= passes);
        }
    }

    void Resolve(Vector3[] acc, float[] accA, int samples, byte[] pix, int W, int H)
    {
        float ex = (float)_s.Exposure;
        for (int i = 0; i < W * H; i++)
        {
            float a = accA[i] / samples;
            // Accumulation includes zero for uncovered samples. Resolve the covered colour
            // before tone mapping so alpha is not applied twice at antialiased edges.
            var c = a > 0 ? acc[i] / (samples * a) * ex : Vector3.Zero;
            // ACES-like filmic curve, then sRGB
            c = new Vector3(Aces(c.X), Aces(c.Y), Aces(c.Z));
            byte B(float v) => (byte)Math.Clamp((int)(MathF.Pow(Math.Clamp(v, 0, 1), 1f / 2.2f) * 255f + 0.5f), 0, 255);
            // premultiplied alpha, as the bitmap expects
            float white = _s.Background == RenderBg.White ? 255 * (1 - a) : 0;
            pix[4 * i] = (byte)(B(c.Z) * a + white); pix[4 * i + 1] = (byte)(B(c.Y) * a + white); pix[4 * i + 2] = (byte)(B(c.X) * a + white);
            pix[4 * i + 3] = _s.Background == RenderBg.White ? (byte)255 : (byte)(a * 255f + 0.5f);
        }
    }

    static float Aces(float x) => Math.Clamp((x * (2.51f * x + 0.03f)) / (x * (2.43f * x + 0.59f) + 0.14f), 0, 1);
}
