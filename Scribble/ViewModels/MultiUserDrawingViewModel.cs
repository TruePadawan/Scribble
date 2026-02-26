using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.SignalR.Client;
using Scribble.Lib.CollaborativeDrawing;
using Scribble.Messages;
using Scribble.Services.DialogService;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels;

public partial class MultiUserDrawingViewModel : ViewModelBase
{
    // Services
    private readonly CollaborativeDrawingService _collaborativeDrawingService;
    private readonly IDialogService _dialogService;

    // Observable properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResetCanvas))]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    [NotifyPropertyChangedFor(nameof(RoomButtonText))]
    [NotifyPropertyChangedFor(nameof(LiveDrawingButtonBackground))]
    [NotifyCanExecuteChangedFor(nameof(GenerateRoomIdCommand))]
    private CollaborativeDrawingRoom? _room;

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


    public MultiUserDrawingViewModel(CollaborativeDrawingService drawingService, IDialogService dialogService)
    {
        _collaborativeDrawingService = drawingService;
        _dialogService = dialogService;

        _collaborativeDrawingService.EventReceived += @event =>
        {
            WeakReferenceMessenger.Default.Send(new NetworkEventReceivedMessage(@event));
        };
        _collaborativeDrawingService.CanvasStateRequested += targetId =>
        {
            WeakReferenceMessenger.Default.Send(new CanvasStateRequestedMessage(targetId));
        };

        _collaborativeDrawingService.CanvasStateReceived += events =>
        {
            WeakReferenceMessenger.Default.Send(new CanvasStateReceivedMessage(events));
        };

        _collaborativeDrawingService.ClientJoinedRoom += UpdateRoomClients;
        _collaborativeDrawingService.ClientLeftRoom += UpdateRoomClients;

        // Listen for requests from the Canvas to send data outward via SignalR
        WeakReferenceMessenger.Default.Register<BroadcastEventMessage>(this,
            async (r, message) =>
            {
                // Event handler for broadcasting events to all clients in the room
                await _collaborativeDrawingService.BroadcastEventAsync(message.RoomId, message.Event);
            });

        WeakReferenceMessenger.Default.Register<SendCanvasStateMessage>(this,
            async (r, message) =>
            {
                // Event handler for sending canvas state to clients that just joined the room
                await _collaborativeDrawingService.SendCanvasStateToClientAsync(message.TargetId, message.Events);
            });
    }

    private void UpdateRoomClients(CollaborativeDrawingUser client, List<CollaborativeDrawingUser> clients)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Room == null || _collaborativeDrawingService.ConnectionId == null) return;
            Room = new CollaborativeDrawingRoom(Room.RoomId, _collaborativeDrawingService.ConnectionId, Room.User.Name)
            {
                Clients = clients
            };
        });
    }

    private async Task JoinRoomAsync(string roomId, string displayName)
    {
        try
        {
            await _collaborativeDrawingService.StartAsync();
            if (_collaborativeDrawingService.ConnectionId != null)
            {
                Room = new CollaborativeDrawingRoom(roomId, _collaborativeDrawingService.ConnectionId, displayName);
            }

            await _collaborativeDrawingService.JoinRoomAsync(roomId, displayName);
        }
        catch (Exception)
        {
            await _collaborativeDrawingService.StopAsync();
            Room = null;
        }
    }

    private async Task LeaveRoomAsync()
    {
        if (Room != null)
        {
            await _collaborativeDrawingService.LeaveRoomAsync(Room.RoomId);
            Room = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleRoomConnection))]
    private async Task ToggleRoomConnectionAsync()
    {
        if (_collaborativeDrawingService.ConnectionState == HubConnectionState.Disconnected)
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
        if (_collaborativeDrawingService.ConnectionState == HubConnectionState.Disconnected)
        {
            RoomId = Guid.NewGuid().ToString("N");
        }
    }
}