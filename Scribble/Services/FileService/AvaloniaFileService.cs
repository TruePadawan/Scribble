using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Scribble.Utils;

namespace Scribble.Services.FileService;

public class AvaloniaFileService : IFileService
{
    /// <summary>
    /// Opens the operating system's file picker in the context of opening a file
    /// </summary>
    /// <param name="options">Determines how the file picker works</param>
    /// <returns>The selected <see cref="IStorageFile">file</see> or null if nothing is selected</returns>
    public async Task<IStorageFile?> PickFileToOpenAsync(FilePickerOpenOptions options)
    {
        var topLevel = Utilities.GetTopLevel();
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        return files.Count == 1 ? files[0] : null;
    }

    /// <summary>
    /// Opens the operating system's file picker in the context of saving a file
    /// </summary>
    /// <param name="options"></param>
    /// <returns>The selected <see cref="IStorageFile">file</see> or null if nothing is selected</returns>
    public async Task<IStorageFile?> PickFileToSaveAsync(FilePickerSaveOptions options)
    {
        var topLevel = Utilities.GetTopLevel();
        if (topLevel == null) return null;

        return await topLevel.StorageProvider.SaveFilePickerAsync(options);
    }
}