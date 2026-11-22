using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Scribble.Extensions;
using Scribble.Services.AutoSaveService;
using Scribble.ViewModels;
using Scribble.Views;

namespace Scribble;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Register all services needed by the application
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddCommonServices();

        var provider = serviceCollection.BuildServiceProvider();
        Services = provider;
        var viewModel = Services.GetRequiredService<MainViewModel>();
        var autoSave = Services.GetRequiredService<AutoSaveService>();
        Control? surface = null;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow
            {
                IsEnabled = false,
                DataContext = viewModel
            };
            desktop.MainWindow = window;
            // The provider owns autosave and the room model and releases both on exit.
            desktop.Exit += (_, _) => provider.Dispose();
            surface = window;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var view = new MainView
            {
                IsEnabled = false,
                DataContext = viewModel,
            };
            singleViewPlatform.MainView = view;
            surface = view;
        }

        base.OnFrameworkInitializationCompleted();
        if (surface is not null) Dispatcher.UIThread.Post(() => _ = RestoreCanvasAsync(autoSave, surface));
    }

    /// <summary>
    /// Restore the previous canvas before enabling input and focusing it.
    /// </summary>
    private static async Task RestoreCanvasAsync(AutoSaveService autoSave, Control surface)
    {
        await autoSave.RestoreAsync();
        // Allow edits after recovery has finished.
        surface.IsEnabled = true;
        if (surface is ContentControl { Content: MainView windowView }) windowView.FocusCanvas();
        if (surface is MainView view) view.FocusCanvas();
    }
}