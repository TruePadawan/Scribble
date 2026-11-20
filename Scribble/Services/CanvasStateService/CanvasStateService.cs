using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Scribble.Services.CanvasStateService.Handlers;
using Scribble.Services.CanvasStateService.State;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.Events;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService;

public class CanvasStateService : ICanvasStateService
{
    private CanvasState CurrentState { get; set; } = new();
    private EventLog _eventLog = new();
    private CheckpointManager _checkpointManager = new();
    private readonly int _checkpointInterval;

    // Identifies the checkpoint used to replay the most recent event suffix.
    internal Guid? LastReplayCheckpointId { get; private set; }

    public IReadOnlyList<CanvasElement> CanvasElements => CurrentState.ElementsWithLayers;
    public Queue<Event> CanvasEvents => new(_eventLog.Events);
    public Guid? ActiveSelectionBoundId => CurrentState.ActiveSelectionBoundId;
    public IReadOnlyList<Guid> SelectedElementIds => CurrentState.SelectedElementIds;
    public SKColor BackgroundColor { get; private set; } = new(0, 0, 0, 162);

    public bool IsRoomActive => _multiUserDrawingService.IsJoining || _multiUserDrawingService.Room != null;
    public bool HasEvents => _eventLog.Events.Count > 0;

    private readonly Stack<Guid> _undoStack = [];
    private readonly Stack<Guid> _redoStack = [];
    public bool CanUndo => !_multiUserDrawingService.IsJoining && _undoStack.Count > 0;
    public bool CanRedo => !_multiUserDrawingService.IsJoining && _redoStack.Count > 0;

    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private readonly EventReplayDispatcher _dispatcher;

    public event Action? CanvasInvalidated;

    // Signals a change to the content written to a document.
    public event Action? ContentChanged;
    public event Action<IReadOnlyList<Guid>, bool>? SelectionInvalidated;
    public event Action? UndoRedoStateChanged;
    public event Action? BackgroundColorChanged;

    public void SetBackgroundColor(SKColor color)
    {
        if (_multiUserDrawingService.IsJoining || BackgroundColor == color) return;
        BackgroundColor = color;
        ContentChanged?.Invoke();
        BackgroundColorChanged?.Invoke();
    }

    public CanvasStateService(IMultiUserDrawingService multiUserDrawingService) : this(multiUserDrawingService, 100)
    {
    }

    // Allow short histories to exercise the production checkpoint path in tests.
    internal CanvasStateService(IMultiUserDrawingService multiUserDrawingService, int checkpointInterval)
    {
        _checkpointInterval = checkpointInterval;
        _multiUserDrawingService = multiUserDrawingService;

        _multiUserDrawingService.EventReceived += OnNetworkEventReceived;
        _multiUserDrawingService.RoomCanvasReady += OnRoomCanvasReady;
        _multiUserDrawingService.JoiningChanged += () => UndoRedoStateChanged?.Invoke();
        _multiUserDrawingService.CanvasStateRequested += async (clientId) =>
        {
            try
            {
                // Network notifications arrive on the UI thread, which owns the event log.
                await _multiUserDrawingService.SendCanvasStateToClientAsync(clientId, CanvasEvents);
            }
            catch (Exception exception)
            {
                Trace.TraceError("Could not send the room canvas. {0}", exception);
            }
        };
        _multiUserDrawingService.ConnectionStarted += () =>
        {
            CurrentState.MyConnectionId = _multiUserDrawingService.ConnectionId;
        };

        _multiUserDrawingService.ConnectionStopped += () => CurrentState.MyConnectionId = null;

        _dispatcher = new EventReplayDispatcher(
            new StrokeReplayHandler(),
            new EraserReplayHandler(),
            new SelectionReplayHandler(),
            new TransformReplayHandler(),
            new TextReplayHandler(),
            new PropertyReplayHandler(),
            new CanvasLifecycleReplayHandler()
        );

#if DEBUG
        _dispatcher.ValidateCompleteness(typeof(Event).Assembly);
#endif
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var actionId = _undoStack.Pop();

        _redoStack.Push(actionId);
        ApplyEvent(new UndoEvent(Guid.NewGuid(), actionId), isLocalEvent: true);

        UndoRedoStateChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var actionId = _redoStack.Pop();

        _undoStack.Push(actionId);
        ApplyEvent(new RedoEvent(Guid.NewGuid(), actionId), isLocalEvent: true);

        UndoRedoStateChanged?.Invoke();
    }

    public void ClearSelection()
    {
        ApplyEvent(new ClearSelectionEvent(Guid.NewGuid()));
    }

    public List<CanvasElement> GetSelectedElements()
    {
        if (ActiveSelectionBoundId == null) return [];
        return CanvasElements
            .Where(e => SelectedElementIds.Contains(e.Id))
            .ToList();
    }

    public void LoadCanvas(List<CanvasElement> elements)
    {
        if (_multiUserDrawingService.IsJoining) return;

        _undoStack.Clear();
        _redoStack.Clear();

        // Prevent cross-room state pollution by resetting completely
        _eventLog = new EventLog();
        _checkpointManager = new CheckpointManager();

        ApplyEvent(new LoadCanvasEvent(Guid.NewGuid(), elements), isLocalEvent: false);

        UndoRedoStateChanged?.Invoke();
    }

    public void ApplyEvent(Event @event, bool isLocalEvent = true)
    {
        // Keep the current document intact until the room join has succeeded.
        if (_multiUserDrawingService.IsJoining) return;

        // Stamp the creator's connection ID on every local event while in a room.
        // Remote events arrive pre-stamped by their originating client.
        if (isLocalEvent && _multiUserDrawingService.Room != null)
        {
            @event = @event with { CreatorConnectionId = _multiUserDrawingService.Room.Me.ConnectionId };
        }

        ProcessEvent(@event, isLocalEvent);

        if (_multiUserDrawingService.Room != null)
        {
            // If the client is in a room, it broadcasts the event to other clients in the room
            _ = _multiUserDrawingService.BroadcastEventAsync(@event);
        }
    }

    // Event handler for when another client in the room draws something
    private void OnNetworkEventReceived(Event @event)
    {
        ProcessEvent(@event);
    }

    // Install the confirmed room history and merge updates received during the join.
    private void OnRoomCanvasReady(Queue<Event>? snapshot, IReadOnlyList<Event> bufferedEvents)
    {
        if (snapshot != null)
        {
            _eventLog = new EventLog();
            _checkpointManager = new CheckpointManager();
            _undoStack.Clear();
            _redoStack.Clear();
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

    private void ProcessEvent(Event @event, bool isLocalEvent = false)
    {
        if (!_eventLog.Append(@event)) return;

        var previous = CurrentState;
        var previousSelection = SelectedElementIds.ToArray();
        var selectionEvent = @event is CreateSelectionBoundEvent or IncreaseSelectionBoundEvent
            or EndSelectionEvent or ClearSelectionEvent or SelectByIdsEvent;
        var transient = selectionEvent || @event is StartEraseStrokeEvent or EraseStrokeLineToEvent or EndStrokeEvent;

        // Fast paths mutate live elements. Rebuilds can compare their old and new materialized boards.
        var previousLine = @event is LineStrokeLineToEvent line &&
                           previous.PaintableStrokes.TryGetValue(line.StrokeId, out var stroke)
            ? stroke.Path
            : null;
        bool contentChanged;
        if (_dispatcher.TryFastPath(@event, CurrentState))
        {
            contentChanged = !transient && @event switch
            {
                MoveCanvasElementsEvent move => move.Delta != SKPoint.Empty && HasTargets(move.BoundId),
                RotateCanvasElementsEvent rotate => rotate.DegreesRad != 0 && HasTargets(rotate.BoundId),
                ScaleCanvasElementsEvent scale => scale.Scale != new SKPoint(1, 1) && HasTargets(scale.BoundId),
                LineStrokeLineToEvent update when previousLine != null =>
                    previousLine.ToSvgPathData() != CurrentState.PaintableStrokes[update.StrokeId].Path.ToSvgPathData(),
                _ => true
            };
        }
        else
        {
            ReplayEvents();
            contentChanged = !transient && !CanvasContent.Equals(previous.ElementsWithLayers, CanvasElements);
        }

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
    }

    // Only transforms with surviving targets can change persistent content.
    private bool HasTargets(Guid boundId)
    {
        return CurrentState.SelectionBounds.TryGetValue(boundId, out var bound) &&
               bound.Targets.Any(id =>
                   CurrentState.PaintableStrokes.ContainsKey(id) || CurrentState.CanvasImages.ContainsKey(id));
    }

    private void ReplayEvents()
    {
        _checkpointManager.TryGetValidCheckpoint(_eventLog.HiddenActionIds, out var bestState, out var lastEventId);
        LastReplayCheckpointId = lastEventId;
        var activeEvents = _eventLog.GetActiveEventsSince(lastEventId);
        bestState.MyConnectionId =
            _multiUserDrawingService.Room?.Me.ConnectionId ?? _multiUserDrawingService.ConnectionId;

        foreach (var ev in activeEvents)
        {
            _dispatcher.Dispatch(ev, bestState);
        }

        // Forward any newly discovered stale actions to the event log
        foreach (var staleActionId in bestState.StaleActionIds)
        {
            _eventLog.MarkActionStale(staleActionId);
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

        bestState.NormalizeLayers();

        if (activeEvents.Count > _checkpointInterval && _eventLog.Events.Count > 0)
        {
            _checkpointManager.CaptureCheckpoint(bestState, _eventLog.Events[^1].Id, _eventLog.HiddenActionIds);
        }

        CurrentState = bestState;
    }
}