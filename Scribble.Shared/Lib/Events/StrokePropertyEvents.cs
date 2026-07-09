using System.Text.Json.Serialization;
using Scribble.Shared.Converters;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Shared.Lib.Events;

public record UpdateStrokeColorEvent(Guid ActionId, List<Guid> StrokeIds, SKColor NewColor)
    : Event(ActionId), ITerminalEvent
{
    [JsonConverter(typeof(SKColorJsonConverter))]
    public SKColor NewColor { get; } = NewColor;
}

public record UpdateStrokeThicknessEvent(Guid ActionId, List<Guid> StrokeIds, float NewThickness)
    : Event(ActionId), ITerminalEvent;

public record UpdateStrokeStyleEvent(Guid ActionId, List<Guid> StrokeIds, float[]? NewDashIntervals)
    : Event(ActionId), ITerminalEvent;

public record UpdateStrokeFillColorEvent(Guid ActionId, List<Guid> StrokeIds, SKColor NewFillColor)
    : Event(ActionId), ITerminalEvent
{
    [JsonConverter(typeof(SKColorJsonConverter))]
    public SKColor NewFillColor { get; } = NewFillColor;
}

public record UpdateStrokeEdgeTypeEvent(Guid ActionId, List<Guid> StrokeIds, SKStrokeJoin NewStrokeJoin)
    : Event(ActionId), ITerminalEvent;

public record UpdateFontSizeEvent(Guid ActionId, List<Guid> StrokeIds, float FontSize)
    : Event(ActionId), ITerminalEvent;

public record SetElementLayerEvent(Guid ActionId, Guid[] TargetElementIds, int NewLayerIndex)
    : Event(ActionId), ITerminalEvent;

public record NudgeElementLayerEvent(Guid ActionId, Guid[] TargetElementIds, int Offset)
    : Event(ActionId), ITerminalEvent;

public record UpdateTextEvent(Guid ActionId, Guid TextStrokeId, string NewText) : Event(ActionId), ITerminalEvent;

public record UpdateFontCasingEvent(Guid ActionId, List<Guid> TextStrokeIds, FontCasing NewCasing)
    : Event(ActionId), ITerminalEvent;

public record UpdateFontStyleEvent(Guid ActionId, List<Guid> TextStrokeIds, FontStyle NewStyle)
    : Event(ActionId), ITerminalEvent;