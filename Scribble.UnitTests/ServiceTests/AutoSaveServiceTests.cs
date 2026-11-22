using System.Text;
using FluentAssertions;
using NSubstitute;
using Scribble.Services.AutoSaveService;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DocumentService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using SkiaSharp;

namespace Scribble.UnitTests.ServiceTests;

public class AutoSaveServiceTests : IDisposable
{
    private readonly AutoSaveService _autoSaveService;
    private readonly ICanvasStateService _canvasStateService;
    private readonly IDocumentService _documentService;
    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private readonly string _tempDir;

    // Fast timers so tests complete in milliseconds rather than seconds
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(500);

    // Wait long enough for the debounce to settle, but not so long the test is slow
    private static readonly TimeSpan AfterDebounce = TimeSpan.FromMilliseconds(300);

    public AutoSaveServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        _canvasStateService = Substitute.For<ICanvasStateService>();
        _documentService = Substitute.For<IDocumentService>();
        _multiUserDrawingService = Substitute.For<IMultiUserDrawingService>();
        _documentService.CaptureSnapshot().Returns(_ => new DocumentSnapshot([], _canvasStateService.BackgroundColor));
        Directory.CreateDirectory(AppDataPath);

        _autoSaveService = new AutoSaveService(
            _canvasStateService, _documentService, _multiUserDrawingService,
            customBaseDirectory: _tempDir,
            debounceDelay: DebounceDelay,
            maxDelay: MaxDelay);
    }

    private string AppDataPath => Path.Combine(_tempDir, "Scribble");
    private string AutoSavedStateFilePath => Path.Combine(AppDataPath, "autosave.scribble");

    // A canceled join resumes the pending recovery without marking the document manually saved.
    [Fact]
    public async Task FailedJoin_ResumesPendingRecoveryUntilManualSave()
    {
        _canvasStateService.ContentChanged += Raise.Event<Action>();
        _multiUserDrawingService.IsJoining.Returns(true);
        _multiUserDrawingService.JoiningChanged += Raise.Event<Action>();
        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);
        File.Exists(AutoSavedStateFilePath).Should().BeFalse();
        _multiUserDrawingService.IsJoining.Returns(false);
        _multiUserDrawingService.JoiningChanged += Raise.Event<Action>();
        await WaitUntilAsync(() => File.Exists(AutoSavedStateFilePath));
        _autoSaveService.HasUnsavedChanges.Should().BeTrue();
        await _autoSaveService.SaveDocumentAsync(() => Task.FromResult<Stream>(new MemoryStream()));
        _autoSaveService.HasUnsavedChanges.Should().BeFalse();
    }

    // Repainting an overlay does not schedule recovery or dirty a clean file.
    [Fact]
    public async Task CanvasInvalidated_WithoutContentChanges_DoesNotScheduleRecovery()
    {
        _canvasStateService.CanvasInvalidated += Raise.Event<Action>();
        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);
        File.Exists(AutoSavedStateFilePath).Should().BeFalse();
        _autoSaveService.HasUnsavedChanges.Should().BeFalse();
    }

    // A room save covers its captured revision even when the membership list changes during I/O.
    [Fact]
    public async Task RoomRosterChangesDuringManualSave_PreservesSavedBaselineOnLeave()
    {
        var room = new MultiUserDrawingRoom("room", "connection", "Alice");
        _multiUserDrawingService.Room.Returns(room);
        _multiUserDrawingService.RoomChanged += Raise.Event<Action<MultiUserDrawingRoom?>>(room);
        _canvasStateService.ContentChanged += Raise.Event<Action>();
        var streamReady = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = _autoSaveService.SaveDocumentAsync(() => streamReady.Task);
        _multiUserDrawingService.RoomChanged += Raise.Event<Action<MultiUserDrawingRoom?>>(room);
        streamReady.SetResult(new MemoryStream());
        await save;
        _multiUserDrawingService.Room.Returns((MultiUserDrawingRoom?)null);
        _multiUserDrawingService.RoomChanged += Raise.Event<Action<MultiUserDrawingRoom?>>((object?)null!);
        _autoSaveService.HasUnsavedChanges.Should().BeFalse();
    }

    // A failed write preserves dirty state, while installing another file establishes a clean baseline.
    [Fact]
    public async Task ManualSaveFails_DocumentStaysDirtyUntilFileOpened()
    {
        _canvasStateService.ContentChanged += Raise.Event<Action>();
        await Assert.ThrowsAsync<IOException>(() =>
            _autoSaveService.SaveDocumentAsync(() =>
                Task.FromException<Stream>(new IOException("Simulated write failure"))));
        _autoSaveService.HasUnsavedChanges.Should().BeTrue();
        _documentService.DocumentOpened += Raise.Event<Action>();
        _autoSaveService.HasUnsavedChanges.Should().BeFalse();
        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);
        File.Exists(AutoSavedStateFilePath).Should().BeFalse();
    }

    // Verify a manual save clears the recovery file it covers.
    [Fact]
    public async Task SaveDocumentAsync_FileExists_FileIsDeleted()
    {
        File.Create(AutoSavedStateFilePath).Dispose();
        File.Exists(AutoSavedStateFilePath).Should().BeTrue();

        await _autoSaveService.SaveDocumentAsync(() => Task.FromResult<Stream>(new MemoryStream()));

        File.Exists(AutoSavedStateFilePath).Should().BeFalse();
    }

    // Verify a manual save also works before the first autosave.
    [Fact]
    public async Task SaveDocumentAsync_FileDoesNotExist_NoExceptionThrown()
    {
        await _autoSaveService.SaveDocumentAsync(() => Task.FromResult<Stream>(new MemoryStream()));
    }

    [Fact]
    public void Dispose_NoExceptionThrown()
    {
        var act = () => _autoSaveService.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledTwice_NoExceptionThrown()
    {
        _autoSaveService.Dispose();
        var act = () => _autoSaveService.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_WhileAutoSaveIsPending_CancelsAutoSave()
    {
        _canvasStateService.ContentChanged += Raise.Event<Action>();
        _autoSaveService.Dispose();

        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);

        await _documentService.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>());
    }

    [Fact]
    public async Task ContentChanged_TriggersAutoSave()
    {
        _canvasStateService.ContentChanged += Raise.Event<Action>();

        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);

        await _documentService.Received().SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>());
        File.Exists(AutoSavedStateFilePath).Should().BeTrue();
    }

    [Fact]
    public async Task ContentChangedWhileInRoom_AutoSaveIsPaused()
    {
        var room = new MultiUserDrawingRoom("room1", "conn-A", "Alice")
        {
            Clients = [new MultiUserDrawingClient("conn-A", "Alice")]
        };

        _canvasStateService.ContentChanged += Raise.Event<Action>();
        _multiUserDrawingService.Room.Returns(room);
        _multiUserDrawingService.RoomChanged += Raise.Event<Action<MultiUserDrawingRoom?>>(room);

        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);

        await _documentService.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>());
    }

    [Fact]
    public async Task RapidContentChangesWithinDebounceWindow_OnlyOneSaveIsTriggered()
    {
        // Fire three invalidations back-to-back, each one cancels the previous debounce timer
        _canvasStateService.ContentChanged += Raise.Event<Action>();
        _canvasStateService.ContentChanged += Raise.Event<Action>();
        _canvasStateService.ContentChanged += Raise.Event<Action>();

        // Wait for the debounce to settle after the final invalidation
        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);

        await _documentService.Received(1).SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>());
    }

    // Exercise recovery and replacement in the same app session.
    [Fact]
    public async Task RestoreThenSaveTwice_ClosesHandlesAndReplacesWithShorterFile()
    {
        File.WriteAllText(AutoSavedStateFilePath, "original recovery");
        await _autoSaveService.RestoreAsync();
        _autoSaveService.HasUnsavedChanges.Should().BeTrue();
        await _documentService.Received(1).LoadAsync(Arg.Any<Stream>());
        using (File.Open(AutoSavedStateFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // An exclusive open confirms that startup released the read handle.
        }

        var content = "replacement document";
        _documentService.SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>())
            .Returns(call => call.Arg<Stream>().WriteAsync(Encoding.UTF8.GetBytes(content)).AsTask());
        _canvasStateService.ContentChanged += Raise.Event<Action>();
        await WaitForRecoveryAsync(content);

        content = "short";
        _canvasStateService.ContentChanged += Raise.Event<Action>();
        await WaitForRecoveryAsync(content);
    }

    // Hold the first write open while changing the background.
    [Fact]
    public async Task EditDuringAutosave_PublishesFollowUpSnapshot()
    {
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationToken = TestContext.Current.CancellationToken;
        _canvasStateService.BackgroundColor.Returns(SKColors.Red);
        _documentService.SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>()).Returns(async call =>
        {
            var color = call.Arg<DocumentSnapshot>().BackgroundColor;
            if (color == SKColors.Red)
            {
                writing.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            else
            {
                secondWrite.TrySetResult();
                await releaseSecond.Task.WaitAsync(cancellationToken);
            }

            await call.Arg<Stream>().WriteAsync(Encoding.UTF8.GetBytes(color.ToString()), cancellationToken);
        });

        _canvasStateService.ContentChanged += Raise.Event<Action>();
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            _canvasStateService.BackgroundColor.Returns(SKColors.Blue);
            _canvasStateService.ContentChanged += Raise.Event<Action>();
            release.SetResult();
            // Inspect the first published revision before allowing the second write to finish.
            await secondWrite.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            await WaitForRecoveryAsync(SKColors.Red.ToString());
            releaseSecond.SetResult();
            await WaitForRecoveryAsync(SKColors.Blue.ToString());
        }
        finally
        {
            release.TrySetResult();
            releaseSecond.TrySetResult();
        }
    }

    // Fail a partial write and check recovery before allowing the retry.
    [Fact]
    public async Task WriteFailure_PreservesRecoveryAndRetries()
    {
        File.WriteAllText(AutoSavedStateFilePath, "previous revision");
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var cancellationToken = TestContext.Current.CancellationToken;
        _documentService.SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>()).Returns(async call =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await call.Arg<Stream>().WriteAsync("partial write"u8.ToArray(), cancellationToken);
                throw new IOException("Simulated write failure");
            }

            retryStarted.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            await call.Arg<Stream>().WriteAsync("new revision"u8.ToArray(), cancellationToken);
        });

        _canvasStateService.ContentChanged += Raise.Event<Action>();
        try
        {
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            File.ReadAllText(AutoSavedStateFilePath).Should().Be("previous revision");
            release.SetResult();
            await WaitForRecoveryAsync("new revision");
        }
        finally
        {
            release.TrySetResult();
        }
    }

    // Enter a live room before the pending local write can publish.
    [Fact]
    public async Task JoinRoomDuringWrite_DiscardsPendingPublication()
    {
        File.WriteAllText(AutoSavedStateFilePath, "previous revision");
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationToken = TestContext.Current.CancellationToken;
        _documentService.SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>()).Returns(async call =>
        {
            writing.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            await call.Arg<Stream>().WriteAsync("pending revision"u8.ToArray(), cancellationToken);
        });

        _canvasStateService.ContentChanged += Raise.Event<Action>();
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            var room = new MultiUserDrawingRoom("room1", "conn-A", "Alice");
            _multiUserDrawingService.Room.Returns(room);
            _multiUserDrawingService.RoomChanged += Raise.Event<Action<MultiUserDrawingRoom?>>(room);
            release.SetResult();
            await WaitUntilAsync(() => !Directory.EnumerateFiles(AppDataPath, "*.tmp").Any());
            File.ReadAllText(AutoSavedStateFilePath).Should().Be("previous revision");
        }
        finally
        {
            release.TrySetResult();
        }
    }

    // Finish an older manual save after a newer autosave has published.
    [Fact]
    public async Task ManualSaveFinishesAfterNewerAutosave_PreservesNewerRecovery()
    {
        var manualWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationToken = TestContext.Current.CancellationToken;
        _documentService.SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>()).Returns(async call =>
        {
            var stream = call.Arg<Stream>();
            if (stream is MemoryStream)
            {
                manualWrite.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            else
            {
                await stream.WriteAsync("newer revision"u8.ToArray(), cancellationToken);
            }
        });

        var save = _autoSaveService.SaveDocumentAsync(() => Task.FromResult<Stream>(new MemoryStream()));
        try
        {
            await manualWrite.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            _canvasStateService.ContentChanged += Raise.Event<Action>();
            await WaitForRecoveryAsync("newer revision");
            release.SetResult();
            await save;
            _autoSaveService.HasUnsavedChanges.Should().BeTrue();
            File.ReadAllText(AutoSavedStateFilePath).Should().Be("newer revision");
        }
        finally
        {
            release.TrySetResult();
        }
    }

    // Preserve the unreadable file when publishing a fresh recovery.
    [Fact]
    public async Task RecoveryCannotBeRead_NextAutosaveKeepsOriginalAsBackup()
    {
        File.WriteAllText(AutoSavedStateFilePath, "invalid document");
        _documentService.LoadAsync(Arg.Any<Stream>())
            .Returns(Task.FromException(new InvalidDataException("Simulated parse failure")));

        await _autoSaveService.RestoreAsync();
        File.ReadAllText(AutoSavedStateFilePath).Should().Be("invalid document");

        _documentService.SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>())
            .Returns(call => call.Arg<Stream>().WriteAsync("usable recovery"u8.ToArray()).AsTask());
        _canvasStateService.ContentChanged += Raise.Event<Action>();
        await WaitForRecoveryAsync("usable recovery");

        var backup = Directory.EnumerateFiles(AppDataPath, "*.bak").Should().ContainSingle().Which;
        File.ReadAllText(backup).Should().Be("invalid document");
    }

    /// <summary>
    /// Wait for the published recovery to contain the expected content.
    /// </summary>
    private Task WaitForRecoveryAsync(string content)
    {
        return WaitUntilAsync(() =>
        {
            if (!File.Exists(AutoSavedStateFilePath)) return false;
            // Let Windows replace the recovery file while the test reads it.
            using var stream = File.Open(AutoSavedStateFilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd() == content;
        });
    }

    /// <summary>
    /// Bound the wait for a background file operation.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    public void Dispose()
    {
        _autoSaveService.Dispose();
        if (Directory.Exists(AppDataPath))
        {
            Directory.Delete(AppDataPath, true);
        }
    }
}