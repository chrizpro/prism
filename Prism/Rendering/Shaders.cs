namespace Prism.Rendering;

/// <summary>GLSL ES 3.00 sources (works on ANGLE/D3D11 on Windows and on desktop GL 3.3+ drivers).</summary>
static class Shaders
{
    const string Head = "#version 300 es\nprecision highp float;\nprecision highp int;\n";

    // ---------------------------------------------------------------- background gradient
    public const string BgVert = Head + @"
out vec2 vUv;
void main(){
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.999, 1.0);
}";
    public const string BgFrag = Head + @"
in vec2 vUv;
uniform vec3 uTop; uniform vec3 uBottom;
out vec4 o;
void main(){
    float t = clamp(vUv.y, 0.0, 1.0);
    vec3 c = mix(uBottom, uTop, smoothstep(0.0, 1.0, t));
    // very soft vignette keeps the eye on the model
    vec2 q = vUv - 0.5;
    c *= 1.0 - 0.18 * dot(q, q);
    o = vec4(c, 1.0);
}";

    // ---------------------------------------------------------------- reference pictures
    public const string ImgVert = Head + @"
uniform mat4 uVP; uniform vec3 uO; uniform vec3 uU; uniform vec3 uV; uniform vec2 uFlip;
out vec2 vUv;
void main(){
    vec2 p = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
    vec2 q = p - 0.5;
    vUv = vec2(mix(p.x, 1.0 - p.x, uFlip.x), mix(1.0 - p.y, p.y, uFlip.y));
    gl_Position = uVP * vec4(uO + uU * q.x + uV * q.y, 1.0);
}";
    public const string ImgFrag = Head + @"
in vec2 vUv; uniform sampler2D uTex; uniform float uAlpha;
out vec4 o;
void main(){ vec4 c = texture(uTex, vUv); o = vec4(c.rgb, c.a * uAlpha); }";

    // ---------------------------------------------------------------- shaded surfaces
    public const string MeshVert = Head + @"
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNrm;
layout(location=2) in float aVal;
uniform mat4 uVP;
out vec3 vPos; out vec3 vNrm; out float vVal;
void main(){
    vPos = aPos; vNrm = aNrm; vVal = aVal;
    gl_Position = uVP * vec4(aPos, 1.0);
}";
    public const string MeshFrag = Head + @"
in vec3 vPos; in vec3 vNrm; in float vVal;
uniform int uField;      // 0 none, 1 heat map of vVal over uFRange, 2 categories
uniform vec2 uFRange;
uniform int uPalette;    // categories: 0 distinct colours, 1 traffic light
vec3 ramp(float t){
    vec3 c0 = vec3(0.15, 0.25, 0.95), c1 = vec3(0.10, 0.80, 0.90), c2 = vec3(0.20, 0.85, 0.30), c3 = vec3(0.97, 0.90, 0.15), c4 = vec3(0.92, 0.18, 0.12);
    float x = clamp(t, 0.0, 1.0) * 4.0;
    if (x < 1.0) return mix(c0, c1, x);
    if (x < 2.0) return mix(c1, c2, x - 1.0);
    if (x < 3.0) return mix(c2, c3, x - 2.0);
    return mix(c3, c4, x - 3.0);
}
vec3 category(int id){
    if (uPalette == 1) {
        if (id == 0) return vec3(0.25, 0.78, 0.35);
        if (id == 1) return vec3(0.98, 0.85, 0.15);
        if (id == 2) return vec3(0.90, 0.15, 0.15);
        return vec3(0.45, 0.55, 0.80);
    }
    if (id <= 0) return vec3(0.62, 0.64, 0.68);
    int k = (id - 1) % 12;
    vec3 pal[12] = vec3[12](vec3(0.90,0.30,0.30), vec3(0.30,0.60,0.95), vec3(0.35,0.80,0.45), vec3(0.95,0.75,0.20), vec3(0.70,0.45,0.90), vec3(0.20,0.80,0.80),
                            vec3(0.95,0.55,0.25), vec3(0.55,0.75,0.25), vec3(0.90,0.45,0.70), vec3(0.45,0.50,0.90), vec3(0.75,0.60,0.40), vec3(0.40,0.70,0.60));
    return pal[k];
}
uniform vec3 uEye;       // camera position
uniform vec3 uRight; uniform vec3 uUp; uniform vec3 uToEye;   // camera basis (world space)
uniform vec3 uColor; uniform vec3 uBack;
uniform vec4 uTint;      // rgb + mix amount
uniform float uAlpha;
uniform vec2 uMat;       // metal, gloss
uniform int uPattern;
uniform vec2 uPatternParams; // size in mm, angle in radians
uniform vec3 uPatternOrigin;
uniform int uZebra;      // 0 normal, 1 zebra, 2 curvature, 3 draft
uniform vec4 uClip;      // plane normal.xyz, d  (hide where dot(n,p) > d)
uniform int uClipOn;
uniform vec3 uPull; uniform float uDraftSin; uniform float uCurvRange;
out vec4 o;
float surfaceVariation(){
    vec3 p=(vPos-uPatternOrigin)/max(uPatternParams.x,0.1);
    float c=cos(uPatternParams.y),s=sin(uPatternParams.y);p.xy=vec2(c*p.x+s*p.y,-s*p.x+c*p.y);
    if(uPattern==2){
        float warp=0.18*sin(p.x*0.8)+0.08*sin(p.x*3.1)+0.025*sin(p.x*11.0);
        float radius=length(vec2(p.y+warp,p.z*0.85+0.18));
        float irregular=sin(p.x*0.7+p.y*1.3)*sin(p.x*2.1+p.z*0.8);
        float phase=radius*24.0+1.2*irregular;
        float ring=0.5+0.5*sin(phase);
        float fp=p.y*180.0+sin(p.x*1.3)*5.0+p.z*7.0+irregular*4.0;
        float fiber=0.5+0.5*sin(fp);
        float bands=mix(pow(ring,12.0),0.161,smoothstep(0.5,3.0,fwidth(phase)));
        float pores=mix(pow(fiber,14.0),0.149,smoothstep(0.5,3.0,fwidth(fp)));
        return 0.98-0.25*bands-0.10*pores+0.04*irregular;
    }
    if(uPattern==3){float phase=p.y*160.0+0.6*sin(p.x*2.0);return 0.99+0.01*sin(phase)*(1.0-smoothstep(0.5,3.0,fwidth(phase)));}
    if(uPattern==4)return 0.91+0.09*sin(p.x*37.0+sin(p.y*41.0))*sin(p.z*39.0+p.y*43.0);
    if(uPattern==5){float a=sin(p.x*22.0),b=sin(p.y*22.0);return a*b>=0.0?0.65+0.35*abs(a):0.65+0.35*abs(b);}
    return 1.0;
}
void main(){
    if (uClipOn == 1 && dot(uClip.xyz, vPos) > uClip.w) discard;
    vec3 N = normalize(vNrm);
    vec3 Nraw = N;
    vec3 V = normalize(uEye - vPos);
    bool back = dot(N, V) < 0.0;
    if (back) N = -N;
    vec3 base = back ? uBack : uColor;
    if(uZebra==0&&uField==0)base=clamp(base*surfaceVariation(),0.0,1.0);

    vec3 L1 = normalize(uToEye * 0.75 - uRight * 0.55 + uUp * 0.75);
    vec3 L2 = normalize(uToEye * 0.60 + uRight * 0.85 - uUp * 0.10);
    float d1 = max(dot(N, L1), 0.0);
    float d2 = max(dot(N, L2), 0.0);
    float hemi = 0.5 + 0.5 * N.z;
    vec3 amb = mix(vec3(0.34, 0.33, 0.35), vec3(0.62, 0.66, 0.72), hemi);
    vec3 col = base * (amb * 0.62 + vec3(1.0, 0.98, 0.95) * d1 * 0.62 + vec3(0.8, 0.88, 1.0) * d2 * 0.22);

    vec3 H = normalize(L1 + V);
    float spec = pow(max(dot(N, H), 0.0), mix(14.0, 240.0, uMat.y)) * mix(0.10, 0.9, uMat.y);
    float rim = pow(1.0 - max(dot(N, V), 0.0), 3.0) * 0.10;
    col *= 1.0 - uMat.x;
    vec3 Rf = reflect(-V, N);
    vec3 env = mix(vec3(0.14, 0.14, 0.16), vec3(0.78, 0.85, 0.95), smoothstep(-0.35, 0.85, Rf.z));
    env += vec3(1.0,0.98,0.95) * smoothstep(mix(0.60,0.86,uMat.y),0.98,dot(Rf,L1))*1.2;
    env += vec3(0.7,0.8,1.0) * smoothstep(0.75,0.97,dot(Rf,L2))*0.5;
    vec3 f0=mix(vec3(0.04),base,uMat.x);
    vec3 fresnel=f0+(1.0-f0)*pow(1.0-max(dot(N,V),0.0),5.0);
    col += env * fresnel * (0.75 + 0.25 * uMat.y);
    spec *= mix(1.0, 1.6, uMat.x);
    col += vec3(spec) * mix(vec3(1.0), base * 1.3, uMat.x) + vec3(rim);

    if (uZebra == 1) {
        vec3 R = reflect(-V, N);
        float x = dot(R, normalize(vec3(0.62, 0.30, 0.72))) * 26.0;
        float s = smoothstep(-0.18, 0.18, sin(x));
        col = mix(vec3(0.04, 0.05, 0.07), vec3(0.97), s);
        col += spec;
    }
    if (uZebra == 2) {
        float k = vVal;      // smooth per-vertex mean curvature computed on the CPU (MeshCurvature)
        float r = uCurvRange > 0.0 ? uCurvRange : 0.08;
        float t = clamp(k / r, -1.0, 1.0);
        vec3 cNeg = vec3(0.12, 0.35, 0.95), cMid = vec3(0.25, 0.80, 0.35), cPos = vec3(0.92, 0.18, 0.15);
        vec3 c = t < 0.0 ? mix(cMid, cNeg, -t) : mix(cMid, cPos, t);
        col = c * (0.55 + 0.45 * d1) + vec3(spec) * 0.6;
    }
    if (uZebra == 3) {
        float a = dot(Nraw, uPull);
        vec3 c;
        if (abs(a) > 0.995) c = vec3(0.25, 0.45, 0.95);
        else if (a >= uDraftSin) c = vec3(0.25, 0.78, 0.35);
        else if (a >= 0.0) c = mix(vec3(0.98, 0.85, 0.15), vec3(0.98, 0.5, 0.12), 1.0 - a / max(uDraftSin, 1e-4));
        else c = vec3(0.9, 0.15, 0.15);
        col = c * (0.6 + 0.4 * d1) + vec3(spec) * 0.5;
    }
    if (uField != 0) {
        if (uField == 2 && uPalette == 2) {
            // selection highlight: untouched triangles keep their normal shading, selected ones glow orange
            if (vVal > 0.5) col = mix(col, vec3(1.0, 0.55, 0.12), 0.62) + vec3(0.04);
        } else {
            int pk = int(vVal + 0.5);
            vec3 fc = uField == 3 ? vec3(float((pk >> 16) & 255), float((pk >> 8) & 255), float(pk & 255)) / 255.0   // 3: packed 24-bit RGB (imported mesh colours)
                : uField == 1 ? ramp((vVal - uFRange.x) / (uFRange.y - uFRange.x)) : category(int(vVal + 0.5));
            col = fc * (0.62 + 0.38 * d1) + vec3(spec) * 0.35;
        }
    }
    col = mix(col, uTint.rgb, uTint.a);
    o = vec4(col, uAlpha);
}";

    // ---------------------------------------------------------------- thick anti-aliased lines (instanced quads)
    public const string LineVert = Head + @"
layout(location=0) in vec2 aCorner;   // x: 0=start 1=end, y: -1/+1 side
layout(location=1) in vec3 aP0;
layout(location=2) in vec3 aP1;
uniform mat4 uVP; uniform vec2 uViewport; uniform float uWidth; uniform float uBias;
out float vSide; out vec3 vWorld;
void main(){
    vWorld = mix(aP0, aP1, aCorner.x);
    vec4 c0 = uVP * vec4(aP0, 1.0);
    vec4 c1 = uVP * vec4(aP1, 1.0);
    const float eps = 1e-3;
    if (c0.w < eps && c1.w < eps) { gl_Position = vec4(2.0, 2.0, 2.0, 1.0); vSide = 0.0; return; }
    if (c0.w < eps) c0 = mix(c0, c1, (eps - c0.w) / (c1.w - c0.w));
    if (c1.w < eps) c1 = mix(c1, c0, (eps - c1.w) / (c0.w - c1.w));
    vec2 s0 = c0.xy / c0.w * uViewport * 0.5;
    vec2 s1 = c1.xy / c1.w * uViewport * 0.5;
    vec2 dir = s1 - s0;
    float len = length(dir);
    dir = len < 1e-4 ? vec2(1.0, 0.0) : dir / len;
    vec2 nrm = vec2(-dir.y, dir.x);
    vec4 c = mix(c0, c1, aCorner.x);
    float hw = uWidth * 0.5 + 0.75;
    vec2 offPx = nrm * aCorner.y * hw + dir * (aCorner.x * 2.0 - 1.0) * (uWidth * 0.5);
    gl_Position = c;
    gl_Position.xy += offPx / (uViewport * 0.5) * c.w;
    gl_Position.z -= uBias * c.w;
    vSide = aCorner.y * hw;
}";
    public const string LineFrag = Head + @"
in float vSide; in vec3 vWorld;
uniform vec4 uColor; uniform float uWidth; uniform vec4 uClip; uniform int uClipOn;
out vec4 o;
void main(){
    if (uClipOn == 1 && dot(uClip.xyz, vWorld) > uClip.w + 0.02) discard;
    float edge = uWidth * 0.5 + 0.75 - abs(vSide);   // distance (px) to the outer rim
    float a = clamp(edge / 1.5, 0.0, 1.0);
    o = vec4(uColor.rgb, uColor.a * a);
}";

    // ---------------------------------------------------------------- points (control points, snaps)
    public const string PointVert = Head + @"
layout(location=0) in vec3 aPos;
layout(location=1) in vec4 aColor;
layout(location=2) in float aSize;
uniform mat4 uVP; uniform float uScale;
out vec4 vColor;
void main(){
    vColor = aColor;
    gl_Position = uVP * vec4(aPos, 1.0);
    gl_Position.z -= 0.0005 * gl_Position.w;
    gl_PointSize = aSize * uScale;
}";
    public const string PointFrag = Head + @"
in vec4 vColor;
out vec4 o;
void main(){
    vec2 q = gl_PointCoord * 2.0 - 1.0;
    float d = length(q);
    if (d > 1.0) discard;
    float ring = smoothstep(0.62, 0.74, d);
    vec3 c = mix(vColor.rgb, vec3(1.0), ring);
    float a = 1.0 - smoothstep(0.92, 1.0, d);
    o = vec4(c, a * vColor.a);
}";

    // ---------------------------------------------------------------- flat translucent triangles (overlay fills)
    public const string FlatVert = Head + @"
layout(location=0) in vec3 aPos;
layout(location=1) in vec4 aColor;
uniform mat4 uVP;
out vec4 vColor;
void main(){ vColor = aColor; gl_Position = uVP * vec4(aPos, 1.0); gl_Position.z -= 0.0004 * gl_Position.w; }";
    public const string FlatFrag = Head + @"
in vec4 vColor;
out vec4 o;
void main(){ o = vColor; }";

    // ---------------------------------------------------------------- infinite-looking grid on the work plane
    public const string GridVert = Head + @"
layout(location=0) in vec3 aPos;
uniform mat4 uVP;
out vec3 vPos;
void main(){ vPos = aPos; gl_Position = uVP * vec4(aPos, 1.0); }";
    public const string GridFrag = Head + @"
in vec3 vPos;
uniform vec3 uOrigin; uniform vec3 uU; uniform vec3 uV;
uniform float uSpacing; uniform vec3 uCenter; uniform float uExtent;
uniform vec3 uAxisU; uniform vec3 uAxisV;
uniform vec3 uMinorCol; uniform vec3 uMajorCol; uniform vec2 uAlpha;
uniform float uDots; uniform float uGlow; uniform float uFade;
out vec4 o;
vec2 gridDist(vec2 c, float s){
    vec2 q = c / s;
    return abs(fract(q - 0.5) - 0.5) / max(fwidth(q), vec2(1e-6));
}
float lines(vec2 c, float s){
    vec2 g = gridDist(c, s);
    return 1.0 - min(min(g.x, g.y), 1.0);
}
float dots(vec2 c, float s, float r){
    vec2 q = c / s;
    vec2 f = (fract(q + 0.5) - 0.5) / max(fwidth(q), vec2(1e-6));
    return 1.0 - smoothstep(r - 0.6, r + 0.9, length(f));
}
float halo(vec2 c, float s){
    vec2 g = gridDist(c, s);
    return exp(-min(g.x, g.y) / 5.0);
}
void main(){
    vec3 d = vPos - uOrigin;
    vec2 c = vec2(dot(d, uU), dot(d, uV));
    float minor = uDots > 0.5 ? dots(c, uSpacing, 1.3) : lines(c, uSpacing);
    float major = uDots > 0.5 ? dots(c, uSpacing * 10.0, 2.6) : lines(c, uSpacing * 10.0);
    float fade = 1.0 - smoothstep(0.25 * uExtent * uFade, 0.9 * uExtent * uFade, length(vPos - uCenter));
    float a = max(minor * uAlpha.x, major * uAlpha.y);
    vec3 col = mix(uMinorCol, uMajorCol, clamp(major * 2.0, 0.0, 1.0));
    if (uGlow > 0.0 && uDots < 0.5) {
        float h = max(halo(c, uSpacing * 10.0) * uAlpha.y, halo(c, uSpacing) * uAlpha.x * 0.6) * uGlow * 0.28;
        a = max(a, h);
        col = mix(col, uMajorCol, clamp(h * 3.0, 0.0, 1.0));
    }
    // axes
    vec2 fw = max(fwidth(c), vec2(1e-6));
    float au = 1.0 - min(abs(c.y) / fw.y, 1.0);   // line along U (c.y == 0)
    float av = 1.0 - min(abs(c.x) / fw.x, 1.0);   // line along V (c.x == 0)
    vec3 outc = col; float outa = a;
    if (au > 0.0) { outc = mix(outc, uAxisU, au); outa = max(outa, au * 0.85); }
    if (av > 0.0) { outc = mix(outc, uAxisV, av); outa = max(outa, av * 0.85); }
    o = vec4(outc, outa * fade);
}";
}
