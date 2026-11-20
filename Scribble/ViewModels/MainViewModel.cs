using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DialogService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.Events;
using SkiaSharp;

namespace Scribble.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public event Action<IReadOnlyList<Guid>, bool>? RequestRefreshSelection;
    public event Action? RequestInvalidateSkiaCanvas;

    [ObservableProperty] private List<CanvasElement> _canvasElements = [];
    private List<CanvasElement> _copiedCanvasElements = [];

    private bool CanUndo => CanvasStateService.CanUndo;
    private bool CanRedo => CanvasStateService.CanRedo;

    private readonly IDialogService _dialogService;
    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private ICanvasStateService CanvasStateService { get; }

    public MultiUserDrawingViewModel MultiUserDrawingViewModel { get; }
    public DocumentViewModel DocumentViewModel { get; }
    public UiStateViewModel UiStateViewModel { get; }
    public CanvasExportViewModel CanvasExportViewModel { get; }

    public MainViewModel(MultiUserDrawingViewModel multiUserDrawingViewModel, DocumentViewModel documentViewModel,
        UiStateViewModel uiStateViewModel,
        IDialogService dialogService,
        CanvasExportViewModel canvasExportViewModel,
        ICanvasStateService canvasStateService,
        IMultiUserDrawingService multiUserDrawingService)
    {
        _dialogService = dialogService;
        _multiUserDrawingService = multiUserDrawingService;
        CanvasStateService = canvasStateService;

        CanvasExportViewModel = canvasExportViewModel;
        MultiUserDrawingViewModel = multiUserDrawingViewModel;
        DocumentViewModel = documentViewModel;
        UiStateViewModel = uiStateViewModel;
    }

    // Observe editor services while this model is presented by the view.
    internal void Activate()
    {
        CanvasStateService.CanvasInvalidated += RefreshCanvas;
        CanvasStateService.SelectionInvalidated += RefreshSelection;
        CanvasStateService.UndoRedoStateChanged += RefreshUndoRedo;
        UiStateViewModel.Activate();
        RefreshCanvas();
        RefreshSelection(CanvasStateService.SelectedElementIds, true);
        RefreshUndoRedo();
    }

    // Release singleton subscriptions when the editor is unbound.
    internal void Deactivate()
    {
        CanvasStateService.CanvasInvalidated -= RefreshCanvas;
        CanvasStateService.SelectionInvalidated -= RefreshSelection;
        CanvasStateService.UndoRedoStateChanged -= RefreshUndoRedo;
        UiStateViewModel.Deactivate();
    }

    // Publish the current drawing to the canvas control.
    private void RefreshCanvas()
    {
        CanvasElements = [.. CanvasStateService.CanvasElements];
        RequestInvalidateSkiaCanvas?.Invoke();
    }

    // Refresh the selection overlay after the canvas changes.
    private void RefreshSelection(IReadOnlyList<Guid> ids, bool refreshOptions) => RequestRefreshSelection?.Invoke(ids, refreshOptions);

    // Recheck commands after replay or attachment.
    private void RefreshUndoRedo()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

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
        if (DocumentViewModel.HasUnsavedChanges)
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
        if (_multiUserDrawingService.Room != null || _multiUserDrawingService.IsJoining) return;

        if (DocumentViewModel.HasUnsavedChanges)
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "This will clear your current canvas. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        DocumentViewModel.ResetDocument();
    }

    [RelayCommand]
    private void Copy()
    {
        _copiedCanvasElements = CanvasStateService.GetSelectedElements()
            .OfType<IClonable>()
            .Select(e => e.Clone(preserveId: false))
            .ToList();
    }

    [RelayCommand]
    private void Paste(SKPoint pointerPos)
    {
        CanvasStateService.ApplyEvent(new PasteCanvasElementsEvent(Guid.NewGuid(), pointerPos, _copiedCanvasElements,
            Guid.NewGuid()));

        // Recreate the copied elements
        _copiedCanvasElements = _copiedCanvasElements
            .OfType<IClonable>()
            .Select(e => e.Clone(preserveId: false))
            .ToList();
    }

    /// <summary>
    /// Deletes the selected elements
    /// </summary>
    [RelayCommand]
    private void Delete()
    {
        if (CanvasStateService.SelectedElementIds.Count == 0) return;
        CanvasStateService.ApplyEvent(new EraseByIdsEvent(Guid.NewGuid(), CanvasStateService.SelectedElementIds));
    }
}