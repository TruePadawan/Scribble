using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Scribble.Shared.Lib;

namespace Scribble.Messages;

// Messages sent FROM Multi-User drawing to Canvas
public record NetworkEventReceivedMessage(Event Event);

public record CanvasStateRequestedMessage(string TargetConnectionId);

public record CanvasStateReceivedMessage(Queue<Event> Events);

// Messages sent FROM Canvas TO Multi-User drawing
public record BroadcastEventMessage(string RoomId, Event Event);

public record SendCanvasStateMessage(string TargetId, Queue<Event> Events);

// A special request message so Multi-User drawing can ask Canvas if it has unsaved work
public class HasEventsRequestMessage : RequestMessage<bool>
{
}