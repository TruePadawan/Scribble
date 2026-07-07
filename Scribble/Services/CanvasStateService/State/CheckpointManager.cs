using System;
using System.Collections.Generic;

namespace Scribble.Services.CanvasStateService.State;

public class CheckpointManager : IDisposable
{
    private readonly List<CanvasCheckpoint> _checkpoints = [];
    private readonly int _maxCheckpoints;

    public CheckpointManager(int maxCheckpoints = 5)
    {
        _maxCheckpoints = maxCheckpoints;
    }

    public void CaptureCheckpoint(CanvasState state, Guid? lastEventId, HashSet<Guid> hiddenActionIds)
    {
        var checkpoint = new CanvasCheckpoint(state.Clone(), lastEventId, hiddenActionIds);
        _checkpoints.Add(checkpoint);

        while (_checkpoints.Count > _maxCheckpoints)
        {
            var oldest = _checkpoints[0];
            _checkpoints.RemoveAt(0);
            oldest.Dispose();
        }
    }

    public bool TryGetValidCheckpoint(HashSet<Guid> currentHiddenActionIds, out CanvasState state,
        out Guid? lastEventId)
    {
        for (var i = _checkpoints.Count - 1; i >= 0; i--)
        {
            var checkpoint = _checkpoints[i];
            if (checkpoint.HiddenActionIdsAtCreation.SetEquals(currentHiddenActionIds))
            {
                state = checkpoint.State.Clone();
                lastEventId = checkpoint.LastAppliedEventId;
                return true;
            }

            // If the checkpoint is no longer valid because hidden actions changed, 
            // we should conceptually drop it and any before it, because undo history was rewritten.
            checkpoint.Dispose();
            _checkpoints.RemoveAt(i);
        }

        state = new CanvasState();
        lastEventId = null;
        return false;
    }

    public void Dispose()
    {
        foreach (var checkpoint in _checkpoints)
        {
            checkpoint.Dispose();
        }

        _checkpoints.Clear();
    }
}