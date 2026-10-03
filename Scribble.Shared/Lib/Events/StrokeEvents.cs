using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

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

// LINE + ARROW + RECTANGLE TOOL
public record LineStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint EndPoint) : Event(ActionId);

// POLYLINE NODE (LINE + ARROW multi-click mode)
public record AddPolylineNodeEvent(Guid ActionId, Guid StrokeId, SKPoint Point) : Event(ActionId);

public record MovePolylineNodeEvent(
    Guid ActionId,
    Guid StrokeId,
    int NodeIndex,
    SKPoint NewPosition
) : Event(ActionId);

// TEXT TOOL
public record AddTextEvent(
    Guid ActionId,
    Guid StrokeId,
    SKPoint Position,
    string Text,
    StrokePaint Paint,
    HashSet<ToolOption> ToolOptions)
    : Event(ActionId), ITerminalEvent;