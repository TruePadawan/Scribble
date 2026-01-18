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

public record EndStrokeEvent(Guid StrokeId) : StrokeEvent(StrokeId);

// PENCIL TOOL
public record PencilStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

// ERASE TOOL
public record StartEraseStrokeEvent(Guid StrokeId, SKPoint StartPoint) : StrokeEvent(StrokeId);

public record EraseStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

public record TriggerEraseEvent(Guid StrokeId) : StrokeEvent(StrokeId);

// LINE + ARROW + RECTANGLE TOOL
public record LineStrokeLineToEvent(Guid StrokeId, SKPoint EndPoint) : StrokeEvent(StrokeId);

// TEXT TOOL
public record AddTextEvent(Guid StrokeId, SKPoint Position, string Text, SKPaint Paint) : StrokeEvent(StrokeId);

// SELECT TOOL
public record CreateSelectionBoundEvent(Guid BoundId, SKPoint StartPoint) : StrokeEvent(BoundId);

public record IncreaseSelectionBoundEvent(Guid BoundId, SKPoint Point) : StrokeEvent(BoundId);

public record EndSelectionEvent(Guid BoundId) : StrokeEvent(BoundId);