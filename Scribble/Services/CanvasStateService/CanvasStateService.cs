using System;
using System.Collections.Generic;
using System.Linq;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.Events;
using Scribble.Shared.Utils;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService;

public class CanvasStateService : ICanvasStateService
{
    private RoomOwnership? _owners;
    private readonly CanvasHistory _history;
    private readonly IMultiUserDrawingService _multiUserDrawingService;
    internal Guid? LastReplayCheckpointId => _history.LastReplayCheckpointId;
    public IReadOnlyList<CanvasElement> CanvasElements => _history.CanvasElements;
    public Queue<Event> CanvasEvents => _history.CanvasEvents;
    public Guid? ActiveSelectionBoundId => _history.ActiveSelectionBoundId;
    public IReadOnlyList<Guid> SelectedElementIds => _history.SelectedElementIds;
    public SKColor BackgroundColor => _history.BackgroundColor;
    public bool HasEvents => _history.HasEvents;
    public bool IsRoomActive => _multiUserDrawingService.IsJoining || _multiUserDrawingService.Room != null;
    public bool CanUndo => !_multiUserDrawingService.IsJoining && _history.CanUndo;
    public bool CanRedo => !_multiUserDrawingService.IsJoining && _history.CanRedo;

    public event Action? CanvasInvalidated;

    // Signals a change to the content written to a document.
    public event Action? ContentChanged;
    public event Action<IReadOnlyList<Guid>, bool>? SelectionInvalidated;
    public event Action? UndoRedoStateChanged;
    public event Action? BackgroundColorChanged;

    public void SetBackgroundColor(SKColor color)
    {
        if (!_multiUserDrawingService.IsJoining) _history.SetBackgroundColor(color);
    }

    public CanvasStateService(IMultiUserDrawingService multiUserDrawingService) : this(multiUserDrawingService, 100)
    {
    }

    // Allow short histories to exercise the production checkpoint path in tests.
    internal CanvasStateService(IMultiUserDrawingService multiUserDrawingService, int checkpointInterval)
    {
        _history = new CanvasHistory(checkpointInterval);
        _history.ContentChanged += () => ContentChanged?.Invoke();
        _history.CanvasInvalidated += () => CanvasInvalidated?.Invoke();
        _history.SelectionInvalidated += (ids, options) => SelectionInvalidated?.Invoke(ids, options);
        _history.BackgroundColorChanged += () => BackgroundColorChanged?.Invoke();
        _history.UndoRedoStateChanged += () => UndoRedoStateChanged?.Invoke();
        _multiUserDrawingService = multiUserDrawingService;

        _multiUserDrawingService.CaptureSeed = CaptureSeed;
        _multiUserDrawingService.EventReceived += edit =>
        {
            if (edit.CreatorConnectionId is { } owner) _owners?.Accept(edit, owner);
            _history.ApplyIncoming(edit);
        };
        _multiUserDrawingService.RoomCanvasReady += (snapshot, buffered) =>
        {
            _owners = new RoomOwnership();
            if (snapshot != null)
            {
                foreach (var edit in snapshot)
                {
                    if (edit.CreatorConnectionId is { } owner)
                    {
                        _owners.Accept(edit, owner, true);
                    }
                }
            }

            foreach (var edit in buffered)
            {
                if (edit.CreatorConnectionId is { } owner)
                {
                    _owners.Accept(edit, owner);
                }
            }

            _history.InstallConfirmedHistory(snapshot, buffered,
                _multiUserDrawingService.SeededRoom && !_seedIsBaseline);
        };
        _multiUserDrawingService.JoiningChanged += () => UndoRedoStateChanged?.Invoke();
        _multiUserDrawingService.ConnectionStarted += () =>
            _history.CreatorId =
                _multiUserDrawingService.Room?.Me.ParticipantId ?? _multiUserDrawingService.ConnectionId;
        _multiUserDrawingService.ConnectionStopped += () =>
        {
            _owners = null;
            _history.CreatorId = null;
        };
        _history.CreatorId = multiUserDrawingService.Room?.Me.ParticipantId ?? multiUserDrawingService.ConnectionId;
    }

    private bool _seedIsBaseline;

    /// <summary>
    /// Multiple previous authors can have overlapping selections. Preserve the visible board
    /// when bringing it into a fresh room instead of reinterpreting those gestures as one owner.
    /// </summary>
    private Queue<Event> CaptureSeed()
    {
        var history = CanvasEvents;
        _seedIsBaseline = history.Select(e => e.CreatorConnectionId).Distinct().Skip(1).Any();
        return _seedIsBaseline
            ? new Queue<Event>([new LoadCanvasEvent(Guid.NewGuid(), [.. CanvasElements])])
            : history;
    }

    public void Undo()
    {
        if (!CanUndo) return;
        ApplyEvent(_history.TakeUndo());
        UndoRedoStateChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        ApplyEvent(_history.TakeRedo());
        UndoRedoStateChanged?.Invoke();
    }

    public void ClearSelection()
    {
        // An empty clear would replay over selections restored by a later undo.
        if (ActiveSelectionBoundId != null)
        {
            ApplyEvent(new ClearSelectionEvent(Guid.NewGuid()));
        }
    }

    /// <summary>Hit-tests the marquee against the elements this participant can select.</summary>
    public List<Guid> GetEnclosedElementIds(SKRect bounds)
    {
        return
        [
            .. CanvasGeometryUtilities
                .GetEnclosedElementIds(bounds, CanvasElements, _multiUserDrawingService.Room?.Me.ParticipantId)
        ];
    }

    public List<CanvasElement> GetSelectedElements()
        => _history.GetSelectedElements();

    public void LoadCanvas(List<CanvasElement> elements)
    {
        if (IsRoomActive) return;

        _history.ResetHistory();
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
            @event = @event with { CreatorConnectionId = _multiUserDrawingService.Room.Me.ParticipantId };
        }

        if (isLocalEvent && _owners != null &&
            !_owners.Accept(@event, _multiUserDrawingService.Room!.Me.ParticipantId)) return;

        var accepted = isLocalEvent ? _history.ApplyLocal(@event) : _history.ApplyIncoming(@event);
        if (accepted && isLocalEvent && _multiUserDrawingService.Room != null)
        {
            _multiUserDrawingService.BroadcastEventAsync(@event);
        }
    }
}