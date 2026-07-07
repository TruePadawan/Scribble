using System;
using System.Collections.Generic;
using Scribble.Shared.Lib;

namespace Scribble.Services.CanvasStateService.State;

public class EventLog
{
    private readonly List<Event> _events = [];
    private readonly HashSet<Guid> _staleActionIds = [];
    private readonly HashSet<Guid> _hiddenActionIds = [];

    public IReadOnlyList<Event> Events => _events;
    public HashSet<Guid> HiddenActionIds => [.._hiddenActionIds];

    public void Append(Event ev)
    {
        _events.Add(ev);
        ProcessEventVisibility(ev);
    }

    public void MarkActionStale(Guid actionId)
    {
        _staleActionIds.Add(actionId);
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
                if (_events[i].ActionId == lastEventId.Value)
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
            // are already evaluated in the hidden/stale logic
            if (ev is UndoEvent or RedoEvent) continue;

            if (!_hiddenActionIds.Contains(ev.ActionId) && !_staleActionIds.Contains(ev.ActionId))
            {
                activeEvents.Add(ev);
            }
        }

        return activeEvents;
    }
}