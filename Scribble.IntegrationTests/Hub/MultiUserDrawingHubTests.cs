using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using SkiaSharp;
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

    public async ValueTask InitializeAsync()
    {
        _aliceConnection = fixture.CreateHubConnection();
        _bobConnection = fixture.CreateHubConnection();
        await _aliceConnection.StartAsync();
        await _bobConnection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _aliceConnection.StopAsync();
        await _bobConnection.StopAsync();
        await _aliceConnection.DisposeAsync();
        await _bobConnection.DisposeAsync();
    }

    // Independent server hosts may both have a first member in the same room.
    [Fact]
    public async Task JoinRoom_SameRoomOnTwoHosts_HasIndependentRosters()
    {
        var token = TestContext.Current.CancellationToken;
        await using var otherHost = new HubServerFixture();
        await using var other = otherHost.CreateHubConnection();

        var roster =
            new TaskCompletionSource<List<MultiUserDrawingClient>>(TaskCreationOptions.RunContinuationsAsynchronously);
        other.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientJoined",
            (_, members) => roster.SetResult(members));
        var room = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", room, "Alice", null, "[]", token);
        await other.StartAsync(token);
        await other.InvokeAsync<RoomAdmission>("JoinRoom", room, "Bob", null, "[]", token);
        (await roster.Task.WaitAsync(TimeSpan.FromSeconds(5), token)).Should().ContainSingle(m => m.Name == "Bob");
    }

    // Only the active token binding can edit, including when both connections use the same name.
    [Fact]
    public async Task Reclaim_RevokesOldTransportAndRejectsForeignBatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var room = Guid.NewGuid().ToString();
        var alice = await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", room, "Alice", null, "[]", ct);
        var bob = await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", room, "Alice", null, null, ct);
        Assert.NotEqual(alice.ParticipantId, bob.ParticipantId);
        var stroke = new StartStrokeEvent(Guid.NewGuid(), Guid.NewGuid(), SKPoint.Empty, new StrokePaint(), ToolType.Pencil, []);
        var other = stroke with { StrokeId = Guid.NewGuid(), ActionId = Guid.NewGuid(), Id = Guid.NewGuid() };
        await _aliceConnection.InvokeAsync("SendEvent", room, stroke, ct);
        await _bobConnection.InvokeAsync("SendEvent", room, other, ct);
        await Assert.ThrowsAsync<HubException>(() => _bobConnection.InvokeAsync("SendEvent", room,
            new EraseByIdsEvent(Guid.NewGuid(), [other.StrokeId, stroke.StrokeId]), ct));
        await Assert.ThrowsAsync<HubException>(() => _bobConnection.InvokeAsync("SendEvent", room,
            new UndoEvent(Guid.NewGuid(), stroke.ActionId), ct));

        await using var replacement = fixture.CreateHubConnection();
        await replacement.StartAsync(ct);
        var resumed = await replacement.InvokeAsync<RoomAdmission>("JoinRoom", room, "Alice", alice.Token, null, ct);
        Assert.Equal(alice.ParticipantId, resumed.ParticipantId);
        Assert.Equal(2, JsonSerializer.Deserialize<List<Event>>(resumed.Snapshot!)!.Count);
        await Assert.ThrowsAsync<HubException>(() => _aliceConnection.InvokeAsync("SendEvent", room, new EndStrokeEvent(stroke.ActionId), ct));
        await replacement.InvokeAsync("SendEvent", room, new EraseByIdsEvent(Guid.NewGuid(), [stroke.StrokeId]), ct);
    }

    // An expired last participant cannot reclaim a new instance created under the same room code.
    [Fact]
    public async Task Rejoin_ExpiredEmptyRoom_IssuesFreshIdentity()
    {
        await using var host = new HubServerFixture();
        await using var first = host.CreateHubConnection();
        await using var next = host.CreateHubConnection();
        var ct = TestContext.Current.CancellationToken;
        await first.StartAsync(ct);
        await next.StartAsync(ct);
        var initial = await first.InvokeAsync<RoomAdmission>("JoinRoom", "room", "Alice", null, "[]", ct);
        await first.InvokeAsync("LeaveRoom", "room", "Alice", ct);
        host.Clock.Offset = TimeSpan.FromMinutes(11);
        Assert.True(await next.InvokeAsync<bool>("NeedsSeed", "room", ct));
        var fresh = await next.InvokeAsync<RoomAdmission>("JoinRoom", "room", "Alice", initial.Token, "[]", ct);
        Assert.NotEqual(initial.ParticipantId, fresh.ParticipantId);
        Assert.NotEqual(initial.Token, fresh.Token);
    }

    // Measure the normalized room snapshot including the server-assigned author and order.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Seed_AtSnapshotByteBoundary_EnforcesLimit(int extraBytes)
    {
        var ct = TestContext.Current.CancellationToken;
        var room = Guid.NewGuid().ToString();
        var empty = new AddTextEvent(Guid.NewGuid(), Guid.NewGuid(), SKPoint.Empty, "", new StrokePaint(), [])
            { RoomSequence = 1, CreatorConnectionId = new string('0', 32) };
        var overhead = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize<Event[]>([empty]));
        var seed = JsonSerializer.Serialize<Event[]>([empty with { Text = new string('x', RoomProtocol.SnapshotBytes - overhead + extraBytes) }]);
        var join = _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", room, "Alice", null, seed, ct);
        if (extraBytes == 0)
            Assert.Equal(RoomProtocol.SnapshotBytes, Encoding.UTF8.GetByteCount((await join).Snapshot!));
        else
            Assert.Contains("8 MiB", (await Assert.ThrowsAsync<HubException>(() => join)).Message);
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

        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        result.User.Name.Should().Be("Alice");
        result.Users.Should().ContainSingle(u => u.Name == "Alice");
    }

    [Fact]
    public async Task JoinRoom_TwoClients_BothClientsReceiveClientJoinedWithUpdatedList()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
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

        await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Bob", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);

        var aliceUsers = await aliceTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var bobUsers = await bobTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        aliceUsers.Should().HaveCount(2).And.Contain(u => u.Name == "Bob");
        bobUsers.Should().HaveCount(2).And.Contain(u => u.Name == "Alice");
    }

    [Fact]
    public async Task Disconnect_AfterJoiningRoom_RemainingClientReceivesClientLeftEvent()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Bob", null, "[]",
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
        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Bob", null, "[]",
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
        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Bob", null, "[]",
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
    public async Task SendEvent_WithSpoofedCreatorId_UsesAuthenticatedOwner()
    {
        var roomId = Guid.NewGuid().ToString();
        var alice = await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Bob", null, "[]",
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

        var received = await bobEventTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        received.CreatorConnectionId.Should().Be(alice.ParticipantId);
    }

    // Check that drawing is routed only within the sender's room.
    [Fact]
    public async Task DrawingRequiresRoomMembershipAndUsesServerCreatorId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var roomId = Guid.NewGuid().ToString();
        var alice = await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]", cancellationToken);
        await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Bob", null, "[]", cancellationToken);
        await using var otherRoomConnection = fixture.CreateHubConnection();
        await otherRoomConnection.StartAsync(cancellationToken);
        await otherRoomConnection.InvokeAsync<RoomAdmission>("JoinRoom", Guid.NewGuid().ToString(), "Charlie", null, "[]", cancellationToken);

        var delivered = new TaskCompletionSource<Event>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var receiver =
            _bobConnection.On<Event>("ReceiveEvent", incoming => { delivered.TrySetResult(incoming); });
        var authorized = new EndStrokeEvent(Guid.NewGuid());
        await Assert.ThrowsAsync<HubException>(() => otherRoomConnection.InvokeAsync("SendEvent", roomId, new EndStrokeEvent(Guid.NewGuid()), cancellationToken));
        await _aliceConnection.InvokeAsync("SendEvent", roomId, authorized, cancellationToken);

        var result = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        result.ActionId.Should().Be(authorized.ActionId);
        result.CreatorConnectionId.Should().Be(alice.ParticipantId);
    }

    [Fact]
    public async Task SendMessage_ValidContent_OtherClientReceivesMessage()
    {
        var roomId = Guid.NewGuid().ToString();
        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Bob", null, "[]",
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
        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Bob", null, "[]",
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
        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);
        await _bobConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Bob", null, "[]",
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
        await _aliceConnection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, "Alice", null, "[]",
            cancellationToken: TestContext.Current.CancellationToken);

        var aliceMsgTcs = new TaskCompletionSource<Message>();
        _aliceConnection.On<Message>("ReceiveMessage", message =>
        {
            if (message.DisplayName == "Bob") aliceMsgTcs.TrySetResult(message);
        });

        // Bob tries to send without joining
        var dto = new MessageDto("msg4", "Bob", "Intruder!");
        await Assert.ThrowsAsync<HubException>(() => _bobConnection.InvokeAsync("SendMessage", roomId, dto, TestContext.Current.CancellationToken));

        var task = aliceMsgTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(() => task);
    }
}