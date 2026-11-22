using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DialogService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels;

/// <summary>
/// View model for managing multi-user drawing
/// </summary>
public partial class MultiUserDrawingViewModel : ViewModelBase, IDisposable
{
    // Services
    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private readonly IDialogService _dialogService;
    private readonly ICanvasStateService _canvasStateService;

    // Keep the warning visible after a disconnect so the retained canvas can be saved.
    [ObservableProperty] public partial bool DrawingSyncFailed { get; set; }

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

    public bool CanResetCanvas => Room == null && CanEditCanvas;

    // Pause canvas input while the connection prepares the shared board.
    public bool CanEditCanvas => !_multiUserDrawingService.IsJoining;
    public bool IsLive => Room != null;
    private bool CanGenerateRoomId => Room == null && CanEditCanvas;

    private bool CanToggleRoomConnection =>
        !string.IsNullOrWhiteSpace(RoomId) && !string.IsNullOrWhiteSpace(ClientDisplayName);

    private bool CanSendMessage => !string.IsNullOrWhiteSpace(Message);

    public string RoomButtonText => !CanEditCanvas ? "Joining…" : IsLive ? "Leave Room" : "Enter Room";

    public IBrush LiveDrawingButtonBackground =>
        IsLive ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Transparent);


    public MultiUserDrawingViewModel(IMultiUserDrawingService drawingService, IDialogService dialogService,
        ICanvasStateService canvasStateService)
    {
        _multiUserDrawingService = drawingService;
        _dialogService = dialogService;
        _canvasStateService = canvasStateService;

        // Chat and delivery updates follow the room even while the editor view is detached.
        _multiUserDrawingService.JoiningChanged += RefreshJoining;
        _multiUserDrawingService.RoomChanged += RefreshRoom;
        _multiUserDrawingService.RosterChanged += RefreshRoom;
        _multiUserDrawingService.IdentityRenewed += ShowNewIdentity;
        _multiUserDrawingService.JoinFailed += ShowJoinFailure;
        _multiUserDrawingService.DrawingSyncFailed += MarkDrawingSyncFailed;
        _multiUserDrawingService.MessageReceived += ReceiveMessage;
        _multiUserDrawingService.MessageSent += ConfirmMessage;
        RefreshRoom(_multiUserDrawingService.Room);
        RefreshJoining();
    }

    // Release subscriptions when the application closes its room service owner.
    public void Dispose()
    {
        _multiUserDrawingService.JoiningChanged -= RefreshJoining;
        _multiUserDrawingService.RoomChanged -= RefreshRoom;
        _multiUserDrawingService.RosterChanged -= RefreshRoom;
        _multiUserDrawingService.IdentityRenewed -= ShowNewIdentity;
        _multiUserDrawingService.JoinFailed -= ShowJoinFailure;
        _multiUserDrawingService.DrawingSyncFailed -= MarkDrawingSyncFailed;
        _multiUserDrawingService.MessageReceived -= ReceiveMessage;
        _multiUserDrawingService.MessageSent -= ConfirmMessage;
    }

    // Refresh controls affected by the join gate.
    private void RefreshJoining()
    {
        OnPropertyChanged(nameof(CanEditCanvas));
        OnPropertyChanged(nameof(CanResetCanvas));
        OnPropertyChanged(nameof(RoomButtonText));
        GenerateRoomIdCommand.NotifyCanExecuteChanged();
    }

    // Keep chat history only for the same room session.
    private void RefreshRoom(MultiUserDrawingRoom? room)
    {
        if (room == null || Room?.Me.ConnectionId != room.Me.ConnectionId) Messages.Clear();
        if (room != null && Room?.Me.ConnectionId != room.Me.ConnectionId) DrawingSyncFailed = false;
        Room = room;
    }

    // Show the delivery warning while keeping all locally applied edits.
    // Explain why previously owned strokes may no longer be editable.
    private void ShowNewIdentity() => _ = _dialogService.ShowInfoAsync("Joined as a new participant",
        "The previous room or reconnect window ended, so you have a new drawing identity.");

    // Surface room admission failures while the solo board is still intact.
    private void ShowJoinFailure(string reason) => _ = _dialogService.ShowInfoAsync("Could not join room", reason);

    private void MarkDrawingSyncFailed() => DrawingSyncFailed = true;

    // Append a received chat message to the conversation.
    private void ReceiveMessage(Message message) => Messages.Add(new ChatMessage(message));

    // Mark the optimistic message as delivered.
    private void ConfirmMessage(string messageId)
    {
        var message = Messages.FirstOrDefault(item => item.Id == messageId);
        message?.IsSent = true;
    }

    [RelayCommand(CanExecute = nameof(CanToggleRoomConnection))]
    private async Task ToggleRoomConnectionAsync()
    {
        if (!_multiUserDrawingService.IsConnected)
        {
            if (_canvasStateService.CanvasElements.Count > 0)
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
        Messages.Add(chatMessage);
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