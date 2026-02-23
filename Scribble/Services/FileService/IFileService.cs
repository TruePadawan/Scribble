using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Scribble.Services.FileService;

public interface IFileService
{
    Task<IStorageFile?> PickFileToOpenAsync();
    Task<IStorageFile?> PickFileToSaveAsync();
}