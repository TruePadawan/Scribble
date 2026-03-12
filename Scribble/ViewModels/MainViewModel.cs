using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services;
using Scribble.Services.DialogService;
using Scribble.Services.FileService;
using Scribble.Services.MultiUserDrawing;
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
    public readonly IDialogService DialogService;
    public readonly IFileService FileService;
    private readonly MultiUserDrawingService _multiUserDrawingService;
    public CanvasStateService CanvasStateService { get; }

    public MultiUserDrawingViewModel MultiUserDrawingViewModel { get; }
    public DocumentViewModel DocumentViewModel { get; }
    public UiStateViewModel UiStateViewModel { get; }
    public CanvasExportViewModel CanvasExportViewModel { get; }

    public MainViewModel(MultiUserDrawingViewModel multiUserDrawingViewModel, DocumentViewModel documentViewModel,
        UiStateViewModel uiStateViewModel,
        IDialogService dialogService,
        CanvasExportViewModel canvasExportViewModel,
        CanvasStateService canvasStateService,
        MultiUserDrawingService multiUserDrawingService, IFileService fileService)
    {
        DialogService = dialogService;
        _multiUserDrawingService = multiUserDrawingService;
        FileService = fileService;
        CanvasStateService = canvasStateService;

        CanvasExportViewModel = canvasExportViewModel;
        MultiUserDrawingViewModel = multiUserDrawingViewModel;
        DocumentViewModel = documentViewModel;
        UiStateViewModel = uiStateViewModel;

        // Wire service events to UI invalidation
        CanvasStateService.CanvasInvalidated += () =>
        {
            CanvasElements = CanvasStateService.CanvasElements.ToList();
            RequestInvalidateSkiaCanvas?.Invoke();
        };

        CanvasStateService.SelectionInvalidated += () => { RequestRefreshSelection?.Invoke(); };

        CanvasStateService.UndoRedoStateChanged += () =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };
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
            var confirmed = await DialogService.ShowWarningConfirmationAsync("Warning",
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
        if (_multiUserDrawingService.Room != null) return;

        if (CanvasStateService.HasEvents)
        {
            var confirmed = await DialogService.ShowWarningConfirmationAsync("Warning",
                "This will clear your current canvas. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        CanvasStateService.LoadCanvas([]);
    }
}