using System.Collections.Generic;
using Scribble.Shared.Lib;
using SkiaSharp;

namespace Scribble.Lib;

public static class CanvasExporter
{
    private static SKRect GetStrokesBounds(IEnumerable<DrawStroke> strokes)
    {
        SKRect totalBounds = SKRect.Empty;
        foreach (var stroke in strokes)
        {
            SKRect pathBounds = stroke.Path.Bounds;
            float halfStrokeWidth = stroke.Paint.StrokeWidth / 2;
            pathBounds.Inflate(halfStrokeWidth, halfStrokeWidth);
            if (totalBounds.IsEmpty)
            {
                totalBounds = pathBounds;
            }
            else
            {
                totalBounds.Union(pathBounds);
            }
        }

        return totalBounds;
    }

    public static byte[]? GetPngData(
        List<DrawStroke> strokes,
        bool includeBackground,
        SKColor backgroundColor,
        int scale = 1, int padding = 20)
    {
        if (strokes.Count == 0)
        {
            return null;
        }

        // Calculate the bounding box of all strokes
        SKRect bounds = GetStrokesBounds(strokes);
        // Add padding
        bounds.Inflate(padding, padding);

        // Determine the final image size based on scale
        var finalImgWidth = (int)(bounds.Width * scale);
        var finalImgHeight = (int)(bounds.Height * scale);

        // Create an off-screen surface
        var imageInfo = new SKImageInfo(finalImgWidth, finalImgHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        using var canvas = surface.Canvas;

        canvas.Clear(includeBackground ? backgroundColor : SKColors.Transparent);

        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);

        // Render the strokes
        foreach (var drawStroke in strokes)
        {
            using var paintToUse = drawStroke.Paint.ToSkPaint();
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

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }
}