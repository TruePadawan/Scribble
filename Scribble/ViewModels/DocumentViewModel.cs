using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Scribble.Lib.Dtos;
using Scribble.Services.CanvasState;
using Scribble.Services.DialogService;
using Scribble.Services.FileService;
using Scribble.Shared.Lib;
using SkiaSharp;

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

    public Func<Color>? GetBackgroundColor { get; set; }
    public event Action<string?>? CanvasFileLoaded;

    public DocumentViewModel(IFileService fileService, IDialogService dialogService,
        CanvasStateService canvasStateService)
    {
        _fileService = fileService;
        _dialogService = dialogService;
        _canvasStateService = canvasStateService;
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
        var canvasElements = _canvasStateService.CanvasElements;
        var backgroundColor = GetBackgroundColor!();
        await using var stream = await file.OpenWriteAsync();
        await using var streamWriter = new StreamWriter(stream);

        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var jsonCanvasStrokes = new JsonArray();
        var jsonCanvasImages = new JsonArray();
        // base64 encoded string -> file id
        Dictionary<string, Guid> imageBase64EncodedStrings = [];
        foreach (var element in canvasElements)
        {
            if (element is Stroke stroke)
            {
                jsonCanvasStrokes.Add(JsonSerializer.SerializeToNode(stroke, serializerOptions));
            }
            else if (element is CanvasImage canvasImage)
            {
                Guid fileId;
                if (imageBase64EncodedStrings.ContainsKey(canvasImage.ImageBase64String))
                {
                    fileId = imageBase64EncodedStrings[canvasImage.ImageBase64String];
                }
                else
                {
                    fileId = Guid.NewGuid();
                    imageBase64EncodedStrings[canvasImage.ImageBase64String] = fileId;
                }

                var canvasImageDto = new CanvasImageDto
                {
                    Id = canvasImage.Id,
                    Width = canvasImage.Bounds.Width,
                    Height = canvasImage.Bounds.Height,
                    X = canvasImage.Bounds.Left,
                    Y = canvasImage.Bounds.Top,
                    FileId = fileId
                };
                jsonCanvasImages.Add(JsonSerializer.SerializeToNode(canvasImageDto, serializerOptions));
            }
        }

        var files = new JsonObject();
        foreach (var (base64String, fileId) in imageBase64EncodedStrings)
        {
            files.Add(fileId.ToString(), base64String);
        }


        var canvasState = new JsonObject
        {
            ["elements"] = new JsonObject
            {
                ["strokes"] = jsonCanvasStrokes,
                ["images"] = jsonCanvasImages
            },
            ["files"] = files,
            ["backgroundColor"] = backgroundColor.ToString()
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
        var jsonCanvasElements = canvasState?["elements"]?.AsObject() ??
                                 throw new Exception("Invalid canvas file, cannot find elements");
        var jsonEncodedFiles = canvasState["files"]?.AsObject() ??
                               throw new Exception("Invalid canvas file, cannot find encoded files");
        var jsonCanvasStrokes = jsonCanvasElements["strokes"]?.AsArray() ??
                                throw new Exception("Invalid canvas file, cannot find strokes");
        var jsonCanvasImages = jsonCanvasElements["images"]?.AsArray() ??
                               throw new Exception("Invalid canvas file, cannot find images");

        var hasEvents = _canvasStateService.HasEvents;
        if (hasEvents)
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "This will clear your current canvas. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        var deserializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        List<CanvasElement> canvasElements = [];
        foreach (var jsonStroke in jsonCanvasStrokes)
        {
            if (jsonStroke is null) throw new Exception("Invalid canvas file");
            var deserializedStroke = JsonSerializer.Deserialize<Stroke>(jsonStroke.ToJsonString(), deserializerOptions);
            if (deserializedStroke is not null)
            {
                canvasElements.Add(deserializedStroke);
            }
        }

        foreach (var jsonImageDto in jsonCanvasImages)
        {
            if (jsonImageDto is null) throw new Exception("Invalid canvas file");
            var deserializedImageDto =
                JsonSerializer.Deserialize<CanvasImageDto>(jsonImageDto.ToJsonString(), deserializerOptions);
            if (deserializedImageDto is not null)
            {
                var fileId = deserializedImageDto.FileId.ToString();
                var base64String = jsonEncodedFiles[fileId]?.ToString() ??
                                   throw new Exception("Invalid canvas file, could not find encoded image");
                var canvasImage = new CanvasImage
                {
                    Id = deserializedImageDto.Id,
                    Bounds = SKRect.Create(
                        deserializedImageDto.X,
                        deserializedImageDto.Y,
                        deserializedImageDto.Width,
                        deserializedImageDto.Height
                    ),
                    ImageBase64String = base64String
                };
                canvasElements.Add(canvasImage);
            }
        }

        var bgColor = canvasState["backgroundColor"]?.ToString();
        _canvasStateService.ApplyEvent(new LoadCanvasEvent(Guid.NewGuid(), canvasElements));
        CanvasFileLoaded?.Invoke(bgColor);
    }
}