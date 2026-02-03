using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Scribble.Shared.Lib;

namespace Scribble.Server.Hubs;

public class LiveDrawingHub : Hub
{
    private static readonly ConcurrentDictionary<string, List<string>> Rooms = new();

    public async Task JoinRoom(string roomId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        Rooms.AddOrUpdate(roomId,
            [Context.ConnectionId],
            // If room exists, add new client to room
            (key, list) =>
            {
                list.Add(Context.ConnectionId);
                return list;
            });

        var usersInRoom = Rooms[roomId];
        if (usersInRoom.Count > 1)
        {
            var hostId = usersInRoom[0];
            // Ask the room's host to send the canvas state to this new client
            await Clients.Client(hostId).SendAsync("RequestCanvasState", Context.ConnectionId);
        }
    }

    public async Task LeaveRoom(string roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

        var usersInRoom = Rooms[roomId];
        usersInRoom.Remove(Context.ConnectionId);
        if (usersInRoom.Count == 0)
        {
            Rooms.TryRemove(roomId, out _);
        }
    }

    public async Task SendEvent(string roomId, Event canvasEvent)
    {
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveEvent", canvasEvent);
    }

    public async Task SendCanvasStateToClient(string targetConnectionId, List<Stroke> currentStrokes)
    {
        await Clients.Client(targetConnectionId).SendAsync("ReceiveCanvasState", currentStrokes);
    }
}