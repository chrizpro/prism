using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace Prism.Rendering;

/// <summary>Embedded Radiance RGBE panorama and solid-angle-correct importance sampling. Z is up.</summary>
public sealed class HdrEnvironment
{
    public const int MaxBytes = 32 * 1024 * 1024;
    public int Width { get; }
    public int Height { get; }
    readonly Vector3[] _pixels;
    readonly double[] _cdf;
    readonly double _total;
    public HdrEnvironment(int width, int height, Vector3[] pixels)
    {
        if (width < 1 || height < 1 || pixels.Length != (long)width * height) throw new ArgumentException("Invalid HDR dimensions.");
        Width = width; Height = height; _pixels = pixels;
        _cdf = new double[pixels.Length]; double average = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            if (!float.IsFinite(p.X + p.Y + p.Z) || p.X < 0 || p.Y < 0 || p.Z < 0 || Math.Max(p.X, Math.Max(p.Y, p.Z)) > 1e6f)
                throw new ArgumentException("HDR radiance must be finite and between 0 and 1,000,000. Reduce the source exposure.");
            average += Luma(p) * SolidAngle(i / width);
        }
        average /= 4 * Math.PI;
        for (int i = 0; i < pixels.Length; i++)
        {
            double weight = average > 0 ? .95 * Luma(pixels[i]) + .05 * average : 1;
            _total += weight * SolidAngle(i / width); _cdf[i] = _total;
        }
    }
    static double Luma(Vector3 p) => .2126 * p.X + .7152 * p.Y + .0722 * p.Z;
    double SolidAngle(int y) => 2 * Math.PI / Width * (Math.Cos(Math.PI * y / Height) - Math.Cos(Math.PI * (y + 1) / Height));
    public static string Import(string path)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new ArgumentException("Choose a Radiance HDR smaller than 32 MB.");
        var data = Convert.ToBase64String(File.ReadAllBytes(path)); _ = Decode(data); return data;
    }
    public static HdrEnvironment Decode(string encoded)
    {
        if (encoded.Length > MaxBytes * 4L / 3 + 4) throw new ArgumentException("HDR exceeds the 32 MB limit.");
        using var stream = new MemoryStream(Convert.FromBase64String(encoded));
        string Line()
        {
            var sb = new StringBuilder(); int c;
            while ((c = stream.ReadByte()) >= 0 && c != 10) { if (c != 13) sb.Append((char)c); if (sb.Length > 4096) throw new ArgumentException("Invalid HDR header."); }
            return sb.ToString();
        }
        string signature = Line(); if (signature is not ("#?RADIANCE" or "#?RGBE")) throw new ArgumentException("Use a Radiance .hdr/.rgbe environment (EXR is not supported).");
        bool format = false; string line;
        while ((line = Line()).Length > 0) if (line == "FORMAT=32-bit_rle_rgbe") format = true;
        if (!format) throw new ArgumentException("Only RGBE Radiance HDR format is supported.");
        var size = Line().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (size.Length != 4 || size[0] is not ("-Y" or "+Y") || size[2] is not ("+X" or "-X") ||
            !int.TryParse(size[1], NumberStyles.None, CultureInfo.InvariantCulture, out int h) || !int.TryParse(size[3], NumberStyles.None, CultureInfo.InvariantCulture, out int w) ||
            w < 1 || h < 1 || w > 8192 || h > 4096 || (long)w * h > 16_777_216) throw new ArgumentException("HDR must use Y/X scanlines, up to 8192 × 4096 and 16 megapixels.");
        int Byte() { int b = stream.ReadByte(); if (b < 0) throw new ArgumentException("HDR image is truncated."); return b; }
        // Downsample linear radiance before building the sampling distribution.
        int factor = Math.Max(1, (int)Math.Ceiling(Math.Max(w / 2048d, h / 1024d))), dw = (w + factor - 1) / factor, dh = (h + factor - 1) / factor;
        var pixels = new Vector3[dw * dh]; var counts = new int[pixels.Length]; var row = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            int a = Byte(), b = Byte(), c = Byte(), d = Byte();
            if (w >= 8 && a == 2 && b == 2 && (c & 128) == 0)
            {
                if ((c << 8 | d) != w) throw new ArgumentException("HDR scanline width does not match its header.");
                for (int channel = 0; channel < 4; channel++)
                {
                    int x = 0;
                    while (x < w)
                    {
                        int n = Byte(); if (n == 0) throw new ArgumentException("Invalid HDR scanline run.");
                        int length = n > 128 ? n - 128 : n;
                        if (x + length > w) throw new ArgumentException("Invalid HDR scanline length.");
                        if (n > 128) { byte v = (byte)Byte(); for (int k = 0; k < length; k++) row[(x++) * 4 + channel] = v; }
                        else for (int k = 0; k < length; k++) row[(x++) * 4 + channel] = (byte)Byte();
                    }
                }
            }
            else
            {
                row[0] = (byte)a; row[1] = (byte)b; row[2] = (byte)c; row[3] = (byte)d;
                int x = 1, shift = 0;
                while (x < w)
                {
                    a = Byte(); b = Byte(); c = Byte(); d = Byte();
                    if (a == 1 && b == 1 && c == 1)
                    {
                        if (shift > 24) throw new ArgumentException("Invalid legacy HDR run.");
                        long run = (long)d << shift; if (run < 1 || run > w - x) throw new ArgumentException("Invalid legacy HDR run length.");
                        for (int k = 0; k < run; k++, x++) Array.Copy(row, (x - 1) * 4, row, x * 4, 4);
                        shift += 8;
                    }
                    else { row[x * 4] = (byte)a; row[x * 4 + 1] = (byte)b; row[x * 4 + 2] = (byte)c; row[x * 4 + 3] = (byte)d; x++; shift = 0; }
                }
            }
            for (int x = 0; x < w; x++)
            {
                int e = row[x * 4 + 3]; float scale = e == 0 ? 0 : MathF.ScaleB(1, e - 136);
                var value = new Vector3(row[x * 4], row[x * 4 + 1], row[x * 4 + 2]) * scale;
                if (!float.IsFinite(value.X + value.Y + value.Z) || Math.Max(value.X, Math.Max(value.Y, value.Z)) > 1e6f) throw new ArgumentException("HDR source radiance exceeds the supported range; reduce its exposure.");
                int px = size[2] == "+X" ? x : w - 1 - x, py = size[0] == "-Y" ? y : h - 1 - y, index = py / factor * dw + px / factor;
                pixels[index] += value; counts[index]++;
            }
        }
        for (int i = 0; i < pixels.Length; i++) pixels[i] /= counts[i];
        return new(dw, dh, pixels);
    }
    static Vector3 Rotate(Vector3 p, float angle) { float c = MathF.Cos(angle), s = MathF.Sin(angle); return new(c * p.X - s * p.Y, s * p.X + c * p.Y, p.Z); }
    (float u, float v) Uv(Vector3 direction, float rotation)
    {
        var d = Rotate(Vector3.Normalize(direction), -rotation * MathF.PI / 180);
        float u = MathF.Atan2(d.Y, d.X) / (2 * MathF.PI); if (u < 0) u += 1;
        return (u, MathF.Acos(Math.Clamp(d.Z, -1, 1)) / MathF.PI);
    }
    public Vector3 Radiance(Vector3 direction, float rotation = 0, float intensity = 1)
    {
        var (u, v) = Uv(direction, rotation); float x = u * Width - .5f, y = v * Height - .5f;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        Vector3 At(int px, int py) => _pixels[Math.Clamp(py, 0, Height - 1) * Width + (px % Width + Width) % Width];
        return Vector3.Lerp(Vector3.Lerp(At(ix, iy), At(ix + 1, iy), x - ix), Vector3.Lerp(At(ix, iy + 1), At(ix + 1, iy + 1), x - ix), y - iy) * intensity;
    }
    public float Pdf(Vector3 direction, float rotation = 0)
    {
        var (u, v) = Uv(direction, rotation); int x = Math.Clamp((int)(u * Width), 0, Width - 1), y = Math.Clamp((int)(v * Height), 0, Height - 1), i = y * Width + x;
        return (float)((_cdf[i] - (i > 0 ? _cdf[i - 1] : 0)) / (_total * SolidAngle(y)));
    }
    public Vector3 Sample(float choice, float azimuth, float elevation, out float pdf, float rotation = 0)
    {
        double target = Math.Clamp(choice, 0, .99999994f) * _total; int lo = 0, hi = _cdf.Length - 1;
        while (lo < hi) { int mid = (lo + hi) / 2; if (target < _cdf[mid]) hi = mid; else lo = mid + 1; }
        int y = lo / Width, x = lo % Width;
        double phi = 2 * Math.PI * (x + Math.Clamp(azimuth, 1e-5f, .99999f)) / Width;
        double z = Math.Cos(Math.PI * y / Height) + (Math.Cos(Math.PI * (y + 1) / Height) - Math.Cos(Math.PI * y / Height)) * Math.Clamp(elevation, 1e-5f, .99999f);
        float radius = (float)Math.Sqrt(Math.Max(0, 1 - z * z));
        var direction = Rotate(new(radius * (float)Math.Cos(phi), radius * (float)Math.Sin(phi), (float)z), rotation * MathF.PI / 180);
        pdf = (float)((_cdf[lo] - (lo > 0 ? _cdf[lo - 1] : 0)) / (_total * SolidAngle(y))); return direction;
    }
}
