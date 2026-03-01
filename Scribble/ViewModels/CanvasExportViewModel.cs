using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Scribble.Lib;
using Scribble.Messages;
using Scribble.Services.FileService;
using Scribble.Shared.Lib;
using Scribble.Utils;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

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

        var pngData = CanvasExporter.GetPngData(drawStrokes,
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
        var clipboard = GetTopLevel()?.Clipboard;
        if (clipboard == null) return;
        await clipboard.SetBitmapAsync(PreviewImage);
    }

    private TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            return TopLevel.GetTopLevel(singleView.MainView);
        }

        return null;
    }
}