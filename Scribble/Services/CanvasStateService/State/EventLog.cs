using System;
using System.Collections.Generic;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.Events;

namespace Scribble.Services.CanvasStateService.State;

public class EventLog
{
    private readonly List<Event> _events = [];
    private readonly Dictionary<Guid, int> _positions = [];
    private int _confirmed;
    private readonly HashSet<Guid> _hiddenActionIds = [];

    public IReadOnlyList<Event> Events => _events;
    public HashSet<Guid> HiddenActionIds => [.. _hiddenActionIds];

    /// <summary>
    /// Ignore events already present in the host snapshot or live history.
    /// </summary>
    public bool Append(Event ev)
    {
        if (!_positions.TryAdd(ev.Id, _events.Count)) return false;
        if (ev.RoomSequence.HasValue) _confirmed++;

        _events.Add(ev);
        ProcessEventVisibility(ev);
        return true;
    }

    /// <summary>
    /// Insert confirmations in room order, ahead of any optimistic local edits.
    /// </summary>
    public bool Confirm(Event edit, out bool rebuild)
    {
        var position = _events.BinarySearch(0, _confirmed, edit,
            Comparer<Event>.Create((left, right) => Nullable.Compare(left.RoomSequence, right.RoomSequence)));
        rebuild = false;
        if (position >= 0) return false;
        position = ~position;
        if (_positions.TryGetValue(edit.Id, out var previous))
        {
            if (previous == position)
            {
                _events[position] = edit;
                _confirmed++;
                return false;
            }

            _events.RemoveAt(previous);
        }

        rebuild = position < _events.Count;
        _events.Insert(position, edit);
        _confirmed++;
        for (var i = position; i < _events.Count; i++) _positions[_events[i].Id] = i;
        if (rebuild)
        {
            _hiddenActionIds.Clear();
            foreach (var ev in _events) ProcessEventVisibility(ev);
        }
        else ProcessEventVisibility(edit);

        return true;
    }

    private void ProcessEventVisibility(Event ev)
    {
        if (ev is UndoEvent undoEvent)
        {
            _hiddenActionIds.Add(undoEvent.TargetActionId);
        }
        else if (ev is RedoEvent redoEvent)
        {
            _hiddenActionIds.Remove(redoEvent.TargetActionId);
        }
    }

    public List<Event> GetActiveEventsSince(Guid? lastEventId)
    {
        var activeEvents = new List<Event>();
        var startIndex = 0;

        if (lastEventId.HasValue)
        {
            for (var i = _events.Count - 1; i >= 0; i--)
            {
                if (_events[i].Id == lastEventId.Value)
                {
                    startIndex = i + 1;
                    break;
                }
            }
        }

        for (var i = startIndex; i < _events.Count; i++)
        {
            var ev = _events[i];

            // Skip undo/redo events themselves since their side effects 
            // are already evaluated in the visibility pass
            if (ev is UndoEvent or RedoEvent) continue;

            // Empty gestures can still clear a previous selection. Replay them in order.
            if (!_hiddenActionIds.Contains(ev.ActionId))
            {
                activeEvents.Add(ev);
            }
        }

        return activeEvents;
    }
}