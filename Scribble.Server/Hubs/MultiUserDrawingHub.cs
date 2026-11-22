using Microsoft.AspNetCore.SignalR;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;

namespace Scribble.Server.Hubs;

/// <summary>Delivers room messages after the registry authorizes their sender.</summary>
public class MultiUserDrawingHub(RoomRegistry rooms) : Hub
{
    /// <summary>Reports whether a room needs its initial canvas history.</summary>
    public bool NeedsSeed(string roomId) => rooms.NeedsSeed(roomId);

    /// <summary>Joins a room and returns the participant's private reconnect token.</summary>
    public async Task<RoomAdmission> JoinRoom(string roomId, string name, string? token, string? seed)
    {
        if (Context.Items.ContainsKey("joined")) throw new HubException("Reconnect before joining again.");
        var joined = rooms.Join(Context.ConnectionId, roomId, name, token, seed);
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
        var accepted = rooms.Edit(Context.ConnectionId, roomId, edit);
        if (accepted.Edit != null) await Clients.Clients(accepted.Recipients).SendAsync("ReceiveEvent", accepted.Edit);
        return accepted.Edit?.RoomSequence;
    }

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