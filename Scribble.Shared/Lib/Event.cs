using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Lib;

/// <summary>
/// Represents the base class for all events in the application.
/// </summary>
[JsonDerivedType(typeof(StartStrokeEvent), typeDiscriminator: "StartStroke")]
[JsonDerivedType(typeof(EndStrokeEvent), typeDiscriminator: "EndStroke")]
[JsonDerivedType(typeof(PencilStrokeLineToEvent), typeDiscriminator: "PencilStrokeLineTo")]
[JsonDerivedType(typeof(StartEraseStrokeEvent), typeDiscriminator: "StartEraseStroke")]
[JsonDerivedType(typeof(EraseStrokeLineToEvent), typeDiscriminator: "EraseStrokeLineTo")]
[JsonDerivedType(typeof(TriggerEraseEvent), typeDiscriminator: "TriggerErase")]
[JsonDerivedType(typeof(LineStrokeLineToEvent), typeDiscriminator: "LineStrokeLineTo")]
[JsonDerivedType(typeof(AddTextEvent), typeDiscriminator: "AddText")]
[JsonDerivedType(typeof(CreateSelectionBoundEvent), typeDiscriminator: "CreateSelectionBound")]
[JsonDerivedType(typeof(IncreaseSelectionBoundEvent), typeDiscriminator: "IncreaseSelectionBound")]
[JsonDerivedType(typeof(EndSelectionEvent), typeDiscriminator: "EndSelection")]
[JsonDerivedType(typeof(MoveStrokesEvent), typeDiscriminator: "MoveStrokes")]
[JsonDerivedType(typeof(RotateStrokesEvent), typeDiscriminator: "RotateStrokes")]
[JsonDerivedType(typeof(ScaleStrokesEvent), typeDiscriminator: "ScaleStrokes")]
[JsonDerivedType(typeof(UndoEvent), typeDiscriminator: "Undo")]
[JsonDerivedType(typeof(RedoEvent), typeDiscriminator: "Redo")]
[JsonDerivedType(typeof(RestoreCanvasEvent), typeDiscriminator: "RestoreCanvasEvent")]
public abstract record Event(Guid ActionId)
{
    public DateTime TimeStamp { get; init; } = DateTime.UtcNow;
}

public interface ITerminalEvent
{
}

public abstract record StrokeEvent(Guid ActionId, Guid StrokeId) : Event(ActionId);

public record StartStrokeEvent(
    Guid ActionId,
    Guid StrokeId,
    SKPoint StartPoint,
    StrokePaint StrokePaint,
    StrokeTool ToolType)
    : StrokeEvent(ActionId, StrokeId);

public record EndStrokeEvent(Guid ActionId, Guid StrokeId) : StrokeEvent(ActionId, StrokeId), ITerminalEvent;

// PENCIL TOOL
public record PencilStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint Point) : StrokeEvent(ActionId, StrokeId);

// ERASE TOOL
public record StartEraseStrokeEvent(Guid ActionId, Guid StrokeId, SKPoint StartPoint) : StrokeEvent(ActionId, StrokeId);

public record EraseStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint Point) : StrokeEvent(ActionId, StrokeId);

public record TriggerEraseEvent(Guid ActionId, Guid StrokeId) : StrokeEvent(ActionId, StrokeId), ITerminalEvent;

// LINE + ARROW + RECTANGLE TOOL
public record LineStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint EndPoint) : StrokeEvent(ActionId, StrokeId);

// TEXT TOOL
public record AddTextEvent(Guid ActionId, Guid StrokeId, SKPoint Position, string Text, StrokePaint Paint)
    : StrokeEvent(ActionId, StrokeId), ITerminalEvent;

// SELECT TOOL
public record CreateSelectionBoundEvent(Guid ActionId, Guid BoundId, SKPoint StartPoint)
    : StrokeEvent(ActionId, BoundId);

public record IncreaseSelectionBoundEvent(Guid ActionId, Guid BoundId, SKPoint Point) : StrokeEvent(ActionId, BoundId);

public record EndSelectionEvent(Guid ActionId, Guid BoundId) : StrokeEvent(ActionId, BoundId), ITerminalEvent;

public record MoveStrokesEvent(Guid ActionId, Guid BoundId, SKPoint Delta) : StrokeEvent(ActionId, BoundId);

public record RotateStrokesEvent(Guid ActionId, Guid BoundId, float DegreesRad, SKPoint Center)
    : StrokeEvent(ActionId, BoundId);

public record ScaleStrokesEvent(Guid ActionId, Guid BoundId, SKPoint Scale, SKPoint Center)
    : StrokeEvent(ActionId, BoundId);

// MISC
public record RestoreCanvasEvent(Guid ActionId, List<Stroke> Strokes) : Event(ActionId), ITerminalEvent;

public record UndoEvent(Guid ActionId, Guid TargetActionId) : Event(ActionId);

public record RedoEvent(Guid ActionId, Guid TargetActionId) : Event(ActionId);