using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Scribble.Shared.Lib;

namespace Scribble.Lib.CollaborativeDrawing;

public class CollaborativeDrawingService(string serverUrl)
{
    private readonly HubConnection _connection =
        new HubConnectionBuilder().WithUrl(serverUrl).WithAutomaticReconnect().Build();

    public HubConnectionState ConnectionState => _connection.State;
    public string? ConnectionId => _connection.ConnectionId;
    public event Action? ConnectionStarted;
    public event Action? ConnectionStopped;
    public event Action<Event>? EventReceived;
    public event Action<string>? CanvasStateRequested;
    public event Action<Queue<Event>>? CanvasStateReceived;
    public event Action<string, List<string>>? ClientJoinedRoom;
    public event Action<string, List<string>>? ClientLeftRoom;

    public async Task StartAsync()
    {
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

        _connection.On<string, List<string>>("ClientJoined",
            (clientId, usersInRoom) => ClientJoinedRoom?.Invoke(clientId, usersInRoom));

        _connection.On<string, List<string>>("ClientLeft",
            (clientId, usersInRoom) => ClientLeftRoom?.Invoke(clientId, usersInRoom));

        await _connection.StartAsync();
        ConnectionStarted?.Invoke();
    }

    public async Task StopAsync()
    {
        await _connection.StopAsync();
        ConnectionStopped?.Invoke();
    }

    public async Task JoinRoomAsync(string roomId)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("JoinRoom", roomId);
        }
        else
        {
            throw new Exception($"Failed to join room, Hub connection state - {_connection.State}");
        }
    }

    public async Task BroadcastEventAsync(string roomId, Event evt)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            // var serializedEvent = JsonSerializer.Serialize(evt);
            await _connection.InvokeAsync("SendEvent", roomId, evt);
        }
    }

    public async Task SendCanvasStateToClientAsync(string targetId, Queue<Event> events)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            var serializedEvents = JsonSerializer.Serialize(events);
            await _connection.InvokeAsync("SendCanvasStateToClient", targetId, serializedEvents);
        }
    }
}