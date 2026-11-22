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

    public event Action<string>? CanvasStateRequested;

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

    // Recheck the session when queued UI work actually runs.
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

        connection.On<string>("RequestCanvasState", target =>
            ForSessionAsync(connection, () =>
            {
                // A new host must install its own room canvas before sharing it.
                if (_pendingJoin is { } pending) pending.SnapshotRequests.Add(target);
                else CanvasStateRequested?.Invoke(target);
            }));

        connection.On<string>("ReceiveCanvasState", serialized => StageSnapshotAsync(connection, serialized));

        connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientJoined", (client, clients) =>
            ForSessionAsync(connection, () =>
            {
                UpdateRoster(client, clients, ClientJoinedRoom);
                if (_pendingJoin is { } pending && client.ConnectionId == _room?.Me.ConnectionId)
                {
                    pending.Announced.TrySetResult(clients.Count > 0 && clients[0].ConnectionId == client.ConnectionId);
                }
            }));

        connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientLeft", (client, clients) =>
            ForSessionAsync(connection, () => UpdateRoster(client, clients, ClientLeftRoom)));

        connection.On<Message>("ReceiveMessage", message =>
            ForSessionAsync(connection, () => MessageReceived?.Invoke(message)));

        connection.On<string>("MessageSent", id =>
            ForSessionAsync(connection, () => MessageSent?.Invoke(id)));
    }

    // Deserialize off the UI thread and stage the snapshot for this join only.
    private Task StageSnapshotAsync(HubConnection connection, string serialized)
    {
        Queue<Event>? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<Queue<Event>>(serialized);
        }
        catch (JsonException)
        {
            snapshot = null;
        }

        return ForSessionAsync(connection, () =>
        {
            if (_pendingJoin is not { } pending) return;
            pending.SnapshotReceived = true;
            pending.Snapshot = snapshot;
        });
    }

    // Replace the room's member list and notify listeners of the join or leave.
    private void UpdateRoster(MultiUserDrawingClient client, List<MultiUserDrawingClient> clients,
        Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? notify)
    {
        if (_room is not { } room) return;
        _room = new MultiUserDrawingRoom(room.RoomId, room.Me.ConnectionId, room.Me.Name) { Clients = clients };
        notify?.Invoke(client, clients);
        RosterChanged?.Invoke(_room);
    }

    // Close, failed join, and explicit leave can all finish the same session.
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
                pending.Announced.TrySetCanceled();
                JoiningChanged?.Invoke();
            }
        });

        if (ownsCleanup) await connection.DisposeAsync();
    }

    public async Task JoinRoomAsync(string roomId, string displayName)
    {
        HubConnection connection;
        var pending = new PendingJoin();
        lock (_gate)
        {
            if (_connection != null) return;
            connection = _createConnection();
            RegisterEventHandlers(connection);
            _connection = connection;
            _pendingJoin = pending;
        }

        try
        {
            await ForSessionAsync(connection, () => JoiningChanged?.Invoke());
            await connection.StartAsync();
            await ForSessionAsync(connection, () =>
            {
                var id = connection.ConnectionId ?? throw new InvalidOperationException("Room connection closed.");
                _room = new MultiUserDrawingRoom(roomId, id, displayName);
                ConnectionStarted?.Invoke();
                RoomChanged?.Invoke(_room);
            });
            await connection.InvokeAsync("JoinRoom", roomId, displayName);
            // The hub invocation can finish before its incoming notifications are dispatched.
            var firstMember = await pending.Announced.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ForSessionAsync(connection, () => CompleteJoin(pending, firstMember));
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to join room - {e.Message}");
            await EndSessionAsync(connection);
        }
    }

    // Install the room canvas only after the server confirms the join.
    private void CompleteJoin(PendingJoin pending, bool firstMember)
    {
        if (pending.Snapshot == null && (pending.SnapshotReceived || !firstMember))
        {
            throw new InvalidOperationException("The room joined without a valid canvas snapshot.");
        }

        RoomCanvasReady?.Invoke(pending.Snapshot, pending.Events);
        _pendingJoin = null;
        JoiningChanged?.Invoke();
        foreach (var target in pending.SnapshotRequests) CanvasStateRequested?.Invoke(target);
    }

    public async Task LeaveRoomAsync(string displayName)
    {
        HubConnection connection;
        MultiUserDrawingRoom? joinedRoom;
        lock (_gate)
        {
            if (_connection is null || _leaving) return;
            _leaving = true;
            connection = _connection;
            // Closing the transport cancels a join still waiting on the host.
            joinedRoom = IsJoining ? null : _room;
        }

        try
        {
            if (joinedRoom != null && connection.State == HubConnectionState.Connected)
            {
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
            if (_connection is not { } connection || _room is not { } room || IsJoining)
                return Task.CompletedTask;
            _lastSend = SendInOrderAsync(_lastSend, connection, room.RoomId, evt);
            return _lastSend;
        }
    }

    // Serialize drawing sends and retire queued edits with the connection that owns them.
    private async Task SendInOrderAsync(Task previous, HubConnection connection, string roomId, Event edit)
    {
        await previous.ConfigureAwait(false);
        try
        {
            Task send;
            lock (_gate)
            {
                if (_connection != connection || _deliveryFailed) return;
                if (_leaving) throw new InvalidOperationException("Room session is closing.");
                send = connection.InvokeAsync("SendEvent", roomId, edit);
            }

            await send.ConfigureAwait(false);
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

    // Hold incoming room data until joining succeeds or the transport closes.
    private sealed class PendingJoin
    {
        public List<Event> Events { get; } = [];
        public List<string> SnapshotRequests { get; } = [];
        public Queue<Event>? Snapshot { get; set; }

        public bool SnapshotReceived { get; set; }

        // The client's own roster announcement follows any host snapshot on the wire.
        public TaskCompletionSource<bool> Announced { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task SendCanvasStateToClientAsync(string targetId, Queue<Event> events)
    {
        var connection = _connection;
        return connection?.State == HubConnectionState.Connected
            ? connection.InvokeAsync("SendCanvasStateToClient", targetId, JsonSerializer.Serialize(events))
            : Task.CompletedTask;
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