using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Prism.Core;

namespace Prism.Controls;

/// <summary>On-demand HSV wheel with keyboard adjustment; no permanent panel footprint.</summary>
public sealed class ColorWheel : Control
{
    public static readonly StyledProperty<uint> ColorProperty = AvaloniaProperty.Register<ColorWheel, uint>(nameof(Color), 0xFFFFFFFF);
    public uint Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public event EventHandler? ColorPicked;
    WriteableBitmap? _image;
    double _renderedValue = -1;
    public ColorWheel() { Focusable = true; Width = Height = 220; ClipToBounds = true; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change); if (change.Property == ColorProperty) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var (h, s, v) = ColorCode.Hsv(Color);
        if (_image == null || Math.Abs(v - _renderedValue) > .001)
        {
            _image?.Dispose(); _image = new WriteableBitmap(new PixelSize(220, 220), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            using var fb = _image.Lock(); var row = new byte[220 * 4];
            for (int y = 0; y < 220; y++)
            {
                Array.Clear(row);
                for (int x = 0; x < 220; x++)
                {
                    double dx = (x + .5 - 110) / 107, dy = (110 - y - .5) / 107, radius = Math.Sqrt(dx * dx + dy * dy);
                    if (radius > 1) continue;
                    uint c = ColorCode.FromHsv(Math.Atan2(dy, dx) * 180 / Math.PI, radius, v);
                    row[x * 4] = (byte)c; row[x * 4 + 1] = (byte)(c >> 8); row[x * 4 + 2] = (byte)(c >> 16); row[x * 4 + 3] = 255;
                }
                Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
            }
            _renderedValue = v;
        }
        context.DrawImage(_image, new Rect(Bounds.Size));
        double a = h * Math.PI / 180, radiusPx = Math.Min(Bounds.Width, Bounds.Height) / 2 - 3;
        var point = new Point(Bounds.Width / 2 + Math.Cos(a) * s * radiusPx, Bounds.Height / 2 - Math.Sin(a) * s * radiusPx);
        context.DrawEllipse(null, new Pen(Brushes.Black, 3), point, 5, 5);
        context.DrawEllipse(null, new Pen(Brushes.White, 1.5), point, 5, 5);
    }
    void Pick(Point point)
    {
        double dx = point.X - Bounds.Width / 2, dy = Bounds.Height / 2 - point.Y;
        double s = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / (Math.Min(Bounds.Width, Bounds.Height) / 2 - 3), 0, 1);
        var hsv = ColorCode.Hsv(Color);
        SetCurrentValue(ColorProperty, ColorCode.FromHsv(Math.Atan2(dy, dx) * 180 / Math.PI, s, hsv.V));
        ColorPicked?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); e.Pointer.Capture(this); Pick(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (e.Pointer.Captured == this) Pick(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); e.Pointer.Capture(null); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); var (h, s, v) = ColorCode.Hsv(Color);
        if (e.Key is Key.Left or Key.Right) h += e.Key == Key.Left ? -2 : 2;
        else if (e.Key is Key.Up or Key.Down) s = Math.Clamp(s + (e.Key == Key.Up ? .02 : -.02), 0, 1);
        else return;
        SetCurrentValue(ColorProperty, ColorCode.FromHsv(h, s, v)); ColorPicked?.Invoke(this, EventArgs.Empty); e.Handled = true;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _image?.Dispose(); _image = null; }
}
