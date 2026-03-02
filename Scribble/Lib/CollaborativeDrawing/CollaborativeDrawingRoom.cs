using System.Collections.Generic;
using Scribble.Shared.Lib;

namespace Scribble.Lib.CollaborativeDrawing;

/// <summary>
/// Represents a multi-user drawing room
/// </summary>
/// <param name="roomId">The unique ID of the room</param>
/// <param name="connectionId">The client's connection ID from SignalR</param>
/// <param name="displayName">The client's display name</param>
public class CollaborativeDrawingRoom(string roomId, string connectionId, string displayName)
{
    public string RoomId { get; } = roomId;
    public CollaborativeDrawingUser User { get; } = new(connectionId, displayName);
    public bool IsHost => Clients.Count >= 1 && Clients[0].ConnectionId == User.ConnectionId;
    public List<CollaborativeDrawingUser> Clients { get; init; } = [];
}