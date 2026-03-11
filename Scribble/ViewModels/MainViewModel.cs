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

    public event Action? RequestRefreshSelection;
    public Action? RequestInvalidateSkiaCanvas { get; set; }

    [ObservableProperty] private List<CanvasElement> _canvasElements = [];

    private bool CanUndo => CanvasStateService.CanUndo;
    private bool CanRedo => CanvasStateService.CanRedo;

    // Services
    private readonly IDialogService _dialogService;
    public CanvasStateService CanvasStateService { get; }

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
        CanvasStateService = canvasStateService;

        CanvasExportViewModel = canvasExportViewModel;
        MultiUserDrawingViewModel = multiUserDrawingViewModel;
        DocumentViewModel = documentViewModel;
        UiStateViewModel = uiStateViewModel;

        // Wire service events to UI invalidation
        CanvasStateService.CanvasInvalidated += () =>
        {
            CanvasElements = CanvasStateService.CanvasElements;
            RequestInvalidateSkiaCanvas?.Invoke();
        };

        CanvasStateService.SelectionInvalidated += () => { RequestRefreshSelection?.Invoke(); };

        CanvasStateService.UndoRedoStateChanged += () =>
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
        CanvasStateService.Undo();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        CanvasStateService.Redo();
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
        if (CanvasStateService.HasEvents)
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
        if (CanvasStateService.HasEvents)
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "This will clear your current canvas. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        CanvasStateService.ApplyEvent(new LoadCanvasEvent(Guid.NewGuid(), []));
    }
}