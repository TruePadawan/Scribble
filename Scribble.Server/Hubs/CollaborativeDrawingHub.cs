using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Scribble.Shared.Lib;

namespace Scribble.Server.Hubs;

public class CollaborativeDrawingHub : Hub
{
    private static readonly ConcurrentDictionary<string, List<CollaborativeDrawingUser>> Rooms = new();
    private static readonly ConcurrentDictionary<string, string> UserToRoom = new();

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        if (UserToRoom.ContainsKey(connectionId))
        {
            var roomId = UserToRoom[connectionId];
            UserToRoom.TryRemove(connectionId, out _);
            var user = Rooms[roomId].Find(user => user.ConnectionId == connectionId);
            if (user != null)
            {
                Rooms[roomId].Remove(user);
                await Clients.Group(roomId).SendAsync("ClientLeft", user, Rooms[roomId]);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinRoom(string roomId, string displayName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        var user = new CollaborativeDrawingUser(Context.ConnectionId, displayName);

        Rooms.AddOrUpdate(roomId,
            [user],
            // If room exists, add new client to room
            (key, list) =>
            {
                list.Add(user);
                return list;
            });
        UserToRoom.TryAdd(Context.ConnectionId, roomId);

        var usersInRoom = Rooms[roomId];
        if (usersInRoom.Count > 1)
        {
            var hostId = usersInRoom[0].ConnectionId;
            // Ask the room's host to send the canvas state to this new client
            await Clients.Client(hostId).SendAsync("RequestCanvasState", Context.ConnectionId);
        }

        await Clients.Group(roomId).SendAsync("ClientJoined", user, usersInRoom);
    }

    public async Task LeaveRoom(string roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

        var room = Rooms[roomId];
        var user = room.Find(user => user.ConnectionId == Context.ConnectionId);
        if (user != null)
        {
            UserToRoom.TryRemove(Context.ConnectionId, out _);
            room.Remove(user);
            if (room.Count == 0)
            {
                Rooms.TryRemove(roomId, out _);
            }
            else
            {
                await Clients.Group(roomId).SendAsync("ClientLeft", Context.ConnectionId, room);
            }
        }
    }

    public async Task SendEvent(string roomId, Event @event)
    {
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveEvent", @event);
    }

    public async Task SendCanvasStateToClient(string targetConnectionId, string serializedEvents)
    {
        await Clients.Client(targetConnectionId).SendAsync("ReceiveCanvasState", serializedEvents);
    }
}