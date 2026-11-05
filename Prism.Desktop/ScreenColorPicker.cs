using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Prism.Desktop;

/// <summary>Windows screen sampler. Freeze each monitor before showing overlays, so the picker never samples itself
/// and the selection click cannot activate an application underneath. No screenshots are saved.</summary>
static class ScreenColorPicker
{
    public static Task<uint?> Pick()
    {
        var result = new TaskCompletionSource<uint?>();
        var windows = new List<Window>(); var bitmaps = new List<WriteableBitmap>(); bool finishing = false;
        void Finish(uint? color)
        {
            if (finishing) return; finishing = true;
            foreach (var w in windows) w.Close();
            foreach (var b in bitmaps) b.Dispose();
            result.TrySetResult(color);
        }
        try
        {
            var probe = new Window();
            var screens = probe.Screens.All;
            foreach (var screen in screens)
            {
                var bounds = screen.Bounds; double scale = screen.Scaling;
                byte[] pixels = Capture(bounds);
                var bitmap = new WriteableBitmap(bounds.Size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                using (var fb = bitmap.Lock()) for (int y = 0; y < bounds.Height; y++) Marshal.Copy(pixels, y * bounds.Width * 4, fb.Address + y * fb.RowBytes, bounds.Width * 4);
                bitmaps.Add(bitmap);
                uint At(int x, int y)
                {
                    x = Math.Clamp(x, 0, bounds.Width - 1); y = Math.Clamp(y, 0, bounds.Height - 1); int i = (y * bounds.Width + x) * 4;
                    return 0xFF000000 | (uint)pixels[i + 2] << 16 | (uint)pixels[i + 1] << 8 | pixels[i];
                }
                var canvas = new Canvas();
                canvas.Children.Add(new Image { Source = bitmap, Width = bounds.Width / scale, Height = bounds.Height / scale, Stretch = Stretch.Fill });
                var magnifier = new Magnifier(At);
                var text = new TextBlock { Foreground = Brushes.White, FontSize = 13, Text = "Click a pixel · Esc cancels" };
                var stack = new StackPanel { Spacing = 6, Children = { magnifier, text } };
                var card = new Border { Background = Brushes.Black, Padding = new Thickness(10), CornerRadius = new CornerRadius(5), Child = stack, IsHitTestVisible = false };
                canvas.Children.Add(card); Canvas.SetLeft(card, 20); Canvas.SetTop(card, 20);
                var window = new Window { Title = "Pick screen color", SystemDecorations = SystemDecorations.None, CanResize = false, ShowInTaskbar = false, Topmost = true,
                    Position = bounds.Position, WindowStartupLocation = WindowStartupLocation.Manual, Width = bounds.Width / scale, Height = bounds.Height / scale,
                    Content = canvas, Cursor = new Cursor(StandardCursorType.Cross) };
                window.PointerMoved += (_, e) =>
                {
                    var point = e.GetPosition(canvas); int x = (int)Math.Floor(point.X * scale), y = (int)Math.Floor(point.Y * scale);
                    magnifier.X = x; magnifier.Y = y; magnifier.InvalidateVisual();
                    text.Text = $"#{At(x,y) & 0xFFFFFF:X6} · Click to pick\nEsc cancels";
                    Canvas.SetLeft(card, Math.Clamp(point.X + 24, 0, Math.Max(0, bounds.Width / scale - 210)));
                    Canvas.SetTop(card, Math.Clamp(point.Y + 24, 0, Math.Max(0, bounds.Height / scale - 185)));
                };
                window.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
                    var p = e.GetPosition(canvas); e.Handled = true; Finish(At((int)Math.Floor(p.X * scale), (int)Math.Floor(p.Y * scale)));
                };
                window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Finish(null); } };
                window.Closed += (_, _) => Finish(null);
                windows.Add(window);
            }
            foreach (var w in windows) w.Show();
            if (windows.Count == 0) Finish(null);
        }
        catch (Exception e) { Finish(null); return Task.FromException<uint?>(e); }
        return result.Task;
    }
    sealed class Magnifier(Func<int,int,uint> pixel) : Control
    {
        public int X, Y;
        public Magnifier() : this((_, _) => 0xFF000000) { }
        protected override Size MeasureOverride(Size availableSize) => new(110, 110);
        public override void Render(DrawingContext context)
        {
            for (int y = -5; y <= 5; y++) for (int x = -5; x <= 5; x++) context.FillRectangle(new SolidColorBrush(Color.FromUInt32(pixel(X + x, Y + y))), new Rect((x + 5) * 10, (y + 5) * 10, 10, 10));
            context.DrawRectangle(null, new Pen(Brushes.White, 2), new Rect(50, 50, 10, 10));
        }
    }
    static byte[] Capture(PixelRect bounds)
    {
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen); bitmap = CreateCompatibleBitmap(screen, bounds.Width, bounds.Height);
            if (screen == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new Win32Exception();
            old = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, 0x00CC0020 | 0x40000000)) throw new Win32Exception();
            SelectObject(memory, old); old = IntPtr.Zero;
            var info = new BitmapInfo { Size = 40, Width = bounds.Width, Height = -bounds.Height, Planes = 1, Bits = 32 };
            var pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
            if (GetDIBits(memory, bitmap, 0, (uint)bounds.Height, pixels, ref info, 0) == 0) throw new Win32Exception();
            return pixels;
        }
        finally { if (old != IntPtr.Zero) SelectObject(memory, old); if (bitmap != IntPtr.Zero) DeleteObject(bitmap); if (memory != IntPtr.Zero) DeleteDC(memory); if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen); }
    }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int X, Y; public uint Colors, Important; }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst,int x,int y,int w,int h,IntPtr src,int sx,int sy,uint rop);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc,IntPtr bitmap,uint start,uint count,[Out] byte[] bytes,ref BitmapInfo info,uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
}
