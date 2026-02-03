using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace Scribble.Lib;

public class LiveDrawingService(string serverUrl)
{
    private readonly HubConnection _connection =
        new HubConnectionBuilder().WithUrl(serverUrl).WithAutomaticReconnect().Build();

    public event Action? ConnectionStarted;
    public event Action? ConnectionStopped;
    public HubConnectionState ConnectionState => _connection.State;

    public async Task StartAsync()
    {
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
}