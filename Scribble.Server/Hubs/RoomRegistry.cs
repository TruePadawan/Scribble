using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Scribble.Shared.Lib;

namespace Scribble.Server.Hubs;

/// <summary>Keeps accepted history, ownership, and reconnect credentials for this server's rooms.</summary>
public sealed class RoomRegistry(TimeProvider clock) : IDisposable
{
    internal static readonly TimeSpan ReclaimWindow = TimeSpan.FromMinutes(10);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Room> _rooms = [];
    private readonly Dictionary<string, Room> _connections = [];
    private ITimer? _cleanup;

    /// <summary>Stores one participant's current connection and reclaim credential.</summary>
    private sealed class Identity
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public MultiUserDrawingClient? Client { get; set; }
        public DateTimeOffset Expires { get; set; }
    }

    /// <summary>Stores a room's membership, ownership records, and accepted event history.</summary>
    private sealed class Room(string code)
    {
        public string Code { get; } = code;
        public RoomOwnership Owners { get; } = new();
        public Dictionary<string, Identity> Identities { get; } = [];
        public Dictionary<Guid, string> History { get; } = [];
        public int Bytes { get; set; } = 2;

        /// <summary>Returns the participants currently connected to the room.</summary>
        public List<MultiUserDrawingClient> Roster() => Identities.Values
            .Select(i => i.Client).OfType<MultiUserDrawingClient>().ToList();
    }

    /// <summary>Expires disconnected credentials and removes rooms with no remaining participants.</summary>
    private void Expire()
    {
        lock (_gate)
        {
            foreach (var room in _rooms.Values.ToArray())
            {
                foreach (var id in room.Identities.Values.Where(i => i.Client == null && i.Expires <= clock.GetUtcNow())
                             .ToArray())
                    room.Identities.Remove(id.Token);
                if (room.Identities.Count == 0) _rooms.Remove(room.Code);
            }
        }
    }

    /// <summary>Checks whether joining this room requires an initial board.</summary>
    internal bool NeedsSeed(string code)
    {
        lock (_gate)
        {
            Expire();
            return !_rooms.ContainsKey(code);
        }
    }

    /// <summary>Creates or reclaims a participant and returns the current room snapshot.</summary>
    internal (RoomAdmission Admission, MultiUserDrawingClient Me, List<MultiUserDrawingClient> Roster, string? Old)
        Join(string connection, string code, string name, string? token, string? seed)
    {
        // Prepare a possible first board before taking the membership lock. Another join may win.
        var newcomer = new Identity();
        Room? prepared = null;
        if (seed != null && NeedsSeed(code))
        {
            if (Encoding.UTF8.GetByteCount(seed) > RoomProtocol.SnapshotBytes) throw TooLarge();
            prepared = new Room(code);
            foreach (var edit in JsonSerializer.Deserialize<List<Event>>(seed) ??
                                 throw new HubException("Invalid canvas."))
                Append(prepared, edit, newcomer.Id, true);
        }

        Identity identity;
        MultiUserDrawingClient me;
        List<MultiUserDrawingClient> roster;
        string? old;
        string[] history;
        bool fresh;
        lock (_gate)
        {
            Expire();
            if (_connections.ContainsKey(connection)) throw new HubException("Already joined.");
            fresh = !_rooms.TryGetValue(code, out var room);
            room ??= prepared ?? throw new HubException("Room expired during join. Join again to start a new room.");
            identity = (token == null ? null : room.Identities.GetValueOrDefault(token)) ?? newcomer;
            old = identity.Client?.ConnectionId;
            if (old != null) _connections.Remove(old);
            me = new MultiUserDrawingClient(connection, name) { ParticipantId = identity.Id };
            identity.Client = me;
            room.Identities[identity.Token] = identity;
            _rooms[code] = room;
            _connections.Add(connection, room);
            _cleanup ??= clock.CreateTimer(_ => Expire(), null, ReclaimWindow, ReclaimWindow);
            roster = room.Roster();
            history = room.History.Values.ToArray();
        }

        var snapshot = "[" + string.Join(',', history) + "]";
        return (new RoomAdmission(identity.Id, identity.Token, snapshot, fresh), me, roster, old);
    }

    /// <summary>Validates ownership, stamps, and appends a new event to room history.</summary>
    private static Event? Append(Room room, Event edit, string owner, bool seed)
    {
        if (room.History.ContainsKey(edit.Id)) return null;
        edit = edit with { CreatorConnectionId = owner, RoomSequence = room.History.Count + 1L };
        var json = JsonSerializer.Serialize(edit);
        var bytes = Encoding.UTF8.GetByteCount(json) + (room.History.Count == 0 ? 0 : 1);
        if (room.Bytes + bytes > RoomProtocol.SnapshotBytes) throw TooLarge();
        if (!room.Owners.Accept(edit, owner, seed))
            throw new HubException("This edit targets another participant or an unknown drawing.");
        room.History.Add(edit.Id, json);
        room.Bytes += bytes;
        return edit;
    }

    /// <summary>Creates the error returned when a room exceeds its history limit.</summary>
    private static HubException TooLarge() =>
        new("Room history exceeds 8 MiB. Use a smaller board or start a new room.");

    /// <summary>Accepts an edit and captures the current broadcast recipients atomically.</summary>
    internal (Event? Edit, List<string> Recipients) Edit(string connection, string code, Event edit)
    {
        lock (_gate)
        {
            var room = GetRoom(connection, code);
            var roster = room.Roster();
            var owner = roster.Single(c => c.ConnectionId == connection).ParticipantId;
            return (Append(room, edit, owner, false),
                roster.Where(c => c.ConnectionId != connection).Select(c => c.ConnectionId).ToList());
        }
    }

    /// <summary>Returns the active room for a connection or rejects the request.</summary>
    private Room GetRoom(string connection, string code) =>
        _connections.TryGetValue(connection, out var room) && room.Code == code
            ? room
            : throw new HubException("Join the room first.");

    /// <summary>Returns the other connected participants in the sender's room.</summary>
    internal List<string> Others(string connection, string code)
    {
        lock (_gate)
            return GetRoom(connection, code).Roster().Select(c => c.ConnectionId).Where(id => id != connection)
                .ToList();
    }

    /// <summary>Revokes a connection and retains its identity through the reclaim window.</summary>
    internal (MultiUserDrawingClient Me, List<MultiUserDrawingClient> Roster)? Leave(string connection)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connection, out var room)) return null;
            var identity = room.Identities.Values.Single(i => i.Client?.ConnectionId == connection);
            var me = identity.Client!;
            identity.Client = null;
            identity.Expires = clock.GetUtcNow() + ReclaimWindow;
            return (me, room.Roster());
        }
    }

    /// <summary>Stops the room expiry timer when the server shuts down.</summary>
    public void Dispose() => _cleanup?.Dispose();
}