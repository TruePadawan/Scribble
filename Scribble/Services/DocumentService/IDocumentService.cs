using System.IO;
using System.Threading.Tasks;

namespace Scribble.Services.DocumentService;

public interface IDocumentService
{
    Task SaveAsync(Stream stream);
    Task LoadAsync(Stream stream);
}