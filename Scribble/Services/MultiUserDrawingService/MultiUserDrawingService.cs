using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Scribble.Lib;
using Scribble.Shared.Lib;

namespace Scribble.Services.MultiUserDrawingService;

public class MultiUserDrawingService(string serverUrl)
{
    private readonly HubConnection _connection =
        new HubConnectionBuilder().WithUrl(serverUrl).Build();

    public MultiUserDrawingRoom? Room { get; private set; }
    public event Action<MultiUserDrawingRoom?>? RoomChanged;
    public bool IsConnected => _connection.State == HubConnectionState.Connected;
    private string? ConnectionId => _connection.ConnectionId;

    // Network Events
    public event Action? ConnectionStarted;
    public event Action? ConnectionStopped;
    public event Action<Event>? EventReceived;
    public event Action<string>? CanvasStateRequested;
    public event Action<Queue<Event>>? CanvasStateReceived;
    public event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientJoinedRoom;
    public event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientLeftRoom;


    // Set up the event handlers and starts a connection to the SignalR server
    private async Task StartAsync()
    {
        if (IsConnected) return;

        // Listen for draw events from others in the room
        _connection.On<Event>("ReceiveEvent", @event => { EventReceived?.Invoke(@event); });

        // Only relevant when this client is the host, listens for requests for the canvas state from other clients
        _connection.On<string>("RequestCanvasState", clientId => CanvasStateRequested?.Invoke(clientId));

        // We're a new client joining a room, listens for the response from the host carrying the canvas state
        _connection.On<string>("ReceiveCanvasState", (serializedEvents) =>
        {
            var events = JsonSerializer.Deserialize<Queue<Event>>(serializedEvents);
            if (events != null)
            {
                CanvasStateReceived?.Invoke(events);
            }
        });

        _connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientJoined",
            (client, usersInRoom) =>
            {
                ClientJoinedRoom?.Invoke(client, usersInRoom);
                RefreshRoomClients(client, usersInRoom);
            });

        _connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientLeft",
            (client, usersInRoom) =>
            {
                ClientLeftRoom?.Invoke(client, usersInRoom);
                RefreshRoomClients(client, usersInRoom);
            });

        await _connection.StartAsync();
        ConnectionStarted?.Invoke();
    }

    // Kill the SignalR connection
    private async Task StopAsync()
    {
        await _connection.StopAsync();
        ConnectionStopped?.Invoke();
    }

    /// <summary>
    /// Joins a room
    /// </summary>
    /// <param name="roomId">Room ID</param>
    /// <param name="displayName">Client's display name</param>
    public async Task JoinRoomAsync(string roomId, string displayName)
    {
        try
        {
            await StartAsync();
            await _connection.InvokeAsync("JoinRoom", roomId, displayName);
            if (ConnectionId != null)
            {
                Room = new MultiUserDrawingRoom(roomId, ConnectionId, displayName);
                RoomChanged?.Invoke(Room);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to join room - {e.Message}");
            await StopAsync();

            Room = null;
            RoomChanged?.Invoke(null);
        }
    }

    /// <summary>
    /// Leaves a room
    /// </summary>
    public async Task LeaveRoomAsync()
    {
        try
        {
            if (Room != null && IsConnected)
            {
                await _connection.InvokeAsync("LeaveRoom", Room.RoomId);
                await StopAsync();
                Room = null;
                RoomChanged?.Invoke(null);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.WriteLine($"Failed to leave room, Hub connection state - {_connection.State}");
        }
    }

    /// <summary>
    /// Broadcasts an event to all other clients in a room
    /// </summary>
    /// <param name="evt">Event to be broadcast</param>
    public async Task BroadcastEventAsync(Event evt)
    {
        if (Room != null && IsConnected)
        {
            await _connection.InvokeAsync("SendEvent", Room.RoomId, evt);
        }
    }

    /// <summary>
    /// Sends the canvas state to a specific client
    /// </summary>
    /// <param name="targetId">Target client's connection ID</param>
    /// <param name="events">Latest queue of events in the room</param>
    public async Task SendCanvasStateToClientAsync(string targetId, Queue<Event> events)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            var serializedEvents = JsonSerializer.Serialize(events);
            await _connection.InvokeAsync("SendCanvasStateToClient", targetId, serializedEvents);
        }
    }

    /// <summary>
    /// Refreshes the room clients list
    /// </summary>
    /// <param name="client"></param>
    /// <param name="clients"></param>
    private void RefreshRoomClients(MultiUserDrawingClient client, List<MultiUserDrawingClient> clients)
    {
        if (Room == null || ConnectionId == null) return;
        Room = new MultiUserDrawingRoom(Room.RoomId, ConnectionId, Room.Me.Name)
        {
            Clients = clients
        };
        RoomChanged?.Invoke(Room);
        Console.WriteLine($"{client.Name} Joined");
    }
}