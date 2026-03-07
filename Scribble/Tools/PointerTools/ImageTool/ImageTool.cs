using System;
using System.IO;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Scribble.Services.FileService;
using Scribble.Shared.Lib;
using Scribble.Utils;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools.ImageTool;

public class ImageTool : PointerTool
{
    private readonly IFileService _fileService;

    public ImageTool(string name, MainViewModel viewModel, IFileService fileService) : base(name, viewModel,
        LoadToolBitmap(typeof(ImageTool), "image.png"))
    {
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        _fileService = fileService;
    }

    public override void HandlePointerClick(Point coord)
    {
        // Open the file picker, ask user to select an image
        var filePickerOptions = new FilePickerOpenOptions
        {
            Title = "Select an image",
            AllowMultiple = false,
            SuggestedFileType = FilePickerFileTypes.ImageAll
        };
        var fileTask = _fileService.PickFileToOpenAsync(filePickerOptions);
        fileTask.ContinueWith(async task =>
        {
            if (task.IsCompletedSuccessfully && task.Result is { } imageFile)
            {
                await using var stream = await imageFile.OpenReadAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();
                var base64String = Convert.ToBase64String(imageBytes);
                var actionId = Guid.NewGuid();
                var imageId = Guid.NewGuid();
                ViewModel.ApplyEvent(new AddImageEvent(actionId, imageId, base64String, Utilities.ToSkPoint(coord)));
            }
        });
    }
}