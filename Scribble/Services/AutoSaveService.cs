using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Scribble.Services.MultiUserDrawing;

namespace Scribble.Services;

/// <summary>
/// Handles autosaving the canvas state and loading it on app startup
/// </summary>
public class AutoSaveService : IDisposable
{
    private readonly CanvasStateService _canvasStateService;
    private readonly DocumentService _documentService;
    private readonly MultiUserDrawingService _multiUserDrawingService;

    private readonly string _appDataPath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _maxDelayCts;
    private bool _hasUnsavedChanges;

    public AutoSaveService(CanvasStateService canvasStateService, DocumentService documentService,
        MultiUserDrawingService multiUserDrawingService)
    {
        _canvasStateService = canvasStateService;
        _documentService = documentService;
        _multiUserDrawingService = multiUserDrawingService;

        _canvasStateService.CanvasInvalidated += OnCanvasInvalidated;
        _multiUserDrawingService.RoomChanged += room =>
        {
            if (room != null)
            {
                PauseAutoSave();
            }
        };

        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _appDataPath = Path.Combine(baseDirectory, "Scribble");
        // Create the app data folder if it doesn't exist
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
            await Task.Delay(TimeSpan.FromSeconds(10), token);
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
            await Task.Delay(TimeSpan.FromSeconds(2), token);

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