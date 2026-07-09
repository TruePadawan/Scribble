using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

// IMAGE TOOL
public record AddImageEvent(
    Guid ActionId,
    Guid ImageId,
    string ImageBase64String,
    SKPoint Position,
    SKSize Size) : Event(ActionId), ITerminalEvent;