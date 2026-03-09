using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Scribble.Messages;
using Scribble.Services.FileService;
using Scribble.Shared.Lib;
using Scribble.Utils;
using Avalonia.Input.Platform;
using SkiaSharp;

namespace Scribble.ViewModels;

/// <summary>
/// View model for handling exporting the canvas state into an image
/// </summary>
public partial class CanvasExportViewModel : ViewModelBase
{
    [ObservableProperty] private int _imageScale;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private bool _includeBackground;

    private readonly IFileService _fileService;

    partial void OnIncludeBackgroundChanged(bool value)
    {
        UpdateCanvasPreview();
    }

    public CanvasExportViewModel(IFileService fileService)
    {
        _imageScale = 1;
        _includeBackground = true;
        _fileService = fileService;
    }

    [RelayCommand]
    private void ChangeImageScale(string value)
    {
        if (!int.TryParse(value, out int parsedScale)) return;
        ImageScale = parsedScale;
        UpdateCanvasPreview();
    }

    /// <summary>
    /// Updates the preview image based on the current canvas state
    /// If there is an active selection, it will be used as the preview else the entire canvas will be used
    /// </summary>
    public void UpdateCanvasPreview()
    {
        List<CanvasElement> previewedElements = [];
        var elementsPayload = WeakReferenceMessenger.Default.Send<RequestSelectedElements>().Response;
        var canvasData = WeakReferenceMessenger.Default.Send<RequestCanvasDataMessage>().Response;

        if (elementsPayload.CanvasElements.Count > 0)
        {
            previewedElements = elementsPayload.CanvasElements;
        }
        else
        {
            foreach (var canvasElement in canvasData.CanvasElements)
            {
                if (canvasElement is DrawStroke drawStroke)
                {
                    previewedElements.Add(drawStroke);
                }
                else if (canvasElement is CanvasImage canvasImage)
                {
                    previewedElements.Add(canvasImage);
                }
            }
        }

        var pngData = GetImageData(previewedElements,
            includeBackground: IncludeBackground,
            backgroundColor: Utilities.ToSkColor(canvasData.BackgroundColor),
            ImageScale, SKEncodedImageFormat.Png);
        if (pngData == null || pngData.Length == 0)
        {
            // Reset the preview image
            PreviewImage = null;
        }
        else
        {
            using var stream = new MemoryStream(pngData);
            PreviewImage = new Bitmap(stream);
        }
    }

    [RelayCommand]
    private async Task ExportCanvasToPngAsync()
    {
        var filePickOptions = new FilePickerSaveOptions
        {
            SuggestedFileName = "canvas_png",
            Title = "Export canvas as PNG",
            DefaultExtension = ".png",
        };
        var file = await _fileService.PickFileToSaveAsync(filePickOptions);
        if (file != null && PreviewImage != null)
        {
            await using var stream = await file.OpenWriteAsync();
            PreviewImage.Save(stream);
        }
    }

    [RelayCommand]
    private async Task ExportCanvasToJpegAsync()
    {
        var canvasData = WeakReferenceMessenger.Default.Send<RequestCanvasDataMessage>().Response;
        List<CanvasElement> elements = [];
        foreach (var canvasStroke in canvasData.CanvasElements)
        {
            if (canvasStroke is DrawStroke drawStroke)
            {
                elements.Add(drawStroke);
            }
        }

        var jpegData = GetImageData(elements,
            includeBackground: IncludeBackground,
            backgroundColor: Utilities.ToSkColor(canvasData.BackgroundColor),
            ImageScale, SKEncodedImageFormat.Jpeg);
        if (jpegData != null && jpegData.Length > 0)
        {
            using var stream = new MemoryStream(jpegData);
            var jpegBitmap = new Bitmap(stream);

            var filePickOptions = new FilePickerSaveOptions
            {
                SuggestedFileName = "canvas_jpeg",
                Title = "Export canvas as JPEG",
                DefaultExtension = ".jpeg",
            };
            var file = await _fileService.PickFileToSaveAsync(filePickOptions);
            if (file != null)
            {
                await using var fileStream = await file.OpenWriteAsync();
                jpegBitmap.Save(fileStream);
            }
        }
    }

    [RelayCommand]
    private async Task CopyCanvasToClipboardAsync()
    {
        var clipboard = Utilities.GetTopLevel()?.Clipboard;
        if (clipboard == null) return;
        await clipboard.SetBitmapAsync(PreviewImage);
    }

    private SKRect GetElementsBounds(IEnumerable<CanvasElement> elements)
    {
        SKRect totalBounds = SKRect.Empty;
        foreach (var element in elements)
        {
            if (element is DrawStroke stroke)
            {
                SKRect pathBounds = stroke.Path.Bounds;
                float halfStrokeWidth = stroke.Paint.StrokeWidth / 2;
                pathBounds.Inflate(halfStrokeWidth, halfStrokeWidth);
                if (totalBounds.IsEmpty)
                {
                    totalBounds = pathBounds;
                }
                else
                {
                    totalBounds.Union(pathBounds);
                }
            }
            else if (element is CanvasImage canvasImage)
            {
                if (totalBounds.IsEmpty)
                {
                    totalBounds = canvasImage.Bounds;
                }
                else
                {
                    totalBounds.Union(canvasImage.Bounds);
                }
            }
        }

        return totalBounds;
    }

    private byte[]? GetImageData(
        List<CanvasElement> elements,
        bool includeBackground,
        SKColor backgroundColor,
        int scale,
        SKEncodedImageFormat format)
    {
        if (elements.Count == 0)
        {
            return null;
        }

        // Calculate the bounding box of all elements
        SKRect bounds = GetElementsBounds(elements);
        // Add padding
        bounds.Inflate(20, 20);

        // Determine the final image size based on scale
        var finalImgWidth = (int)(bounds.Width * scale);
        var finalImgHeight = (int)(bounds.Height * scale);

        // Create an off-screen surface
        var imageInfo = new SKImageInfo(finalImgWidth, finalImgHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        using var canvas = surface.Canvas;

        canvas.Clear(includeBackground ? backgroundColor : SKColors.Transparent);

        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);

        // Render the strokes
        foreach (var element in elements)
        {
            if (element is DrawStroke drawStroke)
            {
                using var paintToUse = drawStroke.Paint.ToSkPaint();
                if (drawStroke.Path.PointCount == 1)
                {
                    canvas.DrawPoint(drawStroke.Path.Points[0], paintToUse);
                }
                else
                {
                    if (drawStroke.Paint.FillColor.Alpha != 0)
                    {
                        var strokeColor = paintToUse.Color;
                        paintToUse.Style = SKPaintStyle.StrokeAndFill;
                        paintToUse.Color = drawStroke.Paint.FillColor;
                        canvas.DrawPath(drawStroke.Path, paintToUse);
                        paintToUse.Style = SKPaintStyle.Stroke;
                        paintToUse.Color = strokeColor;
                    }

                    canvas.DrawPath(drawStroke.Path, paintToUse);
                }
            }
            else if (element is CanvasImage canvasImage)
            {
                var bitmap = canvasImage.GetBitmap();
                // Pushes a snapshot of the current canvas state (transforms, clipping regions, etc.) onto an internal stack before rotating canvas
                // Needed for drawing rotated images
                canvas.Save();
                canvas.RotateRadians(canvasImage.Rotation, canvasImage.Bounds.MidX, canvasImage.Bounds.MidY);

                // Flip the canvas to apply image-flips
                if (canvasImage.FlipX)
                    canvas.Scale(-1, 1, canvasImage.Bounds.MidX, canvasImage.Bounds.MidY);
                if (canvasImage.FlipY)
                    canvas.Scale(1, -1, canvasImage.Bounds.MidX, canvasImage.Bounds.MidY);
                canvas.DrawBitmap(bitmap, canvasImage.Bounds);
                canvas.Restore();
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(format, 100);

        return data.ToArray();
    }
}