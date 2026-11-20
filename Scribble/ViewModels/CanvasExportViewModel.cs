using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Controls;
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

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasOutputDimensions))]
    private string? _outputDimensions;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasExportError))]
    private string? _exportError;

    private const int PreviewMaxEdge = 1_024;

    private const int OutputMaxEdge = 16_384;

    // One RGBA surface at this limit uses 64 MiB before encoding.
    private const long OutputMaxPixels = 16_777_216;
    private const double Padding = 20;

    private readonly IFileService _fileService;
    private readonly ICanvasStateService _canvasStateService;

    private DocumentSnapshot? _snapshot;
    private bool _outputFits;

    // Show dimensions when the dialog contains exportable elements.
    public bool HasOutputDimensions => OutputDimensions is not null;

    // Show an inline error when rendering or validation fails.
    public bool HasExportError => ExportError is not null;

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
        _outputFits = false;
        OutputDimensions = null;
        ExportError = null;
        SetPreview(null);
    }

    // Limit the snapshot to elements that the export renderer can draw.
    private static bool IsExportable(CanvasElement element) => element is PaintableStroke or CanvasImage;

    // Fit the complete canvas in a small preview and validate the requested output separately.
    private void UpdateCanvasPreview()
    {
        if (_snapshot is not { Elements.Count: > 0 } snapshot) return;

        var bounds = Utilities.GetElementsBounds(snapshot.Elements);
        var (width, height) = GetPaddedSize(bounds);
        var outputWidth = Math.Ceiling(width * ImageScale);
        var outputHeight = Math.Ceiling(height * ImageScale);
        _outputFits = ValidateOutputSize(outputWidth, outputHeight) is not null;
        if (!IsPositiveSize(outputWidth, outputHeight))
        {
            OutputDimensions = null;
            ExportError = "The canvas has invalid export dimensions.";
            SetPreview(null);
            return;
        }

        OutputDimensions = $"{outputWidth:N0} × {outputHeight:N0} px";
        ExportError = _outputFits ? null : "Image is too large. Try a smaller selection or lower scale.";

        Bitmap? bitmap = null;
        try
        {
            var scale = Math.Min(ImageScale, PreviewMaxEdge / Math.Max(width, height));
            // Clamp after rounding to guard against floating-point error at the cap.
            var size = new SKSizeI(
                (int)Math.Clamp(Math.Ceiling(width * scale), 1, PreviewMaxEdge),
                (int)Math.Clamp(Math.Ceiling(height * scale), 1, PreviewMaxEdge));
            var encoded = RenderImage(snapshot.Elements, bounds, size, scale, IncludeBackground,
                snapshot.BackgroundColor, SKEncodedImageFormat.Png);
            if (encoded is not null) bitmap = DecodeBitmap(encoded);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Could not render the export preview. {0}", exception);
        }

        if (bitmap is null) ReportRenderError();
        SetPreview(bitmap);
    }

    // Replace the preview and release its previous native bitmap.
    private void SetPreview(Bitmap? bitmap)
    {
        var previous = PreviewImage;
        PreviewImage = bitmap;
        previous?.Dispose();
        ExportCanvasToPngCommand.NotifyCanExecuteChanged();
        ExportCanvasToJpegCommand.NotifyCanExecuteChanged();
        CopyCanvasToClipboardCommand.NotifyCanExecuteChanged();
    }

    // Enable output only when the requested dimensions fit and a preview is available.
    private bool CanExport() => _outputFits && PreviewImage is not null;

    // Decode the PNG for display or clipboard use.
    private static Bitmap DecodeBitmap(byte[] encoded)
    {
        using var stream = new MemoryStream(encoded, writable: false);
        return new Bitmap(stream);
    }

    // Use the same inline feedback for preview and output render failures.
    private void ReportRenderError()
    {
        ExportError ??= "Could not render the image. Try a smaller selection or lower scale.";
    }

    // Encode at the selected output scale, independently of the preview.
    private byte[]? RenderOutput(SKEncodedImageFormat format)
    {
        if (_snapshot is not { } snapshot || !CanExport()) return null;

        ExportError = null;
        try
        {
            var encoded = GetImageData(snapshot.Elements, IncludeBackground, snapshot.BackgroundColor, ImageScale,
                format);
            if (encoded is not null) return encoded;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Could not render the export image. {0}", exception);
        }

        ReportRenderError();
        return null;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportCanvasToPngAsync()
    {
        var encoded = RenderOutput(SKEncodedImageFormat.Png);
        return encoded is null ? Task.CompletedTask : SaveImageAsync(encoded, "PNG", ".png");
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportCanvasToJpegAsync()
    {
        var encoded = RenderOutput(SKEncodedImageFormat.Jpeg);
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
        if (RenderOutput(SKEncodedImageFormat.Png) is not { } encoded) return;

        ExportClipboardImage transfer;
        try
        {
            transfer = new ExportClipboardImage(DecodeBitmap(encoded));
        }
        catch (Exception exception)
        {
            Trace.TraceError("Could not decode the image for the clipboard. {0}", exception);
            ReportRenderError();
            return;
        }

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

    // Calculate padded dimensions without subtracting the float edges in single precision.
    private static (double Width, double Height) GetPaddedSize(SKRect bounds)
    {
        return ((double)bounds.Right - bounds.Left + 2 * Padding,
            (double)bounds.Bottom - bounds.Top + 2 * Padding);
    }

    // Reject empty or non-finite dimensions before any conversion to integers.
    private static bool IsPositiveSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0;

    // Round up and validate output dimensions before allocating a native surface.
    internal static SKSizeI? ValidateOutputSize(double width, double height)
    {
        width = Math.Ceiling(width);
        height = Math.Ceiling(height);
        if (!IsPositiveSize(width, height) || width > OutputMaxEdge || height > OutputMaxEdge)
            return null;

        var size = new SKSizeI((int)width, (int)height);
        return checked((long)size.Width * size.Height) <= OutputMaxPixels ? size : null;
    }

    // Render the requested elements at full resolution if they fit the output limits.
    internal static byte[]? GetImageData(
        IReadOnlyList<CanvasElement> elements,
        bool includeBackground,
        SKColor backgroundColor,
        int scale,
        SKEncodedImageFormat format)
    {
        if (elements.Count == 0) return null;

        var bounds = Utilities.GetElementsBounds(elements);
        var (width, height) = GetPaddedSize(bounds);
        var size = ValidateOutputSize(width * scale, height * scale);
        return size is { } outputSize
            ? RenderImage(elements, bounds, outputSize, scale, includeBackground, backgroundColor, format)
            : null;
    }

    // Draw either the bounded preview or a validated full-resolution image.
    private static byte[]? RenderImage(
        IReadOnlyList<CanvasElement> elements,
        SKRect bounds,
        SKSizeI size,
        double scale,
        bool includeBackground,
        SKColor backgroundColor,
        SKEncodedImageFormat format)
    {
        var imageInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
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

        canvas.Scale((float)scale);
        canvas.Translate((float)(Padding - bounds.Left), (float)(Padding - bounds.Top));

        // Draw in the layer order captured when the dialog opened.
        foreach (var element in elements)
        {
            CanvasElementRenderer.Draw(canvas, element);
        }

        using var image = surface.Snapshot();
        using var data = image?.Encode(format, 100);

        return data?.ToArray();
    }
}