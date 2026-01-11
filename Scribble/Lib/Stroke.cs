using SkiaSharp;

namespace Scribble.Lib;

public record Stroke(bool IsErasingStroke = false)
{
    public SKPath Path { get; } = new();
    public SKPaint Paint { get; init; } = new();
}