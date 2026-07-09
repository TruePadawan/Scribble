using Scribble.Shared.Lib.CanvasElements;
using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

public record LoadCanvasEvent(Guid ActionId, List<CanvasElement> CanvasElements) : Event(ActionId);

public record UndoEvent(Guid ActionId, Guid TargetActionId) : Event(ActionId);

public record RedoEvent(Guid ActionId, Guid TargetActionId) : Event(ActionId);

public record PasteCanvasElementsEvent(
    Guid ActionId,
    SKPoint Position,
    List<CanvasElement> CopiedElements,
    Guid SelectionBoundId)
    : Event(ActionId), ITerminalEvent;