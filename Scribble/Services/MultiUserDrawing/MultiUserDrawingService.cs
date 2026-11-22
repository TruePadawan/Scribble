using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Scribble.Shared.Dtos;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using SkiaSharp;

namespace Scribble.Services.MultiUserDrawing;

public class MultiUserDrawingService : IMultiUserDrawingService
{
    private readonly Func<HubConnection> _createConnection;
    private readonly Dispatcher? _dispatcher;

    private readonly Lock _gate = new();

    // Each send waits for the previous invocation on this connection.
    private Task _lastSend = Task.CompletedTask;
    private bool _deliveryFailed;
    private bool _leaving;
    private RoomTransfer? _transfer;
    private volatile HubConnection? _connection;
    private volatile MultiUserDrawingRoom? _room;
    private volatile PendingJoin? _pendingJoin;
    public MultiUserDrawingRoom? Room => _room;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    // Keep the canvas paused until the join is committed or cancelled.
    public bool IsJoining => _pendingJoin != null;
    public string? ConnectionId => _connection?.ConnectionId;

    public event Action<MultiUserDrawingRoom?>? RoomChanged;

    // Membership updates within the same session.
    public event Action<MultiUserDrawingRoom?>? RosterChanged;

    // Raised when delivery of a local edit could not be confirmed.
    public event Action? DrawingSyncFailed;

    public event Action? ConnectionStarted;
    public event Action? ConnectionStopped;
    public event Action<Event>? EventReceived;

    // Tokens belong to this service's server endpoint and live only in memory.
    private readonly Dictionary<string, string> _tokens = [];
    public Func<Queue<Event>>? CaptureSeed { get; set; }
    public bool SeededRoom { get; private set; }
    public event Action<string>? JoinFailed;
    public event Action? IdentityRenewed;

    // Deliver the snapshot and queued drawing updates in one UI callback.
    public event Action<Queue<Event>?, IReadOnlyList<Event>>? RoomCanvasReady;

    // Refresh editing controls when the join starts or ends.
    public event Action<long, long>? DownloadProgress;
    public Func<SKRect>? CaptureViewport { get; set; }
    public event Action<List<CanvasElement>?>? BoardPreview;
    public event Action? JoiningChanged;
    public event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientJoinedRoom;
    public event Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? ClientLeftRoom;
    public event Action<Message>? MessageReceived;
    public event Action<string>? MessageSent;

    public MultiUserDrawingService(string serverUrl)
        : this(() => new HubConnectionBuilder().WithUrl(serverUrl).Build(), Dispatcher.UIThread)
    {
    }

    internal MultiUserDrawingService(Func<HubConnection> createConnection, Dispatcher? dispatcher = null)
    {
        _createConnection = createConnection;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Recheck the session when queued UI work actually runs.
    /// </summary>
    internal async Task ForSessionAsync(HubConnection connection, Action action)
    {
        void Run()
        {
            lock (_gate)
            {
                if (connection == _connection) action();
            }
        }

        if (_dispatcher is null || _dispatcher.CheckAccess()) Run();
        else await _dispatcher.InvokeAsync(Run);
    }

    /// <summary>Binds incoming room messages to the connection that owns them.</summary>
    private void RegisterEventHandlers(HubConnection connection, RoomTransfer transfer)
    {
        connection.Closed += _ => EndSessionAsync(connection);
        connection.On<Event>("ReceiveEvent", edit => DeliverAsync(connection, transfer, Task.FromResult(edit)));
        connection.On<Guid, int>("ReceiveLargeEvent", (id, bytes) =>
            DeliverAsync(connection, transfer, transfer.ReadEventAsync(id, bytes)));

        connection.On("SessionReplaced", () => { _ = EndSessionAsync(connection); });

        connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientJoined", (client, clients) =>
            ForSessionAsync(connection, () =>
            {
                if (_pendingJoin is { } pending) pending.Roster = clients;
                else UpdateRoster(client, clients, ClientJoinedRoom);
            }));

        connection.On<MultiUserDrawingClient, List<MultiUserDrawingClient>>("ClientLeft", (client, clients) =>
            ForSessionAsync(connection, () =>
            {
                if (_pendingJoin is { } pending) pending.Roster = clients;
                else UpdateRoster(client, clients, ClientLeftRoom);
            }));

        connection.On<Message>("ReceiveMessage", message =>
            ForSessionAsync(connection, () => MessageReceived?.Invoke(message)));

        connection.On<string>("MessageSent", id =>
            ForSessionAsync(connection, () => MessageSent?.Invoke(id)));
    }

    /// <summary>Resolves room images and reports interrupted live downloads to the editor.</summary>
    private async Task DeliverAsync(HubConnection connection, RoomTransfer transfer, Task<Event> incoming)
    {
        lock (_gate)
        {
            if (_connection != connection)
            {
                return;
            }

            if (_pendingJoin is { } pending)
            {
                // Register receipt before resolving images so admission cannot overtake this edit.
                pending.Incoming.Add(incoming);
                return;
            }
        }

        try
        {
            var edit = await transfer.DecodeAsync(await incoming);
            await ForSessionAsync(connection, () => EventReceived?.Invoke(edit));
        }
        catch (Exception error)
        {
            Trace.TraceError("Room drawing download failed. {0}", error);
            await ForSessionAsync(connection, () => DrawingSyncFailed?.Invoke());
            _ = EndSessionAsync(connection);
        }
    }

    /// <summary>
    /// Replace the room's member list and notify listeners of the join or leave.
    /// </summary>
    private void UpdateRoster(MultiUserDrawingClient client, List<MultiUserDrawingClient> clients,
        Action<MultiUserDrawingClient, List<MultiUserDrawingClient>>? notify)
    {
        if (_room is not { } room) return;
        _room = new MultiUserDrawingRoom(room.RoomId, room.Me.ConnectionId, room.Me.Name, room.Me.ParticipantId)
            { Clients = clients };
        notify?.Invoke(client, clients);
        RosterChanged?.Invoke(_room);
    }

    /// <summary>
    /// Close, failed join, and explicit leave can all finish the same session.
    /// </summary>
    private async Task EndSessionAsync(HubConnection connection)
    {
        var ownsCleanup = false;
        await ForSessionAsync(connection, () =>
        {
            ownsCleanup = true;
            if (!_lastSend.IsCompleted && !_deliveryFailed) DrawingSyncFailed?.Invoke();
            _lastSend = Task.CompletedTask;
            _deliveryFailed = false;
            _leaving = false;
            _connection = null;
            _transfer = null;
            var pending = _pendingJoin;
            _pendingJoin = null;
            var hadRoom = _room != null;
            _room = null;
            if (hadRoom)
            {
                RoomChanged?.Invoke(null);
                ConnectionStopped?.Invoke();
            }

            if (pending != null)
            {
                BoardPreview?.Invoke(null);
                JoiningChanged?.Invoke();
            }
        });

        if (ownsCleanup) await connection.DisposeAsync();
    }

    public async Task JoinRoomAsync(string roomId, string displayName)
    {
        HubConnection connection;
        var pending = new PendingJoin();
        RoomTransfer transfer;
        try
        {
            lock (_gate)
            {
                if (_connection != null)
                {
                    return;
                }

                connection = _createConnection();
                _transfer = transfer = new RoomTransfer(connection);
                RegisterEventHandlers(connection, transfer);
                _connection = connection;
                _pendingJoin = pending;
            }
        }
        catch (Exception error)
        {
            JoinFailed?.Invoke(error.Message);
            return;
        }

        try
        {
            await ForSessionAsync(connection, () => JoiningChanged?.Invoke());
            await connection.StartAsync().ConfigureAwait(false);
            var needsSeed = await connection.InvokeAsync<bool>("NeedsSeed", roomId);
            SKRect? viewport = null;
            Queue<Event>? original = null;
            string? token = null;
            await ForSessionAsync(connection, () =>
            {
                viewport = CaptureViewport?.Invoke();
                _tokens.TryGetValue(roomId, out token);
                if (needsSeed)
                {
                    original = CaptureSeed?.Invoke() ?? new Queue<Event>();
                }
            });
            string? seed = null;
            if (original != null)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(await transfer.EncodeAsync(original, true));
                if (bytes.Length > RoomProtocol.SnapshotBytes)
                {
                    throw new InvalidOperationException("Room history exceeds 8 MiB. Use a smaller board.");
                }

                if (bytes.Length > RoomProtocol.ChunkBytes)
                {
                    await transfer.UploadAsync("seed", bytes);
                }
                else
                {
                    seed = Encoding.UTF8.GetString(bytes);
                }
            }

            var admission = await connection.InvokeAsync<RoomAdmission>("JoinRoom", roomId, displayName, token, seed,
                RoomProtocol.Version);
            // Keep the credential even if the participant cancels after admission.
            await ForSessionAsync(connection, () => _tokens[roomId] = admission.Token);
            long received = 0;
            var total = admission.BoardBytes + (long)admission.HistoryBytes + admission.ManifestBytes +
                        admission.AssetBytes;
            var snapshot = await transfer.DownloadAsync(admission, viewport,
                count => ForSessionAsync(connection, () => DownloadProgress?.Invoke(received += count, total)),
                board => ForSessionAsync(connection, () => BoardPreview?.Invoke(board)));

            await DrainAndCommitAsync(connection, pending, transfer, admission.Sequence, buffered =>
            {
                var id = connection.ConnectionId ?? throw new InvalidOperationException("Room connection closed.");
                var newIdentity = token != null && token != admission.Token;
                _room = new MultiUserDrawingRoom(roomId, id, displayName, admission.ParticipantId)
                    { Clients = pending.Roster };
                SeededRoom = admission.Seeded;
                ConnectionStarted?.Invoke();
                BoardPreview?.Invoke(null);
                RoomCanvasReady?.Invoke(snapshot, buffered);
                _pendingJoin = null;
                RoomChanged?.Invoke(_room);
                JoiningChanged?.Invoke();
                if (newIdentity) IdentityRenewed?.Invoke();
            });
        }
        catch (Exception e)
        {
            await ForSessionAsync(connection, () => JoinFailed?.Invoke(e.Message));
            await EndSessionAsync(connection);
        }
    }

    /// <summary>Drains edits received after the snapshot and atomically switches to live delivery.</summary>
    private async Task DrainAndCommitAsync(HubConnection connection, PendingJoin pending, RoomTransfer transfer,
        long sequence, Action<List<Event>> commit)
    {
        var buffered = new List<Event>();
        while (true)
        {
            List<Task<Event>>? batch = null;
            await ForSessionAsync(connection, () =>
            {
                if (pending.Incoming.Count == 0)
                {
                    commit(buffered.OrderBy(edit => edit.RoomSequence).ToList());
                }
                else
                {
                    batch = pending.Incoming;
                    pending.Incoming = [];
                }
            });
            if (batch == null)
            {
                return;
            }

            foreach (var incoming in batch)
            {
                var edit = await incoming;
                if (edit.RoomSequence > sequence)
                {
                    buffered.Add(await transfer.DecodeAsync(edit));
                }
            }
        }
    }

    public async Task LeaveRoomAsync(string displayName)
    {
        HubConnection connection;
        MultiUserDrawingRoom? joinedRoom;
        Task queuedEdits;
        lock (_gate)
        {
            if (_connection is null || _leaving) return;
            _leaving = true;
            connection = _connection;
            // Drain edits queued before Leave. Closing a pending join simply cancels admission.
            queuedEdits = _lastSend;
            joinedRoom = IsJoining ? null : _room;
        }

        try
        {
            if (joinedRoom != null && connection.State == HubConnectionState.Connected)
            {
                await queuedEdits.WaitAsync(TimeSpan.FromSeconds(5));
                await connection.InvokeAsync("LeaveRoom", joinedRoom.RoomId, displayName);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to leave room - {e.Message}");
        }
        finally
        {
            await EndSessionAsync(connection);
        }
    }

    public Task BroadcastEventAsync(Event evt)
    {
        lock (_gate)
        {
            if (_connection is not { } connection || _room is not { } room || IsJoining || _leaving)
                return Task.CompletedTask;
            _lastSend = SendInOrderAsync(_lastSend, connection, _transfer!, room.RoomId, evt);
            return _lastSend;
        }
    }

    /// <summary>
    /// Serialize drawing sends and retire queued edits with the connection that owns them.
    /// </summary>
    private async Task SendInOrderAsync(Task previous, HubConnection connection, RoomTransfer transfer, string roomId,
        Event edit)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            lock (_gate)
            {
                if (_connection != connection || _deliveryFailed)
                {
                    return;
                }
            }

            var confirmed = await transfer.SendAsync(roomId, edit).ConfigureAwait(false);
            if (confirmed != null)
                await ForSessionAsync(connection, () =>
                    EventReceived?.Invoke(edit with
                    {
                        RoomSequence = confirmed, CreatorConnectionId = _room!.Me.ParticipantId
                    }));
        }
        catch (Exception exception)
        {
            Trace.TraceError("Room drawing send failed. {0}", exception);
            await ForSessionAsync(connection, () =>
            {
                _deliveryFailed = true;
                DrawingSyncFailed?.Invoke();
            });
        }
    }

    /// <summary>
    /// Hold incoming room data until joining succeeds or the transport closes.
    /// </summary>
    private sealed class PendingJoin
    {
        public List<Task<Event>> Incoming { get; set; } = [];
        public List<MultiUserDrawingClient> Roster { get; set; } = [];
    }

    public Task BroadcastMessageAsync(MessageDto message)
    {
        var connection = _connection;
        var room = Room;
        return room != null && connection?.ConnectionId == room.Me.ConnectionId
            ? connection.InvokeAsync("SendMessage", room.RoomId, message)
            : Task.CompletedTask;
    }
}