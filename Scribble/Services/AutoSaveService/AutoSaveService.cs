using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DialogService;
using Scribble.Services.DocumentService;
using Scribble.Services.MultiUserDrawing;

namespace Scribble.Services.AutoSaveService;

public class AutoSaveService : IDisposable
{
    private readonly ICanvasStateService _canvasStateService;
    private readonly IDocumentService _documentService;
    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private readonly IDialogService? _dialogService;
    private readonly Dispatcher? _dispatcher;
    private readonly string _appDataPath;
    private readonly string _recoveryPath;
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _maxDelay;
    private readonly Lock _gate = new();
    private readonly Timer _timer;

    private long _editRevision;

    // Most recent revision covered by either kind of save.
    private long CoveredRevision => Math.Max(_manualRevision, _recoveryRevision);

    // The revision currently stored in the recovery file.
    private long _recoveryRevision;
    private long _manualRevision;
    private bool _paused;

    // A successful manual save covers only the revision captured for that write.
    public bool HasUnsavedChanges
    {
        get
        {
            lock (_gate) return _editRevision > _manualRevision;
        }
    }

    private long _session;
    private long? _firstPendingAt;
    private long _lastEditAt;
    private bool _writeInProgress;
    private bool _recovering;
    private bool _preserveRecoveryFile;
    private bool _saveErrorReported;
    private bool _disposed;

    public AutoSaveService(ICanvasStateService canvasStateService, IDocumentService documentService,
        IMultiUserDrawingService multiUserDrawingService, IDialogService dialogService)
        : this(canvasStateService, documentService, multiUserDrawingService,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            dialogService: dialogService, dispatcher: Dispatcher.UIThread)
    {
    }

    internal AutoSaveService(ICanvasStateService canvasStateService, IDocumentService documentService,
        IMultiUserDrawingService multiUserDrawingService, string customBaseDirectory,
        TimeSpan? debounceDelay = null, TimeSpan? maxDelay = null, IDialogService? dialogService = null,
        Dispatcher? dispatcher = null)
    {
        _canvasStateService = canvasStateService;
        _documentService = documentService;
        _multiUserDrawingService = multiUserDrawingService;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        _appDataPath = Path.Combine(customBaseDirectory, "Scribble");
        _recoveryPath = Path.Combine(_appDataPath, "autosave.scribble");
        _debounceDelay = debounceDelay ?? TimeSpan.FromSeconds(2);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(10);

        _paused = _multiUserDrawingService.IsJoining || _multiUserDrawingService.Room != null;
        _timer = new Timer(_ => _ = SavePendingAsync());
        _canvasStateService.ContentChanged += OnDocumentChanged;
        _documentService.DocumentOpened += OnDocumentOpened;
        _multiUserDrawingService.JoiningChanged += RefreshRoomPhase;
        _multiUserDrawingService.RoomChanged += OnRoomChanged;
    }

    // Load startup recovery while autosave is paused.
    public async Task RestoreAsync()
    {
        // Loading the file raises the same change events as editing the canvas.
        lock (_gate) _recovering = true;
        try
        {
            await using var stream = File.OpenRead(_recoveryPath);
            await _documentService.LoadAsync(stream);
            lock (_gate) _recoveryRevision = ++_editRevision;
        }
        catch (FileNotFoundException)
        {
            // No recovery file has been written yet.
        }
        catch (DirectoryNotFoundException)
        {
            // The first autosave will create the directory.
        }
        catch (Exception exception)
        {
            lock (_gate) _preserveRecoveryFile = true;
            ReportError("Could not recover the autosave", exception);
        }
        finally
        {
            lock (_gate) _recovering = false;
        }
    }

    // Track edits in every session and schedule recovery only during solo editing.
    private void OnDocumentChanged()
    {
        lock (_gate)
        {
            if (_disposed || _recovering) return;

            _editRevision++;
            if (_paused) return;
            _lastEditAt = Environment.TickCount64;
            // Keep the first edit's deadline even when later edits reset the quiet period.
            _firstPendingAt ??= _lastEditAt;
            ScheduleWrite();
        }
    }

    // Arm the timer when there is work and the writer is idle.
    private void ScheduleWrite()
    {
        if (_paused || _writeInProgress || _disposed || _firstPendingAt is null) return;

        _timer.Change(TimeSpan.FromMilliseconds(Math.Max(0, GetDelayMilliseconds())), Timeout.InfiniteTimeSpan);
    }

    // Use the earlier of the quiet period and the maximum wait.
    private long GetDelayMilliseconds()
    {
        var now = Environment.TickCount64;
        return Math.Min((long)_debounceDelay.TotalMilliseconds - (now - _lastEditAt),
            (long)_maxDelay.TotalMilliseconds - (now - _firstPendingAt!.Value));
    }

    // Write one snapshot and publish it if its revision is still needed.
    private async Task SavePendingAsync()
    {
        long session;
        lock (_gate)
        {
            if (_disposed || _writeInProgress || _firstPendingAt is null) return;

            // A callback queued before a new edit can run before the current deadline.
            if (GetDelayMilliseconds() > 0)
            {
                ScheduleWrite();
                return;
            }

            // Reserve the writer before releasing the lock for capture and disk I/O.
            session = _session;
            _writeInProgress = true;
            _firstPendingAt = null;
        }

        string? tempPath = null;
        try
        {
            var (snapshot, revision) = _dispatcher is null
                ? CapturePendingSnapshot(session)
                : await _dispatcher.InvokeAsync(() => CapturePendingSnapshot(session));

            using (snapshot)
            {
                if (snapshot is null) return;

                Directory.CreateDirectory(_appDataPath);
                tempPath = _recoveryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 bufferSize: 4096, useAsync: true))
                {
                    await _documentService.SaveAsync(stream, snapshot);
                }

                // Windows needs the temporary file closed before it can be renamed.
                lock (_gate)
                {
                    // A room change or a completed save can make this temporary file obsolete.
                    if (_disposed || session != _session || revision <= CoveredRevision) return;

                    if (_preserveRecoveryFile)
                    {
                        // Keep a copy of the failed recovery before replacing it.
                        File.Copy(_recoveryPath, _recoveryPath + "." + Guid.NewGuid().ToString("N") + ".bak");
                        _preserveRecoveryFile = false;
                    }

                    File.Move(tempPath, _recoveryPath, overwrite: true);
                    tempPath = null;
                    _recoveryRevision = revision;
                    _saveErrorReported = false;
                }
            }
        }
        catch (Exception exception)
        {
            bool report;
            lock (_gate)
            {
                // Show one error per run of failed writes while retries continue.
                report = !_disposed && session == _session && !_saveErrorReported;
                if (report) _saveErrorReported = true;
            }

            if (report) ReportError("Could not save the recovery file", exception);
        }
        finally
        {
            try
            {
                // Only unpublished writes still have a temporary path here.
                if (tempPath is not null) File.Delete(tempPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceError("Could not remove temporary autosave file. {0}", exception);
            }

            lock (_gate)
            {
                _writeInProgress = false;
                if (!_paused && _editRevision > CoveredRevision && _firstPendingAt is null)
                {
                    // Give a failed write the normal debounce interval before retrying.
                    _firstPendingAt = _lastEditAt = Environment.TickCount64;
                }

                ScheduleWrite();
            }
        }
    }

    // Capture pending edits only for the current autosave session.
    private (DocumentSnapshot? Snapshot, long Revision) CapturePendingSnapshot(long session)
    {
        lock (_gate)
        {
            if (_disposed || _paused || session != _session || _editRevision <= CoveredRevision) return default;
            _firstPendingAt = null;
            // Pair the snapshot with the revision it represents.
            return (_documentService.CaptureSnapshot(), _editRevision);
        }
    }

    // Save a manual snapshot and clear recovery data covered by that revision.
    public async Task SaveDocumentAsync(Func<Task<Stream>> openStream)
    {
        DocumentSnapshot snapshot;
        long revision;
        long session;
        bool wasSolo;
        lock (_gate)
        {
            snapshot = _documentService.CaptureSnapshot();
            revision = _editRevision;
            session = _session;
            wasSolo = _multiUserDrawingService.Room is null;
        }

        using (snapshot)
        {
            await using var stream = await openStream();
            await _documentService.SaveAsync(stream, snapshot);
        }

        lock (_gate)
        {
            // Manual saves may clear recovery only for the solo session they captured.
            if (_disposed || session != _session) return;
            _manualRevision = Math.Max(_manualRevision, revision);
            if (!wasSolo || _paused) return;

            // A newer autosave may have finished while the manual save was writing.
            if (_recoveryRevision <= revision && !_preserveRecoveryFile && File.Exists(_recoveryPath))
            {
                try
                {
                    File.Delete(_recoveryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    ReportError("Could not remove the previous autosave", exception);
                }
            }

            // Cancel the timer only when the manual save covers every edit.
            if (CoveredRevision == _editRevision)
            {
                _firstPendingAt = null;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    // Pause local recovery writes when entering a live room.
    private void OnRoomChanged(MultiUserDrawingRoom? room)
    {
        RefreshRoomPhase();
    }

    // Suspend publication during a join and resume uncovered edits on return to solo.
    private void RefreshRoomPhase()
    {
        lock (_gate)
        {
            var paused = _multiUserDrawingService.IsJoining || _multiUserDrawingService.Room != null;
            if (_disposed || paused == _paused) return;
            _paused = paused;
            _session++;
            _firstPendingAt = null;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            if (!paused && _editRevision > CoveredRevision)
            {
                _firstPendingAt = _lastEditAt = Environment.TickCount64;
                ScheduleWrite();
            }
        }
    }

    // Replace the manual baseline and invalidate pending writes from the old document.
    internal void OnDocumentOpened()
    {
        lock (_gate)
        {
            if (_recovering || _disposed) return;
            _session++;
            _manualRevision = _editRevision;
            _firstPendingAt = null;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            try
            {
                if (!_preserveRecoveryFile && File.Exists(_recoveryPath)) File.Delete(_recoveryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ReportError("Could not remove the previous autosave", exception);
            }
        }
    }

    // Log the failure and display it on the UI thread.
    private void ReportError(string title, Exception exception)
    {
        Trace.TraceError("{0}. {1}", title, exception);
        if (_dialogService is null) return;

        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                await _dialogService.ShowInfoAsync(title, exception.Message);
            }
            catch (Exception dialogException)
            {
                Trace.TraceError("Could not display autosave error. {0}", dialogException);
            }
        });
    }

    // Stop scheduling writes and unsubscribe from canvas and room events.
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _firstPendingAt = null;
            _timer.Dispose();
        }

        _canvasStateService.ContentChanged -= OnDocumentChanged;
        _documentService.DocumentOpened -= OnDocumentOpened;
        _multiUserDrawingService.JoiningChanged -= RefreshRoomPhase;
        _multiUserDrawingService.RoomChanged -= OnRoomChanged;
    }
}