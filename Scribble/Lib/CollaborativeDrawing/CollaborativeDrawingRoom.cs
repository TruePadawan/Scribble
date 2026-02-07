using System.Collections.Generic;

namespace Scribble.Lib.CollaborativeDrawing;

public class CollaborativeDrawingRoom(string roomId, string connectionId)
{
    public string RoomId { get; } = roomId;
    private string ConnectionId { get; } = connectionId;
    public bool IsHost => UsersInRoom.Count >= 1 && UsersInRoom[0] == ConnectionId;
    public List<string> UsersInRoom { get; init; } = [];
}