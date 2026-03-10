using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.SignalR.Client;
using Scribble.Lib;
using Scribble.Messages;
using Scribble.Services.DialogService;
using Scribble.Services.MultiUserDrawingService;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels;

/// <summary>
/// View model for managing multi-user drawing
/// </summary>
public partial class MultiUserDrawingViewModel : ViewModelBase
{
    // Services
    private readonly MultiUserDrawingService _multiUserDrawingService;
    private readonly IDialogService _dialogService;

    // Observable properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResetCanvas))]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    [NotifyPropertyChangedFor(nameof(RoomButtonText))]
    [NotifyPropertyChangedFor(nameof(LiveDrawingButtonBackground))]
    [NotifyCanExecuteChangedFor(nameof(GenerateRoomIdCommand))]
    private MultiUserDrawingRoom? _room;

    // Tell the command to re-evaluate when the room id or client display name changes
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ToggleRoomConnectionCommand))]
    private string _roomId = string.Empty;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ToggleRoomConnectionCommand))]
    private string _clientDisplayName = "Bootlicker";

    public bool CanResetCanvas => Room == null || Room.IsHost;
    public bool IsLive => Room != null;
    private bool CanGenerateRoomId => Room == null;

    private bool CanToggleRoomConnection =>
        !string.IsNullOrWhiteSpace(RoomId) && !string.IsNullOrWhiteSpace(ClientDisplayName);

    public string RoomButtonText => IsLive ? "Leave Room" : "Enter Room";

    public IBrush LiveDrawingButtonBackground =>
        IsLive ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Transparent);


    public MultiUserDrawingViewModel(MultiUserDrawingService drawingService, IDialogService dialogService)
    {
        _multiUserDrawingService = drawingService;
        _dialogService = dialogService;

        _multiUserDrawingService.EventReceived += @event =>
        {
            WeakReferenceMessenger.Default.Send(new NetworkEventReceivedMessage(@event));
        };
        _multiUserDrawingService.CanvasStateRequested += targetId =>
        {
            WeakReferenceMessenger.Default.Send(new CanvasStateRequestedMessage(targetId));
        };

        _multiUserDrawingService.CanvasStateReceived += events =>
        {
            WeakReferenceMessenger.Default.Send(new CanvasStateReceivedMessage(events));
        };

        _multiUserDrawingService.ClientJoinedRoom += UpdateRoomClients;
        _multiUserDrawingService.ClientLeftRoom += UpdateRoomClients;

        // Listen for requests from the Canvas to send data outward via SignalR
        WeakReferenceMessenger.Default.Register<BroadcastEventMessage>(this,
            async (r, message) =>
            {
                // Event handler for broadcasting events to all clients in the room
                await _multiUserDrawingService.BroadcastEventAsync(message.RoomId, message.Event);
            });

        WeakReferenceMessenger.Default.Register<SendCanvasStateMessage>(this,
            async (r, message) =>
            {
                // Event handler for sending canvas state to clients that just joined the room
                await _multiUserDrawingService.SendCanvasStateToClientAsync(message.TargetId, message.Events);
            });
    }

    private void UpdateRoomClients(MultiUserDrawingClient client, List<MultiUserDrawingClient> clients)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Room == null || _multiUserDrawingService.ConnectionId == null) return;
            Room = new MultiUserDrawingRoom(Room.RoomId, _multiUserDrawingService.ConnectionId, Room.Me.Name)
            {
                Clients = clients
            };
        });
    }

    private async Task JoinRoomAsync(string roomId, string displayName)
    {
        try
        {
            await _multiUserDrawingService.StartAsync();
            if (_multiUserDrawingService.ConnectionId != null)
            {
                Room = new MultiUserDrawingRoom(roomId, _multiUserDrawingService.ConnectionId, displayName);
            }

            await _multiUserDrawingService.JoinRoomAsync(roomId, displayName);
        }
        catch (Exception)
        {
            await _multiUserDrawingService.StopAsync();
            Room = null;
        }
    }

    private async Task LeaveRoomAsync()
    {
        if (Room != null)
        {
            await _multiUserDrawingService.LeaveRoomAsync(Room.RoomId);
            Room = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleRoomConnection))]
    private async Task ToggleRoomConnectionAsync()
    {
        if (_multiUserDrawingService.ConnectionState == HubConnectionState.Disconnected)
        {
            var hasEvents = WeakReferenceMessenger.Default.Send<HasEventsRequestMessage>();

            if (hasEvents.Response)
            {
                var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                    "This might clear your current canvas. Are you sure you want to proceed?");
                if (!confirmed) return;
            }

            await JoinRoomAsync(RoomId, ClientDisplayName.Trim());
        }
        else
        {
            await LeaveRoomAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerateRoomId))]
    private void GenerateRoomId()
    {
        if (_multiUserDrawingService.ConnectionState == HubConnectionState.Disconnected)
        {
            RoomId = Guid.NewGuid().ToString("N");
        }
    }
}