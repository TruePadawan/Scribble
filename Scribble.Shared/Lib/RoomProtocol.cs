using Scribble.Shared.Lib.Events;

namespace Scribble.Shared.Lib;

/// <summary>Defines transfer limits shared by room clients and the server.</summary>
public static class RoomProtocol
{
    public const int SnapshotBytes = 8 * 1024 * 1024;
    public const int AssetBytes = 32 * 1024 * 1024;
    public const int ChunkBytes = 128 * 1024;
    public const int EnvelopeBytes = 2 * ChunkBytes + 65536;
    public const int Version = 3;
}

/// <summary>Contains room admission details and the joining participant's private token.</summary>
public sealed record RoomAdmission(
    string ParticipantId,
    string Token,
    int HistoryBytes,
    int BoardBytes,
    long Sequence,
    int ManifestBytes,
    int AssetBytes,
    bool Seeded);

/// <summary>Checks drawing targets against their recorded room owners.</summary>
public sealed class RoomOwnership
{
    private readonly Dictionary<Guid, string> _elements = [];
    private readonly Dictionary<Guid, string> _actions = [];

    /// <summary>
    /// Admit a complete edit before reserving any IDs, including all targets of a batch.
    /// </summary>
    public bool Accept(Event edit, string owner, bool seeding = false)
    {
        if (_actions.TryGetValue(edit.ActionId, out var actor) && actor != owner) return false;
        // which IDs will this edit introduce?
        var created = edit switch
        {
            StartStrokeEvent e => [e.StrokeId], AddTextEvent e => [e.StrokeId], AddImageEvent e => [e.ImageId],
            StartEraseStrokeEvent e => [e.StrokeId], CreateSelectionBoundEvent e => [e.BoundId],
            SelectByIdsEvent e => [e.BoundId],
            PasteCanvasElementsEvent e => e.CopiedElements.Select(i => i.Id).Append(e.SelectionBoundId),
            LoadCanvasEvent e when seeding => e.CanvasElements.Select(i => i.Id),
            _ => []
        };

        // which existing IDs must the participant own?
        var targets = edit switch
        {
            PencilStrokeLineToEvent e => [e.StrokeId], LineStrokeLineToEvent e => [e.StrokeId],
            AddPolylineNodeEvent e => [e.StrokeId], MovePolylineNodeEvent e => [e.StrokeId],
            EraseStrokeLineToEvent e => [e.StrokeId], TriggerEraseEvent e => [e.StrokeId],
            EraseByIdsEvent e => e.ElementIds, SelectByIdsEvent e => e.ElementIds,
            EndSelectionEvent e => [e.BoundId], IncreaseSelectionBoundEvent e => [e.BoundId],
            MoveCanvasElementsEvent e => [e.BoundId], RotateCanvasElementsEvent e => [e.BoundId],
            ScaleCanvasElementsEvent e => [e.BoundId], UpdateTextEvent e => [e.TextStrokeId],
            UpdateStrokeColorEvent e => e.StrokeIds, UpdateStrokeThicknessEvent e => e.StrokeIds,
            UpdateStrokeFillColorEvent e => e.StrokeIds, UpdateStrokeEdgeTypeEvent e => e.StrokeIds,
            UpdateStrokeStyleEvent e => e.StrokeIds, UpdateFontSizeEvent e => e.StrokeIds,
            UpdateFontStyleEvent e => e.TextStrokeIds, UpdateFontCasingEvent e => e.TextStrokeIds,
            SetElementLayerEvent e => e.TargetElementIds, NudgeElementLayerEvent e => e.TargetElementIds,
            UndoEvent e when _actions.GetValueOrDefault(e.TargetActionId) == owner => [],
            RedoEvent e when _actions.GetValueOrDefault(e.TargetActionId) == owner => [],
            LoadCanvasEvent when seeding => [],
            StartStrokeEvent or AddTextEvent or AddImageEvent or StartEraseStrokeEvent or CreateSelectionBoundEvent
                or ClearSelectionEvent or EndStrokeEvent or PasteCanvasElementsEvent => [],
            _ => null
        };

        if (targets == null || targets.Any(id => _elements.GetValueOrDefault(id) != owner)) return false;

        var ids = created.ToArray();
        if (ids.Any(_elements.ContainsKey) || ids.Distinct().Count() != ids.Length) return false;
        foreach (var id in ids) _elements[id] = owner;
        _actions[edit.ActionId] = owner;
        return true;
    }
}