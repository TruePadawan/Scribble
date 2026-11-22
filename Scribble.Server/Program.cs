using Scribble.Server.Hubs;
using Scribble.Shared.Lib;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RoomRegistry>();

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = RoomProtocol.EnvelopeBytes;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(origin => true)
            .AllowCredentials();
    });
});

var app = builder.Build();
app.UseCors();

app.MapHub<MultiUserDrawingHub>("/drawingHub");
app.MapGet("/health", () => Results.Ok("Server is awake"));
app.Run();

public partial class Program
{
}