using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a single point in a stroke
/// </summary>
public record struct StrokePoint(SKPoint Point, long TimestampMs);