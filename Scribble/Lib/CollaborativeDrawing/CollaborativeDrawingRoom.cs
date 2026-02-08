using System.Collections.Generic;
using Scribble.Shared.Lib;

namespace Scribble.Lib.CollaborativeDrawing;

public class CollaborativeDrawingRoom(string roomId, string connectionId, string displayName)
{
    public string RoomId { get; } = roomId;
    public CollaborativeDrawingUser User { get; } = new(connectionId, displayName);
    public bool IsHost => Clients.Count >= 1 && Clients[0].ConnectionId == User.ConnectionId;
    public List<CollaborativeDrawingUser> Clients { get; init; } = [];
}