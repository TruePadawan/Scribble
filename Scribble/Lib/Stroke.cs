using System;
using SkiaSharp;

namespace Scribble.Lib;

public record DrawStroke(bool IsErasingStroke = false)
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SKPath Path { get; } = new();
    public SKPaint Paint { get; init; } = new();
}