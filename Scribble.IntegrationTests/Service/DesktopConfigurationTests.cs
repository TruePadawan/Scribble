using System.Text.Json;
using Avalonia.Headless.XUnit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Scribble.Extensions;
using Scribble.Server.Hubs;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DocumentService;
using Scribble.Services.MultiUserDrawing;
using SkiaSharp;

namespace Scribble.IntegrationTests.Service;

/// <summary>Exercises desktop registration with an isolated installation directory.</summary>
public class DesktopConfigurationTests
{
    /// <summary>Unavailable collaboration settings leave a local document editable after a failed join.</summary>
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("{\"ServerUrl\":\"relative/path\"}")]
    public async Task JoinRoom_InvalidConfiguration_AllowsOfflineRoundTrip(string? settings)
    {
        var install = Directory.CreateTempSubdirectory();
        try
        {
            if (settings != null)
            {
                await File.WriteAllTextAsync(Path.Combine(install.FullName, "appsettings.json"), settings);
            }

            var services = new ServiceCollection();
            services.AddCommonServices(install.FullName);

            await using var provider = services.BuildServiceProvider();
            var canvas = provider.GetRequiredService<ICanvasStateService>();
            var documents = provider.GetRequiredService<IDocumentService>();
            var rooms = provider.GetRequiredService<IMultiUserDrawingService>();

            string? failure = null;
            rooms.JoinFailed += message => failure = message;

            await rooms.JoinRoomAsync("test", "Alice");

            Assert.Contains("appsettings.json", failure);
            Assert.False(canvas.IsRoomActive);

            canvas.SetBackgroundColor(SKColors.Green);

            using var stream = new MemoryStream();
            await documents.SaveAsync(stream);
            canvas.SetBackgroundColor(SKColors.Blue);
            stream.Position = 0;

            await documents.LoadAsync(stream);
            Assert.Equal(SKColors.Green, canvas.BackgroundColor);
        }
        finally
        {
            install.Delete(true);
        }
    }

    /// <summary>Joins a listening SignalR server using only the configuration beside the application.</summary>
    [AvaloniaFact]
    public async Task JoinRoom_InstalledSettings_ConnectsToConfiguredServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<RoomRegistry>();

        await using var server = builder.Build();
        server.MapHub<MultiUserDrawingHub>("/drawingHub");
        server.Urls.Add("http://127.0.0.1:0");

        await server.StartAsync(TestContext.Current.CancellationToken);
        var install = Directory.CreateTempSubdirectory();
        try
        {
            var url = server.Urls.Single() + "/drawingHub";
            await File.WriteAllTextAsync(Path.Combine(install.FullName, "appsettings.json"),
                JsonSerializer.Serialize(new { ServerUrl = url }));
            var services = new ServiceCollection();
            services.AddCommonServices(install.FullName);

            await using var provider = services.BuildServiceProvider();
            var rooms = provider.GetRequiredService<IMultiUserDrawingService>();
            try
            {
                await rooms.JoinRoomAsync("local", "Alice");
                Assert.Equal("local", rooms.Room?.RoomId);
            }
            finally
            {
                await rooms.LeaveRoomAsync("Alice");
            }
        }
        finally
        {
            install.Delete(true);
        }
    }

    /// <summary>Loads the room endpoint from the installation while the runner uses another directory.</summary>
    [Fact]
    public void ReadRoomEndpoint_InstalledSettings_ReturnsConfiguredHub()
    {
        var install = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(install.FullName, "appsettings.json"),
                "{\"ServerUrl\":\"https://example.test/drawingHub\"}");
            Assert.Equal("https://example.test/drawingHub",
                ServiceCollectionExtensions.ReadRoomEndpoint(install.FullName).AbsoluteUri);
        }
        finally
        {
            install.Delete(true);
        }
    }
}