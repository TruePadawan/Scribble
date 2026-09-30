using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Scribble.Utils;

/// <summary>
/// Contains utility methods for building SKPaths for various canvas shapes.
/// </summary>
public static class PathUtilities
{
    /// <summary>
    /// Builds a polyline or curved path from a sequence of nodes.
    /// </summary>
    public static void BuildPolylinePath(SKPath path, IReadOnlyList<SKPoint> nodes, bool isCurved)
    {
        if (nodes.Count == 0) return;

        path.MoveTo(nodes[0]);
        
        if (isCurved && nodes.Count > 2)
        {
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                var p0 = i == 0 ? nodes[0] : nodes[i - 1];
                var p1 = nodes[i];
                var p2 = nodes[i + 1];
                var p3 = i + 2 < nodes.Count ? nodes[i + 2] : p2;

                var cp1 = new SKPoint(
                    p1.X + (p2.X - p0.X) / 6f,
                    p1.Y + (p2.Y - p0.Y) / 6f);
                
                var cp2 = new SKPoint(
                    p2.X - (p3.X - p1.X) / 6f,
                    p2.Y - (p3.Y - p1.Y) / 6f);

                path.CubicTo(cp1, cp2, p2);
            }
        }
        else
        {
            for (var i = 1; i < nodes.Count; i++)
            {
                path.LineTo(nodes[i]);
            }
        }
    }

    /// <summary>
    /// Builds a rectangular path (either sharp or rounded) between two points.
    /// </summary>
    public static void BuildRectangleShape(SKPath path, SKPoint startPoint, SKPoint endPoint, bool isCurved)
    {
        path.MoveTo(startPoint);
        var left = Math.Min(startPoint.X, endPoint.X);
        var top = Math.Min(startPoint.Y, endPoint.Y);
        var rect = SKRect.Create(new SKPoint(left, top), Utilities.GetSize(startPoint, endPoint));
        
        if (isCurved)
        {
            path.AddRoundRect(rect, 24f, 24f);
        }
        else
        {
            path.AddRect(rect);
        }
    }

    /// <summary>
    /// Builds an elliptical path between two points.
    /// </summary>
    public static void BuildEllipseShape(SKPath path, SKPoint startPoint, SKPoint endPoint)
    {
        path.MoveTo(startPoint);
        var left = Math.Min(startPoint.X, endPoint.X);
        var top = Math.Min(startPoint.Y, endPoint.Y);
        var rect = SKRect.Create(new SKPoint(left, top), Utilities.GetSize(startPoint, endPoint));
        path.AddOval(rect);
    }
}
