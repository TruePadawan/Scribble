using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Scribble.IntegrationTests.Hub;

public class HubServerFixture : WebApplicationFactory<Program>
{
    public HubConnection CreateHubConnection()
    {
        var handler = Server.CreateHandler();
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/drawingHub", opts =>
            {
                opts.HttpMessageHandlerFactory = _ => handler;
            })
            .Build();
    }
}
