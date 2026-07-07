using System;
using System.Collections.Generic;

namespace Scribble.Services.CanvasStateService.State;

public class CanvasCheckpoint
{
    public CanvasState State { get; }
    public Guid? LastAppliedEventId { get; }
    public HashSet<Guid> HiddenActionIdsAtCreation { get; }

    public CanvasCheckpoint(CanvasState state, Guid? lastAppliedEventId, HashSet<Guid> hiddenActionIdsAtCreation)
    {
        State = state;
        LastAppliedEventId = lastAppliedEventId;
        HiddenActionIdsAtCreation = new HashSet<Guid>(hiddenActionIdsAtCreation);
    }
}