using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;

namespace Scribble.Server.Hubs;

/// <summary>Delivers room messages after the registry authorizes their sender.</summary>
public class MultiUserDrawingHub(RoomRegistry rooms) : Hub
{
    // SignalR creates a hub for each invocation. Its connection items survive between chunks.
    private RoomTransferState Transfer => Context.Items.TryGetValue(typeof(RoomTransferState), out var state)
        ? (RoomTransferState)state!
        : (RoomTransferState)(Context.Items[typeof(RoomTransferState)] = new RoomTransferState());

    /// <summary>Reports whether a room needs its initial canvas history.</summary>
    public bool NeedsSeed(string roomId) => rooms.NeedsSeed(roomId);

    /// <summary>Joins a room and returns the participant's private reconnect token.</summary>
    public async Task<RoomAdmission> JoinRoom(string roomId, string name, string? token, string? seed, int version)
    {
        if (Context.Items.ContainsKey("joined")) throw new HubException("Reconnect before joining again.");
        if (version != RoomProtocol.Version)
        {
            throw new HubException("Update Scribble to join this room.");
        }

        if (Transfer.Take("seed") is { } seedData)
        {
            seed = Encoding.UTF8.GetString(seedData);
        }

        var joined = rooms.Join(Context.ConnectionId, roomId, name, token, seed, Transfer.TakeImages());
        Transfer.BeginSnapshot(joined.Board, joined.History, joined.Manifest);
        Context.Items["joined"] = true;
        if (joined.Old != null) await Clients.Client(joined.Old).SendAsync("SessionReplaced");
        var clients = Clients.Clients(joined.Roster.Select(c => c.ConnectionId).ToArray());
        await clients.SendAsync("ClientJoined", joined.Me, joined.Roster);
        await clients.SendAsync("ReceiveMessage", Notice($"{name} just joined the room"));
        return joined.Admission;
    }

    /// <summary>Validates, records, and broadcasts a drawing edit.</summary>
    public async Task<long?> SendEvent(string roomId, Event edit)
    {
        var accepted = rooms.Edit(Context.ConnectionId, roomId, edit, Transfer.TakeImages());
        if (accepted.Edit != null)
        {
            var clients = Clients.Clients(accepted.Recipients);
            if (accepted.Bytes > RoomProtocol.ChunkBytes)
            {
                await clients.SendAsync("ReceiveLargeEvent", accepted.Edit.Id, accepted.Bytes);
            }
            else
            {
                await clients.SendAsync("ReceiveEvent", accepted.Edit);
            }
        }

        return accepted.Edit?.RoomSequence;
    }

    /// <summary>Publishes a large metadata edit through the usual ownership and sequencing checks.</summary>
    public Task<long?> SendUploadedEvent(string roomId)
    {
        if (Transfer.Take("edit") is not { } data ||
            JsonSerializer.Deserialize<Event>(data) is not { } edit)
        {
            throw new HubException("Missing drawing edit.");
        }

        return SendEvent(roomId, edit);
    }

    /// <summary>Checks whether an active room already stores this image.</summary>
    public bool HasImage(string key) => rooms.Asset(Context.ConnectionId, key) != null;

    /// <summary>Reports the encoded size of a live room image.</summary>
    public int ImageSize(string key) => rooms.Asset(Context.ConnectionId, key)?.Length ??
                                        throw new HubException("Unknown room image.");

    /// <summary>Stages one sequential upload, checking its size and content hash before use.</summary>
    public void UploadChunk(string key, int offset, byte[] data, bool last) => Transfer.Upload(key, offset, data, last);

    /// <summary>Returns a bounded slice from the participant's snapshot, large edit or room image.</summary>
    public byte[] ReadChunk(string key, int offset) => Transfer.Read(key, offset, rooms, Context.ConnectionId);

    /// <summary>Broadcasts a chat message to the sender's room.</summary>
    public async Task SendMessage(string roomId, MessageDto message)
    {
        if (string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 500) return;
        await Clients.Clients(rooms.Others(Context.ConnectionId, roomId)).SendAsync("ReceiveMessage",
            new Message(message.Id, Context.ConnectionId, message.DisplayName, message.Content));
        await Clients.Caller.SendAsync("MessageSent", message.Id);
    }

    /// <summary>Leaves the room while retaining the participant's reclaim window.</summary>
    public Task LeaveRoom(string roomId, string displayName) => Remove(true);

    /// <summary>Removes a disconnected transport from its active room.</summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Remove(false);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Revokes membership and notifies the remaining participants.</summary>
    private async Task Remove(bool announce)
    {
        if (rooms.Leave(Context.ConnectionId) is not { } left) return;
        var clients = Clients.Clients(left.Roster.Select(c => c.ConnectionId).ToArray());
        await clients.SendAsync("ClientLeft", left.Me, left.Roster);
        if (announce) await clients.SendAsync("ReceiveMessage", Notice($"{left.Me.Name} just left the room"));
    }

    /// <summary>Creates a system chat message for room membership changes.</summary>
    private static Message Notice(string body) => new(Guid.NewGuid().ToString("N"), "server", "Scribble-Bot", body);
}