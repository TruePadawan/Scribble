using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Utils;
using Scribble.State;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Controls;

/// <summary>
/// The SkiaCanvas control represents the canvas/whiteboard of the application
/// </summary>
public class SkiaCanvas : Control
{
    /// <summary>Camera transform owned by this viewport.</summary>
    public CameraState Camera { get; } = new();

    public static readonly StyledProperty<List<CanvasElement>> CanvasElementsProperty =
        AvaloniaProperty.Register<SkiaCanvas, List<CanvasElement>>(nameof(CanvasElements));

    public List<CanvasElement> CanvasElements
    {
        get => GetValue(CanvasElementsProperty);
        set => SetValue(CanvasElementsProperty, value);
    }

    public static readonly StyledProperty<Color> CanvasBackgroundProperty =
        AvaloniaProperty.Register<SkiaCanvas, Color>(nameof(CanvasBackground));

    public Color CanvasBackground
    {
        get => GetValue(CanvasBackgroundProperty);
        set => SetValue(CanvasBackgroundProperty, value);
    }

    private static readonly SKPaint LightGridPaint = new()
        { Color = SKColors.Black.WithAlpha(60), IsAntialias = true, Style = SKPaintStyle.Fill };

    private static readonly SKPaint DarkGridPaint = new()
        { Color = SKColors.White.WithAlpha(60), IsAntialias = true, Style = SKPaintStyle.Fill };

    /// <summary>
    /// Cull elements using their visible bounds in world coordinates.
    /// </summary>
    internal static bool IsCanvasElementVisible(CanvasElement element, SKRect visibleWorldRect)
    {
        SKRect elementBounds;
        switch (element)
        {
            case PaintableStroke stroke:
                elementBounds = stroke.Path.Bounds;
                // Include the outline and single-point strokes at the edge of the viewport.
                var margin = Math.Max(1f, stroke.Paint.StrokeWidth / 2);
                elementBounds.Inflate(margin, margin);
                break;
            case CanvasImage image:
                elementBounds = CanvasGeometryUtilities.GetImageWorldBounds(image);
                break;
            default:
                // Unknown element type, draw it to be safe
                return true;
        }

        return visibleWorldRect.IntersectsWith(elementBounds);
    }

    public override void Render(DrawingContext context)
    {
        // Draw background for the control, this is needed to handle pointer events
        context.DrawRectangle(new SolidColorBrush(Colors.Transparent), null, new Rect(Bounds.Size));

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);

        context.Custom(new SkiaDrawOperation(bounds, CaptureFrame()));
    }

    /// <summary>
    /// Record the visible board on the UI thread while the camera and live elements are stable.
    /// </summary>
    internal SKPicture CaptureFrame()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create((float)Bounds.Width, (float)Bounds.Height));
        var bgColor = CanvasBackground;
        DrawBackground(canvas, bgColor);

        // Save the existing canvas state (Avalonia's DPI/layout matrix) so we can restore it later
        canvas.Save();

        var viewMatrix = Camera.GetViewMatrix();
        canvas.Concat(in viewMatrix);

        var visibleWorldRect = VisibleWorldRect;

        // CanvasStateService already publishes layer order. Sort only callers that supply unordered elements.
        if (CanvasElements is { Count: > 0 } elements)
        {
            var ordered = InDrawingOrder(elements)
                ? elements
                : elements.OrderBy(element => element.LayerIndex).ThenBy(element => element.CreatedAt).ToList();
            foreach (var element in ordered)
            {
                if (!IsCanvasElementVisible(element, visibleWorldRect)) continue;
                var erasePreview =
                    element is PaintableStroke { IsToBeErased: true } or CanvasImage { IsToBeErased: true };
                CanvasElementRenderer.Draw(canvas, element, erasePreview);
            }
        }

        // Restore to the pre-camera state
        canvas.Restore();
        // The picture retains recorded paths, paints and immutable pixels until the operation retires.
        return recorder.EndRecording();
    }

    /// <summary>Returns the world-space viewport used for culling and room image priority.</summary>
    internal SKRect VisibleWorldRect => SKRect.Create(Camera.WorldOffSetX, Camera.WorldOffSetY,
        (float)Bounds.Width / Camera.Zoom, (float)Bounds.Height / Camera.Zoom);

    /// <summary>
    /// Check the paint order without allocating a sorted copy on each pointer move.
    /// </summary>
    private static bool InDrawingOrder(List<CanvasElement> elements)
    {
        for (var i = 1; i < elements.Count; i++)
        {
            var previous = elements[i - 1];
            var next = elements[i];
            if (previous.LayerIndex > next.LayerIndex ||
                (previous.LayerIndex == next.LayerIndex && previous.CreatedAt > next.CreatedAt)) return false;
        }

        return true;
    }

    private void DrawBackground(SKCanvas canvas, Color bgColor)
    {
        // Clear the entire viewport with the background color (before camera transform)
        canvas.Clear(Utilities.ToSkColor(bgColor));

        // Draw dot grid in screen space
        var zoom = Camera.Zoom;
        // Base spacing is 60 world units
        var gridSpacing = 60f * zoom;

        // Hide grid when zoomed out too far to avoid clutter and performance issues
        if (gridSpacing >= 15f)
        {
            // Modulo function that correctly handles negative numbers
            float Mod(float a, float n) => (a % n + n) % n;

            var gridOffsetX = Mod(Camera.WorldOffSetX * zoom, gridSpacing);
            var gridOffsetY = Mod(Camera.WorldOffSetY * zoom, gridSpacing);

            // Determine if background is dark or light to pick dot color
            var isDarkBg = bgColor.R * 0.299 + bgColor.G * 0.587 + bgColor.B * 0.114 < 128;
            var gridPaint = isDarkBg ? DarkGridPaint : LightGridPaint;

            for (var x = -gridOffsetX; x < Bounds.Width; x += gridSpacing)
            {
                for (var y = -gridOffsetY; y < Bounds.Height; y += gridSpacing)
                {
                    canvas.DrawCircle(x, y, 1.5f, gridPaint);
                }
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CanvasElementsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldList)
            {
                oldList.CollectionChanged -= OnCanvasElementsCollectionChanged;
            }

            if (change.NewValue is INotifyCollectionChanged newList)
            {
                newList.CollectionChanged += OnCanvasElementsCollectionChanged;
            }

            InvalidateVisual();
        }
        else if (change.Property == CanvasBackgroundProperty)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// runs when an element is added/removed from the canvas elements collection
    /// it disposes bitmaps and SKPaint objects
    /// </summary>
    private void OnCanvasElementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        InvalidateVisual();
    }
}

/// <summary>
/// Own the recorded frame for its entire scene-graph lifetime.
/// </summary>
internal sealed class SkiaDrawOperation(Rect bounds, SKPicture picture) : ICustomDrawOperation
{
    public Rect Bounds { get; } = bounds;

    public bool Equals(ICustomDrawOperation? other) => false;

    /// <summary>
    /// Release the native drawing commands when Avalonia retires the frame.
    /// </summary>
    public void Dispose() => picture.Dispose();

    public bool HitTest(Point p) => false;

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature == null) return;

        using var lease = leaseFeature.Lease();
        lease.SkCanvas.DrawPicture(picture);
    }
}