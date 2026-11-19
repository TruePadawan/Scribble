using System;
using System.Collections.Generic;
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
    private volatile HubConnection? _connection;
    private volatile MultiUserDrawingRoom? _room;
    private bool _joinPending;
    public MultiUserDrawingRoom? Room => _room;
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;
    public string? ConnectionId => _connection?.ConnectionId;

    public event Action<MultiUserDrawingRoom?>? RoomChanged;
    public event Action? ConnectionStarted;
    public event Action? ConnectionStopped;
    public event Action<Event>? EventReceived;
    public event Action<string>? CanvasStateRequested;
    public event Action<Queue<Event>>? CanvasStateReceived;
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
    private async Task ForSessionAsync(HubConnection connection, Action action)
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
        // Each handler belongs to this transport, including after a later rejoin.
        connection.Closed += _ => EndSessionAsync(connection);
        connection.On<Event>("ReceiveEvent", evt =>
            ForSessionAsync(connection, () => EventReceived?.Invoke(evt)));
        connection.On<string>("RequestCanvasState", target =>
            ForSessionAsync(connection, () => CanvasStateRequested?.Invoke(target)));
        connection.On<string>("ReceiveCanvasState", serialized =>
        {
            var events = JsonSerializer.Deserialize<Queue<Event>>(serialized);
            return ForSessionAsync(connection, () =>
            {
                if (events != null) CanvasStateReceived?.Invoke(events);
            });
        });
        connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientJoined", (client, clients) =>
            ForSessionAsync(connection, () => UpdateRoster(client, clients, ClientJoinedRoom)));
        connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientLeft", (client, clients) =>
            ForSessionAsync(connection, () => UpdateRoster(client, clients, ClientLeftRoom)));
        connection.On<Message>("ReceiveMessage", message =>
            ForSessionAsync(connection, () => MessageReceived?.Invoke(message)));
        connection.On<string>("MessageSent", id =>
            ForSessionAsync(connection, () => MessageSent?.Invoke(id)));
    }

    // Replace the room's member list and notify listeners of the join or leave.
    private void UpdateRoster(MultiUserDrawingClient client, List<MultiUserDrawingClient> clients,
        Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? notify)
    {
        if (_room is not { } room) return;
        _room = new MultiUserDrawingRoom(room.RoomId, room.Me.ConnectionId, room.Me.Name) { Clients = clients };
        notify?.Invoke(client, clients);
        RoomChanged?.Invoke(_room);
    }

    // Close, failed join, and explicit leave can all finish the same session.
    private async Task EndSessionAsync(HubConnection connection)
    {
        var ownsCleanup = false;
        await ForSessionAsync(connection, () =>
        {
            ownsCleanup = true;
            _connection = null;
            _joinPending = false;
            var hadRoom = _room != null;
            _room = null;
            if (hadRoom)
            {
                RoomChanged?.Invoke(null);
                ConnectionStopped?.Invoke();
            }
        });

        if (ownsCleanup) await connection.DisposeAsync();
    }

    public async Task JoinRoomAsync(string roomId, string displayName)
    {
        HubConnection connection;
        lock (_gate)
        {
            if (_connection != null) return;
            connection = _createConnection();
            RegisterEventHandlers(connection);
            _connection = connection;
            _joinPending = true;
        }

        try
        {
            await connection.StartAsync();
            await ForSessionAsync(connection, () =>
            {
                var id = connection.ConnectionId ?? throw new InvalidOperationException("Room connection closed.");
                _room = new MultiUserDrawingRoom(roomId, id, displayName);
                ConnectionStarted?.Invoke();
                RoomChanged?.Invoke(_room);
            });
            await connection.InvokeAsync("JoinRoom", roomId, displayName);
            lock (_gate)
            {
                if (_connection == connection) _joinPending = false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to join room - {e.Message}");
            await EndSessionAsync(connection);
        }
    }

    public async Task LeaveRoomAsync(string displayName)
    {
        HubConnection connection;
        MultiUserDrawingRoom? joinedRoom;
        lock (_gate)
        {
            if (_connection is null) return;
            connection = _connection;
            // Closing the transport cancels a join still waiting on the host.
            joinedRoom = _joinPending ? null : _room;
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
        var connection = _connection;
        var room = Room;
        return room != null && connection?.ConnectionId == room.Me.ConnectionId
            ? connection.InvokeAsync("SendEvent", room.RoomId, evt)
            : Task.CompletedTask;
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
