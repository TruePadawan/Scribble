using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Scribble.Services.CanvasStateService.Context;
using Scribble.Services.CanvasStateService.Handlers;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService;

public class CanvasStateService : ICanvasStateService
{
    // State
    private List<CanvasElement> _canvasElements = [];
    public IReadOnlyList<CanvasElement> CanvasElements => _canvasElements;
    public Queue<Event> CanvasEvents { get; private set; } = [];
    public Guid? ActiveSelectionBoundId { get; private set; }
    public List<Guid> SelectedElementIds { get; private set; } = [];
    public SKColor BackgroundColor { get; private set; } = new(0, 0, 0, 162);

    public bool HasEvents => CanvasEvents.Count > 0;
    public bool IsLocalSelection(Guid boundId) => _localSelectionBoundIds.Contains(boundId);

    private readonly HashSet<Guid> _localSelectionBoundIds = [];

    // For fast-path optimizations
    private Dictionary<Guid, PaintableStroke> _strokeLookup = new();
    private Dictionary<Guid, EraserStroke> _eraserStrokeLookup = new();
    private Dictionary<Guid, SKPoint> _eraserHeadLookup = new();
    private Dictionary<Guid, SelectionBound> _selectionBoundLookup = new();
    private Dictionary<Guid, CanvasImage> _canvasImageLookup = new();

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
        _localSelectionBoundIds.Clear();

        CanvasEvents.Clear();
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

        if (@event is CreateSelectionBoundEvent ev && isLocalEvent)
        {
            _localSelectionBoundIds.Add(ev.BoundId);
        }
        else if (@event is PasteCanvasElementsEvent pasteEv && isLocalEvent)
        {
            _localSelectionBoundIds.Add(pasteEv.SelectionBoundId);
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
            CanvasEvents = events;
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

    private bool ApplyFastPathOptimization(Event @event)
    {
        var fastPathCtx = new FastPathContext
        {
            StrokeLookup = _strokeLookup,
            EraserStrokeLookup = _eraserStrokeLookup,
            EraserHeadLookup = _eraserHeadLookup,
            SelectionBoundLookup = _selectionBoundLookup,
            CanvasImageLookup = _canvasImageLookup,
            CanvasElements = _canvasElements,
            LocalSelectionBoundIds = _localSelectionBoundIds,
            OnCanvasInvalidated = CanvasInvalidated,
            OnSelectionInvalidated = SelectionInvalidated
        };

        bool applied = _dispatcher.TryFastPath(@event, fastPathCtx);

        // Sync back any selection state changes from the fast-path handler
        if (applied && fastPathCtx.SelectedElementIds != null)
        {
            ActiveSelectionBoundId = fastPathCtx.ActiveSelectionBoundId;
            SelectedElementIds = fastPathCtx.SelectedElementIds;
        }

        return applied;
    }

    private void ProcessEvent(Event @event, bool isLocalEvent = false)
    {
        CanvasEvents.Enqueue(@event);

        // Fast path: for pencil/line line-to events during active drawing,
        // apply directly to the existing stroke, no replay needed
        bool fastPathWasApplied = ApplyFastPathOptimization(@event);
        if (fastPathWasApplied)
        {
            if (@event is ITerminalEvent && isLocalEvent)
            {
                TrackAction(@event.ActionId);
            }
            return;
        }

        var staleActionIds = ReplayEvents();
        bool changed = false;
        bool currentActionIsStale = false;

        Queue<Event> nonStaleEvents = [];
        foreach (var canvasEvent in CanvasEvents)
        {
            if (!staleActionIds.Contains(canvasEvent.ActionId))
            {
                nonStaleEvents.Enqueue(canvasEvent);
            }
            else
            {
                changed = true;
                if (canvasEvent.ActionId == @event.ActionId)
                {
                    currentActionIsStale = true;
                }
            }
        }

        CanvasEvents = nonStaleEvents;

        if (changed)
        {
            ReplayEvents();
        }

        // Keep track of local non-stale actions for undo/redo functionality
        if (@event is ITerminalEvent && isLocalEvent && !currentActionIsStale)
        {
            TrackAction(@event.ActionId);
        }
    }

    /// <summary>
    /// Builds the latest state of the canvas from the events in the queue
    /// </summary>
    /// <returns></returns>
    private List<Guid> ReplayEvents()
    {
        // Determine which action IDs are hidden by Undo/Redo events
        var hiddenActionIds = new HashSet<Guid>();
        foreach (var canvasEvent in CanvasEvents)
        {
            if (canvasEvent is UndoEvent ev)
            {
                hiddenActionIds.Add(ev.TargetActionId);
            }
            else if (canvasEvent is RedoEvent redoEv)
            {
                hiddenActionIds.Remove(redoEv.TargetActionId);
            }
        }

        // Replay all visible events through registered handlers
        var ctx = new ReplayContext();
        foreach (var canvasEvent in CanvasEvents.Where(canvasEvent => !hiddenActionIds.Contains(canvasEvent.ActionId)))
        {
            _dispatcher.Dispatch(canvasEvent, ctx);
        }

        // Normalize layer indices and update service state
        NormalizeLayersAndUpdateState(ctx);
        return ctx.StaleActionIds;
    }

    /// <summary>
    /// Normalizes layer indices to be contiguous (0..N-1) while preserving relative ordering,
    /// then updates all service-level state from the replay context.
    /// </summary>
    private void NormalizeLayersAndUpdateState(ReplayContext ctx)
    {
        List<CanvasElement> elementsWithLayers =
            [..ctx.PaintableStrokes.Values.ToList(), ..ctx.CanvasImages.Values.ToList()];

        if (elementsWithLayers.Count > 0)
        {
            var distinctLayerIndices = elementsWithLayers
                .Select(e => e.LayerIndex)
                .Distinct()
                .OrderBy(index => index)
                .ToList();

            var layerRemap = new Dictionary<int, int>(distinctLayerIndices.Count);
            for (var i = 0; i < distinctLayerIndices.Count; i++)
            {
                layerRemap[distinctLayerIndices[i]] = i;
            }

            foreach (var element in elementsWithLayers)
            {
                element.LayerIndex = layerRemap[element.LayerIndex];
            }
        }

        DisposeOldState();

        _canvasElements = elementsWithLayers;
        _strokeLookup = ctx.PaintableStrokes;
        _eraserStrokeLookup = ctx.EraserStrokes;
        _eraserHeadLookup = ctx.EraserHeads;
        _selectionBoundLookup = ctx.SelectionBounds;
        _canvasImageLookup = ctx.CanvasImages;

        // Show the selection only on the client that is doing the selection
        var mySelectionBound = ctx.SelectionBounds.FirstOrDefault(pair => _localSelectionBoundIds.Contains(pair.Key));
        ActiveSelectionBoundId = mySelectionBound.Value != null ? mySelectionBound.Key : null;
        SelectedElementIds = mySelectionBound.Value?.Targets.ToList() ?? [];

        CanvasInvalidated?.Invoke();
        SelectionInvalidated?.Invoke();
    }

    private void DisposeOldState()
    {
        foreach (var stroke in _strokeLookup.Values)
        {
            stroke.Dispose();
        }

        foreach (var bound in _selectionBoundLookup.Values)
        {
            bound.Dispose();
        }

        foreach (var image in _canvasImageLookup.Values)
        {
            image.Dispose();
        }

        foreach (var eraser in _eraserStrokeLookup.Values)
        {
            eraser.Dispose();
        }
    }
}