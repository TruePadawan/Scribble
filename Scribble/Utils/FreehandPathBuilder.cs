using System.Collections.Generic;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Utils;

/// <summary>
/// Builds a smooth path from a list of raw stroke points
/// </summary>
public static class FreehandPathBuilder
{
    public static SKPath Build(IReadOnlyList<StrokePoint> points)
    {
        var path = new SKPath();

        if (points.Count == 0)
        {
            return path;
        }

        if (points.Count == 1)
        {
            path.MoveTo(points[0].Point);
            return path;
        }

        if (points.Count == 2)
        {
            path.MoveTo(points[0].Point);
            path.LineTo(points[1].Point);
            return path;
        }

        path.MoveTo(points[0].Point);

        // Build a smooth path using quadratic beziers
        for (int i = 1; i < points.Count - 1; i++)
        {
            var p0 = points[i].Point;
            var p1 = points[i + 1].Point;
            var midPoint = new SKPoint((p0.X + p1.X) / 2f, (p0.Y + p1.Y) / 2f);
            path.QuadTo(p0.X, p0.Y, midPoint.X, midPoint.Y);
        }

        path.LineTo(points[^1].Point);

        return path;
    }
}