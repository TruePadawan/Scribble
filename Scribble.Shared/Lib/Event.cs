using System.Text.Json.Serialization;
using Scribble.Shared.Lib.Events;

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
[JsonDerivedType(typeof(MoveCanvasElementsEvent), typeDiscriminator: "MoveCanvasElements")]
[JsonDerivedType(typeof(RotateCanvasElementsEvent), typeDiscriminator: "RotateCanvasElements")]
[JsonDerivedType(typeof(ScaleCanvasElementsEvent), typeDiscriminator: "ScaleCanvasElements")]
[JsonDerivedType(typeof(UndoEvent), typeDiscriminator: "Undo")]
[JsonDerivedType(typeof(RedoEvent), typeDiscriminator: "Redo")]
[JsonDerivedType(typeof(LoadCanvasEvent), typeDiscriminator: "LoadCanvasEvent")]
[JsonDerivedType(typeof(UpdateStrokeColorEvent), typeDiscriminator: "UpdateStrokeColorEvent")]
[JsonDerivedType(typeof(SetElementLayerEvent), typeDiscriminator: "SetElementLayerEvent")]
[JsonDerivedType(typeof(NudgeElementLayerEvent), typeDiscriminator: "NudgeElementLayerEvent")]
[JsonDerivedType(typeof(UpdateStrokeEdgeTypeEvent), typeDiscriminator: "UpdateStrokeEdgeTypeEvent")]
[JsonDerivedType(typeof(UpdateStrokeFillColorEvent), typeDiscriminator: "UpdateStrokeFillColorEvent")]
[JsonDerivedType(typeof(UpdateFontSizeEvent), typeDiscriminator: "UpdateFontSizeEvent")]
[JsonDerivedType(typeof(UpdateStrokeStyleEvent), typeDiscriminator: "UpdateStrokeStyleEvent")]
[JsonDerivedType(typeof(UpdateStrokeThicknessEvent), typeDiscriminator: "UpdateStrokeThicknessEvent")]
[JsonDerivedType(typeof(ClearSelectionEvent), typeDiscriminator: "ClearSelectionEvent")]
[JsonDerivedType(typeof(AddImageEvent), typeDiscriminator: "AddImageEvent")]
[JsonDerivedType(typeof(UpdateTextEvent), typeDiscriminator: "UpdateTextEvent")]
[JsonDerivedType(typeof(UpdateFontCasingEvent), typeDiscriminator: "UpdateFontCasingEvent")]
[JsonDerivedType(typeof(UpdateFontStyleEvent), typeDiscriminator: "UpdateFontStyleEvent")]
[JsonDerivedType(typeof(PasteCanvasElementsEvent), typeDiscriminator: "PasteCanvasElementsEvent")]
public abstract record Event(Guid ActionId)
{
    public DateTime TimeStamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The SignalR ConnectionId of the client that originally produced this event.
    /// Null for events created in solo mode (no active room).
    /// </summary>
    public string? CreatorConnectionId { get; init; }
}

public interface ITerminalEvent
{
}