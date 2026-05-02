using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.State;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Controls;

/// <summary>
/// The SkiaCanvas control represents the canvas/whiteboard of the application
/// </summary>
public class SkiaCanvas : Control
{
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

    private static bool IsCanvasElementVisible(CanvasElement element, SKRect visibleWorldRect)
    {
        SKRect elementBounds;
        switch (element)
        {
            case PaintableStroke stroke:
                elementBounds = stroke.Path.Bounds;
                break;
            case CanvasImage image:
                elementBounds = image.Bounds;
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

        // Snapshot the element list on the UI thread, the render thread must not enumerate
        // the same mutable list that the UI thread modifies
        var elementsToDraw = CanvasElements.ToList();
        var bgColor = CanvasBackground;
        context.Custom(
            new SkiaDrawOperation(bounds, canvas => { DrawCanvasElementsOnCanvas(canvas, elementsToDraw, bgColor); }));
    }

    private void DrawCanvasElementsOnCanvas(SKCanvas canvas, IEnumerable<CanvasElement> elementsToDraw, Color bgColor)
    {
        // Clear the entire viewport with the background color (before camera transform)
        canvas.Clear(Utilities.ToSkColor(bgColor));

        // Save the existing canvas state (Avalonia's DPI/layout matrix) so we can restore it later
        canvas.Save();

        var viewMatrix = CameraState.GetViewMatrix();
        canvas.Concat(ref viewMatrix);

        // Compute the visible world-space rectangle for culling unnecessary strokes
        var viewportWidth = (float)Bounds.Width;
        var viewportHeight = (float)Bounds.Height;
        var visibleWorldRect = new SKRect(
            CameraState.WorldOffSetX,
            CameraState.WorldOffSetY,
            CameraState.WorldOffSetX + viewportWidth / CameraState.Zoom,
            CameraState.WorldOffSetY + viewportHeight / CameraState.Zoom
        );

        // Draw elements in layer-aware order: lower LayerIndex values are rendered first,
        // while preserving the existing relative order within each layer.
        foreach (var canvasElement in elementsToDraw
                     .OrderBy(e => e.LayerIndex)
                     .ThenBy(e => e.CreatedAt))
        {
            if (!IsCanvasElementVisible(canvasElement, visibleWorldRect)) continue;
            DrawSingleElement(canvas, canvasElement);
        }

        // Restore to the pre-camera state
        canvas.Restore();
    }

    private void DrawSingleElement(SKCanvas canvas, CanvasElement canvasElement)
    {
        if (canvasElement is PaintableStroke paintableStroke)
        {
            var needsMutablePaint = paintableStroke.IsToBeErased || paintableStroke.Paint.FillColor.Alpha != 0;
            var paintToUse = needsMutablePaint
                ? paintableStroke.Paint.ToSkPaint()
                : paintableStroke.Paint.GetCachedSkPaint();
            try
            {
                if (paintableStroke.IsToBeErased)
                {
                    paintToUse.Color = paintToUse.Color.WithAlpha(80);
                }

                if (paintableStroke.Path.PointCount == 1)
                {
                    canvas.DrawPoint(paintableStroke.Path.Points[0], paintToUse);
                }
                else
                {
                    if (paintableStroke.Paint.FillColor.Alpha != 0)
                    {
                        var strokeColor = paintToUse.Color;
                        paintToUse.Style = SKPaintStyle.StrokeAndFill;
                        paintToUse.Color = paintableStroke.Paint.FillColor;
                        canvas.DrawPath(paintableStroke.Path, paintToUse);
                        paintToUse.Style = SKPaintStyle.Stroke;
                        paintToUse.Color = strokeColor;
                    }

                    canvas.DrawPath(paintableStroke.Path, paintToUse);
                }
            }
            finally
            {
                if (needsMutablePaint)
                    paintToUse.Dispose();
            }
        }
        else if (canvasElement is CanvasImage canvasImage)
        {
            var bitmap = canvasImage.GetBitmap();
            // Pushes a snapshot of the current canvas state (transforms, clipping regions, etc.) onto an internal stack before rotating canvas
            // Needed for drawing rotated images
            canvas.Save();
            canvas.RotateRadians(canvasImage.Rotation, canvasImage.Bounds.MidX, canvasImage.Bounds.MidY);

            // Flip the canvas to apply image-flips
            if (canvasImage.FlipX)
                canvas.Scale(-1, 1, canvasImage.Bounds.MidX, canvasImage.Bounds.MidY);
            if (canvasImage.FlipY)
                canvas.Scale(1, -1, canvasImage.Bounds.MidX, canvasImage.Bounds.MidY);

            if (canvasImage.IsToBeErased)
            {
                using var lowOpacityPaint = new SKPaint();
                lowOpacityPaint.Color = SKColors.Black.WithAlpha(80);
                canvas.DrawBitmap(bitmap, canvasImage.Bounds, lowOpacityPaint);
            }
            else
            {
                canvas.DrawBitmap(bitmap, canvasImage.Bounds);
            }

            canvas.Restore();
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

    // runs when an element is added/removed from the canvas elements collection
    // it disposes bitmaps and SKPaint objects
    private void OnCanvasElementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is CanvasImage canvasImage)
                {
                    canvasImage.DisposeBitmap();
                }
                else if (item is PaintableStroke paintableStroke)
                {
                    paintableStroke.Paint.DisposeSkPaint();
                }
            }
        }

        InvalidateVisual();
    }
}

internal class SkiaDrawOperation(Rect bounds, Action<SKCanvas> drawAction) : ICustomDrawOperation
{
    public Rect Bounds { get; } = bounds;

    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose()
    {
    }

    public bool HitTest(Point p) => false;

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature == null) return;

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        drawAction(canvas);
    }
}