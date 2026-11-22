using System;
using System.Collections.Generic;
using System.Linq;
using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.Events;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService;

// Owns the materialized board, replay checkpoints and local undo history.
internal sealed class CanvasHistory(int checkpointInterval = 100)
{
    private CanvasState CurrentState { get; set; } = new();
    private EventLog _eventLog = new();
    private CheckpointManager _checkpointManager = new();

    // Identifies the checkpoint used to replay the most recent event suffix.
    internal Guid? LastReplayCheckpointId { get; private set; }

    public IReadOnlyList<CanvasElement> CanvasElements => CurrentState.ElementsWithLayers;
    public Queue<Event> CanvasEvents => new(_eventLog.Events);
    public Guid? ActiveSelectionBoundId => CurrentState.ActiveSelectionBoundId;
    public IReadOnlyList<Guid> SelectedElementIds => CurrentState.SelectedElementIds;
    public SKColor BackgroundColor { get; private set; } = new(0, 0, 0, 162);

    public bool HasEvents => _eventLog.Events.Count > 0;

    private readonly Stack<Guid> _undoStack = [];
    private readonly Stack<Guid> _redoStack = [];
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    private readonly EventReplayDispatcher _dispatcher = new();

    public event Action? CanvasInvalidated;

    // Signals a change to the content written to a document.
    public event Action? ContentChanged;
    public event Action<IReadOnlyList<Guid>, bool>? SelectionInvalidated;
    public event Action? UndoRedoStateChanged;
    public event Action? BackgroundColorChanged;

    // Notify document and display listeners when the background changes.
    public void SetBackgroundColor(SKColor color)
    {
        if (BackgroundColor == color) return;
        BackgroundColor = color;
        ContentChanged?.Invoke();
        BackgroundColorChanged?.Invoke();
    }

    // Set the identity used to resolve local selections during replay.
    public string? CreatorId
    {
        get => CurrentState.MyConnectionId;
        set => CurrentState.MyConnectionId = value;
    }

    // Move a local action between stacks before recording its visibility event.
    public Event TakeUndo()
    {
        var action = _undoStack.Pop();
        _redoStack.Push(action);
        return new UndoEvent(Guid.NewGuid(), action);
    }

    // Restore the most recently undone local action.
    public Event TakeRedo()
    {
        var action = _redoStack.Pop();
        _undoStack.Push(action);
        return new RedoEvent(Guid.NewGuid(), action);
    }

    // Resolve the selected IDs against the current board.
    public List<CanvasElement> GetSelectedElements()
    {
        if (ActiveSelectionBoundId == null) return [];
        return CanvasElements
            .Where(e => SelectedElementIds.Contains(e.Id))
            .ToList();
    }

    // Discard the previous document’s log, checkpoints and undo stacks.
    public void ResetHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();

        // Prevent cross-room state pollution by resetting completely
        _eventLog = new EventLog();
        _checkpointManager = new CheckpointManager();
    }

    // Track undo ownership for edits made by this editor.
    public bool ApplyLocal(Event edit) => ProcessEvent(edit, true);

    // Apply a received edit without adding to local undo.
    public bool ApplyIncoming(Event edit) => ProcessEvent(edit, false);

    // Install the confirmed room history and merge updates received during the join.
    public void InstallConfirmedHistory(Queue<Event>? snapshot, IReadOnlyList<Event> bufferedEvents)
    {
        if (snapshot != null)
        {
            ResetHistory();
            foreach (var ev in snapshot) _eventLog.Append(ev);
        }

        // The first member keeps the existing canvas and its local undo history.
        var changed = snapshot != null;
        foreach (var ev in bufferedEvents)
        {
            changed |= _eventLog.Append(ev);
        }

        if (changed)
        {
            var previous = CurrentState;
            ReplayEvents();
            if (snapshot != null || !CanvasContent.Equals(previous.ElementsWithLayers, CanvasElements))
            {
                ContentChanged?.Invoke();
            }

            CanvasInvalidated?.Invoke();
            SelectionInvalidated?.Invoke(SelectedElementIds, true);
        }
    }

    /// <summary>
    /// Pushes an action unto the undo stack
    /// </summary>
    /// <param name="actionId"></param>
    private void TrackAction(Guid actionId)
    {
        _undoStack.Push(actionId);
        _redoStack.Clear();

        UndoRedoStateChanged?.Invoke();
    }

    // Apply each ID once and publish the resulting content and selection changes.
    private bool ProcessEvent(Event @event, bool isLocalEvent)
    {
        if (!_eventLog.Append(@event)) return false;

        var previousSelection = SelectedElementIds.ToArray();
        var selectionEvent = @event is CreateSelectionBoundEvent or IncreaseSelectionBoundEvent
            or EndSelectionEvent or ClearSelectionEvent or SelectByIdsEvent;
        var transient = selectionEvent || @event is StartEraseStrokeEvent or EraseStrokeLineToEvent or EndStrokeEvent;

        var contentChanged = ApplyToCanvas(@event, transient);
        if (contentChanged) ContentChanged?.Invoke();
        if (!selectionEvent && @event is not EndStrokeEvent) CanvasInvalidated?.Invoke();
        var selectionChanged = !previousSelection.SequenceEqual(SelectedElementIds);
        var refreshOptions = selectionChanged || (isLocalEvent && @event is EndSelectionEvent or SelectByIdsEvent);
        if (refreshOptions || (contentChanged && SelectedElementIds.Count > 0))
        {
            SelectionInvalidated?.Invoke(SelectedElementIds, refreshOptions || contentChanged);
        }

        // Keep track of local non-stale actions for undo/redo functionality
        if (@event is ITerminalEvent && isLocalEvent && !CurrentState.StaleActionIds.Contains(@event.ActionId))
        {
            TrackAction(@event.ActionId);
        }

        return true;
    }

    // Apply an appended event and report whether the saved drawing changed.
    private bool ApplyToCanvas(Event @event, bool transient)
    {
        // Fast paths mutate live elements. Rebuilds can compare their old and new materialized boards.
        var previousLine = @event is LineStrokeLineToEvent line &&
                           CurrentState.PaintableStrokes.TryGetValue(line.StrokeId, out var stroke)
            ? stroke.Path
            : null;
        if (_dispatcher.TryApplyIncrementally(@event, CurrentState))
        {
            return !transient && @event switch
            {
                MoveCanvasElementsEvent move => move.Delta != SKPoint.Empty && HasTargets(move.BoundId),
                RotateCanvasElementsEvent rotate => rotate.DegreesRad != 0 && HasTargets(rotate.BoundId),
                ScaleCanvasElementsEvent scale => scale.Scale != new SKPoint(1, 1) && HasTargets(scale.BoundId),
                LineStrokeLineToEvent update when previousLine != null =>
                    previousLine.ToSvgPathData() != CurrentState.PaintableStrokes[update.StrokeId].Path.ToSvgPathData(),
                _ => true
            };
        }

        var previous = CurrentState;
        ReplayEvents();
        return !transient && !CanvasContent.Equals(previous.ElementsWithLayers, CanvasElements);
    }

    // Only transforms with surviving targets can change persistent content.
    private bool HasTargets(Guid boundId)
    {
        return CurrentState.SelectionBounds.TryGetValue(boundId, out var bound) &&
               bound.Targets.Any(id =>
                   CurrentState.PaintableStrokes.ContainsKey(id) || CurrentState.CanvasImages.ContainsKey(id));
    }

    // Rebuild visible elements and local selection from the latest usable checkpoint.
    private void ReplayEvents()
    {
        _checkpointManager.TryGetValidCheckpoint(_eventLog.HiddenActionIds, out var bestState, out var lastEventId);
        LastReplayCheckpointId = lastEventId;
        var activeEvents = _eventLog.GetActiveEventsSince(lastEventId);
        bestState.MyConnectionId = CreatorId;

        foreach (var ev in activeEvents)
        {
            _dispatcher.Dispatch(ev, bestState);
        }

        // Show the selection only on the client that is doing the selection
        var myConnectionId = bestState.MyConnectionId;
        var mySelectionBound = bestState.SelectionBounds.Values
            .FirstOrDefault(b => b.CreatorConnectionId == myConnectionId);
        bestState.ActiveSelectionBoundId = mySelectionBound?.Id;
        bestState.SelectedElementIds.Clear();
        if (mySelectionBound != null)
        {
            bestState.SelectedElementIds.AddRange(mySelectionBound.Targets);
        }

        if (activeEvents.Count > checkpointInterval && _eventLog.Events.Count > 0)
        {
            _checkpointManager.CaptureCheckpoint(bestState, _eventLog.Events[^1].Id, _eventLog.HiddenActionIds);
        }

        CurrentState = bestState;
    }
}