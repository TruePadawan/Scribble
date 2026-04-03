using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Scribble.Dtos;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Services;

public class DocumentService
{
    private readonly CanvasStateService _canvasStateService;

    public DocumentService(CanvasStateService canvasStateService)
    {
        _canvasStateService = canvasStateService;
    }

    public async Task SaveAsync(Stream stream)
    {
        var canvasElements = _canvasStateService.CanvasElements;
        var backgroundColor = _canvasStateService.BackgroundColor;

        await using var streamWriter = new StreamWriter(stream);

        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var jsonCanvasStrokes = new JsonArray();
        var jsonCanvasImages = new JsonArray();
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
                    LayerIndex = canvasImage.LayerIndex,
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

    public async Task LoadAsync(Stream stream)
    {
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

        _canvasStateService.LoadCanvas(canvasElements);

        var bgColorString = canvasState["backgroundColor"]?.ToString();
        if (bgColorString != null && SKColor.TryParse(bgColorString, out var bgColor))
        {
            _canvasStateService.SetBackgroundColor(bgColor);
        }
    }
}