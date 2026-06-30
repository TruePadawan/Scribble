using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services.AutoSaveService;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DialogService;
using Scribble.Services.DocumentService;
using Scribble.Services.FileService;
using Scribble.Services.MultiUserDrawing;

namespace Scribble.ViewModels;

/// <summary>
/// View model for handling canvas-to-file and file-to-canvas operations
/// Handles saving and loading the canvas state to/from a file
/// </summary>
public partial class DocumentViewModel : ViewModelBase
{
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly ICanvasStateService _canvasStateService;
    private readonly IDocumentService _documentService;
    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private readonly AutoSaveService _autoSaveService;

    public DocumentViewModel(IFileService fileService, IDialogService dialogService,
        ICanvasStateService canvasStateService, IDocumentService documentService,
        IMultiUserDrawingService multiUserDrawingService, AutoSaveService autoSaveService)
    {
        _fileService = fileService;
        _dialogService = dialogService;
        _canvasStateService = canvasStateService;
        _documentService = documentService;
        _multiUserDrawingService = multiUserDrawingService;
        _autoSaveService = autoSaveService;
    }

    [RelayCommand]
    private async Task SaveCanvasToFileAction()
    {
        var filePickOptions = new FilePickerSaveOptions
        {
            SuggestedFileName = "Scribble",
            Title = "Save canvas state to file",
            DefaultExtension = ".scribble",
        };
        var file = await _fileService.PickFileToSaveAsync(filePickOptions);
        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await _documentService.SaveAsync(stream);
            _autoSaveService.DeleteAutoSavedState();
        }
    }

    [RelayCommand]
    private async Task LoadCanvasFromFileAction()
    {
        if (_multiUserDrawingService.Room != null) return;

        var filePickerOptions = new FilePickerOpenOptions
        {
            SuggestedFileName = "Scribble",
            Title = "Restore canvas state from file",
            AllowMultiple = false,
        };
        var file = await _fileService.PickFileToOpenAsync(filePickerOptions);
        if (file == null) return;

        if (_canvasStateService.HasEvents)
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "This will clear your current canvas. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        await using var stream = await file.OpenReadAsync();
        await _documentService.LoadAsync(stream);
    }
}