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
    private const int CheckpointInterval = 100;

    public IReadOnlyList<CanvasElement> CanvasElements => CurrentState.ElementsWithLayers;
    public Queue<Event> CanvasEvents => new(_eventLog.Events);
    public Guid? ActiveSelectionBoundId => CurrentState.ActiveSelectionBoundId;
    public IReadOnlyList<Guid> SelectedElementIds => CurrentState.SelectedElementIds;
    public SKColor BackgroundColor { get; private set; } = new(0, 0, 0, 162);

    public bool HasEvents => _eventLog.Events.Count > 0;

    private readonly Stack<Guid> _undoStack = [];
    private readonly Stack<Guid> _redoStack = [];
    public bool CanUndo => !_multiUserDrawingService.IsJoining && _undoStack.Count > 0;
    public bool CanRedo => !_multiUserDrawingService.IsJoining && _redoStack.Count > 0;

    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private readonly EventReplayDispatcher _dispatcher;

    public event Action? CanvasInvalidated;
    public event Action? SelectionInvalidated;
    public event Action? UndoRedoStateChanged;
    public event Action? BackgroundColorChanged;

    public void SetBackgroundColor(SKColor color)
    {
        if (_multiUserDrawingService.IsJoining || BackgroundColor == color) return;
        BackgroundColor = color;
        BackgroundColorChanged?.Invoke();
    }

    public CanvasStateService(IMultiUserDrawingService multiUserDrawingService)
    {
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

    public Queue<Event> GetLocalCanvasEvents()
    {
        return new Queue<Event>(CanvasEvents.Where(IsLocalEvent).ToList());
    }

    public bool IsLocalEvent(Event @event)
    {
        var myConnectionId = _multiUserDrawingService.ConnectionId;
        return @event.CreatorConnectionId == myConnectionId;
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

        if (changed) ReplayEvents();
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

        // Fast path: for events during active drawing, apply directly to the existing state
        if (_dispatcher.TryFastPath(@event, CurrentState))
        {
            switch (@event)
            {
                case ITerminalEvent when isLocalEvent && !CurrentState.StaleActionIds.Contains(@event.ActionId):
                    TrackAction(@event.ActionId);
                    break;
            }

            CanvasInvalidated?.Invoke();
            SelectionInvalidated?.Invoke();
            return;
        }

        ReplayEvents();

        // Keep track of local non-stale actions for undo/redo functionality
        if (@event is ITerminalEvent && isLocalEvent && !CurrentState.StaleActionIds.Contains(@event.ActionId))
        {
            TrackAction(@event.ActionId);
        }
    }

    private void ReplayEvents()
    {
        _checkpointManager.TryGetValidCheckpoint(_eventLog.HiddenActionIds, out var bestState, out var lastEventId);
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

        if (activeEvents.Count > CheckpointInterval && _eventLog.Events.Count > 0)
        {
            _checkpointManager.CaptureCheckpoint(bestState, _eventLog.Events[^1].Id, _eventLog.HiddenActionIds);
        }

        CurrentState = bestState;

        CanvasInvalidated?.Invoke();
        SelectionInvalidated?.Invoke();
    }
}