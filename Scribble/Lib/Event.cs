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

public record StartStrokeEvent(Guid StrokeId, SKPoint StartPoint, SKPaint StrokePaint, StrokeTool ToolType)
    : StrokeEvent(StrokeId);

public abstract record StrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

public record EndStrokeEvent(Guid StrokeId) : StrokeEvent(StrokeId);

// PENCIL TOOL
public record PencilStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeLineToEvent(StrokeId, Point);

// ERASE TOOL
public record NewEraseStrokeEvent(Guid StrokeId, SKPoint StartPoint) : StrokeEvent(StrokeId);

public record EraseStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

public record TriggerEraseEvent(Guid StrokeId) : StrokeEvent(StrokeId);

// LINE + ARROW TOOL
public record LineStrokeLineToEvent(Guid StrokeId, SKPoint EndPoint) : StrokeLineToEvent(StrokeId, EndPoint);