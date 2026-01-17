using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Scribble.Lib;
using Scribble.Tools.PointerTools.EllipseTool;
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

    public static readonly StyledProperty<SKColor> CanvasBackgroundProperty =
        AvaloniaProperty.Register<SkiaCanvas, SKColor>(nameof(CanvasBackground));

    public SKColor CanvasBackground
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

    private void DrawContent(SKCanvas canvas, IEnumerable<Stroke> strokesToDraw, SKColor bgColor)
    {
        canvas.Clear(bgColor);

        foreach (var stroke in strokesToDraw)
        {
            switch (stroke)
            {
                case DrawStroke drawStroke:
                    var paintToUse = drawStroke.Paint;
                    IDisposable? disposablePaintClone = null;
                    if (drawStroke.IsToBeErased)
                    {
                        // Dispose clone of paint to prevent memory leaks
                        paintToUse = drawStroke.Paint.Clone();
                        paintToUse.Color = paintToUse.Color.WithAlpha(80);
                        disposablePaintClone = paintToUse;
                    }

                    if (drawStroke.Path.PointCount == 1)
                    {
                        if (drawStroke is TextStroke textStroke)
                        {
                            canvas.DrawText(textStroke.Text, textStroke.Path.LastPoint, paintToUse);
                        }
                        else
                        {
                            var firstPoint = drawStroke.Path.Points[0];
                            canvas.DrawPoint(firstPoint, paintToUse);
                        }
                    }
                    else
                    {
                        switch (drawStroke.ToolType)
                        {
                            case StrokeTool.Ellipse:
                                var start = drawStroke.Path[0];
                                var end = drawStroke.Path[1];
                                var ellipseLeft = Math.Min(start.X, end.X);
                                var ellipseTop = Math.Min(start.Y, end.Y);
                                var ellipseRect = SKRect.Create(new SKPoint(ellipseLeft, ellipseTop),
                                    Utilities.GetSize(start, end));
                                canvas.DrawOval(ellipseRect, paintToUse);
                                break;
                            case StrokeTool.Rectangle:
                                var rectOrigin = drawStroke.Path[0];
                                var rectEnd = drawStroke.Path[1];
                                var left = Math.Min(rectOrigin.X, rectEnd.X);
                                var top = Math.Min(rectOrigin.Y, rectEnd.Y);
                                var rect = SKRect.Create(new SKPoint(left, top),
                                    Utilities.GetSize(rectOrigin, rectEnd));
                                canvas.DrawRect(rect, paintToUse);
                                break;
                            default:
                                canvas.DrawPath(drawStroke.Path, paintToUse);
                                break;
                        }
                    }

                    disposablePaintClone?.Dispose();
                    break;
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