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

    // Tests can advance credential expiry without waiting for the production ten-minute window.
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

    // Exercise concurrent joins with a repeatable 40 ms round trip and 10 Mbit/s downstream.
    public HubConnection CreateDelayedConnection() => new HubConnectionBuilder()
        .WithUrl("http://localhost/drawingHub", options =>
        {
            options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => new DelayedResponse(Server.CreateHandler());
        }).Build();

    // Delay delivery of the buffered HTTP response in proportion to its actual payload.
    private sealed class DelayedResponse(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken token)
        {
            await Task.Delay(40, token);
            var response = await base.SendAsync(request, token);
            if (request.Method == HttpMethod.Get)
            {
                var body = await response.Content.ReadAsByteArrayAsync(token);
                await Task.Delay(TimeSpan.FromSeconds(body.Length / 1_250_000d), token);
            }

            return response;
        }
    }

    // Interrupt real server connections without exposing a production hub method.
    public sealed class ConnectionFaults : IHubFilter
    {
        private readonly ConcurrentDictionary<string, HubCallerContext> _clients = new();
        private readonly ConcurrentDictionary<string, bool> _abortOnLeave = new();

        private readonly ConcurrentDictionary<string, (TaskCompletionSource Started, Task? Release)> _heldSends = new();

        // Signal when the next drawing send reaches the hub and hold it until disconnect.
        public Task HoldNextSend(string id, Task? release = null)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _heldSends[id] = (started, release);
            return started.Task;
        }

        private TaskCompletionSource<string>? _pendingJoin;

        // Hold admission until the joining connection is closed.
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