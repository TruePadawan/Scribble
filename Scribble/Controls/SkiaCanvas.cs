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

public class SkiaCanvas : Control
{
    public static readonly StyledProperty<List<Stroke>> StrokesProperty =
        AvaloniaProperty.Register<SkiaCanvas, List<Stroke>>(nameof(Strokes));

    public List<Stroke> Strokes
    {
        get => GetValue(StrokesProperty);
        set => SetValue(StrokesProperty, value);
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
        var strokesToDraw = Strokes;
        var bgColor = CanvasBackground;
        context.Custom(
            new SkiaDrawOperation(bounds, canvas => { DrawContent(canvas, strokesToDraw, bgColor); }));
    }

    private void DrawContent(SKCanvas canvas, IEnumerable<Stroke> strokesToDraw, Color bgColor)
    {
        canvas.Clear(Utilities.ToSkColor(bgColor));

        foreach (var stroke in strokesToDraw)
        {
            if (stroke is DrawStroke drawStroke)
            {
                using var paintToUse = drawStroke.Paint.ToSkPaint();
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
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StrokesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldList)
            {
                oldList.CollectionChanged -= OnStrokesCollectionChanged;
            }

            if (change.NewValue is INotifyCollectionChanged newList)
            {
                newList.CollectionChanged += OnStrokesCollectionChanged;
            }

            InvalidateVisual();
        }
        else if (change.Property == CanvasBackgroundProperty)
        {
            InvalidateVisual();
        }
    }

    // runs when a stroke is added/removed from the strokes collection
    private void OnStrokesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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