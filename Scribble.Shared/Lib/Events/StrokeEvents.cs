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

/// <summary>
/// PENCIL TOOL
/// </summary>
public record PencilStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint Point) : Event(ActionId);

/// <summary>
/// LINE + ARROW + RECTANGLE TOOL
/// </summary>
public record LineStrokeLineToEvent(Guid ActionId, Guid StrokeId, SKPoint EndPoint) : Event(ActionId);

/// <summary>
/// POLYLINE NODE (LINE + ARROW multi-click mode)
/// </summary>
public record AddPolylineNodeEvent(Guid ActionId, Guid StrokeId, SKPoint Point) : Event(ActionId);

public record MovePolylineNodeEvent(
    Guid ActionId,
    Guid StrokeId,
    int NodeIndex,
    SKPoint NewPosition
) : Event(ActionId);

/// <summary>
/// TEXT TOOL
/// </summary>
public record AddTextEvent(
    Guid ActionId,
    Guid StrokeId,
    SKPoint Position,
    string Text,
    StrokePaint Paint,
    HashSet<ToolOption> ToolOptions)
    : Event(ActionId), ITerminalEvent;