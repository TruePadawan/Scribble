using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Scribble.Dtos;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Services.DocumentService;

/// <summary>Reads and writes the portable JSON document format without UI or canvas services.</summary>
internal sealed class DocumentCodec
{
    /// <summary>Encodes a detached snapshot while leaving the caller's stream open.</summary>
    public static async Task WriteAsync(Stream stream, DocumentSnapshot snapshot)
    {
        await using var streamWriter = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        var serializerOptions = CreateSerializerOptions(writeIndented: true);
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
                if (!imageBase64EncodedStrings.TryGetValue(canvasImage.ImageBase64String, out var fileId))
                {
                    fileId = Guid.NewGuid();
                    imageBase64EncodedStrings[canvasImage.ImageBase64String] = fileId;
                }

                // Each placement keeps its transform even when image bytes share a file ID.
                var imageDto = new CanvasImageDto
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
                jsonCanvasImages.Add(JsonSerializer.SerializeToNode(imageDto, serializerOptions));
            }
        }

        var files = new JsonObject();
        foreach (var (base64String, fileId) in imageBase64EncodedStrings)
        {
            files.Add(fileId.ToString(), base64String);
        }

        // Keep the existing unversioned shape until a schema change requires a version.
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
    }

    /// <summary>Decodes current and legacy unversioned JSON into a detached document.</summary>
    public static async Task<LoadedDocument> ReadAsync(Stream stream)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
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

        var serializerOptions = CreateSerializerOptions(writeIndented: false);
        List<CanvasElement> elements = [];
        try
        {
            foreach (var jsonStroke in jsonCanvasStrokes)
            {
                if (jsonStroke is null) throw new Exception("Invalid canvas file");
                var stroke = JsonSerializer.Deserialize<Stroke>(jsonStroke.ToJsonString(), serializerOptions);
                if (stroke is not null) elements.Add(stroke);
            }

            foreach (var jsonImage in jsonCanvasImages)
            {
                if (jsonImage is null) throw new Exception("Invalid canvas file");
                var imageDto = JsonSerializer.Deserialize<CanvasImageDto>(jsonImage.ToJsonString(), serializerOptions);
                if (imageDto is null) continue;
                var fileId = imageDto.FileId.ToString();
                var base64String = jsonEncodedFiles[fileId]?.ToString() ??
                                   throw new Exception("Invalid canvas file, could not find encoded image");
                elements.Add(new CanvasImage
                {
                    Id = imageDto.Id,
                    LayerIndex = imageDto.LayerIndex,
                    Rotation = imageDto.Rotation,
                    FlipX = imageDto.FlipX,
                    FlipY = imageDto.FlipY,
                    Bounds = SKRect.Create(imageDto.X, imageDto.Y, imageDto.Width, imageDto.Height),
                    ImageBase64String = base64String
                });
            }
        }
        catch
        {
            using var failedDocument = new LoadedDocument(elements, null);
            throw;
        }

        SKColor? backgroundColor = SKColor.TryParse(canvasState["backgroundColor"]?.ToString(), out var color)
            ? color
            : null;
        return new LoadedDocument(elements, backgroundColor);
    }

    /// <summary>Creates serializer settings shared by the document writer and reader.</summary>
    private static JsonSerializerOptions CreateSerializerOptions(bool writeIndented) => new()
    {
        // Reject null required stroke data before handing the document to the canvas.
        RespectNullableAnnotations = true,
        WriteIndented = writeIndented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}