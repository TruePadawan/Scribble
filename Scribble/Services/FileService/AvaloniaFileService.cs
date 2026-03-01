using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Scribble.Services.FileService;

public class AvaloniaFileService : IFileService
{
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

    public async Task<IStorageFile?> PickFileToOpenAsync(FilePickerOpenOptions options)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        return files.Count == 1 ? files[0] : null;
    }

    public async Task<IStorageFile?> PickFileToSaveAsync(FilePickerSaveOptions options)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        return await topLevel.StorageProvider.SaveFilePickerAsync(options);
    }
}