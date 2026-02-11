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

public record StartStrokeEvent(
    Guid ActionId,
    Guid StrokeId,
    SKPoint StartPoint,
    StrokePaint StrokePaint,
    ToolType ToolType,
    HashSet<ToolOption> ToolOptions)
    : Event(ActionId);

public record EndStrokeEvent(Guid ActionId) : Event(ActionId), ITerminalEvent;

// PENCIL TOOL
public record PencilStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint Point) : Event(ActionId);

// ERASE TOOL
public record StartEraseStrokeEvent(Guid ActionId, Guid StrokeId, SKPoint StartPoint) : Event(ActionId);

public record EraseStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint Point) : Event(ActionId);

public record TriggerEraseEvent(Guid ActionId, Guid StrokeId) : Event(ActionId), ITerminalEvent;

// LINE + ARROW + RECTANGLE TOOL
public record LineStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint EndPoint) : Event(ActionId);

// TEXT TOOL
public record AddTextEvent(
    Guid ActionId,
    Guid StrokeId,
    SKPoint Position,
    string Text,
    StrokePaint Paint,
    HashSet<ToolOption> ToolOptions)
    : Event(ActionId), ITerminalEvent;

// SELECT TOOL
public record CreateSelectionBoundEvent(Guid ActionId, Guid BoundId, SKPoint StartPoint) : Event(ActionId);

public record IncreaseSelectionBoundEvent(Guid ActionId, Guid BoundId, SKPoint Point) : Event(ActionId);

public record EndSelectionEvent(Guid ActionId, Guid BoundId) : Event(ActionId), ITerminalEvent;

public record ClearSelectionEvent(Guid ActionId) : Event(ActionId), ITerminalEvent;

public record MoveStrokesEvent(Guid ActionId, Guid BoundId, SKPoint Delta) : Event(ActionId);

public record RotateStrokesEvent(Guid ActionId, Guid BoundId, float DegreesRad, SKPoint Center)
    : Event(ActionId);

public record ScaleStrokesEvent(Guid ActionId, Guid BoundId, SKPoint Scale, SKPoint Center) : Event(ActionId);

// MISC
public record RestoreCanvasEvent(Guid ActionId, List<Stroke> Strokes) : Event(ActionId), ITerminalEvent;

public record UndoEvent(Guid ActionId, Guid TargetActionId) : Event(ActionId);

public record RedoEvent(Guid ActionId, Guid TargetActionId) : Event(ActionId);

public record UpdateStrokeColorEvent(Guid ActionId, List<Guid> StrokeIds, SKColor NewColor)
    : Event(ActionId), ITerminalEvent;

public record UpdateStrokeThicknessEvent(Guid ActionId, List<Guid> StrokeIds, float NewThickness)
    : Event(ActionId), ITerminalEvent;

public record UpdateStrokeStyleEvent(Guid ActionId, List<Guid> StrokeIds, float[]? NewDashIntervals)
    : Event(ActionId), ITerminalEvent;

public record UpdateStrokeFillColorEvent(Guid ActionId, List<Guid> StrokeIds, SKColor NewFillColor)
    : Event(ActionId), ITerminalEvent;

public record UpdateStrokeEdgeTypeEvent(Guid ActionId, List<Guid> StrokeIds, SKStrokeJoin NewStrokeJoin)
    : Event(ActionId), ITerminalEvent;

public record UpdateStrokeFontSizeEvent(Guid ActionId, List<Guid> StrokeIds, float FontSize)
    : Event(ActionId), ITerminalEvent;