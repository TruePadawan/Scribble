using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Scribble.Utils;

namespace Scribble.Services.FileService;

public class AvaloniaFileService : IFileService
{
    public async Task<IStorageFile?> PickFileToOpenAsync(FilePickerOpenOptions options)
    {
        var topLevel = Utilities.GetTopLevel();
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        return files.Count == 1 ? files[0] : null;
    }

    public async Task<IStorageFile?> PickFileToSaveAsync(FilePickerSaveOptions options)
    {
        var topLevel = Utilities.GetTopLevel();
        if (topLevel == null) return null;

        return await topLevel.StorageProvider.SaveFilePickerAsync(options);
    }
}