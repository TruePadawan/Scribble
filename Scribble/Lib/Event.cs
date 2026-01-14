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

public record NewDrawStrokeEvent(Guid StrokeId, SKPoint StartPoint, SKPaint StrokePaint) : Event;

public record DrawStrokeLineToEvent(Guid StrokeId, SKPoint Point) : Event;

public record NewEraseStrokeEvent(Guid StrokeId, SKPoint StartPoint) : Event;

public record EraseStrokeLineToEvent(Guid StrokeId, SKPoint Point) : Event;

public record TriggerEraseEvent(Guid StrokeId) : Event;