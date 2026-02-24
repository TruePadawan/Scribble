using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scribble.Lib.CollaborativeDrawing;
using Scribble.Services.DialogService;
using Scribble.Services.FileService;
using Scribble.ViewModels;

namespace Scribble.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json");
#if DEBUG
        builder.AddJsonFile("appsettings.Development.json", optional: true);
#endif
        var config = builder.Build();

        // Register IConfiguration as a singleton
        collection.AddSingleton<IConfiguration>(config);

        var serverUrl = config["ServerUrl"] ?? throw new Exception("ServerUrl is missing");
        collection.AddSingleton(new CollaborativeDrawingService(serverUrl));

        collection.AddTransient<MultiUserDrawingViewModel>();
        collection.AddTransient<MainViewModel>();

        collection.AddSingleton<IFileService, AvaloniaFileService>();

        collection.AddSingleton<IDialogService, AvaloniaDialogService>();
    }
}