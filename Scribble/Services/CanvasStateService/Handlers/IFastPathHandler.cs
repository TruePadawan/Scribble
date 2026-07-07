using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Applies a fast-path optimization for a specific event type
/// by directly mutating the live canvas state (lookup dictionaries)
/// without triggering a full replay.
/// </summary>
public interface IFastPathHandler<in TEvent> where TEvent : Event
{
    bool TryApplyFastPath(TEvent @event, CanvasState state);
}