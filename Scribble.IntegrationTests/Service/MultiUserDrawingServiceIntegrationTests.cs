using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using Scribble.Services.AutoSaveService;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DialogService;
using Scribble.Services.DocumentService;
using Scribble.ViewModels;
using SkiaSharp;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using FluentAssertions;
using Scribble.IntegrationTests.Hub;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;

namespace Scribble.IntegrationTests.Service;

[Collection("HubTests")]
public class MultiUserDrawingServiceIntegrationTests(HubServerFixture fixture)
    : IClassFixture<HubServerFixture>, IAsyncLifetime
{
    private MultiUserDrawingService _aliceService = null!;
    private MultiUserDrawingService _bobService = null!;
    private Action<string> _answerAliceSnapshotRequests = null!;

    public ValueTask InitializeAsync()
    {
        // Leverage the internal constructor to bypass standard network pipeline
        _aliceService = new MultiUserDrawingService(fixture.CreateHubConnection);
        _bobService = new MultiUserDrawingService(fixture.CreateHubConnection);
        // Give ordinary join tests a host that responds with an empty canvas.
        _answerAliceSnapshotRequests = targetId => _ = _aliceService.SendCanvasStateToClientAsync(targetId, new());
        _aliceService.CanvasStateRequested += _answerAliceSnapshotRequests;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_aliceService.IsConnected) await _aliceService.LeaveRoomAsync("Alice");
        if (_bobService.IsConnected) await _bobService.LeaveRoomAsync("Bob");
    }

    // -- Connection lifecycle --

    [Fact]
    public async Task JoinRoomAsync_Success_ConnectionStartedEventFires()
    {
        var roomId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource();

        _aliceService.ConnectionStarted += () => tcs.TrySetResult();

        await _aliceService.JoinRoomAsync(roomId, "Alice");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        _aliceService.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task JoinRoomAsync_Success_RoomPropertyIsPopulated()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");

        _aliceService.Room.Should().NotBeNull();
        _aliceService.Room.RoomId.Should().Be(roomId);
        _aliceService.Room.Me.Name.Should().Be("Alice");
    }

    [Fact]
    public async Task JoinRoomAsync_Success_RoomChangedEventFiresWithRoom()
    {
        var roomId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<MultiUserDrawingRoom?>();

        _aliceService.RoomChanged += room =>
        {
            if (room != null && room.RoomId == roomId) tcs.TrySetResult(room);
        };

        await _aliceService.JoinRoomAsync(roomId, "Alice");

        var roomResult = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        roomResult.Should().NotBeNull();
        roomResult.RoomId.Should().Be(roomId);
    }

    [Fact]
    public async Task LeaveRoomAsync_AfterJoining_RoomChangedEventFiresWithNull()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");

        var tcs = new TaskCompletionSource<MultiUserDrawingRoom?>();
        _aliceService.RoomChanged += room =>
        {
            if (room == null) tcs.TrySetResult(room);
        };

        await _aliceService.LeaveRoomAsync("Alice");

        var roomResult = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        roomResult.Should().BeNull();
        _aliceService.IsConnected.Should().BeFalse();
        _aliceService.Room.Should().BeNull();
    }

    [Fact]
    public async Task JoinRoomAsync_InvalidServer_RoomRemainsNull()
    {
        var invalidService = new MultiUserDrawingService(() =>
            new HubConnectionBuilder().WithUrl("http://localhost:9999/badurl").Build());

        await invalidService.JoinRoomAsync("room", "Alice");

        invalidService.Room.Should().BeNull();
        invalidService.IsConnected.Should().BeFalse();
    }

    // Exercise the desktop notification path and real recovery file after a server-side abort.
    [AvaloniaFact]
    public async Task ServerDisconnect_PreservesCanvasAndResumesSoloAutosave()
    {
        var token = TestContext.Current.CancellationToken;
        var service = new MultiUserDrawingService(fixture.CreateHubConnection, Dispatcher.UIThread);
        var canvas = new CanvasStateService(service);
        using var viewModel = new MultiUserDrawingViewModel(service, Substitute.For<IDialogService>(), canvas);
        var directory = Directory.CreateTempSubdirectory("scribble-disconnect-");
        using var autosave = new AutoSaveService(canvas, new DocumentService(canvas), service,
            directory.FullName, TimeSpan.FromMilliseconds(20), dispatcher: Dispatcher.UIThread);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ConnectionStopped += () =>
        {
            Dispatcher.UIThread.VerifyAccess();
            stopped.TrySetResult();
        };

        try
        {
            await service.JoinRoomAsync(Guid.NewGuid().ToString(), "Alice");
            viewModel.IsLive.Should().BeTrue();
            var oldConnection = service.ConnectionId!;
            var stroke = Guid.NewGuid();
            canvas.ApplyEvent(new StartStrokeEvent(Guid.NewGuid(), stroke, new SKPoint(5, 5),
                new StrokePaint(), ToolType.Pencil, []));
            fixture.Faults.Abort(oldConnection);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), token);

            viewModel.IsLive.Should().BeFalse();
            service.Room.Should().BeNull();
            canvas.CanvasElements.Should().ContainSingle().Which.Id.Should().Be(stroke);
            canvas.SetBackgroundColor(SKColors.Blue);
            var file = Path.Combine(directory.FullName, "Scribble", "autosave.scribble");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (!File.Exists(file)) await Task.Delay(10, timeout.Token);
            using var recovery = JsonDocument.Parse(await File.ReadAllTextAsync(file, token));
            recovery.RootElement.GetProperty("backgroundColor").GetString().Should().Be(SKColors.Blue.ToString());
            recovery.RootElement.GetProperty("elements").GetProperty("strokes")[0].GetProperty("Id")
                .GetGuid().Should().Be(stroke);

            await service.JoinRoomAsync(Guid.NewGuid().ToString(), "Alice");
            service.ConnectionId.Should().NotBe(oldConnection);
            viewModel.IsLive.Should().BeTrue();
        }
        finally
        {
            await service.LeaveRoomAsync("Alice");
            autosave.Dispose();
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisconnectDuringJoin_ClearsPendingRoomAndAllowsRejoin(bool leaveExplicitly)
    {
        _aliceService.CanvasStateRequested -= _answerAliceSnapshotRequests;
        var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _aliceService.CanvasStateRequested += id => requested.TrySetResult(id);
        var roomId = Guid.NewGuid().ToString();
        var canvas = new CanvasStateService(_bobService);
        var stroke = AddLocalStroke(canvas);
        AddLocalStroke(canvas);
        canvas.Undo();
        await _aliceService.JoinRoomAsync(roomId, "Alice");
        var join = _bobService.JoinRoomAsync(roomId, "Bob");
        var token = TestContext.Current.CancellationToken;
        var id = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        if (leaveExplicitly)
            await _bobService.LeaveRoomAsync("Bob").WaitAsync(TimeSpan.FromSeconds(5), token);
        else
            fixture.Faults.Abort(id);
        await join.WaitAsync(TimeSpan.FromSeconds(5), token);
        _bobService.Room.Should().BeNull();
        _bobService.IsJoining.Should().BeFalse();
        canvas.CanvasElements.Should().ContainSingle().Which.Id.Should().Be(stroke);
        canvas.CanUndo.Should().BeTrue();
        canvas.CanRedo.Should().BeTrue();

        await _bobService.JoinRoomAsync(Guid.NewGuid().ToString(), "Bob");
        _bobService.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectDuringLeave_PublishesCleanupOnce()
    {
        var roomCleared = 0;
        var stopped = 0;
        await _aliceService.JoinRoomAsync(Guid.NewGuid().ToString(), "Alice");
        _aliceService.RoomChanged += room =>
        {
            if (room is null) Interlocked.Increment(ref roomCleared);
        };
        _aliceService.ConnectionStopped += () => Interlocked.Increment(ref stopped);
        fixture.Faults.AbortOnLeave(_aliceService.ConnectionId!);
        await _aliceService.LeaveRoomAsync("Alice");
        await _aliceService.JoinRoomAsync(Guid.NewGuid().ToString(), "Alice");

        roomCleared.Should().Be(1);
        stopped.Should().Be(1);
        _aliceService.Room.Should().NotBeNull();
    }

    // An interrupted send and its queued successor stay local after disconnect and rejoin.
    [Fact]
    public async Task DisconnectDuringSend_RetainsEditsAndRetiresPendingSends()
    {
        var canvas = new CanvasStateService(_aliceService);
        var room = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(room, "Alice");
        var connection = _aliceService.ConnectionId!;
        var held = fixture.Faults.HoldNextSend(connection);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _aliceService.DrawingSyncFailed += () => failed.TrySetResult();
        _aliceService.ConnectionStopped += () => stopped.TrySetResult();
        var action = Guid.NewGuid();
        var id = Guid.NewGuid();
        canvas.ApplyEvent(new StartStrokeEvent(action, id, SKPoint.Empty, new StrokePaint(), ToolType.Pencil, []));
        await held.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        canvas.ApplyEvent(new EndStrokeEvent(action));
        fixture.Faults.Abort(connection);
        await Task.WhenAll(failed.Task, stopped.Task)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        canvas.CanvasElements.Should().ContainSingle().Which.Id.Should().Be(id);
        await _aliceService.JoinRoomAsync(Guid.NewGuid().ToString(), "Alice");
        canvas.CanUndo.Should().BeTrue();
    }

    // A delayed callback from the old transport cannot mutate the newly joined board.
    [Fact]
    public async Task Rejoin_DelayedOldConnectionCallback_IsIgnored()
    {
        HubConnection? transport = null;
        var service = new MultiUserDrawingService(() => transport = fixture.CreateHubConnection());
        var canvas = new CanvasStateService(service);
        try
        {
            await service.JoinRoomAsync(Guid.NewGuid().ToString(), "Alice");
            var old = transport!;
            await service.LeaveRoomAsync("Alice");
            await service.JoinRoomAsync(Guid.NewGuid().ToString(), "Alice");
            service.IsConnected.Should().BeTrue();
            await service.ForSessionAsync(old, () => AddLocalStroke(canvas));
            canvas.HasEvents.Should().BeFalse();
        }
        finally
        {
            await service.LeaveRoomAsync("Alice");
        }
    }

    // A failed real join releases the pending solo revision for recovery.
    [AvaloniaFact]
    public async Task FailedJoin_ResumesRecoveryOfSoloBoard()
    {
        var directory = Directory.CreateTempSubdirectory("scribble-failed-join-");
        var token = TestContext.Current.CancellationToken;
        var service = new MultiUserDrawingService(fixture.CreateHubConnection, Dispatcher.UIThread);
        var canvas = new CanvasStateService(service);
        var room = Guid.NewGuid().ToString();
        _aliceService.CanvasStateRequested -= _answerAliceSnapshotRequests;
        var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _aliceService.CanvasStateRequested += id => requested.TrySetResult(id);
        await _aliceService.JoinRoomAsync(room, "Alice");
        try
        {
            using var autosave = new AutoSaveService(canvas, new DocumentService(canvas), service,
                directory.FullName, TimeSpan.FromMilliseconds(50), dispatcher: Dispatcher.UIThread);
            var stroke = AddLocalStroke(canvas);
            var join = service.JoinRoomAsync(room, "Bob");
            var id = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            fixture.Faults.Abort(id);
            await join.WaitAsync(TimeSpan.FromSeconds(5), token);
            var path = Path.Combine(directory.FullName, "Scribble", "autosave.scribble");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (!File.Exists(path)) await Task.Delay(10, timeout.Token);
            using var file = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
            file.RootElement.GetProperty("elements").GetProperty("strokes")[0].GetProperty("Id")
                .GetGuid().Should().Be(stroke);
            autosave.HasUnsavedChanges.Should().BeTrue();
        }
        finally
        {
            await service.LeaveRoomAsync("Bob");
            directory.Delete(true);
        }
    }

    // -- Event Relay --

    [Fact]
    public async Task BroadcastEventAsync_SecondClientInRoom_EventReceivedEventFires()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");
        await _bobService.JoinRoomAsync(roomId, "Bob");

        var bobEventTcs = new TaskCompletionSource<Event>();
        _bobService.EventReceived += evt => { bobEventTcs.TrySetResult(evt); };

        var endStrokeEvent = new EndStrokeEvent(Guid.NewGuid());
        await _aliceService.BroadcastEventAsync(endStrokeEvent);

        var receivedEvt =
            await bobEventTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        receivedEvt.Should().BeOfType<EndStrokeEvent>();
        receivedEvt.Id.Should().Be(endStrokeEvent.Id);
    }

    [Fact]
    public async Task BroadcastEventAsync_ToEmptyRoom_NoException()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");

        var act = async () => await _aliceService.BroadcastEventAsync(new EndStrokeEvent(Guid.NewGuid()));
        await act.Should().NotThrowAsync();
    }

    // -- Canvas State Handshake --

    [Fact]
    public async Task JoinRoomAsync_AsSecondClient_CanvasStateRequestedFiresOnHost()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");

        var aliceRequestTcs = new TaskCompletionSource<string>();
        _aliceService.CanvasStateRequested += targetClientConnectionId =>
        {
            aliceRequestTcs.TrySetResult(targetClientConnectionId);
        };

        await _bobService.JoinRoomAsync(roomId, "Bob");

        var requestClientConnectionId =
            await aliceRequestTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        requestClientConnectionId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SendCanvasStateToClientAsync_TargetClient_RoomCanvasReadyFires()
    {
        _aliceService.CanvasStateRequested -= _answerAliceSnapshotRequests;
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");

        var aliceRequestTcs = new TaskCompletionSource<string>();
        _aliceService.CanvasStateRequested += targetClientConnectionId =>
        {
            aliceRequestTcs.TrySetResult(targetClientConnectionId);
        };

        var bobStateTcs = new TaskCompletionSource<Queue<Event>?>();
        _bobService.RoomCanvasReady += (snapshot, _) => { bobStateTcs.TrySetResult(snapshot); };

        // Keep the join pending until the test supplies the host's snapshot.
        var join = _bobService.JoinRoomAsync(roomId, "Bob");
        var bobConnectionId =
            await aliceRequestTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var queue = new Queue<Event>();
        queue.Enqueue(new EndStrokeEvent(Guid.NewGuid()));

        await _aliceService.SendCanvasStateToClientAsync(bobConnectionId, queue);

        var receivedQueue =
            await bobStateTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        receivedQueue.Should().HaveCount(1);
        receivedQueue.Peek().Id.Should().Be(queue.Peek().Id);
        await join;
    }

    // Use a held snapshot to exercise the overlap between snapshot history and live edits.
    [Fact]
    public async Task JoinExistingRoom_MergesLiveStrokePointsAndClearsPreviousHistory()
    {
        _aliceService.CanvasStateRequested -= _answerAliceSnapshotRequests;
        var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _aliceService.CanvasStateRequested += id => requested.TrySetResult(id);
        var roomId = Guid.NewGuid().ToString();
        var canvas = new CanvasStateService(_bobService);
        AddLocalStroke(canvas);
        AddLocalStroke(canvas);
        canvas.Undo();
        canvas.CanUndo.Should().BeTrue();
        canvas.CanRedo.Should().BeTrue();
        var previousEvents = canvas.CanvasEvents.Select(evt => evt.Id).ToArray();

        await _aliceService.JoinRoomAsync(roomId, "Alice");
        var join = _bobService.JoinRoomAsync(roomId, "Bob");
        var token = TestContext.Current.CancellationToken;
        var target = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        _bobService.IsJoining.Should().BeTrue();
        canvas.CanUndo.Should().BeFalse();
        canvas.CanRedo.Should().BeFalse();
        canvas.Undo();
        canvas.Redo();
        AddLocalStroke(canvas);
        canvas.CanvasEvents.Select(evt => evt.Id).Should().Equal(previousEvents);

        var action = Guid.NewGuid();
        var stroke = Guid.NewGuid();
        var start = new StartStrokeEvent(action, stroke, new SKPoint(5, 5), new StrokePaint(), ToolType.Pencil, []);
        var overlap = new PencilStrokeLineToEvent(action, stroke, new SKPoint(15, 15));
        var later = new PencilStrokeLineToEvent(action, stroke, new SKPoint(25, 25));
        var end = new EndStrokeEvent(action);
        await _aliceService.BroadcastEventAsync(start);
        await _aliceService.BroadcastEventAsync(overlap);
        await _aliceService.BroadcastEventAsync(later);
        await _aliceService.BroadcastEventAsync(end);
        // The snapshot represents the host's canvas before the last point arrived.
        await _aliceService.SendCanvasStateToClientAsync(target, new Queue<Event>([start, overlap]));
        await join.WaitAsync(TimeSpan.FromSeconds(5), token);

        _bobService.IsJoining.Should().BeFalse();
        canvas.CanvasEvents.Select(evt => evt.Id).Should().Equal(start.Id, overlap.Id, later.Id, end.Id);
        var drawing = canvas.CanvasElements.Should().ContainSingle().Which
            .Should().BeOfType<DrawStroke>().Which;
        drawing.RawPoints.Select(point => point.Point).Should().Equal(start.StartPoint, overlap.Point, later.Point);
        canvas.CanUndo.Should().BeFalse();
        canvas.CanRedo.Should().BeFalse();

        // A copy that arrives after synchronization should also be ignored.
        var duplicateReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _bobService.EventReceived += evt =>
        {
            if (evt.Id == overlap.Id) duplicateReceived.TrySetResult();
        };
        await _aliceService.BroadcastEventAsync(overlap);
        await duplicateReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        canvas.CanvasEvents.Select(evt => evt.Id).Should().Equal(start.Id, overlap.Id, later.Id, end.Id);
    }

    // A room without a host starts from the first member's current canvas.
    [Fact]
    public async Task JoinEmptyRoom_KeepsSoloCanvasAndUndoHistory()
    {
        var canvas = new CanvasStateService(_aliceService);
        var stroke = AddLocalStroke(canvas);

        await _aliceService.JoinRoomAsync(Guid.NewGuid().ToString(), "Alice");

        _aliceService.Room.Should().NotBeNull();
        canvas.CanvasElements.Should().ContainSingle().Which.Id.Should().Be(stroke);
        canvas.CanUndo.Should().BeTrue();
        canvas.Undo();
        canvas.CanvasElements.Should().BeEmpty();
    }

    // A server-confirmed join with an unusable snapshot must preserve the solo board.
    [Fact]
    public async Task JoinExistingRoom_InvalidSnapshot_KeepsSoloCanvasAndUndoHistory()
    {
        var token = TestContext.Current.CancellationToken;
        var roomId = Guid.NewGuid().ToString();
        await using var host = fixture.CreateHubConnection();
        await host.StartAsync(token);
        await host.InvokeAsync("JoinRoom", roomId, "Host", token);
        var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var request = host.On<string>("RequestCanvasState", id => { requested.TrySetResult(id); });

        var canvas = new CanvasStateService(_bobService);
        var stroke = AddLocalStroke(canvas);
        var join = _bobService.JoinRoomAsync(roomId, "Bob");
        var target = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        await host.InvokeAsync("SendCanvasStateToClient", target, "{invalid json", token);
        await join.WaitAsync(TimeSpan.FromSeconds(5), token);

        _bobService.IsJoining.Should().BeFalse();
        _bobService.Room.Should().BeNull();
        canvas.CanvasElements.Should().ContainSingle().Which.Id.Should().Be(stroke);
        canvas.CanUndo.Should().BeTrue();
    }

    // Build a complete local stroke so the canvas has an undoable action.
    private static Guid AddLocalStroke(CanvasStateService canvas)
    {
        var action = Guid.NewGuid();
        var stroke = Guid.NewGuid();
        canvas.ApplyEvent(new StartStrokeEvent(action, stroke, new SKPoint(5, 5),
            new StrokePaint(), ToolType.Pencil, []));
        canvas.ApplyEvent(new EndStrokeEvent(action));
        return stroke;
    }

    // -- Room membership events --

    [Fact]
    public async Task JoinRoomAsync_TwoClients_ClientJoinedRoomEventFiresOnFirst()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");

        var aliceTcs = new TaskCompletionSource<(MultiUserDrawingClient User, List<MultiUserDrawingClient> Users)>();
        _aliceService.ClientJoinedRoom += (user, users) =>
        {
            if (user.Name == "Bob") aliceTcs.TrySetResult((user, users));
        };

        await _bobService.JoinRoomAsync(roomId, "Bob");

        var result = await aliceTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        result.User.Name.Should().Be("Bob");
        result.Users.Should().HaveCount(2);
    }

    [Fact]
    public async Task LeaveRoomAsync_SecondClientLeaves_ClientLeftRoomEventFiresOnFirst()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");
        await _bobService.JoinRoomAsync(roomId, "Bob");

        var aliceTcs = new TaskCompletionSource<(MultiUserDrawingClient User, List<MultiUserDrawingClient> Users)>();
        _aliceService.ClientLeftRoom += (user, users) =>
        {
            if (user.Name == "Bob") aliceTcs.TrySetResult((user, users));
        };

        await _bobService.LeaveRoomAsync("Bob");

        var result = await aliceTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        result.User.Name.Should().Be("Bob");
        result.Users.Should().HaveCount(1); // Only Alice left in the room
    }

    // -- Chat --

    [Fact]
    public async Task BroadcastMessageAsync_RecipientReceivesMessage_MessageReceivedFires()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");
        await _bobService.JoinRoomAsync(roomId, "Bob");

        var bobMsgTcs = new TaskCompletionSource<Message>();
        _bobService.MessageReceived += msg =>
        {
            if (msg.DisplayName == "Alice") bobMsgTcs.TrySetResult(msg);
        };

        var dto = new MessageDto("msg1", "Alice", "Hello World");
        await _aliceService.BroadcastMessageAsync(dto);

        var msg = await bobMsgTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        msg.Content.Should().Be("Hello World");
    }

    [Fact]
    public async Task BroadcastMessageAsync_SenderReceivesAck_MessageSentFires()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");
        await _bobService.JoinRoomAsync(roomId, "Bob");

        var aliceAckTcs = new TaskCompletionSource<string>();
        _aliceService.MessageSent += msgId => { aliceAckTcs.TrySetResult(msgId); };

        var dto = new MessageDto("msg2", "Alice", "Just sent this");
        await _aliceService.BroadcastMessageAsync(dto);

        var msgId = await aliceAckTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        msgId.Should().Be(dto.Id);
    }
}