using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Scribble.Shared.Lib;
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

    public override void Render(DrawingContext context)
    {
        // Draw background for the control, this is needed to handle pointer events
        context.DrawRectangle(new SolidColorBrush(Colors.Transparent), null, new Rect(Bounds.Size));

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        // Have to capture the data outside the Render thread else it throws an exception
        var elementsToDraw = CanvasElements;
        var bgColor = CanvasBackground;
        context.Custom(
            new SkiaDrawOperation(bounds, canvas => { DrawCanvasElementsOnCanvas(canvas, elementsToDraw, bgColor); }));
    }

    private void DrawCanvasElementsOnCanvas(SKCanvas canvas, IEnumerable<CanvasElement> elementsToDraw, Color bgColor)
    {
        canvas.Clear(Utilities.ToSkColor(bgColor));

        // Draw elements in layer-aware order: lower LayerIndex values are rendered first,
        // while preserving the existing relative order within each layer.
        foreach (var canvasElement in System.Linq.Enumerable.OrderBy(elementsToDraw, e => e.LayerIndex))
        {
            DrawSingleElement(canvas, canvasElement);
        }
    }

    private void DrawSingleElement(SKCanvas canvas, CanvasElement canvasElement)
    {
        if (canvasElement is DrawStroke drawStroke)
        {
            var needsMutablePaint = drawStroke.IsToBeErased || drawStroke.Paint.FillColor.Alpha != 0;
            var paintToUse = needsMutablePaint ? drawStroke.Paint.ToSkPaint() : drawStroke.Paint.GetCachedSkPaint();
            try
            {
                if (drawStroke.IsToBeErased)
                {
                    paintToUse.Color = paintToUse.Color.WithAlpha(80);
                }

                if (drawStroke.Path.PointCount == 1)
                {
                    canvas.DrawPoint(drawStroke.Path.Points[0], paintToUse);
                }
                else
                {
                    if (drawStroke.Paint.FillColor.Alpha != 0)
                    {
                        var strokeColor = paintToUse.Color;
                        paintToUse.Style = SKPaintStyle.StrokeAndFill;
                        paintToUse.Color = drawStroke.Paint.FillColor;
                        canvas.DrawPath(drawStroke.Path, paintToUse);
                        paintToUse.Style = SKPaintStyle.Stroke;
                        paintToUse.Color = strokeColor;
                    }

                    canvas.DrawPath(drawStroke.Path, paintToUse);
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
                else if (item is DrawStroke drawStroke)
                {
                    drawStroke.Paint.DisposeSkPaint();
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
        // save canvas state
        canvas.Save();
        drawAction(canvas);
    }
}