using System;
using SkiaSharp;

namespace Scribble.Lib;

/// <summary>
/// Represents the base class for all events in the application.
/// </summary>
public abstract record Event
{
    public DateTime TimeStamp { get; init; } = DateTime.UtcNow;
}

public abstract record StrokeEvent(Guid StrokeId) : Event;

public record NewDrawStrokeEvent(Guid StrokeId, SKPoint StartPoint, SKPaint StrokePaint) : StrokeEvent(StrokeId);

public record DrawStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

public record EndDrawStrokeEvent(Guid StrokeId) : StrokeEvent(StrokeId);

public record NewEraseStrokeEvent(Guid StrokeId, SKPoint StartPoint) : StrokeEvent(StrokeId);

public record EraseStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

public record TriggerEraseEvent(Guid StrokeId) : StrokeEvent(StrokeId);