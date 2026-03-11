using System.Collections.Generic;
using Scribble.Shared.Lib;

namespace Scribble.Services.MultiUserDrawing;

/// <summary>
/// Represents a multi-user drawing room
/// </summary>
/// <param name="roomId">The unique ID of the room</param>
/// <param name="connectionId">The client's connection ID from SignalR</param>
/// <param name="displayName">The client's display name</param>
public class MultiUserDrawingRoom(string roomId, string connectionId, string displayName)
{
    public string RoomId { get; } = roomId;
    public MultiUserDrawingClient Me { get; } = new(connectionId, displayName);
    public bool IsHost => Clients.Count >= 1 && Clients[0].ConnectionId == Me.ConnectionId;
    public List<MultiUserDrawingClient> Clients { get; init; } = [];
}