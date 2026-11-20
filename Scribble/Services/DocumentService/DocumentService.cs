using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Threading;
using Scribble.Dtos;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Services.DocumentService;

public class DocumentService : IDocumentService
{
    private readonly ICanvasStateService _canvasStateService;

    public DocumentService(ICanvasStateService canvasStateService)
    {
        _canvasStateService = canvasStateService;
    }

    // Capture an independent copy of the document on the UI thread.
    public DocumentSnapshot CaptureSnapshot()
    {
        Dispatcher.UIThread.VerifyAccess();
        // Copy mutable elements before serializing on a background thread.
        var elements = _canvasStateService.CanvasElements.OfType<IClonable>()
            .Select(element => element.Clone(preserveId: true)).ToList();
        return new DocumentSnapshot(elements, _canvasStateService.BackgroundColor);
    }

    public async Task SaveAsync(Stream stream)
    {
        using var snapshot = await Dispatcher.UIThread.InvokeAsync(CaptureSnapshot);
        await SaveAsync(stream, snapshot);
    }

    // Write the captured document without reading live canvas state.
    public Task SaveAsync(Stream stream, DocumentSnapshot snapshot)
    {
        return Task.Run(async () =>
        {
            await using var streamWriter = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

            var serializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            var jsonCanvasStrokes = new JsonArray();
            var jsonCanvasImages = new JsonArray();
            Dictionary<string, Guid> imageBase64EncodedStrings = [];
            foreach (var element in snapshot.Elements)
            {
                if (element is PaintableStroke stroke)
                {
                    jsonCanvasStrokes.Add(JsonSerializer.SerializeToNode<Stroke>(stroke, serializerOptions));
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

                    // Each placement keeps its own transform even when images share a file ID.
                    var canvasImageDto = new CanvasImageDto
                    {
                        Id = canvasImage.Id,
                        Width = canvasImage.Bounds.Width,
                        Height = canvasImage.Bounds.Height,
                        X = canvasImage.Bounds.Left,
                        Y = canvasImage.Bounds.Top,
                        LayerIndex = canvasImage.LayerIndex,
                        Rotation = canvasImage.Rotation,
                        FlipX = canvasImage.FlipX,
                        FlipY = canvasImage.FlipY,
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
                ["backgroundColor"] = snapshot.BackgroundColor.ToString()
            };
            await streamWriter.WriteAsync(canvasState.ToJsonString(serializerOptions));
        });
    }

    public event Action? DocumentOpened;

    public async Task LoadAsync(Stream stream)
    {
        using var streamReader =
            new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
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
                    LayerIndex = deserializedImageDto.LayerIndex,
                    Rotation = deserializedImageDto.Rotation,
                    FlipX = deserializedImageDto.FlipX,
                    FlipY = deserializedImageDto.FlipY,
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

        // Apply the loaded elements and background together on the UI thread.
        var bgColorString = canvasState["backgroundColor"]?.ToString();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_canvasStateService.IsRoomActive) return;
            _canvasStateService.LoadCanvas(canvasElements);
            if (bgColorString != null && SKColor.TryParse(bgColorString, out var bgColor))
            {
                _canvasStateService.SetBackgroundColor(bgColor);
            }
            DocumentOpened?.Invoke();
        });
    }
}
