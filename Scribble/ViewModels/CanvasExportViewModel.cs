using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DocumentService;
using Scribble.Services.FileService;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Utils;
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
    private readonly ICanvasStateService _canvasStateService;

    private DocumentSnapshot? _snapshot;
    private byte[]? _previewPng;

    // Refresh the current snapshot when the background option changes.
    partial void OnIncludeBackgroundChanged(bool value) => UpdateCanvasPreview();

    // Refresh the current snapshot when the scale changes.
    partial void OnImageScaleChanged(int value) => UpdateCanvasPreview();

    public CanvasExportViewModel(IFileService fileService, ICanvasStateService canvasStateService)
    {
        _imageScale = 1;
        _includeBackground = true;
        _fileService = fileService;
        _canvasStateService = canvasStateService;
    }

    [RelayCommand]
    private void ChangeImageScale(string value)
    {
        if (int.TryParse(value, out var scale)) ImageScale = scale;
    }

    // Capture the elements shown in the export dialog and keep their visible layer order.
    public void OpenExport()
    {
        CloseExport();

        var selection = _canvasStateService.GetSelectedElements().Where(IsExportable).ToList();
        var elements = selection.Count > 0 ? selection : _canvasStateService.CanvasElements.Where(IsExportable);
        var copies = elements.OrderBy(element => element.LayerIndex).ThenBy(element => element.CreatedAt)
            .OfType<IClonable>().Select(element => element.Clone(preserveId: true)).ToList();
        _snapshot = new DocumentSnapshot(copies, _canvasStateService.BackgroundColor);
        UpdateCanvasPreview();
    }

    // Free the dialog's snapshot and preview when it closes.
    public void CloseExport()
    {
        _snapshot?.Dispose();
        _snapshot = null;
        SetPreview(null);
    }

    // Limit the snapshot to elements that the export renderer can draw.
    private static bool IsExportable(CanvasElement element) => element is PaintableStroke or CanvasImage;

    // Render the open snapshot using the current export options.
    private void UpdateCanvasPreview()
    {
        SetPreview(_snapshot is { } snapshot
            ? GetImageData(snapshot.Elements, IncludeBackground, snapshot.BackgroundColor, ImageScale,
                SKEncodedImageFormat.Png)
            : null);
    }

    // Keep the encoded PNG alongside its preview so saving does not re-encode it.
    private void SetPreview(byte[]? encoded)
    {
        var previous = PreviewImage;
        _previewPng = encoded;
        PreviewImage = encoded is null ? null : DecodeBitmap(encoded);
        previous?.Dispose();
        ExportCanvasToPngCommand.NotifyCanExecuteChanged();
        ExportCanvasToJpegCommand.NotifyCanExecuteChanged();
        CopyCanvasToClipboardCommand.NotifyCanExecuteChanged();
    }

    // Enable export commands once the snapshot has rendered successfully.
    private bool CanExport() => _previewPng is not null;

    // Decode the PNG for display or clipboard use.
    private static Bitmap DecodeBitmap(byte[] encoded)
    {
        using var stream = new MemoryStream(encoded, writable: false);
        return new Bitmap(stream);
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportCanvasToPngAsync()
    {
        return _previewPng is { } encoded ? SaveImageAsync(encoded, "PNG", ".png") : Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportCanvasToJpegAsync()
    {
        if (_snapshot is not { } snapshot || !CanExport()) return Task.CompletedTask;

        var encoded = GetImageData(snapshot.Elements, IncludeBackground, snapshot.BackgroundColor, ImageScale,
            SKEncodedImageFormat.Jpeg);
        return encoded is null ? Task.CompletedTask : SaveImageAsync(encoded, "JPEG", ".jpeg");
    }

    // Hold the encoded bytes across the file picker so closing the dialog is safe.
    private async Task SaveImageAsync(byte[] encoded, string format, string extension)
    {
        var file = await _fileService.PickFileToSaveAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"canvas_{format.ToLowerInvariant()}",
            Title = $"Export canvas as {format}",
            DefaultExtension = extension,
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(encoded);
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task CopyCanvasToClipboardAsync()
    {
        var clipboard = Utilities.GetTopLevel()?.Clipboard;
        return clipboard is null ? Task.CompletedTask : CopyToClipboardAsync(clipboard);
    }

    // Give the clipboard a bitmap it can keep after the export dialog closes.
    internal async Task CopyToClipboardAsync(IClipboard clipboard)
    {
        if (_previewPng is not { } encoded) return;

        var transfer = new ExportClipboardImage(DecodeBitmap(encoded));
        try
        {
            await clipboard.SetDataAsync(transfer);
        }
        catch
        {
            transfer.Dispose();
            throw;
        }
    }

    // Own the copied bitmap until the platform releases the clipboard data.
    private sealed class ExportClipboardImage(Bitmap bitmap) : IDataTransfer, IAsyncDataTransfer
    {
        private readonly DataTransferItem _item = DataTransferItem.Create(DataFormat.Bitmap, bitmap);
        public IReadOnlyList<DataFormat> Formats => _item.Formats;
        IReadOnlyList<IDataTransferItem> IDataTransfer.Items => [_item];
        IReadOnlyList<IAsyncDataTransferItem> IAsyncDataTransfer.Items => [_item];

        // Release the bitmap once the clipboard no longer needs it.
        public void Dispose() => bitmap.Dispose();
    }

    // Render the requested elements into an encoded image.
    internal static byte[]? GetImageData(
        IReadOnlyList<CanvasElement> elements,
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
        SKRect bounds = Utilities.GetElementsBounds(elements);
        // Add padding
        bounds.Inflate(20, 20);

        // Determine the final image size based on scale
        var finalImgWidth = (int)(bounds.Width * scale);
        var finalImgHeight = (int)(bounds.Height * scale);

        // Create an off-screen surface
        var imageInfo = new SKImageInfo(finalImgWidth, finalImgHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        if (surface is null) return null;
        var canvas = surface.Canvas;

        // Flatten transparency onto white for JPEG. PNG keeps the original alpha.
        if (format == SKEncodedImageFormat.Jpeg)
        {
            canvas.Clear(SKColors.White);
            if (includeBackground) canvas.DrawColor(backgroundColor, SKBlendMode.SrcOver);
        }
        else
        {
            canvas.Clear(includeBackground ? backgroundColor : SKColors.Transparent);
        }

        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);

        // Draw in the layer order captured when the dialog opened.
        foreach (var element in elements)
        {
            if (element is PaintableStroke paintableStroke)
            {
                using var paintToUse = paintableStroke.Paint.ToSkPaint();
                if (paintableStroke.Path.PointCount == 1)
                {
                    canvas.DrawPoint(paintableStroke.Path.Points[0], paintToUse);
                }
                else
                {
                    if (paintableStroke.Paint.FillColor.Alpha != 0)
                    {
                        var strokeColor = paintToUse.Color;
                        paintToUse.Style = SKPaintStyle.StrokeAndFill;
                        paintToUse.Color = paintableStroke.Paint.FillColor;
                        canvas.DrawPath(paintableStroke.Path, paintToUse);
                        paintToUse.Style = SKPaintStyle.Stroke;
                        paintToUse.Color = strokeColor;
                    }

                    canvas.DrawPath(paintableStroke.Path, paintToUse);
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
                canvas.DrawBitmap(bitmap, canvasImage.Bounds, new SKSamplingOptions(SKFilterMode.Linear));
                canvas.Restore();
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(format, 100);

        return data?.ToArray();
    }
}