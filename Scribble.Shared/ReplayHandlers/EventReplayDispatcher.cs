using Scribble.Shared.Lib;
using Scribble.Shared.Lib.Events;
using Scribble.Shared.State;

namespace Scribble.Shared.ReplayHandlers;

/// <summary>
/// Each event has one mutation. Registration also declares whether it can run on the live state.
/// </summary>
internal sealed class EventReplayDispatcher
{
    private readonly Dictionary<Type, Func<Event, CanvasState, bool>> _incremental = new();
    private readonly Dictionary<Type, Action<Event, CanvasState>> _rebuild = new();

    // Register event families explicitly so dispatch requires no runtime code generation.
    internal EventReplayDispatcher()
    {
        Rebuild<LoadCanvasEvent>(CanvasLifecycleReplayHandler.Apply, changesLayers: true);
        Rebuild<AddImageEvent>(CanvasLifecycleReplayHandler.Apply);
        Rebuild<SetElementLayerEvent>(CanvasLifecycleReplayHandler.Apply, changesLayers: true);
        Rebuild<NudgeElementLayerEvent>(CanvasLifecycleReplayHandler.Apply, changesLayers: true);
        Rebuild<PasteCanvasElementsEvent>(CanvasLifecycleReplayHandler.Apply, changesLayers: true);
        Rebuild<StartEraseStrokeEvent>(EraserReplayHandler.Apply);
        Incremental<EraseStrokeLineToEvent>(EraserReplayHandler.Apply);
        Rebuild<TriggerEraseEvent>(EraserReplayHandler.Apply, changesLayers: true);
        Rebuild<EraseByIdsEvent>(EraserReplayHandler.Apply, changesLayers: true);
        Rebuild<UpdateStrokeColorEvent>(PropertyReplayHandler.Apply);
        Rebuild<UpdateStrokeThicknessEvent>(PropertyReplayHandler.Apply);
        Rebuild<UpdateStrokeStyleEvent>(PropertyReplayHandler.Apply);
        Rebuild<UpdateStrokeFillColorEvent>(PropertyReplayHandler.Apply);
        Rebuild<UpdateStrokeEdgeTypeEvent>(PropertyReplayHandler.Apply);
        Rebuild<CreateSelectionBoundEvent>(SelectionReplayHandler.Apply);
        Incremental<IncreaseSelectionBoundEvent>(SelectionReplayHandler.Apply);
        Rebuild<EndSelectionEvent>(SelectionReplayHandler.Apply);
        Rebuild<ClearSelectionEvent>(SelectionReplayHandler.Apply);
        Incremental<SelectByIdsEvent>(SelectionReplayHandler.Apply);
        Incremental<StartStrokeEvent>(StrokeReplayHandler.Apply);
        Incremental<PencilStrokeLineToEvent>(StrokeReplayHandler.Apply);
        Incremental<LineStrokeLineToEvent>(StrokeReplayHandler.Apply);
        Incremental<AddPolylineNodeEvent>(StrokeReplayHandler.Apply);
        Incremental<MovePolylineNodeEvent>(StrokeReplayHandler.Apply);
        Incremental<EndStrokeEvent>(StrokeReplayHandler.Apply);
        Rebuild<AddTextEvent>(TextReplayHandler.Apply);
        Rebuild<UpdateTextEvent>(TextReplayHandler.Apply);
        Rebuild<UpdateFontSizeEvent>(TextReplayHandler.Apply);
        Rebuild<UpdateFontCasingEvent>(TextReplayHandler.Apply);
        Rebuild<UpdateFontStyleEvent>(TextReplayHandler.Apply);
        Incremental<MoveCanvasElementsEvent>(TransformReplayHandler.Apply);
        Incremental<RotateCanvasElementsEvent>(TransformReplayHandler.Apply);
        Incremental<ScaleCanvasElementsEvent>(TransformReplayHandler.Apply);
    }

    /// <summary>
    /// Bind an incremental operation for use by both incoming events and history replay.
    /// </summary>
    private void Incremental<T>(Func<T, CanvasState, bool> apply) where T : Event =>
        _incremental.Add(typeof(T), (ev, state) => apply((T)ev, state));

    /// <summary>
    /// Keep the ordered index current at each structural event, including during bulk replay.
    /// </summary>
    private void Rebuild<T>(Action<T, CanvasState> apply, bool changesLayers = false) where T : Event =>
        _rebuild.Add(typeof(T), (ev, state) =>
        {
            apply((T)ev, state);
            if (changesLayers) state.NormalizeLayers();
        });

    /// <summary>
    /// Rebuild when the operation or its missing target requires history resolution.
    /// </summary>
    internal bool TryApplyIncrementally(Event ev, CanvasState state) =>
        _incremental.TryGetValue(ev.GetType(), out var apply) && apply(ev, state);

    /// <summary>
    /// Replay the same operation against the state built from preceding visible events.
    /// </summary>
    internal void Dispatch(Event ev, CanvasState state)
    {
        if (_incremental.TryGetValue(ev.GetType(), out var apply)) apply(ev, state);
        else _rebuild[ev.GetType()](ev, state);
    }

    /// <summary>
    /// Undo and redo are consumed by EventLog before dispatch.
    /// </summary>
    internal bool Handles(Type type) => type == typeof(UndoEvent) || type == typeof(RedoEvent) ||
                                        _incremental.ContainsKey(type) || _rebuild.ContainsKey(type);
}