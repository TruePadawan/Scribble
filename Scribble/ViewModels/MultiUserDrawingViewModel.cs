using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services;
using Scribble.Services.DialogService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Dtos;
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
    private readonly CanvasStateService _canvasStateService;

    // Observable properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResetCanvas))]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    [NotifyPropertyChangedFor(nameof(RoomButtonText))]
    [NotifyPropertyChangedFor(nameof(LiveDrawingButtonBackground))]
    [NotifyPropertyChangedFor(nameof(ClientCount))]
    [NotifyCanExecuteChangedFor(nameof(GenerateRoomIdCommand))]
    private MultiUserDrawingRoom? _room;

    // Tell the command to re-evaluate when the room id or client display name changes
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ToggleRoomConnectionCommand))]
    private string _roomId = string.Empty;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ToggleRoomConnectionCommand))]
    private string _clientDisplayName = "Archer";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private string _message = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ChatToggleText))]
    private bool _isChatVisible = true;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public string ChatToggleText => IsChatVisible ? "Collapse Chatbox" : "Expand Chatbox";
    public int ClientCount => Room?.Clients.Count ?? 0;
    public bool CanResetCanvas => Room == null;
    public bool IsLive => Room != null;
    private bool CanGenerateRoomId => Room == null;

    private bool CanToggleRoomConnection =>
        !string.IsNullOrWhiteSpace(RoomId) && !string.IsNullOrWhiteSpace(ClientDisplayName);

    private bool CanSendMessage => !string.IsNullOrWhiteSpace(Message);

    public string RoomButtonText => IsLive ? "Leave Room" : "Enter Room";

    public IBrush LiveDrawingButtonBackground =>
        IsLive ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Transparent);


    public MultiUserDrawingViewModel(MultiUserDrawingService drawingService, IDialogService dialogService,
        CanvasStateService canvasStateService)
    {
        _multiUserDrawingService = drawingService;
        _dialogService = dialogService;
        _canvasStateService = canvasStateService;

        _multiUserDrawingService.RoomChanged += room =>
        {
            Room = room;
            // Clear all messages after leaving a room
            if (room == null)
            {
                Dispatcher.UIThread.Post(() => Messages.Clear());
            }
        };
        _multiUserDrawingService.MessageReceived += message =>
        {
            Dispatcher.UIThread.Post(() => Messages.Add(new ChatMessage(message)));
        };
        _multiUserDrawingService.MessageSent += messageId =>
        {
            // Update the sent status of the chat message
            Dispatcher.UIThread.Post(() => { Messages.First(message => message.Id == messageId).IsSent = true; });
        };
    }

    [RelayCommand(CanExecute = nameof(CanToggleRoomConnection))]
    private async Task ToggleRoomConnectionAsync()
    {
        if (!_multiUserDrawingService.IsConnected)
        {
            if (_canvasStateService.HasEvents)
            {
                var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                    "This might clear your current canvas. Are you sure you want to proceed?");
                if (!confirmed) return;
            }

            await _multiUserDrawingService.JoinRoomAsync(RoomId, ClientDisplayName.Trim());
        }
        else
        {
            await _multiUserDrawingService.LeaveRoomAsync(ClientDisplayName.Trim());
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerateRoomId))]
    private void GenerateRoomId()
    {
        if (!_multiUserDrawingService.IsConnected)
        {
            RoomId = Guid.NewGuid().ToString("N");
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (Room == null) return;

        if (string.IsNullOrWhiteSpace(ClientDisplayName))
        {
            ClientDisplayName = "Archer";
        }

        var cleanedMessage = Message.Trim();
        if (string.IsNullOrWhiteSpace(cleanedMessage) || cleanedMessage.Length > 500) return;

        // Optimistic sending
        var messageId = Guid.NewGuid().ToString("N");
        var senderConnectionId = Room.Me.ConnectionId;
        var message = new Message(messageId, senderConnectionId, ClientDisplayName, cleanedMessage);
        var chatMessage = new ChatMessage(message, true)
        {
            IsSent = false
        };
        Dispatcher.UIThread.Post(() => Messages.Add(chatMessage));
        Message = string.Empty;

        await _multiUserDrawingService.BroadcastMessageAsync(new MessageDto(messageId, ClientDisplayName,
            cleanedMessage));
    }

    [RelayCommand]
    private void ToggleChatVisibility()
    {
        IsChatVisible = !IsChatVisible;
    }
}