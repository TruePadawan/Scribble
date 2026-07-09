using FluentAssertions;
using Scribble.IntegrationTests.Hub;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.Events;

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
        _aliceService = new MultiUserDrawingService(fixture.CreateHubConnection());
        _bobService = new MultiUserDrawingService(fixture.CreateHubConnection());
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
        var invalidService = new MultiUserDrawingService("http://localhost:9999/badurl");

        await invalidService.JoinRoomAsync("room", "Alice");

        invalidService.Room.Should().BeNull();
        invalidService.IsConnected.Should().BeFalse();
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
    public async Task SendCanvasStateToClientAsync_TargetClient_CanvasStateReceivedFires()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceService.JoinRoomAsync(roomId, "Alice");

        var aliceRequestTcs = new TaskCompletionSource<string>();
        _aliceService.CanvasStateRequested += targetClientConnectionId =>
        {
            aliceRequestTcs.TrySetResult(targetClientConnectionId);
        };

        var bobStateTcs = new TaskCompletionSource<Queue<Event>>();
        _bobService.CanvasStateReceived += events => { bobStateTcs.TrySetResult(events); };

        await _bobService.JoinRoomAsync(roomId, "Bob");
        var bobConnectionId =
            await aliceRequestTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var queue = new Queue<Event>();
        queue.Enqueue(new EndStrokeEvent(Guid.NewGuid()));

        await _aliceService.SendCanvasStateToClientAsync(bobConnectionId, queue);

        var receivedQueue =
            await bobStateTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        receivedQueue.Should().HaveCount(1);
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