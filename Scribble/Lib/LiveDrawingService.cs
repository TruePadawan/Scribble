using System;
using System.Collections.Generic;
using System.Text.Json;
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
        _connection.On<string>("ReceiveEvent", ev =>
        {
            var @event = JsonSerializer.Deserialize<Event>(ev);
            if (@event != null)
            {
                EventReceived?.Invoke(@event);
            }
        });

        // Only relevant when this client is the host, listens for requests for the canvas state from other clients
        _connection.On<string>("RequestCanvasState", req => CanvasStateRequested?.Invoke(req));

        // We're a new client joining a room, listens for the response from the host carrying the canvas state
        _connection.On<string>("ReceiveCanvasState", (serializedStrokes) =>
        {
            var strokes = JsonSerializer.Deserialize<List<Stroke>>(serializedStrokes);
            if (strokes != null)
            {
                CanvasStateReceived?.Invoke(strokes);
            }
        });

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
            var serializedEvent = JsonSerializer.Serialize(evt);
            await _connection.InvokeAsync("SendEvent", roomId, serializedEvent);
        }
    }

    public async Task SendCanvasStateToClientAsync(string targetId, List<Stroke> strokes)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            var serializedStrokes = JsonSerializer.Serialize(strokes);
            await _connection.InvokeAsync("SendCanvasStateToClient", targetId, serializedStrokes);
        }
    }
}