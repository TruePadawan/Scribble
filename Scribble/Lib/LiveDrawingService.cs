using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Scribble.Shared.Lib;

namespace Scribble.Lib;

public class LiveDrawingService(string serverUrl)
{
    private readonly HubConnection _connection =
        new HubConnectionBuilder().WithUrl(serverUrl).WithAutomaticReconnect().Build();

    public HubConnectionState ConnectionState => _connection.State;
    public event Action? ConnectionStarted;
    public event Action? ConnectionStopped;
    public event Action<Event>? EventReceived;
    public event Action<string>? CanvasStateRequested;
    public event Action<List<Stroke>>? CanvasStateReceived;

    public async Task StartAsync()
    {
        // Listen for draw events from others in the room
        _connection.On<Event>("ReceiveEvent", ev => EventReceived?.Invoke(ev));

        // Only relevant when this client is the host, listens for requests for the canvas state from other clients
        _connection.On<string>("RequestCanvasState", req => CanvasStateRequested?.Invoke(req));

        // We're a new client joining a room, listens for the response from the host carrying the canvas state
        _connection.On<List<Stroke>>("ReceiveCanvasState", req => CanvasStateReceived?.Invoke(req));

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
            Console.WriteLine($"Joined room - {roomId}");
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
            await _connection.InvokeAsync("SendEvent", roomId, evt);
        }
    }

    public async Task SendCanvasStateToClientAsync(string targetId, List<Stroke> strokes)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("SendCanvasStateToClient", targetId, strokes);
        }
    }
}