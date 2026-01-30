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

public interface ITerminalEvent
{
}

public abstract record StrokeEvent(Guid StrokeId) : Event;

public record StartStrokeEvent(Guid StrokeId, SKPoint StartPoint, StrokePaint StrokePaint, StrokeTool ToolType)
    : StrokeEvent(StrokeId)
{
    public SKColor FIllColor { get; init; } = SKColors.Transparent;
}

public record EndStrokeEvent(Guid StrokeId) : StrokeEvent(StrokeId), ITerminalEvent;

// PENCIL TOOL
public record PencilStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

// ERASE TOOL
public record StartEraseStrokeEvent(Guid StrokeId, SKPoint StartPoint) : StrokeEvent(StrokeId);

public record EraseStrokeLineToEvent(Guid StrokeId, SKPoint Point) : StrokeEvent(StrokeId);

public record TriggerEraseEvent(Guid StrokeId) : StrokeEvent(StrokeId), ITerminalEvent;

// LINE + ARROW + RECTANGLE TOOL
public record LineStrokeLineToEvent(Guid StrokeId, SKPoint EndPoint) : StrokeEvent(StrokeId);

// TEXT TOOL
public record AddTextEvent(Guid StrokeId, SKPoint Position, string Text, StrokePaint Paint)
    : StrokeEvent(StrokeId), ITerminalEvent;

// SELECT TOOL
public record CreateSelectionBoundEvent(Guid BoundId, SKPoint StartPoint) : StrokeEvent(BoundId);

public record IncreaseSelectionBoundEvent(Guid BoundId, SKPoint Point) : StrokeEvent(BoundId);

public record EndSelectionEvent(Guid BoundId) : StrokeEvent(BoundId), ITerminalEvent;

public record MoveStrokesEvent(Guid BoundId, SKPoint Delta) : StrokeEvent(BoundId);

public record RotateStrokesEvent(Guid BoundId, float DegreesRad, SKPoint Center) : StrokeEvent(BoundId);

public record ScaleStrokesEvent(Guid BoundId, SKPoint Scale, SKPoint Center) : StrokeEvent(BoundId);

// public record EndMoveStrokesEvent(Guid BoundId): StrokeEvent(BoundId), ITerminalEvent;
