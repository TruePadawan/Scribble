using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Scribble.Services.CanvasStateService.Handlers;
using Scribble.Services.CanvasStateService.State;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
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
    public List<Guid> SelectedElementIds => CurrentState.SelectedElementIds;
    public SKColor BackgroundColor { get; private set; } = new(0, 0, 0, 162);

    public bool HasEvents => _eventLog.Events.Count > 0;

    private readonly Stack<Guid> _undoStack = [];
    private readonly Stack<Guid> _redoStack = [];
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private readonly EventReplayDispatcher _dispatcher;

    public event Action? CanvasInvalidated;
    public event Action? SelectionInvalidated;
    public event Action? UndoRedoStateChanged;
    public event Action? BackgroundColorChanged;

    public void SetBackgroundColor(SKColor color)
    {
        if (BackgroundColor == color) return;
        BackgroundColor = color;
        BackgroundColorChanged?.Invoke();
    }

    public CanvasStateService(IMultiUserDrawingService multiUserDrawingService)
    {
        _multiUserDrawingService = multiUserDrawingService;

        _multiUserDrawingService.EventReceived += OnNetworkEventReceived;
        _multiUserDrawingService.CanvasStateReceived += OnCanvasStateReceived;
        _multiUserDrawingService.CanvasStateRequested += async (clientId) =>
        {
            await _multiUserDrawingService.SendCanvasStateToClientAsync(clientId, CanvasEvents);
        };

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
        if (_undoStack.Count == 0) return;
        var actionId = _undoStack.Pop();

        _redoStack.Push(actionId);
        ApplyEvent(new UndoEvent(Guid.NewGuid(), actionId), isLocalEvent: true);

        UndoRedoStateChanged?.Invoke();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var actionId = _redoStack.Pop();

        _undoStack.Push(actionId);
        ApplyEvent(new RedoEvent(Guid.NewGuid(), actionId), isLocalEvent: true);

        UndoRedoStateChanged?.Invoke();
    }

    public bool IsLocalSelection(Guid boundId)
    {
        if (!CurrentState.SelectionBounds.TryGetValue(boundId, out var bound)) return false;
        var myConnectionId = _multiUserDrawingService.Room?.Me.ConnectionId;
        return bound.CreatorConnectionId == myConnectionId;
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
        Dispatcher.UIThread.Post(() => { ProcessEvent(@event); });
    }

    // Event handler for processing the canvas state snapshot received from the room's host
    private void OnCanvasStateReceived(Queue<Event> events)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _eventLog = new EventLog();
            _checkpointManager = new CheckpointManager();
            foreach (var ev in events)
            {
                _eventLog.Append(ev);
            }

            ReplayEvents();
        });
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
        _eventLog.Append(@event);

        // Fast path: for events during active drawing, apply directly to the existing state
        if (_dispatcher.TryFastPath(@event, CurrentState))
        {
            switch (@event)
            {
                case ITerminalEvent when isLocalEvent:
                    TrackAction(@event.ActionId);
                    break;
                case IncreaseSelectionBoundEvent:
                    SelectionInvalidated?.Invoke();
                    break;
            }

            CanvasInvalidated?.Invoke();
            return;
        }

        ReplayEvents();

        // Keep track of local non-stale actions for undo/redo functionality
        if (@event is ITerminalEvent && isLocalEvent)
        {
            TrackAction(@event.ActionId);
        }
    }

    private void ReplayEvents()
    {
        _checkpointManager.TryGetValidCheckpoint(_eventLog.HiddenActionIds, out var bestState, out var lastEventId);
        var activeEvents = _eventLog.GetActiveEventsSince(lastEventId);

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
        var myConnectionId = _multiUserDrawingService.Room?.Me.ConnectionId;
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
            _checkpointManager.CaptureCheckpoint(bestState, _eventLog.Events[^1].ActionId, _eventLog.HiddenActionIds);
        }

        CurrentState = bestState;

        CanvasInvalidated?.Invoke();
        SelectionInvalidated?.Invoke();
    }
}