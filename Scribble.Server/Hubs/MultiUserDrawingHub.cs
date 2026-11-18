using Microsoft.AspNetCore.SignalR;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;

namespace Scribble.Server.Hubs;

public class MultiUserDrawingHub : Hub
{
    // Keep room membership and snapshot requests in sync under one lock.
    private static readonly Lock MembershipGate = new();
    private static readonly Dictionary<string, List<Participant>> Rooms = new();

    private static readonly Dictionary<string, Participant> Members = new();

    // Maximum time a joining client waits for the host's canvas snapshot.
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(15);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await RemoveMemberAsync();
        await base.OnDisconnectedAsync(exception);
    }

    // Wait for the host's canvas before announcing a new member.
    public async Task JoinRoom(string roomId, string displayName)
    {
        Participant member;
        string? hostId;
        lock (MembershipGate)
        {
            // A repeated join is harmless while the connection is still in that room.
            if (Members.TryGetValue(Context.ConnectionId, out var existing) && existing.RoomId == roomId) return;
            if (Context.Items.ContainsKey(nameof(JoinRoom)))
            {
                throw new HubException("Reconnect before joining a room again.");
            }

            // Keep the join marker on the connection even after it leaves.
            Context.Items[nameof(JoinRoom)] = true;
            member = new Participant(roomId, new MultiUserDrawingClient(Context.ConnectionId, displayName));
            if (!Rooms.TryGetValue(roomId, out var room))
            {
                room = [];
                Rooms.Add(roomId, room);
            }

            hostId = room.FirstOrDefault(participant => participant.IsEstablished)?.Client.ConnectionId;
            if (hostId is null && room.Count > 0)
            {
                throw new HubException("The room has no synchronized host. Reconnect and try again.");
            }

            // The first member starts with their own canvas.
            member.RequestedHostId = hostId;
            member.IsEstablished = hostId is null;
            room.Add(member);
            Members.Add(Context.ConnectionId, member);
        }

        try
        {
            if (hostId is not null)
            {
                // Release the membership lock so the host can answer while this join waits.
                await Clients.Client(hostId).SendAsync("RequestCanvasState", member.Client.ConnectionId);
                if (!await member.SnapshotDelivered.Task.WaitAsync(SnapshotTimeout, Context.ConnectionAborted))
                {
                    throw new HubException("The room has no synchronized host. Reconnect and try again.");
                }
            }

            List<MultiUserDrawingClient> roster;
            lock (MembershipGate)
            {
                // The client may have disconnected while waiting for its snapshot.
                if (!Members.ContainsKey(Context.ConnectionId)) return;

                member.IsEstablished = true;
                roster = GetRoster(member.RoomId);
            }

            var clients = Clients.Clients([.. roster.Select(client => client.ConnectionId)]);
            await clients.SendAsync("ClientJoined", member.Client, roster);
            await clients.SendAsync("ReceiveMessage", CreateRoomMessage($"{displayName} just joined the room"));
        }
        catch (TimeoutException)
        {
            await RemoveMemberAsync();
            throw new HubException("Canvas synchronization timed out. Reconnect and try again.");
        }
        catch
        {
            await RemoveMemberAsync();
            throw;
        }
    }

    public Task LeaveRoom(string roomId, string displayName)
    {
        return RemoveMemberAsync(roomId, announce: true);
    }

    // Remove the connection and reassign any snapshot requests it was handling.
    private async Task RemoveMemberAsync(string? expectedRoomId = null, bool announce = false)
    {
        Participant member;
        List<MultiUserDrawingClient> roster;
        string[] pendingClientIds;
        string? newHostId;

        lock (MembershipGate)
        {
            if (!Members.TryGetValue(Context.ConnectionId, out var current) ||
                (expectedRoomId is not null && current.RoomId != expectedRoomId))
            {
                return;
            }

            member = current;
            Members.Remove(Context.ConnectionId);
            // Wake any join still waiting on this connection.
            member.SnapshotDelivered.TrySetResult(false);

            var room = Rooms[member.RoomId];
            room.Remove(member);
            roster = GetRoster(member.RoomId);
            if (room.Count == 0) Rooms.Remove(member.RoomId);

            // Hand unanswered requests to the next synchronized member.
            newHostId = roster.FirstOrDefault()?.ConnectionId;
            var pending = room.Where(participant => participant.RequestedHostId == member.Client.ConnectionId &&
                                                    !participant.SnapshotAccepted).ToArray();
            foreach (var participant in pending)
            {
                participant.RequestedHostId = newHostId;
                if (newHostId is null) participant.SnapshotDelivered.TrySetResult(false);
            }

            pendingClientIds = pending.Select(participant => participant.Client.ConnectionId).ToArray();
        }

        if (newHostId is not null)
        {
            foreach (var joiningClientId in pendingClientIds)
            {
                await Clients.Client(newHostId).SendAsync("RequestCanvasState", joiningClientId);
            }
        }

        if (member.IsEstablished && roster.Count > 0)
        {
            var clients = Clients.Clients(roster.Select(client => client.ConnectionId).ToList());
            await clients.SendAsync("ClientLeft", member.Client, roster);
            if (announce)
            {
                await clients.SendAsync("ReceiveMessage",
                    CreateRoomMessage($"{member.Client.Name} just left the room"));
            }
        }
    }

    // Relay drawing events to the sender's room using their verified connection ID.
    public async Task SendEvent(string roomId, Event @event)
    {
        if (@event.CreatorConnectionId is not null && @event.CreatorConnectionId != Context.ConnectionId) return;

        var recipients = GetRecipients(roomId, includeJoiningClients: true);
        if (recipients is null) return;

        await Clients.Clients(recipients).SendAsync("ReceiveEvent",
            @event with { CreatorConnectionId = Context.ConnectionId });
    }

    // Deliver a snapshot only for a pending request assigned to this host.
    public async Task SendCanvasStateToClient(string targetConnectionId, string serializedEvents)
    {
        Participant target;
        lock (MembershipGate)
        {
            if (!Members.TryGetValue(Context.ConnectionId, out var host) || !host.IsEstablished ||
                !Members.TryGetValue(targetConnectionId, out var joining) || joining.RoomId != host.RoomId ||
                joining.RequestedHostId != Context.ConnectionId || joining.SnapshotAccepted ||
                Environment.TickCount64 >= joining.SnapshotDeadline)
            {
                return;
            }

            target = joining;
            // Claim the request before releasing the lock so it can only be answered once.
            target.SnapshotAccepted = true;
        }

        try
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveCanvasState", serializedEvents);
            target.SnapshotDelivered.TrySetResult(true);
        }
        catch
        {
            target.SnapshotDelivered.TrySetResult(false);
            throw;
        }
    }

    public async Task SendMessage(string roomId, MessageDto messageDto)
    {
        if (string.IsNullOrWhiteSpace(messageDto.Content) || messageDto.Content.Length > 500) return;

        var recipients = GetRecipients(roomId, includeJoiningClients: false);
        if (recipients is null) return;

        await Clients.Clients(recipients).SendAsync("ReceiveMessage",
            new Message(messageDto.Id, Context.ConnectionId, messageDto.DisplayName, messageDto.Content));
        await Clients.Caller.SendAsync("MessageSent", messageDto.Id);
    }

    // Resolve recipients from server-side membership after validating the sender.
    private List<string>? GetRecipients(string roomId, bool includeJoiningClients)
    {
        lock (MembershipGate)
        {
            if (!Members.TryGetValue(Context.ConnectionId, out var sender) ||
                !sender.IsEstablished || sender.RoomId != roomId)
            {
                return null;
            }

            // Copy recipient IDs while membership is stable, then send outside the lock.
            return Rooms[sender.RoomId]
                .Where(member => member != sender && (includeJoiningClients || member.IsEstablished))
                .Select(member => member.Client.ConnectionId)
                .ToList();
        }
    }

    // List members who have finished joining the room.
    private static List<MultiUserDrawingClient> GetRoster(string roomId)
    {
        return Rooms[roomId].Where(member => member.IsEstablished).Select(member => member.Client).ToList();
    }

    // Build a chat message for a room membership change.
    private static Message CreateRoomMessage(string content)
    {
        return new Message(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "Scribble-Bot", content);
    }

    // Track a connection's room membership and any pending canvas snapshot.
    private sealed class Participant(string roomId, MultiUserDrawingClient client)
    {
        public string RoomId { get; } = roomId;
        public MultiUserDrawingClient Client { get; } = client;
        public string? RequestedHostId { get; set; }
        public bool SnapshotAccepted { get; set; }
        public bool IsEstablished { get; set; }
        public long SnapshotDeadline { get; } = Environment.TickCount64 + (long)SnapshotTimeout.TotalMilliseconds;

        // Signals whether the host's snapshot was sent to the joining client.
        public TaskCompletionSource<bool> SnapshotDelivered { get; } =
            // Resume the waiting join outside the code that signals completion.
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
