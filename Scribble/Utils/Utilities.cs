using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using SkiaSharp;

namespace Scribble.Utils;

/// <summary>
/// Contains various utility methods
/// </summary>
public static class Utilities
{
    /// <summary>
    /// Converts a Color to a SkiaSharp Color
    /// </summary>
    /// <returns>The equivalent <see cref="SKColor">SKColor</see> object</returns>
    public static SKColor ToSkColor(Color color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    /// <summary>
    /// Converts a SkiaSharp Color to a Color
    /// </summary>
    /// <returns>The equivalent <see cref="Color">Color</see> object</returns>
    public static Color FromSkColor(SKColor color)
    {
        return new Color(color.Alpha, color.Red, color.Green, color.Blue);
    }

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
    /// Converts a Point to a SkiaSharp Point
    /// </summary>
    /// <returns>The equivalent <see cref="Point">Point</see> object</returns>
    public static SKPoint ToSkPoint(Point coord)
    {
        return new SKPoint((float)coord.X, (float)coord.Y);
    }

    /// <summary>
    /// Returns true if two points are within a small epsilon of each other.
    /// Filters subpixel jitter from tablet pens without affecting intentional movement.
    /// </summary>
    public static bool AreSamePosition(Point a, Point b, double epsilon = 0.5)
    {
        return Math.Abs(a.X - b.X) < epsilon && Math.Abs(a.Y - b.Y) < epsilon;
    }

    public static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            return TopLevel.GetTopLevel(singleView.MainView);
        }

        return null;
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

        float lineLenSq = float.Pow(end.X - start.X, 2) + float.Pow(end.Y - start.Y, 2);

        // Check if the 'line' is actually just a point
        if (lineLenSq == 0)
        {
            return SKPoint.Distance(point, start) < tolerance;
        }

        float t = ((point.X - start.X) * (end.X - start.X) +
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
}