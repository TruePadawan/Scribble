using System;
using System.IO;
using Avalonia.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scribble.Services.AutoSaveService;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DialogService;
using Scribble.Services.DocumentService;
using Scribble.Services.FileService;
using Scribble.Services.MultiUserDrawing;
using Scribble.ViewModels;

namespace Scribble.Extensions;

/// <summary>
/// Extension methods for registering services in the application
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection collection)
    {
        /// <summary>Registers desktop services using settings beside the executable.</summary>
        public void AddCommonServices() => collection.AddCommonServices(AppContext.BaseDirectory);

        /// <summary>Registers the editor and its optional room connection for an installation.</summary>
        internal void AddCommonServices(string installDirectory)
        {
            collection.AddSingleton<IMultiUserDrawingService>(_ =>
            {
                // A service's reconnect tokens belong to the endpoint used by its first connection.
                Uri? endpoint = null;
                return new MultiUserDrawingService(() =>
                {
                    endpoint ??= ReadRoomEndpoint(installDirectory);
                    return new HubConnectionBuilder().WithUrl(endpoint).Build();
                }, Dispatcher.UIThread);
            });
            collection.AddSingleton<ICanvasStateService, CanvasStateService>();
            collection.AddSingleton<IDocumentService, DocumentService>();
            // One chat history follows the application-wide room connection across editor views.
            collection.AddSingleton<MultiUserDrawingViewModel>();

            collection.AddTransient<CanvasExportViewModel>();
            collection.AddTransient<UiStateViewModel>();
            collection.AddTransient<DocumentViewModel>();
            collection.AddTransient<MainViewModel>();

            collection.AddSingleton<IFileService, AvaloniaFileService>();
            collection.AddSingleton<IDialogService, AvaloniaDialogService>();
            collection.AddSingleton<AutoSaveService>();
        }
    }

    /// <summary>Reads the installed desktop settings when a drawing connection is requested.</summary>
    internal static Uri ReadRoomEndpoint(string installDirectory)
    {
        var builder = new ConfigurationBuilder().SetBasePath(installDirectory)
            .AddJsonFile("appsettings.json", optional: true);
#if DEBUG
        builder.AddJsonFile("appsettings.Development.json", optional: true);
#endif
        try
        {
            var config = builder.Build();
            using var lifetime = config as IDisposable;
            var url = config["ServerUrl"];
            if (Uri.TryCreate(url, UriKind.Absolute, out var endpoint) &&
                (endpoint.Scheme == Uri.UriSchemeHttps || endpoint.Scheme == Uri.UriSchemeHttp) &&
                endpoint.Host.Length > 0)
            {
                return endpoint;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                          or FormatException)
        {
            throw new InvalidOperationException(
                "Could not read the drawing server settings. Check appsettings.json in the Scribble install directory.",
                error);
        }

        throw new InvalidOperationException(
            "Set ServerUrl to a full HTTP or HTTPS drawing hub address in the installed appsettings.json.");
    }
}