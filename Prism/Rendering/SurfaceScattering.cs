using System;
using System.Numerics;

namespace Prism.Rendering;

public readonly record struct ScatteredRay(Vector3 Direction, Vector3 Weight, float Pdf, bool Transmitted, bool Delta);

/// <summary>Anisotropic GGX reflection and dielectric reflection/transmission in radiance transport mode.</summary>
public static class SurfaceScattering
{
    public static void Frame(Vector3 n, float angle, out Vector3 t, out Vector3 b)
    {
        var x = MathF.Abs(n.Z) < .999f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, n)) : Vector3.UnitX;
        var y = Vector3.Cross(n, x); float a = angle * MathF.PI / 180;
        t = x * MathF.Cos(a) + y * MathF.Sin(a); b = Vector3.Cross(n, t);
    }
    static (float x, float y) Alphas(float roughness, float anisotropy)
    {
        float a = Math.Max(.0036f, roughness * roughness), aspect = MathF.Sqrt(1 - .9f * Math.Clamp(anisotropy, 0, .95f));
        return (a / aspect, a * aspect);
    }
    public static float Distribution(Vector3 n, Vector3 t, Vector3 b, Vector3 h, float ax, float ay)
    {
        float z = Vector3.Dot(n, h); if (z <= 0) return 0;
        float x = Vector3.Dot(t, h) / ax, y = Vector3.Dot(b, h) / ay, d = x * x + y * y + z * z;
        return 1 / (MathF.PI * ax * ay * d * d);
    }
    static float Mask(Vector3 n, Vector3 t, Vector3 b, Vector3 v, float ax, float ay)
    {
        float z = Math.Abs(Vector3.Dot(n, v)); if (z < 1e-8f) return 0;
        float x = Vector3.Dot(t, v) * ax, y = Vector3.Dot(b, v) * ay;
        return 2 / (1 + MathF.Sqrt(1 + (x * x + y * y) / (z * z)));
    }
    public static Vector3 SampleNormal(Vector3 n, float roughness, float anisotropy, float angle, float u, float v)
    {
        Frame(n, angle, out var t, out var b); var (ax, ay) = Alphas(roughness, anisotropy);
        float radius = MathF.Sqrt(Math.Clamp(u, 0, .999999f) / Math.Max(1e-6f, 1 - u)), phi = 2 * MathF.PI * v;
        return Vector3.Normalize(n + t * (ax * radius * MathF.Cos(phi)) + b * (ay * radius * MathF.Sin(phi)));
    }
    public static Vector3 Opaque(Vector3 n, Vector3 view, Vector3 light, Vector3 color, float metal, float roughness, float anisotropy, float angle, out float pdf)
    {
        pdf = 0; float nv = Vector3.Dot(n, view), nl = Vector3.Dot(n, light); var sum = view + light;
        if (nv <= 0 || nl <= 0 || sum.LengthSquared() < 1e-12f) return Vector3.Zero;
        var h = Vector3.Normalize(sum); float vh = Math.Max(Vector3.Dot(view, h), 1e-6f);
        Frame(n, angle, out var t, out var b); var (ax, ay) = Alphas(roughness, anisotropy);
        float d = Distribution(n, t, b, h, ax, ay), probability = .2f + .8f * metal;
        pdf = probability * d * Math.Max(Vector3.Dot(n, h), 0) / (4 * vh) + (1 - probability) * nl / MathF.PI;
        var f0 = Vector3.Lerp(new(.04f), color, metal); var f = f0 + (Vector3.One - f0) * MathF.Pow(1 - vh, 5);
        float transmit = .96f * (1 - MathF.Pow(1 - nv, 5)) * .96f * (1 - MathF.Pow(1 - nl, 5));
        return color * ((1 - metal) * transmit / MathF.PI) + f * (d * Mask(n, t, b, view, ax, ay) * Mask(n, t, b, light, ax, ay) / (4 * nv * nl));
    }
    public static Vector3 Coated(Vector3 n, Vector3 view, Vector3 light, Vector3 color, float metal, float roughness, float anisotropy, float angle, float coat, float coatRoughness, out float pdf)
    {
        var basis = Opaque(n, view, light, color, metal, roughness, anisotropy, angle, out pdf);
        if (coat <= 0) return basis;
        var top = Opaque(n, view, light, new(.04f), 1, coatRoughness, 0, 0, out float topPdf);
        float nv = Math.Max(0, Vector3.Dot(n, view)), nl = Math.Max(0, Vector3.Dot(n, light));
        float fv = .04f + .96f * MathF.Pow(1 - nv, 5), fl = .04f + .96f * MathF.Pow(1 - nl, 5);
        pdf = pdf * (1 - .25f * coat) + topPdf * (.25f * coat);
        return basis * ((1 - coat * fv) * (1 - coat * fl)) + top * coat;
    }
    public static Vector3 SampleOpaque(Vector3 n, Vector3 view, float metal, float roughness, float anisotropy, float angle, float coat, float coatRoughness, float choice, float u, float v)
    {
        float cp = .25f * coat;
        if (choice < cp || (choice - cp) / Math.Max(1 - cp, 1e-6f) < .2f + .8f * metal)
        {
            var h = SampleNormal(n, choice < cp ? coatRoughness : roughness, choice < cp ? 0 : anisotropy, choice < cp ? 0 : angle, u, v);
            return -view + 2 * Vector3.Dot(view, h) * h;
        }
        Frame(n, 0, out var t, out var b); float radius = MathF.Sqrt(u), phi = 2 * MathF.PI * v;
        return t * (radius * MathF.Cos(phi)) + b * (radius * MathF.Sin(phi)) + n * MathF.Sqrt(Math.Max(0, 1 - u));
    }
    public static float Fresnel(float cosine, float etaI, float etaT)
    {
        if (Math.Abs(etaI - etaT) < 1e-6f) return 0;
        cosine = Math.Clamp(Math.Abs(cosine), 0, 1); float ratio = etaI / etaT;
        float sin2 = ratio * ratio * (1 - cosine * cosine); if (sin2 >= 1) return 1;
        float ct = MathF.Sqrt(1 - sin2), rs = (etaI * cosine - etaT * ct) / (etaI * cosine + etaT * ct), rp = (etaT * cosine - etaI * ct) / (etaT * cosine + etaI * ct);
        return .5f * (rs * rs + rp * rp);
    }
    public static bool Refract(Vector3 view, Vector3 normal, float etaI, float etaT, out Vector3 direction)
    {
        float c = Vector3.Dot(view, normal), ratio = etaI / etaT, k = 1 - ratio * ratio * Math.Max(0, 1 - c * c);
        direction = Vector3.Zero; if (c <= 0 || k < 0) return false;
        direction = Vector3.Normalize(-ratio * view + (ratio * c - MathF.Sqrt(k)) * normal); return true;
    }
    public static bool Smooth(float roughness, float etaI, float etaT) => roughness <= .061f || Math.Abs(etaI - etaT) < 1e-6f;
    public static Vector3 Dielectric(Vector3 n, Vector3 view, Vector3 light, float etaI, float etaT, float roughness, out float pdf)
    {
        pdf = 0; if (Smooth(roughness, etaI, etaT)) return Vector3.Zero;
        float nv = Vector3.Dot(n, view), nl = Vector3.Dot(n, light); if (nv <= 1e-8f || Math.Abs(nl) < 1e-8f) return Vector3.Zero;
        bool reflect = nl > 0; float eta = etaT / etaI; var sum = view + light * (reflect ? 1 : eta);
        if (sum.LengthSquared() < 1e-12f) return Vector3.Zero;
        var h = Vector3.Normalize(sum); if (Vector3.Dot(h, n) < 0) h = -h;
        float vh = Vector3.Dot(view, h), lh = Vector3.Dot(light, h); if (vh <= 0 || (reflect ? lh <= 0 : lh >= 0)) return Vector3.Zero;
        Frame(n, 0, out var t, out var b); var (ax, ay) = Alphas(roughness, 0);
        float d = Distribution(n, t, b, h, ax, ay), g = Mask(n, t, b, view, ax, ay) * Mask(n, t, b, light, ax, ay), f = Fresnel(vh, etaI, etaT), normalPdf = d * Math.Max(Vector3.Dot(n, h), 0);
        if (reflect) { pdf = normalPdf * f / (4 * vh); return new(f * d * g / (4 * nv * nl)); }
        float denominator = lh + vh / eta; denominator *= denominator;
        if (denominator < 1e-16f) return Vector3.Zero;
        pdf = normalPdf * Math.Abs(lh) / denominator * (1 - f);
        return new((1 - f) * d * g * Math.Abs(lh * vh / (nv * nl * denominator)) / (eta * eta));
    }
    public static ScatteredRay SampleDielectric(Vector3 n, Vector3 view, float etaI, float etaT, float roughness, float choice, float u, float v)
    {
        bool delta = Smooth(roughness, etaI, etaT); var h = delta ? n : SampleNormal(n, roughness, 0, 0, u, v);
        float vh = Vector3.Dot(view, h); if (vh <= 0) return default;
        float f = Fresnel(vh, etaI, etaT); bool reflect = choice < f; Vector3 direction;
        if (reflect) direction = -view + 2 * vh * h;
        else if (!Refract(view, h, etaI, etaT, out direction)) return default;
        float nl = Vector3.Dot(n, direction); if (reflect ? nl <= 0 : nl >= 0) return default;
        if (delta) return new(direction, new(reflect ? 1 : etaI * etaI / (etaT * etaT)), reflect ? f : 1 - f, !reflect, true);
        var bsdf = Dielectric(n, view, direction, etaI, etaT, roughness, out float pdf);
        return pdf > 1e-15f ? new(direction, bsdf * (Math.Abs(nl) / pdf), pdf, !reflect, false) : default;
    }
    public static Vector3 Absorption(uint tint, float distance, float referenceDistance)
    {
        var color = SurfaceLayers.Srgb(tint); float d = Math.Max(0, distance) / Math.Max(.01f, referenceDistance);
        return new(MathF.Pow(Math.Max(color.X, .0001f), 2.2f * d), MathF.Pow(Math.Max(color.Y, .0001f), 2.2f * d), MathF.Pow(Math.Max(color.Z, .0001f), 2.2f * d));
    }
}
