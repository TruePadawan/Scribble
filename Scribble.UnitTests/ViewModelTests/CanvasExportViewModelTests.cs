using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FluentAssertions;
using NSubstitute;
using Scribble.Services.CanvasStateService;
using Scribble.Services.FileService;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.UnitTests.ViewModelTests;

// Exercise the same export commands bound to the desktop dialog.
public class CanvasExportViewModelTests
{
    private readonly ICanvasStateService _canvas = Substitute.For<ICanvasStateService>();
    private readonly IFileService _files = Substitute.For<IFileService>();
    private readonly CanvasExportViewModel _export;

    // Start with a transparent canvas and no selection.
    public CanvasExportViewModelTests()
    {
        _canvas.GetSelectedElements().Returns([]);
        _canvas.BackgroundColor.Returns(SKColors.Transparent);
        _export = new CanvasExportViewModel(_files, _canvas);
    }

    // Check that both file formats export an image-only board with the expected background.
    [AvaloniaFact]
    public async Task Export_ImageOnlyBoard_IncludesImageInBothFormats()
    {
        _canvas.CanvasElements.Returns([CreateImage(0, 0)]);
        _canvas.BackgroundColor.Returns(new SKColor(0, 0, 255, 128));
        _export.OpenExport();
        try
        {
            using var png = await SaveAndDecodeAsync(jpeg: false);
            using var jpeg = await SaveAndDecodeAsync(jpeg: true);
            png.GetPixel(35, 35).Should().Be(SKColors.Blue);
            AssertColorNear(jpeg.GetPixel(35, 35), SKColors.Blue);
            png.GetPixel(5, 5).Should().Be(new SKColor(0, 0, 255, 128));
            AssertColorNear(jpeg.GetPixel(5, 5), new SKColor(127, 127, 255));
        }
        finally
        {
            _export.CloseExport();
        }
    }

    // Supply mixed elements in reverse order and check the visible color at the overlap.
    [AvaloniaFact]
    public async Task Export_MixedBoard_RendersInCanvasLayerOrder()
    {
        var image = CreateImage(10, 10);
        image.LayerIndex = 1;
        var stroke = CreateStroke(0);
        using var path = stroke.Path;
        _canvas.CanvasElements.Returns([image, stroke]);
        _export.OpenExport();
        try
        {
            using var png = await SaveAndDecodeAsync(jpeg: false);
            using var jpeg = await SaveAndDecodeAsync(jpeg: true);
            png.GetPixel(25, 25).Should().Be(SKColors.Red);
            png.GetPixel(40, 40).Should().Be(SKColors.Blue);
            AssertColorNear(jpeg.GetPixel(25, 25), SKColors.Red);
            AssertColorNear(jpeg.GetPixel(40, 40), SKColors.Blue);
        }
        finally
        {
            _export.CloseExport();
        }
    }

    // Keep the original selection even when the board changes and the file picker delays saving.
    [AvaloniaFact]
    public async Task Export_SelectionChangesAfterOpen_SavesOnlyCapturedImage()
    {
        var image = CreateImage(0, 0);
        var unrelated = CreateStroke(300);
        using var path = unrelated.Path;
        _canvas.CanvasElements.Returns([image, unrelated]);
        _canvas.GetSelectedElements().Returns([image]);
        _export.OpenExport();

        try
        {
            image.Bounds = SKRect.Create(100, 100, 10, 10);
            _canvas.GetSelectedElements().Returns([]);
            _export.IncludeBackground = false;
            using var png = await SaveAndDecodeAsync(jpeg: false);

            var picker = new TaskCompletionSource<IStorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _files.PickFileToSaveAsync(Arg.Any<FilePickerSaveOptions>()).Returns(picker.Task);
            var saving = _export.ExportCanvasToJpegCommand.ExecuteAsync(null);
            _export.CloseExport();
            using var output = new MemoryStream();
            var file = Substitute.For<IStorageFile>();
            file.OpenWriteAsync().Returns(output);
            picker.SetResult(file);
            await saving;
            using var jpeg = SKBitmap.Decode(output.ToArray());

            png.Width.Should().Be(70);
            jpeg.Width.Should().Be(70);
            png.GetPixel(35, 35).Should().Be(SKColors.Blue);
            AssertColorNear(jpeg.GetPixel(35, 35), SKColors.Blue);
            png.GetPixel(5, 5).Alpha.Should().Be(0);
            AssertColorNear(jpeg.GetPixel(5, 5), SKColors.White);
        }
        finally
        {
            _export.CloseExport();
        }
    }

    // Read clipboard data after disposing the dialog preview.
    [AvaloniaFact]
    public async Task CopyToClipboard_DialogCloses_TransferRetainsBitmap()
    {
        _canvas.CanvasElements.Returns([CreateImage(0, 0)]);
        var clipboard = Substitute.For<IClipboard>();
        IAsyncDataTransfer? data = null;
        clipboard.SetDataAsync(Arg.Do<IAsyncDataTransfer>(transfer => data = transfer)).Returns(Task.CompletedTask);
        _export.OpenExport();

        var preview = _export.PreviewImage;
        await _export.CopyToClipboardAsync(clipboard);
        _export.CloseExport();

        data.Should().NotBeNull();
        using var transfer = data!;
        var copied = await transfer.TryGetBitmapAsync();
        copied.Should().NotBeNull().And.NotBeSameAs(preview);
        _export.PreviewImage.Should().BeNull();
    }

    // Disable all output commands whenever there are no exportable elements.
    [AvaloniaFact]
    public void OpenExport_EmptyCanvas_DisablesExportAndCopy()
    {
        _canvas.CanvasElements.Returns([]);
        _export.OpenExport();
        try
        {
            _export.ExportCanvasToPngCommand.CanExecute(null).Should().BeFalse();
            _export.ExportCanvasToJpegCommand.CanExecute(null).Should().BeFalse();
            _export.CopyCanvasToClipboardCommand.CanExecute(null).Should().BeFalse();
        }
        finally
        {
            _export.CloseExport();
        }
    }

    // Save through the regular command and decode the bytes written to the file.
    private async Task<SKBitmap> SaveAndDecodeAsync(bool jpeg)
    {
        using var output = new MemoryStream();
        var file = Substitute.For<IStorageFile>();
        file.OpenWriteAsync().Returns(output);
        _files.PickFileToSaveAsync(Arg.Any<FilePickerSaveOptions>()).Returns(file);
        var command = jpeg ? _export.ExportCanvasToJpegCommand : _export.ExportCanvasToPngCommand;
        command.CanExecute(null).Should().BeTrue();
        await command.ExecuteAsync(null);
        return SKBitmap.Decode(output.ToArray());
    }

    // Create a solid image with predictable pixels for export assertions.
    private static CanvasImage CreateImage(float x, float y)
    {
        using var bitmap = new SKBitmap(30, 30);
        bitmap.Erase(SKColors.Blue);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new CanvasImage
        {
            Bounds = SKRect.Create(x, y, 30, 30),
            ImageBase64String = Convert.ToBase64String(png.ToArray()),
        };
    }

    // Create a filled stroke that overlaps the test image.
    private static DrawStroke CreateStroke(float x)
    {
        using var path = new SKPathBuilder();
        path.AddRect(SKRect.Create(x, 0, 45, 45));
        return new DrawStroke
        {
            Path = path.Snapshot(),
            Paint = new StrokePaint { Color = SKColors.Red, FillColor = SKColors.Red },
            ToolOptions = [],
            ToolType = ToolType.Rectangle,
        };
    }

    // Allow for channel rounding during JPEG compression.
    private static void AssertColorNear(SKColor actual, SKColor expected)
    {
        ((int)actual.Red).Should().BeCloseTo(expected.Red, 5);
        ((int)actual.Green).Should().BeCloseTo(expected.Green, 5);
        ((int)actual.Blue).Should().BeCloseTo(expected.Blue, 5);
    }
}
