using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Scribble.Messages;
using Scribble.Services.DialogService;
using Scribble.Services.FileService;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels;

/// <summary>
/// View model for handling canvas-to-file and file-to-canvas operations
/// Handles saving and loading the canvas state to/from a file
/// </summary>
public partial class DocumentViewModel : ViewModelBase
{
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;

    public DocumentViewModel(IFileService fileService, IDialogService dialogService)
    {
        _fileService = fileService;
        _dialogService = dialogService;
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
            await SaveCanvasToFileAsync(file);
        }
    }

    private async Task SaveCanvasToFileAsync(IStorageFile file)
    {
        var canvasData = WeakReferenceMessenger.Default.Send<RequestCanvasDataMessage>().Response;
        await using var stream = await file.OpenWriteAsync();
        await using var streamWriter = new StreamWriter(stream);

        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        var jsonCanvasStrokes = new JsonArray();
        foreach (var element in canvasData.CanvasElements)
        {
            if (element is Stroke stroke)
            {
                jsonCanvasStrokes.Add(JsonSerializer.SerializeToNode(stroke, serializerOptions));
            }
            // TODO: Add canvas images
        }

        var canvasState = new JsonObject
        {
            ["strokes"] = jsonCanvasStrokes,
            ["backgroundColor"] = canvasData.BackgroundColor.ToString()
        };
        await streamWriter.WriteAsync(canvasState.ToJsonString(serializerOptions));
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
        if (file != null)
        {
            await LoadCanvasFromFileAsync(file);
        }
    }

    private async Task LoadCanvasFromFileAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var streamReader = new StreamReader(stream);
        var json = await streamReader.ReadToEndAsync();
        var canvasState = JsonNode.Parse(json);
        var rawStrokes = canvasState?["strokes"]?.AsArray();
        if (canvasState is null || rawStrokes is null) return;

        var hasEvents = WeakReferenceMessenger.Default.Send<HasEventsRequestMessage>().Response;
        if (hasEvents)
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "This will clear your current canvas. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        List<CanvasElement> elements = [];
        foreach (var stroke in rawStrokes)
        {
            if (stroke is null) throw new Exception("Invalid canvas file");
            var deserializedStroke = JsonSerializer.Deserialize<Stroke>(stroke.ToJsonString());
            if (deserializedStroke is null) throw new Exception("Invalid canvas file");
            elements.Add(deserializedStroke);
        }
        // TODO: Add canvas images

        var bgColor = canvasState["backgroundColor"]?.ToString();
        WeakReferenceMessenger.Default.Send(new LoadCanvasDataMessage(elements, bgColor));
    }
}