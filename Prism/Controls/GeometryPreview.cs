using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Prism.Rendering;

namespace Prism.Controls;

/// <summary>A cached, cancellable render of actual input geometry. A separate camera never moves the working viewport.</summary>
public sealed class GeometryPreview : Control
{
    public static readonly StyledProperty<StudioPreviewSource?> SourceProperty = AvaloniaProperty.Register<GeometryPreview, StudioPreviewSource?>(nameof(Source));
    public StudioPreviewSource? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    WriteableBitmap? _bitmap;
    CancellationTokenSource? _render;
    public Task PendingRender { get; private set; } = Task.CompletedTask;
    public bool HasRenderedImage => _bitmap != null;
    public string RenderStatus { get; private set; } = "Select an issue to inspect its geometry";

    public GeometryPreview()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => RequestRender();
        AttachedToVisualTree += (_, _) => RequestRender();
        DetachedFromVisualTree += (_, _) => { _render?.Cancel(); _bitmap?.Dispose(); _bitmap = null; };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty) RequestRender();
    }
    void RequestRender()
    {
        _render?.Cancel(); _render?.Dispose(); _render = new CancellationTokenSource();
        _bitmap?.Dispose(); _bitmap = null; InvalidateVisual();
        var source = Source;
        if (source == null) { RenderStatus = "No geometry available for this issue"; return; }
        RenderStatus = "Preparing geometry preview…";
        var token = _render.Token; bool light = ActualThemeVariant == ThemeVariant.Light;
        PendingRender = RenderAsync(source, light, token);
    }
    async Task RenderAsync(StudioPreviewSource source, bool light, CancellationToken token)
    {
        try
        {
            var result = await Task.Run(() => StudioPreview.Render(source, light, token), token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || !ReferenceEquals(Source, source)) return;
                var bitmap = new WriteableBitmap(new PixelSize(result.Width, result.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                using (var frame = bitmap.Lock())
                    for (int y = 0; y < result.Height; y++) Marshal.Copy(result.Bgra, y * result.Width * 4, frame.Address + y * frame.RowBytes, result.Width * 4);
                _bitmap?.Dispose(); _bitmap = bitmap; RenderStatus = source.Caption; InvalidateVisual();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is not OutOfMemoryException)
        { await Dispatcher.UIThread.InvokeAsync(() => { if (!token.IsCancellationRequested) { RenderStatus = "Preview unavailable"; InvalidateVisual(); } }); }
    }
    public override void Render(DrawingContext context)
    {
        if (_bitmap != null)
        {
            // Fill the thumbnail without stretching the geometry.
            double scale = Math.Max(Bounds.Width / _bitmap.PixelSize.Width, Bounds.Height / _bitmap.PixelSize.Height);
            double width = Bounds.Width / scale, height = Bounds.Height / scale;
            context.DrawImage(_bitmap, new Rect((_bitmap.PixelSize.Width - width) / 2, (_bitmap.PixelSize.Height - height) / 2, width, height), new Rect(Bounds.Size));
        }
        else
        {
            context.FillRectangle(new SolidColorBrush(ActualThemeVariant == ThemeVariant.Light ? 0xFFE0E7EFu : 0xFF25313Du), new Rect(Bounds.Size));
            var text = new FormattedText(RenderStatus, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.Gray) { MaxTextWidth = Math.Max(20, Bounds.Width - 24) };
            context.DrawText(text, new Point(12, Math.Max(8, (Bounds.Height - text.Height) / 2)));
        }
    }
}
