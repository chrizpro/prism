using System;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Prism.Rendering;

/// <summary>Bounded, embedded image decoded once per material/render; no I/O in shading.</summary>
public sealed class SurfaceImage
{
    public const int MaxBytes = 16 * 1024 * 1024;
    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }
    public SurfaceImage(int width, int height, byte[] rgba) { Width = width; Height = height; Rgba = rgba; }
    public static string Import(string path)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new ArgumentException("Choose an image smaller than 16 MB.");
        var data = Convert.ToBase64String(File.ReadAllBytes(path));
        _ = Decode(data); return data;
    }
    public static unsafe SurfaceImage Decode(string data)
    {
        if (data.Length > MaxBytes * 4L / 3 + 4) throw new ArgumentException("Image exceeds the 16 MB limit.");
        using var stream = new MemoryStream(Convert.FromBase64String(data));
        using var original = new Bitmap(stream);
        if (original.PixelSize.Width > 16384 || original.PixelSize.Height > 16384 || original.PixelSize.Width * (long)original.PixelSize.Height > 64_000_000)
            throw new ArgumentException("Image dimensions exceed 16384 pixels or 64 megapixels.");
        stream.Position = 0;
        using var bmp = original.PixelSize.Width >= original.PixelSize.Height
            ? Bitmap.DecodeToWidth(stream, Math.Min(original.PixelSize.Width, 2048))
            : Bitmap.DecodeToHeight(stream, Math.Min(original.PixelSize.Height, 2048));
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        // Drawing performs pixel format conversion on every supported Avalonia backend.
        using var target = new RenderTargetBitmap(new PixelSize(w, h), new Avalonia.Vector(96, 96));
        using (var dc = target.CreateDrawingContext()) dc.DrawImage(bmp, new Rect(new Size(w, h)));
        var bytes = new byte[w * h * 4];
        fixed (byte* p = bytes) target.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, bytes.Length, w * 4);
        bool bgra = target.Format == Avalonia.Platform.PixelFormat.Bgra8888;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            if (bgra) (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
            int a = bytes[i + 3];
            if (a is > 0 and < 255 && target.AlphaFormat == Avalonia.Platform.AlphaFormat.Premul)
                for (int k = 0; k < 3; k++) bytes[i + k] = (byte)Math.Min(255, bytes[i + k] * 255 / a);
            // Transparent pixels are composited on white, consistently for textures and backplates.
            for (int k = 0; k < 3; k++) bytes[i + k] = (byte)((bytes[i + k] * a + 255 * (255 - a) + 127) / 255);
            bytes[i + 3] = 255;
        }
        return new(w, h, bytes);
    }
    public Vector3 Sample(float u, float v, bool repeat = true)
    {
        u = repeat ? u - MathF.Floor(u) : Math.Clamp(u, 0, 1);
        v = repeat ? v - MathF.Floor(v) : Math.Clamp(v, 0, 1);
        float x = u * Width - .5f, y = v * Height - .5f;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        Vector3 Pixel(int px, int py)
        {
            px = repeat ? (px % Width + Width) % Width : Math.Clamp(px, 0, Width - 1);
            py = repeat ? (py % Height + Height) % Height : Math.Clamp(py, 0, Height - 1);
            int i = (py * Width + px) * 4; return new Vector3(Rgba[i], Rgba[i + 1], Rgba[i + 2]) / 255;
        }
        return Vector3.Lerp(Vector3.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), x - ix), Vector3.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), x - ix), y - iy);
    }
    public Vector3 Triplanar(Vector3 p, Vector3 n)
    {
        var w = Vector3.Abs(n); w *= w; w *= w; w /= Math.Max(w.X + w.Y + w.Z, 1e-6f);
        return Sample(p.Y, p.Z) * w.X + Sample(p.X, p.Z) * w.Y + Sample(p.X, p.Y) * w.Z;
    }
}
