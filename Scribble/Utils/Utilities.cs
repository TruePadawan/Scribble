using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using SkiaSharp;

namespace Scribble.Utils;

public static class Utilities
{
    public static SKColor ToSkColor(Color color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    public static Color FromSkColor(SKColor color)
    {
        return new Color(color.Alpha, color.Red, color.Green, color.Blue);
    }

    public static SKSize GetSize(SKPoint start, SKPoint end)
    {
        return new SKSize(Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y));
    }

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
}