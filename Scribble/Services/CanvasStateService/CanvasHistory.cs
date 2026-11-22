using System;
using System.Collections.Generic;
using System.Linq;
using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.Events;
using Scribble.Shared.ReplayHandlers;
using Scribble.Shared.State;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService;

/// <summary>
/// Owns the materialized board, replay checkpoints and local undo history.
/// </summary>
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

    /// <summary>
    /// Notify document and display listeners when the background changes.
    /// </summary>
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

    /// <summary>
    /// Move a local action between stacks before recording its visibility event.
    /// </summary>
    public Event TakeUndo()
    {
        var action = _undoStack.Pop();
        _redoStack.Push(action);
        return new UndoEvent(Guid.NewGuid(), action);
    }

    /// <summary>
    /// Restore the most recently undone local action.
    /// </summary>
    public Event TakeRedo()
    {
        var action = _redoStack.Pop();
        _undoStack.Push(action);
        return new RedoEvent(Guid.NewGuid(), action);
    }

    /// <summary>
    /// Resolve the selected IDs against the current board.
    /// </summary>
    public List<CanvasElement> GetSelectedElements()
    {
        if (ActiveSelectionBoundId == null) return [];
        return
        [
            .. CanvasElements
                .Where(e => SelectedElementIds.Contains(e.Id))
        ];
    }

    /// <summary>
    /// Discard the previous document’s log, checkpoints and undo stacks.
    /// </summary>
    public void ResetHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();

        // Prevent cross-room state pollution by resetting completely
        _eventLog = new EventLog();
        _checkpointManager = new CheckpointManager();
    }

    /// <summary>
    /// Track undo ownership for edits made by this editor.
    /// </summary>
    public bool ApplyLocal(Event edit) => ProcessEvent(edit, true);

    /// <summary>
    /// Apply a received edit without adding to local undo.
    /// </summary>
    public bool ApplyIncoming(Event edit) => ProcessEvent(edit, false);

    /// <summary>
    /// Install the confirmed room history and merge updates received during the join.
    /// </summary>
    public void InstallConfirmedHistory(Queue<Event>? snapshot, IReadOnlyList<Event> bufferedEvents,
        bool keepUndo = false)
    {
        var previousLog = _eventLog;
        var previousCheckpoints = _checkpointManager;
        var previousBoard = CurrentState;
        var previousCheckpointId = LastReplayCheckpointId;
        if (snapshot != null)
        {
            _eventLog = new EventLog();
            _checkpointManager = new CheckpointManager();
            foreach (var edit in snapshot) _eventLog.Append(edit);
        }

        var changed = snapshot != null;
        foreach (var edit in bufferedEvents.OrderBy(e => e.RoomSequence))
        {
            changed |= _eventLog.Append(edit);
        }

        if (!changed) return;

        try
        {
            ReplayEvents();
        }
        catch when (snapshot != null)
        {
            // A snapshot that cannot be replayed must leave the solo board and undo history intact.
            _eventLog = previousLog;
            _checkpointManager = previousCheckpoints;
            CurrentState = previousBoard;
            LastReplayCheckpointId = previousCheckpointId;
            throw;
        }

        if (snapshot != null && !keepUndo)
        {
            _undoStack.Clear();
            _redoStack.Clear();
            if (CreatorId != null) RestoreUndoHistory();
        }

        if (snapshot != null || !CanvasContent.Equals(previousBoard.ElementsWithLayers, CanvasElements))
        {
            ContentChanged?.Invoke();
        }

        CanvasInvalidated?.Invoke();
        SelectionInvalidated?.Invoke(SelectedElementIds, true);
    }

    /// <summary>
    /// Reconnects recover only this participant's undoable actions and visibility changes.
    /// </summary>
    private void RestoreUndoHistory()
    {
        foreach (var edit in _eventLog.Events.Where(e => e.CreatorConnectionId == CreatorId))
        {
            switch (edit)
            {
                case UndoEvent undo when _undoStack.TryPeek(out var last) && last == undo.TargetActionId:
                    _redoStack.Push(_undoStack.Pop());
                    break;
                case RedoEvent redo when _redoStack.TryPeek(out var next) && next == redo.TargetActionId:
                    _undoStack.Push(_redoStack.Pop());
                    break;
                case ITerminalEvent when !CurrentState.StaleActionIds.Contains(edit.ActionId):
                    _undoStack.Push(edit.ActionId);
                    _redoStack.Clear();
                    break;
            }
        }
    }

    /// <summary>
    /// Pushes an action unto the undo stack
    /// </summary>
    private void TrackAction(Guid actionId)
    {
        _undoStack.Push(actionId);
        _redoStack.Clear();

        UndoRedoStateChanged?.Invoke();
    }

    /// <summary>
    /// Apply each ID once and publish the resulting content and selection changes.
    /// </summary>
    private bool ProcessEvent(Event @event, bool isLocalEvent)
    {
        if (!isLocalEvent && @event.RoomSequence.HasValue)
        {
            if (!_eventLog.Confirm(@event, out var rebuild)) return false;
            if (rebuild)
            {
                _checkpointManager = new CheckpointManager();
                ReplayEvents();
                ContentChanged?.Invoke();
                CanvasInvalidated?.Invoke();
                SelectionInvalidated?.Invoke(SelectedElementIds, true);
                return true;
            }
        }
        else if (!_eventLog.Append(@event)) return false;

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

    /// <summary>
    /// Apply an appended event and report whether the saved drawing changed.
    /// </summary>
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

    /// <summary>
    /// Only transforms with surviving targets can change persistent content.
    /// </summary>
    private bool HasTargets(Guid boundId)
    {
        return CurrentState.SelectionBounds.TryGetValue(boundId, out var bound) &&
               bound.Targets.Any(id =>
                   CurrentState.PaintableStrokes.ContainsKey(id) || CurrentState.CanvasImages.ContainsKey(id));
    }

    /// <summary>
    /// Rebuild visible elements and local selection from the latest usable checkpoint.
    /// </summary>
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