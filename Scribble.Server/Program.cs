using Scribble.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = null;
});

var app = builder.Build();

app.UseHttpsRedirection();

app.MapHub<CollaborativeDrawingHub>("/drawingHub");
app.Run();