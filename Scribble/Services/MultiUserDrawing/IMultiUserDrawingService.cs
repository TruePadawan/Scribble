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
    // True while the connection is setting up its room canvas.
    bool IsJoining { get; }
    string? ConnectionId { get; }

    // The desktop service delivers lifecycle and network notifications on the UI thread.
    event Action? ConnectionStarted;
    event Action? ConnectionStopped;
    event Action<Event>? EventReceived;
    // Capture the solo history only when creating a fresh room.
    Func<Queue<Event>>? CaptureSeed { get; set; }
    event Action<string>? JoinFailed;
    event Action? IdentityRenewed;
    bool SeededRoom { get; }
    // Commit the optional host snapshot and buffered live events together.
    event Action<Queue<Event>?, IReadOnlyList<Event>>? RoomCanvasReady;
    // Notify the desktop when editing needs to pause or resume.
    event Action? JoiningChanged;
    event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientJoinedRoom;
    event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientLeftRoom;
    event Action<Message>? MessageReceived;
    event Action<string>? MessageSent;
    event Action<MultiUserDrawingRoom?>? RoomChanged;
    // Membership updates within the same session.
    event Action<MultiUserDrawingRoom?>? RosterChanged;
    // Raised when delivery of a local edit could not be confirmed.
    event Action? DrawingSyncFailed;


    // Methods
    Task JoinRoomAsync(string roomId, string displayName);
    Task LeaveRoomAsync(string displayName);
    Task BroadcastEventAsync(Event evt);
    Task BroadcastMessageAsync(MessageDto message);
}
