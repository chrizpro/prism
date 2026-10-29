using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Prism.Rendering;

namespace Prism.Automation;

/// <summary>Captures the app window (Windows: PrintWindow, which works even when other windows overlap it).</summary>
static class WindowCapture
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage;
        public int XPels, YPels; public uint ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[]? bits, ref BITMAPINFOHEADER bi, uint usage);

    /// <summary>A small BGRA copy of part of the window (client pixels), for tab previews. Returns null when the window cannot be captured.</summary>
    public static (byte[] pixels, int width, int height)? CaptureThumbnail(Window? window, int x, int y, int cropWidth, int cropHeight, int maxWidth)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var hwnd = window?.TryGetPlatformHandle()?.Handle;
        if (hwnd is null) return null;
        GetClientRect(hwnd.Value, out var rc);
        int w = rc.R - rc.L, h = rc.B - rc.T;
        if (w <= 0 || h <= 0) return null;
        x = Math.Clamp(x, 0, w - 1); y = Math.Clamp(y, 0, h - 1);
        cropWidth = Math.Clamp(cropWidth, 1, w - x); cropHeight = Math.Clamp(cropHeight, 1, h - y);

        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!PrintWindow(hwnd.Value, mem, 3)) return null;
            var bi = new BITMAPINFOHEADER { Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), Width = w, Height = -h, Planes = 1, BitCount = 32 };
            var pixels = new byte[w * h * 4];
            SelectObject(mem, old);
            if (GetDIBits(mem, bmp, 0, (uint)h, pixels, ref bi, 0) != h) return null;
            int tw = Math.Min(maxWidth, cropWidth), th = Math.Max(1, cropHeight * tw / cropWidth);
            var thumb = new byte[tw * th * 4];
            for (int ty = 0; ty < th; ty++)
                for (int tx = 0; tx < tw; tx++)
                {
                    // box average over the source pixels this thumbnail pixel covers
                    int sx0 = x + tx * cropWidth / tw, sx1 = Math.Max(sx0 + 1, x + (tx + 1) * cropWidth / tw);
                    int sy0 = y + ty * cropHeight / th, sy1 = Math.Max(sy0 + 1, y + (ty + 1) * cropHeight / th);
                    int b = 0, g = 0, r = 0, n = 0;
                    for (int sy = sy0; sy < sy1 && sy < h; sy++)
                        for (int sx = sx0; sx < sx1 && sx < w; sx++) { int i = (sy * w + sx) * 4; b += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2]; n++; }
                    int o = (ty * tw + tx) * 4; n = Math.Max(n, 1);
                    thumb[o] = (byte)(b / n); thumb[o + 1] = (byte)(g / n); thumb[o + 2] = (byte)(r / n); thumb[o + 3] = 255;
                }
            return (thumb, tw, th);
        }
        catch { return null; }
        finally
        {
            SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public static byte[] CapturePng(Window? window)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Screenshots are only implemented on Windows.");
        var hwnd = window?.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("No window handle.");
        GetClientRect(hwnd, out var rc);
        int w = rc.R - rc.L, h = rc.B - rc.T;
        if (w <= 0 || h <= 0) throw new InvalidOperationException("Window has no client area (minimised?).");

        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        try
        {
            // PW_CLIENTONLY (1) | PW_RENDERFULLCONTENT (2): include DirectComposition / GPU content.
            if (!PrintWindow(hwnd, mem, 3)) throw new InvalidOperationException("Could not capture the application window.");
            var bi = new BITMAPINFOHEADER { Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), Width = w, Height = -h, Planes = 1, BitCount = 32 };
            var pixels = new byte[w * h * 4];
            // GetDIBits requires the bitmap not to be selected into a device context.
            SelectObject(mem, old);
            if (GetDIBits(mem, bmp, 0, (uint)h, pixels, ref bi, 0) != h) throw new InvalidOperationException("Could not read the captured window pixels.");
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return PngWriter.Encode(pixels, w, h);
        }
        finally
        {
            SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
