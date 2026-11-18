using System.IO;
using System.Threading.Tasks;

namespace Scribble.Services.DocumentService;

public interface IDocumentService
{
    // Capture a snapshot on the UI thread for the caller to dispose.
    DocumentSnapshot CaptureSnapshot();
    Task SaveAsync(Stream stream);
    // Write an existing snapshot and leave the stream open.
    Task SaveAsync(Stream stream, DocumentSnapshot snapshot);
    Task LoadAsync(Stream stream);
}
