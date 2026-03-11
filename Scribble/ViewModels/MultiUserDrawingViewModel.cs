using System;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services;
using Scribble.Services.DialogService;
using Scribble.Services.MultiUserDrawing;

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
    private string _clientDisplayName = "Bootlicker";

    public int ClientCount => Room?.Clients.Count ?? 0;
    public bool CanResetCanvas => Room == null || Room.IsHost;
    public bool IsLive => Room != null;
    private bool CanGenerateRoomId => Room == null;

    private bool CanToggleRoomConnection =>
        !string.IsNullOrWhiteSpace(RoomId) && !string.IsNullOrWhiteSpace(ClientDisplayName);

    public string RoomButtonText => IsLive ? "Leave Room" : "Enter Room";

    public IBrush LiveDrawingButtonBackground =>
        IsLive ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Transparent);


    public MultiUserDrawingViewModel(MultiUserDrawingService drawingService, IDialogService dialogService,
        CanvasStateService canvasStateService)
    {
        _multiUserDrawingService = drawingService;
        _dialogService = dialogService;
        _canvasStateService = canvasStateService;

        _multiUserDrawingService.RoomChanged += room => { Room = room; };
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
            await _multiUserDrawingService.LeaveRoomAsync();
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
}