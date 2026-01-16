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

public abstract record StartStrokeEvent(Guid StrokeId, SKPoint StartPoint, SKPaint StrokePaint) : StrokeEvent(StrokeId);

public abstract record StrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

public abstract record EndStrokeEvent(Guid StrokeId) : StrokeEvent(StrokeId);

// FREE FORM DRAW TOOL
public record NewDrawStrokeEvent(Guid StrokeId, SKPoint StartPoint, SKPaint StrokePaint)
    : StartStrokeEvent(StrokeId, StartPoint, StrokePaint);

public record DrawStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeLineToEvent(StrokeId, Point);

public record EndDrawStrokeEvent(Guid StrokeId) : EndStrokeEvent(StrokeId);

// ERASE TOOL
public record NewEraseStrokeEvent(Guid StrokeId, SKPoint StartPoint) : StrokeEvent(StrokeId);

public record EraseStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

public record TriggerEraseEvent(Guid StrokeId) : StrokeEvent(StrokeId);

// LINE TOOL
public record NewLineStrokeEvent(Guid StrokeId, SKPoint StartPoint, SKPaint StrokePaint)
    : StartStrokeEvent(StrokeId, StartPoint, StrokePaint);

public record LineStrokeLineToEvent(Guid StrokeId, SKPoint EndPoint) : StrokeLineToEvent(StrokeId, EndPoint);

public record EndLineStrokeEvent(Guid StrokeId) : EndStrokeEvent(StrokeId);

// ARROW TOOL
public record ArrowStrokeLineToEvent(Guid StrokeId, SKPoint EndPoint) : StrokeLineToEvent(StrokeId, EndPoint);