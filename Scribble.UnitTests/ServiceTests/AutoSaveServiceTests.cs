using FluentAssertions;
using NSubstitute;
using Scribble.Services.AutoSaveService;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DocumentService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;

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

        _autoSaveService = new AutoSaveService(
            _canvasStateService, _documentService, _multiUserDrawingService,
            customBaseDirectory: _tempDir,
            debounceDelay: DebounceDelay,
            maxDelay: MaxDelay);
    }

    private string AppDataPath => Path.Combine(_tempDir, "Scribble");
    private string AutoSavedStateFilePath => Path.Combine(AppDataPath, "autosave.scribble");

    [Fact]
    public void DeleteAutoSavedState_FileExists_FileIsDeleted()
    {
        File.Create(AutoSavedStateFilePath).Dispose();
        File.Exists(AutoSavedStateFilePath).Should().BeTrue();

        _autoSaveService.DeleteAutoSavedState();

        File.Exists(AutoSavedStateFilePath).Should().BeFalse();
    }

    [Fact]
    public void DeleteAutoSavedState_FileDoesNotExist_NoExceptionThrown()
    {
        var act = () => _autoSaveService.DeleteAutoSavedState();

        act.Should().NotThrow();
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
        _canvasStateService.CanvasInvalidated += Raise.Event<Action>();
        _autoSaveService.Dispose();

        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);

        await _documentService.DidNotReceive().SaveAsync(Arg.Any<Stream>());
    }

    [Fact]
    public async Task CanvasIsInvalidated_TriggersAutoSave()
    {
        _canvasStateService.CanvasInvalidated += Raise.Event<Action>();

        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);

        await _documentService.Received().SaveAsync(Arg.Any<Stream>());
        File.Exists(AutoSavedStateFilePath).Should().BeTrue();
    }

    [Fact]
    public async Task CanvasIsInvalidatedWhileInRoom_AutoSaveIsPaused()
    {
        var room = new MultiUserDrawingRoom("room1", "conn-A", "Alice")
        {
            Clients = [new MultiUserDrawingClient("conn-A", "Alice")]
        };

        _canvasStateService.CanvasInvalidated += Raise.Event<Action>();
        _multiUserDrawingService.Room.Returns(room);
        _multiUserDrawingService.RoomChanged += Raise.Event<Action<MultiUserDrawingRoom?>>(room);

        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);

        await _documentService.DidNotReceive().SaveAsync(Arg.Any<Stream>());
    }

    [Fact]
    public async Task RapidCanvasInvalidationsWithinDebounceWindow_OnlyOneSaveIsTriggered()
    {
        // Fire three invalidations back-to-back, each one cancels the previous debounce timer
        _canvasStateService.CanvasInvalidated += Raise.Event<Action>();
        _canvasStateService.CanvasInvalidated += Raise.Event<Action>();
        _canvasStateService.CanvasInvalidated += Raise.Event<Action>();

        // Wait for the debounce to settle after the final invalidation
        await Task.Delay(AfterDebounce, TestContext.Current.CancellationToken);

        await _documentService.Received(1).SaveAsync(Arg.Any<Stream>());
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