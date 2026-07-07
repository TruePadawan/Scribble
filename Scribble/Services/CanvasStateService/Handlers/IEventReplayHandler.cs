using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles the replay of a specific event type by applying its
/// effect to the CanvasState during a full event-log rebuild.
/// </summary>
public interface IEventReplayHandler<in TEvent> where TEvent : Event
{
    void Replay(TEvent @event, CanvasState state);
}