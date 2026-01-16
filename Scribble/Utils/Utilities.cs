using System;
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
}