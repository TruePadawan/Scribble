using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;

namespace Scribble.Services.MultiUserDrawing;

public interface IMultiUserDrawingService
{
    MultiUserDrawingRoom? Room { get; }
    bool IsConnected { get; }

    // Network events
    event Action? ConnectionStarted;
    event Action? ConnectionStopped;
    event Action<Event>? EventReceived;
    event Action<string>? CanvasStateRequested;
    event Action<Queue<Event>>? CanvasStateReceived;
    event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientJoinedRoom;
    event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientLeftRoom;
    event Action<Message>? MessageReceived;
    event Action<string>? MessageSent;
    event Action<MultiUserDrawingRoom?>? RoomChanged;

    // Methods
    Task JoinRoomAsync(string roomId, string displayName);
    Task LeaveRoomAsync(string displayName);
    Task BroadcastEventAsync(Event evt);
    Task SendCanvasStateToClientAsync(string targetId, Queue<Event> events);
    Task BroadcastMessageAsync(MessageDto message);
}