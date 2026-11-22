using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Controls;

/// <summary>
/// Draw document elements onto any Skia canvas in world coordinates.
/// </summary>
internal static class CanvasElementRenderer
{
    private static readonly SKPaint EraserImagePaint = new() { Color = SKColors.Black.WithAlpha(80) };

    /// <summary>
    /// The screen caller can request erase feedback for an individual element.
    /// </summary>
    internal static void Draw(SKCanvas canvas, CanvasElement element, bool erasePreview = false)
    {
        switch (element)
        {
            case PaintableStroke stroke:
                DrawStroke(canvas, stroke, erasePreview);
                break;
            case CanvasImage image:
                DrawImage(canvas, image, erasePreview);
                break;
        }
    }

    /// <summary>
    /// Draw a single point or a filled path followed by its outline.
    /// </summary>
    private static void DrawStroke(SKCanvas canvas, PaintableStroke stroke, bool erasePreview)
    {
        var fill = stroke.Paint.FillColor;
        // Only modify paint owned by this draw call.
        using var temporary = erasePreview || fill.Alpha != 0 ? stroke.Paint.ToSkPaint() : null;
        var paint = temporary ?? stroke.Paint.GetCachedSkPaint();
        if (erasePreview) paint.Color = paint.Color.WithAlpha(80);

        if (stroke.Path.PointCount == 1)
        {
            canvas.DrawPoint(stroke.Path.GetPoint(0), paint);
            return;
        }

        if (fill.Alpha != 0)
        {
            var outline = paint.Color;
            paint.Style = SKPaintStyle.StrokeAndFill;
            paint.Color = fill;
            canvas.DrawPath(stroke.Path, paint);
            paint.Style = SKPaintStyle.Stroke;
            paint.Color = outline;
        }

        canvas.DrawPath(stroke.Path, paint);
    }

    /// <summary>
    /// Rotate and flip around the placement center, restoring the caller's transform afterward.
    /// </summary>
    private static void DrawImage(SKCanvas canvas, CanvasImage image, bool erasePreview)
    {
        var bitmap = image.GetBitmap();
        var bounds = image.Bounds;
        canvas.Save();
        canvas.RotateRadians(image.Rotation, bounds.MidX, bounds.MidY);
        if (image.FlipX || image.FlipY)
            canvas.Scale(image.FlipX ? -1 : 1, image.FlipY ? -1 : 1, bounds.MidX, bounds.MidY);
        canvas.DrawBitmap(bitmap, bounds, new SKSamplingOptions(SKFilterMode.Linear),
            erasePreview ? EraserImagePaint : null);
        canvas.Restore();
    }
}