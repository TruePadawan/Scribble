using System.Collections.Generic;

namespace Scribble.Lib;

public class LiveDrawingRoom(string roomId, string connectionId)
{
    public string RoomId { get; } = roomId;
    public string ConnectionId { get; set; } = connectionId;
    public bool IsHost => UsersInRoom.Count >= 1 && UsersInRoom[0] == ConnectionId;
    public List<string> UsersInRoom { get; set; } = [];
}