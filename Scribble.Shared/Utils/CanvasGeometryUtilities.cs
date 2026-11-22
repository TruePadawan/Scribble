using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Shared.Utils;

/// <summary>Computes drawing geometry for both desktop and server replay.</summary>
public static class CanvasGeometryUtilities
{
    /// <summary>
    /// Returns the size of the rectangle formed by the two points
    /// </summary>
    /// <param name="start">Starting point</param>
    /// <param name="end">End point</param>
    /// <returns>A <see cref="SKSize">SKSize</see> object representing the size of the rectangle</returns>
    public static SKSize GetSize(SKPoint start, SKPoint end)
    {
        return new SKSize(Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y));
    }

    /// <summary>
    /// Checks if a point is near a line segment
    /// </summary>
    /// <param name="point">The point</param>
    /// <param name="lineEndPoints">An array holding the start and endpoints of the line segment</param>
    /// <param name="tolerance">The delta which determines if a point is considered near the line or not</param>
    /// <returns></returns>
    public static bool IsPointNearLine(SKPoint point, SKPoint[] lineEndPoints, float tolerance)
    {
        var start = lineEndPoints[0];
        var end = lineEndPoints[1];

        var lineLenSq = float.Pow(end.X - start.X, 2) + float.Pow(end.Y - start.Y, 2);

        // Check if the 'line' is actually just a point
        if (lineLenSq == 0)
        {
            return SKPoint.Distance(point, start) < tolerance;
        }

        var t = ((point.X - start.X) * (end.X - start.X) +
                 (point.Y - start.Y) * (end.Y - start.Y)) / lineLenSq;

        // Clamp t to the segment [0, 1] to handle the endpoints correctly
        t = float.Clamp(t, 0f, 1f);

        // Find the closest point on the line segment
        var closest = new SKPoint(
            start.X + t * (end.X - start.X),
            start.Y + t * (end.Y - start.Y)
        );

        return SKPoint.Distance(point, closest) < tolerance;
    }

    /// <summary>
    /// Return the axis-aligned world bounds of an image as drawn.
    /// </summary>
    internal static SKRect GetImageWorldBounds(CanvasImage image)
    {
        var bounds = image.Bounds;
        if (image.Rotation == 0f) return bounds;

        // Centered flips leave the outer bounds unchanged.
        var rotation = SKMatrix.CreateRotation(image.Rotation, bounds.MidX, bounds.MidY);
        return rotation.MapRect(bounds);
    }

    /// <summary>
    /// Returns the combined bounds of the elements
    /// </summary>
    public static SKRect GetElementsBounds(IEnumerable<CanvasElement> elements)
    {
        var totalBounds = SKRect.Empty;
        foreach (var element in elements)
        {
            if (element is PaintableStroke stroke)
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
            else if (element is CanvasImage canvasImage)
            {
                var imageBounds = GetImageWorldBounds(canvasImage);
                if (totalBounds.IsEmpty)
                {
                    totalBounds = imageBounds;
                }
                else
                {
                    totalBounds.Union(imageBounds);
                }
            }
        }

        return totalBounds;
    }
}