using System;
using System.IO;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Scribble.Services.DialogService;
using Scribble.Services.FileService;
using Scribble.Shared.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.ImageTool;

public class ImageTool : PointerTool
{
    private const float MaxImageDimension = 600f;
    private const float MaxFileSize = 1500000f;

    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;

    public ImageTool(string name, MainViewModel viewModel, IFileService fileService, IDialogService dialogService) :
        base(name, viewModel,
            LoadToolBitmap(typeof(ImageTool), "image.png"))
    {
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        _fileService = fileService;
        _dialogService = dialogService;
    }

    public override void HandlePointerClick(Point coord)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // Open the file picker, ask user to select an image
            var filePickerOptions = new FilePickerOpenOptions
            {
                Title = "Select an image",
                AllowMultiple = false,
                SuggestedFileType = FilePickerFileTypes.ImageAll
            };
            var imageFile = await _fileService.PickFileToOpenAsync(filePickerOptions);
            if (imageFile is null)
                return;

            var fileProperties = await imageFile.GetBasicPropertiesAsync();
            if (fileProperties.Size > MaxFileSize)
            {
                await _dialogService.ShowInfoAsync("File size exceeds the maximum allowed size",
                    "Please select an image less than or equal to 1.5 MB");
                return;
            }

            // Load the image from the file
            await using var stream = await imageFile.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);

            var imageBytes = memoryStream.ToArray();
            var base64String = Convert.ToBase64String(imageBytes);
            var bitmap = SKBitmap.Decode(imageBytes);

            var actionId = Guid.NewGuid();
            var imageId = Guid.NewGuid();
            var imageSize = ScaleToFit(bitmap.Width, bitmap.Height, MaxImageDimension);
            ViewModel.ApplyEvent(new AddImageEvent(actionId, imageId, base64String, imageSize,
                Utilities.ToSkPoint(coord)));
        });
    }

    private static SKSize ScaleToFit(float width, float height, float maxDimension)
    {
        if (width <= maxDimension && height <= maxDimension)
            return new SKSize(width, height);

        var scale = Math.Min(maxDimension / width, maxDimension / height);
        return new SKSize(width * scale, height * scale);
    }
}