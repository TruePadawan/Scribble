using Scribble.Services.CanvasStateService.Context;
using Scribble.Shared.Lib;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles the replay of a specific event type by applying its
/// effect to the ReplayContext during a full event-log rebuild.
/// </summary>
public interface IEventReplayHandler<in TEvent> where TEvent : Event
{
    void Replay(TEvent @event, ReplayContext ctx);
}