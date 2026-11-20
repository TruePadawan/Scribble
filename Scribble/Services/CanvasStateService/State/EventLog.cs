using System;
using System.Collections.Generic;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.Events;

namespace Scribble.Services.CanvasStateService.State;

public class EventLog
{
    private readonly List<Event> _events = [];
    private readonly HashSet<Guid> _eventIds = [];
    private readonly HashSet<Guid> _hiddenActionIds = [];

    public IReadOnlyList<Event> Events => _events;
    public HashSet<Guid> HiddenActionIds => [.._hiddenActionIds];

    // Ignore events already present in the host snapshot or live history.
    public bool Append(Event ev)
    {
        if (!_eventIds.Add(ev.Id)) return false;

        _events.Add(ev);
        ProcessEventVisibility(ev);
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
