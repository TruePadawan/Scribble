using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

/// <summary>
/// IMAGE TOOL
/// </summary>
public record AddImageEvent(
    Guid ActionId,
    Guid ImageId,
    string ImageBase64String,
    SKPoint Position,
    SKSize Size) : Event(ActionId), ITerminalEvent;