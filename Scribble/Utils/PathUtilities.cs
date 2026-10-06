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
    public static void BuildPolylinePath(SKPathBuilder path, IReadOnlyList<SKPoint> nodes, bool isCurved)
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
    public static void BuildRectangleShape(SKPathBuilder path, SKPoint startPoint, SKPoint endPoint, bool isCurved)
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
    public static void BuildEllipseShape(SKPathBuilder path, SKPoint startPoint, SKPoint endPoint)
    {
        path.MoveTo(startPoint);
        var left = Math.Min(startPoint.X, endPoint.X);
        var top = Math.Min(startPoint.Y, endPoint.Y);
        var rect = SKRect.Create(new SKPoint(left, top), Utilities.GetSize(startPoint, endPoint));
        path.AddOval(rect);
    }

    /// <summary>
    /// Builds a diamond path (either sharp or rounded) between two points.
    /// </summary>
    public static void BuildDiamondShape(SKPathBuilder path, SKPoint startPoint, SKPoint endPoint, bool isCurved)
    {
        var left = Math.Min(startPoint.X, endPoint.X);
        var right = Math.Max(startPoint.X, endPoint.X);
        var top = Math.Min(startPoint.Y, endPoint.Y);
        var bottom = Math.Max(startPoint.Y, endPoint.Y);

        var midX = left + (right - left) / 2f;
        var midY = top + (bottom - top) / 2f;

        // The four cardinal vertices of the diamond.
        var topPt = new SKPoint(midX, top);
        var rightPt = new SKPoint(right, midY);
        var bottomPt = new SKPoint(midX, bottom);
        var leftPt = new SKPoint(left, midY);

        if (isCurved)
        {
            var halfW = (right - left) / 2f;
            var halfH = (bottom - top) / 2f;

            // All four edges of the diamond have the same length (it's a rhombus).
            // Compute unit vectors for each edge direction, then pick an offset
            // that gives a natural-looking border-radius at the tips.
            var edgeLen = (float)Math.Sqrt(halfW * halfW + halfH * halfH);
            var offset = Math.Min(edgeLen * 0.25f, Math.Min(halfW, halfH) * 0.5f);

            // Unit direction vectors for each of the four edges
            var inverseLength = 1f / edgeLen;
            var trDir = new SKPoint(halfW * inverseLength, halfH * inverseLength);
            var rbDir = new SKPoint(-halfW * inverseLength, halfH * inverseLength);
            var blDir = new SKPoint(-halfW * inverseLength, -halfH * inverseLength);
            var ltDir = new SKPoint(halfW * inverseLength, -halfH * inverseLength);

            // Points on each edge just BEFORE arriving at the vertex.
            var beforeTop = new SKPoint(topPt.X - ltDir.X * offset, topPt.Y - ltDir.Y * offset);
            var beforeRight = new SKPoint(rightPt.X - trDir.X * offset, rightPt.Y - trDir.Y * offset);
            var beforeBottom = new SKPoint(bottomPt.X - rbDir.X * offset, bottomPt.Y - rbDir.Y * offset);
            var beforeLeft = new SKPoint(leftPt.X - blDir.X * offset, leftPt.Y - blDir.Y * offset);

            // Points on each edge just AFTER leaving the vertex.
            var afterTop = new SKPoint(topPt.X + trDir.X * offset, topPt.Y + trDir.Y * offset);
            var afterRight = new SKPoint(rightPt.X + rbDir.X * offset, rightPt.Y + rbDir.Y * offset);
            var afterBottom = new SKPoint(bottomPt.X + blDir.X * offset, bottomPt.Y + blDir.Y * offset);
            var afterLeft = new SKPoint(leftPt.X + ltDir.X * offset, leftPt.Y + ltDir.Y * offset);

            // Straight edges with a QuadTo rounding only the four sharp tips.
            path.MoveTo(beforeTop);
            path.QuadTo(topPt, afterTop);
            path.LineTo(beforeRight);
            path.QuadTo(rightPt, afterRight);
            path.LineTo(beforeBottom);
            path.QuadTo(bottomPt, afterBottom);
            path.LineTo(beforeLeft);
            path.QuadTo(leftPt, afterLeft);
            path.Close();
        }
        else
        {
            path.MoveTo(topPt);
            path.LineTo(rightPt);
            path.LineTo(bottomPt);
            path.LineTo(leftPt);
            path.Close();
        }
    }
}