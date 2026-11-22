using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Scribble.IntegrationTests.Hub;

public class HubServerFixture : WebApplicationFactory<Program>
{
    public ConnectionFaults Faults { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSignalR(options => options.AddFilter(Faults));
        });
    }

    /// <summary>
    /// Tests can advance credential expiry without waiting for the production ten-minute window.
    /// </summary>
    public sealed class RoomClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }

    public RoomClock Clock { get; } = new();

    public HubConnection CreateHubConnection()
    {
        var handler = Server.CreateHandler();
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/drawingHub", opts => { opts.HttpMessageHandlerFactory = _ => handler; })
            .Build();
    }

    /// <summary>
    /// Interrupt real server connections without exposing a production hub method.
    /// </summary>
    public sealed class ConnectionFaults : IHubFilter
    {
        private readonly ConcurrentDictionary<string, HubCallerContext> _clients = new();
        private readonly ConcurrentDictionary<string, bool> _abortOnLeave = new();

        private readonly ConcurrentDictionary<string, (TaskCompletionSource Started, Task? Release)> _heldSends = new();

        /// <summary>
        /// Signal when the next drawing send reaches the hub and hold it until disconnect.
        /// </summary>
        public Task HoldNextSend(string id, Task? release = null)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _heldSends[id] = (started, release);
            return started.Task;
        }

        private TaskCompletionSource<string>? _pendingJoin;
        private (TaskCompletionSource Started, Task Release)? _imageRead;
        private readonly ConcurrentDictionary<string, int> _imageDownloads = new();

        /// <summary>Pauses the next image download after its first chunk has arrived.</summary>
        public Task HoldImageRead(Task release)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _imageRead = (started, release);
            return started.Task;
        }

        /// <summary>Counts image downloads started on this transport.</summary>
        public int Downloads(string connection) => _imageDownloads.GetValueOrDefault(connection);

        /// <summary>
        /// Hold admission until the joining connection is closed.
        /// </summary>
        public Task<string> HoldNextJoin()
        {
            _pendingJoin = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pendingJoin.Task;
        }

        public void Abort(string id) => _clients[id].Abort();
        public void AbortOnLeave(string id) => _abortOnLeave[id] = true;

        public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            _clients[context.Context.ConnectionId] = context.Context;
            return next(context);
        }

        public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception,
            Func<HubLifetimeContext, Exception?, Task> next)
        {
            _clients.TryRemove(context.Context.ConnectionId, out _);
            _abortOnLeave.TryRemove(context.Context.ConnectionId, out _);
            return next(context, exception);
        }

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
            Func<HubInvocationContext, ValueTask<object?>> next)
        {
            if (context.HubMethodName == "ReadChunk" && context.HubMethodArguments[0] is string key &&
                key.StartsWith("sha256:", StringComparison.Ordinal))
            {
                if (context.HubMethodArguments[1] is 0)
                {
                    _imageDownloads.AddOrUpdate(context.Context.ConnectionId, 1, (_, count) => count + 1);
                }
                else if (_imageRead is { } read)
                {
                    _imageRead = null;
                    read.Started.SetResult();
                    await read.Release.WaitAsync(context.Context.ConnectionAborted);
                }
            }

            if (context.HubMethodName == "JoinRoom" && Interlocked.Exchange(ref _pendingJoin, null) is { } pending)
            {
                pending.SetResult(context.Context.ConnectionId);
                await Task.Delay(Timeout.Infinite, context.Context.ConnectionAborted);
            }

            if (context.HubMethodName == "LeaveRoom" && _abortOnLeave.TryRemove(context.Context.ConnectionId, out _))
            {
                context.Context.Abort();
            }

            if (context.HubMethodName == "SendEvent" &&
                _heldSends.TryRemove(context.Context.ConnectionId, out var held))
            {
                held.Started.SetResult();
                var release = held.Release ?? Task.Delay(Timeout.Infinite, context.Context.ConnectionAborted);
                await release.WaitAsync(context.Context.ConnectionAborted);
            }

            return await next(context);
        }
    }
}