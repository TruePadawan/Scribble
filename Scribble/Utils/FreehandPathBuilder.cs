using System.Collections.Generic;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Utils;

/// <summary>
/// Builds a smooth path from a list of raw stroke points
/// </summary>
public static class FreehandPathBuilder
{
    public static void AppendPoint(SKPath path, ref SKPath? stablePath, IReadOnlyList<StrokePoint> points)
    {
        if (points.Count == 0)
        {
            path.Reset();
            stablePath?.Dispose();
            stablePath = null;
            return;
        }

        if (points.Count == 1)
        {
            path.Reset();
            path.MoveTo(points[0].Point);
            return;
        }

        if (points.Count == 2)
        {
            path.Reset();
            path.MoveTo(points[0].Point);
            path.LineTo(points[1].Point);
            return;
        }

        // points.Count >= 3
        if (stablePath == null)
        {
            stablePath = new SKPath();
            stablePath.MoveTo(points[0].Point);
            for (int i = 1; i < points.Count - 1; i++)
            {
                var controlPoint = points[i].Point;
                var lastPoint = points[i + 1].Point;
                var midPoint = new SKPoint((controlPoint.X + lastPoint.X) / 2f, (controlPoint.Y + lastPoint.Y) / 2f);
                stablePath.QuadTo(controlPoint.X, controlPoint.Y, midPoint.X, midPoint.Y);
            }
        }
        else
        {
            var lastIdx = points.Count - 1;
            var controlPoint = points[lastIdx - 1].Point;
            var lastPoint = points[lastIdx].Point;
            var midPoint = new SKPoint((controlPoint.X + lastPoint.X) / 2f, (controlPoint.Y + lastPoint.Y) / 2f);
            stablePath.QuadTo(controlPoint.X, controlPoint.Y, midPoint.X, midPoint.Y);
        }

        path.Reset();
        path.AddPath(stablePath);
        path.LineTo(points[^1].Point);
    }
}