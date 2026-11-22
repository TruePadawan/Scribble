using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;

namespace Scribble.Services.MultiUserDrawing;

public class MultiUserDrawingService : IMultiUserDrawingService
{
    private readonly Func<HubConnection> _createConnection;
    private readonly Dispatcher? _dispatcher;

    private readonly Lock _gate = new();

    // Each send waits for the previous invocation on this connection.
    private Task _lastSend = Task.CompletedTask;
    private bool _deliveryFailed;
    private bool _leaving;
    private volatile HubConnection? _connection;
    private volatile MultiUserDrawingRoom? _room;
    private volatile PendingJoin? _pendingJoin;
    public MultiUserDrawingRoom? Room => _room;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    // Keep the canvas paused until the join is committed or cancelled.
    public bool IsJoining => _pendingJoin != null;
    public string? ConnectionId => _connection?.ConnectionId;

    public event Action<MultiUserDrawingRoom?>? RoomChanged;

    // Membership updates within the same session.
    public event Action<MultiUserDrawingRoom?>? RosterChanged;

    // Raised when delivery of a local edit could not be confirmed.
    public event Action? DrawingSyncFailed;

    public event Action? ConnectionStarted;
    public event Action? ConnectionStopped;
    public event Action<Event>? EventReceived;

    // Tokens belong to this service's server endpoint and live only in memory.
    private readonly Dictionary<string, string> _tokens = [];
    public Func<Queue<Event>>? CaptureSeed { get; set; }
    public bool SeededRoom { get; private set; }
    public event Action<string>? JoinFailed;
    public event Action? IdentityRenewed;

    // Deliver the snapshot and queued drawing updates in one UI callback.
    public event Action<Queue<Event>?, IReadOnlyList<Event>>? RoomCanvasReady;

    // Refresh editing controls when the join starts or ends.
    public event Action? JoiningChanged;
    public event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientJoinedRoom;
    public event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientLeftRoom;
    public event Action<Message>? MessageReceived;
    public event Action<string>? MessageSent;

    public MultiUserDrawingService(string serverUrl)
        : this(() => new HubConnectionBuilder().WithUrl(serverUrl).Build(), Dispatcher.UIThread)
    {
    }

    internal MultiUserDrawingService(Func<HubConnection> createConnection, Dispatcher? dispatcher = null)
    {
        _createConnection = createConnection;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Recheck the session when queued UI work actually runs.
    /// </summary>
    internal async Task ForSessionAsync(HubConnection connection, Action action)
    {
        void Run()
        {
            lock (_gate)
            {
                if (connection == _connection) action();
            }
        }

        if (_dispatcher is null || _dispatcher.CheckAccess()) Run();
        else await _dispatcher.InvokeAsync(Run);
    }

    private void RegisterEventHandlers(HubConnection connection)
    {
        connection.Closed += _ => EndSessionAsync(connection);
        connection.On<Event>("ReceiveEvent", evt =>
            ForSessionAsync(connection, () =>
            {
                if (_pendingJoin is { } pending) pending.Events.Add(evt);
                else EventReceived?.Invoke(evt);
            }));

        connection.On("SessionReplaced", () => { _ = EndSessionAsync(connection); });

        connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientJoined", (client, clients) =>
            ForSessionAsync(connection, () =>
            {
                if (_pendingJoin is { } pending) pending.Roster = clients;
                else UpdateRoster(client, clients, ClientJoinedRoom);
            }));

        connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientLeft", (client, clients) =>
            ForSessionAsync(connection, () =>
            {
                if (_pendingJoin is { } pending) pending.Roster = clients;
                else UpdateRoster(client, clients, ClientLeftRoom);
            }));

        connection.On<Message>("ReceiveMessage", message =>
            ForSessionAsync(connection, () => MessageReceived?.Invoke(message)));

        connection.On<string>("MessageSent", id =>
            ForSessionAsync(connection, () => MessageSent?.Invoke(id)));
    }

    /// <summary>
    /// Replace the room's member list and notify listeners of the join or leave.
    /// </summary>
    private void UpdateRoster(MultiUserDrawingClient client, List<MultiUserDrawingClient> clients,
        Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? notify)
    {
        if (_room is not { } room) return;
        _room = new MultiUserDrawingRoom(room.RoomId, room.Me.ConnectionId, room.Me.Name, room.Me.ParticipantId)
            { Clients = clients };
        notify?.Invoke(client, clients);
        RosterChanged?.Invoke(_room);
    }

    /// <summary>
    /// Close, failed join, and explicit leave can all finish the same session.
    /// </summary>
    private async Task EndSessionAsync(HubConnection connection)
    {
        var ownsCleanup = false;
        await ForSessionAsync(connection, () =>
        {
            ownsCleanup = true;
            if (!_lastSend.IsCompleted && !_deliveryFailed) DrawingSyncFailed?.Invoke();
            _lastSend = Task.CompletedTask;
            _deliveryFailed = false;
            _leaving = false;
            _connection = null;
            var pending = _pendingJoin;
            _pendingJoin = null;
            var hadRoom = _room != null;
            _room = null;
            if (hadRoom)
            {
                RoomChanged?.Invoke(null);
                ConnectionStopped?.Invoke();
            }

            if (pending != null)
            {
                JoiningChanged?.Invoke();
            }
        });

        if (ownsCleanup) await connection.DisposeAsync();
    }

    public async Task JoinRoomAsync(string roomId, string displayName)
    {
        HubConnection connection;
        var pending = new PendingJoin();
        try
        {
            lock (_gate)
            {
                if (_connection != null)
                {
                    return;
                }

                connection = _createConnection();
                RegisterEventHandlers(connection);
                _connection = connection;
                _pendingJoin = pending;
            }
        }
        catch (Exception error)
        {
            JoinFailed?.Invoke(error.Message);
            return;
        }

        try
        {
            await ForSessionAsync(connection, () => JoiningChanged?.Invoke());
            await connection.StartAsync();
            var needsSeed = await connection.InvokeAsync<bool>("NeedsSeed", roomId);
            string? seed = null;
            string? token = null;
            await ForSessionAsync(connection, () =>
            {
                _tokens.TryGetValue(roomId, out token);
                if (needsSeed) seed = JsonSerializer.Serialize(CaptureSeed?.Invoke() ?? new Queue<Event>());
            });
            if (seed != null && System.Text.Encoding.UTF8.GetByteCount(seed) > RoomProtocol.SnapshotBytes)
                throw new InvalidOperationException("Room history exceeds 8 MiB. Use a smaller board.");

            var admission = await connection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, displayName, token, seed);
            var snapshot = JsonSerializer.Deserialize<Queue<Event>>(admission.Snapshot)
                           ?? throw new InvalidOperationException("Invalid room snapshot.");

            await ForSessionAsync(connection, () =>
            {
                var id = connection.ConnectionId ?? throw new InvalidOperationException("Room connection closed.");
                var newIdentity = token != null && token != admission.Token;
                _tokens[roomId] = admission.Token;
                _room = new MultiUserDrawingRoom(roomId, id, displayName, admission.ParticipantId)
                    { Clients = pending.Roster };
                SeededRoom = admission.Seeded;
                ConnectionStarted?.Invoke();
                RoomCanvasReady?.Invoke(snapshot, pending.Events);
                _pendingJoin = null;
                RoomChanged?.Invoke(_room);
                JoiningChanged?.Invoke();
                if (newIdentity) IdentityRenewed?.Invoke();
            });
        }
        catch (Exception e)
        {
            await ForSessionAsync(connection, () => JoinFailed?.Invoke(e.Message));
            await EndSessionAsync(connection);
        }
    }

    public async Task LeaveRoomAsync(string displayName)
    {
        HubConnection connection;
        MultiUserDrawingRoom? joinedRoom;
        Task queuedEdits;
        lock (_gate)
        {
            if (_connection is null || _leaving) return;
            _leaving = true;
            connection = _connection;
            // Drain edits queued before Leave. Closing a pending join simply cancels admission.
            queuedEdits = _lastSend;
            joinedRoom = IsJoining ? null : _room;
        }

        try
        {
            if (joinedRoom != null && connection.State == HubConnectionState.Connected)
            {
                await queuedEdits.WaitAsync(TimeSpan.FromSeconds(5));
                await connection.InvokeAsync("LeaveRoom", joinedRoom.RoomId, displayName);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to leave room - {e.Message}");
        }
        finally
        {
            await EndSessionAsync(connection);
        }
    }

    public Task BroadcastEventAsync(Event evt)
    {
        lock (_gate)
        {
            if (_connection is not { } connection || _room is not { } room || IsJoining || _leaving)
                return Task.CompletedTask;
            _lastSend = SendInOrderAsync(_lastSend, connection, room.RoomId, evt);
            return _lastSend;
        }
    }

    /// <summary>
    /// Serialize drawing sends and retire queued edits with the connection that owns them.
    /// </summary>
    private async Task SendInOrderAsync(Task previous, HubConnection connection, string roomId, Event edit)
    {
        await previous.ConfigureAwait(false);
        try
        {
            Task<long?> send;
            lock (_gate)
            {
                if (_connection != connection || _deliveryFailed) return;
                send = connection.InvokeAsync<long?>("SendEvent", roomId, edit);
            }

            var confirmed = await send.ConfigureAwait(false);
            if (confirmed != null)
                await ForSessionAsync(connection, () =>
                    EventReceived?.Invoke(edit with
                    {
                        RoomSequence = confirmed, CreatorConnectionId = _room!.Me.ParticipantId
                    }));
        }
        catch (Exception exception)
        {
            Trace.TraceError("Room drawing send failed. {0}", exception);
            await ForSessionAsync(connection, () =>
            {
                _deliveryFailed = true;
                DrawingSyncFailed?.Invoke();
            });
        }
    }

    /// <summary>
    /// Hold incoming room data until joining succeeds or the transport closes.
    /// </summary>
    private sealed class PendingJoin
    {
        public List<Event> Events { get; } = [];
        public List<MultiUserDrawingClient> Roster { get; set; } = [];
    }

    public Task BroadcastMessageAsync(MessageDto message)
    {
        var connection = _connection;
        var room = Room;
        return room != null && connection?.ConnectionId == room.Me.ConnectionId
            ? connection.InvokeAsync("SendMessage", room.RoomId, message)
            : Task.CompletedTask;
    }
}