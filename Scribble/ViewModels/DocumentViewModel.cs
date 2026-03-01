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
    private async Task SaveToFileActionAsync()
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
        foreach (var stroke in canvasData.Strokes)
        {
            jsonCanvasStrokes.Add(JsonSerializer.SerializeToNode(stroke, serializerOptions));
        }

        var canvasState = new JsonObject
        {
            ["strokes"] = jsonCanvasStrokes,
            ["backgroundColor"] = canvasData.BackgroundColor.ToString()
        };
        await streamWriter.WriteAsync(canvasState.ToJsonString(serializerOptions));
    }

    [RelayCommand]
    private async Task OpenFileAction()
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

        List<Stroke> strokes = [];
        foreach (var stroke in rawStrokes)
        {
            if (stroke is null) throw new Exception("Invalid canvas file");
            var deserializedStroke = JsonSerializer.Deserialize<Stroke>(stroke.ToJsonString());
            if (deserializedStroke is null) throw new Exception("Invalid canvas file");
            strokes.Add(deserializedStroke);
        }

        var bgColor = canvasState["backgroundColor"]?.ToString();
        WeakReferenceMessenger.Default.Send(new LoadCanvasDataMessage(strokes, bgColor));
    }

    [RelayCommand]
    public async Task ResetCanvasAsync()
    {
        var hasEvents = WeakReferenceMessenger.Default.Send<HasEventsRequestMessage>().Response;
        if (hasEvents)
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "This will clear your current canvas. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        WeakReferenceMessenger.Default.Send(new ClearCanvasMessage());
    }
}