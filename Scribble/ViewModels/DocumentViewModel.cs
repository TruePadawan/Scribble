using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services;
using Scribble.Services.DialogService;
using Scribble.Services.FileService;

namespace Scribble.ViewModels;

/// <summary>
/// View model for handling canvas-to-file and file-to-canvas operations
/// Handles saving and loading the canvas state to/from a file
/// </summary>
public partial class DocumentViewModel : ViewModelBase
{
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly CanvasStateService _canvasStateService;
    private readonly DocumentService _documentService;

    public DocumentViewModel(IFileService fileService, IDialogService dialogService,
        CanvasStateService canvasStateService, DocumentService documentService)
    {
        _fileService = fileService;
        _dialogService = dialogService;
        _canvasStateService = canvasStateService;
        _documentService = documentService;
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
        }
    }

    [RelayCommand]
    private async Task LoadCanvasFromFileAction()
    {
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