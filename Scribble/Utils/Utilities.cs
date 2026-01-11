using Avalonia.Media;
using SkiaSharp;

namespace Scribble.Utils;

public static class Utilities
{
    public static SKColor ToSkColor(Color color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }
}