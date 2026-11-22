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
        builder.ConfigureServices(services => services.AddSignalR(options => options.AddFilter(Faults)));
    }

    public HubConnection CreateHubConnection()
    {
        var handler = Server.CreateHandler();
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/drawingHub", opts => { opts.HttpMessageHandlerFactory = _ => handler; })
            .Build();
    }

    // Interrupt real server connections without exposing a production hub method.
    public sealed class ConnectionFaults : IHubFilter
    {
        private readonly ConcurrentDictionary<string, HubCallerContext> _clients = new();
        private readonly ConcurrentDictionary<string, bool> _abortOnLeave = new();

        private readonly ConcurrentDictionary<string, TaskCompletionSource> _heldSends = new();

        // Signal when the next drawing send reaches the hub and hold it until disconnect.
        public Task HoldNextSend(string id)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _heldSends[id] = started;
            return started.Task;
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
            if (context.HubMethodName == "LeaveRoom" && _abortOnLeave.TryRemove(context.Context.ConnectionId, out _))
            {
                context.Context.Abort();
            }

            if (context.HubMethodName == "SendEvent" &&
                _heldSends.TryRemove(context.Context.ConnectionId, out var held))
            {
                held.SetResult();
                await Task.Delay(Timeout.Infinite, context.Context.ConnectionAborted);
            }

            return await next(context);
        }
    }
}