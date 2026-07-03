using Scribble.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = null;
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

public partial class Program { }