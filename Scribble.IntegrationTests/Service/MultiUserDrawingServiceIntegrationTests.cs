using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using NSubstitute;
using Scribble.IntegrationTests.Hub;
using Scribble.Services.AutoSaveService;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DialogService;
using Scribble.Services.DocumentService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.IntegrationTests.Service;

[Collection("HubTests")]
public class MultiUserDrawingServiceIntegrationTests(HubServerFixture fixture)
    : IClassFixture<HubServerFixture>, IAsyncLifetime
{
    private MultiUserDrawingService _aliceService = null!;
    private MultiUserDrawingService _bobService = null!;

    public ValueTask InitializeAsync()
    {
        // Leverage the internal constructor to bypass standard network pipeline
        _aliceService = new MultiUserDrawingService(fixture.CreateHubConnection);
        _bobService = new MultiUserDrawingService(fixture.CreateHubConnection);
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
        var roomId = Guid.NewGuid().ToString();
        var canvas = new CanvasStateService(_bobService);
        var stroke = AddLocalStroke(canvas);
        AddLocalStroke(canvas);
        canvas.Undo();
        await _aliceService.JoinRoomAsync(roomId, "Alice");
        var requested = fixture.Faults.HoldNextJoin();
        var join = _bobService.JoinRoomAsync(roomId, "Bob");
        var token = TestContext.Current.CancellationToken;
        var id = await requested.WaitAsync(TimeSpan.FromSeconds(5), token);
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
        await _aliceService.JoinRoomAsync(room, "Alice");
        try
        {
            using var autosave = new AutoSaveService(canvas, new DocumentService(canvas), service,
                directory.FullName, TimeSpan.FromMilliseconds(50), dispatcher: Dispatcher.UIThread);
            var stroke = AddLocalStroke(canvas);
            var requested = fixture.Faults.HoldNextJoin();
            var join = service.JoinRoomAsync(room, "Bob");
            var id = await requested.WaitAsync(TimeSpan.FromSeconds(5), token);
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

    /// <summary>
    /// Build a complete local stroke so the canvas has an undoable action.
    /// </summary>
    private static Guid AddLocalStroke(CanvasStateService canvas)
    {
        var action = Guid.NewGuid();
        var stroke = Guid.NewGuid();
        canvas.ApplyEvent(new StartStrokeEvent(action, stroke, new SKPoint(5, 5),
            new StrokePaint(), ToolType.Pencil, []));
        canvas.ApplyEvent(new EndStrokeEvent(action));
        return stroke;
    }

    // An oversized seed reports its limit before replacing the solo document or its undo stack.
    [Fact]
    public async Task JoinRoom_OversizedSeed_ReportsFailureAndPreservesSoloCanvas()
    {
        var alice = new CanvasStateService(_aliceService);
        var stroke = AddLocalStroke(alice);
        var payload = new AddImageEvent(Guid.NewGuid(), Guid.NewGuid(),
            Convert.ToBase64String(new byte[RoomProtocol.AssetBytes + 1]),
            SKPoint.Empty, new SKSize(1, 1));
        alice.ApplyEvent(payload);
        string? failure = null;
        _aliceService.JoinFailed += reason => failure = reason;
        await _aliceService.JoinRoomAsync(Guid.NewGuid().ToString(), "Alice");
        Assert.Contains("32 MiB", failure);
        Assert.Null(_aliceService.Room);
        Assert.True(alice.CanUndo);
        Assert.Contains(alice.CanvasElements, e => e.Id == stroke);
        Assert.Contains(alice.CanvasElements, e => e.Id == payload.ImageId);
    }

    // Rejoining an occupied or empty room restores ownership and excludes later solo edits.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Rejoin_RecoversOwnedStrokeAndRoomHistory(bool occupied, bool disconnect)
    {
        var room = Guid.NewGuid().ToString();
        var alice = new CanvasStateService(_aliceService);
        var bob = new CanvasStateService(_bobService);
        var seed = AddLocalStroke(alice);
        await _aliceService.JoinRoomAsync(room, "Alice");
        var owner = _aliceService.Room!.Me.ParticipantId;
        var connection = _aliceService.ConnectionId;
        if (occupied) await _bobService.JoinRoomAsync(room, "Bob");
        if (disconnect)
        {
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _aliceService.ConnectionStopped += () => stopped.TrySetResult();
            fixture.Faults.Abort(connection!);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        else await _aliceService.LeaveRoomAsync("Alice");

        var solo = AddLocalStroke(alice);
        if (occupied)
        {
            bob.ApplyEvent(new SelectByIdsEvent(Guid.NewGuid(), Guid.NewGuid(), [seed]));
            Assert.Empty(bob.SelectedElementIds);
            bob.ApplyEvent(new UpdateStrokeColorEvent(Guid.NewGuid(), [seed], SKColors.Blue));
            Assert.Equal(SKColors.Red, Assert.IsType<DrawStroke>(bob.CanvasElements.Single()).Paint.Color);
            AddLocalStroke(bob);
            await _bobService.BroadcastEventAsync(new EndStrokeEvent(Guid.NewGuid()));
        }

        await _aliceService.JoinRoomAsync(room, "Alice");
        Assert.Equal(owner, _aliceService.Room!.Me.ParticipantId);
        Assert.NotEqual(connection, _aliceService.ConnectionId);
        Assert.DoesNotContain(alice.CanvasElements, e => e.Id == solo);
        Assert.Equal(occupied ? 2 : 1, alice.CanvasElements.Count);
        alice.ApplyEvent(new UpdateStrokeColorEvent(Guid.NewGuid(), [seed], SKColors.Green));
        Assert.Equal(SKColors.Green,
            Assert.IsType<DrawStroke>(alice.CanvasElements.Single(e => e.Id == seed)).Paint.Color);
    }

    // Reverse two valid optimistic layer edits and compare the confirmed histories on three clients.
    [AvaloniaFact]
    public async Task ConcurrentLayerChanges_WritersAndLateJoinerAgree()
    {
        var aliceService = new MultiUserDrawingService(fixture.CreateHubConnection, Dispatcher.UIThread);
        var bobService = new MultiUserDrawingService(fixture.CreateHubConnection, Dispatcher.UIThread);
        try
        {
            var room = Guid.NewGuid().ToString();
            var alice = new CanvasStateService(aliceService);
            var bob = new CanvasStateService(bobService);
            await aliceService.JoinRoomAsync(room, "Alice");
            await bobService.JoinRoomAsync(room, "Bob");
            var a = AddLocalStroke(alice);
            await aliceService.BroadcastEventAsync(new EndStrokeEvent(Guid.NewGuid()));
            var b = AddLocalStroke(bob);
            await bobService.BroadcastEventAsync(new EndStrokeEvent(Guid.NewGuid()));
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = fixture.Faults.HoldNextSend(aliceService.ConnectionId!, release.Task);
            var update = new SetElementLayerEvent(Guid.NewGuid(), [b], 10);
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = new SetElementLayerEvent(Guid.NewGuid(), [a], 10);
            aliceService.EventReceived += e =>
            {
                if (e.Id == update.Id) received.TrySetResult();
            };
            bobService.EventReceived += e =>
            {
                if (e.Id == first.Id) settled.TrySetResult();
            };
            try
            {
                alice.ApplyEvent(first);
                await held.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                bob.ApplyEvent(update);
                await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
            finally
            {
                release.TrySetResult();
            }

            await aliceService.BroadcastEventAsync(first);
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var third = new MultiUserDrawingService(fixture.CreateHubConnection);
            try
            {
                var late = new CanvasStateService(third);
                await third.JoinRoomAsync(room, "Third");
                Assert.Equal(new[] { b, a }, late.CanvasElements.Select(e => e.Id));
                Assert.Equal(late.CanvasElements.Select(e => e.Id), alice.CanvasElements.Select(e => e.Id));
                Assert.Equal(late.CanvasElements.Select(e => e.Id), bob.CanvasElements.Select(e => e.Id));
                Assert.Equal(late.CanvasEvents.Select(e => e.Id), alice.CanvasEvents.Select(e => e.Id));
            }
            finally
            {
                await third.LeaveRoomAsync("Third");
            }
        }
        finally
        {
            await Task.WhenAll(aliceService.LeaveRoomAsync("Alice"), bobService.LeaveRoomAsync("Bob"));
        }
    }

    /// <summary>Measures complete joins and replay on stroke and repeated-image boards.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(96)]
    public async Task JoinRoom_LargeHistory_InstallsCompleteSnapshot(int placements)
    {
        using var server = new RoomBenchmarkServer();
        await server.StartAsync(TestContext.Current.CancellationToken);
        _aliceService = new MultiUserDrawingService(() => server.Connect(false));
        var alice = new CanvasStateService(_aliceService);
        var reader = new MultiUserDrawingService(() => server.Connect(true, true));
        var concurrent = new MultiUserDrawingService(() => server.Connect(true));
        var failures = new ConcurrentQueue<string>();
        _aliceService.JoinFailed += failures.Enqueue;
        reader.JoinFailed += failures.Enqueue;
        concurrent.JoinFailed += failures.Enqueue;
        var replay = new Stopwatch();
        reader.RoomCanvasReady += (_, _) => replay.Start();
        var bob = new CanvasStateService(reader);
        reader.RoomCanvasReady += (_, _) => replay.Stop();
        var other = new CanvasStateService(concurrent);
        if (placements > 0)
        {
            // Use a noisy PNG so compression does not hide the cost of image transfer.
            using var bitmap = new SKBitmap(256, 256);
            var random = new Random(8);
            bitmap.Pixels = Enumerable.Range(0, 256 * 256)
                .Select(_ => new SKColor((uint)random.Next(1 << 24) | 0xff000000u)).ToArray();
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            var encoded = Convert.ToBase64String(png.ToArray());
            for (var i = 0; i < placements; i++)
            {
                alice.ApplyEvent(new AddImageEvent(Guid.NewGuid(), Guid.NewGuid(), encoded, new SKPoint(i, i),
                    new SKSize(256, 256)));
            }
        }
        else
        {
            var stroke = Guid.NewGuid();
            var action = Guid.NewGuid();
            alice.ApplyEvent(
                new StartStrokeEvent(action, stroke, new SKPoint(), new StrokePaint(), ToolType.Pencil, []));
            for (var i = 1; i <= 5000; i++)
                alice.ApplyEvent(new PencilStrokeLineToEvent(action, stroke, new SKPoint(i, i % 100)));
            alice.ApplyEvent(new EndStrokeEvent(action));
        }

        var room = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(room, "Alice");
        long transferred = 0;
        reader.DownloadProgress += (received, _) => transferred = received;
        var timer = Stopwatch.StartNew();
        double? previewMs = null;
        reader.BoardPreview += board =>
        {
            if (board != null)
            {
                previewMs ??= timer.Elapsed.TotalMilliseconds;
            }
        };
        await Task.WhenAll(reader.JoinRoomAsync(room, "Bob"), concurrent.JoinRoomAsync(room, "Third"));
        timer.Stop();
        var participant = reader.Room?.Me.ParticipantId;
        await Task.WhenAll(reader.LeaveRoomAsync("Bob"), concurrent.LeaveRoomAsync("Third"));
        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures));
        Assert.NotNull(participant);
        Assert.Equal(alice.CanvasEvents.Count, other.CanvasEvents.Count);
        Assert.Equal(alice.CanvasEvents.Count, bob.CanvasEvents.Count);
        Assert.Equal(alice.CanvasElements.Count, bob.CanvasElements.Count);

        TestContext.Current.TestOutputHelper!.WriteLine(
            $"ServerPeak={server.PeakBytes}, Placements={placements}, response={server.ResponseBytes}, preview={previewMs:F1} ms, snapshot and images={transferred}, both joins={timer.Elapsed.TotalMilliseconds:F1}, replay={replay.Elapsed.TotalMilliseconds:F1} ms");
        await _aliceService.LeaveRoomAsync("Alice");
    }

    /// <summary>Exercises a shared image across owners, a frozen join cut, cancellation, and undo.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinRoom_RepeatedImageDuringDelete_PreservesBoardAndOwnership(bool cancel)
    {
        var ct = TestContext.Current.CancellationToken;
        var room = Guid.NewGuid().ToString();
        var asset = new byte[RoomProtocol.ChunkBytes + 1];
        new Random(31).NextBytes(asset);
        var source = Convert.ToBase64String(asset);
        await _aliceService.JoinRoomAsync(room, "Alice");
        await _bobService.JoinRoomAsync(room, "Bob");
        var placements = Enumerable.Range(0, 10).Select(i =>
            new AddImageEvent(Guid.NewGuid(), Guid.NewGuid(), source, new SKPoint(i, i), new SKSize(16, 16))).ToArray();
        for (var i = 0; i < placements.Length; i += 2)
        {
            await Task.WhenAll(_aliceService.BroadcastEventAsync(placements[i]),
                _bobService.BroadcastEventAsync(placements[i + 1]));
        }

        var reader = new MultiUserDrawingService(fixture.CreateHubConnection);
        var canvas = new CanvasStateService(reader);
        var solo = AddLocalStroke(canvas);
        var preview = new List<CanvasElement>();
        reader.BoardPreview += board =>
        {
            if (board != null)
            {
                preview = board;
            }
        };
        long received = 0, total = 0;
        reader.DownloadProgress += (count, expected) => (received, total) = (count, expected);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = fixture.Faults.HoldImageRead(release.Task);
        var joining = reader.JoinRoomAsync(room, "Third");
        try
        {
            await held.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.InRange(received, 1, total - 1);
            Assert.Equal(10, preview.Count);
            Assert.Equal(2, preview.Select(p => p.CreatorConnectionId).Distinct().Count());
            Assert.All(preview.Cast<CanvasImage>(),
                image => Assert.Equal(RoomAssets.Key(asset), image.ImageBase64String));
            Assert.Equal(1, fixture.Faults.Downloads(reader.ConnectionId!));
            var delete = new EraseByIdsEvent(Guid.NewGuid(), [placements[0].ImageId]);
            await _aliceService.BroadcastEventAsync(delete);
            if (cancel)
            {
                await reader.LeaveRoomAsync("Third");
                await joining;
                Assert.Null(reader.Room);
                Assert.Equal(solo, Assert.Single(canvas.CanvasElements).Id);
                Assert.True(canvas.CanUndo);
                return;
            }

            release.TrySetResult();
            await joining;
            Assert.Equal(total, received);
            Assert.Equal(9, canvas.CanvasElements.Count);
            Assert.DoesNotContain(canvas.CanvasElements, e => e.Id == placements[0].ImageId);
            var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            reader.EventReceived += edit =>
            {
                if (edit is UndoEvent)
                {
                    restored.TrySetResult();
                }
            };
            await _aliceService.BroadcastEventAsync(new UndoEvent(Guid.NewGuid(), delete.ActionId));
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(10, canvas.CanvasElements.Count);
            Assert.All(canvas.CanvasElements.Cast<CanvasImage>(),
                image => Assert.Equal(source, image.ImageBase64String));
            Assert.Equal(1, fixture.Faults.Downloads(reader.ConnectionId!));
        }
        finally
        {
            release.TrySetResult();
            await reader.LeaveRoomAsync("Third");
        }
    }

    /// <summary>Checks chunked live metadata and the first image fetched for a joining viewport.</summary>
    [Fact]
    public async Task JoinRoom_OffscreenImageFirst_PrioritizesTheViewportAndRestoresLargeEdits()
    {
        var room = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(room, "Alice");
        await _bobService.JoinRoomAsync(room, "Bob");
        var large = new AddTextEvent(Guid.NewGuid(), Guid.NewGuid(), SKPoint.Empty,
            new string(' ', RoomProtocol.ChunkBytes + 1), new StrokePaint(), []);
        var delivered = new TaskCompletionSource<Event>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bobService.EventReceived += edit =>
        {
            if (edit.Id == large.Id)
            {
                delivered.TrySetResult(edit);
            }
        };
        await _aliceService.BroadcastEventAsync(large);
        Assert.Equal(large.Text, Assert.IsType<AddTextEvent>(await delivered.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Text);

        var offscreen = new AddImageEvent(Guid.NewGuid(), Guid.NewGuid(), "AQ==", new SKPoint(300, 300),
            new SKSize(10, 10));
        var visible = new AddImageEvent(Guid.NewGuid(), Guid.NewGuid(), "Ag==", SKPoint.Empty, new SKSize(10, 10));
        await _aliceService.BroadcastEventAsync(offscreen);
        await _aliceService.BroadcastEventAsync(visible);
        var reader = new MultiUserDrawingService(fixture.CreateHubConnection)
        {
            CaptureViewport = () => SKRect.Create(0, 0, 50, 50)
        };
        var firstResolved = new List<Guid>();
        reader.BoardPreview += board =>
        {
            if (board != null && firstResolved.Count == 0)
            {
                firstResolved.AddRange(board.OfType<CanvasImage>()
                    .Where(image => !image.ImageBase64String.StartsWith("sha256:", StringComparison.Ordinal))
                    .Select(image => image.Id));
            }
        };
        try
        {
            await reader.JoinRoomAsync(room, "Third");
            Assert.NotNull(reader.Room);
            Assert.Equal(visible.ImageId, Assert.Single(firstResolved));
        }
        finally
        {
            await reader.LeaveRoomAsync("Third");
        }
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