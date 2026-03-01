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

    public void UpdateCanvasPreview()
    {
        var canvasData = WeakReferenceMessenger.Default.Send<RequestCanvasDataMessage>().Response;
        List<DrawStroke> drawStrokes = [];
        foreach (var canvasStroke in canvasData.Strokes)
        {
            if (canvasStroke is DrawStroke drawStroke)
            {
                drawStrokes.Add(drawStroke);
            }
        }

        var pngData = GetPngData(drawStrokes,
            includeBackground: IncludeBackground,
            backgroundColor: Utilities.ToSkColor(canvasData.BackgroundColor),
            ImageScale);
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
    private async Task CopyCanvasToClipboardAsync()
    {
        var clipboard = Utilities.GetTopLevel()?.Clipboard;
        if (clipboard == null) return;
        await clipboard.SetBitmapAsync(PreviewImage);
    }

    private SKRect GetStrokesBounds(IEnumerable<DrawStroke> strokes)
    {
        SKRect totalBounds = SKRect.Empty;
        foreach (var stroke in strokes)
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

        return totalBounds;
    }

    private byte[]? GetPngData(
        List<DrawStroke> strokes,
        bool includeBackground,
        SKColor backgroundColor,
        int scale = 1, int padding = 20)
    {
        if (strokes.Count == 0)
        {
            return null;
        }

        // Calculate the bounding box of all strokes
        SKRect bounds = GetStrokesBounds(strokes);
        // Add padding
        bounds.Inflate(padding, padding);

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
        foreach (var drawStroke in strokes)
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

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }
}