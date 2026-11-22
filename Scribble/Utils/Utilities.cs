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
    /// Converts a Point to a SkiaSharp Point
    /// </summary>
    /// <returns>The equivalent <see cref="SKPoint">SKPoint</see> object</returns>
    public static SKPoint ToSkPoint(Point coord)
    {
        return new SKPoint((float)coord.X, (float)coord.Y);
    }

    /// <summary>
    /// Converts a SkiaSharp Point to a Point
    /// </summary>
    /// <param name="point"></param>
    /// <returns>The equivalent <see cref="Point">Point</see> object</returns>
    public static Point FromSkPoint(SKPoint point)
    {
        return new Point(point.X, point.Y);
    }

    /// <summary>
    /// Returns true if two points are within a small epsilon of each other.
    /// </summary>
    public static bool AreSamePosition(SKPoint a, SKPoint b, double epsilon = 0.5)
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
    /// Converts degrees to radians
    /// </summary>
    /// <returns></returns>
    public static float DegreesToRadians(float degrees)
    {
        return (float)(degrees * Math.PI / 180);
    }

    /// <summary>
    /// Converts radians to degrees
    /// </summary>
    /// <returns></returns>
    public static float RadiansToDegrees(float radians)
    {
        return (float)(radians * 180 / Math.PI);
    }
}