using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Prism.Core;
using Prism.Core.Cam;

namespace Prism.Commands;

/// <summary>Carve a picture into the top of the material: raster passes whose depth follows the image's brightness, so the toolpath shows the pattern the bit will cut.</summary>
public sealed class ImageReliefCommand : CncOperationCommand
{
    readonly TextParam _file;
    readonly NumberParam _x, _y, _w, _step;
    readonly ChoiceParam _deep, _bit;

    public ImageReliefCommand()
    {
        _file = Add(new TextParam { Label = "Image file", Hint = "Path to a PNG, JPG, BMP or GIF" });
        _x = Num("X from", 0); _y = Num("Y from", 0);
        _w = Num("Width of the carving", 100, 1, 10000, 5);
        _step = Num("Stepover", 15, 1, 95, 1, "% of tool");
        _deep = Choice("Deepest where the image is", 0, "Dark", "Light");
        _bit = Choice("Bit", 0, "Ball nose", "Flat end mill");
        Dia.Value = 3; Depth.Value = 2; Feed.Value = 1000; Plunge.Value = 300; Rpm.Value = 16000;
    }

    public override string Title => "CNC Image Carve";
    public override string Icon => "cnc-image";
    public override string Hint => "Carve a picture into the top of the stock. Each pixel's brightness sets the depth, so the passes show the pattern the bit cuts. Use a ball nose bit for smooth relief. The carving is placed with its lower-left corner at X from / Y from and keeps the picture's proportions.";
    protected override string ToolName => _bit.Index == 0 ? $"{Dia.Value:0.##} mm ball nose end mill" : $"{Dia.Value:0.##} mm end mill";

    /// <summary>Read an image as brightness 0 (black) to 1 (white), composited over white.</summary>
    public static (int w, int h, float[] light) LoadBrightness(string path)
    {
        var bytesOnDisk = File.ReadAllBytes(path);
        if (bytesOnDisk.Length > 8 && bytesOnDisk[0] == 137 && bytesOnDisk[1] == 80 && TryDecodePng(bytesOnDisk) is { } png) return png;
        using var bmp = new Bitmap(path);
        var size = bmp.PixelSize;
        using var wb = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        bmp.CopyPixels(fb, AlphaFormat.Premul);
        var bytes = new byte[fb.RowBytes * size.Height];
        Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        var light = new float[size.Width * size.Height];
        for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                int i = y * fb.RowBytes + x * 4;
                double white = 255 - bytes[i + 3];                       // composite over white
                double l = (0.114 * (bytes[i] + white) + 0.587 * (bytes[i + 1] + white) + 0.299 * (bytes[i + 2] + white)) / 255;
                light[y * size.Width + x] = (float)Math.Clamp(l, 0, 1);
            }
        return (size.Width, size.Height, light);
    }

    /// <summary>Decode an ordinary PNG (8 bit gray, gray+alpha, RGB or RGBA, not interlaced) without the UI toolkit; null when it is another kind of PNG.</summary>
    static (int w, int h, float[] light)? TryDecodePng(byte[] data)
    {
        int pos = 8, w = 0, h = 0, type = 0; bool ok = false; var idat = new MemoryStream();
        while (pos + 8 <= data.Length)
        {
            int len = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
            string name = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            if (len < 0 || pos + 12 + len > data.Length) return null;
            if (name == "IHDR")
            {
                w = (data[pos + 8] << 24) | (data[pos + 9] << 16) | (data[pos + 10] << 8) | data[pos + 11];
                h = (data[pos + 12] << 24) | (data[pos + 13] << 16) | (data[pos + 14] << 8) | data[pos + 15];
                type = data[pos + 17]; ok = data[pos + 16] == 8 && data[pos + 20] == 0 && (type is 0 or 2 or 4 or 6);
            }
            else if (name == "IDAT") idat.Write(data, pos + 8, len);
            else if (name == "IEND") break;
            pos += 12 + len;
        }
        if (!ok || w <= 0 || h <= 0 || (long)w * h > 100_000_000) return null;
        int bpp = type switch { 0 => 1, 2 => 3, 4 => 2, _ => 4 }, stride = w * bpp;
        var raw = new byte[(stride + 1) * h];
        idat.Position = 0;
        using (var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress)) { int got = 0, n; while (got < raw.Length && (n = z.Read(raw, got, raw.Length - got)) > 0) got += n; if (got < raw.Length) return null; }
        var img = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)];
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? img[y * stride + x - bpp] : 0, b = y > 0 ? img[(y - 1) * stride + x] : 0, c = x >= bpp && y > 0 ? img[(y - 1) * stride + x - bpp] : 0, v = raw[y * (stride + 1) + 1 + x];
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                v += f switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => pa <= pb && pa <= pc ? a : pb <= pc ? b : c, _ => 0 };
                img[y * stride + x] = (byte)v;
            }
        }
        var light = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int o = i * bpp; double r, g, b2, al = 255;
            if (type is 0 or 4) { r = g = b2 = img[o]; if (type == 4) al = img[o + 1]; } else { r = img[o]; g = img[o + 1]; b2 = img[o + 2]; if (type == 6) al = img[o + 3]; }
            double white = 255 - al;
            light[i] = (float)Math.Clamp((0.299 * (al / 255 * r + white) + 0.587 * (al / 255 * g + white) + 0.114 * (al / 255 * b2 + white)) / 255, 0, 1);
        }
        return (w, h, light);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (string.IsNullOrWhiteSpace(_file.Value)) { Results.Add(new ResultLine("Image", "Type the path of the picture to carve.")); return null; }
        if (!File.Exists(_file.Value)) throw new FormatException("That image file does not exist: " + _file.Value);
        var (iw, ih, light) = LoadBrightness(_file.Value);
        double width = _w.Value, height = width * ih / iw, r = Radius;
        double step = Math.Max(0.1, Dia.Value * _step.Value / 100), pitch = Math.Max(0.2, Dia.Value / 4);
        if ((height / step + 1) * (width / pitch + 1) > 1_500_000) throw new FormatException("This carving would need too many moves. Make it smaller or use a larger stepover.");
        double Depth01(double x, double y)
        {
            int px = Math.Clamp((int)((x - _x.Value) / width * iw), 0, iw - 1), py = Math.Clamp((int)((1 - (y - _y.Value) / height) * ih), 0, ih - 1);
            double l = light[py * iw + px];
            return _deep.Index == 0 ? 1 - l : l;
        }
        Start(); Pass();
        bool fwd = true, first = true; int lines = 0;
        for (double y = _y.Value; y <= _y.Value + height + 1e-6; y += step)
        {
            double yy = Math.Min(y, _y.Value + height);
            int n = (int)Math.Ceiling(width / pitch);
            for (int k = 0; k <= n; k++)
            {
                double x = _x.Value + width * (fwd ? k : n - k) / n;
                double z = TopZ.Value - Depth.Value * Depth01(x, yy);
                if (first) { Go(x, yy, SafeZ, MoveKind.Rapid); Go(x, yy, z, MoveKind.Plunge); first = false; }
                else Go(x, yy, z, MoveKind.Cut);
            }
            fwd = !fwd; lines++;
            if (yy >= _y.Value + height) break;
        }
        Go(Last.x, Last.y, SafeZ, MoveKind.Rapid);
        Results.Add(new ResultLine("Carving", $"{width:0.#} × {height:0.#} mm from {iw} × {ih} pixels"));
        Results.Add(new ResultLine("Passes", lines.ToString()));
        return Output("CNC image carve", Seal("Image carve"));
    }
}
