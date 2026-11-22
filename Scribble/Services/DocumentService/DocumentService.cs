using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib.CanvasElements;

namespace Scribble.Services.DocumentService;

/// <summary>Captures editor state and installs documents around the UI-independent file codec.</summary>
public class DocumentService : IDocumentService
{
    private readonly ICanvasStateService _canvasStateService;

    /// <summary>Creates a document service for the active canvas.</summary>
    public DocumentService(ICanvasStateService canvasStateService)
    {
        _canvasStateService = canvasStateService;
    }

    /// <summary>
    /// Capture an independent copy of the document on the UI thread.
    /// </summary>
    public DocumentSnapshot CaptureSnapshot()
    {
        Dispatcher.UIThread.VerifyAccess();
        // Copy mutable elements before serializing on a background thread.
        var elements = _canvasStateService.CanvasElements.OfType<IClonable>()
            .Select(element => element.Clone(preserveId: true)).ToList();
        return new DocumentSnapshot(elements, _canvasStateService.BackgroundColor);
    }

    /// <summary>Captures the active canvas and writes the snapshot without closing the stream.</summary>
    public async Task SaveAsync(Stream stream)
    {
        using var snapshot = await Dispatcher.UIThread.InvokeAsync(CaptureSnapshot);
        await SaveAsync(stream, snapshot);
    }

    /// <summary>
    /// Encode a detached snapshot away from the UI thread.
    /// </summary>
    public Task SaveAsync(Stream stream, DocumentSnapshot snapshot) =>
        Task.Run(() => DocumentCodec.WriteAsync(stream, snapshot));

    public event Action? DocumentOpened;

    /// <summary>Parses a document off-thread, then installs it on the UI thread if allowed.</summary>
    public async Task LoadAsync(Stream stream)
    {
        using var document = await Task.Run(() => DocumentCodec.ReadAsync(stream));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_canvasStateService.IsRoomActive) return;
            _canvasStateService.LoadCanvas(document.TakeElements());
            if (document.BackgroundColor is { } backgroundColor)
            {
                _canvasStateService.SetBackgroundColor(backgroundColor);
            }

            DocumentOpened?.Invoke();
        });
    }
}