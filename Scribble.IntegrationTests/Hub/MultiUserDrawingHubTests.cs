using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.Events;

namespace Scribble.IntegrationTests.Hub;

[Collection("HubTests")]
public class MultiUserDrawingHubTests(HubServerFixture fixture) : IClassFixture<HubServerFixture>, IAsyncLifetime
{
    private HubConnection _aliceConnection = null!;
    private HubConnection _bobConnection = null!;
    private IDisposable _answerAliceSnapshotRequests = null!;

    public async ValueTask InitializeAsync()
    {
        _aliceConnection = fixture.CreateHubConnection();
        _bobConnection = fixture.CreateHubConnection();
        _answerAliceSnapshotRequests = _aliceConnection.On<string>("RequestCanvasState", targetId =>
        {
            _ = _aliceConnection.SendAsync("SendCanvasStateToClient", targetId, "[]");
        });

        await _aliceConnection.StartAsync();
        await _bobConnection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _answerAliceSnapshotRequests.Dispose();
        await _aliceConnection.StopAsync();
        await _bobConnection.StopAsync();
        await _aliceConnection.DisposeAsync();
        await _bobConnection.DisposeAsync();
    }

    // -- Room Join --

    [Fact]
    public async Task JoinRoom_SingleClient_ClientJoinedEventContainsOnlyThatClient()
    {
        var roomId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<(MultiUserDrawingClient User, List<MultiUserDrawingClient> Users)>();

        _aliceConnection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientJoined", (user, users) =>
        {
            if (user.Name == "Alice") tcs.TrySetResult((user, users));
        });

        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        result.User.Name.Should().Be("Alice");
        result.Users.Should().ContainSingle(u => u.Name == "Alice");
    }

    [Fact]
    public async Task JoinRoom_TwoClients_BothClientsReceiveClientJoinedWithUpdatedList()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);

        var aliceTcs = new TaskCompletionSource<List<MultiUserDrawingClient>>();
        var bobTcs = new TaskCompletionSource<List<MultiUserDrawingClient>>();

        _aliceConnection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientJoined", (user, users) =>
        {
            if (user.Name == "Bob") aliceTcs.TrySetResult(users);
        });

        _bobConnection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientJoined", (user, users) =>
        {
            if (user.Name == "Bob") bobTcs.TrySetResult(users);
        });

        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var aliceUsers = await aliceTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var bobUsers = await bobTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        aliceUsers.Should().HaveCount(2).And.Contain(u => u.Name == "Bob");
        bobUsers.Should().HaveCount(2).And.Contain(u => u.Name == "Alice");
    }

    [Fact]
    public async Task JoinRoom_TwoClients_HostReceivesCanvasStateRequest()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);

        var hostRequestTcs = new TaskCompletionSource<string>();
        _aliceConnection.On<string>("RequestCanvasState",
            newClientConnectionId => { hostRequestTcs.TrySetResult(newClientConnectionId); });

        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var targetConnectionId =
            await hostRequestTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        targetConnectionId.Should().Be(_bobConnection.ConnectionId);
    }

    // -- Disconnect / Leave --

    [Fact]
    public async Task Disconnect_AfterJoiningRoom_RemainingClientReceivesClientLeftEvent()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var aliceTcs = new TaskCompletionSource<MultiUserDrawingClient>();
        _aliceConnection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientLeft",
            (user, users) => { aliceTcs.TrySetResult(user); });

        // Simulate Bob abruptly disconnecting
        await _bobConnection.StopAsync(TestContext.Current.CancellationToken);

        var leftUser = await aliceTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        leftUser.Name.Should().Be("Bob");
    }

    [Fact]
    public async Task LeaveRoom_ClientLeaves_OtherClientReceivesClientLeftEvent()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var aliceTcs = new TaskCompletionSource<MultiUserDrawingClient>();
        _aliceConnection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientLeft",
            (user, users) => { aliceTcs.TrySetResult(user); });

        await _bobConnection.InvokeAsync("LeaveRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var leftUser = await aliceTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        leftUser.Name.Should().Be("Bob");
    }

    // -- Event Routing --

    [Fact]
    public async Task SendEvent_ToRoom_OtherClientReceivesEvent()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var bobEventTcs = new TaskCompletionSource<Event>();
        _bobConnection.On<Event>("ReceiveEvent", @event => { bobEventTcs.TrySetResult(@event); });

        var testEvent = new EndStrokeEvent(Guid.NewGuid())
        {
            CreatorConnectionId = _aliceConnection.ConnectionId
        };

        await _aliceConnection.InvokeAsync("SendEvent", roomId, testEvent,
            cancellationToken: TestContext.Current.CancellationToken);

        var received = await bobEventTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        received.Should().BeOfType<EndStrokeEvent>();
    }

    [Fact]
    public async Task SendEvent_WithSpoofedCreatorId_EventIsDropped()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var bobEventTcs = new TaskCompletionSource<Event>();
        _bobConnection.On<Event>("ReceiveEvent", @event => { bobEventTcs.TrySetResult(@event); });

        var spoofedEvent = new EndStrokeEvent(Guid.NewGuid())
        {
            // Alice pretends to be Bob
            CreatorConnectionId = _bobConnection.ConnectionId
        };

        await _aliceConnection.InvokeAsync("SendEvent", roomId, spoofedEvent,
            cancellationToken: TestContext.Current.CancellationToken);

        var task = bobEventTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(() => task);
    }

    // -- Canvas State Relay --

    [Fact]
    public async Task SendCanvasStateToClient_TargetClientReceivesPayload()
    {
        _answerAliceSnapshotRequests.Dispose();
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);

        var bobStateTcs = new TaskCompletionSource<string>();
        using var receiver = _bobConnection.On<string>("ReceiveCanvasState", state => { bobStateTcs.TrySetResult(state); });
        var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var request = _aliceConnection.On<string>("RequestCanvasState", targetId => { requested.TrySetResult(targetId); });
        var join = _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        const string payload = "[]";
        var target = await requested.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await _aliceConnection.InvokeAsync("SendCanvasStateToClient", target, payload,
            cancellationToken: TestContext.Current.CancellationToken);

        var receivedState =
            await bobStateTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        receivedState.Should().Be(payload);
        await join;
    }

    // Check that one requested snapshot reaches the joining client.
    [Fact]
    public async Task SnapshotCanOnlyBeDeliveredOnceByDesignatedHost()
    {
        _answerAliceSnapshotRequests.Dispose();
        var cancellationToken = TestContext.Current.CancellationToken;
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice", cancellationToken);
        var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var request = _aliceConnection.On<string>("RequestCanvasState", target => { requested.TrySetResult(target); });
        var snapshots = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var receiver = _bobConnection.On<string>("ReceiveCanvasState", snapshots.Enqueue);
        var join = _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob", cancellationToken);
        var targetId = await requested.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        await using var outsider = fixture.CreateHubConnection();
        await outsider.StartAsync(cancellationToken);
        await outsider.InvokeAsync("SendCanvasStateToClient", targetId, "[ ]", cancellationToken);
        await _aliceConnection.InvokeAsync("SendCanvasStateToClient", targetId, "[]", cancellationToken);
        await join;
        await _aliceConnection.InvokeAsync("SendCanvasStateToClient", targetId, "[ ]", cancellationToken);

        // A later room message confirms preceding deliveries have been processed.
        var messageReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var message = _bobConnection.On<Message>("ReceiveMessage", incoming =>
        {
            if (incoming.Content == "barrier") messageReceived.TrySetResult();
        });
        await _aliceConnection.InvokeAsync("SendMessage", roomId, new MessageDto("1", "Alice", "barrier"),
            cancellationToken);
        await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        snapshots.Should().Equal("[]");
    }

    // Check that drawing is routed only within the sender's room.
    [Fact]
    public async Task DrawingRequiresRoomMembershipAndUsesServerCreatorId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice", cancellationToken);
        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob", cancellationToken);
        await using var otherRoomConnection = fixture.CreateHubConnection();
        await otherRoomConnection.StartAsync(cancellationToken);
        await otherRoomConnection.InvokeAsync("JoinRoom", Guid.NewGuid().ToString(), "Charlie", cancellationToken);

        var delivered = new TaskCompletionSource<Event>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var receiver = _bobConnection.On<Event>("ReceiveEvent", incoming => { delivered.TrySetResult(incoming); });
        var authorized = new EndStrokeEvent(Guid.NewGuid());
        await otherRoomConnection.InvokeAsync("SendEvent", roomId, new EndStrokeEvent(Guid.NewGuid()), cancellationToken);
        await _aliceConnection.InvokeAsync("SendEvent", roomId, authorized, cancellationToken);

        var result = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        result.ActionId.Should().Be(authorized.ActionId);
        result.CreatorConnectionId.Should().Be(_aliceConnection.ConnectionId);
    }

    // Disconnect the original host while a third member is joining.
    [Fact]
    public async Task HostLeavesDuringSnapshotRequest_NextSynchronizedMemberAnswers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice", cancellationToken);
        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob", cancellationToken);
        _answerAliceSnapshotRequests.Dispose();

        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var request = _aliceConnection.On<string>("RequestCanvasState", _ => { requested.TrySetResult(); });
        // Bob already has the canvas and can take over when Alice leaves.
        using var answer = _bobConnection.On<string>("RequestCanvasState", target =>
        {
            _ = _bobConnection.SendAsync("SendCanvasStateToClient", target, "[]");
        });
        await using var joiningClient = fixture.CreateHubConnection();
        await joiningClient.StartAsync(cancellationToken);
        var join = joiningClient.InvokeAsync("JoinRoom", roomId, "Charlie", cancellationToken);

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await _aliceConnection.StopAsync(cancellationToken);
        await join.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
    }

    // Verify that a join fails promptly when no host remains.
    [Fact]
    public async Task OnlyHostLeavesDuringSnapshotRequest_JoinFails()
    {
        _answerAliceSnapshotRequests.Dispose();
        var cancellationToken = TestContext.Current.CancellationToken;
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice", cancellationToken);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var request = _aliceConnection.On<string>("RequestCanvasState", _ => { requested.TrySetResult(); });
        var join = _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob", cancellationToken);

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await _aliceConnection.StopAsync(cancellationToken);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(
            () => join.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));
    }

    // -- Chat Messaging --

    [Fact]
    public async Task SendMessage_ValidContent_OtherClientReceivesMessage()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var bobMsgTcs = new TaskCompletionSource<Message>();
        _bobConnection.On<Message>("ReceiveMessage", message =>
        {
            if (message.DisplayName == "Alice") bobMsgTcs.TrySetResult(message);
        });

        var dto = new MessageDto("msg1", "Alice", "Hello World");
        await _aliceConnection.InvokeAsync("SendMessage", roomId, dto,
            cancellationToken: TestContext.Current.CancellationToken);

        var received = await bobMsgTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        received.Content.Should().Be(dto.Content);
    }

    [Fact]
    public async Task SendMessage_OwnClient_ReceivesMessageSentAcknowledgement()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var ackTcs = new TaskCompletionSource<string>();
        _aliceConnection.On<string>("MessageSent", msgId => { ackTcs.TrySetResult(msgId); });

        var dto = new MessageDto("msg2", "Alice", "Just to me");
        await _aliceConnection.InvokeAsync("SendMessage", roomId, dto,
            cancellationToken: TestContext.Current.CancellationToken);

        var receivedMsgId = await ackTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        receivedMsgId.Should().Be(dto.Id);
    }

    [Fact]
    public async Task SendMessage_EmptyContent_IsDropped()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync("JoinRoom", roomId, "Bob",
            cancellationToken: TestContext.Current.CancellationToken);

        var bobMsgTcs = new TaskCompletionSource<Message>();
        _bobConnection.On<Message>("ReceiveMessage", message =>
        {
            if (message.DisplayName == "Alice") bobMsgTcs.TrySetResult(message);
        });

        var dto = new MessageDto("msg3", "Alice", "   ");
        await _aliceConnection.InvokeAsync("SendMessage", roomId, dto,
            cancellationToken: TestContext.Current.CancellationToken);

        var task = bobMsgTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(() => task);
    }

    [Fact]
    public async Task SendMessage_ClientNotInRoom_MessageIsDropped()
    {
        var roomId = Guid.NewGuid().ToString();
        // Alice joins, Bob does NOT join
        await _aliceConnection.InvokeAsync("JoinRoom", roomId, "Alice",
            cancellationToken: TestContext.Current.CancellationToken);

        var aliceMsgTcs = new TaskCompletionSource<Message>();
        _aliceConnection.On<Message>("ReceiveMessage", message =>
        {
            if (message.DisplayName == "Bob") aliceMsgTcs.TrySetResult(message);
        });

        // Bob tries to send without joining
        var dto = new MessageDto("msg4", "Bob", "Intruder!");
        await _bobConnection.InvokeAsync("SendMessage", roomId, dto,
            cancellationToken: TestContext.Current.CancellationToken);

        var task = aliceMsgTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(() => task);
    }
}
