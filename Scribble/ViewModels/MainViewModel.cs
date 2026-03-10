using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services.CanvasState;
using Scribble.Services.DialogService;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public static int CanvasWidth => 10000;
    public static int CanvasHeight => 10000;

    public event Action? RequestInvalidateSelection;
    public Action? RequestInvalidateCanvas { get; set; }

    [ObservableProperty] private List<CanvasElement> _canvasElements = [];

    private bool CanUndo => _canvasStateService.CanUndo;
    private bool CanRedo => _canvasStateService.CanRedo;

    // Services
    private readonly IDialogService _dialogService;
    private readonly CanvasStateService _canvasStateService;

    public CanvasStateService CanvasStateService => _canvasStateService;

    public MultiUserDrawingViewModel MultiUserDrawingViewModel { get; }
    public DocumentViewModel DocumentViewModel { get; }
    public UiStateViewModel UiStateViewModel { get; }
    public CanvasExportViewModel CanvasExportViewModel { get; }

    public MainViewModel(MultiUserDrawingViewModel multiUserDrawingViewModel, DocumentViewModel documentViewModel,
        UiStateViewModel uiStateViewModel,
        IDialogService dialogService,
        CanvasExportViewModel canvasExportViewModel,
        CanvasStateService canvasStateService)
    {
        _dialogService = dialogService;
        _canvasStateService = canvasStateService;

        CanvasExportViewModel = canvasExportViewModel;
        MultiUserDrawingViewModel = multiUserDrawingViewModel;
        DocumentViewModel = documentViewModel;
        UiStateViewModel = uiStateViewModel;

        // Wire service events to UI invalidation
        _canvasStateService.CanvasInvalidated += () =>
        {
            CanvasElements = _canvasStateService.CanvasElements;
            RequestInvalidateCanvas?.Invoke();
        };

        _canvasStateService.SelectionInvalidated += () => { RequestInvalidateSelection?.Invoke(); };

        _canvasStateService.UndoRedoStateChanged += () =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };

        // Wire DocumentViewModel background color handling
        DocumentViewModel.GetBackgroundColor = () => UiStateViewModel.BackgroundColor;
        DocumentViewModel.CanvasFileLoaded += bgColorHex =>
        {
            if (bgColorHex != null)
            {
                UiStateViewModel.BackgroundColor = Color.Parse(bgColorHex);
            }
        };

        // Wire CanvasExportViewModel background color access
        CanvasExportViewModel.GetBackgroundColor = () => UiStateViewModel.BackgroundColor;
    }

    public Vector GetCanvasDimensions() => new(CanvasWidth, CanvasHeight);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _canvasStateService.Undo();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _canvasStateService.Redo();
    }

    [RelayCommand]
    private void OpenUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Process.Start("xdg-open", url);
                Console.WriteLine($"Could not open URL: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (_canvasStateService.HasEvents)
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "All unsaved work will be lost. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand]
    private async Task ResetCanvas()
    {
        if (_canvasStateService.HasEvents)
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "This will clear your current canvas. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        _canvasStateService.ApplyEvent(new LoadCanvasEvent(Guid.NewGuid(), []));
    }
}