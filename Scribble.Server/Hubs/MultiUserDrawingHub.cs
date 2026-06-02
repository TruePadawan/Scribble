using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.AspNetCore.SignalR;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;

namespace Scribble.Server.Hubs;

public class MultiUserDrawingHub : Hub
{
    private static readonly ConcurrentDictionary<string, ImmutableList<MultiUserDrawingClient>> Rooms = new();
    private static readonly ConcurrentDictionary<string, string> UserToRoom = new();

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        if (UserToRoom.TryRemove(connectionId, out var roomId))
        {
            // Snapshot the current list so we can find the user before removing them
            MultiUserDrawingClient? user = null;
            var updatedList = Rooms.AddOrUpdate(roomId,
                // Room doesn't exist (shouldn't happen, but safe fallback)
                _ => ImmutableList<MultiUserDrawingClient>.Empty,
                // Remove the disconnected client from the room
                (_, list) =>
                {
                    user = list.Find(u => u.ConnectionId == connectionId);
                    return list.RemoveAll(u => u.ConnectionId == connectionId);
                });

            // Clean up empty rooms
            if (updatedList.IsEmpty)
            {
                Rooms.TryRemove(roomId, out _);
            }

            if (user != null)
            {
                await Clients.Group(roomId).SendAsync("ClientLeft", user, updatedList);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinRoom(string roomId, string displayName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        var user = new MultiUserDrawingClient(Context.ConnectionId, displayName);

        var usersInRoom = Rooms.AddOrUpdate(roomId,
            _ => ImmutableList.Create(user),
            (_, list) => list.Add(user));

        UserToRoom.TryAdd(Context.ConnectionId, roomId);

        if (usersInRoom.Count > 1)
        {
            var hostId = usersInRoom[0].ConnectionId;
            // Ask the room's host to send the canvas state to this new client
            await Clients.Client(hostId).SendAsync("RequestCanvasState", Context.ConnectionId);
        }

        await Clients.Group(roomId).SendAsync("ClientJoined", user, usersInRoom);

        var messageId = Guid.NewGuid().ToString("N");
        var scribbleBotId = Guid.NewGuid().ToString("N");
        var scribbleBotMessage = $"{displayName} just joined the room";
        await Clients.Group(roomId).SendAsync("ReceiveMessage",
            new Message(messageId, scribbleBotId, "Scribble-Bot", scribbleBotMessage));
    }

    public async Task LeaveRoom(string roomId, string displayName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

        if (!Rooms.TryGetValue(roomId, out var room)) return;

        var user = room.Find(u => u.ConnectionId == Context.ConnectionId);
        if (user != null)
        {
            UserToRoom.TryRemove(Context.ConnectionId, out _);

            var updatedList = Rooms.AddOrUpdate(roomId,
                _ => ImmutableList<MultiUserDrawingClient>.Empty,
                (_, list) => list.Remove(user));

            if (updatedList.IsEmpty)
            {
                Rooms.TryRemove(roomId, out _);
            }
            else
            {
                await Clients.Group(roomId).SendAsync("ClientLeft", user, updatedList);

                var messageId = Guid.NewGuid().ToString("N");
                var scribbleBotId = Guid.NewGuid().ToString("N");
                var scribbleBotMessage = $"{displayName} just left the room";
                await Clients.OthersInGroup(roomId).SendAsync("ReceiveMessage",
                    new Message(messageId, scribbleBotId, "Scribble-Bot", scribbleBotMessage));
            }
        }
    }

    public async Task SendEvent(string roomId, Event @event)
    {
        // Reject events that falsely claim to originate from a different connection.
        // This guard prevents a malicious client from spoofing another user's creator ID.
        if (@event.CreatorConnectionId != null &&
            @event.CreatorConnectionId != Context.ConnectionId)
        {
            return;
        }

        await Clients.OthersInGroup(roomId).SendAsync("ReceiveEvent", @event);
    }

    public async Task SendCanvasStateToClient(string targetConnectionId, string serializedEvents)
    {
        await Clients.Client(targetConnectionId).SendAsync("ReceiveCanvasState", serializedEvents);
    }

    public async Task SendMessage(string roomId, MessageDto messageDto)
    {
        if (string.IsNullOrWhiteSpace(messageDto.Content)) return;
        if (messageDto.Content.Length > 500) return;

        // Verify the sender actually belongs to this room
        if (!UserToRoom.TryGetValue(Context.ConnectionId, out var actualRoomId) || actualRoomId != roomId)
            return;

        var messageId = Guid.NewGuid().ToString("N");
        await Clients.Group(roomId).SendAsync("ReceiveMessage",
            new Message(messageId, Context.ConnectionId, messageDto.DisplayName, messageDto.Content));
    }
}