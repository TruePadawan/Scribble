using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DocumentService;
using Scribble.Services.MultiUserDrawing;

namespace Scribble.Services.AutoSaveService;

/// <summary>
/// Handles autosaving the canvas state and loading it on app startup
/// </summary>
public class AutoSaveService : IDisposable
{
    private readonly ICanvasStateService _canvasStateService;
    private readonly IDocumentService _documentService;
    private readonly IMultiUserDrawingService _multiUserDrawingService;

    private readonly string _appDataPath;
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _maxDelay;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _maxDelayCts;
    private bool _hasUnsavedChanges;

    public AutoSaveService(ICanvasStateService canvasStateService, IDocumentService documentService,
        IMultiUserDrawingService multiUserDrawingService)
        : this(canvasStateService, documentService, multiUserDrawingService,
            customBaseDirectory: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    /// <summary>
    /// Constructor for testing purposes
    /// </summary>
    internal AutoSaveService(ICanvasStateService canvasStateService, IDocumentService documentService,
        IMultiUserDrawingService multiUserDrawingService, string customBaseDirectory,
        TimeSpan? debounceDelay = null, TimeSpan? maxDelay = null)
    {
        _canvasStateService = canvasStateService;
        _documentService = documentService;
        _multiUserDrawingService = multiUserDrawingService;
        _debounceDelay = debounceDelay ?? TimeSpan.FromSeconds(2);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(10);

        _canvasStateService.CanvasInvalidated += OnCanvasInvalidated;
        _multiUserDrawingService.RoomChanged += room =>
        {
            if (room != null)
            {
                PauseAutoSave();
            }
        };

        _appDataPath = Path.Combine(customBaseDirectory, "Scribble");
        Directory.CreateDirectory(_appDataPath);

        LoadAutoSavedState();
    }

    private async void OnCanvasInvalidated()
    {
        try
        {
            if (_multiUserDrawingService.Room != null) return;
            if (!_hasUnsavedChanges)
            {
                _hasUnsavedChanges = true;
                _ = StartMaxDelayTimer();
            }

            await AutoSaveAsync();
        }
        catch (TaskCanceledException)
        {
            // Expected behaviour
        }
    }

    /// <summary>
    /// Starts a timer that will wait a max of 10 seconds before saving the canvas state
    /// </summary>
    private async Task StartMaxDelayTimer()
    {
        try
        {
            _maxDelayCts?.Cancel();
            _maxDelayCts?.Dispose();
            _maxDelayCts = new CancellationTokenSource();
            var token = _maxDelayCts.Token;
            await Task.Delay(_maxDelay, token);
            await WriteToDiskAsync();
        }
        catch (TaskCanceledException)
        {
            // Expected behaviour
        }
    }

    /// <summary>
    /// Loads the autosaved state from the autosaved state file
    /// </summary>
    private void LoadAutoSavedState()
    {
        var autosavedStateFilePath = Path.Combine(_appDataPath, "autosave.scribble");
        if (File.Exists(autosavedStateFilePath))
        {
            _ = _documentService.LoadAsync(File.OpenRead(autosavedStateFilePath));
        }
    }

    /// <summary>
    /// Saves the canvas state to the autosaved state file
    /// </summary>
    private async Task AutoSaveAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            // wait for the debounce period
            await Task.Delay(_debounceDelay, token);

            await WriteToDiskAsync();
            _maxDelayCts?.Cancel();
            _maxDelayCts?.Dispose();
            _maxDelayCts = null;
        }
        catch (TaskCanceledException)
        {
            // Ignore, this is expected behaviour
        }
    }

    /// <summary>
    /// Writes the canvas state to the autosaved state file
    /// </summary>
    private async Task WriteToDiskAsync()
    {
        // ensures only one write operation runs at a time
        await _saveLock.WaitAsync();
        try
        {
            // Write the data to a temporary file and move it to the autosaved state file
            if (_hasUnsavedChanges)
            {
                var autosavedStateFilePath = Path.Combine(_appDataPath, "autosave.scribble");
                var tempPath = Path.Combine(_appDataPath, "autosave.scribble.tmp");
                await using var fileStream = File.OpenWrite(tempPath);
                await _documentService.SaveAsync(fileStream);
                File.Move(tempPath, autosavedStateFilePath, overwrite: true);
                _hasUnsavedChanges = false;
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void PauseAutoSave()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _maxDelayCts?.Cancel();
        _maxDelayCts?.Dispose();
        _maxDelayCts = null;
        _hasUnsavedChanges = false;
    }

    /// <summary>
    /// Deletes the autosaved state file
    /// </summary>
    public void DeleteAutoSavedState()
    {
        var autosavedStateFilePath = Path.Combine(_appDataPath, "autosave.scribble");
        if (File.Exists(autosavedStateFilePath))
        {
            File.Delete(autosavedStateFilePath);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _maxDelayCts?.Cancel();
        _maxDelayCts?.Dispose();
        _maxDelayCts = null;
        _saveLock.Dispose();
    }
}