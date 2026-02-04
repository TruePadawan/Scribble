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
public abstract record Event
{
    public DateTime TimeStamp { get; init; } = DateTime.UtcNow;
}

public abstract record StrokeEvent(Guid StrokeId) : Event;

public record StartStrokeEvent(Guid StrokeId, SKPoint StartPoint, StrokePaint StrokePaint, StrokeTool ToolType)
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
public record AddTextEvent(Guid StrokeId, SKPoint Position, string Text, StrokePaint Paint)
    : StrokeEvent(StrokeId);

// SELECT TOOL
public record CreateSelectionBoundEvent(Guid BoundId, SKPoint StartPoint) : StrokeEvent(BoundId);

public record IncreaseSelectionBoundEvent(Guid BoundId, SKPoint Point) : StrokeEvent(BoundId);

public record EndSelectionEvent(Guid BoundId) : StrokeEvent(BoundId);

public record MoveStrokesEvent(Guid BoundId, SKPoint Delta) : StrokeEvent(BoundId);

public record RotateStrokesEvent(Guid BoundId, float DegreesRad, SKPoint Center) : StrokeEvent(BoundId);

public record ScaleStrokesEvent(Guid BoundId, SKPoint Scale, SKPoint Center) : StrokeEvent(BoundId);

// MISC
public record RestoreCanvasEvent(List<Stroke> Strokes) : Event;

public record UndoEvent(Guid TargetStrokeId) : StrokeEvent(TargetStrokeId);

public record RedoEvent(Guid TargetStrokeId) : StrokeEvent(TargetStrokeId);